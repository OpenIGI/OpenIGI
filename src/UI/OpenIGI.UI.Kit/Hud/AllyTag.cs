using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Hud;

/// <summary>
/// An ally's name over their head, set flat on the screen as a modern shooter marks its allies, and kept spare: the name in
/// small white type with a soft shadow under it to read against sky and wall alike, a small marker in the accent pointing
/// down at the player, and -- far off -- how far, quieter beside it. No plate behind it: across a compound of allies, plates
/// were a wall of boxes. It shrinks a little and fades towards <see cref="ReachMetres"/>, so the nearest reads first.
/// </summary>
public static class AllyTag
{
    /// <summary>How far a tag is drawn at all, in metres.</summary>
    public const double ReachMetres = 140.0;

    // The name's size up close, in layout pixels; across the yard it is drawn at FarScale of it.
    private const float Size = 17f;
    private const float FarScale = 0.85f;

    // Out to here a tag keeps its full size; by ReachMetres it has shrunk to the far scale.
    private const double NearMetres = 8.0;

    // The stretch before ReachMetres over which it fades.
    private const double FadeMetres = 30.0;

    // Beyond here a tag says how far as well.
    private const double DistanceShownMetres = 25.0;

    /// <summary>Draws a tag over a head.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="font">The type it is set in.</param>
    /// <param name="head">The point over the player's head, on the screen.</param>
    /// <param name="name">The player's name.</param>
    /// <param name="distanceMetres">How far the player is.</param>
    public static void Draw(UiPainter painter, UiFont font, UiVec2 head, string name, double distanceMetres)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrWhiteSpace(name) || distanceMetres > ReachMetres)
        {
            return;
        }

        LayoutContext context = painter.Context;
        double far = Math.Clamp((distanceMetres - NearMetres) / (ReachMetres - NearMetres), 0.0, 1.0);
        float scale = 1f - ((1f - FarScale) * (float)far);
        float fade = (float)Math.Clamp((ReachMetres - distanceMetres) / FadeMetres, 0.0, 1.0);

        float size = context.Px(Size * scale);
        float marker = context.Px(6f * scale);
        float shadowOffset = MathF.Max(1f, context.Px(1f));
        Bgra32 shade = Bgra32.FromRgba(0, 0, 0, (byte)(200 * fade));

        // The marker: a small triangle pointing down at the head, in the accent.
        float tipY = head.Y - context.Px(4f * scale);
        painter.FillTriangle(
            new UiVec2(head.X - marker, tipY - marker),
            new UiVec2(head.X + marker, tipY - marker),
            new UiVec2(head.X, tipY),
            UiPalette.Accent.WithAlpha((byte)(235 * fade)));

        // The name over it, and far off how far, quieter; each a hair of shadow under it.
        string? howFar = distanceMetres > DistanceShownMetres
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{distanceMetres:0} m")
            : null;
        float farSize = size * 0.85f;
        float gap = size * 0.45f;
        float nameWidth = font.MeasureWidth(name, size);
        float farWidth = howFar is null ? 0f : font.MeasureWidth(howFar, farSize);
        float total = nameWidth + (howFar is null ? 0f : gap + farWidth);
        float lineHeight = size * 1.2f;
        var line = new UiRect(head.X - (total * 0.5f), tipY - marker - context.Px(3f * scale) - lineHeight, total, lineHeight);

        Label(painter, font, name, new UiVec2(line.X, UiRowStyle.CapsCenterY(font, size, line)), size, UiPalette.Text.WithAlpha((byte)(245 * fade)), shade, shadowOffset);
        if (howFar is not null)
        {
            Label(painter, font, howFar, new UiVec2(line.X + nameWidth + gap, UiRowStyle.CapsCenterY(font, farSize, line)), farSize, UiPalette.TextSecondary.WithAlpha((byte)(240 * fade)), shade, shadowOffset);
        }
    }

    // Some words with a hair of shadow under them.
    private static void Label(UiPainter painter, UiFont font, string text, UiVec2 at, float size, Bgra32 colour, Bgra32 shade, float offset)
    {
        painter.DrawLabel(font, text, new UiVec2(at.X + offset, at.Y + offset), size, shade, 0.02f);
        painter.DrawLabel(font, text, at, size, colour, 0.02f);
    }
}
