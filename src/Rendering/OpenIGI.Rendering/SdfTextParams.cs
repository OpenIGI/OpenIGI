using OpenIGI.Core.Graphics;

namespace OpenIGI.Rendering;

/// <summary>
/// How a run of signed-distance-field text is shaded: the atlas's distance spread, and the optional
/// outline and glow drawn from that one distance channel.
/// </summary>
/// <remarks>
/// <para>
/// The fill colour is the per-vertex colour, so it can vary per glyph for a gradient or a per-glyph
/// animation. Everything here is what the whole run shares. Distances are in the normalised space the
/// atlas stores — the glyph edge is at <c>0.5</c>, inside is greater — so <see cref="OutlineWidth"/> and
/// <see cref="GlowWidth"/> are fractions of that, independent of the size the text is drawn at.
/// </para>
/// <para>
/// <see cref="PxRange"/> is the number of atlas texels the distance field ramps over, taken from the
/// atlas's SDF padding. The shader combines it with the on-screen size of a texel to keep the edge one
/// screen pixel soft at any magnification, which is the whole point of the distance-field approach.
/// </para>
/// </remarks>
/// <param name="PxRange">The distance-field spread in atlas texels; the atlas's SDF padding.</param>
/// <param name="OutlineColor">The outline colour.</param>
/// <param name="OutlineWidth">The outline half-width in normalised distance, or zero for no outline.</param>
/// <param name="GlowColor">The glow colour.</param>
/// <param name="GlowWidth">The distance below the edge the glow fades over, or zero for no glow.</param>
/// <param name="GlowIntensity">The glow's peak opacity, or zero for no glow.</param>
/// <param name="Dissolve">How far the glyphs have burned away, zero whole to one gone.</param>
/// <param name="DissolveSeed">Which grain of noise the burn follows.</param>
public readonly record struct SdfTextParams(
    float PxRange,
    Bgra32 OutlineColor,
    float OutlineWidth,
    Bgra32 GlowColor,
    float GlowWidth,
    float GlowIntensity,
    float Dissolve = 0f,
    float DissolveSeed = 0f)
{
    /// <summary>Creates parameters for plain anti-aliased fill with no outline or glow.</summary>
    /// <param name="pxRange">The distance-field spread in atlas texels.</param>
    /// <returns>The parameters.</returns>
    public static SdfTextParams Plain(float pxRange) =>
        new(pxRange, default, 0f, default, 0f, 0f);

    /// <summary>Returns these parameters with an outline added.</summary>
    /// <param name="color">The outline colour.</param>
    /// <param name="width">The outline half-width in normalised distance.</param>
    /// <returns>The parameters with the outline set.</returns>
    public SdfTextParams WithOutline(Bgra32 color, float width) =>
        this with { OutlineColor = color, OutlineWidth = width };

    /// <summary>Returns these parameters with a glow added.</summary>
    /// <param name="color">The glow colour.</param>
    /// <param name="width">The distance below the edge the glow fades over.</param>
    /// <param name="intensity">The glow's peak opacity.</param>
    /// <returns>The parameters with the glow set.</returns>
    public SdfTextParams WithGlow(Bgra32 color, float width, float intensity) =>
        this with { GlowColor = color, GlowWidth = width, GlowIntensity = intensity };

    /// <summary>Burns the glyphs away: zero is whole, one is gone, with an ember fringe along the cut.</summary>
    /// <param name="amount">How far gone, from zero to one.</param>
    /// <param name="seed">Which grain of noise the cut follows, so two blocks do not burn identically.</param>
    /// <returns>The parameters with the dissolve applied.</returns>
    public SdfTextParams WithDissolve(float amount, float seed = 0f) =>
        this with { Dissolve = amount, DissolveSeed = seed };
}
