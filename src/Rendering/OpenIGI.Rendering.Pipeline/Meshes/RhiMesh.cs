using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Meshes;

/// <summary>A mesh's vertex and index buffers, in one of the three shapes the renderer asks for.</summary>
/// <remarks>
/// <para>
/// A <em>static</em> mesh is immutable: its vertices and indices are written once and never change,
/// which is what most of the world is. A <em>storage</em> mesh is allocated to a capacity and filled by
/// appending ranges — the terrain's growing pages. An <em>animated</em> mesh's vertices are rewritten
/// each pose.
/// </para>
/// <para>
/// Where the memory sits is no longer decided here. The RHI places a buffer from what it was created
/// for, which is the whole reason that use is declared up front, and each backend knows what its own
/// driver wants. What does stay here is the pose ring, because that is not a memory question.
/// </para>
/// </remarks>
public sealed class RhiMesh : IDisposable
{
    /// <summary>How many pose buffers an animated mesh rings through.</summary>
    /// <remarks>
    /// Two more than the frames in flight. A frame binds one (current, previous) pair, so the frames
    /// that have not retired hold at most three distinct slots between them and a fourth is always free
    /// for the next tick to write. Overridable for the A/B that proves the hazard: at two the ring
    /// aliases exactly as the old pair did.
    /// </remarks>
    public static readonly int PoseRingLength = ResolvePoseRingLength();

    /// <summary>How many frames may be in flight, which is what makes a pose slot free again.</summary>
    public const int MaxFramesInFlight = 2;

    private static long _poseWriteHazards;

    private readonly IRhiDevice _device;
    private readonly IRhiBuffer _vertices;
    private readonly IRhiBuffer _indices;

    // An animated mesh's poses live in a ring rather than a pair. A pose write must never land on
    // memory a frame still in flight is reading: the GPU is up to MaxFramesInFlight frames behind the
    // host, and a body's two streams stay bound for every frame between two ticks. OpenGL got this for
    // free -- it re-specified the buffer, which orphans the old storage and lets the driver retire it
    // once the GPU is done -- and the port dropped that, writing the new pose over vertices an
    // unretired frame was still fetching. The ring is the explicit form of the same idea: a slot is
    // written only once every frame that bound it has retired, so the previous pose a frame reads stays
    // the pose it was recorded with. Null for a static or storage mesh, whose vertices are either
    // immutable or appended under their own versioning.
    private readonly IRhiBuffer[]? _poseRing;
    private readonly ulong[]? _poseSlotLastBoundFrame;

    // A storage mesh's indices as the host last wrote them. Both backends move whole words and an index
    // is half of one, so a range that starts or ends on an odd index has to be widened -- and widening
    // it means writing the neighbours too. Without a copy of what they hold, that write would zero an
    // index belonging to the page next door.
    private readonly ushort[]? _indexShadow;
    private int _frontSlot;
    private int _previousSlot;
    private bool _disposed;

    private RhiMesh(
        IRhiDevice device,
        int vertexCapacity,
        int indexCapacity,
        bool animated,
        bool storage,
        string label)
    {
        _device = device;
        VertexCapacity = vertexCapacity;
        IndexCapacity = indexCapacity;
        Animated = animated;
        Storage = storage;
        Label = label;

        // Rounded up to a pair: an index is two bytes, both backends move whole words, and a mesh with
        // an odd index count is ordinary. The extra slot is past anything ever drawn.
        int indexSlots = (indexCapacity + 1) & ~1;

        _indexShadow = storage ? new ushort[indexSlots] : null;
        _vertices = CreateVertexBuffer(device, vertexCapacity, label);
        _indices = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(indexSlots * sizeof(ushort)),
            RhiBufferUsage.Index | RhiBufferUsage.CopyDestination | RhiBufferUsage.RayTracingInput,
            $"{label}.indices"));

        if (!animated)
        {
            return;
        }

        // Slot zero is the mesh's own vertex buffer, so everything that addresses a mesh by that buffer
        // keeps working; the rest of the ring is allocated beside it.
        _poseRing = new IRhiBuffer[PoseRingLength];
        _poseRing[0] = _vertices;

        for (int slot = 1; slot < _poseRing.Length; slot++)
        {
            _poseRing[slot] = CreateVertexBuffer(device, vertexCapacity, $"{label}.pose{slot}");
        }

        _poseSlotLastBoundFrame = new ulong[_poseRing.Length];
        _frontSlot = 0;
        _previousSlot = _poseRing.Length - 1;
    }

    /// <summary>Gets how many pose writes had to land on a slot a frame in flight could still read.</summary>
    /// <remarks>
    /// Zero on a correct run. It rises only when the ring cannot offer a retired slot, which needs the
    /// frame rate to fall far enough that several ticks pass inside one unretired frame.
    /// </remarks>
    public static long PoseWriteHazards => Interlocked.Read(ref _poseWriteHazards);

    /// <summary>Gets the name the buffers were created with.</summary>
    public string Label { get; }

    /// <summary>Gets how many vertices the mesh can ever hold.</summary>
    public int VertexCapacity { get; }

    /// <summary>Gets how many indices the mesh can ever hold.</summary>
    public int IndexCapacity { get; }

    /// <summary>Gets how many indices are worth drawing, which a storage mesh grows.</summary>
    public int DrawableIndexCount { get; private set; }

    /// <summary>Gets whether the vertices are rewritten each pose.</summary>
    public bool Animated { get; }

    /// <summary>Gets whether the mesh is an appendable storage mesh.</summary>
    public bool Storage { get; }

    /// <summary>Gets how many times the contents have changed, for anything caching from them.</summary>
    public int ContentVersion { get; private set; }

    /// <summary>Gets the vertex buffer this frame draws from.</summary>
    public IRhiBuffer VertexBuffer => _poseRing is null ? _vertices : _poseRing[_frontSlot];

    /// <summary>Gets the vertex buffer holding the pose before this one, for the velocity raster.</summary>
    public IRhiBuffer PreviousVertexBuffer => _poseRing is null ? _vertices : _poseRing[_previousSlot];

    /// <summary>Gets the index buffer.</summary>
    public IRhiBuffer IndexBuffer => _indices;

    /// <summary>Creates a mesh whose vertices and indices never change.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="vertices">The vertices.</param>
    /// <param name="indices">The indices.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>The mesh.</returns>
    public static RhiMesh CreateStatic(
        IRhiDevice device,
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices,
        string label = "mesh")
    {
        ArgumentNullException.ThrowIfNull(device);

        RhiMesh mesh = new(device, vertices.Length, indices.Length, animated: false, storage: false, label);

        device.WriteBuffer(mesh._vertices, 0, vertices);
        device.WriteBuffer(mesh._indices, 0, PadToPair(indices));

        mesh.DrawableIndexCount = indices.Length;
        mesh.ContentVersion = 1;
        return mesh;
    }

    /// <summary>Creates a mesh whose vertices are replaced each pose.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="vertices">The first pose, which fixes the vertex count.</param>
    /// <param name="indices">The indices, which do not change.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>The mesh.</returns>
    public static RhiMesh CreateAnimated(
        IRhiDevice device,
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices,
        string label = "mesh")
    {
        ArgumentNullException.ThrowIfNull(device);

        RhiMesh mesh = new(device, vertices.Length, indices.Length, animated: true, storage: false, label);

        // Every slot starts at the first pose, so a frame that reads the previous stream before a second
        // pose has been written reads the same pose rather than an empty buffer.
        foreach (IRhiBuffer slot in mesh._poseRing!)
        {
            device.WriteBuffer(slot, 0, vertices);
        }

        device.WriteBuffer(mesh._indices, 0, PadToPair(indices));

        mesh.DrawableIndexCount = indices.Length;
        mesh.ContentVersion = 1;
        return mesh;
    }

    /// <summary>Creates a mesh of a fixed capacity, to be filled by appending ranges.</summary>
    /// <param name="device">The device the mesh lives on.</param>
    /// <param name="vertexCapacity">How many vertices it can ever hold.</param>
    /// <param name="indexCapacity">How many indices it can ever hold.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    /// <returns>The mesh, drawing nothing until a range is uploaded.</returns>
    public static RhiMesh CreateStorage(
        IRhiDevice device,
        int vertexCapacity,
        int indexCapacity,
        string label = "mesh")
    {
        ArgumentNullException.ThrowIfNull(device);
        return new RhiMesh(device, vertexCapacity, indexCapacity, animated: false, storage: true, label);
    }

    /// <summary>Replaces an animated mesh's vertices.</summary>
    /// <param name="vertices">The new pose. Must be as many vertices as the mesh was created with.</param>
    /// <param name="frameCounter">The frame counter, which decides the ring slot.</param>
    /// <exception cref="GraphicsDeviceException">The mesh is not animated or the count differs.</exception>
    /// <remarks>
    /// Into a slot no unretired frame can still be fetching, then the ring moves forward: what was drawn
    /// as the current pose becomes the previous-pose stream without a copy, which is what gives the
    /// velocity raster the real old vertices for skinned motion.
    /// </remarks>
    public void UpdateVertices(ReadOnlySpan<Vertex3D> vertices, ulong frameCounter)
    {
        if (!Animated)
        {
            throw new GraphicsDeviceException("Only an animated mesh can have its vertices replaced.");
        }

        if (vertices.Length != VertexCapacity)
        {
            throw new GraphicsDeviceException(
                $"An animated mesh takes exactly {VertexCapacity} vertices but got {vertices.Length}.");
        }

        int chosen = ChoosePoseSlot(frameCounter);

        _device.WriteBuffer(_poseRing![chosen], 0, vertices);
        _previousSlot = _frontSlot;
        _frontSlot = chosen;
        ContentVersion++;
    }

    /// <summary>Writes a range of vertices and indices into a storage mesh.</summary>
    /// <param name="vertexOffset">The first vertex slot written.</param>
    /// <param name="vertices">The vertices to write; may be empty.</param>
    /// <param name="indexOffset">The first index slot written.</param>
    /// <param name="indices">The indices to write; may be empty.</param>
    /// <exception cref="GraphicsDeviceException">A range runs past its capacity.</exception>
    public void UploadRange(
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        if (vertexOffset < 0 || vertexOffset + vertices.Length > VertexCapacity)
        {
            throw new GraphicsDeviceException("A vertex range runs past the storage mesh's capacity.");
        }

        if (indexOffset < 0 || indexOffset + indices.Length > IndexCapacity)
        {
            throw new GraphicsDeviceException("An index range runs past the storage mesh's capacity.");
        }

        if (!vertices.IsEmpty)
        {
            _device.WriteBuffer(_vertices, (ulong)(vertexOffset * Vertex3D.Size), vertices);
        }

        if (!indices.IsEmpty)
        {
            WriteIndicesAt(indexOffset, indices);
        }

        DrawableIndexCount = Math.Max(DrawableIndexCount, indexOffset + indices.Length);
        ContentVersion++;
    }

    /// <summary>Records that the frame being built has bound this mesh's two pose streams.</summary>
    /// <param name="frameCounter">The frame counter of the frame doing the binding.</param>
    /// <remarks>
    /// What keeps a pose write off memory the GPU is still reading. Stamped on both live slots, since a
    /// frame fetches from both, and read back by <see cref="UpdateVertices"/> to tell a retired slot
    /// from one an unretired frame still refers to.
    /// </remarks>
    public void MarkPoseSlotsBound(ulong frameCounter)
    {
        if (_poseSlotLastBoundFrame is null)
        {
            return;
        }

        // Stamped one past the frame, so that nought keeps its meaning: never bound at all. Without the
        // offset a slot bound by frame nought is indistinguishable from one no frame has ever touched,
        // and every slot of a freshly created mesh reads as still in flight.
        _poseSlotLastBoundFrame[_frontSlot] = frameCounter + 1;
        _poseSlotLastBoundFrame[_previousSlot] = frameCounter + 1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _indices.Dispose();

        if (_poseRing is not null)
        {
            // Slot zero is the vertex buffer, disposed below with every other mesh's.
            for (int slot = 1; slot < _poseRing.Length; slot++)
            {
                _poseRing[slot].Dispose();
            }
        }

        _vertices.Dispose();
    }

    private static int ResolvePoseRingLength()
    {
        string? configured = Environment.GetEnvironmentVariable("OPENIGI_VK_POSE_RING");
        return int.TryParse(configured, out int length) && length >= 2 && length <= 8
            ? length
            : MaxFramesInFlight + 2;
    }

    private static IRhiBuffer CreateVertexBuffer(IRhiDevice device, int capacity, string label) =>
        device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(capacity * Vertex3D.Size),
            RhiBufferUsage.Vertex | RhiBufferUsage.CopyDestination | RhiBufferUsage.RayTracingInput,
            label));

    /// <summary>Rounds a run of indices up to a pair, because a buffer write moves whole words.</summary>
    /// <remarks>
    /// The extra index is past what is ever drawn, so what it holds does not matter. Only for a write
    /// that starts at zero and covers everything; a partial write goes through the shadow instead.
    /// </remarks>
    private static ReadOnlySpan<ushort> PadToPair(ReadOnlySpan<ushort> indices)
    {
        if (indices.Length % 2 == 0)
        {
            return indices;
        }

        ushort[] padded = new ushort[indices.Length + 1];
        indices.CopyTo(padded);
        return padded;
    }

    /// <summary>Picks the ring slot this pose may be written into.</summary>
    /// <param name="frameCounter">The frame counter.</param>
    /// <returns>A slot that is neither live pose and that no unretired frame bound.</returns>
    /// <remarks>
    /// The current pose is never a candidate: the flip is about to make it the previous stream. Of the
    /// rest, a slot is free once every frame that bound it has retired, which is
    /// <see cref="MaxFramesInFlight"/> frames behind. If the frame rate has fallen far enough that
    /// several ticks pass inside one unretired frame, no slot is free; the least recently bound one is
    /// taken and the write is counted, because tearing one pose is a better failure than refusing to
    /// animate.
    /// </remarks>
    private int ChoosePoseSlot(ulong frameCounter)
    {
        IRhiBuffer[] ring = _poseRing!;
        ulong[] lastBound = _poseSlotLastBoundFrame!;
        int oldest = -1;

        for (int step = 1; step <= ring.Length; step++)
        {
            int slot = (_frontSlot + step) % ring.Length;

            if (slot == _frontSlot)
            {
                continue;
            }

            // Nought means no frame has ever bound it, so it is free whatever the counter says.
            if (lastBound[slot] == 0 || lastBound[slot] + MaxFramesInFlight <= frameCounter + 1)
            {
                return slot;
            }

            if (oldest < 0 || lastBound[slot] < lastBound[oldest])
            {
                oldest = slot;
            }
        }

        Interlocked.Increment(ref _poseWriteHazards);
        return oldest;
    }

    /// <summary>Writes indices at a slot offset, keeping the write word-aligned either side.</summary>
    /// <remarks>
    /// A range starting or ending on an odd index is widened to the word either side, and the
    /// neighbours it then covers come from the shadow — which is what they already hold. Widening
    /// without the shadow would zero an index belonging to the page next door, and the page would lose
    /// a triangle to a neighbour's upload.
    /// </remarks>
    private void WriteIndicesAt(int indexOffset, ReadOnlySpan<ushort> indices)
    {
        ushort[] shadow = _indexShadow!;
        indices.CopyTo(shadow.AsSpan(indexOffset));

        int start = indexOffset & ~1;
        int end = Math.Min(shadow.Length, (indexOffset + indices.Length + 1) & ~1);

        _device.WriteBuffer<ushort>(_indices, (ulong)(start * sizeof(ushort)), shadow.AsSpan(start, end - start));
    }
}
