using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi1.World;

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

    private readonly IGraphicsDevice _device;
    private readonly Slot[] _slots = new Slot[Slots];

    // The frame being assembled: its vertices and indices, and where each mesh's run of both starts.
    private readonly List<Vertex3D> _vertices = [];
    private readonly List<ushort> _indices = [];
    private readonly List<(int VertexStart, int IndexStart)> _runs = [];

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
    public TerrainShadowCaster(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
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
        _vertices.Clear();
        _indices.Clear();
        _runs.Clear();
        _runs.Add((0, 0));
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

    /// <summary>Adds one drawn chunk's base layer, morphed as the shader morphs it.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="geometry">Its geometry, whose first layer is the base pass.</param>
    public void Add(in TerrainChunk chunk, TerrainChunkGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Layers.Length == 0)
        {
            return;
        }

        TerrainChunkLayer layer = geometry.Layers[0];
        int vertexCount = geometry.Vertices.Length / geometry.Layers.Length;
        if (vertexCount == 0 || layer.IndexCount < 3)
        {
            return;
        }

        // One mesh cannot index past 65536 vertices; a chunk that would overflow starts the next.
        (int vertexStart, _) = _runs[^1];
        if (_vertices.Count - vertexStart + vertexCount > VertexCapacity)
        {
            vertexStart = _vertices.Count;
            _runs.Add((vertexStart, _indices.Count));
        }

        _hash = (_hash * 31) + (long)chunk.Position.X;
        _hash = (_hash * 31) + (long)chunk.Position.Y;
        _hash = (_hash * 31) + (long)chunk.Position.Z;
        _hash = (_hash * 31) + chunk.LodExponent;

        double chunkX = chunk.Position.X / WorldScene.UnitsPerMetre;
        double chunkY = chunk.Position.Y / WorldScene.UnitsPerMetre;
        double chunkZ = chunk.Position.Z / WorldScene.UnitsPerMetre;

        // The shader's ramp, in the metres it works in.
        double scale = 0.375 * WorldScene.UnitsPerMetre / (1 << (25 - chunk.LodExponent));

        int baseVertex = _vertices.Count - vertexStart;
        for (int i = 0; i < vertexCount; i++)
        {
            Vertex3D v = geometry.Vertices[i];
            double worldX = chunkX + v.Position.X;
            double worldY = chunkY + v.Position.Y;
            double worldZ = chunkZ + v.Position.Z;

            // Measured on the target, so that two chunks meeting at a shared vertex agree — exactly as
            // the vertex stage does it.
            double targetX = worldX + v.Normal.X - _camera.X;
            double targetY = worldY + v.Normal.Y - _camera.Y;
            double targetZ = worldZ + v.Normal.Z - _camera.Z;
            double reach = Math.Max(Math.Abs(targetX), Math.Max(Math.Abs(targetY), Math.Abs(targetZ)));
            double morph = Math.Clamp((reach * scale - _offset) * _slope, 0.0, 1.0);

            _vertices.Add(new Vertex3D(
                new Vector3f(
                    (float)(worldX + (v.Normal.X * morph) - _origin.X),
                    (float)(worldY + (v.Normal.Y * morph) - _origin.Y),
                    (float)(worldZ + (v.Normal.Z * morph) - _origin.Z)),
                v.Normal,
                v.DiffuseU,
                v.DiffuseV,
                v.LightmapU,
                v.LightmapV,
                v.Colour,
                v.SurfaceNormal));
        }

        for (int i = layer.IndexOffset; i < layer.IndexOffset + layer.IndexCount; i++)
        {
            _indices.Add((ushort)(geometry.Indices[i] + baseVertex));
        }
    }

    /// <summary>Uploads what was assembled, or keeps the last upload where nothing changed.</summary>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_indices.Count < 3)
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

    private void Upload(Slot slot)
    {
        slot.Origin = _origin;
        slot.IndexCounts.Clear();

        Span<Vertex3D> vertices = CollectionsMarshal.AsSpan(_vertices);
        Span<ushort> indices = CollectionsMarshal.AsSpan(_indices);

        for (int run = 0; run < _runs.Count; run++)
        {
            (int vertexStart, int indexStart) = _runs[run];
            int vertexEnd = run + 1 < _runs.Count ? _runs[run + 1].VertexStart : _vertices.Count;
            int indexEnd = run + 1 < _runs.Count ? _runs[run + 1].IndexStart : _indices.Count;

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

    private sealed class Slot
    {
        public List<MeshHandle> Meshes { get; } = [];

        public List<int> IndexCounts { get; } = [];

        public Vector3d Origin { get; set; }
    }
}
