using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.World;

/// <summary>A model flattened into one vertex buffer, one index buffer and a subset per draw record.</summary>
/// <param name="Vertices">The vertices, in the model's own order.</param>
/// <param name="Indices">Absolute indices into <paramref name="Vertices"/>, three per triangle.</param>
/// <param name="Subsets">One entry per draw record, in record order.</param>
public sealed record MeshGeometry(Vertex3D[] Vertices, ushort[] Indices, MeshSubset[] Subsets)
{
    /// <summary>Gets a conservative local-space bound calculated once when geometry is built.</summary>
    public BoundingSphere Bounds { get; } = BoundingSphere.FromVertices(Vertices);

    /// <summary>Gets the total triangle count.</summary>
    public int TriangleCount => Indices.Length / 3;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Vertices.Length} vertices, {TriangleCount} triangles, {Subsets.Length} subset(s)";
}

/// <summary>One draw record's slice of a <see cref="MeshGeometry"/>.</summary>
/// <param name="IndexOffset">The first index of this subset.</param>
/// <param name="IndexCount">How many indices the subset covers.</param>
/// <param name="MaterialSlot">
/// The slot to resolve against the drawing instance's material list, or
/// <see cref="OpenIGI.Formats.World.MefDrawRecord.NoMaterialSlot"/> when the subset draws untextured.
/// </param>
/// <param name="LightmapPage">
/// The index into the model's <see cref="OpenIGI.Formats.World.MefModel.LightmapPages"/> of the baked
/// page this subset samples; zero on models that have none.
/// </param>
public readonly record struct MeshSubset(int IndexOffset, int IndexCount, int MaterialSlot, int LightmapPage)
{
    /// <summary>Gets a value indicating whether this subset has a material.</summary>
    public bool HasMaterial => MaterialSlot != OpenIGI.Formats.World.MefDrawRecord.NoMaterialSlot;

    /// <summary>Gets the number of triangles the subset covers.</summary>
    public int TriangleCount => IndexCount / 3;

    /// <inheritdoc/>
    public override string ToString() => $"[{IndexOffset}..{IndexOffset + IndexCount}) slot={MaterialSlot}";
}
