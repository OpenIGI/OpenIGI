namespace OpenIGI.Rendering.Vulkan.Bindless;

/// <summary>Hands out and takes back slots in a fixed-size heap.</summary>
/// <remarks>
/// <para>
/// A sorted list of free ranges rather than a free list of individual slots, for two reasons. It stays
/// compact — a heap that is mostly free is one range, not half a million entries — and it can serve a
/// run of consecutive slots, which is what an array of textures indexed as a block needs.
/// </para>
/// <para>
/// First fit, not best fit. The heaps here are large relative to what is ever allocated from them, so
/// fragmentation never becomes the problem that would justify the extra search.
/// </para>
/// </remarks>
internal sealed class VulkanDescriptorAllocator
{
    private readonly List<Range> _free = [];
    private readonly Lock _lock = new();
    private readonly uint _capacity;

    /// <summary>Initialises a new instance of the <see cref="VulkanDescriptorAllocator"/> class.</summary>
    /// <param name="capacity">How many slots the heap holds.</param>
    public VulkanDescriptorAllocator(uint capacity)
    {
        _capacity = capacity;
        if (capacity > 0)
        {
            _free.Add(new Range(0, capacity - 1));
        }
    }

    /// <summary>Gets how many slots the heap holds in total.</summary>
    public uint Capacity => _capacity;

    /// <summary>Gets how many slots are currently handed out.</summary>
    public uint InUse
    {
        get
        {
            lock (_lock)
            {
                uint free = 0;
                foreach (Range range in _free)
                {
                    free += range.Last - range.First + 1;
                }

                return _capacity - free;
            }
        }
    }

    /// <summary>Takes a run of consecutive slots.</summary>
    /// <param name="count">How many slots are needed.</param>
    /// <returns>The first slot's index, or <see cref="VulkanDescriptorHandle.InvalidIndex"/> when full.</returns>
    public uint Allocate(uint count = 1)
    {
        if (count == 0)
        {
            return VulkanDescriptorHandle.InvalidIndex;
        }

        lock (_lock)
        {
            for (int i = 0; i < _free.Count; i++)
            {
                Range range = _free[i];
                uint length = range.Last - range.First + 1;
                if (length < count)
                {
                    continue;
                }

                uint start = range.First;
                if (length == count)
                {
                    _free.RemoveAt(i);
                }
                else
                {
                    _free[i] = new Range(range.First + count, range.Last);
                }

                return start;
            }
        }

        return VulkanDescriptorHandle.InvalidIndex;
    }

    /// <summary>Gives a run of slots back, merging it with any range it now touches.</summary>
    /// <param name="index">The first slot's index.</param>
    /// <param name="count">How many slots to release.</param>
    public void Free(uint index, uint count = 1)
    {
        if (count == 0 || index == VulkanDescriptorHandle.InvalidIndex)
        {
            return;
        }

        uint last = index + count - 1;

        lock (_lock)
        {
            // Insert in order, then coalesce with the neighbours — otherwise a heap that is allocated and
            // freed repeatedly would shred into single-slot ranges and never serve a run again.
            int position = 0;
            while (position < _free.Count && _free[position].First < index)
            {
                position++;
            }

            _free.Insert(position, new Range(index, last));

            if (position + 1 < _free.Count && _free[position].Last + 1 == _free[position + 1].First)
            {
                _free[position] = new Range(_free[position].First, _free[position + 1].Last);
                _free.RemoveAt(position + 1);
            }

            if (position > 0 && _free[position - 1].Last + 1 == _free[position].First)
            {
                _free[position - 1] = new Range(_free[position - 1].First, _free[position].Last);
                _free.RemoveAt(position);
            }
        }
    }

    private readonly record struct Range(uint First, uint Last);
}
