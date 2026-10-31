using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>The session's scoreboard, held open on its key.</summary>
internal sealed partial class RemasteredHudRenderer
{
    // The board's measures, in design units. Nothing is smaller than LabelText: it is read at a glance mid-fight, and on
    // a phone the same units come out a third of the size they do on a monitor.
    private const float BoardWidth = 1100f;
    private const float BoardHeader = 92f;
    private const float BoardColumns = 46f;
    private const float BoardRow = 70f;
    private const float BoardRowGap = 7f;
    private const float BoardFooter = 52f;
    private const float BoardPad = 34f;
    private const float LabelText = 14f;

    private static Bgra32 BoardFace => Bgra32.FromRgba(13, 15, 18, 246);
    private static Bgra32 RowFace => Bgra32.FromRgba(255, 255, 255, 12);
    private static Bgra32 Hairline => Bgra32.FromRgba(255, 255, 255, 22);
    private static Bgra32 Alive => Bgra32.FromRgb(74, 222, 128);
    private static Bgra32 Down => Bgra32.FromRgb(251, 176, 59);
    private static Bgra32 Bad => Bgra32.FromRgb(232, 92, 80);

    // Each player's badge colour, by slot: distinct at a glance, none of them the accent the own row is lit in.
    private static readonly Bgra32[] BadgeColours =
    [
        Bgra32.FromRgb(96, 165, 250), Bgra32.FromRgb(74, 222, 128), Bgra32.FromRgb(244, 114, 182), Bgra32.FromRgb(167, 139, 250),
        Bgra32.FromRgb(45, 212, 191), Bgra32.FromRgb(251, 146, 60), Bgra32.FromRgb(163, 230, 53), Bgra32.FromRgb(248, 113, 113),
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A modern shooter's board, laid over the picture while its key is held. The header: the mode in a filled tag, the room's
    /// code large beneath its label, and on the right the mission, the squad's kills and who is still up. Then a card per
    /// player: a badge of their initial in their own colour, the name with HOST and YOU tags, a coloured state line under
    /// it, kills large and bright, deaths large and grey, ping coloured with its signal bars. The own card is lit in the
    /// accent; a fallen one is dimmed. A team match puts its two teams' scores in the header instead.
    /// </para>
    /// <para>Not the original's: it had no other players to list.</para>
    /// </remarks>
    public void DrawScoreboard(SpriteBatch batch, Scoreboard board, int? localEntity, string? roomCode, string place, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(board);
        if (_theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);

        bool teamed = board.Lines.Select(static l => l.Team).Distinct().Count() > 1;
        ScoreboardLine[] lines = [.. board.Lines.OrderBy(static l => l.Team).ThenByDescending(static l => l.Kills).ThenBy(static l => l.Deaths)];
        float rows = lines.Length * Px(BoardRow + BoardRowGap);
        float height = Px(BoardHeader) + Px(BoardColumns) + rows + Px(14f) + Px(BoardFooter);
        float width = MathF.Min(Px(BoardWidth), context.SafeRect.Width * 0.94f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float top = context.SafeRect.Y + MathF.Max(Px(20f), (context.SafeRect.Height - height) * 0.35f);
        float pad = Px(BoardPad);
        float right = x + width - pad;
        var panel = new UiRect(x, top, width, height);

        painter.DrawShadow(panel, _surfaces?.Shadow ?? default, Bgra32.FromRgba(0, 0, 0, 170), Px(32f), new UiVec2(0f, Px(8f)));
        painter.FillRoundedRect(panel, _surfaces?.Rounded ?? default, BoardFace, Px(10f));
        Outline(painter, panel, Hairline, MathF.Max(1f, Px(1f)));

        // The header: the mode, the room, and the numbers that matter on the right.
        var header = new UiRect(x, top, width, Px(BoardHeader));
        string mode = teamed ? "TEAM" : "CO-OP";
        float chipText = Px(LabelText + 2f);
        float chipWidth = (type.Heading.MeasureWidth(mode, chipText) * 1.12f) + Px(24f);
        var chip = new UiRect(x + pad, header.Y + ((header.Height - Px(36f)) * 0.5f), chipWidth, Px(36f));
        painter.FillRect(chip, Accent);
        painter.DrawLabel(type.Heading, mode, new UiVec2(chip.X + Px(12f), UiRowStyle.CapsCenterY(type.Heading, chipText, chip)), chipText, Bgra32.FromRgb(20, 16, 10), 0.1f);

        float nameX = chip.Right + Px(22f);
        string caption = roomCode is null ? "SERVER" : "ROOM";
        string heading = roomCode ?? place;
        Stat(painter, type, context, nameX, header, caption, heading, Text, leftAligned: true, Px(30f));

        float statRight = right;
        int up = lines.Count(static l => l.IsAlive);
        if (teamed)
        {
            statRight = Stat(painter, type, context, statRight, header, "TEAM 2", board.BadScore.ToString(CultureInfo.InvariantCulture), Accent);
            statRight = Stat(painter, type, context, statRight - Px(44f), header, "TEAM 1", board.GoodScore.ToString(CultureInfo.InvariantCulture), UiPalette.Info);
            Stat(painter, type, context, statRight - Px(44f), header, "ROUND", board.Round.ToString(CultureInfo.InvariantCulture), Text);
        }
        else
        {
            statRight = Stat(painter, type, context, statRight, header, "ALIVE", string.Create(CultureInfo.InvariantCulture, $"{up}/{lines.Length}"), up == 0 ? Bad : Alive);
            statRight = Stat(painter, type, context, statRight - Px(44f), header, "SQUAD KILLS", lines.Sum(static l => l.Kills).ToString(CultureInfo.InvariantCulture), Text);
            if (roomCode is not null && place.Length > 0)
            {
                string mission = place.StartsWith("Mission ", StringComparison.Ordinal) ? place["Mission ".Length..] : place;
                Stat(painter, type, context, statRight - Px(44f), header, "MISSION", mission, Accent);
            }
        }

        painter.FillRect(new UiRect(x, header.Bottom, width, 1f), Hairline);

        // The columns' heads.
        float pingRight = right - Px(10f);
        float deathsRight = pingRight - Px(150f);
        float killsRight = deathsRight - Px(120f);
        var heads = new UiRect(x, header.Bottom, width, Px(BoardColumns));
        float headText = Px(LabelText);
        float headY = UiRowStyle.CapsCenterY(type.Heading, headText, heads) + Px(4f);
        painter.DrawLabel(type.Heading, "OPERATIVE", new UiVec2(x + pad + Px(4f), headY), headText, UiPalette.TextMuted, 0.12f);
        RightLabel(painter, type.Heading, "KILLS", killsRight, headY, headText, UiPalette.TextMuted);
        RightLabel(painter, type.Heading, "DEATHS", deathsRight, headY, headText, UiPalette.TextMuted);
        RightLabel(painter, type.Heading, "PING", pingRight, headY, headText, UiPalette.TextMuted);

        float y = heads.Bottom + Px(4f);
        foreach (ScoreboardLine line in lines)
        {
            var row = new UiRect(x + pad - Px(14f), y, width - (pad * 2f) + Px(28f), Px(BoardRow));
            bool own = localEntity is { } self && line.Entity == self;
            DrawPlayerCard(painter, type, context, row, line, own, teamed, killsRight, deathsRight, pingRight);
            y += Px(BoardRow + BoardRowGap);
        }

        // The footer: the game's name, and the key that keeps the board up.
        float footerTop = top + height - Px(BoardFooter);
        painter.FillRect(new UiRect(x, footerTop, width, 1f), Hairline);
        var footer = new UiRect(x, footerTop, width, Px(BoardFooter));
        float footText = Px(LabelText);
        float footY = UiRowStyle.CapsCenterY(type.Heading, footText, footer);
        painter.DrawLabel(type.Heading, "OPENIGI", new UiVec2(x + pad, footY), footText, UiPalette.TextMuted, 0.14f);
        float capWidth = (type.Heading.MeasureWidth("TAB", footText) * 1.14f) + Px(22f);
        var cap = new UiRect(right - capWidth, footer.Y + ((footer.Height - Px(28f)) * 0.5f), capWidth, Px(28f));
        Outline(painter, cap, UiPalette.TextMuted.WithAlpha(160), MathF.Max(1f, Px(1.5f)));
        painter.DrawLabel(type.Heading, "TAB", new UiVec2(cap.X + Px(11f), UiRowStyle.CapsCenterY(type.Heading, footText, cap)), footText, Text, 0.12f);
        RightLabel(painter, type.Heading, "HOLD", cap.X - Px(12f), footY, footText, UiPalette.TextMuted);
    }

    // One header figure, its label small over its value large; returns where the next figure to its left ends.
    private static float Stat(
        UiPainter painter, UiTheme type, LayoutContext context, float edge, UiRect header, string label, string value, Bgra32 colour, bool leftAligned = false, float? valueSize = null)
    {
        float labelSize = context.Px(LabelText - 1f);
        float size = valueSize ?? context.Px(30f);
        float labelWidth = type.Heading.MeasureWidth(label, labelSize) * 1.14f;
        float valueWidth = type.Heading.MeasureWidth(value, size);
        float wide = MathF.Max(labelWidth, valueWidth);
        float left = leftAligned ? edge : edge - wide;
        float labelY = header.Y + context.Px(22f);
        float valueY = labelY + labelSize + context.Px(6f);
        if (leftAligned)
        {
            painter.DrawLabel(type.Heading, label, new UiVec2(left, labelY), labelSize, UiPalette.TextMuted, 0.12f);
            painter.DrawLabel(type.Heading, value, new UiVec2(left, valueY), size, colour, 0.02f);
        }
        else
        {
            painter.DrawLabel(type.Heading, label, new UiVec2(edge - labelWidth, labelY), labelSize, UiPalette.TextMuted, 0.12f);
            painter.DrawLabel(type.Heading, value, new UiVec2(edge - valueWidth, valueY), size, colour, 0.02f);
        }

        return left;
    }

    // One player's card: lit when it is this player's own, dimmed when it has fallen.
    private void DrawPlayerCard(
        UiPainter painter,
        UiTheme type,
        LayoutContext context,
        UiRect row,
        ScoreboardLine line,
        bool own,
        bool teamed,
        float killsRight,
        float deathsRight,
        float pingRight)
    {
        float Px(float units) => context.Px(units);
        painter.FillRoundedRect(row, _surfaces?.Rounded ?? default, own ? Accent.WithAlpha(36) : RowFace, Px(6f));
        if (own)
        {
            painter.FillRect(new UiRect(row.X, row.Y + Px(2f), MathF.Max(2f, Px(3f)), row.Height - Px(4f)), Accent);
        }

        bool host = line.Slot == ServerSession.HostSlot;
        float dim = line.IsAlive ? 1f : 0.55f;

        // The badge: the player's initial on their own colour (their team's in a team match).
        float badgeSize = Px(44f);
        var badge = new UiRect(row.X + Px(16f), row.Y + ((row.Height - badgeSize) * 0.5f), badgeSize, badgeSize);
        Bgra32 badgeColour = teamed
            ? (line.Team == TeamRoster.Good ? UiPalette.Info : Accent)
            : BadgeColours[(int)((uint)line.Slot % (uint)BadgeColours.Length)];
        painter.FillRoundedRect(badge, _surfaces?.Rounded ?? default, badgeColour.WithAlpha((byte)(255 * dim)), Px(5f));
        string initial = line.Name.Length > 0 ? char.ToUpperInvariant(line.Name[0]).ToString() : "?";
        float initialSize = Px(22f);
        float initialWidth = type.Heading.MeasureWidth(initial, initialSize);
        painter.DrawLabel(
            type.Heading, initial, new UiVec2(badge.X + ((badge.Width - initialWidth) * 0.5f), UiRowStyle.CapsCenterY(type.Heading, initialSize, badge)), initialSize, Bgra32.FromRgb(14, 16, 20), 0f);

        // The name and its tags, with its state on a line of its own beneath.
        float nameX = badge.Right + Px(16f);
        float nameSize = Px(24f);
        float nameY = row.Y + Px(12f);
        Bgra32 nameColour = line.IsAlive ? UiPalette.TextBright : UiPalette.TextMuted;
        painter.DrawLabel(type.Heading, line.Name, new UiVec2(nameX, nameY), nameSize, nameColour, 0.01f);
        float tagX = nameX + type.Heading.MeasureWidth(line.Name, nameSize) + Px(12f);
        if (host)
        {
            tagX = Tag(painter, type, context, tagX, nameY + Px(2f), "HOST", Accent, filled: false) + Px(8f);
        }

        if (own)
        {
            Tag(painter, type, context, tagX, nameY + Px(2f), "YOU", UiPalette.TextBright, filled: true);
        }

        float stateSize = Px(LabelText);
        float stateY = nameY + nameSize + Px(8f);
        (string state, Bgra32 stateColour) = line.IsAlive ? ("ALIVE", Alive) : ("DOWN", Down);
        painter.FillDot(new UiVec2(nameX + Px(4f), stateY + (stateSize * 0.5f)), Px(4f), stateColour);
        painter.DrawLabel(type.Heading, state, new UiVec2(nameX + Px(14f), stateY), stateSize, stateColour, 0.1f);

        // The numbers: kills bright, deaths grey, ping in its colour with its bars.
        float figureSize = Px(34f);
        float figureY = UiRowStyle.CapsCenterY(type.Heading, figureSize, row);
        RightLabel(painter, type.Heading, line.Kills.ToString(CultureInfo.InvariantCulture), killsRight, figureY, figureSize, line.IsAlive ? UiPalette.TextBright : UiPalette.TextMuted);
        RightLabel(painter, type.Heading, line.Deaths.ToString(CultureInfo.InvariantCulture), deathsRight, figureY, figureSize, UiPalette.TextMuted);

        int bars = host ? 4 : line.Ping < 60 ? 4 : line.Ping < 110 ? 3 : line.Ping < 170 ? 2 : 1;
        Bgra32 pingColour = host ? UiPalette.TextMuted : PingColour(line.Ping);
        float barsWidth = Px(26f);
        DrawSignal(painter, context, new UiRect(pingRight - barsWidth, row.Y + ((row.Height - Px(18f)) * 0.5f), barsWidth, Px(18f)), bars, pingColour);
        float pingSize = Px(22f);
        string ping = host ? "-" : string.Create(CultureInfo.InvariantCulture, $"{line.Ping}ms");
        RightLabel(painter, type.Heading, ping, pingRight - barsWidth - Px(10f), UiRowStyle.CapsCenterY(type.Heading, pingSize, row), pingSize, pingColour);
    }

    // A small word in a box beside a name: HOST outlined in the accent, YOU filled.
    private static float Tag(UiPainter painter, UiTheme type, LayoutContext context, float left, float top, string text, Bgra32 colour, bool filled)
    {
        float size = context.Px(LabelText - 1f);
        float width = (type.Heading.MeasureWidth(text, size) * 1.14f) + context.Px(14f);
        var box = new UiRect(left, top, width, context.Px(22f));
        if (filled)
        {
            painter.FillRect(box, colour);
        }
        else
        {
            Outline(painter, box, colour, MathF.Max(1f, context.Px(1.5f)));
        }

        painter.DrawLabel(type.Heading, text, new UiVec2(box.X + context.Px(7f), UiRowStyle.CapsCenterY(type.Heading, size, box)), size, filled ? Bgra32.FromRgb(14, 16, 20) : colour, 0.12f);
        return box.Right;
    }

    // Four rising bars, as many lit as the round trip deserves.
    private static void DrawSignal(UiPainter painter, LayoutContext context, UiRect area, int lit, Bgra32 colour)
    {
        float bar = context.Px(4f);
        float gap = (area.Width - (bar * 4f)) / 3f;
        for (int i = 0; i < 4; i++)
        {
            float h = area.Height * (0.4f + (0.2f * i));
            var r = new UiRect(area.X + (i * (bar + gap)), area.Bottom - h, bar, h);
            painter.FillRect(r, i < lit ? colour : Bgra32.FromRgba(255, 255, 255, 36));
        }
    }

    private static void Outline(UiPainter painter, UiRect rect, Bgra32 colour, float thickness)
    {
        painter.FillRect(new UiRect(rect.X, rect.Y, rect.Width, thickness), colour);
        painter.FillRect(new UiRect(rect.X, rect.Bottom - thickness, rect.Width, thickness), colour);
        painter.FillRect(new UiRect(rect.X, rect.Y, thickness, rect.Height), colour);
        painter.FillRect(new UiRect(rect.Right - thickness, rect.Y, thickness, rect.Height), colour);
    }

    // A label set with its right edge at a column's.
    private static void RightLabel(UiPainter painter, UiFont font, string text, float right, float y, float size, Bgra32 colour) =>
        painter.DrawLabel(font, text, new UiVec2(right - font.MeasureWidth(text, size), y), size, colour, 0.02f);

    // Green while a round trip is unnoticed, amber while it shows, red when it is in the way.
    private static Bgra32 PingColour(int ping) => ping < 60 ? Alive : ping < 110 ? Down : Bad;

    /// <inheritdoc/>
    /// <remarks>
    /// Low and centred, clear of the crosshair: DOWN in the alarm colour, what brings a fallen player back, and the
    /// revival's bar filling in the accent as a teammate stands by.
    /// </remarks>
    public void DrawDowned(SpriteBatch batch, float progress, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_theme is not { } type)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float Px(float units) => context.Px(units);
        float width = MathF.Min(Px(560f), context.SafeRect.Width * 0.9f);
        float height = Px(150f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float y = context.SafeRect.Y + (context.SafeRect.Height * 0.62f);
        var panel = new UiRect(x, y, width, height);
        painter.DrawShadow(panel, _surfaces?.Shadow ?? default, Bgra32.FromRgba(0, 0, 0, 160), Px(26f), new UiVec2(0f, Px(6f)));
        painter.FillRoundedRect(panel, _surfaces?.Rounded ?? default, BoardFace, Px(10f));
        painter.FillRect(new UiRect(x, y, width, MathF.Max(2f, Px(3f))), Bad);

        float titleSize = Px(34f);
        string title = progress > 0f ? "BEING REVIVED" : "YOU'RE DOWN";
        float titleWidth = type.Heading.MeasureWidth(title, titleSize) * 1.06f;
        painter.DrawLabel(type.Heading, title, new UiVec2(x + ((width - titleWidth) * 0.5f), y + Px(22f)), titleSize, progress > 0f ? Accent : Bad, 0.06f);

        string line = progress > 0f
            ? "Stay put, a teammate is with you"
            : "A teammate can revive you by standing beside you for 5 seconds  ·  Fire to watch another";
        float lineSize = Px(17f);
        float lineWidth = type.Body.MeasureWidth(line, lineSize);
        painter.DrawLabel(type.Body, line, new UiVec2(x + ((width - lineWidth) * 0.5f), y + Px(68f)), lineSize, UiPalette.TextSecondary, 0.01f);

        var track = new UiRect(x + Px(34f), y + height - Px(40f), width - Px(68f), Px(12f));
        painter.FillRoundedRect(track, _surfaces?.Rounded ?? default, Bgra32.FromRgba(255, 255, 255, 26), Px(6f));
        if (progress > 0f)
        {
            var fill = new UiRect(track.X, track.Y, MathF.Max(track.Height, track.Width * Math.Clamp(progress, 0f, 1f)), track.Height);
            painter.FillRoundedRect(fill, _surfaces?.Rounded ?? default, Accent, Px(6f));
        }
    }
}
