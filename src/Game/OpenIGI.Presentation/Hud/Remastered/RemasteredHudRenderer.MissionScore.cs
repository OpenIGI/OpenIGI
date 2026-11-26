using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Scoring;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>A completed mission's rank screen.</content>
/// <remarks>
/// <para>
/// I.G.I. 2's mission score screen (<c>MissionScoreScreen_DrawHandler</c>) laid out as the session's own sheets are, on the
/// played title's glass: the title across its header strip; the original's three columns -- the category, what was achieved,
/// and what the next rank asks -- under their heads; a rule; and the rank earned under it, with the ranks to earn marked
/// beside it. An achieved figure that falls short of the rank it is weighed against is in the danger's ink, as retail draws it
/// red, and one that meets it is lit.
/// </para>
/// <para>
/// A co-op squad is scored as one: the figures above are what holds for all of it, and the rank its one rank. The table under it
/// lays each player's part as the scoreboard lays its lines -- their own figures, no rank of their own -- this player's washed in
/// the highlight, as many lines as the screen has room for. Its foot counts down to the squad
/// going on, beside the key that goes on sooner where this end may (the host's, as Space is in the original) or word that the
/// host decides.
/// </para>
/// </remarks>
public sealed partial class RemasteredHudRenderer
{
    // The sheet's measures, in layout pixels.
    private const float ScorePad = 30f;
    private const float ScoreColumnGap = 46f;
    private const float ScoreMinWidth = 700f;
    private const float ScoreSquadWidth = 900f;
    private const float ScoreRow = 40f;

    // The squad's lines: the tallest and shortest each is drawn.
    private const float SquadRow = 34f;
    private const float SquadRowCompact = 24f;

    /// <inheritdoc/>
    public void DrawMissionScore(SpriteBatch batch, MissionScoreCard card, MissionScorePrompt prompt, float opacity, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(card);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float fade = Math.Clamp(opacity, 0f, 1f);
        Bgra32 Ink(Bgra32 colour) => HudOverlay.Fade(colour, fade);

        // The level stands still behind it, darkened so the sheet is what is read.
        o.Painter.FillRect(new UiRect(0f, 0f, o.Context.Viewport.X, o.Context.Viewport.Y), Bgra32.FromRgba(0, 0, 0, (byte)(110 * fade)));

        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float large = o.Px(HudOverlay.Large);
        float pad = o.Px(ScorePad);
        float gap = o.Px(ScoreColumnGap);
        float row = o.Px(ScoreRow);

        // The columns as wide as their widest word, as retail measures its tiles.
        string category = OverlayText.Capitals(card.CategoryHeading);
        string achieved = OverlayText.Capitals(card.AchievedHeading);
        string? target = card.TargetHeading is { } t ? OverlayText.Capitals(t) : null;
        float labelWidth = MathF.Max(o.Text.MeasureAt(category, small), o.Text.MeasureAt(OverlayText.Capitals(card.RankLabel), body));
        float achievedWidth = o.Text.MeasureAt(achieved, small);
        float targetWidth = target is null ? 0f : o.Text.MeasureAt(target, small);
        foreach (MissionScoreLine line in card.Lines)
        {
            labelWidth = MathF.Max(labelWidth, o.Text.MeasureAt(OverlayText.Capitals(line.Label), body));
            achievedWidth = MathF.Max(achievedWidth, o.Text.MeasureAt(line.Achieved, body) + o.Px(20f));
            if (line.Target is { } aim)
            {
                targetWidth = MathF.Max(targetWidth, o.Text.MeasureAt(aim, body));
            }
        }

        IReadOnlyList<MissionSquadLine> squad = card.SquadLines;
        float header = o.Text.LineHeightAt(body) + o.Px(22f);
        float heads = o.Text.LineHeightAt(small) + o.Px(16f);
        float rankBlock = o.Text.LineHeightAt(large) + o.Px(28f);
        float footer = o.KeyCapHeight + o.Px(24f);
        float columns = labelWidth + gap + achievedWidth + (target is null ? 0f : gap + targetWidth);
        UiRect safe = o.Safe;
        float least = squad.Count > 0 ? ScoreSquadWidth : ScoreMinWidth;
        float width = MathF.Min(MathF.Max(o.Px(least), columns + (pad * 2f)), safe.Width * 0.94f);
        float fixedHeight = header + o.Px(14f) + heads + (card.Lines.Count * row) + o.Px(14f) + rankBlock + footer;

        // The squad's table takes what height is left, its lines as tall as there is room for, the rest told as a count.
        SquadFit fit = squad.Count > 0 ? FitSquad(o, squad, (safe.Height * 0.94f) - fixedHeight) : default;
        float height = fixedHeight + fit.Height;
        var sheet = new UiRect(safe.X + ((safe.Width - width) * 0.5f), safe.Y + MathF.Max(o.Px(20f), (safe.Height - height) * 0.42f), width, height);
        o.Sheet(sheet, fade);

        // The header: the original's title, and the rank earned at its right for the eye that reads no further.
        var strip = new UiRect(sheet.X, sheet.Y, sheet.Width, header);
        o.HeaderStrip(strip, fade);
        float headY = strip.Y + ((strip.Height - o.Text.LineHeightAt(body)) * 0.5f);
        float x = sheet.X + pad;
        o.Diamond(new UiVec2(x + o.Px(4f), headY + (o.Text.LineHeightAt(body) * 0.5f)), o.Px(5f), Ink(o.Ink.Accent));
        o.Text.DrawAt(OverlayText.Capitals(card.Title), x + o.Px(16f), headY, body, Ink(o.Ink.Highlight));
        DrawRankMarks(o, card, strip.Right - pad, headY + (o.Text.LineHeightAt(body) * 0.5f), fade);

        // The columns' heads, centred in the space the content leaves so a narrow card does not hug the left; over a squad's
        // table, on its left edge, so the sheet reads as one column of figures.
        float left = squad.Count > 0 ? sheet.X + pad : sheet.X + MathF.Max(pad, (sheet.Width - columns) * 0.5f);
        float achievedX = left + labelWidth + gap;
        float targetX = achievedX + achievedWidth + gap;
        float y = strip.Bottom + o.Px(14f);
        o.Text.DrawAt(category, left, y, small, Ink(o.Quiet));
        o.Text.DrawAt(achieved, achievedX, y, small, Ink(o.Quiet));
        if (target is not null)
        {
            o.Text.DrawAt(target, targetX, y, small, Ink(o.Quiet));
        }

        y += heads;
        o.Rule(sheet.X + pad, y - o.Px(8f), sheet.Width - (pad * 2f), Ink(HudOverlay.Fade(o.Ink.Primary, 0.55f)));

        // The figures: each label quiet, what was achieved lit where it meets the rank and in the danger's ink where it falls
        // short, with the title's diamond before it in the same ink, and the next rank's figure after it.
        foreach (MissionScoreLine line in card.Lines)
        {
            float textY = y + ((row - o.Text.LineHeightAt(body)) * 0.5f);
            Bgra32 ink = line.Met ? o.Ink.Highlight : o.Ink.Danger;
            o.Text.DrawAt(OverlayText.Capitals(line.Label), left, textY, body, Ink(o.Ink.Plain));
            o.Diamond(new UiVec2(achievedX + o.Px(4f), textY + (o.Text.LineHeightAt(body) * 0.5f)), o.Px(4f), Ink(line.Met ? o.Ink.Ally : o.Ink.Danger));
            o.Text.DrawAt(line.Achieved, achievedX + o.Px(20f), textY, body, Ink(ink));
            if (line.Target is { } aim)
            {
                o.Text.DrawAt(aim, targetX, textY, body, Ink(o.Quiet));
            }

            y += row;
        }

        // The rank earned, large, under a rule.
        y += o.Px(14f);
        o.Rule(sheet.X + pad, y - o.Px(6f), sheet.Width - (pad * 2f), Ink(HudOverlay.Fade(o.Ink.Primary, 0.55f)));
        float rankY = y + ((rankBlock - o.Text.LineHeightAt(large)) * 0.5f);
        o.Text.DrawAt(OverlayText.Capitals(card.RankLabel), left, rankY + ((o.Text.LineHeightAt(large) - o.Text.LineHeightAt(body)) * 0.5f), body, Ink(o.Ink.Plain));
        o.Text.DrawAt(OverlayText.Capitals(card.RankName), achievedX, rankY, large, Ink(card.Rank >= 0 ? o.Ink.Accent : o.Quiet), shadow: true);
        y += rankBlock;

        if (squad.Count > 0)
        {
            DrawSquad(o, squad, fit, new UiRect(sheet.X + pad, y, sheet.Width - (pad * 2f), fit.Height), fade);
        }

        DrawScoreFoot(o, prompt, sheet, footer, pad, fade);
    }

    // How the squad's table fits: its lines' height, how many are shown, and its whole height.
    private readonly record struct SquadFit(float RowHeight, int Shown, float Height);

    private static SquadFit FitSquad(HudOverlay o, IReadOnlyList<MissionSquadLine> squad, float room)
    {
        float top = o.Text.LineHeightAt(o.Px(HudOverlay.Small)) + o.Px(18f);
        float more = o.Text.LineHeightAt(o.Px(HudOverlay.Small)) + o.Px(6f);
        float lines = MathF.Max(0f, room - top);
        float rowHeight = Math.Clamp(lines / squad.Count, o.Px(SquadRowCompact), o.Px(SquadRow));
        int shown = squad.Count;
        if (shown * rowHeight > lines)
        {
            shown = Math.Max(1, (int)((lines - more) / rowHeight));
        }

        return new SquadFit(rowHeight, shown, top + (shown * rowHeight) + (shown < squad.Count ? more : 0f) + o.Px(8f));
    }

    // The squad's table: every player's part in its score in the scoreboard's manner, this player's washed in the highlight. Where
    // the screen holds fewer lines than players, the first are shown -- this player's always among them -- and the rest counted.
    private static void DrawSquad(HudOverlay o, IReadOnlyList<MissionSquadLine> squad, SquadFit fit, UiRect area, float fade)
    {
        Bgra32 Ink(Bgra32 colour) => HudOverlay.Fade(colour, fade);
        float small = o.Px(HudOverlay.Small);
        float body = o.Px(HudOverlay.Body);

        // The figures' columns, from the right edge inward, each as wide as its head or its widest figure.
        string[] heads = ["KILLS", "ACCURACY", "HEALTH", "SPOTTED"];
        float[] rights = new float[heads.Length];
        float edge = area.Right;
        for (int c = 0; c < heads.Length; c++)
        {
            rights[c] = edge;
            float widest = MathF.Max(o.Text.MeasureAt(heads[c], small), squad.Max(line => o.Text.MeasureAt(SquadFigure(line, c), body)));
            edge -= widest + o.Px(ScoreColumnGap * 0.6f);
        }

        float y = area.Y;
        o.Rule(area.X, y, area.Width, Ink(HudOverlay.Fade(o.Ink.Primary, 0.55f)));
        y += o.Px(8f);
        o.Text.DrawAt("SQUAD", area.X, y, small, Ink(o.Quiet));
        for (int c = 0; c < heads.Length; c++)
        {
            o.Text.DrawRightAt(heads[c], rights[c], y, small, Ink(o.Quiet));
        }

        y += o.Text.LineHeightAt(small) + o.Px(10f);
        List<MissionSquadLine> shown = [.. squad.Take(fit.Shown)];
        if (fit.Shown < squad.Count && !shown.Any(static line => line.IsOwn) && squad.FirstOrDefault(static line => line.IsOwn) is { IsOwn: true } own)
        {
            shown[^1] = own;
        }

        foreach (MissionSquadLine line in shown)
        {
            var rowRect = new UiRect(area.X - o.Px(8f), y, area.Width + o.Px(16f), fit.RowHeight - o.Px(2f));
            o.Row(rowRect, line.IsOwn);
            float textY = y + ((fit.RowHeight - o.Text.LineHeightAt(body)) * 0.5f);
            Bgra32 name = line.IsOwn ? o.Ink.Highlight : o.Ink.Plain;
            o.Text.DrawAt(o.Text.ClipAt(OverlayText.Prose(line.Name), body, edge - area.X - o.Px(16f)), area.X, textY, body, Ink(name));
            for (int c = 0; c < heads.Length; c++)
            {
                o.Text.DrawRightAt(SquadFigure(line, c), rights[c], textY, body, Ink(o.Ink.Plain));
            }

            y += fit.RowHeight;
        }

        if (fit.Shown < squad.Count)
        {
            string more = string.Create(CultureInfo.InvariantCulture, $"+{squad.Count - fit.Shown} MORE");
            o.Text.DrawAt(more, area.X, y + o.Px(2f), small, Ink(o.Quiet));
        }
    }

    // A squad line's figure, by its column from the right: kills, accuracy, health, times spotted.
    private static string SquadFigure(MissionSquadLine line, int column) => column switch
    {
        0 => line.Kills.ToString(CultureInfo.InvariantCulture),
        1 => line.Accuracy,
        2 => line.Health,
        _ => line.Spotted.ToString(CultureInfo.InvariantCulture),
    };

    // The sheet's foot: a co-op squad's countdown at its left, and the key that goes on at its right where this end may.
    private static void DrawScoreFoot(HudOverlay o, MissionScorePrompt prompt, UiRect sheet, float footer, float pad, float fade)
    {
        float small = o.Px(HudOverlay.Small);
        float capY = sheet.Bottom - footer + o.Px(10f);
        float textY = capY + ((o.KeyCapHeight - o.Text.LineHeightAt(small)) * 0.5f);
        if (prompt.SecondsLeft is { } seconds)
        {
            string countdown = seconds > 0
                ? string.Create(CultureInfo.InvariantCulture, $"NEXT MISSION IN {seconds}")
                : "NEXT MISSION";
            float x = o.Text.DrawAt(countdown, sheet.X + pad, textY, small, HudOverlay.Fade(o.Ink.Highlight, fade));
            if (prompt.WaitsForHost)
            {
                o.Text.DrawAt("  ·  THE HOST CAN GO ON SOONER", sheet.X + pad + x, textY, small, HudOverlay.Fade(o.Quiet, fade));
            }
        }

        float right = sheet.Right - pad;
        if (prompt.ContinueKey is { } key)
        {
            float capX = sheet.Right - pad - o.KeyCapWidth(key);
            o.KeyCap(key, capX, capY);
            right = capX - o.Px(10f) - o.Text.DrawRightAt("CONTINUE", capX - o.Px(10f), textY, small, HudOverlay.Fade(o.Quiet, fade)) - o.Px(18f);
        }

        // A single player's run sent to the boards, at the left: in the ally's ink placed, the danger's not ranked.
        if (prompt.SecondsLeft is null && prompt.Board is { Length: > 0 } board)
        {
            Bgra32 ink = prompt.BoardGood switch { true => o.Ink.Ally, false => o.Ink.Danger, _ => o.Quiet };
            float left = sheet.X + pad;
            o.Text.DrawAt(o.Text.ClipAt(OverlayText.Capitals(board), small, MathF.Max(0f, right - left)), left, textY, small, HudOverlay.Fade(ink, fade));
        }
    }

    // The ranks to earn as a row of the title's diamonds ending at a point, as many lit as the rank earned is high.
    private static void DrawRankMarks(HudOverlay o, MissionScoreCard card, float right, float middle, float fade)
    {
        float radius = o.Px(6f);
        float step = o.Px(18f);
        for (int i = card.RankCount - 1; i >= 0; i--)
        {
            bool lit = i <= card.Rank;
            float cx = right - radius - ((card.RankCount - 1 - i) * step);
            o.Diamond(new UiVec2(cx, middle), radius, HudOverlay.Fade(lit ? o.Ink.Accent : HudOverlay.Fade(o.Ink.Dim, 0.8f), fade));
        }
    }
}
