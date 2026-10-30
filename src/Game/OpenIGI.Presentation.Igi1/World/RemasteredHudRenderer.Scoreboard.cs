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
    // Each player's badge colour, by slot: distinct at a glance, none of them the accent the own line is marked in.
    private static readonly Bgra32[] BadgeColours =
    [
        Bgra32.FromRgb(96, 165, 250), Bgra32.FromRgb(52, 211, 153), Bgra32.FromRgb(244, 114, 182), Bgra32.FromRgb(167, 139, 250),
        Bgra32.FromRgb(45, 212, 191), Bgra32.FromRgb(251, 146, 60), Bgra32.FromRgb(163, 230, 53), Bgra32.FromRgb(248, 113, 113),
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A modern shooter's board, laid over the picture while its key is held: a header strip naming the game and where it
    /// is played, then each team as a block -- an emblem with its score and who is still up on the left, its players on
    /// striped rows to the right, each with a badge of their initial in their own colour, kills, deaths and round trip.
    /// This player's own row is lit in the accent. Co-op is one team; a team match stacks two around the score.
    /// </para>
    /// <para>Not the original's: it had no other players to list.</para>
    /// </remarks>
    public void DrawScoreboard(SpriteBatch batch, Scoreboard board, int? localEntity, string title, int screenWidth, int screenHeight)
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

        List<IGrouping<int, ScoreboardLine>> teams = [.. board.Lines.GroupBy(static l => l.Team).OrderBy(static g => g.Key)];
        bool teamed = teams.Count > 1;

        float headerHeight = Px(46f);
        float columnsHeight = Px(26f);
        float rowHeight = Px(34f);
        float teamGap = Px(10f);
        float divider = teamed ? Px(54f) : 0f;
        float footerHeight = Px(30f);
        float BlockHeight(IGrouping<int, ScoreboardLine> team) => Math.Max(team.Count() * rowHeight, Px(124f));
        float height = headerHeight + columnsHeight + teams.Sum(BlockHeight) + (teams.Count * teamGap) + divider + footerHeight + Px(6f);

        float width = MathF.Min(Px(980f), context.SafeRect.Width * 0.8f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float top = context.SafeRect.Y + (context.SafeRect.Height * 0.13f);
        var panel = new UiRect(x, top, width, height);

        // The board: a soft shadow, a dark glass body darkening downwards, a thin light edge.
        painter.DrawShadow(panel, _surfaces?.Shadow ?? default, Bgra32.FromRgba(0, 0, 0, 150), Px(28f), new UiVec2(0f, Px(6f)));
        painter.FillGradient(
            panel,
            [new UiGradientStop(0f, Bgra32.FromRgba(24, 27, 31, 236)), new UiGradientStop(1f, Bgra32.FromRgba(12, 14, 16, 240))],
            UiGradientDirection.Vertical);
        Outline(painter, panel, Bgra32.FromRgba(255, 255, 255, 22), Math.Max(1f, Px(1f)));

        // The header: the game and where it is played on the left, how many play on the right.
        var header = new UiRect(x, top, width, headerHeight);
        painter.FillRect(header, Bgra32.FromRgba(0, 0, 0, 70));
        painter.FillRect(new UiRect(x, top + headerHeight - 1f, width, 1f), Bgra32.FromRgba(255, 255, 255, 18));
        float pad = Px(18f);
        float diamond = Px(10f);
        painter.FillDiamond(new UiVec2(x + pad + (diamond * 0.5f), top + (headerHeight * 0.5f)), diamond, Accent);
        float labelSize = Px(11f);
        float headY = top + ((headerHeight - labelSize) * 0.5f) - Px(1f);
        string mode = teamed ? "TEAM MATCH" : "CO-OP";
        float modeX = x + pad + diamond + Px(12f);
        painter.DrawLabel(type.Mono, mode, new UiVec2(modeX, headY), labelSize, Accent, 0.3f);
        float modeWidth = type.Mono.MeasureWidth(mode, labelSize) * 1.3f;
        float titleSize = Px(17f);
        painter.DrawLabel(
            type.Body, title, new UiVec2(modeX + modeWidth + Px(14f), UiRowStyle.CapsCenterY(type.Body, titleSize, header)), titleSize, Text, 0.02f);
        string count = board.Lines.Length == 1 ? "1 PLAYER" : string.Create(CultureInfo.InvariantCulture, $"{board.Lines.Length} PLAYERS");
        if (teamed)
        {
            count = string.Create(CultureInfo.InvariantCulture, $"ROUND {board.Round}  ·  {count}");
        }

        RightLabel(painter, type.Mono, count, x + width - pad, headY, labelSize, Muted);

        // The columns' heads, over every team's rows.
        float emblemWidth = Px(170f);
        float rowsLeft = x + emblemWidth;
        float pingRight = x + width - pad;
        float deathsRight = pingRight - Px(96f);
        float killsRight = deathsRight - Px(80f);
        float y = top + headerHeight;
        float columnY = y + ((columnsHeight - labelSize) * 0.5f) + Px(2f);
        painter.DrawLabel(type.Mono, "PLAYER", new UiVec2(rowsLeft + Px(48f), columnY), labelSize, Muted, 0.28f);
        RightLabel(painter, type.Mono, "KILLS", killsRight, columnY, labelSize, Muted);
        RightLabel(painter, type.Mono, "DEATHS", deathsRight, columnY, labelSize, Muted);
        RightLabel(painter, type.Mono, "PING", pingRight, columnY, labelSize, Muted);
        y += columnsHeight;

        for (int t = 0; t < teams.Count; t++)
        {
            IGrouping<int, ScoreboardLine> team = teams[t];
            float block = BlockHeight(team);
            Bgra32 colour = teamed && team.Key == TeamRoster.Good ? UiPalette.Info : Accent;
            int score = team.Key == TeamRoster.Good ? board.GoodScore : board.BadScore;
            int alive = team.Count(static l => l.IsAlive);
            DrawTeamEmblem(
                painter, type, new UiRect(x, y, emblemWidth, block), teamed ? score : alive, teamed ? $"TEAM {team.Key + 1}" : "OPERATIVES", alive, team.Count(), colour);

            float rowY = y;
            int index = 0;
            foreach (ScoreboardLine line in team.OrderByDescending(static l => l.Kills).ThenBy(static l => l.Deaths))
            {
                var row = new UiRect(rowsLeft, rowY, (x + width) - rowsLeft - Px(8f), rowHeight - Px(2f));
                bool own = localEntity is { } self && line.Entity == self;
                DrawPlayerRow(painter, type, row, line, own, index++, killsRight, deathsRight, pingRight);
                rowY += rowHeight;
            }

            y += block + teamGap;

            // Between two teams: the score, as large as the header is small.
            if (teamed && t == 0)
            {
                var band = new UiRect(x, y, width, divider);
                painter.FillGradient(
                    band,
                    [
                        new UiGradientStop(0f, Bgra32.FromRgba(255, 255, 255, 0)),
                        new UiGradientStop(0.5f, Bgra32.FromRgba(255, 255, 255, 14)),
                        new UiGradientStop(1f, Bgra32.FromRgba(255, 255, 255, 0)),
                    ],
                    UiGradientDirection.Horizontal);
                string versus = string.Create(CultureInfo.InvariantCulture, $"{board.GoodScore}   :   {board.BadScore}");
                float versusSize = Px(28f);
                float versusWidth = type.Heading.MeasureWidth(versus, versusSize);
                painter.DrawLabel(
                    type.Heading, versus, new UiVec2(x + ((width - versusWidth) * 0.5f), UiRowStyle.CapsCenterY(type.Heading, versusSize, band)), versusSize, Text, 0.04f);
                y += divider;
            }
        }

        // The footer: which key keeps the board up.
        float footerTop = top + height - footerHeight;
        painter.FillRect(new UiRect(x, footerTop, width, 1f), Bgra32.FromRgba(255, 255, 255, 14));
        float footSize = Px(10f);
        float footY = footerTop + ((footerHeight - footSize) * 0.5f);
        painter.DrawLabel(type.Mono, "OPENIGI", new UiVec2(x + pad, footY), footSize, UiPalette.TextDim, 0.3f);
        RightLabel(painter, type.Mono, "HOLD TAB", x + width - pad, footY, footSize, UiPalette.TextDim);
    }

    // A team's emblem: a cut-cornered shield in the team's colour holding its number, its name and who is still up beneath.
    private static void DrawTeamEmblem(
        UiPainter painter, UiTheme type, UiRect column, int number, string name, int alive, int total, Bgra32 colour)
    {
        float size = MathF.Min(column.Width * 0.44f, column.Height * 0.5f);
        var shield = new UiRect(column.X + ((column.Width - size) * 0.5f), column.Y + (column.Height * 0.1f), size, size);
        float cut = size * 0.18f;
        painter.FillCutRect(shield, cut, UiCorners.TopLeft | UiCorners.BottomRight, colour.WithAlpha(70));
        var inner = new UiRect(shield.X + 2f, shield.Y + 2f, shield.Width - 4f, shield.Height - 4f);
        painter.FillCutRect(inner, cut * 0.9f, UiCorners.TopLeft | UiCorners.BottomRight, Bgra32.FromRgba(10, 12, 14, 230));
        painter.FillRect(new UiRect(inner.X + cut, inner.Bottom - 3f, inner.Width - (cut * 2f), 2f), colour);

        string digits = number.ToString(CultureInfo.InvariantCulture);
        float digitSize = size * 0.56f;
        float digitWidth = type.Heading.MeasureWidth(digits, digitSize);
        painter.DrawLabel(
            type.Heading, digits, new UiVec2(shield.X + ((shield.Width - digitWidth) * 0.5f), UiRowStyle.CapsCenterY(type.Heading, digitSize, shield)), digitSize, colour, 0f);

        float nameSize = MathF.Max(9f, size * 0.15f);
        float nameWidth = type.Mono.MeasureWidth(name, nameSize) * 1.25f;
        float nameY = shield.Bottom + (size * 0.16f);
        painter.DrawLabel(type.Mono, name, new UiVec2(column.X + ((column.Width - nameWidth) * 0.5f), nameY), nameSize, Text, 0.25f);
        string up = string.Create(CultureInfo.InvariantCulture, $"ALIVE {alive}/{total}");
        float upSize = MathF.Max(8f, size * 0.13f);
        float upWidth = type.Mono.MeasureWidth(up, upSize) * 1.25f;
        painter.DrawLabel(
            type.Mono, up, new UiVec2(column.X + ((column.Width - upWidth) * 0.5f), nameY + (nameSize * 1.8f)), upSize, alive > 0 ? Muted : UiPalette.Danger, 0.25f);
    }

    // One player: a striped row, lit in the accent when it is this player's own, the badge, the name, the numbers.
    private void DrawPlayerRow(
        UiPainter painter, UiTheme type, UiRect row, ScoreboardLine line, bool own, int index, float killsRight, float deathsRight, float pingRight)
    {
        if (own)
        {
            painter.FillGradient(row, [new UiGradientStop(0f, Accent.WithAlpha(90)), new UiGradientStop(1f, Accent.WithAlpha(18))], UiGradientDirection.Horizontal);
            painter.FillRect(new UiRect(row.X, row.Y, 3f, row.Height), Accent);
        }
        else
        {
            painter.FillRect(row, Bgra32.FromRgba(255, 255, 255, (byte)(index % 2 == 0 ? 16 : 8)));
        }

        // The badge: the player's initial on their own colour, greyed when they are down.
        float badgeSize = row.Height - 8f;
        var badge = new UiRect(row.X + 10f, row.Y + 4f, badgeSize, badgeSize);
        Bgra32 badgeColour = line.IsAlive ? BadgeColours[(int)((uint)line.Slot % (uint)BadgeColours.Length)] : UiPalette.TextDim;
        painter.FillRoundedRect(badge, _surfaces?.Rounded ?? default, badgeColour, badgeSize * 0.2f);
        string initial = line.Name.Length > 0 ? char.ToUpperInvariant(line.Name[0]).ToString() : "?";
        float initialSize = badgeSize * 0.62f;
        float initialWidth = type.Heading.MeasureWidth(initial, initialSize);
        painter.DrawLabel(
            type.Heading, initial, new UiVec2(badge.X + ((badge.Width - initialWidth) * 0.5f), UiRowStyle.CapsCenterY(type.Heading, initialSize, badge)), initialSize, Bgra32.FromRgb(12, 14, 16), 0f);

        float nameSize = row.Height * 0.5f;
        float textY = UiRowStyle.CapsCenterY(type.Body, nameSize, row);
        Bgra32 colour = own ? UiPalette.AccentLight : line.IsAlive ? Text : UiPalette.TextDim;
        float nameX = badge.Right + 12f;
        painter.DrawLabel(type.Body, line.Name, new UiVec2(nameX, textY), nameSize, colour, 0.02f);

        // Down, said in so many words beside the name: a dimmed line alone reads as a rendering fault.
        if (!line.IsAlive)
        {
            float tagSize = nameSize * 0.62f;
            float tagX = nameX + type.Body.MeasureWidth(line.Name, nameSize) + 12f;
            painter.DrawLabel(type.Mono, "DOWN", new UiVec2(tagX, UiRowStyle.CapsCenterY(type.Mono, tagSize, row)), tagSize, UiPalette.Danger, 0.28f);
        }

        RightLabel(painter, type.Body, line.Kills.ToString(CultureInfo.InvariantCulture), killsRight, textY, nameSize, colour);
        RightLabel(painter, type.Body, line.Deaths.ToString(CultureInfo.InvariantCulture), deathsRight, textY, nameSize, colour);
        if (line.Slot == ServerSession.HostSlot)
        {
            float chipSize = nameSize * 0.6f;
            const string chip = "HOST";
            float chipWidth = (type.Mono.MeasureWidth(chip, chipSize) * 1.3f) + 12f;
            var chipRect = new UiRect(pingRight - chipWidth, row.Y + ((row.Height - (chipSize + 10f)) * 0.5f), chipWidth, chipSize + 10f);
            painter.FillRect(chipRect, Accent.WithAlpha(46));
            painter.DrawLabel(type.Mono, chip, new UiVec2(chipRect.X + 6f, UiRowStyle.CapsCenterY(type.Mono, chipSize, chipRect)), chipSize, Accent, 0.3f);
        }
        else
        {
            RightLabel(painter, type.Body, line.Ping.ToString(CultureInfo.InvariantCulture), pingRight, textY, nameSize, PingColour(line.Ping));
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

    // Green while a round trip is unnoticed, the accent while it shows, red when it is in the way.
    private static Bgra32 PingColour(int ping) =>
        ping < 60 ? Signal : ping < 130 ? Accent : Bgra32.FromRgb(232, 92, 80);
}
