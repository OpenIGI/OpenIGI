namespace OpenIGI.Formats.World;

/// <summary>One entry of a pack's <c>GTT </c> material table: a diffuse texture and a lightmap.</summary>
/// <remarks>
/// <para>
/// <see cref="Lightmap"/> is <see cref="PackId.None"/> in every entry of every retail pack — all 6942 of
/// them. Object lightmaps are bound per object from <c>lightmaps/lightmaps.res</c> instead, so this field
/// is only ever exercised by modified data.
/// </para>
/// <para>
/// The original resolves a non-negative level-local lightmap id against its <em>palette</em> table while
/// resolving the common-pack case against the texture-companion table, and resolves "none" as index −1 of
/// that companion table — an out-of-bounds read. OpenIGI diverges: "none" means no lightmap, and a
/// level-local id resolves like the common-pack case. Neither path is reachable with retail data. See
/// the original.
/// </para>
/// </remarks>
/// <param name="Texture">The diffuse texture id, in the pack's <c>TEXF</c> id space.</param>
/// <param name="Lightmap">The lightmap id, or <see cref="PackId.None"/>.</param>
public readonly record struct MtpMaterial(PackId Texture, PackId Lightmap)
{
    /// <summary>The size in bytes of one table entry.</summary>
    public const int Size = 8;

    /// <inheritdoc/>
    public override string ToString() => $"tex={Texture} lm={Lightmap}";
}
