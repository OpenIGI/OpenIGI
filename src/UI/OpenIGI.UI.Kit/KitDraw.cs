using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit;

/// <summary>Where a key cap sits: in a header bar (quieter) or a footer hint (brighter).</summary>
public enum KeyCapStyle
{
    /// <summary>A header's Q / E: muted text, a fainter border.</summary>
    Header,

    /// <summary>A footer hint: primary text, a slightly stronger border.</summary>
    Footer,
}

/// <summary>
/// The design's small recurring marks, drawn directly rather than as widgets: eyebrows, key caps, progress
/// segments, pills, glass, the backdrop vignette and blurred display text.
/// </summary>
/// <remarks>
/// Sizes are in design units and go through the painter's layout context, so a mark drawn here scales
/// with the screen exactly as the widgets around it do.
/// </remarks>
public static class KitDraw
{
    /// <summary>Draws an eyebrow: a small monospaced, widely tracked caption, accent by default.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The text, already in capitals.</param>
    /// <param name="topLeft">The top-left, in pixels.</param>
    /// <param name="size">The size, in design units.</param>
    /// <param name="color">The colour, or <see langword="null"/> for the accent.</param>
    /// <param name="tracking">The letterspacing, in ems.</param>
    /// <returns>The pen after the last glyph.</returns>
    public static UiVec2 Eyebrow(UiPainter painter, UiTheme theme, string text, UiVec2 topLeft, float size = 12f, Bgra32? color = null, float tracking = 0.3f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        return painter.DrawLabel(theme.Mono, text, topLeft, painter.Context.Px(size), color ?? theme.Accent, tracking);
    }

    /// <summary>Measures a key cap's box.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key's name.</param>
    /// <param name="size">The text size, in design units.</param>
    /// <returns>The cap's size, in pixels.</returns>
    public static UiVec2 MeasureKeyCap(LayoutContext context, UiTheme theme, string key, float size = 11f)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(theme);
        float px = context.Px(size);
        return new UiVec2(
            theme.Mono.MeasureWidth(key, px) + context.Px(14f),
            theme.Mono.LineHeight(px) + context.Px(6f));
    }

    /// <summary>Draws a key cap: a key's name in a hairline box.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key's name.</param>
    /// <param name="topLeft">The cap's top-left, in pixels.</param>
    /// <param name="style">Header or footer styling.</param>
    /// <param name="size">The text size, in design units.</param>
    /// <returns>The cap's box.</returns>
    public static UiRect KeyCap(UiPainter painter, UiTheme theme, string key, UiVec2 topLeft, KeyCapStyle style = KeyCapStyle.Footer, float size = 11f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        UiVec2 extent = MeasureKeyCap(context, theme, key, size);
        var box = new UiRect(topLeft.X, topLeft.Y, extent.X, extent.Y);
        painter.StrokeRect(box, context.Px(1f), UiPalette.White(style == KeyCapStyle.Footer ? 0.22f : 0.18f));
        float px = context.Px(size);
        painter.DrawText(
            theme.Mono,
            key,
            new UiVec2(box.X + context.Px(7f), UiRowStyle.CenterY(theme.Mono, px, box)),
            px,
            style == KeyCapStyle.Footer ? theme.Text : theme.TextMuted);
        return box;
    }

    /// <summary>Draws a row of progress segments: so many of a total filled in the accent.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="topLeft">The first segment's top-left, in pixels.</param>
    /// <param name="filled">How many are filled.</param>
    /// <param name="total">How many there are.</param>
    /// <param name="width">Each segment's width, in design units.</param>
    /// <param name="height">Each segment's height, in design units.</param>
    /// <param name="gap">The gap between segments, in design units.</param>
    /// <param name="accent">The filled colour.</param>
    /// <returns>The row's total width, in pixels.</returns>
    public static float Segments(UiPainter painter, UiVec2 topLeft, int filled, int total, float width, float height, float gap, Bgra32 accent)
    {
        ArgumentNullException.ThrowIfNull(painter);
        LayoutContext context = painter.Context;
        float w = context.Px(width);
        float h = context.Px(height);
        float g = context.Px(gap);
        for (int i = 0; i < total; i++)
        {
            painter.FillRect(new UiRect(topLeft.X + (i * (w + g)), topLeft.Y, w, h), i < filled ? accent : UiPalette.White(0.14f));
        }

        return total <= 0 ? 0f : (total * w) + ((total - 1) * g);
    }

    /// <summary>Measures the width of a row of progress segments.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="total">How many there are.</param>
    /// <param name="width">Each segment's width, in design units.</param>
    /// <param name="gap">The gap between segments, in design units.</param>
    /// <returns>The width in pixels.</returns>
    public static float SegmentsWidth(LayoutContext context, int total, float width, float gap)
    {
        ArgumentNullException.ThrowIfNull(context);
        return total <= 0 ? 0f : (total * context.Px(width)) + ((total - 1) * context.Px(gap));
    }

    /// <summary>Draws a pill: a monospaced word on a filled or outlined chip, such as ACTIVE.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The text, in capitals.</param>
    /// <param name="topRight">The pill's top-right, in pixels: pills sit against a right edge.</param>
    /// <param name="fill">The fill, or <see langword="null"/> for an outline in <paramref name="ink"/>.</param>
    /// <param name="ink">The text colour.</param>
    /// <param name="size">The text size, in design units.</param>
    /// <returns>The pill's box.</returns>
    public static UiRect Pill(UiPainter painter, UiTheme theme, string text, UiVec2 topRight, Bgra32? fill, Bgra32 ink, float size = 10f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float px = context.Px(size);
        const float Tracking = 0.22f;
        float width = UiPainter.MeasureLabel(theme.Mono, text, px, Tracking) + context.Px(16f);
        float height = theme.Mono.LineHeight(px) + context.Px(6f);
        var box = new UiRect(topRight.X - width, topRight.Y, width, height);
        if (fill is { } solid)
        {
            painter.FillRect(box, solid);
        }
        else
        {
            painter.StrokeRect(box, context.Px(1f), ink);
        }

        painter.DrawLabel(theme.Mono, text, new UiVec2(box.X + context.Px(8f), UiRowStyle.CenterY(theme.Mono, px, box)), px, ink, Tracking);
        return box;
    }

    /// <summary>Draws a glass panel: the blurred scene behind it, a dark tint and a hairline.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="rect">The panel, in pixels.</param>
    /// <param name="tint">The tint, or <see langword="null"/> for the theme's panel colour.</param>
    /// <param name="blur">The blur in design units, or <see langword="null"/> for the theme's.</param>
    /// <param name="border">The hairline, or <see langword="null"/> for the theme's border.</param>
    public static void Glass(UiPainter painter, UiTheme theme, UiRect rect, Bgra32? tint = null, float? blur = null, Bgra32? border = null)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        painter.DrawBackdrop(rect, painter.Context.Px(blur ?? theme.GlassBlur), tint ?? theme.Panel);
        Bgra32 line = border ?? theme.Border;
        if (line.A > 0)
        {
            painter.StrokeRect(rect, painter.Context.Px(1f), line);
        }
    }

    /// <summary>
    /// Draws the design's backdrop vignette over a scene: darkest on the left where the content sits,
    /// darker at the top under the header and at the bottom over the footer.
    /// </summary>
    /// <param name="painter">The painter.</param>
    /// <param name="rect">The area covered, in pixels.</param>
    /// <param name="style">The menu's vignette or the launcher's, which is a little heavier.</param>
    /// <param name="glow">Whether to add the faint accent glow the menu has in its centre-right.</param>
    public static void Vignette(UiPainter painter, UiRect rect, VignetteStyle style = VignetteStyle.Menu, bool glow = true)
    {
        ArgumentNullException.ThrowIfNull(painter);
        bool launcher = style == VignetteStyle.Launcher;
        Span<UiGradientStop> across =
        [
            new(0f, UiPalette.InkAt(launcher ? 0.94f : 0.92f)),
            new(launcher ? 0.42f : 0.38f, UiPalette.InkAt(launcher ? 0.6f : 0.55f)),
            new(launcher ? 0.75f : 0.7f, UiPalette.InkAt(launcher ? 0.2f : 0.15f)),
            new(1f, UiPalette.InkAt(launcher ? 0.55f : 0.45f)),
        ];
        painter.FillGradient(rect, across, UiGradientDirection.Horizontal);

        Span<UiGradientStop> down =
        [
            new(0f, UiPalette.InkAt(launcher ? 0.9f : 0.85f)),
            new(launcher ? 0.2f : 0.18f, UiPalette.InkAt(0f)),
            new(0.6f, UiPalette.InkAt(0f)),
            new(1f, UiPalette.InkAt(launcher ? 0.92f : 0.9f)),
        ];
        painter.FillGradient(rect, down, UiGradientDirection.Vertical);

        if (glow && !launcher)
        {
            // radial-gradient(ellipse at 65% 45%, accent .06, transparent 55%)
            var center = new UiVec2(rect.X + (rect.Width * 0.65f), rect.Y + (rect.Height * 0.45f));
            // CSS sizes the ellipse to reach the farthest corner (here the bottom-left) and the stop at 55%.
            const float Reach = 0.55f * 1.41421356f;
            Span<UiGradientStop> warm =
            [
                new(0f, UiPalette.AccentAt(0.06f)),
                new(1f, UiPalette.AccentAt(0f)),
            ];
            painter.FillRadialGradient(rect, center, new UiVec2(rect.Width * 0.65f * Reach, rect.Height * 0.55f * Reach), warm);
        }
    }

    /// <summary>Draws display text softened by a blur, for a title resolving into focus.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The font.</param>
    /// <param name="text">The text.</param>
    /// <param name="topLeft">The top-left, in pixels.</param>
    /// <param name="sizePx">The size, in pixels.</param>
    /// <param name="color">The colour.</param>
    /// <param name="trackingEm">The letterspacing, in ems.</param>
    /// <param name="blurPx">The blur radius, in pixels; under half a pixel draws the text sharp.</param>
    /// <remarks>
    /// A ring of faint copies around the true position: the cheapest blur that works identically on
    /// every renderer, and at the sizes it is used for — a title's first few hundred milliseconds — it reads
    /// as a lens pulling focus.
    /// </remarks>
    public static void BlurredText(UiPainter painter, UiFont font, string text, UiVec2 topLeft, float sizePx, Bgra32 color, float trackingEm, float blurPx)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        if (blurPx < 0.5f)
        {
            painter.DrawLabel(font, text, topLeft, sizePx, color, trackingEm);
            return;
        }

        const int Copies = 8;
        float strength = Math.Clamp(blurPx / 10f, 0f, 1f);
        byte centreAlpha = (byte)(color.A * (1f - (0.6f * strength)));
        byte ringAlpha = (byte)(color.A * 0.6f * strength / (Copies * 0.55f));
        for (int i = 0; i < Copies; i++)
        {
            float angle = i * MathF.Tau / Copies;
            var offset = new UiVec2(MathF.Cos(angle) * blurPx * 0.6f, MathF.Sin(angle) * blurPx * 0.6f);
            painter.DrawLabel(font, text, topLeft + offset, sizePx, color.WithAlpha(ringAlpha), trackingEm);
        }

        painter.DrawLabel(font, text, topLeft, sizePx, color.WithAlpha(centreAlpha), trackingEm);
    }

    /// <summary>Draws the accent light sweep that crosses the screen when a mission changes.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="screen">The screen, in pixels.</param>
    /// <param name="progress">How far the sweep has travelled, 0 off the left edge to 1 off the right.</param>
    /// <param name="opacity">The sweep's opacity.</param>
    public static void Sweep(UiPainter painter, UiRect screen, float progress, float opacity)
    {
        ArgumentNullException.ThrowIfNull(painter);
        if (opacity <= 0.002f)
        {
            return;
        }

        LayoutContext context = painter.Context;
        float band = context.Px(420f);

        // The band travels from -420 to the right edge; the line, half a band ahead, to 210 past it.
        float bandX = screen.X - band + ((screen.Width + band) * progress);
        float lineX = screen.X - (band * 0.5f) + ((screen.Width + band) * progress);

        Span<UiGradientStop> wash =
        [
            new(0f, UiPalette.AccentAt(0f)),
            new(0.5f, UiPalette.AccentAt(0.22f * opacity)),
            new(1f, UiPalette.AccentAt(0f)),
        ];
        painter.FillGradient(new UiRect(bandX, screen.Y, band, screen.Height), wash, UiGradientDirection.Horizontal);

        float glow = context.Px(28f);
        Span<UiGradientStop> halo =
        [
            new(0f, UiPalette.AccentAt(0f)),
            new(0.5f, UiPalette.AccentAt(0.6f * opacity)),
            new(1f, UiPalette.AccentAt(0f)),
        ];
        painter.FillGradient(new UiRect(lineX - glow, screen.Y, glow * 2f, screen.Height), halo, UiGradientDirection.Horizontal, UiBlend.Additive);
        painter.FillRect(new UiRect(lineX - context.Px(1f), screen.Y, context.Px(2f), screen.Height), UiPalette.AccentAt(opacity));
    }
}

/// <summary>Which of the design's two backdrop vignettes to draw.</summary>
public enum VignetteStyle
{
    /// <summary>The in-game menu's, with a faint accent glow.</summary>
    Menu,

    /// <summary>The launcher's, a little heavier and without the glow.</summary>
    Launcher,
}
