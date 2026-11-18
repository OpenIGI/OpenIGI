using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Hud;

/// <summary>
/// The player's own spawn protection, under the crosshair while it lasts: SPAWN PROTECTION in the shield's blue over a thin bar
/// running down as its time does, and a quiet line saying firing ends it. No panel, as the other marks over play.
/// </summary>
public static class ShieldHud
{
    /// <summary>Draws the read-out.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme, for its type.</param>
    /// <param name="left">The seconds it has left.</param>
    /// <param name="total">The seconds it lasts in all, which the bar is a share of.</param>
    public static void Draw(UiPainter painter, UiTheme theme, float left, float total)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        if (left <= 0f)
        {
            return;
        }

        LayoutContext context = painter.Context;
        UiRect safe = context.SafeRect;
        float middle = safe.X + (safe.Width * 0.5f);
        float top = safe.Y + (safe.Height * 0.5f) + context.Px(64f);

        // The last half second fades it out rather than cutting it off.
        float opacity = Math.Clamp(left / 0.5f, 0f, 1f);
        Bgra32 shield = UiPalette.Info.WithAlpha((byte)(255 * opacity));

        const string Title = "SPAWN PROTECTION";
        float size = context.Px(14f);
        const float Tracking = 0.22f;
        float width = UiPainter.MeasureLabel(theme.Heading, Title, size, Tracking);
        HudInk.Label(painter, theme.Heading, Title, new UiVec2(middle - (width * 0.5f), top), size, shield, Tracking);

        float barWidth = context.Px(160f);
        var track = new UiRect(middle - (barWidth * 0.5f), top + theme.Heading.LineHeight(size) + context.Px(6f), barWidth, MathF.Max(2f, context.Px(3f)));
        painter.FillRect(track, UiPalette.White(0.18f * opacity));
        painter.FillRect(new UiRect(track.X, track.Y, track.Width * Math.Clamp(left / MathF.Max(0.1f, total), 0f, 1f), track.Height), shield);

        const string Hint = "Firing ends it";
        float hintSize = context.Px(13f);
        float hintWidth = theme.Body.MeasureWidth(Hint, hintSize);
        HudInk.Label(painter, theme.Body, Hint, new UiVec2(middle - (hintWidth * 0.5f), track.Bottom + context.Px(6f)), hintSize, UiPalette.TextSecondary.WithAlpha((byte)(230 * opacity)), 0.01f);
    }
}
