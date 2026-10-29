using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>A placement's uploaded mesh and, once first drawn, its per-subset draw state.</summary>
internal sealed class ResolvedWorldModel(MeshGeometry geometry, MeshHandle mesh, bool isSector)
{
    public MeshGeometry Geometry { get; } = geometry;

    public MeshHandle Mesh { get; } = mesh;

    /// <summary>Whether winding repair has usable normals and the mesh is not an interior sector.</summary>
    /// <remarks>
    /// IGI2 lightmapped MEFs omit normals. Their authored winding must remain two-sided; applying
    /// the prop optimization erases mission 2's cave. IGI1 sectors likewise enclose the viewer.
    /// Evaluate once at residency, rather than scanning vertices in every render pass.
    /// </remarks>
    public bool CanCullBackFaces => !isSector && _hasNormals;

    /// <summary>Whether this is an interior sector whose winding repair has usable normals: culled while the eye is outside it.</summary>
    public bool CanCullFromOutside => isSector && _hasNormals;

    private readonly bool _hasNormals = Array.TrueForAll(
        geometry.Vertices, vertex => float.IsFinite(vertex.Normal.LengthSquared) && vertex.Normal.LengthSquared > 0.000001f);

    public WorldSubsetDraw[]? Subsets { get; set; }

    /// <summary>Gets or sets whether any of those subsets is glazing.</summary>
    /// <remarks>
    /// So the glass pass can reject a placement before it frustum-tests it. Almost nothing in a level
    /// is a window, and without this the pass would walk every crate, tree and oil drum in the draw
    /// order to find the handful of models that have a pane in them.
    /// </remarks>
    public bool HasGlass { get; set; }

    /// <summary>The light this placement is the fixture of, or -1 when it is not one.</summary>
    /// <remarks>Resolved with the subsets, and melted with them whenever the switchboard changes.</remarks>
    public int LampFixture { get; set; } = -1;

    /// <summary>Gets or sets the placement's occlusion query, created on first geometry-pass use.</summary>
    public OcclusionQueryHandle Query { get; set; }

    /// <summary>Gets the local box the query rasterises — the bounding sphere's enclosing cube.</summary>
    public OcclusionBounds QueryBounds { get; } = new(
        new Vector3f(
            geometry.Bounds.Centre.X - geometry.Bounds.Radius,
            geometry.Bounds.Centre.Y - geometry.Bounds.Radius,
            geometry.Bounds.Centre.Z - geometry.Bounds.Radius),
        new Vector3f(
            geometry.Bounds.Centre.X + geometry.Bounds.Radius,
            geometry.Bounds.Centre.Y + geometry.Bounds.Radius,
            geometry.Bounds.Centre.Z + geometry.Bounds.Radius));
}
