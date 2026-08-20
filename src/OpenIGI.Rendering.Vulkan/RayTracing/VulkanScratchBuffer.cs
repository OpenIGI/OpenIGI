using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The working memory acceleration-structure builds need, handed out a range at a time.
/// </summary>
/// <remarks>
/// <para>
/// A build wants somewhere to sort and partition while it runs, and it wants that memory only for the
/// duration of the build. Giving each structure its own allocation would mean an allocation per mesh
/// held for a frame; one buffer that a batch of builds carves up between them is a single allocation and
/// no bookkeeping, since every build in the batch finishes before the batch's barrier.
/// </para>
/// <para>
/// It grows and never shrinks. The largest batch the scene ever builds is the size it settles at, which
/// happens in the first frames and then stops.
/// </para>
/// </remarks>
internal sealed class VulkanScratchBuffer : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly ulong _alignment;

    private VulkanBuffer? _buffer;
    private ulong _offset;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanScratchBuffer"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to allocate on.</param>
    /// <param name="allocator">The allocator backing the buffer.</param>
    /// <param name="alignment">The alignment each handed-out range must start at.</param>
    public VulkanScratchBuffer(Vk vk, VulkanDevice device, VulkanAllocator allocator, ulong alignment)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _alignment = Math.Max(1, alignment);
    }

    /// <summary>Gets how many bytes the buffer currently holds.</summary>
    public ulong Capacity => _buffer?.Size ?? 0;

    /// <summary>
    /// Makes sure the buffer can serve a batch of the given total size, reallocating if it cannot, and
    /// starts handing ranges out from the beginning again.
    /// </summary>
    /// <param name="requiredBytes">The total the batch will ask for, alignment padding included.</param>
    /// <returns>
    /// The buffer that was replaced when the reallocation grew it, for the caller to retire once the GPU
    /// has finished with it; <see langword="null"/> when nothing was replaced.
    /// </returns>
    /// <remarks>
    /// The old buffer is returned rather than freed because a previous frame's builds may still be
    /// reading it. It is the caller — which knows what the GPU has reached — that can safely retire it.
    /// </remarks>
    public VulkanBuffer? BeginBatch(ulong requiredBytes)
    {
        _offset = 0;

        if (_buffer is not null && _buffer.Size >= requiredBytes)
        {
            return null;
        }

        VulkanBuffer? replaced = _buffer;

        // Grow generously rather than exactly, so a batch that creeps up by one mesh at a time does not
        // reallocate every frame.
        ulong size = Math.Max(requiredBytes, Math.Max(64 * 1024, (_buffer?.Size ?? 0) * 2));
        _buffer = new VulkanBuffer(
            _vk,
            _device,
            _allocator,
            VulkanRayTracingApi.Align(size, _alignment),
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit,
            MemoryPropertyFlags.DeviceLocalBit);

        return replaced;
    }

    /// <summary>Hands out an aligned range and advances past it.</summary>
    /// <param name="bytes">How many bytes the build needs.</param>
    /// <returns>The device address of the range.</returns>
    /// <exception cref="InvalidOperationException">
    /// The batch asked for more than <see cref="BeginBatch"/> was told it would.
    /// </exception>
    public ulong Allocate(ulong bytes)
    {
        if (_buffer is null)
        {
            throw new InvalidOperationException("BeginBatch must be called before a range is allocated.");
        }

        ulong start = VulkanRayTracingApi.Align(_offset, _alignment);
        if (start + bytes > _buffer.Size)
        {
            throw new InvalidOperationException(
                $"A build asked for {bytes} scratch bytes at {start}, past the {_buffer.Size}-byte buffer.");
        }

        _offset = start + bytes;
        return _buffer.DeviceAddress + start;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _buffer?.Dispose();
        _buffer = null;
    }
}
