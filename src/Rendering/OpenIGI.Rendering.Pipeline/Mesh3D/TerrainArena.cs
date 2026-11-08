using System.Numerics;
using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The terrain's chunks in one arena, their triangles filed by bucket, and the table a frame places them with.</summary>
/// <remarks>
/// <para>
/// The storage behind <see cref="ITerrainArenaDevice"/> (World/TerrainArena.slang draws from it). Every chunk's vertices
/// sit in one buffer, beside a second stream saying which chunk each vertex is; every bucket's triangles sit in one index
/// list, rebased onto the arena. Both are kept on the host as well, so a buffer outgrown is made again from what is
/// there, and a chunk taken out leaves its triangles zeroed -- degenerate -- until the list is compacted.
/// </para>
/// <para>
/// Everything is uploaded at the frame's first bucket draw and not again that frame. A write from the host lands before
/// the frame's commands run, whichever command it follows in the recording, so the arena the GPU sees is the one at the
/// end of the frame's recording: what changes after a frame has drawn waits for the next, and every draw of a frame
/// sees one consistent arena. Changes before the first draw -- the frame's new chunks, its placements -- are what that
/// upload carries.
/// </para>
/// </remarks>
internal sealed class TerrainArena : IDisposable
{
    /// <summary>The record a chunk not placed this frame has.</summary>
    public const uint NoRecord = uint.MaxValue;

    /// <summary>How many floats one placement takes: the four columns, the ramp and the offset from the eye.</summary>
    public const int RecordFloats = 24;

    // The smallest block of vertices a chunk is given; blocks come in powers of two from here, and a freed block waits
    // for a chunk of its size class.
    private const int MinimumBlockBits = 6;

    // Past this many indices, and once half of them are holes, a bucket's list is packed again.
    private const int CompactionFloor = 4096;

    private readonly IRhiDevice _device;
    private readonly IRhiBindGroupLayout _layout;
    private readonly RetiredResources? _retired;

    private Vertex3D[] _vertices = new Vertex3D[1 << 16];
    private uint[] _chunkOf = new uint[1 << 16];
    private int _vertexTop;
    private readonly List<int>[] _freeBlocks = new List<int>[32];
    private int _dirtyVertexStart = int.MaxValue;
    private int _dirtyVertexEnd;
    private IRhiBuffer? _vertexBuffer;
    private IRhiBuffer? _chunkBuffer;

    private Slot[] _slots = new Slot[256];
    private uint[] _recordOf = new uint[256];
    private readonly Stack<int> _freeSlots = new();
    private int _slotTop;

    private float[] _records = new float[RecordFloats * 1024];
    private int _recordCount;
    private readonly List<int> _placed = [];
    private IRhiBuffer? _recordOfBuffer;
    private IRhiBuffer? _recordBuffer;
    private IRhiBindGroup? _group;

    private readonly Dictionary<int, Bucket> _buckets = [];
    private readonly List<Bucket> _bucketList = [];

    private ulong _frame = ulong.MaxValue;
    private bool _uploaded;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="TerrainArena"/> class.</summary>
    /// <param name="device">The device its buffers live on.</param>
    /// <param name="layout">The layout of the group the vertex stage reads the table through (<see cref="CreateLayout"/>).</param>
    /// <param name="retired">Where an outgrown buffer waits for the frames that may still read it.</param>
    public TerrainArena(IRhiDevice device, IRhiBindGroupLayout layout, RetiredResources? retired)
    {
        _device = device;
        _layout = layout;
        _retired = retired;
        Array.Fill(_recordOf, NoRecord);
    }

    /// <summary>Gets the buffer of every chunk's vertices; valid after <see cref="Upload"/>.</summary>
    public IRhiBuffer VertexBuffer => _vertexBuffer!;

    /// <summary>Gets the buffer saying which chunk each vertex is; valid after <see cref="Upload"/>.</summary>
    public IRhiBuffer ChunkBuffer => _chunkBuffer!;

    /// <summary>Gets the group the vertex stage reads the frame's table through; valid after <see cref="Upload"/>.</summary>
    public IRhiBindGroup Group => _group!;

    /// <summary>Gets how many chunks are in the arena.</summary>
    public int ChunkCount => _slotTop - _freeSlots.Count;

    /// <summary>Gets how many chunks are placed this frame.</summary>
    public int PlacedCount => _placed.Count;

    /// <summary>The layout of the group the table is read through: the record of each slot, then the records.</summary>
    /// <param name="device">The device.</param>
    /// <returns>The layout.</returns>
    public static IRhiBindGroupLayout CreateLayout(IRhiDevice device) =>
        device.CreateBindGroupLayout(new RhiBindGroupLayoutDescriptor(
            [
                new RhiBindGroupLayoutEntry(0, RhiBindingType.ReadOnlyStorageBuffer, RhiShaderStages.Vertex),
                new RhiBindGroupLayoutEntry(1, RhiBindingType.ReadOnlyStorageBuffer, RhiShaderStages.Vertex),
            ],
            "terrain-arena"));

    /// <summary>Puts a chunk in.</summary>
    /// <param name="vertices">Its vertices.</param>
    /// <param name="indices">Its triangles.</param>
    /// <param name="parts">Where each layer is filed.</param>
    /// <returns>Its slot, or -1 for a chunk with nothing to draw or a layer outside its indices.</returns>
    public int Add(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices, ReadOnlySpan<TerrainArenaPart> parts)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (vertices.IsEmpty || parts.IsEmpty)
        {
            return -1;
        }

        foreach (TerrainArenaPart part in parts)
        {
            if (part.IndexOffset < 0 || part.IndexCount < 0 || part.IndexOffset + part.IndexCount > indices.Length)
            {
                return -1;
            }
        }

        int slot = _freeSlots.Count > 0 ? _freeSlots.Pop() : _slotTop++;
        if (slot >= _slots.Length)
        {
            int size = _slots.Length * 2;
            Array.Resize(ref _slots, size);
            int old = _recordOf.Length;
            Array.Resize(ref _recordOf, size);
            Array.Fill(_recordOf, NoRecord, old, size - old);
        }

        int sizeClass = Math.Max(MinimumBlockBits, BitOperations.Log2(BitOperations.RoundUpToPowerOf2((uint)vertices.Length)));
        int start = TakeBlock(sizeClass);
        vertices.CopyTo(_vertices.AsSpan(start));
        _chunkOf.AsSpan(start, vertices.Length).Fill((uint)slot);
        MarkVertices(start, vertices.Length);

        var segments = new Segment[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            TerrainArenaPart part = parts[i];
            Bucket bucket = BucketFor(part.Bucket);
            segments[i] = new Segment(bucket, bucket.Append(indices.Slice(part.IndexOffset, part.IndexCount), (uint)start), part.IndexCount);
        }

        _slots[slot] = new Slot(start, sizeClass, segments);
        _recordOf[slot] = NoRecord;
        return slot;
    }

    /// <summary>Takes a chunk out: its triangles become degenerate and its block and slot are free.</summary>
    /// <param name="slot">Its slot.</param>
    public void Remove(int slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if ((uint)slot >= (uint)_slotTop || _slots[slot].Segments is not { } segments)
        {
            return;
        }

        foreach (Segment segment in segments)
        {
            segment.Bucket.Erase(segment.Start, segment.Count);
        }

        (_freeBlocks[_slots[slot].SizeClass] ??= []).Add(_slots[slot].Start);
        _slots[slot] = default;
        _recordOf[slot] = NoRecord;
        _freeSlots.Push(slot);

        foreach (Segment segment in segments)
        {
            if (segment.Bucket.Holes * 2 > segment.Bucket.Count && segment.Bucket.Count > CompactionFloor)
            {
                Compact(segment.Bucket);
            }
        }
    }

    /// <summary>Starts a frame's placements.</summary>
    /// <param name="frame">The device's frame counter.</param>
    /// <returns><see langword="false"/> when this frame has already drawn from the arena.</returns>
    public bool Begin(ulong frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame == _frame && _uploaded)
        {
            return false;
        }

        foreach (int slot in _placed)
        {
            _recordOf[slot] = NoRecord;
        }

        _placed.Clear();
        _recordCount = 0;
        _frame = frame;
        _uploaded = false;
        return true;
    }

    /// <summary>Places a chunk, from the block a draw of it alone would push.</summary>
    /// <param name="slot">Its slot.</param>
    /// <param name="block">The forward block for the chunk (<see cref="OpenIGI.Rendering.Mesh3D.ForwardPushBlock"/>).</param>
    public void Place(int slot, ReadOnlySpan<float> block)
    {
        if ((uint)slot >= (uint)_slotTop || _slots[slot].Segments is null || _uploaded)
        {
            return;
        }

        if (_recordOf[slot] == NoRecord)
        {
            _placed.Add(slot);
        }

        int record = _recordCount++;
        if ((record + 1) * RecordFloats > _records.Length)
        {
            Array.Resize(ref _records, _records.Length * 2);
        }

        // The model-view-projection's sixteen as they lie; the ramp from the first terrain row and the offset from the
        // second, each to its own vector.
        Span<float> at = _records.AsSpan(record * RecordFloats, RecordFloats);
        block[..16].CopyTo(at);
        block.Slice(20, 3).CopyTo(at[16..]);
        at[19] = 0f;
        block.Slice(24, 3).CopyTo(at[20..]);
        at[23] = 0f;
        _recordOf[slot] = (uint)record;
    }

    /// <summary>Gets a bucket's index list, if anything has been filed under it.</summary>
    /// <param name="key">The bucket.</param>
    /// <param name="buffer">Its indices on the device.</param>
    /// <param name="count">How many to draw.</param>
    /// <returns><see langword="true"/> when there is something to draw.</returns>
    public bool TryBucket(int key, out IRhiBuffer buffer, out int count)
    {
        if (_buckets.TryGetValue(key, out Bucket? bucket) && bucket.Buffer is { } uploaded && bucket.Count > 0)
        {
            buffer = uploaded;
            count = bucket.Count;
            return true;
        }

        buffer = null!;
        count = 0;
        return false;
    }

    /// <summary>Uploads what the frame changed, once, before its first bucket draw.</summary>
    public void Upload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_uploaded)
        {
            return;
        }

        _uploaded = true;
        bool regroup = _group is null;

        // The vertices and the chunk stream, grown together.
        ulong vertexBytes = (ulong)_vertices.Length * Vertex3D.Size;
        if (_vertexBuffer is null || _vertexBuffer.SizeInBytes < vertexBytes)
        {
            Retire(_vertexBuffer);
            Retire(_chunkBuffer);
            _vertexBuffer = _device.CreateBuffer(new RhiBufferDescriptor(
                vertexBytes, RhiBufferUsage.Vertex | RhiBufferUsage.CopyDestination, "terrain-arena.vertices"));
            _chunkBuffer = _device.CreateBuffer(new RhiBufferDescriptor(
                (ulong)_chunkOf.Length * sizeof(uint), RhiBufferUsage.Vertex | RhiBufferUsage.CopyDestination, "terrain-arena.chunks"));
            _dirtyVertexStart = 0;
            _dirtyVertexEnd = _vertexTop;
        }

        if (_dirtyVertexEnd > _dirtyVertexStart)
        {
            int start = _dirtyVertexStart;
            int length = _dirtyVertexEnd - start;
            _device.WriteBuffer(_vertexBuffer, (ulong)start * Vertex3D.Size, MemoryMarshal.AsBytes(_vertices.AsSpan(start, length)));
            _device.WriteBuffer(_chunkBuffer!, (ulong)start * sizeof(uint), MemoryMarshal.AsBytes(_chunkOf.AsSpan(start, length)));
        }

        _dirtyVertexStart = int.MaxValue;
        _dirtyVertexEnd = 0;

        // The table: a record a slot, then the records.
        ulong recordOfBytes = (ulong)_recordOf.Length * sizeof(uint);
        if (_recordOfBuffer is null || _recordOfBuffer.SizeInBytes < recordOfBytes)
        {
            Retire(_recordOfBuffer);
            _recordOfBuffer = _device.CreateBuffer(new RhiBufferDescriptor(
                recordOfBytes, RhiBufferUsage.Storage | RhiBufferUsage.CopyDestination, "terrain-arena.record-of"));
            regroup = true;
        }

        ulong recordBytes = (ulong)_records.Length * sizeof(float);
        if (_recordBuffer is null || _recordBuffer.SizeInBytes < recordBytes)
        {
            Retire(_recordBuffer);
            _recordBuffer = _device.CreateBuffer(new RhiBufferDescriptor(
                recordBytes, RhiBufferUsage.Storage | RhiBufferUsage.CopyDestination, "terrain-arena.records"));
            regroup = true;
        }

        _device.WriteBuffer(_recordOfBuffer, 0, MemoryMarshal.AsBytes(_recordOf.AsSpan(0, Math.Max(_slotTop, 1))));
        if (_recordCount > 0)
        {
            _device.WriteBuffer(_recordBuffer, 0, MemoryMarshal.AsBytes(_records.AsSpan(0, _recordCount * RecordFloats)));
        }

        foreach (Bucket bucket in _bucketList)
        {
            bucket.Upload(_device, _retired);
        }

        if (regroup)
        {
            Retire(_group);
            _group = _device.CreateBindGroup(new RhiBindGroupDescriptor(
                _layout,
                [
                    RhiBindGroupEntry.Of(0, _recordOfBuffer, 0, _recordOfBuffer.SizeInBytes),
                    RhiBindGroupEntry.Of(1, _recordBuffer, 0, _recordBuffer.SizeInBytes),
                ],
                "terrain-arena"));
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
        _group?.Dispose();
        _vertexBuffer?.Dispose();
        _chunkBuffer?.Dispose();
        _recordOfBuffer?.Dispose();
        _recordBuffer?.Dispose();
        foreach (Bucket bucket in _bucketList)
        {
            bucket.Buffer?.Dispose();
        }
    }

    private void Retire(IDisposable? resource)
    {
        if (resource is null)
        {
            return;
        }

        if (_retired is { } retired)
        {
            retired.Retire(resource);
        }
        else
        {
            resource.Dispose();
        }
    }

    private int TakeBlock(int sizeClass)
    {
        if (_freeBlocks[sizeClass] is { Count: > 0 } free)
        {
            int reused = free[^1];
            free.RemoveAt(free.Count - 1);
            return reused;
        }

        int size = 1 << sizeClass;
        int start = _vertexTop;
        _vertexTop += size;
        if (_vertexTop > _vertices.Length)
        {
            int grown = Math.Max(_vertices.Length * 2, _vertexTop);
            Array.Resize(ref _vertices, grown);
            Array.Resize(ref _chunkOf, grown);
        }

        return start;
    }

    private void MarkVertices(int start, int count)
    {
        _dirtyVertexStart = Math.Min(_dirtyVertexStart, start);
        _dirtyVertexEnd = Math.Max(_dirtyVertexEnd, start + count);
    }

    private Bucket BucketFor(int key)
    {
        if (!_buckets.TryGetValue(key, out Bucket? bucket))
        {
            bucket = new Bucket(key);
            _buckets[key] = bucket;
            _bucketList.Add(bucket);
        }

        return bucket;
    }

    // Packs a bucket's live segments to the front of its list, in the order they sit, and points each chunk at its own.
    private void Compact(Bucket bucket)
    {
        List<(int Slot, int Index, int Start)> live = [];
        for (int slot = 0; slot < _slotTop; slot++)
        {
            if (_slots[slot].Segments is not { } segments)
            {
                continue;
            }

            for (int i = 0; i < segments.Length; i++)
            {
                if (ReferenceEquals(segments[i].Bucket, bucket))
                {
                    live.Add((slot, i, segments[i].Start));
                }
            }
        }

        live.Sort((left, right) => left.Start.CompareTo(right.Start));

        uint[] packed = new uint[Math.Max(bucket.Indices.Length / 2, 64)];
        int at = 0;
        foreach ((int slot, int index, int start) in live)
        {
            Segment segment = _slots[slot].Segments![index];
            if (at + segment.Count > packed.Length)
            {
                Array.Resize(ref packed, Math.Max(packed.Length * 2, at + segment.Count));
            }

            bucket.Indices.AsSpan(start, segment.Count).CopyTo(packed.AsSpan(at));
            _slots[slot].Segments![index] = segment with { Start = at };
            at += segment.Count;
        }

        bucket.Replace(packed, at);
    }

    /// <summary>One chunk in the arena: its block of vertices and where each of its layers is filed.</summary>
    private record struct Slot(int Start, int SizeClass, Segment[]? Segments);

    /// <summary>One layer's triangles in a bucket's list.</summary>
    private readonly record struct Segment(Bucket Bucket, int Start, int Count);

    /// <summary>One bucket's index list, on the host and on the device.</summary>
    private sealed class Bucket(int key)
    {
        private int _dirtyStart = int.MaxValue;
        private int _dirtyEnd;

        public int Key { get; } = key;

        public uint[] Indices { get; private set; } = new uint[1024];

        public int Count { get; private set; }

        public int Holes { get; private set; }

        public IRhiBuffer? Buffer { get; private set; }

        public int Append(ReadOnlySpan<ushort> indices, uint vertexBase)
        {
            int start = Count;
            if (start + indices.Length > Indices.Length)
            {
                uint[] grown = Indices;
                Array.Resize(ref grown, Math.Max(Indices.Length * 2, start + indices.Length));
                Indices = grown;
            }

            Span<uint> into = Indices.AsSpan(start, indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                into[i] = indices[i] + vertexBase;
            }

            Count += indices.Length;
            Mark(start, indices.Length);
            return start;
        }

        // Every index of the range to zero: three alike, so each triangle has no area and is not drawn.
        public void Erase(int start, int count)
        {
            Indices.AsSpan(start, count).Clear();
            Holes += count;
            Mark(start, count);
        }

        public void Replace(uint[] indices, int count)
        {
            // The ranges marked before belong to the old list, which may have run past this one's end: the new list is
            // what goes up, all of it.
            Indices = indices;
            Count = count;
            Holes = 0;
            _dirtyStart = 0;
            _dirtyEnd = count;
        }

        public void Upload(IRhiDevice device, RetiredResources? retired)
        {
            ulong bytes = (ulong)Indices.Length * sizeof(uint);
            if (Buffer is null || Buffer.SizeInBytes < bytes)
            {
                if (Buffer is { } old)
                {
                    if (retired is not null)
                    {
                        retired.Retire(old);
                    }
                    else
                    {
                        old.Dispose();
                    }
                }

                Buffer = device.CreateBuffer(new RhiBufferDescriptor(
                    bytes, RhiBufferUsage.Index | RhiBufferUsage.CopyDestination, $"terrain-arena.bucket-{Key}"));
                _dirtyStart = 0;
                _dirtyEnd = Count;
            }

            if (_dirtyEnd > _dirtyStart)
            {
                device.WriteBuffer(
                    Buffer,
                    (ulong)_dirtyStart * sizeof(uint),
                    MemoryMarshal.AsBytes(Indices.AsSpan(_dirtyStart, _dirtyEnd - _dirtyStart)));
            }

            _dirtyStart = int.MaxValue;
            _dirtyEnd = 0;
        }

        private void Mark(int start, int count)
        {
            _dirtyStart = Math.Min(_dirtyStart, start);
            _dirtyEnd = Math.Max(_dirtyEnd, start + count);
        }
    }
}
