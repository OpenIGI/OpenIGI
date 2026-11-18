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
    /// <param name="health">Their health, 0 to 1, shown as a thin bar under the name while they are hurt; <see langword="null"/> where not known.</param>
    public static void Draw(UiPainter painter, UiFont font, UiVec2 head, string name, double distanceMetres, float? health = null)
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

        // The marker: a small triangle pointing down at the head, in the accent.
        float tipY = head.Y - context.Px(4f * scale);
        painter.FillTriangle(
            new UiVec2(head.X - marker, tipY - marker),
            new UiVec2(head.X + marker, tipY - marker),
            new UiVec2(head.X, tipY),
            UiPalette.Accent.WithAlpha((byte)(235 * fade)));

        // The name over it, and far off how far, quieter; hurt, a thin bar between the name and the marker -- how much health is
        // left, in its colour. Whole, no bar, so a healthy squad stays quiet.
        bool hurt = health is < 0.995f;
        float barHeight = MathF.Max(2f, context.Px(3f * scale));
        float barSpace = hurt ? barHeight + context.Px(4f * scale) : 0f;
        float top = tipY - marker - context.Px(3f * scale) - barSpace - font.LineHeight(size);
        UiRect line = HudInk.Caption(painter, font, head.X, top, name, distanceMetres > DistanceShownMetres ? HudInk.Distance(distanceMetres) : null, size, fade);
        if (hurt && health is { } left)
        {
            float barWidth = MathF.Max(context.Px(36f), line.Width);
            var track = new UiRect(head.X - (barWidth * 0.5f), line.Bottom + context.Px(2f * scale), barWidth, barHeight);
            painter.FillRect(track, UiPalette.InkAt(0.6f * fade));
            painter.FillRect(new UiRect(track.X, track.Y, track.Width * Math.Clamp(left, 0f, 1f), track.Height), UiPalette.Health(left).WithAlpha((byte)(235 * fade)));
        }
    }
}
