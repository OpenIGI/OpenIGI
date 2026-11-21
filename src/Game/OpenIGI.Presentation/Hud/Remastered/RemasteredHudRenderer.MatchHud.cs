using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// A multiplayer mode's HUD, in the played title's look: the score strip across the top (each side's score and players in its ink,
/// the clock between on the title's glass, the bomb's fuse in its place once planted), the role and the mode under it, a plant or
/// defuse under way and what the player can do where it stands under the crosshair, and a decided round's or match's card across
/// the middle on the title's glass band.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    /// <inheritdoc/>
    public void DrawMatchHud(SpriteBatch batch, MatchHud hud, string? warning, string useKey, int screenWidth, int screenHeight, string buyKey = "G")
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(hud);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        UiRect safe = o.Safe;
        float centre = safe.X + (safe.Width * 0.5f);
        float top = safe.Y + o.Px(12f);
        float stripBottom = hud.HasTeams ? TeamStrip(o, hud, centre, top) : SoloStrip(o, hud, centre, top);

        // Under the strip: the mode, the target, the player's role; then the lines of the moment.
        float y = Caption(o, hud, centre, stripBottom + o.Px(8f));
        Bgra32 dark = Bgra32.FromRgba(0, 0, 0, 180);
        if (hud.Planted.Length > 0 && hud.Result is null)
        {
            bool attacking = hud.Role == "ATTACK";
            string line = hud.Role is null ? $"BOMB PLANTED AT {hud.Planted}" : attacking ? $"DEFEND THE BOMB AT {hud.Planted}" : $"DEFUSE THE BOMB AT {hud.Planted}";
            y = Chip(o, line, centre, y, dark, HudOverlay.Fade(o.Ink.Enemy, 0.7f + (0.3f * o.Pulse)));
        }
        else if (hud.Notice is { } notice)
        {
            y = Chip(o, notice, centre, y, dark, o.Ink.Highlight);
        }

        if (hud.TeamNote is { } note)
        {
            y = Chip(o, note, centre, y, dark, o.Ink.Ally);
        }

        if (hud.BombNote is { } carried)
        {
            y = Chip(o, carried, centre, y, dark, hud.BombMarkAt is null ? o.Ink.Highlight : o.Ink.Enemy);
        }

        if (hud.FreeLoadout && hud.Money is null)
        {
            _ = Chip(o, $"{buyKey}  LOADOUT", centre, y, dark, o.Ink.Accent);
        }

        if (hud.Money is { } money)
        {
            string cash = string.Create(CultureInfo.InvariantCulture, $"${money:N0}");
            if (hud.MoneyChange is { } change)
            {
                cash += string.Create(CultureInfo.InvariantCulture, $"   {(change > 0 ? "+" : "-")}${Math.Abs(change):N0}");
            }

            string line = hud.BuyClock is { } buy ? $"{cash} - {buyKey}  BUY  {buy}" : cash;
            _ = Chip(o, line, centre, y, hud.BuyClock is null ? dark : HudOverlay.Fade(o.Ink.Accent, 0.85f), hud.BuyClock is null ? o.Ink.Accent : Bgra32.FromRgb(10, 10, 8));
        }

        // Under the crosshair: the plant or defuse under way, or what the player can do here.
        float middle = safe.Y + (safe.Height * 0.5f);
        if (hud.Progress is { } progress)
        {
            ProgressBar(o, hud.ProgressLabel ?? string.Empty, progress, centre, middle + o.Px(70f), hud.ProgressLabel?.StartsWith("DEFUS", StringComparison.Ordinal) == true ? o.Ink.Ally : o.Ink.Enemy);
        }
        else if (hud.Hint is { } hint)
        {
            Hint(o, hint, useKey, centre, middle + o.Px(70f));
        }

        if (hud.Result is { } result)
        {
            ResultBand(o, result, hud.Phase == MatchPhase.Intermission, centre, raised: hud.MapVote is not null);
        }

        if (warning is not null)
        {
            var band = new UiRect(safe.X, safe.Y + (safe.Height * 0.28f), safe.Width, o.Px(56f));
            o.Band(band, HudOverlay.Fade(o.Ink.Danger, 0.35f + (0.25f * o.Pulse)));
            float large = o.Px(HudOverlay.Large);
            o.Text.DrawCentredAt(o.Text.Capitals(warning), centre, band.Y + ((band.Height - o.Text.LineHeightAt(large)) * 0.5f), large, o.Ink.Highlight, shadow: true);
        }
    }

    // The two teams either side of the clock, each its score and its players; returns the strip's bottom.
    private static float TeamStrip(HudOverlay o, MatchHud hud, float centre, float top)
    {
        float height = o.Px(52f);
        float clockWidth = o.Px(128f);
        float scoreWidth = o.Px(64f);
        TeamStrip left = hud.Left!;
        TeamStrip right = hud.Right!;
        Bgra32 leftInk = left.IsOwn || !right.IsOwn ? o.Ink.Ally : o.Ink.Enemy;
        Bgra32 rightInk = left.IsOwn || !right.IsOwn ? o.Ink.Enemy : o.Ink.Ally;

        var clock = new UiRect(centre - (clockWidth * 0.5f), top, clockWidth, height);
        ClockPlate(o, clock, hud, centre);

        // Each side's score on a plate of its ink, then its players outward from it.
        var leftScore = new UiRect(clock.X - scoreWidth - o.Px(4f), top, scoreWidth, height);
        var rightScore = new UiRect(clock.Right + o.Px(4f), top, scoreWidth, height);
        ScoreBlock(o, leftScore, left.Score, leftInk);
        ScoreBlock(o, rightScore, right.Score, rightInk);
        Pips(o, left.Alive, leftScore.X - o.Px(10f), top + o.Px(8f), -1, leftInk);
        Pips(o, right.Alive, rightScore.Right + o.Px(10f), top + o.Px(8f), 1, rightInk);

        // The teams' names under their scores.
        float small = o.Px(HudOverlay.Small);
        o.Text.DrawCentredAt(o.Text.Capitals(left.Name), leftScore.X + (scoreWidth * 0.5f), top + height + o.Px(4f), small, leftInk, shadow: true);
        o.Text.DrawCentredAt(o.Text.Capitals(right.Name), rightScore.X + (scoreWidth * 0.5f), top + height + o.Px(4f), small, rightInk, shadow: true);
        return top + height + o.Text.LineHeightAt(small) + o.Px(8f);
    }

    // The clock on the title's glass, or the bomb's fuse in its place, with what it counts under it.
    private static void ClockPlate(HudOverlay o, UiRect clock, MatchHud hud, float centre)
    {
        o.Plate(clock, Bgra32.FromRgba(0, 0, 0, 200));
        o.Plate(clock, HudOverlay.Fade(o.Ink.Glass.WithAlpha(255), 0.4f));
        o.Brackets(clock, HudOverlay.Fade(o.Ink.Primary, 0.85f), 6f);
        float large = o.Px(HudOverlay.Large);
        float small = o.Px(HudOverlay.Small);
        float figureY = clock.Y + o.Px(4f);
        float underY = clock.Bottom - o.Text.LineHeightAt(small) - o.Px(5f);
        if (hud.Fuse is { } fuse)
        {
            Bgra32 hot = UiGeometry.Lerp(o.Ink.Danger, o.Ink.Highlight, o.Pulse * 0.35f);
            o.Diamond(new UiVec2(clock.X + o.Px(20f), clock.Y + (clock.Height * 0.4f)), o.Px(7f + (2f * o.Pulse)), hot);
            o.Text.DrawCentredAt(fuse, clock.X + (clock.Width * 0.58f), figureY, large, hot);
            o.Meter(new UiRect(clock.X + o.Px(6f), clock.Bottom - o.Px(5f), (clock.Width - o.Px(12f)) * 1f, MathF.Max(2f, o.Px(3f))), hud.FuseFraction, hot);
            return;
        }

        Bgra32 ink = hud.ClockUrgent ? UiGeometry.Lerp(o.Ink.Danger, o.Ink.Highlight, o.Pulse * 0.4f) : o.Ink.Highlight;
        o.Text.DrawCentredAt(hud.Clock ?? "--:--", centre, figureY, large, ink);
        string under = hud.Round > 0 ? $"ROUND {hud.Round}" : hud.Phase == MatchPhase.Warmup ? "WARMUP" : "TIME";
        o.Text.DrawCentredAt(under, centre, underY, small, o.Quiet);
    }

    // A free-for-all: the player's own score and place, the clock, and the one to beat; returns the strip's bottom.
    private static float SoloStrip(HudOverlay o, MatchHud hud, float centre, float top)
    {
        float height = o.Px(52f);
        float clockWidth = o.Px(128f);
        float side = o.Px(170f);
        var clock = new UiRect(centre - (clockWidth * 0.5f), top, clockWidth, height);
        ClockPlate(o, clock, hud, centre);

        float small = o.Px(HudOverlay.Small);
        float large = o.Px(HudOverlay.Large);
        bool leading = hud.OwnRank == 1;
        var own = new UiRect(clock.X - side - o.Px(4f), top, side, height);
        Bgra32 ownInk = leading ? o.Ink.Accent : o.Ink.Ally;
        o.Plate(own, Bgra32.FromRgba(0, 0, 0, 190));
        o.Plate(own, HudOverlay.Fade(ownInk, 0.18f));
        o.Brackets(own, ownInk, 6f);
        o.Text.DrawAt($"YOU - {Place(hud.OwnRank)}", own.X + o.Px(10f), own.Y + o.Px(5f), small, ownInk);
        o.Text.DrawRightAt(hud.OwnScore.ToString(CultureInfo.InvariantCulture), own.Right - o.Px(10f), own.Bottom - o.Text.LineHeightAt(large) - o.Px(3f), large, o.Ink.Highlight);

        var rival = new UiRect(clock.Right + o.Px(4f), top, side, height);
        o.Plate(rival, Bgra32.FromRgba(0, 0, 0, 190));
        o.Brackets(rival, HudOverlay.Fade(o.Ink.Enemy, 0.85f), 6f);
        string who = hud.LeaderName.Length > 0 ? (leading ? $"NEXT - {hud.LeaderName}" : $"LEADER - {hud.LeaderName}") : "NO RIVAL YET";
        o.Text.DrawAt(o.Text.ClipAt(o.Text.Capitals(who), small, side - o.Px(20f)), rival.X + o.Px(10f), rival.Y + o.Px(5f), small, o.Ink.Enemy);
        o.Text.DrawAt(hud.LeaderScore.ToString(CultureInfo.InvariantCulture), rival.X + o.Px(10f), rival.Bottom - o.Text.LineHeightAt(large) - o.Px(3f), large, o.Ink.Highlight);
        return top + height + o.Px(6f);
    }

    // The mode, what wins it, and the player's role, in one line under the strip; returns the y below it.
    private static float Caption(HudOverlay o, MatchHud hud, float centre, float y)
    {
        float small = o.Px(HudOverlay.Small);
        string target = hud.Target <= 0 ? string.Empty : hud.Round > 0 ? $"FIRST TO {hud.Target}" : $"FIRST TO {hud.Target} KILLS";
        string title = o.Text.Capitals(hud.Title);
        string line = target.Length > 0 ? $"{title} - {target}" : title;
        float width = o.Text.MeasureAt(line, small);
        float roleWidth = hud.Role is { } role ? o.Text.MeasureAt(role, small) + o.Px(10f) + o.Px(10f) : 0f;
        float x = centre - ((width + roleWidth) * 0.5f);
        if (hud.Role is { } shown)
        {
            x = o.Tag(shown, x, y - o.Px(2f), shown == "ATTACK" ? o.Ink.Enemy : o.Ink.Ally, filled: true) + o.Px(10f);
        }

        o.Text.DrawAt(line, x, y, small, o.Quiet, shadow: true);
        return y + o.Text.LineHeightAt(small) + o.Px(12f);
    }

    // A side's score on a plate of its ink, cut the title's way.
    private static void ScoreBlock(HudOverlay o, UiRect rect, int score, Bgra32 ink)
    {
        o.Plate(rect, HudOverlay.Fade(ink, 0.85f));
        float large = o.Px(HudOverlay.Large);
        o.Text.DrawCentredAt(score.ToString(CultureInfo.InvariantCulture), rect.X + (rect.Width * 0.5f), rect.Y + ((rect.Height - o.Text.LineHeightAt(large)) * 0.5f), large, Bgra32.FromRgb(8, 10, 8));
    }

    // A side's players as the theme's diamonds running outward from its score: lit while standing, hollow when down.
    private static void Pips(HudOverlay o, IReadOnlyList<bool> alive, float from, float top, int direction, Bgra32 ink)
    {
        float radius = o.Px(7f);
        float step = (radius * 2f) + o.Px(3f);
        int count = Math.Min(alive.Count, 16);
        for (int i = 0; i < count; i++)
        {
            float x = direction < 0 ? from - (i * step) - radius : from + (i * step) + radius;
            var at = new UiVec2(x, top + o.Px(17f));
            o.Diamond(at, radius + o.Px(1.5f), Bgra32.FromRgba(0, 0, 0, 170));
            o.Diamond(at, radius, alive[i] ? ink : HudOverlay.Fade(ink, 0.35f));
            if (!alive[i])
            {
                o.Diamond(at, radius * 0.55f, Bgra32.FromRgba(0, 0, 0, 200));
            }
        }
    }

    // A line on a small plate, centred; returns the y below it.
    private static float Chip(HudOverlay o, string text, float centre, float y, Bgra32 face, Bgra32 ink)
    {
        float small = o.Px(HudOverlay.Small);
        string said = o.Text.Capitals(text);
        float width = o.Text.MeasureAt(said, small) + o.Px(28f);
        var plate = new UiRect(centre - (width * 0.5f), y, width, o.Text.LineHeightAt(small) + o.Px(10f));
        o.Plate(plate, face);
        o.Brackets(plate, HudOverlay.Fade(ink, 0.7f), 4f);
        o.Text.DrawCentredAt(said, centre, y + o.Px(5f), small, ink);
        return plate.Bottom + o.Px(6f);
    }

    // A plant or defuse under way: what, the title's meter, and how far.
    private static void ProgressBar(HudOverlay o, string label, float progress, float centre, float y, Bgra32 ink)
    {
        float width = o.Px(340f);
        float x = centre - (width * 0.5f);
        float small = o.Px(HudOverlay.Small);
        var plate = new UiRect(x - o.Px(14f), y - o.Px(10f), width + o.Px(28f), o.Text.LineHeightAt(small) + o.Px(34f));
        o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 190));
        o.Brackets(plate, HudOverlay.Fade(o.Ink.Primary, 0.8f), 5f);
        o.Text.DrawAt(o.Text.Capitals(label), x, y - o.Px(2f), small, o.Ink.Highlight);
        o.Text.DrawRightAt(string.Create(CultureInfo.InvariantCulture, $"{(int)(Math.Clamp(progress, 0f, 1f) * 100f)}%"), x + width, y - o.Px(2f), small, ink);
        o.Meter(new UiRect(x, y + o.Text.LineHeightAt(small) + o.Px(6f), width, MathF.Max(3f, o.Px(6f))), progress, ink);
    }

    // What the player can do here: HOLD [E] TO PLANT THE BOMB AT A.
    private static void Hint(HudOverlay o, ModeHint hint, string key, float centre, float y)
    {
        float body = o.Px(HudOverlay.Body);
        string verb = o.Text.Capitals(hint.Verb);
        string action = o.Text.Capitals(hint.Action);
        float gap = o.Px(10f);
        float keyWidth = hint.Keyed ? o.KeyCapWidth(key) : 0f;
        float whole = o.Text.MeasureAt(verb, body) + gap + (hint.Keyed ? keyWidth + gap : 0f) + o.Text.MeasureAt(action, body);
        float height = MathF.Max(o.KeyCapHeight, o.Text.LineHeightAt(body)) + o.Px(12f);
        var plate = new UiRect(centre - (whole * 0.5f) - o.Px(16f), y - o.Px(6f), whole + o.Px(32f), height);
        o.Plate(plate, Bgra32.FromRgba(0, 0, 0, 190));
        o.Brackets(plate, HudOverlay.Fade(o.Ink.Primary, 0.8f), 5f);
        float textY = plate.Y + ((plate.Height - o.Text.LineHeightAt(body)) * 0.5f);
        float x = plate.X + o.Px(16f);
        x += o.Text.DrawAt(verb, x, textY, body, o.Quiet) + gap;
        if (hint.Keyed)
        {
            x = o.KeyCap(key, x, plate.Y + ((plate.Height - o.KeyCapHeight) * 0.5f), lit: true).Right + gap;
        }

        o.Text.DrawAt(action, x, textY, body, o.Ink.Highlight);
    }

    // A decided round's or match's card: the title's glass band across the screen, edged in the outcome's ink, the result large on
    // it. Raised to just under the score strip while the vote for the next map and the scoreboard share the screen under it.
    private static void ResultBand(HudOverlay o, ResultCard result, bool match, float centre, bool raised = false)
    {
        UiRect safe = o.Safe;
        Bgra32 ink = result.Outcome > 0 ? (match ? o.Ink.Accent : o.Ink.Ally) : result.Outcome < 0 ? o.Ink.Enemy : o.Ink.Highlight;
        float bandHeight = o.Px(match ? MatchCardHeight : 118f);
        var band = new UiRect(safe.X, raised ? safe.Y + o.Px(IntermissionCardTop) : safe.Y + (safe.Height * 0.3f), safe.Width, bandHeight);
        o.GlassBand(band, ink);

        float huge = o.Px(HudOverlay.Huge);
        o.Text.DrawCentredAt(o.Text.Capitals(result.Title), centre, band.Y + o.Px(match ? 18f : 14f), huge, ink, shadow: true);
        bool summed = result.Summary is not null;
        float body = o.Px(HudOverlay.Body);
        if (result.Detail.Length > 0)
        {
            o.Text.DrawCentredAt(o.Text.Capitals(result.Detail), centre, band.Bottom - o.Px(summed ? 86f : 36f), body, o.Ink.Highlight, shadow: true);
        }

        if (result.Summary is { } summary)
        {
            SummaryRow(o, centre, band.Bottom - o.Px(58f), summary.Place, summary.Players, summary.Kills, summary.Deaths, summary.Ratio, summary.Best);
        }
    }

    // The player's own match in numbers across the card at its end: where it placed, its kills, deaths and kills a death, each
    // figure over its small label, with MVP first in the accent for the one who finished on top.
    private static void SummaryRow(HudOverlay o, float centre, float top, int place, int players, int kills, int deaths, string ratio, bool best)
    {
        (string Label, string Value, bool Lit)[] figures =
        [
            ("PLACE", string.Create(CultureInfo.InvariantCulture, $"{Place(place)}/{players}"), best),
            ("KILLS", kills.ToString(CultureInfo.InvariantCulture), false),
            ("DEATHS", deaths.ToString(CultureInfo.InvariantCulture), false),
            ("K/D", ratio, false),
        ];

        float large = o.Px(HudOverlay.Large);
        float small = o.Px(HudOverlay.Small);
        float column = o.Px(124f);
        float x = centre - (((figures.Length * column) + (best ? column : 0f)) * 0.5f);
        if (best)
        {
            o.Tag("MVP", x + (column * 0.5f) - ((o.Text.MeasureAt("MVP", small) + o.Px(10f)) * 0.5f), top + o.Px(8f), o.Ink.Accent, filled: true);
            x += column;
        }

        foreach ((string label, string value, bool lit) in figures)
        {
            float middle = x + (column * 0.5f);
            o.Text.DrawCentredAt(value, middle, top, large, lit ? o.Ink.Accent : o.Ink.Highlight, shadow: true);
            o.Text.DrawCentredAt(label, middle, top + o.Text.LineHeightAt(large) + o.Px(2f), small, o.Quiet);
            x += column;
        }
    }

    // A place said as the card says it: 1ST, 2ND, 3RD, 4TH, 11TH.
    private static string Place(int rank) => rank switch
    {
        <= 0 => "-",
        _ when rank % 100 is 11 or 12 or 13 => string.Create(CultureInfo.InvariantCulture, $"{rank}TH"),
        _ => (rank % 10) switch
        {
            1 => string.Create(CultureInfo.InvariantCulture, $"{rank}ST"),
            2 => string.Create(CultureInfo.InvariantCulture, $"{rank}ND"),
            3 => string.Create(CultureInfo.InvariantCulture, $"{rank}RD"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{rank}TH"),
        },
    };
}
