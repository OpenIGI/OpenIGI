using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// Co-op's revival, and the spawn shield, in the played title's look and kept spare: over a fallen teammate -- wherever they
/// are, through walls, pinned to the screen's edge with an arrow when out of view -- the theme's diamond with a cross, filling
/// in the accent from its foot as they are brought back, their name and how far under it; a line at the top when one goes down;
/// and for the player who is down, low on the screen and clear of the crosshair, a word over the title's meter and one quiet
/// line. No sheet behind any of it, so none stands between the player and the fight.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // The marker's diamond, in layout pixels from its middle to a point.
    private const float ReviveDiamond = 22f;

    /// <inheritdoc/>
    public void DrawReviveMarker(
        SpriteBatch batch,
        ReviveMarker marker,
        in Camera3D eye,
        float horizontalTangent,
        float aspectRatio,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        // Wherever they lie: in view where they are, else pinned to the screen's edge pointing the way.
        WorldCanvas canvas = new(marker.AnchorMetres, eye, horizontalTangent, aspectRatio, screenWidth, screenHeight, MetresPerUnit);
        bool inView = canvas.PinToScreen(o.Px(64f), out float x, out float y);
        var centre = new UiVec2(x, y);
        float radius = o.Px(ReviveDiamond);
        float progress = Math.Clamp(marker.Progress, 0f, 1f);
        bool reviving = progress > 0f;

        // The diamond: dark, its rim in the title's colour, filled in the accent from its foot as far as the revival has run.
        o.Diamond(centre, radius + o.Px(3f), Bgra32.FromRgba(0, 0, 0, 170));
        o.Diamond(centre, radius, reviving ? o.Ink.Accent : o.Ink.Primary);
        o.Diamond(centre, radius - MathF.Max(2f, o.Px(3f)), Bgra32.FromRgba(0, 0, 0, 200));
        if (reviving)
        {
            float filled = radius * 2f * progress;
            o.Painter.PushClip(new UiRect(centre.X - radius, centre.Y + radius - filled, radius * 2f, filled));
            o.Diamond(centre, radius - MathF.Max(2f, o.Px(3f)), HudOverlay.Fade(o.Ink.Accent, 0.55f));
            o.Painter.PopClip();
        }

        // The cross: the title's bright ink while nobody is at them, the accent once someone is.
        Bgra32 cross = reviving ? o.Ink.Accent : o.Ink.Highlight;
        float arm = radius * 0.4f;
        float thick = MathF.Max(2f, radius * 0.17f);
        o.Painter.FillRect(new UiRect(centre.X - arm, centre.Y - (thick * 0.5f), arm * 2f, thick), cross);
        o.Painter.FillRect(new UiRect(centre.X - (thick * 0.5f), centre.Y - arm, thick, arm * 2f), cross);

        if (!inView)
        {
            o.EdgeArrow(centre, radius + o.Px(6f), new UiVec2(x - (screenWidth * 0.5f), y - (screenHeight * 0.5f)), cross);
        }

        o.Caption(centre.X, centre.Y + radius + o.Px(6f), marker.Name, HudOverlay.Distance(canvas.DistanceMetres), o.Ink.Ally);
    }

    /// <inheritdoc/>
    public void DrawDownedAlert(SpriteBatch batch, int index, string name, double distanceMetres, float opacity, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o || opacity <= 0f)
        {
            return;
        }

        // Where the mission's own messages run, under them, newest first: the enemy's diamond, who, and how far.
        float size = o.Px(HudOverlay.Body);
        float line = o.Text.LineHeightAt(size) + o.Px(8f);
        float top = o.Safe.Y + (o.Safe.Height * 0.16f) + (index * line);
        float centre = o.Safe.X + (o.Safe.Width * 0.5f);
        UiRect said = o.Caption(centre + o.Px(10f), top, name + " IS DOWN", HudOverlay.Distance(distanceMetres), o.Ink.Enemy, opacity);
        var mark = new UiVec2(said.X - o.Px(12f), top + (said.Height * 0.5f));
        o.Diamond(mark, o.Px(7f), HudOverlay.Fade(Bgra32.FromRgba(0, 0, 0, 180), opacity));
        o.Diamond(mark, o.Px(5f), HudOverlay.Fade(o.Ink.Enemy, opacity));
    }

    /// <inheritdoc/>
    /// <remarks>Low and centred, clear of the crosshair, with nothing behind it.</remarks>
    public void DrawDowned(SpriteBatch batch, float progress, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        bool reviving = progress > 0f;
        float middle = o.Safe.X + (o.Safe.Width * 0.5f);
        float top = o.Safe.Y + (o.Safe.Height * 0.66f);
        float large = o.Px(HudOverlay.Large);
        o.Text.DrawCentredAt(reviving ? "BEING REVIVED" : "DOWN", middle, top, large, reviving ? o.Ink.Accent : o.Ink.Danger, shadow: true);

        float barWidth = MathF.Min(o.Px(340f), o.Safe.Width * 0.6f);
        var track = new UiRect(middle - (barWidth * 0.5f), top + o.Text.LineHeightAt(large) + o.Px(10f), barWidth, MathF.Max(2f, o.Px(4f)));
        o.Meter(track, reviving ? progress : 0f, o.Ink.Accent);

        string hint = reviving ? "STAY PUT, A TEAMMATE IS WITH YOU" : "A TEAMMATE CAN BRING YOU BACK - FIRE TO WATCH ANOTHER";
        o.Text.DrawCentredAt(hint, middle, track.Bottom + o.Px(12f), o.Px(HudOverlay.Small), o.Ink.Highlight, shadow: true);
    }

    /// <inheritdoc/>
    public void DrawShield(SpriteBatch batch, float left, float total, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (left <= 0f || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        // Under the crosshair while it lasts, its last half second fading it out rather than cutting it off.
        float opacity = Math.Clamp(left / 0.5f, 0f, 1f);
        float middle = o.Safe.X + (o.Safe.Width * 0.5f);
        float top = o.Safe.Y + (o.Safe.Height * 0.5f) + o.Px(64f);
        float small = o.Px(HudOverlay.Small);
        o.Text.DrawCentredAt("SPAWN PROTECTION", middle, top, small, HudOverlay.Fade(o.Ink.Ally, opacity), shadow: true);
        float barWidth = o.Px(160f);
        var track = new UiRect(middle - (barWidth * 0.5f), top + o.Text.LineHeightAt(small) + o.Px(6f), barWidth, MathF.Max(2f, o.Px(3f)));
        o.Painter.PushOpacity(opacity);
        o.Meter(track, left / MathF.Max(0.1f, total), o.Ink.Ally);
        o.Painter.PopOpacity();
        o.Text.DrawCentredAt("FIRING ENDS IT", middle, track.Bottom + o.Px(8f), small, HudOverlay.Fade(o.Quiet, opacity), shadow: true);
    }
}
