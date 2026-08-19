using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Memory;

/// <summary>
/// Chooses which of the adapter's memory types an allocation lives in, and makes the allocation.
/// </summary>
/// <remarks>
/// <para>
/// Vulkan hands memory management to the application: every buffer and image is backed by memory the
/// caller allocates and binds itself, and the caller alone knows whether a resource wants to live where
/// the GPU reaches it fastest (<see cref="MemoryPropertyFlags.DeviceLocalBit"/>) or where the CPU can
/// write it (<see cref="MemoryPropertyFlags.HostVisibleBit"/>). This concentrates the type search and
/// the allocation so every resource asks the same way.
/// </para>
/// <para>
/// One <see cref="DeviceMemory"/> is allocated per resource here. That is the simple, correct starting
/// point; a driver caps how many allocations may exist at once, so a later pass sub-allocates many
/// resources out of a few large blocks. Keeping every allocation behind this one seam is what lets that
/// pass be a change here rather than everywhere.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanAllocator
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;

    /// <summary>Initialises a new instance of the <see cref="VulkanAllocator"/> class.</summary>
    /// <param name="device">The device to allocate on.</param>
    public VulkanAllocator(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _vk = Vk.GetApi();
        _device = device.Device;
        _memoryProperties = device.MemoryProperties;
    }

    /// <summary>Allocates device memory of a type satisfying a resource's requirements.</summary>
    /// <param name="requirements">The requirements Vulkan reported for the buffer or image.</param>
    /// <param name="properties">The memory properties the resource needs — device-local, host-visible, and so on.</param>
    /// <returns>The allocated memory. The caller owns it and must free it with <see cref="Free"/>.</returns>
    /// <exception cref="GraphicsDeviceException">No memory type satisfies the requirements.</exception>
    public DeviceMemory Allocate(in MemoryRequirements requirements, MemoryPropertyFlags properties)
    {
        uint typeIndex = FindMemoryType(requirements.MemoryTypeBits, properties);
        MemoryAllocateInfo info = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = typeIndex,
        };

        DeviceMemory memory;
        VkCheck.Ok(_vk.AllocateMemory(_device, &info, null, &memory), "vkAllocateMemory");
        return memory;
    }

    /// <summary>Frees an allocation made by <see cref="Allocate"/>.</summary>
    /// <param name="memory">The memory to free. A null handle is ignored.</param>
    public void Free(DeviceMemory memory)
    {
        if (memory.Handle != 0)
        {
            _vk.FreeMemory(_device, memory, null);
        }
    }

    /// <summary>Finds a memory type index satisfying both a resource's type mask and property flags.</summary>
    /// <param name="typeBits">The bitmask of acceptable memory types from the resource's requirements.</param>
    /// <param name="properties">The properties the memory must have.</param>
    /// <returns>The index of a suitable memory type.</returns>
    /// <exception cref="GraphicsDeviceException">No memory type is both allowed and suitable.</exception>
    public uint FindMemoryType(uint typeBits, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memory = _memoryProperties;
        for (uint i = 0; i < memory.MemoryTypeCount; i++)
        {
            bool allowed = (typeBits & (1u << (int)i)) != 0;
            bool suitable = (memory.MemoryTypes[(int)i].PropertyFlags & properties) == properties;
            if (allowed && suitable)
            {
                return i;
            }
        }

        throw new GraphicsDeviceException(
            $"No memory type satisfies requirements 0x{typeBits:X} with properties {properties}.");
    }
}
