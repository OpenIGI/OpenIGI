using OpenIGI.Rendering.D3D12.Interop;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Descriptors;

/// <summary>One descriptor heap and the runs of it handed out, first fit, merged as they come back.</summary>
/// <remarks>
/// <para>
/// The shader-visible heaps are each one heap for the life of the device: a command list binds one
/// CBV/SRV/UAV heap and one sampler heap, and changing either mid-list flushes the GPU. A bind group takes a
/// run of the heap when it is created and writes its descriptors straight into it, so binding one is a
/// pointer into the heap and nothing is copied per draw.
/// </para>
/// <para>
/// The render-target and depth heaps are the same allocator over a heap the GPU never sees.
/// </para>
/// <para>
/// A run given back is free at once: the caller defers giving it back until no frame in flight can read
/// it, which is the device's retirement, not this.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12DescriptorHeap : IDisposable
{
    private readonly List<(int Start, int Count)> _free = [];
    private ID3D12DescriptorHeap* _heap;
    private readonly ulong _cpuStart;
    private readonly ulong _gpuStart;
    private readonly uint _increment;

    /// <summary>Initialises a new instance of the <see cref="D3D12DescriptorHeap"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="type">Which kind of descriptor it holds.</param>
    /// <param name="capacity">How many.</param>
    /// <param name="shaderVisible">Whether shaders read it.</param>
    /// <param name="label">A name for captures.</param>
    public D3D12DescriptorHeap(ID3D12Device10* device, DescriptorHeapType type, int capacity, bool shaderVisible, string label)
    {
        DescriptorHeapDesc description = new()
        {
            Type = type,
            NumDescriptors = (uint)capacity,
            Flags = shaderVisible ? DescriptorHeapFlags.ShaderVisible : DescriptorHeapFlags.None,
        };

        ID3D12DescriptorHeap* heap = null;
        Guid iid = ID3D12DescriptorHeap.Guid;
        D3D12Check.Ok(device->CreateDescriptorHeap(&description, &iid, (void**)&heap), $"CreateDescriptorHeap ({label})");
        _heap = heap;
        D3D12Names.Set((ID3D12Object*)heap, label);

        Capacity = capacity;
        ShaderVisible = shaderVisible;
        _increment = device->GetDescriptorHandleIncrementSize(type);
        _cpuStart = heap->GetCPUDescriptorHandleForHeapStart().Ptr;
        _gpuStart = shaderVisible ? heap->GetGPUDescriptorHandleForHeapStart().Ptr : 0;
        _free.Add((0, capacity));
    }

    /// <summary>Gets the heap.</summary>
    public ID3D12DescriptorHeap* Heap => _heap;

    /// <summary>Gets how many descriptors it holds.</summary>
    public int Capacity { get; }

    /// <summary>Gets a value indicating whether shaders read it.</summary>
    public bool ShaderVisible { get; }

    /// <summary>Gets how many descriptors are handed out.</summary>
    public int InUse { get; private set; }

    /// <summary>Takes a run of descriptors.</summary>
    /// <param name="count">How many, at least one.</param>
    /// <returns>The index of the first.</returns>
    /// <exception cref="GraphicsDeviceException">The heap has no run that long left.</exception>
    public int Allocate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        for (int i = 0; i < _free.Count; i++)
        {
            (int start, int length) = _free[i];
            if (length < count)
            {
                continue;
            }

            if (length == count)
            {
                _free.RemoveAt(i);
            }
            else
            {
                _free[i] = (start + count, length - count);
            }

            InUse += count;
            return start;
        }

        throw new GraphicsDeviceException(
            $"A descriptor heap of {Capacity} is out of room for {count} more ({InUse} in use).");
    }

    /// <summary>Gives a run back, merging it with its neighbours.</summary>
    /// <param name="start">Where it starts.</param>
    /// <param name="count">How long it is.</param>
    public void Free(int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        InUse -= count;

        int at = 0;
        while (at < _free.Count && _free[at].Start < start)
        {
            at++;
        }

        _free.Insert(at, (start, count));

        if (at + 1 < _free.Count && start + count == _free[at + 1].Start)
        {
            _free[at] = (start, count + _free[at + 1].Count);
            _free.RemoveAt(at + 1);
        }

        if (at > 0 && _free[at - 1].Start + _free[at - 1].Count == start)
        {
            _free[at - 1] = (_free[at - 1].Start, _free[at - 1].Count + _free[at].Count);
            _free.RemoveAt(at);
        }
    }

    /// <summary>The CPU handle of a descriptor, which a view is written through.</summary>
    /// <param name="index">The descriptor.</param>
    /// <returns>The handle.</returns>
    public CpuDescriptorHandle Cpu(int index) => new() { Ptr = (nuint)(_cpuStart + ((ulong)index * _increment)) };

    /// <summary>The GPU handle of a descriptor, which a table is bound through.</summary>
    /// <param name="index">The descriptor.</param>
    /// <returns>The handle.</returns>
    public GpuDescriptorHandle Gpu(int index) => new() { Ptr = _gpuStart + ((ulong)index * _increment) };

    /// <inheritdoc/>
    public void Dispose() => D3D12Check.Release(ref _heap);
}
