using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Servers;

/// <summary>
/// The mark of an official server -- one of ours, whose matches count toward the stats and leaderboards: a tick and
/// OFFICIAL in the accent, in a box outlined in it, the same make as a mode's badge. Wherever the multiplayer page names a
/// server it draws this after the name.
/// </summary>
public static class OfficialMark
{
    private const string Text = "OFFICIAL";
    private const float Tracking = 0.08f;

    /// <summary>Gets the mark's width, to keep room for it after a name.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="theme">The theme.</param>
    /// <returns>The width, in pixels.</returns>
    public static float Width(LayoutContext context, UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(theme);
        return context.Px(30f) + UiPainter.MeasureLabel(theme.Heading, Text, context.Px(11f), Tracking);
    }

    /// <summary>Draws the mark, its left edge at a point and its middle on a line.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="leftMiddle">Its left edge, at the height of its middle.</param>
    /// <param name="appear">How far it has faded in, 0 to 1.</param>
    /// <returns>Its box.</returns>
    public static UiRect Draw(UiPainter painter, UiTheme theme, UiVec2 leftMiddle, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float height = context.Px(20f);
        var box = new UiRect(leftMiddle.X, leftMiddle.Y - (height / 2f), Width(context, theme), height);
        Bgra32 colour = UiPalette.Fade(theme.Accent, appear);
        float tick = context.Px(12f);
        painter.FillRect(box, UiPalette.AccentAt(0.12f * appear));
        painter.StrokeRect(box, context.Px(1f), colour);
        KitIcons.Draw(painter, KitIcon.Check, new UiVec2(box.X + context.Px(11f), box.Center.Y), tick, colour);
        float size = context.Px(11f);
        painter.DrawLabel(theme.Heading, Text, new UiVec2(box.X + context.Px(21f), UiRowStyle.CapsCenterY(theme.Heading, size, box)), size, colour, Tracking);
        return box;
    }
}
