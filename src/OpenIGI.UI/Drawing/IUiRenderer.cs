using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.UI.Drawing;

/// <summary>
/// The one seam between the interface and whatever puts pixels on screen: the game's sprite batch, or
/// the launcher's Skia canvas.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. <see cref="UiPainter"/> owns everything that should look the same on every
/// backend — the clip, opacity and transform stacks, polygon clipping, edge feathering, gradients and
/// text layout — and reduces it all to these few calls in device space. A renderer only has to put a
/// coloured, optionally textured quad, a glyph and a blurred-backdrop polygon on screen, in submission
/// order.
/// </para>
/// <para>
/// Every coordinate arrives already transformed and clipped: a renderer never sees the clip stack, so
/// it needs no scissor support and cannot get clipping subtly different from another renderer.
/// </para>
/// </remarks>
public interface IUiRenderer
{
    /// <summary>Draws a quad, corners in clockwise order from the top-left.</summary>
    /// <param name="image">The image sampled, or <see cref="UiImage.None"/> for solid colour.</param>
    /// <param name="topLeft">The first corner.</param>
    /// <param name="topRight">The second corner.</param>
    /// <param name="bottomRight">The third corner.</param>
    /// <param name="bottomLeft">The fourth corner; equal to the third for a triangle.</param>
    /// <param name="blend">How the quad combines with what is behind it.</param>
    void DrawQuad(
        UiImage image,
        in UiVertex topLeft,
        in UiVertex topRight,
        in UiVertex bottomRight,
        in UiVertex bottomLeft,
        UiBlend blend);

    /// <summary>Draws one glyph of laid-out text.</summary>
    /// <param name="font">The font the glyph belongs to.</param>
    /// <param name="glyph">The glyph's placement.</param>
    /// <param name="color">The fill colour, opacity already applied.</param>
    /// <param name="effects">The outline, glow and dissolve; <see langword="null"/> for the font's plain fill.</param>
    /// <param name="blend">How the glyph combines with what is behind it.</param>
    void DrawGlyph(UiFont font, in UiGlyph glyph, Bgra32 color, in UiTextEffects? effects, UiBlend blend);

    /// <summary>Fills a convex polygon with a blurred copy of what is behind the interface — frosted glass.</summary>
    /// <param name="polygon">The polygon in device pixels, already clipped.</param>
    /// <param name="sigmaPx">The blur's standard deviation in device pixels.</param>
    /// <param name="tint">A colour composited over the blurred backdrop, opacity already applied.</param>
    /// <param name="opacity">How opaque the glass itself is, 0 to 1: the painter's opacity, for a screen fading in.</param>
    /// <param name="zoom">How magnified the scene behind is, about the target's centre: 1 samples it in place.</param>
    /// <remarks>
    /// A renderer with nothing to blur fills the polygon with the tint alone, which is exactly what the
    /// glass looks like over a flat background.
    /// </remarks>
    void DrawBackdrop(ReadOnlySpan<UiVec2> polygon, float sigmaPx, Bgra32 tint, float opacity, float zoom);
}
