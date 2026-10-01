using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One tile of terrain — I.G.I. 2's <c>TerrainMap</c>: where a tile sits, how large it is, and the id
/// that names its height, material and lightmap files.
/// </summary>
/// <remarks>
/// <para>
/// The eight parameters land one for one on <c>TerrainMap_t</c>'s edit fields in <c>igi2.exe</c>, in
/// declaration order: <c>iID</c>, <c>nEditWidth</c>, <c>nEditHeight</c>, <c>tEditPos</c>,
/// <c>vEditWorldWidth</c>, <c>vEditWorldHeight</c>, <c>vSoftenEdgeSize</c>, <c>zDEMFile</c>. The engine
/// copies each edit field to its run-time twin on load, which is why the declaration names the edit
/// half and the loader validates against it.
/// </para>
/// <para>
/// <b>The map extent and the world extent are independent.</b> <see cref="MapWidth"/> counts samples and
/// <see cref="WorldWidth"/> measures distance, so the sample spacing is the ratio of the two — a tile
/// can be resampled without moving, which is exactly what <c>TerrainMap_ResampleAndAlign</c> does.
/// <see cref="MapWidth"/> and <see cref="MapHeight"/> are also the numbers each of the three files is
/// checked against as it loads: a file whose header disagrees is rejected outright rather than stretched.
/// </para>
/// </remarks>
public sealed partial class TerrainMapTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "TerrainMap";

    /// <summary>The alias the three files are resolved under — <c>TerrainMap_GetDirectory</c>.</summary>
    /// <remarks>
    /// <c>igi2ed 0x62FE50</c> builds it as the literals <c>"MISSION:"</c> and <c>"heightmaps"</c>
    /// concatenated, and <c>TerrainMap_Alloc</c> appends <c>/heightmap%03d.<i>ext</i></c>. On a retail
    /// install the files are not loose: they arrive through <c>heightmaps/heightmaps.res</c>.
    /// </remarks>
    public const string MapDirectory = "MISSION:heightmaps";

    /// <summary>The id below which a tile's filename is well formed — the original asserts it.</summary>
    /// <remarks>
    /// <c>TerrainMap_Alloc</c> aborts with <c>"Error occured while loading heightmaps"</c> for an id of
    /// 1000 or more, because the name is built with <c>%03d</c> and a wider id would collide with
    /// another tile's.
    /// </remarks>
    public const int MaximumId = 1000;

    /// <summary>Gets or sets the tile id that names its files — <c>TerrainMap_t::iID</c>.</summary>
    /// <remarks>
    /// Not <see cref="GameTask.Id"/>, which is the slot a <c>Task_New</c> call claimed in the tree's
    /// 4096-entry table. This one is content: it is the number in <c>heightmap%03d.thm</c>, and two
    /// tiles in different levels routinely share it.
    /// </remarks>
    [ScriptParam(ScriptParamType.Int32, "ID")]
    public int MapId { get; set; }

    /// <summary>Gets or sets how many samples the tile holds along x — <c>nEditWidth</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Map width")]
    public int MapWidth { get; set; }

    /// <summary>Gets or sets how many samples the tile holds along y — <c>nEditHeight</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Map height")]
    public int MapHeight { get; set; }

    /// <summary>Gets or sets where the tile's origin sits — <c>tEditPos</c>.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets how far the tile spans along x — <c>vEditWorldWidth</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "World width")]
    public float WorldWidth { get; set; }

    /// <summary>Gets or sets how far the tile spans along y — <c>vEditWorldHeight</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "World height")]
    public float WorldHeight { get; set; }

    /// <summary>Gets or sets how wide the blend into a neighbouring tile is — <c>vSoftenEdgeSize</c>.</summary>
    /// <remarks>
    /// Read by <c>TerrainMap_SoftenHeightmapEdges</c> and <c>TerrainMap_SoftenLightmapEdges</c>
    /// (<c>igi2ed 0x62A770</c>, <c>0x62ACE0</c>), which run in the editor and bake their result into the
    /// files. Nothing softens at run time; the seam is already in the data.
    /// </remarks>
    [ScriptParam(ScriptParamType.Real32, "Soften edge size")]
    public float SoftenEdgeSize { get; set; }

    /// <summary>Gets or sets the elevation model the tile was imported from — <c>zDEMFile</c>.</summary>
    /// <remarks>
    /// An editor provenance field: <c>TerrainMap_LoadDEM</c> reads it when the author asks to re-import,
    /// and the game never opens it. Empty on most shipped tiles.
    /// </remarks>
    [ScriptParam(ScriptParamType.String256, "DEM File name")]
    public string DemFileName { get; set; } = string.Empty;

    /// <summary>Gets the resource name of this tile's height map.</summary>
    public string HeightMapResource => ResourceNameFor(Igi2TerrainMapKind.Height);

    /// <summary>Gets the resource name of this tile's material map.</summary>
    public string MaterialMapResource => ResourceNameFor(Igi2TerrainMapKind.Material);

    /// <summary>Gets the resource name of this tile's lightmap.</summary>
    public string LightMapResource => ResourceNameFor(Igi2TerrainMapKind.Light);

    /// <summary>Gets the file extension one map kind is stored under.</summary>
    /// <param name="kind">The map kind.</param>
    /// <returns>The extension, leading dot included.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a known kind.</exception>
    public static string ExtensionOf(Igi2TerrainMapKind kind) => kind switch
    {
        Igi2TerrainMapKind.Height => ".thm",
        Igi2TerrainMapKind.Material => ".tmm",
        Igi2TerrainMapKind.Light => ".tlm",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Builds the resource name one of this tile's three maps is loaded under.</summary>
    /// <param name="kind">Which of the three.</param>
    /// <returns>The alias-qualified resource name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a known kind.</exception>
    /// <exception cref="InvalidOperationException"><see cref="MapId"/> is outside the range the name can encode.</exception>
    /// <remarks>
    /// The three-digit id is the original's own <c>%03d</c>, and the range check is the original's own
    /// assertion rather than defensiveness: an out-of-range id does not produce a missing file, it
    /// produces <em>another tile's</em> file.
    /// </remarks>
    public string ResourceNameFor(Igi2TerrainMapKind kind)
    {
        string extension = ExtensionOf(kind);
        if (MapId is < 0 or >= MaximumId)
        {
            throw new InvalidOperationException(
                $"Terrain map id {MapId} is outside the 0 to {MaximumId - 1} the file name encodes.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{MapDirectory}/heightmap{MapId:D3}{extension}");
    }
}
