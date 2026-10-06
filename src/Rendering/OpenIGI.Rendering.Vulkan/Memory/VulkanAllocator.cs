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
/// <para>
/// <b>Host-visible memory is pooled under a cap of its own, and a small one.</b> It is the memory that
/// counts against the process: a block of it parked in the pool is RAM the game holds and does not use.
/// One shared cap let a level's worth of upload staging -- every texture of a menu backdrop passes
/// through some -- fill the whole pool and stay there, while the device-local blocks that are the reason
/// the pool exists were freed for want of room. Each kind now has its own ceiling, and
/// <see cref="TrimHostVisible"/> gives the host-visible blocks back once a load is over.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanAllocator : IDisposable
{
    /// <summary>The ceiling on retained-but-unused memory, whatever the adapter's heaps say.</summary>
    private const long MaxPooledBytesCeiling = 768L * 1024 * 1024;

    /// <summary>The share of the largest heap the pool will hold, when that is the smaller number.</summary>
    private const double MaxPooledHeapShare = 0.10;

    /// <summary>The ceiling on retained host-visible memory, which is the process's own RAM.</summary>
    private const long MaxPooledHostBytes = 64L * 1024 * 1024;

    /// <summary>The largest resource carved out of a shared block rather than given a block of its own.</summary>
    private const ulong MaxSubAllocation = 256 * 1024;

    /// <summary>The smallest slot a shared block is divided into.</summary>
    private const ulong MinSlot = 1024;

    /// <summary>The size of a shared block small resources are carved out of.</summary>
    private const ulong SlabBytes = 4 * 1024 * 1024;

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
    private long _pooledDeviceBytes;
    private long _pooledHostBytes;
    private bool _disposed;

    // What is allocated from the driver right now, live or pooled, per memory type, and how often the pool
    // has answered. Read by Describe; kept under the gate with everything else.
    private readonly long[] _allocatedBytes = new long[32];
    private readonly int[] _allocatedCount = new int[32];
    private readonly long[] _pooledBytesByType = new long[32];
    private readonly int[] _pooledCountByType = new int[32];
    private long _poolHits;
    private long _poolMisses;
    private long _driverFrees;

    // Shared blocks small resources are carved out of, by shape, and how many slots are in use across
    // them. See AllocateResource.
    private readonly Dictionary<SlabKey, List<Slab>> _slabs = [];
    private long _slabSlotsUsed;
    private long _slabBytesUsed;

    /// <summary>Initialises a new instance of the <see cref="VulkanAllocator"/> class.</summary>
    /// <param name="device">The device to allocate on.</param>
    public VulkanAllocator(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _vk = device.Vk;
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
    /// <returns>The allocated memory. The caller owns it and must free it with <see cref="Free(DeviceMemory)"/>.</returns>
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
                Unpool(key);
                _poolHits++;
                return reused;
            }

            _poolMisses++;
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
            _allocatedBytes[key.TypeIndex] += (long)key.Size;
            _allocatedCount[key.TypeIndex]++;
        }

        return memory;
    }

    /// <summary>Allocates memory for one resource, carving a small device-local one out of a shared block.</summary>
    /// <param name="requirements">The requirements Vulkan reported for the resource.</param>
    /// <param name="properties">The memory properties the resource needs.</param>
    /// <param name="kind">Whether the resource is a buffer or an image.</param>
    /// <param name="deviceAddressable">Whether the memory backs a buffer whose device address is taken.</param>
    /// <returns>Where the resource is bound: the memory and the offset into it. Free with <see cref="Free(in VulkanAllocation)"/>.</returns>
    /// <remarks>
    /// <para>
    /// A block of its own per resource cost every small buffer and texture at least
    /// <see cref="SizeGrain"/> -- a mesh's few kilobytes of vertices took sixty-four -- and a level is
    /// thousands of them: on a menu soak over eight thousand driver allocations, a few hundred megabytes of
    /// them padding, and on this driver device memory counts against the process. A small device-local
    /// resource now takes a power-of-two slot of a shared block, at least its own alignment.
    /// </para>
    /// <para>
    /// Buffers and images never share a block, which is what keeps the driver's buffer-image granularity
    /// from having to be reasoned about at all. Host-visible memory is never shared: it is mapped by
    /// whole allocation, and two owners of one mapping is a bug waiting to happen.
    /// </para>
    /// </remarks>
    public VulkanAllocation AllocateResource(
        in MemoryRequirements requirements,
        MemoryPropertyFlags properties,
        VulkanResourceKind kind,
        bool deviceAddressable = false)
    {
        bool shareable = (properties & MemoryPropertyFlags.HostVisibleBit) == 0
            && requirements.Size <= MaxSubAllocation
            && requirements.Alignment <= MaxSubAllocation;

        if (!shareable)
        {
            return new VulkanAllocation(Allocate(requirements, properties, deviceAddressable), 0, null, 0);
        }

        uint typeIndex = FindMemoryType(requirements.MemoryTypeBits, properties);
        bool addressable = deviceAddressable && SupportsDeviceAddress;
        ulong slot = Math.Max(MinSlot, Math.Max(
            BitOperations.RoundUpToPowerOf2(requirements.Size),
            BitOperations.RoundUpToPowerOf2(Math.Max(1UL, requirements.Alignment))));
        SlabKey key = new(typeIndex, addressable, kind, slot);

        lock (_gate)
        {
            if (!_slabs.TryGetValue(key, out List<Slab>? slabs))
            {
                slabs = [];
                _slabs[key] = slabs;
            }

            foreach (Slab candidate in slabs)
            {
                if (candidate.Free.Count > 0)
                {
                    return Take(candidate);
                }
            }
        }

        // A new block, from the pool or the driver, made outside the lock like any other allocation.
        MemoryRequirements block = new()
        {
            Size = SlabBytes,
            Alignment = slot,
            MemoryTypeBits = 1u << (int)typeIndex,
        };

        DeviceMemory memory = Allocate(block, properties, addressable);
        Slab slab = new(key, memory, (int)(SlabBytes / slot));

        lock (_gate)
        {
            _slabs[key].Add(slab);
            return Take(slab);
        }

        VulkanAllocation Take(Slab from)
        {
            int index = from.Free.Pop();
            from.Used++;
            _slabSlotsUsed++;
            _slabBytesUsed += (long)from.Key.Slot;
            return new VulkanAllocation(from.Memory, (ulong)index * from.Key.Slot, from, index);
        }
    }

    /// <summary>Frees an allocation made by <see cref="AllocateResource"/>.</summary>
    /// <param name="allocation">The allocation. A default one is ignored.</param>
    /// <remarks>
    /// A slot goes back to its block. A block left empty is given back too, unless it is the last of its
    /// shape with room -- the next resource of that shape would only allocate it again.
    /// </remarks>
    public void Free(in VulkanAllocation allocation)
    {
        if (allocation.Slab is not { } slab)
        {
            Free(allocation.Memory);
            return;
        }

        DeviceMemory? release = null;
        lock (_gate)
        {
            slab.Free.Push(allocation.Slot);
            slab.Used--;
            _slabSlotsUsed--;
            _slabBytesUsed -= (long)slab.Key.Slot;

            if (slab.Used == 0 && !_disposed
                && _slabs.TryGetValue(slab.Key, out List<Slab>? slabs)
                && slabs.Exists(other => !ReferenceEquals(other, slab) && other.Free.Count > 0))
            {
                slabs.Remove(slab);
                release = slab.Memory;
            }
        }

        if (release is { } memory)
        {
            Free(memory);
        }
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
            bool known = _shapes.TryGetValue(memory.Handle, out PoolKey key);
            if (!_disposed && known && HasRoomFor(key))
            {
                if (!_free.TryGetValue(key, out Stack<DeviceMemory>? bucket))
                {
                    bucket = new Stack<DeviceMemory>();
                    _free[key] = bucket;
                }

                bucket.Push(memory);
                Pool(key);
                return;
            }

            if (known)
            {
                ForgetShape(memory, key);
            }
        }

        _vk.FreeMemory(_device, memory, null);
    }

    /// <summary>Gives retained device-local blocks back to the driver until at most <paramref name="keepBytes"/> remain.</summary>
    /// <param name="keepBytes">How much to keep for the next load to reuse.</param>
    /// <returns>How many bytes went back.</returns>
    /// <remarks>
    /// The pool earns its keep during a load -- the outgoing level's textures are freed a few frames after
    /// the incoming one starts allocating, and the incoming one reuses them -- and holds hundreds of
    /// megabytes for nothing after it. On a driver where device memory is backed by the process's own
    /// commit, as it is here, those megabytes count against the game's RAM.
    /// </remarks>
    public long TrimDeviceLocal(long keepBytes)
    {
        List<DeviceMemory> released = [];
        long bytes = 0;

        lock (_gate)
        {
            foreach ((PoolKey key, Stack<DeviceMemory> bucket) in _free)
            {
                if (IsHostVisible(key.TypeIndex))
                {
                    continue;
                }

                while (bucket.Count > 0 && _pooledDeviceBytes > keepBytes)
                {
                    DeviceMemory memory = bucket.Pop();
                    Unpool(key);
                    ForgetShape(memory, key);
                    released.Add(memory);
                    bytes += (long)key.Size;
                }
            }
        }

        foreach (DeviceMemory memory in released)
        {
            _vk.FreeMemory(_device, memory, null);
        }

        return bytes;
    }

    /// <summary>Gives every retained host-visible block back to the driver.</summary>
    /// <remarks>
    /// For the end of a load. Upload staging comes back in a burst as a level's textures go up, and kept,
    /// it is RAM held for the next burst -- which may be minutes away, or never. Device-local blocks stay:
    /// they are what the next level's textures are allocated out of.
    /// </remarks>
    /// <returns>How many bytes went back.</returns>
    public long TrimHostVisible()
    {
        List<DeviceMemory> released = [];
        long bytes = 0;

        lock (_gate)
        {
            foreach ((PoolKey key, Stack<DeviceMemory> bucket) in _free)
            {
                if (!IsHostVisible(key.TypeIndex))
                {
                    continue;
                }

                while (bucket.Count > 0)
                {
                    DeviceMemory memory = bucket.Pop();
                    Unpool(key);
                    ForgetShape(memory, key);
                    released.Add(memory);
                    bytes += (long)key.Size;
                }
            }
        }

        foreach (DeviceMemory memory in released)
        {
            _vk.FreeMemory(_device, memory, null);
        }

        return bytes;
    }

    /// <summary>Describes what is allocated, per memory type, for a memory report.</summary>
    /// <returns>One line per memory type in use, then a summary line.</returns>
    public string Describe()
    {
        var text = new System.Text.StringBuilder();
        lock (_gate)
        {
            PhysicalDeviceMemoryProperties memory = _memoryProperties;
            for (int i = 0; i < memory.MemoryTypeCount; i++)
            {
                if (_allocatedCount[i] == 0)
                {
                    continue;
                }

                MemoryPropertyFlags flags = memory.MemoryTypes[i].PropertyFlags;
                text.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"  type {i} (heap {memory.MemoryTypes[i].HeapIndex}, {Describe(flags)}): "
                    + $"{Mib(_allocatedBytes[i])} in {_allocatedCount[i]} block(s), "
                    + $"{Mib(_pooledBytesByType[i])} of it pooled in {_pooledCountByType[i]}\n");
            }

            int slabCount = 0;
            foreach (List<Slab> slabs in _slabs.Values)
            {
                slabCount += slabs.Count;
            }

            text.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"  shared blocks: {slabCount} of {Mib((long)SlabBytes)}, {_slabSlotsUsed} small resource(s) in "
                + $"{Mib(_slabBytesUsed)} of them\n");

            text.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"  pool: {Mib(_pooledDeviceBytes)} device-local (cap {Mib(_maxPooledBytes)}), "
                + $"{Mib(_pooledHostBytes)} host-visible (cap {Mib(MaxPooledHostBytes)}); "
                + $"{_poolHits} hit(s), {_poolMisses} miss(es), {_driverFrees} freed to the driver");
        }

        return text.ToString();
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

            // The shared blocks too: everything carved from them has been destroyed by now.
            foreach (List<Slab> slabs in _slabs.Values)
            {
                foreach (Slab slab in slabs)
                {
                    _vk.FreeMemory(_device, slab.Memory, null);
                }
            }

            _slabs.Clear();
            _shapes.Clear();
            _pooledDeviceBytes = 0;
            _pooledHostBytes = 0;
        }
    }

    private bool IsHostVisible(uint typeIndex) =>
        (_memoryProperties.MemoryTypes[(int)typeIndex].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) != 0;

    private bool HasRoomFor(in PoolKey key) =>
        IsHostVisible(key.TypeIndex)
            ? _pooledHostBytes + (long)key.Size <= MaxPooledHostBytes
            : _pooledDeviceBytes + (long)key.Size <= _maxPooledBytes;

    private void Pool(in PoolKey key)
    {
        if (IsHostVisible(key.TypeIndex))
        {
            _pooledHostBytes += (long)key.Size;
        }
        else
        {
            _pooledDeviceBytes += (long)key.Size;
        }

        _pooledBytesByType[key.TypeIndex] += (long)key.Size;
        _pooledCountByType[key.TypeIndex]++;
    }

    private void Unpool(in PoolKey key)
    {
        if (IsHostVisible(key.TypeIndex))
        {
            _pooledHostBytes -= (long)key.Size;
        }
        else
        {
            _pooledDeviceBytes -= (long)key.Size;
        }

        _pooledBytesByType[key.TypeIndex] -= (long)key.Size;
        _pooledCountByType[key.TypeIndex]--;
    }

    private void ForgetShape(DeviceMemory memory, in PoolKey key)
    {
        _shapes.Remove(memory.Handle);
        _allocatedBytes[key.TypeIndex] -= (long)key.Size;
        _allocatedCount[key.TypeIndex]--;
        _driverFrees++;
    }

    private static string Mib(long bytes) =>
        FormattableString.Invariant($"{bytes / (1024.0 * 1024.0):F1} MiB");

    private static string Describe(MemoryPropertyFlags flags)
    {
        List<string> parts = [];
        if ((flags & MemoryPropertyFlags.DeviceLocalBit) != 0)
        {
            parts.Add("device-local");
        }

        if ((flags & MemoryPropertyFlags.HostVisibleBit) != 0)
        {
            parts.Add("host-visible");
        }

        if ((flags & MemoryPropertyFlags.HostCachedBit) != 0)
        {
            parts.Add("cached");
        }

        return parts.Count == 0 ? "none" : string.Join('+', parts);
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

        // Every discrete card exposes a device-local, host-visible type over the classic 256 MB window,
        // with or without a resizable BAR, so the type alone says nothing. A resizable BAR is that type
        // over a heap larger than the window.
        const ulong ClassicBarWindow = 256UL * 1024 * 1024;

        PhysicalDeviceMemoryProperties memory = _memoryProperties;
        for (uint i = 0; i < memory.MemoryTypeCount; i++)
        {
            MemoryType type = memory.MemoryTypes[(int)i];
            if ((type.PropertyFlags & wanted) == wanted
                && memory.MemoryHeaps[(int)type.HeapIndex].Size > ClassicBarWindow)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The shape of a shared block: memory type, address flag, resource kind and slot size.</summary>
    internal readonly record struct SlabKey(uint TypeIndex, bool Addressable, VulkanResourceKind Kind, ulong Slot);

    /// <summary>A shared block and its free slots.</summary>
    internal sealed class Slab
    {
        public Slab(SlabKey key, DeviceMemory memory, int slots)
        {
            Key = key;
            Memory = memory;
            for (int i = slots - 1; i >= 0; i--)
            {
                Free.Push(i);
            }
        }

        public SlabKey Key { get; }

        public DeviceMemory Memory { get; }

        public Stack<int> Free { get; } = new();

        public int Used { get; set; }
    }

    /// <summary>The shape a retained block was allocated for: identical shapes are interchangeable.</summary>
    /// <param name="TypeIndex">The memory type the block lives in.</param>
    /// <param name="Size">The block's byte size.</param>
    /// <param name="Addressable">Whether the block was allocated to carry a device address.</param>
    private readonly record struct PoolKey(uint TypeIndex, ulong Size, bool Addressable);
}

/// <summary>What a resource's memory is: a buffer, or an image.</summary>
internal enum VulkanResourceKind
{
    /// <summary>A buffer.</summary>
    Buffer,

    /// <summary>An image.</summary>
    Image,
}

/// <summary>Where a resource is bound: a block of memory, and the offset into it.</summary>
/// <param name="Memory">The memory.</param>
/// <param name="Offset">Where the resource starts in it.</param>
/// <param name="Slab">The shared block it was carved from, or <see langword="null"/> for a block of its own.</param>
/// <param name="Slot">Which slot of that block.</param>
internal readonly record struct VulkanAllocation(DeviceMemory Memory, ulong Offset, VulkanAllocator.Slab? Slab, int Slot);
