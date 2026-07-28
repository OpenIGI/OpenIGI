using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>One terrain chunk ready to draw: its vertices, its triangles, and a pass per ground material.</summary>
/// <remarks>
/// A chunk carries one copy of its geometry per material covering it, because the texture coordinates
/// depend on the material's own scale. The copies differ only in their texture coordinates and in the
/// per-vertex alpha that fades one material into the next.
/// </remarks>
/// <param name="Vertices">Every pass's vertices, back to back.</param>
/// <param name="Indices">Absolute indices into <paramref name="Vertices"/>, three per triangle.</param>
/// <param name="Layers">One pass per material, in the order they must be drawn.</param>
public sealed record TerrainChunkGeometry(
    Vertex3D[] Vertices,
    ushort[] Indices,
    IReadOnlyList<TerrainChunkLayer> Layers)
{
    /// <summary>Gets the total triangle count across every pass.</summary>
    public int TriangleCount => Indices.Length / 3;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Vertices.Length} vertices, {TriangleCount} triangles, {Layers.Count} pass(es)";
}

/// <summary>One ground material's pass over a chunk.</summary>
/// <param name="MaterialId">The terrain material this pass draws.</param>
/// <param name="TileIndex">The tile of the level's <c>terrain.tex</c> to sample.</param>
/// <param name="IndexOffset">The first index of this pass.</param>
/// <param name="IndexCount">How many indices the pass covers.</param>
/// <param name="IsBase">
/// <see langword="true"/> for the pass that fills the chunk. It is drawn opaque and every later pass
/// blends over it, weighted by the per-vertex alpha.
/// </param>
/// <param name="IsDetail">
/// <see langword="true"/> for a material's second, double-density pass — the band's second scale and
/// tile map. The original cross-fades it in as the camera approaches, which is what the renderer's
/// distance fade reproduces; without it the ground shows only the coarse tile and looks low-resolution
/// up close.
/// </param>
public readonly record struct TerrainChunkLayer(
    int MaterialId,
    int TileIndex,
    int IndexOffset,
    int IndexCount,
    bool IsBase,
    bool IsDetail)
{
    /// <summary>Gets the number of triangles the pass covers.</summary>
    public int TriangleCount => IndexCount / 3;

    /// <inheritdoc/>
    public override string ToString() =>
        $"material {MaterialId} on tile {TileIndex}{(IsBase ? " (base)" : string.Empty)}";
}
