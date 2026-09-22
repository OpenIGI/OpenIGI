using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>
/// The per-frame vertex and index storage the 2D path appends into, one ring per frame in flight.
/// </summary>
/// <remarks>
/// <para>
/// Two-dimensional drawing is immediate: each call brings its own short vertex and index spans rather
/// than a mesh uploaded once. A buffer per call would exhaust an allocation budget within a frame, so
/// each frame in flight owns one vertex and one index buffer and every draw appends at a running
/// offset. A draw records the offset it wrote at, so the buffers are written once and bound many times.
/// </para>
/// <para>
/// The bytes are gathered on the host and uploaded once per frame rather than per append. That is the
/// difference from the mapped Vulkan ring this replaces, and it is the portable shape: the RHI has no
/// persistent mapping because a browser has none, and its queue write is the upload path both backends
/// share. One write per frame rather than one per draw is also the cheaper of the two everywhere.
/// </para>
/// <para>
/// Each frame in flight having its own pair is what keeps the host filling frame N+1 off the buffers
/// the GPU is still reading for frame N.
/// </para>
/// </remarks>
public sealed class SpriteGeometryRing : IDisposable
{
    /// <summary>How many vertices one frame may draw. Sized for a heavy HUD-and-menu frame with headroom.</summary>
    public const int VertexCapacity = 128 * 1024;

    /// <summary>How many indices one frame may draw.</summary>
    public const int IndexCapacity = 256 * 1024;

    private readonly Ring[] _rings;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="SpriteGeometryRing"/> class.</summary>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="framesInFlight">How many rings to keep — one per frame in flight.</param>
    public SpriteGeometryRing(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _rings = new Ring[framesInFlight];

        for (int i = 0; i < framesInFlight; i++)
        {
            _rings[i] = new Ring(device, i);
        }
    }

    /// <summary>Gets how many rings there are, which is how many frames may be in flight.</summary>
    public int FramesInFlight => _rings.Length;

    /// <summary>Empties a frame's ring, ready to be appended into.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    public void Reset(int frameIndex) => _rings[frameIndex].Reset();

    /// <summary>Gets the vertex buffer of a frame's ring, to bind before drawing.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <returns>The vertex buffer.</returns>
    public IRhiBuffer VertexBuffer(int frameIndex) => _rings[frameIndex].Vertices;

    /// <summary>Gets the index buffer of a frame's ring, to bind before drawing.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <returns>The index buffer.</returns>
    public IRhiBuffer IndexBuffer(int frameIndex) => _rings[frameIndex].Indices;

    /// <summary>Appends a draw's vertices and indices into a frame's ring.</summary>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    /// <param name="vertices">The vertices to append.</param>
    /// <param name="indices">The indices, addressing <paramref name="vertices"/> from zero.</param>
    /// <param name="baseVertex">Receives the value to add to every index of the draw.</param>
    /// <param name="firstIndex">Receives the first index of the draw.</param>
    /// <exception cref="GraphicsDeviceException">The ring cannot hold the appended geometry.</exception>
    public void Append(
        int frameIndex,
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        out int baseVertex,
        out int firstIndex) =>
        _rings[frameIndex].Append(vertices, indices, out baseVertex, out firstIndex);

    /// <summary>Uploads what a frame appended, which must happen before its commands are submitted.</summary>
    /// <param name="device">The device to write through.</param>
    /// <param name="frameIndex">The frame-in-flight index.</param>
    public void Flush(IRhiDevice device, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(device);
        _rings[frameIndex].Flush(device);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (Ring ring in _rings)
        {
            ring.Dispose();
        }
    }

    private sealed class Ring : IDisposable
    {
        private readonly Vertex2D[] _vertices = new Vertex2D[VertexCapacity];
        private readonly ushort[] _indices = new ushort[IndexCapacity];
        private int _vertexCount;
        private int _indexCount;

        public Ring(IRhiDevice device, int frame)
        {
            Vertices = device.CreateBuffer(new RhiBufferDescriptor(
                (ulong)(VertexCapacity * Vertex2D.Size),
                RhiBufferUsage.Vertex | RhiBufferUsage.CopyDestination,
                $"sprite2d-vertices-{frame}"));

            Indices = device.CreateBuffer(new RhiBufferDescriptor(
                (ulong)(IndexCapacity * sizeof(ushort)),
                RhiBufferUsage.Index | RhiBufferUsage.CopyDestination,
                $"sprite2d-indices-{frame}"));
        }

        public IRhiBuffer Vertices { get; }

        public IRhiBuffer Indices { get; }

        public void Reset()
        {
            _vertexCount = 0;
            _indexCount = 0;
        }

        public void Append(
            ReadOnlySpan<Vertex2D> vertices,
            ReadOnlySpan<ushort> indices,
            out int baseVertex,
            out int firstIndex)
        {
            if (_vertexCount + vertices.Length > VertexCapacity || _indexCount + indices.Length > IndexCapacity)
            {
                throw new GraphicsDeviceException(
                    "The 2D geometry ring is full; a single frame drew more than it is sized for.");
            }

            baseVertex = _vertexCount;
            firstIndex = _indexCount;

            vertices.CopyTo(_vertices.AsSpan(_vertexCount));
            indices.CopyTo(_indices.AsSpan(_indexCount));

            _vertexCount += vertices.Length;
            _indexCount += indices.Length;
        }

        public void Flush(IRhiDevice device)
        {
            if (_vertexCount == 0)
            {
                return;
            }

            device.WriteBuffer<Vertex2D>(Vertices, 0, _vertices.AsSpan(0, _vertexCount));

            // The index count is rounded up to a pair, because both backends require a buffer write to
            // be a multiple of four bytes and an index is two.
            int indices = (_indexCount + 1) & ~1;
            device.WriteBuffer<ushort>(Indices, 0, _indices.AsSpan(0, indices));
        }

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
        }
    }
}
