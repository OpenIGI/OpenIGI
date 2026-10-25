using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>A terrain chunk's cache key: its integer position and its level of detail.</summary>
/// <remarks>
/// The geometry a record holds is only the same geometry when it is drawn in the same place at the same
/// detail, so the octree record index cannot be the key. See <see cref="TerrainRenderer"/>.
/// </remarks>
internal readonly record struct TerrainChunkKey(long X, long Y, long Z, int Lod);

/// <summary>Everything a batch page's draws must share: the material state and the spatial cell.</summary>
internal readonly record struct TerrainBatchKey(
    int LodExponent,
    int TileIndex,
    bool IsBase,
    bool IsDetail,
    int LayerSlot,
    long CellX,
    long CellY,
    long CellZ)
{
    /// <summary>The edge of one batch cell, in metres.</summary>
    public const int BatchCellMetres = 2048;

    public static TerrainBatchKey For(in TerrainChunk chunk, in TerrainChunkLayer layer, int layerSlot)
    {
        double unitsPerCell = BatchCellMetres * WorldScene.UnitsPerMetre;
        return new(
            chunk.LodExponent,
            layer.TileIndex,
            layer.IsBase,
            layer.IsDetail,
            layerSlot,
            (long)Math.Floor(chunk.Position.X / unitsPerCell),
            (long)Math.Floor(chunk.Position.Y / unitsPerCell),
            (long)Math.Floor(chunk.Position.Z / unitsPerCell));
    }
}

/// <summary>Where one of a chunk's material layers went: the page, its index range and its size.</summary>
internal readonly record struct TerrainBatchedLayer(
    TerrainBatchPage Page,
    MeshDrawRange Range,
    int TriangleCount,
    int VertexCount);

/// <summary>One cached chunk: its geometry, the pages its layers live in, and when it was last drawn.</summary>
internal sealed class TerrainChunkEntry(TerrainChunk chunk, TerrainChunkGeometry geometry)
{
    public TerrainChunk Chunk { get; } = chunk;

    public TerrainChunkGeometry Geometry { get; } = geometry;

    public TerrainBatchedLayer[] Layers { get; set; } = [];

    public MeshHandle DirectMesh { get; set; }

    public TerrainBatchPage OcclusionPage { get; set; } = null!;

    public long LastUsedFrame { get; set; }
}
