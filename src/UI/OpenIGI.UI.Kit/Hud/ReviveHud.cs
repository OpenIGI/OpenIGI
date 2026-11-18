using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Hud;

/// <summary>
/// Co-op's revival on the HUD, kept spare: over a fallen teammate a small dark disc with a cross, its ring filling in the
/// accent as they are brought back and their name under it; and for the player who is down, low on the screen and clear of
/// the crosshair, a word (DOWN, or BEING REVIVED) over a thin bar and one quiet line -- no panel behind either, so neither
/// stands between the player and the fight.
/// </summary>
public static class ReviveHud
{
    // The marker's disc and ring, in layout pixels.
    private const float DiscRadius = 20f;
    private const float RingWidth = 3f;

    // The downed read-out's bar, in layout pixels.
    private const float BarWidth = 340f;
    private const float BarHeight = 4f;

    /// <summary>Draws the marker over a fallen teammate.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for its type.</param>
    /// <param name="centre">Where the marker stands on the screen: over where the teammate lies.</param>
    /// <param name="name">The teammate's name.</param>
    /// <param name="progress">How far their revival has run, nought to one.</param>
    public static void DrawMarker(UiPainter painter, UiTheme theme, UiVec2 centre, string name, float progress)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float radius = context.Px(DiscRadius);
        float ring = context.Px(RingWidth);
        progress = Math.Clamp(progress, 0f, 1f);

        // The disc, its ring's track, and the ring filled as far as the revival has run.
        painter.FillDot(centre, radius, UiPalette.InkAt(0.72f));
        painter.FillArc(centre, radius - ring, radius, 0f, MathF.Tau, UiPalette.White(0.22f));
        if (progress > 0f)
        {
            painter.FillArc(centre, radius - ring, radius, 0f, MathF.Tau * progress, UiPalette.Accent);
        }

        // The cross: white while nobody is at them, the accent once someone is.
        Bgra32 cross = progress > 0f ? UiPalette.Accent : UiPalette.Text;
        float arm = radius * 0.42f;
        float thick = MathF.Max(2f, radius * 0.2f);
        painter.FillRect(new UiRect(centre.X - arm, centre.Y - (thick * 0.5f), arm * 2f, thick), cross);
        painter.FillRect(new UiRect(centre.X - (thick * 0.5f), centre.Y - arm, thick, arm * 2f), cross);

        // The name under it, a hair of shadow under that.
        float size = context.Px(16f);
        float width = theme.Body.MeasureWidth(name, size);
        var at = new UiVec2(centre.X - (width * 0.5f), centre.Y + radius + context.Px(5f));
        float offset = MathF.Max(1f, context.Px(1f));
        painter.DrawLabel(theme.Body, name, new UiVec2(at.X + offset, at.Y + offset), size, Bgra32.FromRgba(0, 0, 0, 200), 0.02f);
        painter.DrawLabel(theme.Body, name, at, size, UiPalette.Text, 0.02f);
    }

    /// <summary>Draws the read-out of the player who is down.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for its type.</param>
    /// <param name="progress">How far a teammate has got with reviving them, nought to one; nought while nobody is.</param>
    public static void DrawDowned(UiPainter painter, UiTheme theme, float progress)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        UiRect safe = context.SafeRect;
        float middle = safe.X + (safe.Width * 0.5f);
        float top = safe.Y + (safe.Height * 0.66f);
        bool reviving = progress > 0f;
        Bgra32 shade = Bgra32.FromRgba(0, 0, 0, 220);
        float offset = MathF.Max(1f, context.Px(1f));

        // The word, letterspaced, in the alarm colour while down and the accent while being brought back.
        string title = reviving ? "BEING REVIVED" : "DOWN";
        float titleSize = context.Px(26f);
        const float Tracking = 0.14f;
        float titleWidth = UiPainter.MeasureLabel(theme.Heading, title, titleSize, Tracking);
        var titleAt = new UiVec2(middle - (titleWidth * 0.5f), top);
        painter.DrawLabel(theme.Heading, title, new UiVec2(titleAt.X + offset, titleAt.Y + offset), titleSize, shade, Tracking);
        painter.DrawLabel(theme.Heading, title, titleAt, titleSize, reviving ? UiPalette.Accent : UiPalette.Danger, Tracking);

        // The bar: a thin track, filled as far as the revival has run.
        float barWidth = MathF.Min(context.Px(BarWidth), safe.Width * 0.6f);
        var track = new UiRect(middle - (barWidth * 0.5f), top + theme.Heading.LineHeight(titleSize) + context.Px(10f), barWidth, MathF.Max(2f, context.Px(BarHeight)));
        painter.FillRect(track, UiPalette.White(0.18f));
        if (reviving)
        {
            painter.FillRect(new UiRect(track.X, track.Y, track.Width * Math.Clamp(progress, 0f, 1f), track.Height), UiPalette.Accent);
        }

        // One quiet line under it.
        string hint = reviving ? "Stay put, a teammate is with you" : "A teammate can bring you back  ·  Fire to watch another";
        float hintSize = context.Px(17f);
        float hintWidth = theme.Body.MeasureWidth(hint, hintSize);
        var hintAt = new UiVec2(middle - (hintWidth * 0.5f), track.Bottom + context.Px(10f));
        painter.DrawLabel(theme.Body, hint, new UiVec2(hintAt.X + offset, hintAt.Y + offset), hintSize, shade, 0.01f);
        painter.DrawLabel(theme.Body, hint, hintAt, hintSize, UiPalette.Text.WithAlpha(215), 0.01f);
    }
}
