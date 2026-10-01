using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Drawing;

/// <summary>One positioned glyph handed to a renderer, in device space and already clipped.</summary>
/// <remarks>
/// <para>
/// It carries the glyph two ways so either kind of renderer can draw it from the same layout. An atlas
/// renderer (the game) draws <see cref="Destination"/> sampled from <see cref="Source"/>, both already
/// trimmed to the clip. A vector renderer (the launcher's Skia canvas) draws <see cref="Character"/> at
/// <see cref="PenX"/>, <see cref="BaselineY"/> and <see cref="SizePx"/>, clipping to <see cref="Clip"/>
/// itself when <see cref="Trimmed"/> says the clip cut the glyph.
/// </para>
/// <para>
/// Both come from the one <c>GlyphRun</c> over the atlas's metrics, so line breaks, widths and the pen
/// advance are identical whichever renderer is drawing.
/// </para>
/// </remarks>
/// <param name="Destination">The glyph's box in device pixels, trimmed to the clip.</param>
/// <param name="Source">The glyph's box in atlas texture coordinates, trimmed in proportion.</param>
/// <param name="Character">The character drawn.</param>
/// <param name="PenX">The pen position at the glyph's origin, in device pixels.</param>
/// <param name="BaselineY">The baseline, in device pixels.</param>
/// <param name="SizePx">The text size in device pixels.</param>
/// <param name="Clip">The clip rectangle in force, in device pixels.</param>
/// <param name="Trimmed">Whether the clip cut into the glyph's box.</param>
public readonly record struct UiGlyph(
    UiRect Destination,
    UiRect Source,
    char Character,
    float PenX,
    float BaselineY,
    float SizePx,
    UiRect Clip,
    bool Trimmed);
