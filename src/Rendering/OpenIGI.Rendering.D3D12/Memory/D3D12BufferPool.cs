using OpenIGI.Rendering.D3D12.Interop;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12.Memory;

/// <summary>A run of one of a pool's blocks: the block's resource, and where in it the run starts.</summary>
/// <param name="Block">The block.</param>
/// <param name="Offset">Where the run starts.</param>
/// <param name="Size">How long it is.</param>
internal readonly record struct D3D12BufferRange(D3D12BufferBlock Block, ulong Offset, ulong Size);

/// <summary>One large buffer a pool hands out runs of.</summary>
internal sealed unsafe class D3D12BufferBlock
{
    private readonly List<(ulong Start, ulong Size)> _free;

    /// <summary>Initialises a new instance of the <see cref="D3D12BufferBlock"/> class.</summary>
    /// <param name="resource">The buffer, which this owns.</param>
    /// <param name="size">Its size.</param>
    /// <param name="mapped">Where it is mapped, or null.</param>
    public D3D12BufferBlock(ID3D12Resource* resource, ulong size, byte* mapped)
    {
        Resource = resource;
        Size = size;
        Mapped = mapped;
        GpuAddress = resource->GetGPUVirtualAddress();
        _free = [(0, size)];
    }

    /// <summary>Gets the buffer.</summary>
    public ID3D12Resource* Resource { get; private set; }

    /// <summary>Gets its size.</summary>
    public ulong Size { get; }

    /// <summary>Gets where it is mapped, or null for device-local memory.</summary>
    public byte* Mapped { get; }

    /// <summary>Gets its GPU address.</summary>
    public ulong GpuAddress { get; }

    /// <summary>Gets how many bytes are handed out.</summary>
    public ulong Used { get; private set; }

    /// <summary>Takes the first run long enough, at the alignment asked for.</summary>
    /// <param name="size">How many bytes.</param>
    /// <param name="alignment">A power of two the run starts on.</param>
    /// <param name="offset">Where the run starts.</param>
    /// <returns><see langword="true"/> when one was found.</returns>
    public bool TryAllocate(ulong size, ulong alignment, out ulong offset)
    {
        for (int i = 0; i < _free.Count; i++)
        {
            (ulong start, ulong length) = _free[i];
            ulong aligned = (start + alignment - 1) & ~(alignment - 1);
            ulong waste = aligned - start;
            if (length < waste + size)
            {
                continue;
            }

            _free.RemoveAt(i);
            ulong tail = length - waste - size;
            if (tail > 0)
            {
                _free.Insert(i, (aligned + size, tail));
            }

            if (waste > 0)
            {
                _free.Insert(i, (start, waste));
            }

            Used += size;
            offset = aligned;
            return true;
        }

        offset = 0;
        return false;
    }

    /// <summary>Gives a run back, merging it with its neighbours.</summary>
    /// <param name="start">Where it starts.</param>
    /// <param name="size">How long it is.</param>
    public void Free(ulong start, ulong size)
    {
        Used -= size;
        int at = 0;
        while (at < _free.Count && _free[at].Start < start)
        {
            at++;
        }

        _free.Insert(at, (start, size));
        if (at + 1 < _free.Count && start + size == _free[at + 1].Start)
        {
            _free[at] = (start, size + _free[at + 1].Size);
            _free.RemoveAt(at + 1);
        }

        if (at > 0 && _free[at - 1].Start + _free[at - 1].Size == start)
        {
            _free[at - 1] = (_free[at - 1].Start, _free[at - 1].Size + _free[at].Size);
            _free.RemoveAt(at);
        }
    }

    /// <summary>Releases the buffer.</summary>
    public void Release()
    {
        if (Resource is not null)
        {
            ((IUnknown*)Resource)->Release();
            Resource = null;
        }
    }
}

/// <summary>Small buffers of one heap type, placed side by side in large shared buffers.</summary>
/// <remarks>
/// <para>
/// A committed resource is a heap of its own, and a heap is at least 64 KiB: a level's few thousand small
/// vertex, index and constant buffers committed one by one waste most of the memory they take and cost a
/// kernel call each. Here they are runs of a few 32 MiB buffers instead, 512 bytes apart -- the alignment a
/// texture copy footprint needs, and a multiple of every other a buffer view or copy does.
/// </para>
/// <para>
/// Enhanced barriers give a buffer no layout, so sharing one resource between unrelated buffers costs no
/// state tracking: what orders one buffer's write before another's read is a global barrier either way.
/// Only buffers no shader stores to and none viewed by element are pooled -- a structured view counts in
/// whole elements from the start of the resource, which a run 512 bytes in need not begin on.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12BufferPool : IDisposable
{
    /// <summary>The size of one block.</summary>
    public const ulong BlockSize = 32UL * 1024 * 1024;

    /// <summary>The largest buffer pooled; anything larger is committed on its own.</summary>
    public const ulong MaxPooledSize = 16UL * 1024 * 1024;

    /// <summary>Where a pooled run starts: <c>D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT</c>, which also serves a constant view.</summary>
    public const ulong Alignment = 512;

    private readonly ID3D12Device10* _device;
    private readonly HeapType _heap;
    private readonly string _label;
    private readonly List<D3D12BufferBlock> _blocks = [];

    /// <summary>Initialises a new instance of the <see cref="D3D12BufferPool"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="heap">The heap type every block lives in.</param>
    /// <param name="label">A name for captures.</param>
    public D3D12BufferPool(ID3D12Device10* device, HeapType heap, string label)
    {
        _device = device;
        _heap = heap;
        _label = label;
    }

    /// <summary>Gets how many blocks it holds.</summary>
    public int Blocks => _blocks.Count;

    /// <summary>Gets how many bytes are handed out.</summary>
    public ulong Used => _blocks.Aggregate(0UL, (sum, block) => sum + block.Used);

    /// <summary>Takes a run.</summary>
    /// <param name="size">How many bytes, at most <see cref="MaxPooledSize"/>.</param>
    /// <returns>The run.</returns>
    public D3D12BufferRange Allocate(ulong size)
    {
        size = (size + Alignment - 1) & ~(Alignment - 1);
        foreach (D3D12BufferBlock block in _blocks)
        {
            if (block.TryAllocate(size, Alignment, out ulong offset))
            {
                return new D3D12BufferRange(block, offset, size);
            }
        }

        D3D12BufferBlock added = CreateBlock();
        _blocks.Add(added);
        if (!added.TryAllocate(size, Alignment, out ulong first))
        {
            throw new GraphicsDeviceException($"A {size}-byte buffer does not fit a fresh {BlockSize}-byte pool block.");
        }

        return new D3D12BufferRange(added, first, size);
    }

    /// <summary>Gives a run back; a block left empty is released, unless it is the last.</summary>
    /// <param name="range">The run.</param>
    public void Free(D3D12BufferRange range)
    {
        range.Block.Free(range.Offset, range.Size);
        if (range.Block.Used == 0 && _blocks.Count > 1)
        {
            _blocks.Remove(range.Block);
            range.Block.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (D3D12BufferBlock block in _blocks)
        {
            block.Release();
        }

        _blocks.Clear();
    }

    private D3D12BufferBlock CreateBlock()
    {
        ResourceDesc1 description = new()
        {
            Dimension = ResourceDimension.Buffer,
            Width = BlockSize,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc(1, 0),
            Layout = TextureLayout.LayoutRowMajor,
        };

        HeapProperties properties = new() { Type = _heap };
        ID3D12Resource* resource = null;
        Guid iid = ID3D12Resource.Guid;
        D3D12Check.Ok(
            _device->CreateCommittedResource3(&properties, HeapFlags.CreateNotZeroed, &description, BarrierLayout.Undefined, null, null, 0, null, &iid, (void**)&resource),
            $"CreateCommittedResource3 (buffer pool '{_label}')");
        D3D12Names.Set((ID3D12Object*)resource, $"{_label} pool {_blocks.Count}");
        D3D12Residency.High(_device, resource);

        byte* mapped = null;
        if (_heap != HeapType.Default)
        {
            void* pointer = null;
            Silk.NET.Direct3D12.Range none = default;
            D3D12Check.Ok(resource->Map(0, &none, &pointer), "ID3D12Resource::Map (buffer pool)");
            mapped = (byte*)pointer;
        }

        return new D3D12BufferBlock(resource, BlockSize, mapped);
    }
}
