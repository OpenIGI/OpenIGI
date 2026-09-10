using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One terrain tile of an I.G.I. 2 level, placed in the world.</summary>
/// <param name="Id">The tile id, which is what names its three files.</param>
/// <param name="Centre">Where the tile's middle sits, in world units.</param>
/// <param name="TexelWidth">The distance between samples along X, in world units.</param>
/// <param name="TexelHeight">The distance between samples along Y, in world units.</param>
/// <param name="Width">How many samples the tile is across.</param>
/// <param name="Height">How many samples the tile is down.</param>
/// <param name="Heights">The height map, whose samples are stored relative to <paramref name="BaseHeight"/>.</param>
/// <param name="BaseHeight">The Z the samples are measured from — the parent <c>Terrain</c>'s own, in world units.</param>
/// <param name="Materials">The material map, or null when the tile ships none.</param>
/// <param name="Lightmap">The baked lightmap, or null when the tile ships none.</param>
public readonly record struct Igi2TerrainTile(
    int Id,
    Vector3d Centre,
    double TexelWidth,
    double TexelHeight,
    int Width,
    int Height,
    Igi2TerrainMap Heights,
    double BaseHeight,
    Igi2TerrainMap? Materials,
    Igi2TerrainMap? Lightmap)
{
    /// <summary>Gets the world height of one sample.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The height, in world units.</returns>
    /// <remarks>
    /// <para>
    /// <b>A sample is an offset from the level's terrain, not a world height.</b>
    /// <c>Terrain_GetZFromWorldCoordinates</c> (<c>igi2ed 0x632FF0</c>) interpolates the four samples
    /// around a world position and adds <c>ptTerrain-&gt;tPos.z</c> to the result — the <em>parent</em>
    /// <c>Terrain</c> task's Z, not the tile's. The X and Y halves of the same function convert the
    /// query and the tile's own position into terrain space by subtracting the same origin from both,
    /// so those two cancel and only Z is left as a real offset.
    /// </para>
    /// <para>
    /// Twenty-three of the twenty-four shipped levels author that Z as zero, which is why every path
    /// through here read the same either way. <c>location1/level3</c> authors it as 36,864,000 — nine
    /// kilometres — and there the difference is the whole level: the ground draws nine kilometres below
    /// the buildings standing on it, which past a four-kilometre far plane is not a misplaced terrain
    /// but a black screen.
    /// </para>
    /// </remarks>
    public double HeightAt(int x, int y) => BaseHeight + Heights.GetHeight(x, y);

    /// <summary>Gets the world position of one sample.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The position, with the sample's world height as Z.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sample is outside the tile.</exception>
    /// <remarks>
    /// <b>Samples sit at texel centres, not at grid corners.</b> The tile spans
    /// <c>centre ± worldExtent / 2</c> and is divided into <see cref="Width"/> texels, so sample
    /// <c>i</c> is at <c>left + (i + 0.5) * texelWidth</c> — which puts the first sample half a texel
    /// inside the edge and the last one half a texel short of the far edge.
    /// <para>
    /// That is the original's own arithmetic, read from both directions.
    /// <c>TerrainMap_ResampleAndAlign</c> (<c>igi2.exe 0x571780</c>) derives
    /// <c>vBaseTexelWidth = vWorldWidth * 4096 / nWidth</c> and snaps against
    /// <c>tPos - (vWorldWidth * 4096 + vBaseTexelWidth) / 2</c>, half a texel before the left edge;
    /// <c>Terrain_SampleMaterial</c> (<c>0x56EDC0</c>) inverts it as
    /// <c>(x - centre + half) / texelWidth</c> and rejects anything within half a texel of either edge.
    /// </para>
    /// <para>
    /// Dividing by <see cref="Width"/> rather than <c>Width - 1</c> is the part worth stating: a grid
    /// laid corner to corner would be one texel too wide and half a texel out of place, which reads as
    /// a terrain that <em>almost</em> lines up with the objects standing on it.
    /// </para>
    /// </remarks>
    public Vector3d PositionOf(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return new Vector3d(
            Centre.X - (Width * TexelWidth * 0.5) + ((x + 0.5) * TexelWidth),
            Centre.Y - (Height * TexelHeight * 0.5) + ((y + 0.5) * TexelHeight),
            HeightAt(x, y));
    }

    /// <summary>Gets the material id painted on one sample, or zero when the tile ships no material map.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The material id, which indexes a level's declared materials by their own <c>ID</c>.</returns>
    public byte MaterialAt(int x, int y) => Materials?.GetMaterial(x, y) ?? 0;

    /// <summary>Gets the baked light on one sample, or opaque white when the tile ships no lightmap.</summary>
    /// <param name="x">The sample's column.</param>
    /// <param name="y">The sample's row.</param>
    /// <returns>The colour.</returns>
    /// <remarks>
    /// White rather than black for the missing case, which is the original's own fallback: it allocates
    /// the buffer and <c>memset</c>s it to <c>0xFF</c>, so an unlit tile draws at full brightness rather
    /// than disappearing.
    /// </remarks>
    public Bgra32 LightAt(int x, int y) => Lightmap?.GetLight(x, y) ?? new Bgra32(255, 255, 255, 255);
}

/// <summary>
/// An I.G.I. 2 level's ground: the tiles it declares, loaded and placed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The level is the authority, not the archive.</b> A level's <c>TerrainMap</c> tasks name the tiles
/// it has, and the ids are sparse — <c>location2/LEVEL6</c> declares only tile 2. Enumerating
/// <c>heightmaps.res</c> instead would load four tiles that belong to no level at all, because
/// <c>location3/LEVEL4</c> ships two while declaring none.
/// </para>
/// <para>
/// A tile missing any of its three files is complete rather than broken, which is the original's own
/// tolerance: <c>TerrainMap_Alloc</c> substitutes a zeroed material map and a white lightmap and carries
/// on. The height map is the one that cannot be substituted usefully, so a tile without it is skipped.
/// </para>
/// </remarks>
public sealed class Igi2TerrainSurface
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.terrain";

    private Igi2TerrainSurface(
        IReadOnlyList<Igi2TerrainTile> tiles,
        IReadOnlyDictionary<int, TerrainMaterialTask> materials)
    {
        Tiles = tiles;
        Materials = materials;
    }

    /// <summary>Gets the level's tiles, in the order it declared them.</summary>
    public IReadOnlyList<Igi2TerrainTile> Tiles { get; }

    /// <summary>Gets the level's terrain materials, keyed by the id a material map's samples carry.</summary>
    /// <remarks>
    /// Keyed by <c>ID</c> rather than by position, because that is what a <c>.tmm</c> sample holds: the
    /// table is sparse and a level may declare its materials in any order.
    /// </remarks>
    public IReadOnlyDictionary<int, TerrainMaterialTask> Materials { get; }

    /// <summary>Gets a value indicating whether the level has any ground to draw.</summary>
    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>Loads the ground a level declares.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="heightmaps">The level's <c>heightmaps.res</c>, or null when it ships none.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <returns>The surface, which may be empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static Igi2TerrainSurface Build(GameTask levelRoot, IresArchive? heightmaps, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        IIgiLogger log = logger ?? NullLogger.Instance;
        List<Igi2TerrainTile> tiles = [];
        Dictionary<int, TerrainMaterialTask> materials = [];

        // heightmaps.res keys its entries by bare file name -- `heightmap000.thm`, not the alias-
        // qualified path the task builds. The original never notices: TerrainMap_Alloc sprintfs a name
        // and hands it to the file layer, which resolves through the mounted archive either way.
        Dictionary<string, IresEntry> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (IresEntry entry in heightmaps?.Entries ?? [])
        {
            byName[BareName(entry.Name)] = entry;
        }

        foreach (GameTask task in Walk(levelRoot))
        {
            if (task is TerrainMaterialTask material)
            {
                materials[material.MaterialId] = material;
            }
            else if (task is TerrainMapTask map && TryLoad(map, byName, log, out Igi2TerrainTile tile))
            {
                tiles.Add(tile);
            }
        }

        log.Information(LogCategory, $"Terrain: {tiles.Count} tile(s), {materials.Count} material(s).");
        return new Igi2TerrainSurface(tiles, materials);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 terrain, {Tiles.Count} tile(s), {Materials.Count} material(s)";

    private static bool TryLoad(
        TerrainMapTask map,
        IReadOnlyDictionary<string, IresEntry> byName,
        IIgiLogger log,
        out Igi2TerrainTile tile)
    {
        tile = default;
        if (!TryRead(byName, map.HeightMapResource, out Igi2TerrainMap? heights))
        {
            log.Warning(LogCategory, $"Terrain tile {map.MapId} has no height map; it is not drawn.");
            return false;
        }

        // The world extent is authored in metres and the engine multiplies by 4096 before it divides by
        // the sample count -- so the spacing is in world units, and the tile is nWidth texels wide
        // rather than nWidth - 1 spans wide.
        double texelWidth = map.WorldWidth * WorldScene.UnitsPerMetre / heights.Width;
        double texelHeight = map.WorldHeight * WorldScene.UnitsPerMetre / heights.Height;

        _ = TryRead(byName, map.MaterialMapResource, out Igi2TerrainMap? materialMap);
        _ = TryRead(byName, map.LightMapResource, out Igi2TerrainMap? lightMap);

        tile = new Igi2TerrainTile(
            map.MapId,
            map.Position,
            texelWidth,
            texelHeight,
            heights.Width,
            heights.Height,
            heights,
            BaseHeightOf(map),
            Matching(materialMap, heights) ? materialMap : null,
            Matching(lightMap, heights) ? lightMap : null);

        return true;
    }

    // The Z a tile's samples are measured from, which Terrain_GetZFromWorldCoordinates takes from the
    // TerrainMap's parent rather than from the TerrainMap. A tile the level parented somewhere else is
    // measured from nothing, which is the same answer the original gives when no terrain is active.
    private static double BaseHeightOf(TerrainMapTask map)
    {
        for (GameTask? parent = map.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TerrainTask terrain)
            {
                return terrain.Position.Z;
            }
        }

        return 0.0;
    }

    // A map whose extent disagrees with the height map's is not usable as a per-sample channel. The
    // original never reaches this case because it validates each file against nEditWidth/nEditHeight as
    // it loads; this keeps the same guarantee for a caller that indexes all three together.
    private static bool Matching(Igi2TerrainMap? map, Igi2TerrainMap heights) =>
        map is not null && map.Width == heights.Width && map.Height == heights.Height;

    private static bool TryRead(
        IReadOnlyDictionary<string, IresEntry> byName,
        string resource,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Igi2TerrainMap? map)
    {
        map = null;
        if (!byName.TryGetValue(BareName(resource), out IresEntry? entry))
        {
            return false;
        }

        map = Igi2TerrainMap.Read(entry.Data);
        return true;
    }

    private static string BareName(string name)
    {
        int slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    private static IEnumerable<GameTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;

            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
