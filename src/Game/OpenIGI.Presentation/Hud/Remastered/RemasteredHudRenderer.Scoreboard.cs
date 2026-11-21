using OpenIGI.Core.Titles;
using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>The session's scoreboard, held open on its key.</summary>
/// <remarks>
/// <para>
/// I.G.I. 2's own board (statusscreen.c <c>StatusScreen_DisplayScoreboard</c>) for both titles, on the played title's glass. A
/// team match stands its two sides in two columns either side of the middle, as the original does -- each its <c>TEAM: name</c>
/// and <c>SCORE: n</c> over the heads NAME, KILLS, DEATHS, ASSISTS and PING -- and a free-for-all or a co-op squad has one. Each player's
/// line is sorted by kills (<c>StatusScreen_SortByFrags</c>) and set in the original's inks: this player's own in the ally's,
/// one who is down in the enemy's with the original's <c>S</c> before their number, everyone else plain.
/// </para>
/// <para>
/// Project I.G.I. had no multiplayer to take a board from: it stands the same board in its own status screen's look, so both
/// titles' sessions read alike.
/// </para>
/// </remarks>
public sealed partial class RemasteredHudRenderer
{
    // The board's measures, in layout pixels.
    private const float BoardWidth = 1320f;
    private const float BoardColumnGap = 34f;
    private const float BoardPad = 26f;

    // The tallest and shortest a player's line is; beside the vote, lines are fitted between the two.
    private const float BoardRow = 40f;
    private const float BoardRowCompact = 30f;

    private readonly List<(int Entity, float X, float Y, float Width, float Height)> _removeButtons = [];

    /// <inheritdoc/>
    public IReadOnlyList<(int Entity, float X, float Y, float Width, float Height)> ScoreboardRemoveButtons => _removeButtons;

    /// <inheritdoc/>
    public void DrawScoreboard(
        SpriteBatch batch,
        Scoreboard board,
        int? localEntity,
        string? roomCode,
        string place,
        int screenWidth,
        int screenHeight,
        ScoreboardControls? controls = null,
        bool relayed = false,
        string mode = "",
        bool besideVote = false,
        Func<string, UiPicture?>? avatarOf = null)
    {
        _removeButtons.Clear();
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(board);
        if (Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        // A team mode is split by team even while a side stands empty; a free-for-all never is, whatever team its lines carry;
        // a co-op squad with players on both sides (a host's test) is split too.
        bool teamed = MatchModes.IsVersus(mode)
            ? MatchModes.HasTeams(mode)
            : board.Lines.Select(static l => l.Team).Distinct().Count() > 1;
        ScoreboardLine[] lines = [.. board.Lines.OrderByDescending(static l => l.Kills).ThenByDescending(static l => l.Assists).ThenBy(static l => l.Deaths).ThenBy(static l => l.Slot)];
        int ownTeam = localEntity is { } self && lines.FirstOrDefault(l => l.Entity == self) is { Name: not null } mine ? mine.Team : -1;
        int[] sides = teamed ? [.. new[] { TeamRoster.Good, TeamRoster.Bad }.Union(lines.Select(static l => l.Team).Order())] : [];
        int tallest = teamed ? sides.Max(side => lines.Count(l => l.Team == side)) : lines.Length;

        // The sheet: beside the vote, the left of the screen under the match's card, its lines as tall as there is room for;
        // alone, in the middle, every line full height.
        float pad = o.Px(BoardPad);
        float header = o.Text.LineHeightAt(o.Px(HudOverlay.Body)) + o.Px(22f);
        float columnHead = (o.Text.LineHeightAt(o.Px(HudOverlay.Body)) * 1f) + (o.Text.LineHeightAt(o.Px(HudOverlay.Small)) * 1f) + o.Px(26f);
        float footer = besideVote && controls is null ? o.Px(12f) : o.KeyCapHeight + o.Px(20f);
        float fixedHeight = header + columnHead + footer + o.Px(10f);
        UiRect? area = besideVote ? IntermissionAreas(o.Context).Board : null;
        int rowsShown = Math.Max(1, tallest);
        float rowHeight = area is { } fit
            ? Math.Clamp((fit.Height - fixedHeight) / rowsShown, o.Px(BoardRowCompact), o.Px(BoardRow))
            : o.Px(BoardRow);
        float height = fixedHeight + (rowsShown * rowHeight);
        UiRect safe = o.Safe;
        float width = area?.Width ?? MathF.Min(o.Px(teamed ? BoardWidth : BoardWidth * 0.72f), safe.Width * 0.94f);
        float x = area?.X ?? safe.X + ((safe.Width - width) * 0.5f);

        // Retail stands its board's top at 4 % of the screen; the sheet keeps clear of the match's strip above that.
        float top = area?.Y ?? safe.Y + MathF.Max(o.Px(70f), (safe.Height - height) * 0.3f);
        var sheet = new UiRect(x, top, width, height);
        o.Sheet(sheet);

        // The header: what is played and where across its strip, the figures that matter at its right.
        DrawBoardHeader(o, board, lines, new UiRect(x, top, width, header), roomCode, place, relayed, mode, teamed, pad);

        // The columns: a side each in a team match, side by side as the original stands them; else one.
        float columnsTop = top + header + o.Px(10f);
        float inner = width - (pad * 2f);
        if (teamed)
        {
            float gap = o.Px(BoardColumnGap);
            float columnWidth = (inner - (gap * (sides.Length - 1))) / sides.Length;
            for (int i = 0; i < sides.Length; i++)
            {
                int side = sides[i];
                int score = side == TeamRoster.Good ? board.GoodScore : side == TeamRoster.Bad ? board.BadScore : 0;
                var column = new UiRect(x + pad + (i * (columnWidth + gap)), columnsTop, columnWidth, height - header - footer);
                DrawBoardColumn(o, column, [.. lines.Where(l => l.Team == side)], side, score, ownTeam, localEntity, avatarOf, controls, rowHeight);
            }
        }
        else
        {
            DrawBoardColumn(o, new UiRect(x + pad, columnsTop, inner, height - header - footer), lines, -1, null, ownTeam, localEntity, avatarOf, controls, rowHeight);
        }

        // The foot: the key that keeps the board up; nothing while it is up by itself for the vote.
        if (!besideVote || controls is not null)
        {
            float capY = sheet.Bottom - footer + o.Px(8f);
            string key = "TAB";
            float capX = sheet.Right - pad - o.KeyCapWidth(key);
            o.KeyCap(key, capX, capY);
            float hold = o.Px(HudOverlay.Small);
            o.Text.DrawRightAt("HOLD", capX - o.Px(10f), capY + ((o.KeyCapHeight - o.Text.LineHeightAt(hold)) * 0.5f), hold, o.Quiet);
            if (controls is not null && _removeButtons.Count > 0)
            {
                o.Text.DrawAt("CLICK REMOVE TWICE TO PUT A PLAYER OUT", sheet.X + pad, capY + ((o.KeyCapHeight - o.Text.LineHeightAt(hold)) * 0.5f), hold, o.Quiet);
            }
        }

        // The pointer, once a click has taken it out onto the board.
        if (controls is { } pointerHand)
        {
            UiCursor.Draw(o.Painter, new UiVec2(pointerHand.PointerX, pointerHand.PointerY));
        }
    }

    // The board's header strip: the mode and the room on the left, and on the right the round, who is up, and the link.
    private static void DrawBoardHeader(
        HudOverlay o, Scoreboard board, ScoreboardLine[] lines, UiRect strip, string? roomCode, string place, bool relayed, string mode, bool teamed, float pad)
    {
        o.HeaderStrip(strip);
        float size = o.Px(HudOverlay.Body);
        float y = strip.Y + ((strip.Height - o.Text.LineHeightAt(size)) * 0.5f);
        bool versus = MatchModes.IsVersus(mode) || mode == MatchModes.Objective;
        string what = versus ? MatchModes.Title(mode).ToUpperInvariant() : teamed ? "TEAM" : "CO-OP";
        float x = strip.X + pad;
        o.Diamond(new UiVec2(x + o.Px(4f), y + (o.Text.LineHeightAt(size) * 0.5f)), o.Px(5f), o.Ink.Accent);
        x += o.Px(16f);
        x += o.Text.DrawAt(what, x, y, size, o.Ink.Highlight) + o.Px(18f);
        string where = roomCode is null ? "SERVER" : "ROOM " + roomCode;
        x += o.Text.DrawAt(where, x, y, size, o.Quiet) + o.Px(18f);

        // The right: a label quiet, its figure lit, from the right edge inward.
        float right = strip.Right - pad;
        void Figure(string label, string value, Bgra32 ink)
        {
            right = o.Text.DrawRightAt(value, right, y, size, ink);
            right = o.Text.DrawRightAt(label + " ", right, y, size, o.Quiet) - o.Px(22f);
        }

        int up = lines.Count(static l => l.IsAlive);
        if (roomCode is not null && !versus)
        {
            Figure("LINK", relayed ? "RELAYED" : "DIRECT", relayed ? o.Ink.Accent : o.Ink.Ally);
        }

        Figure("ALIVE", string.Create(CultureInfo.InvariantCulture, $"{up}/{lines.Length}"), up == 0 && lines.Length > 0 ? o.Ink.Enemy : o.Ink.Highlight);
        if (teamed && board.Round > 0)
        {
            Figure("ROUND", board.Round.ToString(CultureInfo.InvariantCulture), o.Ink.Highlight);
        }
        else if (!teamed && !versus)
        {
            Figure("SQUAD KILLS", lines.Sum(static l => l.Kills).ToString(CultureInfo.InvariantCulture), o.Ink.Highlight);
        }

        // The mission in what room the figures leave, cut where it would run into them.
        if (place.Length > 0)
        {
            string mission = place.StartsWith("Mission ", StringComparison.Ordinal) ? "MISSION " + place["Mission ".Length..] : place;
            o.Text.DrawAt(o.Text.ClipAt(OverlayText.Capitals(mission), size, right - x), x, y, size, o.Quiet);
        }
    }

    // One column of the board: a side's title and score in a team match, the heads, and its players' lines under them.
    private void DrawBoardColumn(
        HudOverlay o,
        UiRect column,
        ScoreboardLine[] players,
        int side,
        int? score,
        int ownTeam,
        int? localEntity,
        Func<string, UiPicture?>? avatarOf,
        ScoreboardControls? controls,
        float rowHeight)
    {
        float body = o.Px(HudOverlay.Body);
        float small = o.Px(HudOverlay.Small);
        float y = column.Y;

        // TEAM: name, SCORE: n -- the original's two lines over its column, set as one, in the side's ink.
        if (side >= 0)
        {
            Bgra32 ink = o.SideInk(side, ownTeam, TeamRoster.Good);
            string name = side is TeamRoster.Good or TeamRoster.Bad ? Game.Multiplayer.Modes.MatchState.TeamName(side) : "Unassigned";
            o.Text.DrawAt("TEAM: " + OverlayText.Capitals(name), column.X, y, body, ink);
            if (score is { } points)
            {
                o.Text.DrawRightAt(string.Create(CultureInfo.InvariantCulture, $"SCORE: {points}"), column.Right, y, body, ink);
            }
        }
        else
        {
            o.Text.DrawAt("PLAYERS", column.X, y, body, o.Ink.Highlight);
            o.Text.DrawRightAt(players.Length.ToString(CultureInfo.InvariantCulture), column.Right, y, body, o.Quiet);
        }

        y += o.Text.LineHeightAt(body) + o.Px(8f);
        o.Rule(column.X, y, column.Width, HudOverlay.Fade(o.Ink.Primary, 0.55f));
        y += o.Px(8f);

        // The heads, as the original names its columns.
        BoardColumns columns = BoardColumns.For(o, column);
        o.Text.DrawAt("ID", columns.Id, y, small, o.Quiet);
        o.Text.DrawAt("NAME", columns.Name, y, small, o.Quiet);
        o.Text.DrawRightAt("KILLS", columns.Kills, y, small, o.Quiet);
        o.Text.DrawRightAt("DEATHS", columns.Deaths, y, small, o.Quiet);
        o.Text.DrawRightAt("ASSISTS", columns.Assists, y, small, o.Quiet);
        o.Text.DrawRightAt("PING", columns.Ping, y, small, o.Quiet);
        y += o.Text.LineHeightAt(small) + o.Px(8f);

        if (players.Length == 0)
        {
            o.Text.DrawAt("NO PLAYERS", columns.Name, y + ((rowHeight - o.Text.LineHeightAt(body)) * 0.5f), body, HudOverlay.Fade(o.Quiet, 0.7f));
            return;
        }

        foreach (ScoreboardLine line in players)
        {
            var row = new UiRect(column.X - o.Px(8f), y, column.Width + o.Px(16f), rowHeight - o.Px(2f));
            bool own = localEntity is { } self && line.Entity == self;
            DrawBoardLine(o, row, columns, line, own, avatarOf?.Invoke(line.Avatar));
            if (controls is { } hand && hand.Removable.Contains(line.Entity))
            {
                DrawRemove(o, row, columns.FiguresLeft - o.Px(10f), line.Entity, hand);
            }

            y += rowHeight;
        }
    }

    // One player's line: the original's number with its S while down, the player's picture, the name with its tags, kills/deaths,
    // and the round trip.
    private static void DrawBoardLine(HudOverlay o, UiRect row, BoardColumns columns, ScoreboardLine line, bool own, UiPicture? avatar)
    {
        float body = o.Px(HudOverlay.Body);
        float textY = row.Y + ((row.Height - o.Text.LineHeightAt(body)) * 0.5f);
        bool host = line.Slot == ServerSession.HostSlot;

        // The original's three inks: this player's own, one who is down, everyone else.
        Bgra32 ink = own ? o.Ink.Ally : !line.IsAlive ? o.Ink.Enemy : o.Ink.Plain;
        o.Row(row, own, ink: o.Ink.Ally);

        // "S [3]" while down, "  [3]" while up: the number the original shows every player by.
        string id = string.Create(CultureInfo.InvariantCulture, $"{(line.IsAlive ? ' ' : 'S')} [{line.Slot}]");
        o.Text.DrawAt(id, columns.Id, textY, body, line.IsAlive ? o.Quiet : o.Ink.Enemy);

        // The name, after the player's picture: their Discord avatar where they signed in, else their initial on a plate of their
        // ink, so every name starts at the same place.
        float edge = MathF.Min(row.Height - o.Px(8f), o.Px(30f));
        var frame = new UiRect(columns.Name, row.Y + ((row.Height - edge) * 0.5f), edge, edge);
        float fade = line.IsAlive ? 1f : 0.55f;
        if (avatar is { } picture)
        {
            o.Plate(frame, Bgra32.FromRgba(0, 0, 0, 160), o.Theme.Motifs.CornerSize * 0.3f);
            picture.DrawCover(o.Painter, frame, fade);
        }
        else
        {
            o.Plate(frame, HudOverlay.Fade(ink, 0.22f * fade), o.Theme.Motifs.CornerSize * 0.3f);
            string initial = line.Name.Length > 0 ? char.ToUpperInvariant(line.Name[0]).ToString() : "?";
            float letter = edge * 0.6f;
            o.Text.DrawCentredAt(initial, frame.X + (frame.Width * 0.5f), frame.Y + ((frame.Height - o.Text.LineHeightAt(letter)) * 0.5f), letter, HudOverlay.Fade(ink, fade));
        }

        float nameX = frame.Right + o.Px(10f);

        float room = columns.FiguresLeft - o.Px(16f) - nameX;
        string name = o.Text.ClipAt(OverlayText.Prose(line.Name), body, room);
        float after = nameX + o.Text.DrawAt(name, nameX, textY, body, ink) + o.Px(10f);
        float tagY = row.Y + ((row.Height - (o.Text.LineHeightAt(o.Px(HudOverlay.Small)) + o.Px(4f))) * 0.5f);
        if (host)
        {
            after = o.Tag("HOST", after, tagY, o.Ink.Accent) + o.Px(6f);
        }

        if (own)
        {
            o.Tag("YOU", after, tagY, o.Ink.Ally, filled: true);
        }

        // Kills, deaths and assists, each under its own head; then the round trip with its bars.
        o.Text.DrawRightAt(line.Kills.ToString(CultureInfo.InvariantCulture), columns.Kills, textY, body, ink);
        o.Text.DrawRightAt(line.Deaths.ToString(CultureInfo.InvariantCulture), columns.Deaths, textY, body, HudOverlay.Fade(ink, 0.8f));
        o.Text.DrawRightAt(line.Assists.ToString(CultureInfo.InvariantCulture), columns.Assists, textY, body, HudOverlay.Fade(ink, 0.8f));
        float bars = o.Px(20f);
        float barsHeight = o.Text.LineHeightAt(body) * 0.8f;
        Bgra32 pingInk = host ? o.Quiet : o.PingInk(line.Ping);
        o.Signal(new UiRect(columns.Ping - bars, row.Y + ((row.Height - barsHeight) * 0.5f), bars, barsHeight), host ? 4 : HudOverlay.PingBars(line.Ping), pingInk);
        string ping = host ? "-" : line.Ping.ToString(CultureInfo.InvariantCulture);
        o.Text.DrawRightAt(ping, columns.Ping - bars - o.Px(8f), textY, body, pingInk);
    }

    // The host's button on a joiner's line: REMOVE, lit under the pointer, and CONFIRM filled in red once it has been clicked.
    private void DrawRemove(HudOverlay o, UiRect row, float right, int entity, ScoreboardControls hand)
    {
        bool confirming = hand.Confirming == entity;
        string word = confirming ? "CONFIRM" : "REMOVE";
        float small = o.Px(HudOverlay.Small);
        float width = o.Text.MeasureAt(word, small) + o.Px(10f);
        float height = o.Text.LineHeightAt(small) + o.Px(4f);
        var button = new UiRect(right - width, row.Y + ((row.Height - height) * 0.5f), width, height);
        bool over = hand.PointerX >= button.X && hand.PointerX <= button.Right && hand.PointerY >= button.Y && hand.PointerY <= button.Bottom;
        if (over && !confirming)
        {
            o.Plate(button, HudOverlay.Fade(o.Ink.Danger, 0.3f), o.Theme.Motifs.CornerSize * 0.4f);
        }

        o.Tag(word, button.X, button.Y, o.Ink.Danger, filled: confirming);
        _removeButtons.Add((entity, button.X, button.Y, button.Width, button.Height));
    }

    // Where a board column's figures stand across it: the number and name from its left; kills, deaths, assists and the round
    // trip by their right edges, each figure's column as wide as its head; and where the first figure's column begins.
    private readonly record struct BoardColumns(float Id, float Name, float Kills, float Deaths, float Assists, float Ping, float FiguresLeft)
    {
        public static BoardColumns For(HudOverlay o, UiRect column)
        {
            float id = column.X;
            float name = id + o.Text.MeasureAt("S [00]", o.Px(HudOverlay.Body)) + o.Px(14f);
            float figure = o.Text.MeasureAt("ASSISTS", o.Px(HudOverlay.Small)) + o.Px(18f);
            float ping = column.Right;
            float assists = ping - o.Px(100f);
            float deaths = assists - figure;
            float kills = deaths - figure;
            return new BoardColumns(id, name, kills, deaths, assists, ping, kills - figure);
        }
    }
}
