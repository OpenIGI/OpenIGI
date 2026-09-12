using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>A level's terrain, loaded: the octree, its geometry, and the tables its script declares.</summary>
/// <remarks>
/// <para>
/// The original holds all of this on one terrain object — the visibility bitmaps at <c>+883</c>, the
/// heightmaps at <c>+884</c>, the command blob at <c>+2</c> and the octree at <c>+3</c>. Gathering it
/// into one value keeps the fact that a level may legitimately have none of it, or only some of it, in
/// the type rather than in a comment.
/// </para>
/// <para>
/// <see cref="IsUsable"/> is the question a renderer asks. <b>Level 14's terrain files are not live
/// data</b> — 14400 of its 15202 octree records point at a block that cannot exist — so it answers no,
/// and that level draws its objects over nothing rather than following wild offsets.
/// </para>
/// </remarks>
public sealed class TerrainData
{
    /// <summary>Initializes a new instance of the <see cref="TerrainData"/> class.</summary>
    /// <param name="octree">The octree.</param>
    /// <param name="commands">The geometry blob.</param>
    /// <param name="visibility">The material bitmaps.</param>
    /// <param name="heightmap">The heightmaps, or <see langword="null"/> on a level that ships none.</param>
    /// <param name="lightmaps">The baked terrain lightmaps, or <see langword="null"/>.</param>
    /// <param name="tiles">The ground texture atlas, or <see langword="null"/> when the level ships none.</param>
    /// <param name="materials">The materials the terrain script declared, keyed by id.</param>
    /// <param name="tileMaps">The tile maps the terrain script declared, keyed by id.</param>
    /// <param name="defects">Why the terrain is not usable, empty when it is.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TerrainData(
        TerrainOctree octree,
        TerrainCommands commands,
        TerrainVisibility visibility,
        TerrainHeightmap? heightmap,
        TerrainLightmaps? lightmaps,
        TexAtlasFile? tiles,
        IReadOnlyDictionary<int, TerrainMaterial> materials,
        IReadOnlyDictionary<int, TerrainTileMap> tileMaps,
        IReadOnlyList<string> defects)
    {
        ArgumentNullException.ThrowIfNull(octree);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(tileMaps);
        ArgumentNullException.ThrowIfNull(defects);

        Octree = octree;
        Commands = commands;
        Visibility = visibility;
        Heightmap = heightmap;
        Lightmaps = lightmaps;
        Tiles = tiles;
        Materials = materials;
        TileMaps = tileMaps;
        Defects = defects;
    }

    /// <summary>Gets the octree the traversal descends.</summary>
    public TerrainOctree Octree { get; }

    /// <summary>Gets the geometry every chunk is cut from.</summary>
    public TerrainCommands Commands { get; }

    /// <summary>Gets the per-cell material bitmaps.</summary>
    public TerrainVisibility Visibility { get; }

    /// <summary>Gets the heightmaps, or <see langword="null"/>.</summary>
    /// <remarks>Seven of the fourteen retail levels ship no <c>terrain.hmp</c>; that is not a fault.</remarks>
    public TerrainHeightmap? Heightmap { get; }

    /// <summary>Gets the baked terrain lightmap pages, or <see langword="null"/>.</summary>
    public TerrainLightmaps? Lightmaps { get; }

    /// <summary>Gets the ground texture atlas a tile map indexes, or <see langword="null"/>.</summary>
    /// <remarks>Every retail level ships eighteen 128×128 tiles, which is exactly the range its tile maps use.</remarks>
    public TexAtlasFile? Tiles { get; }

    /// <summary>Gets the materials the terrain script declared, keyed by id.</summary>
    /// <remarks>Every retail level declares <c>0</c> to <c>6</c>.</remarks>
    public IReadOnlyDictionary<int, TerrainMaterial> Materials { get; }

    /// <summary>Gets the tile maps the terrain script declared, keyed by id.</summary>
    /// <remarks>Every retail level declares <c>0</c> to <c>7</c>, except level 8 which stops at <c>6</c>.</remarks>
    public IReadOnlyDictionary<int, TerrainTileMap> TileMaps { get; }

    /// <summary>Gets why the terrain cannot be drawn, empty when it can.</summary>
    public IReadOnlyList<string> Defects { get; }

    /// <summary>Gets a value indicating whether the terrain can be traversed and drawn.</summary>
    public bool IsUsable => Defects.Count == 0;

    /// <summary>Reads the block a chunk draws.</summary>
    /// <param name="chunk">The chunk, as the traversal emitted it.</param>
    /// <param name="block">Receives the block.</param>
    /// <returns><see langword="false"/> when the chunk's offset does not address a block.</returns>
    public bool TryGetBlock(in TerrainChunk chunk, out TerrainCommandBlock block) =>
        Commands.TryGetBlock(chunk.CommandOffset, out block);

    /// <inheritdoc/>
    public override string ToString() =>
        IsUsable
            ? $"terrain: {Octree.Count} nodes, {Materials.Count} material(s), {TileMaps.Count} tile map(s)"
            : $"terrain unusable: {string.Join("; ", Defects)}";
}
