using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>An acceleration structure and the buffer its nodes live in.</summary>
/// <remarks>
/// <para>
/// Vulkan splits an acceleration structure in two: a buffer that holds the actual node data, which the
/// application allocates and owns, and a handle that describes a region of that buffer as a structure.
/// The two are created separately and must be destroyed in the right order, so they are kept together
/// here and neither is ever seen without the other.
/// </para>
/// <para>
/// A structure is reached by device address rather than through a descriptor when it is a build input —
/// a bottom-level structure named by a top-level instance is a raw pointer — so the address is taken at
/// creation and cached.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanAccelerationStructure : IDisposable
{
    private readonly VulkanRayTracingApi _api;
    private readonly Device _device;
    private readonly VulkanBuffer _buffer;
    private bool _disposed;

    private VulkanAccelerationStructure(
        VulkanRayTracingApi api,
        Device device,
        VulkanBuffer buffer,
        AccelerationStructureKHR handle,
        AccelerationStructureTypeKHR type,
        ulong size)
    {
        _api = api;
        _device = device;
        _buffer = buffer;
        Handle = handle;
        Type = type;
        Size = size;

        AccelerationStructureDeviceAddressInfoKHR info = new()
        {
            SType = StructureType.AccelerationStructureDeviceAddressInfoKhr,
            AccelerationStructure = handle,
        };
        DeviceAddress = api.Structures.GetAccelerationStructureDeviceAddress(device, &info);
    }

    /// <summary>Gets the structure handle.</summary>
    public AccelerationStructureKHR Handle { get; }

    /// <summary>Gets whether this is a bottom-level or top-level structure.</summary>
    public AccelerationStructureTypeKHR Type { get; }

    /// <summary>Gets the size in bytes of the structure's node data.</summary>
    public ulong Size { get; }

    /// <summary>Gets the structure's device address, for naming it as a build input.</summary>
    public ulong DeviceAddress { get; }

    /// <summary>Gets the buffer the structure's nodes live in.</summary>
    public VulkanBuffer Buffer => _buffer;

    /// <summary>Allocates a buffer of the requested size and describes it as an acceleration structure.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device to create on.</param>
    /// <param name="allocator">The allocator backing the buffer.</param>
    /// <param name="type">Whether the structure is bottom-level or top-level.</param>
    /// <param name="size">How many bytes of node data to reserve.</param>
    /// <returns>The created structure.</returns>
    public static VulkanAccelerationStructure Create(
        Vk vk,
        VulkanRayTracingApi api,
        VulkanDevice device,
        VulkanAllocator allocator,
        AccelerationStructureTypeKHR type,
        ulong size)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(device);

        ulong aligned = VulkanRayTracingApi.Align(Math.Max(size, 1), VulkanRayTracingApi.StructureAlignment);
        VulkanBuffer buffer = new(
            vk,
            device,
            allocator,
            aligned,
            BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
            MemoryPropertyFlags.DeviceLocalBit);

        AccelerationStructureCreateInfoKHR createInfo = new()
        {
            SType = StructureType.AccelerationStructureCreateInfoKhr,
            Buffer = buffer.Handle,
            Offset = 0,
            Size = aligned,
            Type = type,
        };

        AccelerationStructureKHR handle;
        VkCheck.Ok(
            api.Structures.CreateAccelerationStructure(device.Device, &createInfo, null, &handle),
            "vkCreateAccelerationStructureKHR");

        return new VulkanAccelerationStructure(api, device.Device, buffer, handle, type, aligned);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The handle describes a region of the buffer, so it has to go first: destroying the buffer while
        // a structure still refers to it is undefined.
        _api.Structures.DestroyAccelerationStructure(_device, Handle, null);
        _buffer.Dispose();
    }
}
