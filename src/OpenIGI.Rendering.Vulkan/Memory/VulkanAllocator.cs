using System.Numerics;
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
/// <para>
/// A <em>freed block is retained rather than returned to the driver</em>, keyed by the shape it was
/// allocated for (memory type, byte size, whether it carries a device address), and the next allocation
/// of that same shape is handed a retained block instead of calling <c>vkAllocateMemory</c> again. A
/// fresh host-visible allocation is a blocking driver call of tens to hundreds of milliseconds — the
/// terrain streams growing pages in and out as the camera turns, and re-using a retired page's memory
/// turns that per-page stall into a dictionary lookup. Retained memory is capped so the pool cannot grow
/// without bound; over the cap a freed block is genuinely returned.
/// </para>
/// <para>
/// The size in that key is <b>rounded up</b> rather than exact, and that is what makes the pool work at
/// all for anything but the terrain. Pages are one size and hit an exact-size pool every time; a level's
/// thousand textures are a thousand different sizes and hit it never — so swapping a menu backdrop was a
/// thousand <c>vkAllocateMemory</c> calls against a thousand <c>vkFreeMemory</c> calls, measured at
/// 794 ms allocating and 868 ms freeing over a handful of swaps, all of it on the render thread.
/// Rounding to three significant binary digits (see <see cref="BucketSize"/>) puts eight buckets in
/// every power of two: an eighth wasted at worst, and a texture the outgoing level freed fits the
/// incoming one's.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanAllocator : IDisposable
{
    /// <summary>The ceiling on retained-but-unused memory, whatever the adapter's heaps say.</summary>
    private const long MaxPooledBytesCeiling = 768L * 1024 * 1024;

    /// <summary>The share of the largest heap the pool will hold, when that is the smaller number.</summary>
    private const double MaxPooledHeapShare = 0.10;

    /// <summary>Allocations at or under this are all rounded to it: below it the buckets are noise.</summary>
    private const ulong SizeGrain = 64 * 1024;

    /// <summary>How many significant binary digits a bucketed size keeps. Three is an eighth wasted.</summary>
    private const int SizeMantissaBits = 3;

    /// <summary>The most retained-but-unused memory the pool keeps before it starts genuinely freeing.</summary>
    private readonly long _maxPooledBytes;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;

    // Retained free blocks, bucketed by the exact shape an allocation asked for, so a reused block always
    // satisfies the new resource's size, type and device-address requirements without re-checking them.
    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, Stack<DeviceMemory>> _free = new();

    // The shape each live block was allocated for, so Free can bucket a block back without the caller
    // carrying its requirements. Keyed by the raw memory handle, and stable across reuse.
    private readonly Dictionary<ulong, PoolKey> _shapes = new();
    private long _pooledBytes;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanAllocator"/> class.</summary>
    /// <param name="device">The device to allocate on.</param>
    public VulkanAllocator(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _vk = Vk.GetApi();
        _device = device.Device;
        _memoryProperties = device.MemoryProperties;
        SupportsDeviceAddress = device.Capabilities.BufferDeviceAddress;
        ResizableBar = FindResizableBarType() is not null;

        // Sized off the adapter rather than fixed: a tenth of the largest heap is generous on a card
        // with ten gigabytes and still modest on one with four, and holding a level's worth of freed
        // textures is the whole point - the incoming level allocates almost exactly what the outgoing
        // one released.
        _maxPooledBytes = (long)Math.Min(
            (ulong)MaxPooledBytesCeiling, (ulong)(LargestHeapBytes() * MaxPooledHeapShare));
    }

    /// <summary>Gets a value indicating whether allocations can back buffers that carry a device address.</summary>
    public bool SupportsDeviceAddress { get; }

    /// <summary>
    /// Gets a value indicating whether the adapter exposes device-local memory the CPU can also write.
    /// </summary>
    /// <remarks>
    /// A resizable BAR: the whole of video memory mapped into the host address space rather than the
    /// traditional 256&#160;MB window. Where it exists, data the CPU produces and the GPU consumes every
    /// frame — instance descriptors, shader binding tables, build scratch, descriptor heaps — can be
    /// written straight to where the GPU reads it, with no staging buffer and no copy command.
    /// </remarks>
    public bool ResizableBar { get; }

    /// <summary>Allocates device memory of a type satisfying a resource's requirements.</summary>
    /// <param name="requirements">The requirements Vulkan reported for the buffer or image.</param>
    /// <param name="properties">The memory properties the resource needs — device-local, host-visible, and so on.</param>
    /// <param name="deviceAddressable">
    /// Whether the memory will back a buffer whose device address is taken. Vulkan requires this be
    /// declared at allocation, not only at buffer creation, so a buffer created with the address usage
    /// but backed by memory allocated without this flag is invalid.
    /// </param>
    /// <returns>The allocated memory. The caller owns it and must free it with <see cref="Free"/>.</returns>
    /// <exception cref="GraphicsDeviceException">No memory type satisfies the requirements.</exception>
    public DeviceMemory Allocate(
        in MemoryRequirements requirements,
        MemoryPropertyFlags properties,
        bool deviceAddressable = false)
    {
        uint typeIndex = FindMemoryType(requirements.MemoryTypeBits, properties);
        bool addressable = deviceAddressable && SupportsDeviceAddress;

        // The block is allocated at the bucketed size, not the requested one. Vulkan is happy to back a
        // resource with memory larger than it needs - the requirement is a floor - so a block sized for
        // its bucket serves every resource that rounds into it.
        PoolKey key = new(typeIndex, BucketSize(requirements.Size), addressable);

        // Hand back a retired block of the identical shape before asking the driver for a new one.
        lock (_gate)
        {
            if (_free.TryGetValue(key, out Stack<DeviceMemory>? bucket) && bucket.Count > 0)
            {
                DeviceMemory reused = bucket.Pop();
                _pooledBytes -= (long)key.Size;
                return reused;
            }
        }

        MemoryAllocateFlagsInfo flagsInfo = new()
        {
            SType = StructureType.MemoryAllocateFlagsInfo,
            Flags = MemoryAllocateFlags.AddressBit,
        };

        MemoryAllocateInfo info = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = addressable ? &flagsInfo : null,
            AllocationSize = key.Size,
            MemoryTypeIndex = typeIndex,
        };

        DeviceMemory memory;
        VkCheck.Ok(_vk.AllocateMemory(_device, &info, null, &memory), "vkAllocateMemory");

        lock (_gate)
        {
            _shapes[memory.Handle] = key;
        }

        return memory;
    }

    /// <summary>Frees an allocation made by <see cref="Allocate"/>.</summary>
    /// <remarks>
    /// The block is retained for re-use, keyed by the shape it was allocated for, unless the pool is over
    /// its cap — then it is returned to the driver. A caller never sees the difference: a freed block is
    /// no longer bound to anything, so whether its memory is recycled or released is the allocator's
    /// concern alone.
    /// </remarks>
    /// <param name="memory">The memory to free. A null handle is ignored.</param>
    public void Free(DeviceMemory memory)
    {
        if (memory.Handle == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_disposed
                && _shapes.TryGetValue(memory.Handle, out PoolKey key)
                && _pooledBytes + (long)key.Size <= _maxPooledBytes)
            {
                if (!_free.TryGetValue(key, out Stack<DeviceMemory>? bucket))
                {
                    bucket = new Stack<DeviceMemory>();
                    _free[key] = bucket;
                }

                bucket.Push(memory);
                _pooledBytes += (long)key.Size;
                return;
            }

            _shapes.Remove(memory.Handle);
        }

        _vk.FreeMemory(_device, memory, null);
    }

    /// <summary>Returns every retained block to the driver.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _disposed = true;
            foreach (Stack<DeviceMemory> bucket in _free.Values)
            {
                while (bucket.Count > 0)
                {
                    _vk.FreeMemory(_device, bucket.Pop(), null);
                }
            }

            _free.Clear();
            _shapes.Clear();
            _pooledBytes = 0;
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

    /// <summary>
    /// Gets the memory properties for data the CPU writes and the GPU reads every frame, preferring
    /// device-local memory the host can reach where the adapter offers it.
    /// </summary>
    /// <returns>The properties to ask <see cref="Allocate"/> for.</returns>
    public MemoryPropertyFlags UploadMemoryProperties()
    {
        MemoryPropertyFlags host = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        return ResizableBar ? host | MemoryPropertyFlags.DeviceLocalBit : host;
    }

    /// <summary>Rounds an allocation up to the size its pool bucket is allocated at.</summary>
    /// <param name="size">The size the resource's requirements asked for.</param>
    /// <returns>The bucket's size, never less than <paramref name="size"/>.</returns>
    /// <remarks>
    /// Three significant binary digits: 64k, 72k, 80k ... 128k, 144k, and so on up. Eight buckets to a
    /// power of two is coarse enough that a level's textures land on one another's blocks and fine
    /// enough that nothing wastes more than an eighth of what it asked for.
    /// </remarks>
    internal static ulong BucketSize(ulong size)
    {
        if (size <= SizeGrain)
        {
            return SizeGrain;
        }

        int highestBit = 63 - BitOperations.LeadingZeroCount(size);
        int shift = Math.Max(0, highestBit - SizeMantissaBits);
        ulong step = 1UL << shift;
        return (size + step - 1) & ~(step - 1);
    }

    private long LargestHeapBytes()
    {
        PhysicalDeviceMemoryProperties memory = _memoryProperties;
        ulong largest = 0;
        for (uint i = 0; i < memory.MemoryHeapCount; i++)
        {
            largest = Math.Max(largest, memory.MemoryHeaps[(int)i].Size);
        }

        return largest == 0 ? MaxPooledBytesCeiling : (long)largest;
    }

    private uint? FindResizableBarType()
    {
        MemoryPropertyFlags wanted = MemoryPropertyFlags.DeviceLocalBit
            | MemoryPropertyFlags.HostVisibleBit
            | MemoryPropertyFlags.HostCoherentBit;

        PhysicalDeviceMemoryProperties memory = _memoryProperties;
        for (uint i = 0; i < memory.MemoryTypeCount; i++)
        {
            if ((memory.MemoryTypes[(int)i].PropertyFlags & wanted) == wanted)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The shape a retained block was allocated for: identical shapes are interchangeable.</summary>
    /// <param name="TypeIndex">The memory type the block lives in.</param>
    /// <param name="Size">The block's byte size.</param>
    /// <param name="Addressable">Whether the block was allocated to carry a device address.</param>
    private readonly record struct PoolKey(uint TypeIndex, ulong Size, bool Addressable);
}
