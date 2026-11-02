using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// The terrain as the ray-traced world should see it this frame: the chunks that were drawn, at the
/// positions the vertex stage drew them, in a mesh of their own.
/// </summary>
/// <remarks>
/// <para>
/// The batch pages the terrain is rasterised from are the wrong thing to trace. A page keeps every
/// chunk ever streamed into it at its level of detail for as long as the cache retains it, the same
/// ground is held again by the pages of the neighbouring levels, and the vertex stage slides each
/// vertex towards where it sits at the next level by an amount that depends on the camera. A structure
/// built over the pages therefore held two or three copies of every hill, none of them where the hill
/// was on screen, and the sun's shadow rays hit the copies: the ground shadowed itself in sharp,
/// chunk-shaped patches wherever a copy stood above the drawn surface.
/// </para>
/// <para>
/// So the caster is rebuilt from the descent. Every chunk it emits contributes its base layer, each vertex
/// moved by the same morph the shader applies — the ramp on the target position, the Chebyshev distance
/// from the camera, the offset and slope the terrain's fade window gives — so where it is on screen the
/// caster is the drawn surface to the precision of a float. The descent is run without the frustum (see
/// <c>TerrainView.Panoramic</c>): built from what was drawn, a hill behind the camera cast no shadow on
/// the valley in front of it, and the valley went in and out of shadow as the player turned.
/// </para>
/// <para>
/// The mesh is a storage mesh whose buffers are written directly by the host, so it cannot be rewritten
/// while a frame in flight is still building a structure over it. A small ring of meshes takes turns:
/// a slot written now was last used <see cref="Slots"/> frames ago, which is past the frames the device
/// keeps in flight. A frame whose drawn set and camera are the last frame's resubmits the slot it has,
/// and nothing is written or rebuilt.
/// </para>
/// </remarks>
internal sealed class TerrainShadowCaster : IDisposable
{
    /// <summary>How many meshes take turns; one more than the frames the device keeps in flight.</summary>
    private const int Slots = 3;

    /// <summary>The most vertices one mesh holds — the limit of 16-bit indexing.</summary>
    private const int VertexCapacity = ushort.MaxValue + 1;

    /// <summary>Indices per mesh; a base layer carries about three indices per vertex.</summary>
    private const int IndexCapacity = VertexCapacity * 4;

    /// <summary>Below this many chunks the fill runs on the calling thread; the hand-off costs more than it saves.</summary>
    private const int ParallelThreshold = 256;

    /// <summary>How many parts a parallel fill is cut into; the calling thread fills one of them itself.</summary>
    private const int ParallelParts = 8;

    private readonly IGraphicsDevice _device;
    private readonly IWorkScheduler? _work;
    private readonly Task[] _parts = new Task[ParallelParts - 1];
    private readonly Slot[] _slots = new Slot[Slots];

    // The frame being assembled: one record per chunk saying where its vertices and indices land, and where
    // each mesh's run of both starts. The vertices themselves are written only by Commit, and only when the
    // caster changed.
    private readonly List<Pending> _pending = [];
    private readonly List<(int VertexStart, int IndexStart)> _runs = [];
    private Vertex3D[] _vertices = [];
    private ushort[] _indices = [];
    private int _vertexCount;
    private int _indexCount;

    private int _current = -1;
    private bool _committed;
    private long _lastHash;
    private Vector3d _lastCamera;
    private float _lastOffset;
    private float _lastSlope;
    private bool _disposed;

    private Vector3d _camera;
    private Vector3d _origin;
    private float _offset;
    private float _slope;
    private long _hash;

    /// <summary>Initialises a new instance of the <see cref="TerrainShadowCaster"/> class.</summary>
    /// <param name="device">The device the meshes live on.</param>
    /// <param name="work">Where the fill's parallel parts run, or null to fill on the calling thread.</param>
    public TerrainShadowCaster(IGraphicsDevice device, IWorkScheduler? work = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _work = work;
        for (int i = 0; i < Slots; i++)
        {
            _slots[i] = new Slot();
        }
    }

    /// <summary>Gets how many triangles the last submitted caster held.</summary>
    public int LastTriangleCount { get; private set; }

    /// <summary>Starts assembling a frame's caster.</summary>
    /// <param name="cameraMetres">The camera the terrain was drawn from, in metres.</param>
    /// <param name="fadeOffset">The fade window's offset, as the renderer hands the shader.</param>
    /// <param name="fadeSlope">The fade window's slope.</param>
    public void Begin(in Vector3d cameraMetres, float fadeOffset, float fadeSlope)
    {
        _pending.Clear();
        _runs.Clear();
        _runs.Add((0, 0));
        _vertexCount = 0;
        _indexCount = 0;
        _camera = cameraMetres;
        _offset = fadeOffset;
        _slope = fadeSlope;
        _hash = 17;

        // Positions are stored relative to a point near the camera so they fit a float; the mesh is
        // placed by the same offset. A kilometre grid keeps the origin still while the camera walks.
        _origin = new Vector3d(
            Math.Floor(cameraMetres.X / 1024.0) * 1024.0,
            Math.Floor(cameraMetres.Y / 1024.0) * 1024.0,
            Math.Floor(cameraMetres.Z / 1024.0) * 1024.0);
    }

    /// <summary>Adds one drawn chunk's base layer, to be morphed as the shader morphs it.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="geometry">Its geometry, whose first layer is the base pass.</param>
    /// <param name="coarse">
    /// Whether a chunk refined onto the smooth ground may cast with its original triangles instead.
    /// The renderer asks for that for chunks out of sight: what matters there is the shape of the
    /// shadow they throw, which a few centimetres of smoothing does not change, and the caster is
    /// rebuilt every frame the camera moves, so the refined triangles would cost on every one of them.
    /// A chunk in sight casts refined, or the drawn ground would sit under its own caster in every
    /// hollow and shadow itself.
    /// </param>
    /// <remarks>
    /// Only placed here, not written. The frame's whole caster is known before any vertex is morphed, so a
    /// frame whose caster is the last one's writes nothing at all, and one that changed writes every chunk
    /// at once, in parallel (see <see cref="Commit"/>). Morphing each chunk as it arrived, one vertex at a
    /// time onto a growing list, cost about four milliseconds a frame at the Ultra terrain detail on every
    /// frame the camera moved, and was nearly all of what that detail cost over Enhanced.
    /// </remarks>
    public void Add(in TerrainChunk chunk, TerrainChunkGeometry geometry, bool coarse = false)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Layers.Length == 0)
        {
            return;
        }

        TerrainChunkLayer layer = geometry.Layers[0];
        ushort[]? coarseIndices = coarse ? geometry.Coarse : null;
        int indexCount = coarseIndices?.Length ?? layer.IndexCount;

        // The original triangles reach only the chunk's own vertices, which come first.
        int vertexCount = coarseIndices is not null ? geometry.CoarseVertexCount : geometry.VerticesPerPass;
        if (vertexCount == 0 || indexCount < 3)
        {
            return;
        }

        // One mesh cannot index past 65536 vertices; a chunk that would overflow starts the next. Nor
        // can it hold more indices than it was made with, which a smoothed chunk -- two triangles to
        // a vertex where the stored ground has about one -- reaches before it reaches the vertices.
        (int vertexStart, int indexStart) = _runs[^1];
        if (_vertexCount - vertexStart + vertexCount > VertexCapacity
            || _indexCount - indexStart + indexCount > IndexCapacity)
        {
            vertexStart = _vertexCount;
            _runs.Add((vertexStart, _indexCount));
        }

        _hash = (_hash * 31) + (long)chunk.Position.X;
        _hash = (_hash * 31) + (long)chunk.Position.Y;
        _hash = (_hash * 31) + (long)chunk.Position.Z;
        _hash = (_hash * 31) + chunk.LodExponent;

        // Which geometry, and whether it casts coarse: a changed smoothing setting rebuilds a chunk in
        // place, and a chunk turning into view swaps its triangles, with no chunk entering or leaving.
        _hash = (_hash * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(geometry);
        _hash = (_hash * 31) + (coarseIndices is null ? 0 : 1);

        _pending.Add(new Pending(
            chunk.Position,
            chunk.LodExponent,
            geometry,
            coarseIndices,
            vertexCount,
            indexCount,
            _vertexCount,
            _indexCount,
            _vertexCount - vertexStart));
        _vertexCount += vertexCount;
        _indexCount += indexCount;
    }

    /// <summary>Morphs and uploads what was assembled, or keeps the last upload where nothing changed.</summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_indexCount < 3)
        {
            _committed = false;
            LastTriangleCount = 0;
            return;
        }

        bool unchanged = _current >= 0
            && _hash == _lastHash
            && _camera == _lastCamera
            && _offset == _lastOffset
            && _slope == _lastSlope;

        if (!unchanged)
        {
            Fill();
            _current = (_current + 1) % Slots;
            Upload(_slots[_current]);
            _lastHash = _hash;
            _lastCamera = _camera;
            _lastOffset = _offset;
            _lastSlope = _slope;
        }

        _committed = true;
        int triangles = 0;
        foreach (int indexCount in _slots[_current].IndexCounts)
        {
            triangles += indexCount / 3;
        }

        LastTriangleCount = triangles;
    }

    /// <summary>Offers the committed caster to the traced world.</summary>
    /// <param name="camera">The camera the terrain is drawn with, in metres, to place the mesh.</param>
    public void Submit(in Camera3D camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_committed)
        {
            return;
        }

        Slot slot = _slots[_current];
        Matrix4f modelView = camera.CreateModelView(slot.Origin);
        for (int i = 0; i < slot.Meshes.Count; i++)
        {
            int indexCount = slot.IndexCounts[i];
            if (indexCount >= 3)
            {
                _device.SubmitShadowCaster(slot.Meshes[i], modelView, indexCount);
            }
        }
    }

    /// <summary>Draws the committed caster into the sun's cascade being filled.</summary>
    /// <param name="lightCamera">The light's camera, at the eye and looking along the sun.</param>
    /// <remarks>
    /// The same surface the traced sun is given, so the two shadows agree on what the ground is: the
    /// morph is already in the vertices, and the cascade's own depth bias keeps it off itself.
    /// </remarks>
    public void DrawCascade(in Camera3D lightCamera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_committed)
        {
            return;
        }

        Slot slot = _slots[_current];
        Matrix4f modelView = lightCamera.CreateModelView(slot.Origin);
        for (int i = 0; i < slot.Meshes.Count; i++)
        {
            int indexCount = slot.IndexCounts[i];
            if (indexCount >= 3)
            {
                _device.DrawMesh(
                    slot.Meshes[i], 0, indexCount, modelView, TextureHandle.None, TextureHandle.None, BlendMode.Opaque);
            }
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
        foreach (Slot slot in _slots)
        {
            foreach (MeshHandle mesh in slot.Meshes)
            {
                _device.DestroyMesh(mesh);
            }

            slot.Meshes.Clear();
        }
    }

    // Every chunk's vertices morphed into place and its indices rebased, each chunk into the range Add gave
    // it, so the chunks are independent of one another and go in parallel.
    private void Fill()
    {
        if (_vertices.Length < _vertexCount)
        {
            _vertices = new Vertex3D[Math.Max(_vertexCount, _vertices.Length * 2)];
        }

        if (_indices.Length < _indexCount)
        {
            _indices = new ushort[Math.Max(_indexCount, _indices.Length * 2)];
        }

        FillContext context = new(_pending.ToArray(), _vertices, _indices, _camera, _origin, _offset, _slope);
        int count = _pending.Count;
        if (count < ParallelThreshold || _work is not { IsConcurrent: true } work)
        {
            context.FillRange(0, count);
            return;
        }

        // The host's scheduler takes all but the last part; this thread fills that one, then waits for the rest.
        for (int part = 0; part < ParallelParts - 1; part++)
        {
            int from = count * part / ParallelParts;
            int to = count * (part + 1) / ParallelParts;
            _parts[part] = work.Run(() => context.FillRange(from, to));
        }

        context.FillRange(count * (ParallelParts - 1) / ParallelParts, count);
        Task.WaitAll(_parts);
    }

    private void Upload(Slot slot)
    {
        slot.Origin = _origin;
        slot.IndexCounts.Clear();

        Span<Vertex3D> vertices = _vertices.AsSpan(0, _vertexCount);
        Span<ushort> indices = _indices.AsSpan(0, _indexCount);

        for (int run = 0; run < _runs.Count; run++)
        {
            (int vertexStart, int indexStart) = _runs[run];
            int vertexEnd = run + 1 < _runs.Count ? _runs[run + 1].VertexStart : _vertexCount;
            int indexEnd = run + 1 < _runs.Count ? _runs[run + 1].IndexStart : _indexCount;

            if (slot.Meshes.Count <= run)
            {
                slot.Meshes.Add(_device.CreateMeshStorage(VertexCapacity, IndexCapacity));
            }

            int indexCount = indexEnd - indexStart;
            if (vertexEnd > vertexStart && indexCount > 0)
            {
                _device.UploadMeshRange(
                    slot.Meshes[run],
                    0,
                    vertices[vertexStart..vertexEnd],
                    0,
                    indices[indexStart..indexEnd]);
            }

            slot.IndexCounts.Add(indexCount);
        }

        // A slot that held more meshes last time keeps them, unused, rather than churning buffers.
        for (int i = _runs.Count; i < slot.Meshes.Count; i++)
        {
            slot.IndexCounts.Add(0);
        }
    }

    /// <summary>One chunk's place in the frame's caster: what to write, and where.</summary>
    private readonly record struct Pending(
        Vector3d Position,
        int LodExponent,
        TerrainChunkGeometry Geometry,
        ushort[]? CoarseIndices,
        int VertexCount,
        int IndexCount,
        int VertexAt,
        int IndexAt,
        int BaseVertex);

    /// <summary>What every chunk's fill shares, so the parallel loop captures one object.</summary>
    private sealed class FillContext(
        Pending[] pending,
        Vertex3D[] vertices,
        ushort[] indices,
        Vector3d camera,
        Vector3d origin,
        float offset,
        float slope)
    {
        public void FillRange(int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                FillOne(pending[i]);
            }
        }

        private void FillOne(in Pending item)
        {
            TerrainChunkGeometry geometry = item.Geometry;

            double chunkX = item.Position.X / WorldScene.UnitsPerMetre;
            double chunkY = item.Position.Y / WorldScene.UnitsPerMetre;
            double chunkZ = item.Position.Z / WorldScene.UnitsPerMetre;

            // The shader's ramp, in the metres it works in.
            double scale = 0.375 * WorldScene.UnitsPerMetre / (1 << (25 - item.LodExponent));

            Span<Vertex3D> into = vertices.AsSpan(item.VertexAt, item.VertexCount);
            ReadOnlySpan<Vertex3D> source = geometry.Vertices.AsSpan(0, item.VertexCount);
            for (int i = 0; i < source.Length; i++)
            {
                ref readonly Vertex3D v = ref source[i];
                double worldX = chunkX + v.Position.X;
                double worldY = chunkY + v.Position.Y;
                double worldZ = chunkZ + v.Position.Z;

                // Measured on the target, so that two chunks meeting at a shared vertex agree — exactly as
                // the vertex stage does it.
                double targetX = worldX + v.Normal.X - camera.X;
                double targetY = worldY + v.Normal.Y - camera.Y;
                double targetZ = worldZ + v.Normal.Z - camera.Z;
                double reach = Math.Max(Math.Abs(targetX), Math.Max(Math.Abs(targetY), Math.Abs(targetZ)));
                double morph = Math.Clamp(((reach * scale) - offset) * slope, 0.0, 1.0);

                into[i] = new Vertex3D(
                    new Vector3f(
                        (float)(worldX + (v.Normal.X * morph) - origin.X),
                        (float)(worldY + (v.Normal.Y * morph) - origin.Y),
                        (float)(worldZ + (v.Normal.Z * morph) - origin.Z)),
                    v.Normal,
                    v.DiffuseU,
                    v.DiffuseV,
                    v.LightmapU,
                    v.LightmapV,
                    v.Colour,
                    v.SurfaceNormal);
            }

            Span<ushort> target = indices.AsSpan(item.IndexAt, item.IndexCount);
            ReadOnlySpan<ushort> triangles = item.CoarseIndices is { } coarse
                ? coarse
                : geometry.Indices.AsSpan(geometry.Layers[0].IndexOffset, item.IndexCount);
            for (int i = 0; i < triangles.Length; i++)
            {
                target[i] = (ushort)(triangles[i] + item.BaseVertex);
            }
        }
    }

    private sealed class Slot
    {
        public List<MeshHandle> Meshes { get; } = [];

        public List<int> IndexCounts { get; } = [];

        public Vector3d Origin { get; set; }
    }
}
