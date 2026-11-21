using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Kit;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// The local player's combat feedback round the crosshair -- the hit marker's four ticks as its rounds strike (red for a kill,
/// wider for the head), and a red glow on a ring round the middle pointing where each shot that struck it came from -- and, in the
/// played title's look, the card a dead player watches from, the round's numbers, and the connection's figures.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    private static Bgra32 HitWhite => Bgra32.FromRgb(255, 255, 255);
    private static Bgra32 HitKill => Bgra32.FromRgb(255, 72, 60);
    private static Bgra32 DamageRed => Bgra32.FromRgb(232, 40, 32);

    /// <inheritdoc/>
    public void DrawSpectating(SpriteBatch batch, SpectateTarget watched, KillFeedView? feed, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        UiRect safe = o.Safe;
        float centre = safe.X + (safe.Width * 0.5f);
        string name = OverlayText.Capitals(watched.Name);
        string line = watched.Killer
            ? string.Create(CultureInfo.InvariantCulture, $"KILLED BY {name} - {watched.Metres:0} M")
            : $"SPECTATING {name}";
        string next = watched.Killer ? "FIRE: WATCH YOUR SIDE" : "FIRE: NEXT PLAYER";
        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);

        // The killer's weapon and how it struck, after the line.
        FeedPart? picture = null;
        bool head = false;
        float extra = watched.Killer ? KilledByWidth(o, feed, out picture, out head) : 0f;
        float lineWidth = o.Text.MeasureAt(line, body) + extra;
        float width = Math.Max(lineWidth, o.Text.MeasureAt(next, small));
        var sheet = new UiRect(centre - (width * 0.5f) - o.Px(22f), safe.Bottom - o.Px(150f), width + o.Px(44f), o.Text.LineHeightAt(body) + o.Text.LineHeightAt(small) + o.Px(26f));
        o.Sheet(sheet);
        float x = centre - (lineWidth * 0.5f);
        float y = sheet.Y + o.Px(9f);
        x += o.Text.DrawAt(line, x, y, body, watched.Killer ? o.Ink.Enemy : o.Ink.Highlight);
        float middle = y + (o.Text.LineHeightAt(body) * 0.5f);
        if (picture is { } shown)
        {
            x += o.Px(14f);
            DrawPart(batch, o, shown, x, middle, 1f);
            x += shown.Width;
        }

        if (head)
        {
            KitIcons.Draw(o.Painter, KitIcon.Headshot, new UiVec2(x + o.Px(20f), middle), o.Px(20f), o.Ink.Accent);
        }

        o.Text.DrawCentredAt(next, centre, y + o.Text.LineHeightAt(body) + o.Px(6f), small, o.Quiet);
    }

    /// <inheritdoc/>
    public void DrawNetGraph(SpriteBatch batch, IReadOnlyList<string> lines, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float small = o.Px(HudOverlay.Small);
        float step = o.Text.LineHeightAt(small) + o.Px(3f);
        float width = lines.Max(l => o.Text.MeasureAt(OverlayText.Capitals(l), small)) + o.Px(24f);
        var plate = new UiRect(o.Safe.Right - width - o.Px(16f), o.Safe.Y + (o.Safe.Height * 0.62f), width, (step * lines.Count) + o.Px(16f));
        o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 190));
        o.Brackets(plate, HudOverlay.Fade(o.Ink.Primary, 0.7f), 4f);
        for (int i = 0; i < lines.Count; i++)
        {
            o.Text.DrawAt(OverlayText.Capitals(lines[i]), plate.X + o.Px(12f), plate.Y + o.Px(8f) + (step * i), small, o.Ink.Primary);
        }
    }

    /// <inheritdoc/>
    public void DrawCombat(SpriteBatch batch, CombatHud hud, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(hud);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        UiRect safe = o.Safe;
        UiVec2 middle = new(safe.X + (safe.Width * 0.5f), safe.Y + (safe.Height * 0.5f));

        // Each mark a short band of the ring, brightest at its inner edge and gone at its outer, with a hard line along it.
        float inner = o.Px(118f);
        foreach (DamageMark mark in hud.Marks)
        {
            byte alpha = (byte)(220 * Math.Clamp(mark.Strength, 0f, 1f));
            const float half = 0.34f;
            o.Painter.FillArc(middle, inner, inner + o.Px(30f), mark.Angle - half, mark.Angle + half, DamageRed.WithAlpha(alpha), DamageRed.WithAlpha(0));
            o.Painter.FillArc(middle, inner, inner + o.Px(3f), mark.Angle - (half * 0.8f), mark.Angle + (half * 0.8f), DamageRed.WithAlpha((byte)Math.Min(255, alpha + 30)));
        }

        // The round's numbers, under the round's card.
        if (hud.Report is { } report)
        {
            string line = string.Create(
                CultureInfo.InvariantCulture,
                $"DAMAGE DEALT {report.Dealt * 100:0} - TAKEN {report.Taken * 100:0} - HITS {report.Hits}");
            float body = o.Px(HudOverlay.Body);
            float width = o.Text.MeasureAt(line, body);
            var plate = new UiRect(middle.X - (width * 0.5f) - o.Px(18f), safe.Y + (safe.Height * 0.66f), width + o.Px(36f), o.Text.LineHeightAt(body) + o.Px(14f));
            o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 190));
            o.Brackets(plate, HudOverlay.Fade(o.Ink.Primary, 0.8f), 5f);
            o.Text.DrawCentredAt(line, middle.X, plate.Y + o.Px(7f), body, o.Ink.Highlight);
        }

        if (hud.Marker is { } marker)
        {
            Bgra32 colour = (marker.Killed ? HitKill : HitWhite).WithAlpha((byte)(255 * Math.Clamp(marker.Strength, 0f, 1f)));
            float near = o.Px(marker.Head ? 9f : 7f);
            float far = o.Px(marker.Head ? 22f : 17f) + (o.Px(4f) * (1f - marker.Strength));
            float width = o.Px(marker.Killed ? 3f : 2.2f);
            for (int corner = 0; corner < 4; corner++)
            {
                float angle = (MathF.PI * 0.25f) + (corner * MathF.PI * 0.5f);
                var along = new UiVec2(MathF.Sin(angle), -MathF.Cos(angle));
                o.Painter.StrokeSoft(
                    new UiVec2(middle.X + (along.X * near), middle.Y + (along.Y * near)),
                    new UiVec2(middle.X + (along.X * far), middle.Y + (along.Y * far)),
                    width,
                    colour);
            }
        }
    }
}
