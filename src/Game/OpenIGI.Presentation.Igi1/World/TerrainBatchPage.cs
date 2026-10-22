using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>A 16-bit indexed mesh page sharing every state needed by one multi-draw.</summary>
internal sealed class TerrainBatchPage
{
    /// <summary>The most vertices one page holds: what a 16-bit index can reach.</summary>
    public const int MaximumBatchVertices = ushort.MaxValue + 1;

    /// <summary>The smallest vertex capacity a page's mesh is created with.</summary>
    /// <remarks>
    /// <para>
    /// Headroom, so that appending a chunk to a page does not recreate its mesh every time; the
    /// capacity doubles from here as the page fills, so a page reaching a thousand vertices is
    /// recreated four times over its whole life rather than once per chunk.
    /// </para>
    /// <para>
    /// It is small because pages are: the batch key separates every level of detail, ground tile,
    /// layer slot and base-or-detail pass, and there are only so many chunks in a 2048 m cell that
    /// share all four. Measured over a level 1 panorama, the median page holds ELEVEN vertices and
    /// three quarters of them hold under twenty; the top percent reach a thousand or so. A floor of
    /// 4096 was charging 192 KB of device memory for a median page holding 528 bytes of geometry --
    /// 2306 live pages, 105 412 vertices held against 9 445 376 allocated, some 486 MB of buffers
    /// for 5 MB of ground. The doubling is what handles the rare large page; the floor only has to
    /// cover the common tiny one.
    /// </para>
    /// </remarks>
    private const int MinimumVertexCapacity = 64;

    /// <summary>The smallest index capacity a page's mesh is created with.</summary>
    /// <remarks>Three per vertex, which is what the ground's triangles measure at (2.72 over a panorama).</remarks>
    private const int MinimumIndexCapacity = MinimumVertexCapacity * 3;

    private readonly List<Vertex3D> _vertices = [];

    private readonly List<ushort> _indices = [];
    private readonly List<VisibleDraw> _visible = [];
    private readonly List<MeshDrawRange> _visibleRanges = [];
    private Vector3f _minimum = new(float.MaxValue);
    private Vector3f _maximum = new(float.MinValue);
    private int _uploadedVertices;
    private int _uploadedIndices;
    private int _vertexCapacity;
    private int _indexCapacity;
    private int _deadVertices;
    private bool _dirty;
    private bool _queryRequested;
    private readonly TerrainBatcher _owner;

    public TerrainBatchPage(TerrainBatchKey key, IGraphicsDevice device, TerrainBatcher owner)
    {
        Key = key;
        _owner = owner;
        OriginMetres = new Vector3d(
            key.CellX * TerrainBatchKey.BatchCellMetres,
            key.CellY * TerrainBatchKey.BatchCellMetres,
            key.CellZ * TerrainBatchKey.BatchCellMetres);
        OcclusionQuery = key.IsBase ? device.CreateOcclusionQuery() : OcclusionQueryHandle.None;
    }

    public static Comparison<TerrainBatchPage> DrawOrder { get; } = (left, right) =>
    {
        int layer = left.Key.LayerSlot.CompareTo(right.Key.LayerSlot);
        return layer != 0 ? layer : left.MinimumDistance.CompareTo(right.MinimumDistance);
    };

    public TerrainBatchKey Key { get; private set; }

    public Vector3d OriginMetres { get; private set; }

    public MeshHandle Mesh { get; private set; }

    public OcclusionQueryHandle OcclusionQuery { get; private set; }

    public OcclusionBounds Bounds => new(_minimum, _maximum);

    public bool HasVisibleRanges => _visible.Count > 0;

    /// <summary>Gets how many vertices the page holds, dead ranges included.</summary>
    public int VertexCount => _vertices.Count;

    /// <summary>Gets how many vertices the page's mesh was created with room for.</summary>
    public int VertexCapacity => _vertexCapacity;

    /// <summary>Gets how many of those vertices belong to evicted chunks.</summary>
    public int DeadVertexCount => _deadVertices;

    /// <summary>Gets whether enough of the page is holes that it is worth rebuilding from its survivors.</summary>
    public bool NeedsCompaction => _deadVertices > 0 && _deadVertices * 2 >= _vertices.Count;

    /// <summary>Notes that one appended range's chunk has been evicted.</summary>
    public void Release(int vertexCount) => _deadVertices += vertexCount;

    /// <summary>Forgets every appended range, keeping the mesh and its capacity for what comes next.</summary>
    public void Reset()
    {
        _vertices.Clear();
        _indices.Clear();
        _visible.Clear();
        _visibleRanges.Clear();
        _minimum = new Vector3f(float.MaxValue);
        _maximum = new Vector3f(float.MinValue);
        _deadVertices = 0;
        _uploadedVertices = 0;
        _uploadedIndices = 0;
        _dirty = true;
        _queryRequested = false;
        MinimumDistance = double.MaxValue;
    }

    /// <summary>Makes a spare page the page for another key.</summary>
    public void Rekey(TerrainBatchKey key, IGraphicsDevice device)
    {
        Key = key;
        OriginMetres = new Vector3d(
            key.CellX * TerrainBatchKey.BatchCellMetres,
            key.CellY * TerrainBatchKey.BatchCellMetres,
            key.CellZ * TerrainBatchKey.BatchCellMetres);

        // Only base pages are occlusion-queried, so the query follows the key -- and a recycled page
        // never inherits one. A query handle carries the last verdict the device recorded for it, and
        // that verdict was measured against the geometry and the position of the page this one used
        // to be: a spare that emptied while hidden behind a hill comes back as a page somewhere else
        // entirely, and would be culled on the old page's "hidden" until its retest landed several
        // frames later. A fresh query is conservatively visible, which is the only safe answer for a
        // page nothing has probed yet.
        if (OcclusionQuery.IsValid)
        {
            device.DestroyOcclusionQuery(OcclusionQuery);
            OcclusionQuery = OcclusionQueryHandle.None;
        }

        if (key.IsBase)
        {
            OcclusionQuery = device.CreateOcclusionQuery();
        }

        Reset();
    }

    /// <summary>Gets whether a camera position sits inside this page's occlusion bounds.</summary>
    /// <param name="cameraMetres">The camera position, in metres.</param>
    /// <param name="margin">Extra reach in metres, keeping the near plane clear of the box.</param>
    public bool ContainsCamera(in Vector3d cameraMetres, double margin)
    {
        double x = cameraMetres.X - OriginMetres.X;
        double y = cameraMetres.Y - OriginMetres.Y;
        double z = cameraMetres.Z - OriginMetres.Z;
        return x >= _minimum.X - margin && x <= _maximum.X + margin
            && y >= _minimum.Y - margin && y <= _maximum.Y + margin
            && z >= _minimum.Z - margin && z <= _maximum.Z + margin;
    }

    public bool QueryRequested => _queryRequested;

    public double MinimumDistance { get; private set; }

    public ReadOnlySpan<MeshDrawRange> VisibleRanges
    {
        get
        {
            if (_visible.Count > 1)
            {
                _visible.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
            }

            _visibleRanges.Clear();
            foreach (VisibleDraw draw in _visible)
            {
                _visibleRanges.Add(draw.Range);
            }
            return CollectionsMarshal.AsSpan(_visibleRanges);
        }
    }

    public bool CanFit(int vertices) => _vertices.Count + vertices <= MaximumBatchVertices;

    public MeshDrawRange Append(
        ReadOnlySpan<Vertex3D> vertices,
        ReadOnlySpan<ushort> indices,
        int sourceVertex,
        Vector3f translation)
    {
        int vertexBase = _vertices.Count;
        int indexBase = _indices.Count;

        // Grown to what this append needs before the loops, so a page filling up over many appends
        // does not shed a trail of half-size backing arrays behind each doubling.
        _vertices.EnsureCapacity(vertexBase + vertices.Length);
        _indices.EnsureCapacity(indexBase + indices.Length);

        foreach (Vertex3D vertex in vertices)
        {
            Vector3f position = new(
                vertex.Position.X + translation.X,
                vertex.Position.Y + translation.Y,
                vertex.Position.Z + translation.Z);
            Vector3f morphed = new(
                position.X + vertex.Normal.X,
                position.Y + vertex.Normal.Y,
                position.Z + vertex.Normal.Z);
            ExpandBounds(position);
            ExpandBounds(morphed);
            _vertices.Add(new Vertex3D(
                position,
                vertex.Normal,
                vertex.DiffuseU,
                vertex.DiffuseV,
                vertex.LightmapU,
                vertex.LightmapV,
                vertex.Colour,
                vertex.SurfaceNormal));
        }

        foreach (ushort index in indices)
        {
            _indices.Add(checked((ushort)(vertexBase + index - sourceVertex)));
        }

        _dirty = true;
        return new MeshDrawRange(indexBase, indices.Length);
    }

    public void BeginFrame()
    {
        _visible.Clear();
        _visibleRanges.Clear();
        MinimumDistance = double.MaxValue;
        _queryRequested = false;
    }

    /// <returns><see langword="true"/> when this is the first visible range queued.</returns>
    public bool Queue(MeshDrawRange range, double distance)
    {
        bool first = _visible.Count == 0;
        _visible.Add(new VisibleDraw(range, distance));
        MinimumDistance = Math.Min(MinimumDistance, distance);
        return first;
    }

    /// <returns><see langword="true"/> when this page needs adding to the frame's query order.</returns>
    public bool RequestQuery(double distance)
    {
        MinimumDistance = Math.Min(MinimumDistance, distance);
        if (_queryRequested || !OcclusionQuery.IsValid)
        {
            return false;
        }

        _queryRequested = true;
        return true;
    }

    /// <summary>Brings the GPU copy up to date with what has been appended.</summary>
    /// <remarks>
    /// Pages grow every time terrain streams in, and re-uploading a whole page per appended chunk
    /// was the frame-spike this renderer was profiled to. The mesh is allocated with headroom and
    /// each append uploads only its own range; the full re-upload survives only for the rare
    /// doubling where the headroom runs out.
    /// </remarks>
    public void EnsureUploaded(IGraphicsDevice device)
    {
        if (!_dirty && Mesh.IsValid)
        {
            return;
        }

        if (Mesh.IsValid && _vertices.Count <= _vertexCapacity && _indices.Count <= _indexCapacity)
        {
            device.UploadMeshRange(
                Mesh,
                _uploadedVertices,
                CollectionsMarshal.AsSpan(_vertices)[_uploadedVertices..],
                _uploadedIndices,
                CollectionsMarshal.AsSpan(_indices)[_uploadedIndices..]);
        }
        else
        {
            if (Mesh.IsValid)
            {
                device.DestroyMesh(Mesh);
                Mesh = MeshHandle.None;
                _owner.NotePageMeshDestroyed();
            }

            _owner.NotePageMeshCreated();
            _vertexCapacity = Math.Min(
                MaximumBatchVertices,
                (int)Math.Max(MinimumVertexCapacity, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_vertices.Count)));
            _indexCapacity = (int)Math.Max(
                MinimumIndexCapacity,
                System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_indices.Count));
            Mesh = device.CreateMeshStorage(_vertexCapacity, _indexCapacity);
            device.UploadMeshRange(
                Mesh,
                0,
                CollectionsMarshal.AsSpan(_vertices),
                0,
                CollectionsMarshal.AsSpan(_indices));
        }

        _uploadedVertices = _vertices.Count;
        _uploadedIndices = _indices.Count;
        _dirty = false;
    }

    public void Destroy(IGraphicsDevice device)
    {
        if (Mesh.IsValid)
        {
            device.DestroyMesh(Mesh);
            Mesh = MeshHandle.None;
            _owner.NotePageMeshDestroyed();
        }

        if (OcclusionQuery.IsValid)
        {
            device.DestroyOcclusionQuery(OcclusionQuery);
            OcclusionQuery = OcclusionQueryHandle.None;
        }

        _uploadedVertices = 0;
        _uploadedIndices = 0;
        _vertexCapacity = 0;
        _indexCapacity = 0;
    }

    private void ExpandBounds(Vector3f point)
    {
        _minimum = new Vector3f(
            Math.Min(_minimum.X, point.X),
            Math.Min(_minimum.Y, point.Y),
            Math.Min(_minimum.Z, point.Z));
        _maximum = new Vector3f(
            Math.Max(_maximum.X, point.X),
            Math.Max(_maximum.Y, point.Y),
            Math.Max(_maximum.Z, point.Z));
    }

    private readonly record struct VisibleDraw(MeshDrawRange Range, double Distance);
}
