using OpenIGI.Core.Graphics;

namespace OpenIGI.UI.Drawing;

/// <summary>The decorations a distance-field glyph can carry: an outline, a glow and a dissolve.</summary>
/// <remarks>
/// Widths are in distance-field units, the same units the game's SDF shader reads (0 to about 0.5 of the
/// field's padding). A renderer that draws vector glyphs maps them onto a stroke and a blur of the same
/// apparent size; one that cannot express an effect leaves it out rather than failing.
/// </remarks>
/// <param name="OutlineColor">The outline colour.</param>
/// <param name="OutlineWidth">The outline width; zero for none.</param>
/// <param name="GlowColor">The glow colour.</param>
/// <param name="GlowWidth">How far the glow reaches; zero for none.</param>
/// <param name="GlowIntensity">The glow's peak strength, 0 to 1.</param>
/// <param name="Dissolve">How far the glyph has burned away, 0 whole to 1 gone.</param>
/// <param name="DissolveSeed">The noise seed for the dissolve pattern.</param>
public readonly record struct UiTextEffects(
    Bgra32 OutlineColor,
    float OutlineWidth,
    Bgra32 GlowColor,
    float GlowWidth,
    float GlowIntensity,
    float Dissolve = 0f,
    float DissolveSeed = 0f)
{
    /// <summary>Gets plain text: no outline, glow or dissolve.</summary>
    public static UiTextEffects Plain => default;

    /// <summary>Gets a value indicating whether these effects decorate the glyph at all.</summary>
    public bool IsPlain => OutlineWidth <= 0f && GlowWidth <= 0f && GlowIntensity <= 0f && Dissolve <= 0f;

    /// <summary>Returns a copy with an outline.</summary>
    /// <param name="color">The outline colour.</param>
    /// <param name="width">The outline width in distance-field units.</param>
    /// <returns>The decorated effects.</returns>
    public UiTextEffects WithOutline(Bgra32 color, float width) =>
        this with { OutlineColor = color, OutlineWidth = width };

    /// <summary>Returns a copy with a glow.</summary>
    /// <param name="color">The glow colour.</param>
    /// <param name="width">How far the glow reaches, in distance-field units.</param>
    /// <param name="intensity">The glow's peak strength.</param>
    /// <returns>The decorated effects.</returns>
    public UiTextEffects WithGlow(Bgra32 color, float width, float intensity) =>
        this with { GlowColor = color, GlowWidth = width, GlowIntensity = intensity };

    /// <summary>Returns a copy with a dissolve.</summary>
    /// <param name="amount">How far the glyph has burned away.</param>
    /// <param name="seed">The noise seed.</param>
    /// <returns>The decorated effects.</returns>
    public UiTextEffects WithDissolve(float amount, float seed = 0f) =>
        this with { Dissolve = amount, DissolveSeed = seed };
}
