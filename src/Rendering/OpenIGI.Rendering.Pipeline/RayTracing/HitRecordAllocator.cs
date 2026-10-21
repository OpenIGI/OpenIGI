using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>The traced world's material records: the buffer a ray shader looks a hit up in, and who owns which run of it.</summary>
/// <remarks>
/// <para>
/// One record per geometry of every live structure, in the layout the ray shader reads them through the
/// buffer's shader handle. A structure rebuilt over a larger range set is given a fresh run of records
/// rather than overwriting the run a frame in flight may still be reading, which is the whole of the
/// synchronisation this needs.
/// </para>
/// <para>
/// A run given back is held for a few frames before it is handed out again; see <see cref="Release"/> and
/// <see cref="Reclaim"/>. Without that the records were append-only: every terrain page streamed in took
/// one for good, so flying across a large level ran them out, and every structure built after that was
/// pointed at record nought -- some other mesh's vertices, indices and cut-out mask. Its rays read the wrong
/// mask (a tree's whole card cast a shadow) and indexed past the end of the wrong index buffer (a device
/// fault).
/// </para>
/// </remarks>
internal sealed class HitRecordAllocator : IDisposable
{
    // One per geometry of every live structure. A run a rebuild or an eviction leaves behind goes back
    // to be reused once no frame in flight can still read it; see Release.
    private const int MaxRecords = 16384;

    // How many frames a released run of records is held before it is handed out again: past the frames
    // in flight and the scene's own retirement slack, so no trace still running can read a record that
    // has since been rewritten for another mesh.
    private const int RecordRetireFrames = 8;

    // Two shader handles (vertices, indices), a first index, a mask layer, an albedo layer and the flags.
    private const int RecordSize = 32;

    private readonly IRhiDevice _device;
    private readonly IOpenIgiLogger _logger;

    // The material records, one per geometry of every built structure, in the layout the ray shader
    // reads them through the buffer's shader handle. Append-only: a structure rebuilt over a larger range set is
    // given a fresh run of records rather than overwriting the run a frame in flight may still be
    // reading, which is the whole of the synchronisation this needs.
    private readonly IRhiBuffer _records;
    private int _recordCount;
    private bool _recordOverflow;

    // Runs of records free to hand out again, in address order with neighbours merged, and the runs
    // released but not yet old enough to reuse, each with the frame it may be reused from. Without these
    // the records were append-only: every terrain page streamed in took one for good, so flying across a
    // large level ran them out, and every structure built after that was pointed at record nought -- some
    // other mesh's vertices, indices and cut-out mask. Its rays read the wrong mask (a tree's whole card
    // cast a shadow) and indexed past the end of the wrong index buffer (a device fault).
    private readonly List<(int Start, int Count)> _freeRecords = [];
    private readonly List<(int Frame, int Start, int Count)> _releasedRecords = [];

    /// <summary>Initialises a new instance of the <see cref="HitRecordAllocator"/> class.</summary>
    /// <param name="device">The device the records live on.</param>
    /// <param name="logger">Where running out of records is said.</param>
    public HitRecordAllocator(IRhiDevice device, IOpenIgiLogger logger)
    {
        _device = device;
        _logger = logger;

        // Host visible and written directly. The records are tiny, they change only when a structure is
        // built, and the alternative -- staging into device-local memory -- would need a transfer
        // recorded and fenced for what amounts to thirty-two bytes.
        _records = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(MaxRecords * RecordSize),
            RhiBufferUsage.RayTracingInput | RhiBufferUsage.MapWrite,
            "traced world hit records"));
    }

    /// <summary>Gets the handle of the material records a ray shader looks a hit up in.</summary>
    public ulong Handle => _records.ShaderHandle;

    /// <summary>Rewrites the flag word of one record where it stands, leaving the rest of it alone.</summary>
    /// <param name="record">The record's index.</param>
    /// <param name="flagWord">The range's new flag word.</param>
    /// <remarks>
    /// For a run whose glow alone changed -- a lamp switched, faded, or first seen while its photocell had
    /// it off. Not a new run, so not a new record: the tube reflects the state it is in now rather than the
    /// one it happened to be in the first frame it was drawn.
    /// </remarks>
    public void RewriteFlag(int record, uint flagWord)
    {
        ReadOnlySpan<uint> flag = [flagWord];
        _device.WriteBuffer(_records, (ulong)((record * RecordSize) + (7 * sizeof(uint))), flag);
    }

    /// <summary>Appends one material record per range and returns where the run starts.</summary>
    /// <param name="mesh">The mesh the ranges belong to.</param>
    /// <param name="surfaces">The ranges, in geometry order.</param>
    /// <returns>
    /// The index of the first record, which is what the instance carries as its custom index, or -1 where
    /// there are none left to give.
    /// </returns>
    public int Write(IRayTracedMesh mesh, List<RayTracedSurface> surfaces)
    {
        int start = Allocate(surfaces.Count);
        if (start < 0)
        {
            // Out of records. The caller leaves the mesh out of the traced world: built against records
            // that are not its own, its rays would read another mesh's buffers. Said once, because a
            // missing shadow would otherwise look like a different bug.
            if (!_recordOverflow)
            {
                _recordOverflow = true;
                _logger.Warning(
                    "RayTracing",
                    $"Material records exhausted at {MaxRecords}; meshes admitted after this point are left out of the traced world.");
            }

            return -1;
        }

        Span<uint> record = stackalloc uint[RecordSize / sizeof(uint)];

        for (int i = 0; i < surfaces.Count; i++)
        {
            RayTracedSurface s = surfaces[i];
            ulong vertices = mesh.Vertices.ShaderHandle;
            ulong indices = mesh.Indices.ShaderHandle;

            record[0] = (uint)vertices;
            record[1] = (uint)(vertices >> 32);
            record[2] = (uint)indices;
            record[3] = (uint)(indices >> 32);
            record[4] = (uint)s.IndexOffset;
            record[5] = unchecked((uint)s.MaskLayer);
            record[6] = unchecked((uint)s.AlbedoLayer);
            record[7] = s.FlagWord;

            _device.WriteBuffer(_records, (ulong)((start + i) * RecordSize), (ReadOnlySpan<uint>)record);
        }

        return start;
    }

    /// <summary>Takes a run of records: the first free run long enough, else fresh ones at the end.</summary>
    /// <returns>Where the run starts, or -1 where there is no room.</returns>
    private int Allocate(int count)
    {
        for (int i = 0; i < _freeRecords.Count; i++)
        {
            (int start, int length) = _freeRecords[i];
            if (length < count)
            {
                continue;
            }

            if (length == count)
            {
                _freeRecords.RemoveAt(i);
            }
            else
            {
                _freeRecords[i] = (start + count, length - count);
            }

            return start;
        }

        if (_recordCount + count > MaxRecords)
        {
            return -1;
        }

        int fresh = _recordCount;
        _recordCount += count;
        return fresh;
    }

    /// <summary>Gives a run of records back, to be handed out again once no frame in flight can read it.</summary>
    /// <param name="start">Where the run starts.</param>
    /// <param name="count">How many records it holds; nought gives nothing back.</param>
    /// <param name="frame">The scene's frame the run is released on.</param>
    public void Release(int start, int count, int frame)
    {
        if (count > 0)
        {
            _releasedRecords.Add((frame + RecordRetireFrames, start, count));
        }
    }

    /// <summary>Moves the released runs old enough to reuse onto the free list, merging neighbours.</summary>
    /// <param name="frame">The scene's frame, already advanced.</param>
    public void Reclaim(int frame)
    {
        for (int i = _releasedRecords.Count - 1; i >= 0; i--)
        {
            (int releasedFrame, int start, int count) = _releasedRecords[i];
            if (releasedFrame > frame)
            {
                continue;
            }

            _releasedRecords.RemoveAt(i);

            int at = 0;
            while (at < _freeRecords.Count && _freeRecords[at].Start < start)
            {
                at++;
            }

            _freeRecords.Insert(at, (start, count));

            if (at + 1 < _freeRecords.Count && start + count == _freeRecords[at + 1].Start)
            {
                _freeRecords[at] = (start, count + _freeRecords[at + 1].Count);
                _freeRecords.RemoveAt(at + 1);
            }

            if (at > 0 && _freeRecords[at - 1].Start + _freeRecords[at - 1].Count == start)
            {
                _freeRecords[at - 1] = (_freeRecords[at - 1].Start, _freeRecords[at - 1].Count + _freeRecords[at].Count);
                _freeRecords.RemoveAt(at);
            }
        }

        // A free run that ends where the used span does goes back to the span, so fresh allocations at
        // the end stay as cheap as they were before anything was released.
        if (_freeRecords.Count > 0 && _freeRecords[^1].Start + _freeRecords[^1].Count == _recordCount)
        {
            _recordCount = _freeRecords[^1].Start;
            _freeRecords.RemoveAt(_freeRecords.Count - 1);
        }
    }

    /// <summary>Forgets every run, for a level change: the next record handed out is the first.</summary>
    public void Reset()
    {
        _recordCount = 0;
        _recordOverflow = false;
        _freeRecords.Clear();
        _releasedRecords.Clear();
    }

    /// <inheritdoc/>
    public void Dispose() => _records.Dispose();
}
