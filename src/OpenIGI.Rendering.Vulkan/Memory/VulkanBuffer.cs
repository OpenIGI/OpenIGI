using System.Runtime.CompilerServices;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Memory;

/// <summary>A Vulkan buffer and the device memory backing it.</summary>
/// <remarks>
/// <para>
/// A buffer is created, its memory requirements read, memory of a suitable type allocated, and the two
/// bound together — the four steps every Vulkan buffer needs, kept in one place so a caller names only
/// the size, what the buffer is for, and whether the CPU must be able to write it.
/// </para>
/// <para>
/// A host-visible buffer can be written straight from the CPU and is what staging uploads and
/// per-frame uniform data use; a device-local buffer lives in the memory the GPU reads fastest and is
/// filled by copying from a host-visible staging buffer. This type covers both — the difference is only
/// which memory properties it was asked for — and mapping is available exactly when the memory is host
/// visible.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBuffer : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VulkanAllocator _allocator;
    private readonly DeviceMemory _memory;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanBuffer"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to create the buffer on.</param>
    /// <param name="allocator">The allocator backing the buffer's memory.</param>
    /// <param name="sizeInBytes">The buffer's size in bytes. Must be positive.</param>
    /// <param name="usage">What the buffer is for.</param>
    /// <param name="memoryProperties">The memory properties the buffer needs.</param>
    /// <exception cref="GraphicsDeviceException">The buffer could not be created or backed.</exception>
    public VulkanBuffer(
        Vk vk,
        VulkanDevice device,
        VulkanAllocator allocator,
        ulong sizeInBytes,
        BufferUsageFlags usage,
        MemoryPropertyFlags memoryProperties)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        if (sizeInBytes == 0)
        {
            throw new GraphicsDeviceException("A buffer must have a positive size.");
        }

        _vk = vk;
        _device = device.Device;
        _allocator = allocator;
        Size = sizeInBytes;
        HostVisible = memoryProperties.HasFlag(MemoryPropertyFlags.HostVisibleBit);

        BufferCreateInfo createInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = sizeInBytes,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };

        Buffer buffer;
        VkCheck.Ok(_vk.CreateBuffer(_device, &createInfo, null, &buffer), "vkCreateBuffer");
        Handle = buffer;

        _vk.GetBufferMemoryRequirements(_device, buffer, out MemoryRequirements requirements);
        _memory = _allocator.Allocate(requirements, memoryProperties);
        VkCheck.Ok(_vk.BindBufferMemory(_device, buffer, _memory, 0), "vkBindBufferMemory");
    }

    /// <summary>Gets the buffer handle.</summary>
    public Buffer Handle { get; }

    /// <summary>Gets the buffer's size in bytes.</summary>
    public ulong Size { get; }

    /// <summary>Gets a value indicating whether the CPU can map and write the buffer's memory.</summary>
    public bool HostVisible { get; }

    /// <summary>Copies a span of blittable values into a host-visible buffer, from its start.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="data">The data to write.</param>
    /// <exception cref="GraphicsDeviceException">The buffer is not host visible, or the data overflows it.</exception>
    public void Write<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (!HostVisible)
        {
            throw new GraphicsDeviceException("A device-local buffer cannot be written directly; stage into it.");
        }

        ulong bytes = (ulong)(data.Length * Unsafe.SizeOf<T>());
        if (bytes > Size)
        {
            throw new GraphicsDeviceException($"Writing {bytes} bytes overflows a {Size}-byte buffer.");
        }

        void* mapped = null;
        VkCheck.Ok(_vk.MapMemory(_device, _memory, 0, bytes, 0, ref mapped), "vkMapMemory");
        try
        {
            Span<T> destination = new(mapped, data.Length);
            data.CopyTo(destination);
        }
        finally
        {
            _vk.UnmapMemory(_device, _memory);
        }
    }

    /// <summary>Copies a span into a host-visible buffer at a byte offset.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="byteOffset">Where in the buffer to start writing.</param>
    /// <param name="data">The data to write.</param>
    /// <exception cref="GraphicsDeviceException">The buffer is not host visible, or the write overflows it.</exception>
    public void WriteAt<T>(ulong byteOffset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        if (!HostVisible)
        {
            throw new GraphicsDeviceException("A device-local buffer cannot be written directly; stage into it.");
        }

        if (data.IsEmpty)
        {
            return;
        }

        ulong bytes = (ulong)(data.Length * Unsafe.SizeOf<T>());
        if (byteOffset + bytes > Size)
        {
            throw new GraphicsDeviceException($"Writing {bytes} bytes at {byteOffset} overflows a {Size}-byte buffer.");
        }

        void* mapped = null;
        VkCheck.Ok(_vk.MapMemory(_device, _memory, byteOffset, bytes, 0, ref mapped), "vkMapMemory");
        try
        {
            data.CopyTo(new Span<T>(mapped, data.Length));
        }
        finally
        {
            _vk.UnmapMemory(_device, _memory);
        }
    }

    /// <summary>Copies the start of a host-visible buffer's memory out into a span.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="destination">Receives the data, from the start of the buffer.</param>
    /// <exception cref="GraphicsDeviceException">The buffer is not host visible.</exception>
    public void Read<T>(Span<T> destination)
        where T : unmanaged
    {
        if (!HostVisible)
        {
            throw new GraphicsDeviceException("A device-local buffer cannot be read directly.");
        }

        ulong bytes = (ulong)(destination.Length * Unsafe.SizeOf<T>());
        void* mapped = null;
        VkCheck.Ok(_vk.MapMemory(_device, _memory, 0, bytes, 0, ref mapped), "vkMapMemory");
        try
        {
            new ReadOnlySpan<T>(mapped, destination.Length).CopyTo(destination);
        }
        finally
        {
            _vk.UnmapMemory(_device, _memory);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyBuffer(_device, Handle, null);
        _allocator.Free(_memory);
    }
}
