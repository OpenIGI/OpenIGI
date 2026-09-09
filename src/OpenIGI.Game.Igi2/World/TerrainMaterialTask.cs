using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One row of a level's terrain material table — I.G.I. 2's <c>TerrainMaterial</c>, selected per sample
/// by a tile's <c>.tmm</c> map.
/// </summary>
/// <remarks>
/// <para>
/// The seven parameters land on <c>TerrainMaterial_t</c> in <c>igi2.exe</c>: <c>iID</c>,
/// <c>iGameMaterial</c>, then <c>atTexture[0]</c> and <c>atTexture[1]</c> — a two-element array of
/// <c>TerrainTexture_t</c>, each a filename and a scale — and finally <c>eMappingStyle</c>. The
/// declaration interleaves the array differently from the struct, naming both filenames and then both
/// scales, which is why the two are read as four separate parameters rather than as a pair of rows.
/// </para>
/// <para>
/// <see cref="MaterialId"/> is the value a <c>.tmm</c> sample carries, not the task's position in the tree, so
/// the table is sparse by design and a level may declare its materials in any order. Across the retail
/// tree the sample values span 0 to 7.
/// </para>
/// <para>
/// This replaces I.G.I. 1's arrangement rather than extending it: there, the game material came from a
/// per-face selector inside the terrain's own <c>TAMC</c> table, and the texture came from a texture
/// modifier bound to a chunk. Here one row carries both, and a sample picks the row directly.
/// </para>
/// </remarks>
public sealed partial class TerrainMaterialTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "TerrainMaterial";

    /// <summary>Gets or sets the index a material map sample carries — <c>TerrainMaterial_t::iID</c>.</summary>
    /// <remarks>
    /// Not <see cref="GameTask.Id"/>, which is the slot a <c>Task_New</c> call claimed in the tree's
    /// 4096-entry table. This one is the byte a <c>.tmm</c> sample holds.
    /// </remarks>
    [ScriptParam(ScriptParamType.Int32, "ID")]
    public int MaterialId { get; set; }

    /// <summary>Gets or sets what walking on it sounds and feels like — <c>iGameMaterial</c>.</summary>
    /// <remarks>
    /// A row of the shared game-material table, the same one weapon impacts and footsteps resolve
    /// through, rather than anything terrain owns.
    /// </remarks>
    [ScriptParam(ScriptParamType.Int32, "Game material")]
    public int GameMaterial { get; set; }

    /// <summary>Gets or sets the base texture — <c>atTexture[0].zTextureFilename</c>.</summary>
    [ScriptParam(ScriptParamType.String256, "Texture")]
    public string Texture { get; set; } = string.Empty;

    /// <summary>Gets or sets the detail texture blended over it — <c>atTexture[1].zTextureFilename</c>.</summary>
    [ScriptParam(ScriptParamType.String256, "Detail texture")]
    public string DetailTexture { get; set; } = string.Empty;

    /// <summary>Gets or sets how far the base texture is tiled — <c>atTexture[0].vScale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Texture scale")]
    public float TextureScale { get; set; }

    /// <summary>Gets or sets how far the detail texture is tiled — <c>atTexture[1].vScale</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Detail texture scale")]
    public float DetailTextureScale { get; set; }

    /// <summary>Gets or sets how texture coordinates are generated — <c>eMappingStyle</c>.</summary>
    [ScriptParam(ScriptParamType.Int32, "Mapping style")]
    public int MappingStyle { get; set; }
}
