namespace OpenIGI.Formats.World;

/// <summary>One entry of a pack's <c>GTT </c> material table: a diffuse texture and a palette.</summary>
/// <remarks>
/// <para>
/// A model's draw record names a slot; the instance turns that slot into an index into this table; this
/// table gives the two halves. <c>MTP_ParseMTP</c> (<c>igi2.exe 0x4F5DE0</c>) reads them as
/// <c>gtt[2 * slot]</c> and <c>gtt[2 * slot + 1]</c> and resolves each against a table of its own.
/// </para>
/// <para>
/// <see cref="Palette"/> is <see cref="PackId.None"/> in every entry of every retail pack — all 12,523
/// of them, across both games. Object lightmaps are bound per object from
/// <c>lightmaps/lightmaps.res</c> instead, and 8-bit palettised textures are not what either shipped
/// game draws, so this field is only ever exercised by modified data.
/// </para>
/// <para>
/// The original resolves it against three different tables: <c>−1</c> against <c>paPalInfo</c>, which
/// indexes it at <c>−1</c> and reads out of bounds; a level-local id against <c>paPalInfoExtra</c>; and a
/// common-pack id against the common pack's <c>paPalInfo</c>. OpenIGI diverges on the first: "none" means
/// no palette. No retail path reaches any of the three.
/// </para>
/// </remarks>
/// <param name="Texture">The diffuse texture id, in the pack's <c>TEXF</c> id space.</param>
/// <param name="Palette">The palette id, or <see cref="PackId.None"/>.</param>
public readonly record struct MtpMaterial(PackId Texture, PackId Palette)
{
    /// <summary>The size in bytes of one table entry.</summary>
    public const int Size = 8;

    /// <inheritdoc/>
    public override string ToString() => $"tex={Texture} pal={Palette}";
}
