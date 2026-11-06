using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <content>
/// A multiplayer mode's HUD: the score strip across the top (each side's score and players, the clock between, the bomb's
/// fuse in its place once planted), the role and the mode under it, a plant or defuse in progress and what the player can do
/// where it stands under the crosshair, and a decided round's or match's card across the middle.
/// </content>
internal sealed partial class RemasteredHudRenderer
{
    // The player's own side, the other, and the strip's faces.
    private static Bgra32 Ally => Bgra32.FromRgb(86, 172, 255);
    private static Bgra32 Enemy => Bgra32.FromRgb(240, 86, 74);
    private static Bgra32 StripFace => Bgra32.FromRgba(10, 12, 16, 214);
    private static Bgra32 StripEdge => Bgra32.FromRgba(255, 255, 255, 28);
    private static Bgra32 PipDown => Bgra32.FromRgba(255, 255, 255, 34);
    private static Bgra32 Victory => Bgra32.FromRgb(255, 206, 92);

    /// <inheritdoc/>
    public void DrawMatchHud(SpriteBatch batch, MatchHud hud, string? warning, string useKey, int screenWidth, int screenHeight, string buyKey = "G")
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(hud);
        if (_theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        double now = Environment.TickCount64 / 1000.0;
        float pulse = 0.5f + (0.5f * MathF.Sin((float)(now * Math.PI * 2.4)));

        UiRect safe = context.SafeRect;
        float centre = safe.X + (safe.Width * 0.5f);
        float top = safe.Y + Px(12f);
        float stripBottom = hud.HasTeams
            ? TeamStrip(painter, type, hud, centre, top, pulse, Px)
            : SoloStrip(painter, type, hud, centre, top, pulse, Px);

        // Under the strip: the mode, the target, the player's role; then the line of the moment.
        float y = stripBottom + Px(8f);
        y = Caption(painter, type, hud, centre, y, Px);
        if (hud.Planted.Length > 0 && hud.Result is null)
        {
            bool attacking = hud.Role == "ATTACK";
            string line = hud.Role is null ? $"BOMB PLANTED AT {hud.Planted}" : attacking ? $"DEFEND THE BOMB AT {hud.Planted}" : $"DEFUSE THE BOMB AT {hud.Planted}";
            y = Chip(painter, type, line, centre, y, Enemy.WithAlpha((byte)(150 + (90 * pulse))), UiPalette.TextBright, Px);
        }
        else if (hud.Notice is { } notice)
        {
            y = Chip(painter, type, notice, centre, y, StripFace, UiPalette.TextBright, Px);
        }

        if (hud.TeamNote is { } note)
        {
            y = Chip(painter, type, note, centre, y, Ally.WithAlpha(150), UiPalette.TextBright, Px);
        }

        if (hud.Money is { } money)
        {
            string cash = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"${money:N0}");
            string line = hud.BuyClock is { } buy ? $"{cash}   ·   {buyKey}  BUY  {buy}" : cash;
            _ = Chip(painter, type, line, centre, y, hud.BuyClock is null ? StripFace : Victory.WithAlpha(190), hud.BuyClock is null ? Victory : Bgra32.FromRgb(20, 16, 10), Px);
        }

        // Under the crosshair: the plant or defuse under way, or what the player can do here.
        float middle = safe.Y + (safe.Height * 0.5f);
        if (hud.Progress is { } progress)
        {
            ProgressBar(painter, type, hud.ProgressLabel ?? string.Empty, progress, centre, middle + Px(70f), hud.ProgressLabel?.StartsWith("DEFUS", StringComparison.Ordinal) == true ? Ally : Enemy, Px);
        }
        else if (hud.Hint is { } hint)
        {
            Hint(painter, type, hint, useKey, centre, middle + Px(70f), Px);
        }

        if (hud.Result is { } result)
        {
            ResultBand(painter, type, result, hud.Phase == MatchPhase.Intermission, centre, safe, Px);
        }

        if (warning is not null)
        {
            float size = Px(30f);
            float width = UiPainter.MeasureLabel(type.Heading, warning, size, 0.08f);
            var band = new UiRect(safe.X, safe.Y + (safe.Height * 0.28f), safe.Width, Px(56f));
            Band(painter, band, Enemy.WithAlpha((byte)(90 + (70 * pulse))));
            painter.DrawLabel(type.Heading, warning, new UiVec2(centre - (width * 0.5f), band.Y + Px(12f)), size, UiPalette.TextBright, 0.08f);
        }
    }

    // The two teams either side of the clock, each its score and its players; returns the strip's bottom.
    private float TeamStrip(UiPainter painter, UiTheme type, MatchHud hud, float centre, float top, float pulse, Func<float, float> Px)
    {
        float height = Px(52f);
        float clockWidth = Px(128f);
        float scoreWidth = Px(64f);
        TeamStrip left = hud.Left!;
        TeamStrip right = hud.Right!;
        Bgra32 leftColour = left.IsOwn || !right.IsOwn ? Ally : Enemy;
        Bgra32 rightColour = left.IsOwn || !right.IsOwn ? Enemy : Ally;

        // The clock, or the bomb's fuse in its place.
        var clock = new UiRect(centre - (clockWidth * 0.5f), top, clockWidth, height);
        painter.DrawShadow(clock, _surfaces?.Shadow ?? default, Bgra32.FromRgba(0, 0, 0, 120), Px(10f), new UiVec2(0f, Px(2f)));
        painter.FillGradient(
            clock,
            [new UiGradientStop(0f, Bgra32.FromRgba(22, 26, 32, 236)), new UiGradientStop(1f, Bgra32.FromRgba(8, 10, 13, 236))],
            UiGradientDirection.Vertical);
        painter.FillRect(new UiRect(clock.X, clock.Y + clock.Height - Px(2f), clock.Width, Px(2f)), StripEdge);
        if (hud.Fuse is { } fuse)
        {
            Bgra32 hot = UiGeometry.Lerp(Enemy, UiPalette.TextBright, pulse * 0.35f);
            painter.FillDiamond(new UiVec2(clock.X + Px(24f), clock.Y + (height * 0.45f)), Px(9f + (2f * pulse)), hot);
            Centred(painter, type.Display, fuse, clock.X + (clockWidth * 0.56f), clock.Y + Px(6f), Px(28f), hot, 0.02f);
            painter.FillRect(new UiRect(clock.X, clock.Y + height - Px(4f), clockWidth, Px(4f)), Bgra32.FromRgba(255, 255, 255, 20));
            painter.FillRect(new UiRect(clock.X, clock.Y + height - Px(4f), clockWidth * hud.FuseFraction, Px(4f)), hot);
        }
        else
        {
            Bgra32 ink = hud.ClockUrgent ? UiGeometry.Lerp(Enemy, UiPalette.TextBright, pulse * 0.4f) : UiPalette.TextBright;
            Centred(painter, type.Display, hud.Clock ?? "--:--", centre, clock.Y + Px(4f), Px(28f), ink, 0.02f);
        }

        string under = hud.Round > 0 ? $"ROUND {hud.Round}" : hud.Phase == MatchPhase.Warmup ? "WARMUP" : "TIME";
        Centred(painter, type.Body, under, hud.Fuse is null ? centre : clock.X + (clockWidth * 0.56f), clock.Y + Px(34f), Px(13f), UiPalette.TextSecondary, 0.14f);

        // Each side's score in its colour, cut on its outer edge.
        var leftScore = new UiRect(clock.X - scoreWidth - Px(4f), top, scoreWidth, height);
        var rightScore = new UiRect(clock.Right + Px(4f), top, scoreWidth, height);
        ScoreBlock(painter, type, leftScore, left.Score, leftColour, UiCorners.BottomLeft, Px);
        ScoreBlock(painter, type, rightScore, right.Score, rightColour, UiCorners.BottomRight, Px);

        // Their players outward from them: a slanted pip each, lit while standing.
        Pips(painter, left.Alive, leftScore.X - Px(10f), top + Px(8f), -1, leftColour, Px);
        Pips(painter, right.Alive, rightScore.Right + Px(10f), top + Px(8f), 1, rightColour, Px);

        // The teams' names under their scores.
        Centred(painter, type.Body, left.Name, leftScore.X + (scoreWidth * 0.5f), top + height + Px(4f), Px(13f), leftColour, 0.12f);
        Centred(painter, type.Body, right.Name, rightScore.X + (scoreWidth * 0.5f), top + height + Px(4f), Px(13f), rightColour, 0.12f);
        return top + height + Px(20f);
    }

    // A free-for-all: the player's own score and place, the clock, and the one to beat; returns the strip's bottom.
    private static float SoloStrip(UiPainter painter, UiTheme type, MatchHud hud, float centre, float top, float pulse, Func<float, float> Px)
    {
        float height = Px(52f);
        float clockWidth = Px(128f);
        float side = Px(150f);
        var clock = new UiRect(centre - (clockWidth * 0.5f), top, clockWidth, height);
        painter.FillGradient(
            clock,
            [new UiGradientStop(0f, Bgra32.FromRgba(22, 26, 32, 236)), new UiGradientStop(1f, Bgra32.FromRgba(8, 10, 13, 236))],
            UiGradientDirection.Vertical);
        Bgra32 ink = hud.ClockUrgent ? UiGeometry.Lerp(Enemy, UiPalette.TextBright, pulse * 0.4f) : UiPalette.TextBright;
        Centred(painter, type.Display, hud.Clock ?? "--:--", centre, top + Px(4f), Px(28f), ink, 0.02f);
        Centred(painter, type.Body, hud.Phase == MatchPhase.Warmup ? "WARMUP" : "TIME", centre, top + Px(34f), Px(13f), UiPalette.TextSecondary, 0.14f);

        bool leading = hud.OwnRank == 1;
        var own = new UiRect(clock.X - side - Px(4f), top, side, height);
        painter.FillCutRect(own, Px(12f), UiCorners.BottomLeft, (leading ? Victory : Ally).WithAlpha(220));
        painter.DrawLabel(type.Body, $"YOU  ·  {Place(hud.OwnRank)}", new UiVec2(own.X + Px(12f), own.Y + Px(8f)), Px(13f), Bgra32.FromRgba(8, 10, 14, 230), 0.12f);
        painter.DrawLabel(type.Display, hud.OwnScore.ToString(System.Globalization.CultureInfo.InvariantCulture), new UiVec2(own.X + Px(12f), own.Y + Px(20f)), Px(26f), Bgra32.FromRgb(8, 10, 14), 0f);

        var rival = new UiRect(clock.Right + Px(4f), top, side, height);
        painter.FillCutRect(rival, Px(12f), UiCorners.BottomRight, StripFace);
        string who = hud.LeaderName.Length > 0 ? (leading ? $"NEXT  ·  {hud.LeaderName}" : $"LEADER  ·  {hud.LeaderName}") : "NO RIVAL YET";
        painter.DrawLabel(type.Body, Clip(type.Body, who.ToUpperInvariant(), Px(13f), side - Px(24f)), new UiVec2(rival.X + Px(12f), rival.Y + Px(6f)), Px(13f), UiPalette.TextSecondary, 0.12f);
        painter.DrawLabel(type.Display, hud.LeaderScore.ToString(System.Globalization.CultureInfo.InvariantCulture), new UiVec2(rival.X + Px(12f), rival.Y + Px(20f)), Px(26f), UiPalette.TextBright, 0f);
        return top + height + Px(4f);
    }

    // The mode, what wins it, and the player's role, in one line under the strip.
    private static float Caption(UiPainter painter, UiTheme type, MatchHud hud, float centre, float y, Func<float, float> Px)
    {
        string target = hud.Target <= 0 ? string.Empty : hud.Round > 0 ? $"FIRST TO {hud.Target}" : $"FIRST TO {hud.Target} KILLS";
        string line = target.Length > 0 ? $"{hud.Title}  ·  {target}" : hud.Title;
        float size = Px(14f);
        float width = UiPainter.MeasureLabel(type.Body, line, size, 0.18f);
        float roleWidth = hud.Role is { } role ? UiPainter.MeasureLabel(type.Heading, role, size, 0.2f) + Px(18f) : 0f;
        float x = centre - ((width + roleWidth + (roleWidth > 0 ? Px(10f) : 0f)) * 0.5f);
        if (hud.Role is { } shown)
        {
            var chip = new UiRect(x, y - Px(4f), roleWidth, Px(22f));
            painter.FillCutRect(chip, Px(5f), UiCorners.TopLeft | UiCorners.BottomRight, shown == "ATTACK" ? Enemy.WithAlpha(210) : Ally.WithAlpha(210));
            painter.DrawLabel(type.Heading, shown, new UiVec2(chip.X + Px(9f), y - Px(1f)), size, UiPalette.TextBright, 0.2f);
            x += roleWidth + Px(10f);
        }

        painter.DrawLabel(type.Body, line, new UiVec2(x, y), size, UiPalette.TextSecondary, 0.18f);
        return y + Px(24f);
    }

    private static void ScoreBlock(UiPainter painter, UiTheme type, UiRect rect, int score, Bgra32 colour, UiCorners cut, Func<float, float> Px)
    {
        painter.FillCutRect(rect, Px(12f), cut, colour.WithAlpha(226));
        painter.FillGradient(
            new UiRect(rect.X, rect.Y, rect.Width, rect.Height * 0.5f),
            [new UiGradientStop(0f, Bgra32.FromRgba(255, 255, 255, 40)), new UiGradientStop(1f, Bgra32.FromRgba(255, 255, 255, 0))],
            UiGradientDirection.Vertical);
        Centred(painter, type.Display, score.ToString(System.Globalization.CultureInfo.InvariantCulture), rect.X + (rect.Width * 0.5f), rect.Y + Px(6f), Px(32f), Bgra32.FromRgb(255, 255, 255), 0f);
    }

    // A team's players as slanted pips running outward from its score: lit while standing, hollow when down.
    private static void Pips(UiPainter painter, IReadOnlyList<bool> alive, float from, float top, int direction, Bgra32 colour, Func<float, float> Px)
    {
        float width = Px(9f);
        float height = Px(34f);
        float slant = Px(5f);
        float step = width + Px(5f);
        int count = Math.Min(alive.Count, 16);
        for (int i = 0; i < count; i++)
        {
            float x = direction < 0 ? from - ((i + 1) * step) : from + (i * step);
            var a = new UiVec2(x + slant, top);
            var b = new UiVec2(x + slant + width, top);
            var c = new UiVec2(x + width, top + height);
            var d = new UiVec2(x, top + height);
            if (alive[i])
            {
                painter.FillQuad(a, b, c, d, colour);
            }
            else
            {
                painter.FillQuad(a, b, c, d, PipDown);
                painter.StrokeLine(new UiVec2(x + slant + width, top + Px(4f)), new UiVec2(x, top + height - Px(4f)), Px(1.5f), colour.WithAlpha(120));
            }
        }
    }

    // A line on a small plate, centred; returns the y below it.
    private static float Chip(UiPainter painter, UiTheme type, string text, float centre, float y, Bgra32 face, Bgra32 ink, Func<float, float> Px)
    {
        float size = Px(13f);
        float width = UiPainter.MeasureLabel(type.Heading, text, size, 0.14f) + Px(28f);
        var plate = new UiRect(centre - (width * 0.5f), y, width, Px(26f));
        painter.FillCutRect(plate, Px(7f), UiCorners.TopLeft | UiCorners.BottomRight, face);
        painter.DrawLabel(type.Heading, text, new UiVec2(plate.X + Px(14f), y + Px(6f)), size, ink, 0.14f);
        return y + Px(32f);
    }

    // A plant or defuse under way: what, a bar, and how far.
    private void ProgressBar(UiPainter painter, UiTheme type, string label, float progress, float centre, float y, Bgra32 colour, Func<float, float> Px)
    {
        float width = Px(340f);
        float x = centre - (width * 0.5f);
        var plate = new UiRect(x - Px(14f), y - Px(10f), width + Px(28f), Px(54f));
        painter.DrawShadow(plate, _surfaces?.Shadow ?? default, Bgra32.FromRgba(0, 0, 0, 110), Px(12f));
        painter.FillCutRect(plate, Px(10f), UiCorners.TopLeft | UiCorners.BottomRight, StripFace);
        painter.DrawLabel(type.Heading, label, new UiVec2(x, y - Px(2f)), Px(13f), UiPalette.TextBright, 0.16f);
        string percent = $"{(int)(Math.Clamp(progress, 0f, 1f) * 100f)}%";
        float percentWidth = UiPainter.MeasureLabel(type.Mono, percent, Px(13f), 0f);
        painter.DrawLabel(type.Mono, percent, new UiVec2(x + width - percentWidth, y - Px(2f)), Px(13f), colour, 0f);
        var track = new UiRect(x, y + Px(22f), width, Px(8f));
        painter.FillRect(track, Bgra32.FromRgba(255, 255, 255, 26));
        var fill = new UiRect(x, track.Y, width * Math.Clamp(progress, 0f, 1f), track.Height);
        painter.FillGradient(fill, [new UiGradientStop(0f, colour.WithAlpha(170)), new UiGradientStop(1f, colour)], UiGradientDirection.Horizontal);
        painter.FillRect(new UiRect(fill.Right - Px(2f), track.Y - Px(3f), Px(2f), track.Height + Px(6f)), UiPalette.TextBright);
    }

    // What the player can do here: HOLD [E] TO PLANT THE BOMB AT A.
    private void Hint(UiPainter painter, UiTheme type, ModeHint hint, string key, float centre, float y, Func<float, float> Px)
    {
        float size = Px(15f);
        float verb = UiPainter.MeasureLabel(type.Heading, hint.Verb, size, 0.12f);
        float keyText = UiPainter.MeasureLabel(type.Heading, key, size, 0f);
        float keyWidth = Math.Max(Px(28f), keyText + Px(14f));
        float action = UiPainter.MeasureLabel(type.Heading, hint.Action, size, 0.12f);
        float gap = Px(10f);
        float whole = verb + gap + keyWidth + gap + action;
        var plate = new UiRect(centre - (whole * 0.5f) - Px(16f), y - Px(8f), whole + Px(32f), Px(42f));
        painter.FillCutRect(plate, Px(9f), UiCorners.TopLeft | UiCorners.BottomRight, StripFace);
        float x = plate.X + Px(16f);
        painter.DrawLabel(type.Heading, hint.Verb, new UiVec2(x, y + Px(3f)), size, UiPalette.TextSecondary, 0.12f);
        x += verb + gap;
        var badge = new UiRect(x, y - Px(1f), keyWidth, Px(26f));
        painter.FillRoundedRect(badge, _surfaces?.Rounded ?? default, UiPalette.TextBright, Px(5f));
        painter.DrawLabel(type.Heading, key, new UiVec2(badge.X + ((keyWidth - keyText) * 0.5f), y + Px(3f)), size, Bgra32.FromRgb(10, 12, 16), 0f);
        x += keyWidth + gap;
        painter.DrawLabel(type.Heading, hint.Action, new UiVec2(x, y + Px(3f)), size, UiPalette.TextBright, 0.12f);
    }

    // A decided round's or match's card: a band across the screen in the outcome's colour, the result large on it.
    private static void ResultBand(UiPainter painter, UiTheme type, ResultCard result, bool match, float centre, UiRect safe, Func<float, float> Px)
    {
        Bgra32 colour = result.Outcome > 0 ? (match ? Victory : Ally) : result.Outcome < 0 ? Enemy : UiPalette.TextSecondary;
        float bandHeight = Px(match ? 150f : 118f);
        var band = new UiRect(safe.X, safe.Y + (safe.Height * 0.3f), safe.Width, bandHeight);
        Band(painter, band, Bgra32.FromRgba(6, 8, 11, 200));
        Band(painter, new UiRect(band.X, band.Y, band.Width, Px(2f)), colour.WithAlpha(230));
        Band(painter, new UiRect(band.X, band.Bottom - Px(2f), band.Width, Px(2f)), colour.WithAlpha(230));

        float size = Px(match ? 76f : 58f);
        Centred(painter, type.Display, result.Title, centre, band.Y + Px(match ? 18f : 14f), size, colour, 0.08f);
        if (result.Detail.Length > 0)
        {
            Centred(painter, type.Heading, result.Detail, centre, band.Bottom - Px(36f), Px(16f), UiPalette.TextBright, 0.2f);
        }
    }

    // A horizontal band that fades out at its ends.
    private static void Band(UiPainter painter, UiRect band, Bgra32 colour)
    {
        Bgra32 clear = colour.WithAlpha(0);
        painter.FillGradient(
            band,
            [new UiGradientStop(0f, clear), new UiGradientStop(0.25f, colour), new UiGradientStop(0.75f, colour), new UiGradientStop(1f, clear)],
            UiGradientDirection.Horizontal);
    }

    private static void Centred(UiPainter painter, UiFont font, string text, float centre, float y, float size, Bgra32 colour, float tracking)
    {
        float width = UiPainter.MeasureLabel(font, text, size, tracking);
        painter.DrawLabel(font, text, new UiVec2(centre - (width * 0.5f), y), size, colour, tracking);
    }

    // A string cut to a width, with an ellipsis.
    private static string Clip(UiFont font, string text, float size, float width)
    {
        if (UiPainter.MeasureLabel(font, text, size, 0.14f) <= width)
        {
            return text;
        }

        for (int length = text.Length - 1; length > 1; length--)
        {
            string cut = string.Concat(text.AsSpan(0, length), "…");
            if (UiPainter.MeasureLabel(font, cut, size, 0.14f) <= width)
            {
                return cut;
            }
        }

        return text;
    }

    private static string Place(int rank) => rank switch
    {
        <= 0 => "-",
        _ when rank % 100 is 11 or 12 or 13 => $"{rank}TH",
        _ => (rank % 10) switch { 1 => $"{rank}ST", 2 => $"{rank}ND", 3 => $"{rank}RD", _ => $"{rank}TH" },
    };
}
