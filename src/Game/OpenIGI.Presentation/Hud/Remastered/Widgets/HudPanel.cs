using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered.Widgets;

/// <summary>
/// The glass a group of the display stands on: the picture behind it blurred and tinted, its edge cut the theme's way, a thin
/// rim, brackets at its corners, and now and then a bright band sweeping across it.
/// </summary>
public static class HudPanel
{
    /// <summary>How wide the backdrop's blur is, in pixels at the full screen.</summary>
    public const float BlurSigma = 20f;

    /// <summary>Draws a panel.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="rect">Where it stands, in pixels.</param>
    /// <param name="opacity">How far it has faded in, nought to one.</param>
    public static void Draw(HudDrawContext context, UiRect rect, float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(context);
        Draw(context.Painter, context.Theme, context.DesignScale, context.Seconds, rect, opacity);
    }

    /// <summary>Draws a panel in a theme's look, for a display that is not the heads-up one: the remastered computer's.</summary>
    /// <param name="painter">The painter, whose backdrop the glass is drawn from.</param>
    /// <param name="theme">The title's look.</param>
    /// <param name="scale">How many pixels a design pixel is drawn as.</param>
    /// <param name="seconds">The time now, for the shimmer.</param>
    /// <param name="rect">Where it stands, in pixels.</param>
    /// <param name="opacity">How far it has faded in, nought to one.</param>
    public static void Draw(UiPainter painter, HudTheme theme, float scale, float seconds, UiRect rect, float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        if (rect.IsEmpty || opacity <= 0f)
        {
            return;
        }

        HudMotifs motifs = theme.Motifs;
        HudPalette palette = theme.Palette;
        float cut = motifs.CornerSize * scale;
        UiCorners corners = motifs.Corner == HudCorner.Cut ? UiCorners.TopLeft | UiCorners.BottomRight : UiCorners.All;
        Span<UiVec2> outline = stackalloc UiVec2[8];
        int count = Outline(rect, cut, corners, outline);
        ReadOnlySpan<UiVec2> shape = outline[..count];

        // The glass: the picture behind, blurred, under the theme's tint.
        painter.DrawBackdrop(shape, BlurSigma, HudDrawContext.Fade(palette.Glass, opacity));

        // Frost: a pale haze over the blurred picture, and a soft wash down it, lighter at the top, so it reads as a pane of
        // frosted glass and not a hole or a flat plate.
        painter.FillPolygon(shape, HudDrawContext.Fade(Bgra32.White, 0.07f * opacity));
        painter.FillGradient(
            rect,
            [new UiGradientStop(0f, HudDrawContext.Fade(palette.Highlight, 0.16f * opacity)), new UiGradientStop(0.45f, HudDrawContext.Fade(palette.Primary, 0.05f * opacity)), new UiGradientStop(1f, HudDrawContext.Fade(palette.Primary, 0f))],
            UiGradientDirection.Vertical);

        // Its rim, thin and quiet, and the light caught along its top edge.
        float hairline = MathF.Max(1f, (motifs.Hairline * scale));
        painter.StrokePolygon(shape, hairline, HudDrawContext.Fade(palette.Primary, 0.35f * opacity));
        float inset = (motifs.CornerSize * scale) + hairline;
        painter.FillRect(new UiRect(rect.X + inset, rect.Y + hairline, rect.Width - (inset * 2f), hairline), HudDrawContext.Fade(palette.Highlight, 0.5f * opacity));

        // Brackets at the corners, bright.
        if (motifs.BracketLength > 0f)
        {
            Brackets(painter, theme, scale, rect, motifs.BracketLength * scale, HudDrawContext.Fade(palette.Primary, 0.9f * opacity));
        }

        // Now and then a bright band sweeps across.
        if (motifs.Shimmer)
        {
            Shimmer(painter, theme, scale, seconds, rect, opacity);
        }
    }

    /// <summary>Draws brackets at a rectangle's four corners.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="length">How far each arm runs, in pixels.</param>
    /// <param name="colour">Their colour.</param>
    public static void Brackets(HudDrawContext context, UiRect rect, float length, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(context);
        Brackets(context.Painter, context.Theme, context.DesignScale, rect, length, colour);
    }

    /// <summary>Draws brackets at a rectangle's four corners, in a theme's look.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The title's look.</param>
    /// <param name="scale">How many pixels a design pixel is drawn as.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="length">How far each arm runs, in pixels.</param>
    /// <param name="colour">Their colour.</param>
    public static void Brackets(UiPainter painter, HudTheme theme, float scale, UiRect rect, float length, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float t = MathF.Max(1f, theme.Motifs.Hairline * scale * 1.5f);
        float l = MathF.Min(length, MathF.Min(rect.Width, rect.Height) * 0.5f);
        (float x0, float y0, float x1, float y1) = (rect.X, rect.Y, rect.Right, rect.Bottom);
        painter.FillRect(new UiRect(x0, y0, l, t), colour);
        painter.FillRect(new UiRect(x0, y0, t, l), colour);
        painter.FillRect(new UiRect(x1 - l, y0, l, t), colour);
        painter.FillRect(new UiRect(x1 - t, y0, t, l), colour);
        painter.FillRect(new UiRect(x0, y1 - t, l, t), colour);
        painter.FillRect(new UiRect(x0, y1 - l, t, l), colour);
        painter.FillRect(new UiRect(x1 - l, y1 - t, l, t), colour);
        painter.FillRect(new UiRect(x1 - t, y1 - l, t, l), colour);
    }

    /// <summary>Draws a rule: one thin line, or the theme's pair.</summary>
    /// <param name="context">The frame's drawing.</param>
    /// <param name="x">Its left end, in pixels.</param>
    /// <param name="y">Its height, in pixels.</param>
    /// <param name="width">Its length, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    public static void Rule(HudDrawContext context, float x, float y, float width, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(context);
        Rule(context.Painter, context.Theme, context.DesignScale, x, y, width, colour);
    }

    /// <summary>Draws a rule in a theme's look: one thin line, or the theme's pair.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The title's look.</param>
    /// <param name="scale">How many pixels a design pixel is drawn as.</param>
    /// <param name="x">Its left end, in pixels.</param>
    /// <param name="y">Its height, in pixels.</param>
    /// <param name="width">Its length, in pixels.</param>
    /// <param name="colour">Its colour.</param>
    public static void Rule(UiPainter painter, HudTheme theme, float scale, float x, float y, float width, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float t = MathF.Max(1f, theme.Motifs.Hairline * scale);
        painter.FillRect(new UiRect(x, y, width, t), colour);
        if (theme.Motifs.DoubleRules)
        {
            painter.FillRect(new UiRect(x, y + (t * 3f), width * 0.62f, t), HudDrawContext.Fade(colour, 0.45f));
        }
    }

    // A bright diagonal band crossing the panel once every few seconds.
    private static void Shimmer(UiPainter painter, HudTheme theme, float scale, float seconds, UiRect rect, float opacity)
    {
        const float Period = 6.5f;
        const float Sweep = 0.9f;
        float phase = (seconds + (rect.X * 0.0013f)) % Period;
        if (phase > Sweep)
        {
            return;
        }

        float t = phase / Sweep;
        float bandWidth = MathF.Max((18f * scale), rect.Width * 0.12f);
        float x = rect.X - bandWidth + ((rect.Width + (bandWidth * 2f)) * t);
        float lean = rect.Height * 0.6f;
        Bgra32 bright = HudDrawContext.Fade(theme.Palette.Highlight, 0.16f * opacity * MathF.Sin(t * MathF.PI));
        painter.PushClip(rect);
        painter.FillQuad(
            new UiVec2(x, rect.Bottom),
            new UiVec2(x + bandWidth, rect.Bottom),
            new UiVec2(x + bandWidth + lean, rect.Y),
            new UiVec2(x + lean, rect.Y),
            bright);
        painter.PopClip();
    }

    // The panel's outline, clockwise from the top left, with the theme's corners cut.
    private static int Outline(UiRect r, float cut, UiCorners corners, Span<UiVec2> points)
    {
        int n = 0;
        float c = MathF.Min(cut, MathF.Min(r.Width, r.Height) * 0.5f);
        if ((corners & UiCorners.TopLeft) != 0)
        {
            points[n++] = new UiVec2(r.X, r.Y + c);
            points[n++] = new UiVec2(r.X + c, r.Y);
        }
        else
        {
            points[n++] = new UiVec2(r.X, r.Y);
        }

        if ((corners & UiCorners.TopRight) != 0)
        {
            points[n++] = new UiVec2(r.Right - c, r.Y);
            points[n++] = new UiVec2(r.Right, r.Y + c);
        }
        else
        {
            points[n++] = new UiVec2(r.Right, r.Y);
        }

        if ((corners & UiCorners.BottomRight) != 0)
        {
            points[n++] = new UiVec2(r.Right, r.Bottom - c);
            points[n++] = new UiVec2(r.Right - c, r.Bottom);
        }
        else
        {
            points[n++] = new UiVec2(r.Right, r.Bottom);
        }

        if ((corners & UiCorners.BottomLeft) != 0)
        {
            points[n++] = new UiVec2(r.X + c, r.Bottom);
            points[n++] = new UiVec2(r.X, r.Bottom - c);
        }
        else
        {
            points[n++] = new UiVec2(r.X, r.Bottom);
        }

        return n;
    }
}
