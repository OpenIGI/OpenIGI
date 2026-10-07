using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>One collision mesh level, <c>MultiMesh_Mesh_t</c>, unpacked into the arrays the line handlers walk.</summary>
/// <remarks>
/// The faces, vertices, sphere tree and material rows are the first game's unpacking (<see cref="RetailRayMesh"/>);
/// the sequel adds two fields the handlers report: each face's lightmap and each material's portal index.
/// </remarks>
public sealed class Igi2CollisionMesh
{
    /// <summary>Initializes a new instance of the <see cref="Igi2CollisionMesh"/> class.</summary>
    /// <param name="mesh">The collision mesh level.</param>
    public Igi2CollisionMesh(MefCollisionMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Arrays = new RetailRayMesh(mesh);

        ReadOnlySpan<MefCollisionFace> faces = mesh.Faces;
        Lightmaps = new int[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            Lightmaps[i] = faces[i].Lightmap;
        }

        Portals = new int[mesh.Materials];
        Rows = new int[mesh.Materials];
        for (int m = 0; m < Portals.Length; m++)
        {
            Portals[m] = mesh.PortalIndex(m);
            Rows[m] = mesh.MaterialRow(m);
        }
    }

    /// <summary>Gets the faces, vertices, sphere tree and material rows.</summary>
    public RetailRayMesh Arrays { get; }

    /// <summary>Gets each face's lightmap index, <c>iLightmap</c>; <c>0xFFFF</c> for none.</summary>
    public int[] Lightmaps { get; }

    /// <summary>Gets each material's portal index, <c>iPortal</c>; <c>0xFFFF</c> for none.</summary>
    public int[] Portals { get; }

    /// <summary>Gets each material's game-material row as stored, <c>iMaterial</c>; <c>0xFFFF</c> for none.</summary>
    public int[] Rows { get; }
}

/// <summary>A placed solid model, <c>RigidDynCubeObj</c>, as the collision sees it: a pose, a scale, and its meshes.</summary>
public sealed class Igi2RigidObject : IIgi2Collider
{
    private readonly IReadOnlyList<Igi2CollisionMesh?> _meshes;

    /// <summary>Initializes a new instance of the <see cref="Igi2RigidObject"/> class.</summary>
    /// <param name="owner">What a hit reports as struck.</param>
    /// <param name="meshes">The collision mesh levels, by <c>iMesh</c>; a level the model lacks is <see langword="null"/>.</param>
    /// <param name="radius">The model's radius, <c>Mesh3D_Mesh_t.vRadius</c> of LOD 0 (<c>Mesh3D_GetLODVirModelRadius</c>).</param>
    /// <param name="isBuildingMesh">Whether the model draws as a building, <c>eRenderMeshType</c> 3.</param>
    public Igi2RigidObject(object owner, IReadOnlyList<Igi2CollisionMesh?> meshes, float radius, bool isBuildingMesh)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        Radius = radius;
        IsBuildingMesh = isBuildingMesh;
    }

    /// <summary>Gets what the object stands for in the level.</summary>
    public object Owner { get; }

    /// <summary>Gets or sets the position, <c>tPos</c>, world units.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets the orientation, <c>tOrientation</c>: nine floats, row by row, local to world.</summary>
    public float[] Orientation { get; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Gets or sets the scale on each local axis, <c>+0xF8</c>.</summary>
    public Vector3f Scale { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets the model's radius.</summary>
    public float Radius { get; }

    /// <summary>
    /// Gets the radius the bounds tests use: the model's radius scaled by the largest of the three scales, as the
    /// line handler forms it.
    /// </summary>
    public float BoundingRadius
    {
        get
        {
            float sx = Scale.X, sy = Scale.Y, sz = Scale.Z;
            float largest = sy > sz ? sy : sz;
            return ((Fpu)Radius * (largest < sx ? sx : largest)).F;
        }
    }

    /// <summary>Gets a value indicating whether the model draws as a building.</summary>
    public bool IsBuildingMesh { get; }

    /// <summary>Gets or sets a value indicating whether the model has baked lightmaps (<c>+0x134</c> set and non-empty).</summary>
    public bool IsLightmapped { get; set; }

    /// <summary>Gets one collision mesh level.</summary>
    /// <param name="level">The level, <c>iMesh</c>.</param>
    /// <returns>The level, or <see langword="null"/> when the model has none there.</returns>
    public Igi2CollisionMesh? Mesh(int level) => (uint)level < (uint)_meshes.Count ? _meshes[level] : null;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>RigidDynCubeObj_TraceLinesHandler</c> answers both the line and the ground events
    /// (<c>RigidDynCubeObj_GenericOpen</c>, <c>igi2 0x58E170</c>), so this is <see cref="Igi2RigidTrace"/>.
    /// </remarks>
    public void NextZ(Igi2LineTrace trace) => Igi2RigidTrace.Trace(this, trace);
}
