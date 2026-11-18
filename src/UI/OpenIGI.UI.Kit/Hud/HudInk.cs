using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Hud;

/// <summary>
/// The marks the HUD sets over the level with nothing behind them -- an ally's name, a fallen teammate, a ping -- drawn alike:
/// words with a hair of shadow under them, so they read on sky and wall alike; and the arrow a mark held on the screen's edge
/// points the way with, ringed in dark so it reads on bright ground.
/// </summary>
public static class HudInk
{
    /// <summary>Draws words with a hair of shadow under them.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The type.</param>
    /// <param name="text">The words.</param>
    /// <param name="at">Their top left.</param>
    /// <param name="size">Their size, in pixels.</param>
    /// <param name="colour">Their colour; the shadow fades with it.</param>
    /// <param name="tracking">Their letterspacing.</param>
    public static void Label(UiPainter painter, UiFont font, string text, UiVec2 at, float size, Bgra32 colour, float tracking = 0.02f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        float offset = MathF.Max(1f, painter.Context.Px(1f));
        painter.DrawLabel(font, text, new UiVec2(at.X + offset, at.Y + offset), size, Bgra32.FromRgba(0, 0, 0, (byte)(210 * colour.A / 255)), tracking);
        painter.DrawLabel(font, text, at, size, colour, tracking);
    }

    /// <summary>Draws a line of words centred on a point across, with a quieter second part after the first (a name, and how far).</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The type.</param>
    /// <param name="centreX">The line's middle, across.</param>
    /// <param name="top">The line's top.</param>
    /// <param name="first">The first part.</param>
    /// <param name="second">The quieter part, or <see langword="null"/>.</param>
    /// <param name="size">The first part's size, in pixels; the second's is a little smaller.</param>
    /// <param name="opacity">How opaque the line is, 0 to 1.</param>
    /// <returns>The line's box.</returns>
    public static UiRect Caption(UiPainter painter, UiFont font, float centreX, float top, string first, string? second, float size, float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        float secondSize = size * 0.85f;
        float gap = size * 0.45f;
        float firstWidth = font.MeasureWidth(first, size);
        float total = firstWidth + (second is null ? 0f : gap + font.MeasureWidth(second, secondSize));
        float x = centreX - (total * 0.5f);
        Label(painter, font, first, new UiVec2(x, top), size, UiPalette.Text.WithAlpha((byte)(245 * opacity)));
        if (second is not null)
        {
            float y = top + ((font.LineHeight(size) - font.LineHeight(secondSize)) * 0.5f);
            Label(painter, font, second, new UiVec2(x + firstWidth + gap, y), secondSize, UiPalette.TextSecondary.WithAlpha((byte)(240 * opacity)));
        }

        return new UiRect(x, top, total, font.LineHeight(size));
    }

    /// <summary>How far, as a mark says it beside a name: <c>42 m</c>.</summary>
    /// <param name="metres">The distance.</param>
    /// <returns>The words.</returns>
    public static string Distance(double metres) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{metres:0} m");

    /// <summary>
    /// Draws the arrow beside a mark held on the screen's edge, pointing the way to what it marks: just outside a circle
    /// about the mark, ringed in dark.
    /// </summary>
    /// <param name="painter">The painter.</param>
    /// <param name="centre">The mark's middle.</param>
    /// <param name="clear">How far out from the middle the arrow starts.</param>
    /// <param name="way">The way to point, any length.</param>
    /// <param name="colour">Its colour.</param>
    public static void EdgeArrow(UiPainter painter, UiVec2 centre, float clear, UiVec2 way, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(painter);
        float length = MathF.Sqrt((way.X * way.X) + (way.Y * way.Y));
        if (length < 0.001f)
        {
            return;
        }

        LayoutContext context = painter.Context;
        var unit = new UiVec2(way.X / length, way.Y / length);
        var side = new UiVec2(-unit.Y, unit.X);
        float arrow = context.Px(9f);
        var foot = new UiVec2(centre.X + (unit.X * clear), centre.Y + (unit.Y * clear));
        UiVec2[] corners =
        [
            new(foot.X + (unit.X * arrow * 1.3f), foot.Y + (unit.Y * arrow * 1.3f)),
            new(foot.X + (side.X * arrow), foot.Y + (side.Y * arrow)),
            new(foot.X - (side.X * arrow), foot.Y - (side.Y * arrow)),
        ];

        // The dark ring: the same arrow grown about its own middle.
        var middle = new UiVec2((corners[0].X + corners[1].X + corners[2].X) / 3f, (corners[0].Y + corners[1].Y + corners[2].Y) / 3f);
        UiVec2 Grown(UiVec2 corner) => new(middle.X + ((corner.X - middle.X) * 1.4f), middle.Y + ((corner.Y - middle.Y) * 1.4f));
        painter.FillTriangle(Grown(corners[0]), Grown(corners[1]), Grown(corners[2]), UiPalette.InkAt(0.75f * colour.A / 255f));
        painter.FillTriangle(corners[0], corners[1], corners[2], colour);
    }
}
