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
    // Sizes in design units. Nothing on the board is smaller than SmallText: it is read at a glance mid-fight, and on a
    // phone the same units are a third of the size they are on a monitor.
    private const float HeaderHeight = 64f;
    private const float ColumnsHeight = 36f;
    private const float RowHeight = 48f;
    private const float RowGap = 4f;
    private const float RowText = 22f;
    private const float TitleText = 24f;
    private const float SmallText = 15f;
    private const float Gutter = 24f;

    // Each player's badge colour, by slot: distinct at a glance, none of them the accent the own row is lit in.
    private static readonly Bgra32[] BadgeColours =
    [
        Bgra32.FromRgb(96, 165, 250), Bgra32.FromRgb(52, 211, 153), Bgra32.FromRgb(244, 114, 182), Bgra32.FromRgb(167, 139, 250),
        Bgra32.FromRgb(45, 212, 191), Bgra32.FromRgb(251, 146, 60), Bgra32.FromRgb(163, 230, 53), Bgra32.FromRgb(248, 113, 113),
    ];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// A modern shooter's board, laid over the picture while its key is held: a header naming the game and where it is
    /// played, with who is still up; then the players on rows tall enough to read in a fight, each with a badge of their
    /// initial in their own colour, kills, deaths and round trip, this player's own row lit in the accent. A team match
    /// gives each team a header of its own in its colour, with its score, and the score between them.
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
        float teamHeader = teamed ? Px(44f) : 0f;
        float rows = board.Lines.Length * Px(RowHeight + RowGap);
        float height = Px(HeaderHeight) + Px(ColumnsHeight) + rows + (teams.Count * teamHeader) + Px(Gutter * 0.75f);

        float width = MathF.Min(Px(1040f), context.SafeRect.Width * 0.9f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float top = context.SafeRect.Y + MathF.Max(Px(24f), (context.SafeRect.Height - height) * 0.3f);
        var panel = new UiRect(x, top, width, height);
        float pad = Px(Gutter);

        // The board: a soft shadow, a dark glass body, a thin light edge, the accent along its top.
        painter.DrawShadow(panel, _surfaces?.Shadow ?? default, Bgra32.FromRgba(0, 0, 0, 160), Px(30f), new UiVec2(0f, Px(8f)));
        painter.FillGradient(
            panel,
            [new UiGradientStop(0f, Bgra32.FromRgba(22, 25, 29, 240)), new UiGradientStop(1f, Bgra32.FromRgba(12, 14, 16, 244))],
            UiGradientDirection.Vertical);
        Outline(painter, panel, Bgra32.FromRgba(255, 255, 255, 26), MathF.Max(1f, Px(1f)));
        painter.FillRect(new UiRect(x, top, width, MathF.Max(2f, Px(3f))), Accent);

        // The header: what is played and where on the left; who is still up, or the round, on the right.
        var header = new UiRect(x, top, width, Px(HeaderHeight));
        string mode = teamed ? "TEAM MATCH" : "CO-OP";
        float modeSize = Px(SmallText + 1f);
        float modeWidth = type.Heading.MeasureWidth(mode, modeSize) * 1.12f;
        var modeChip = new UiRect(x + pad, header.Y + ((header.Height - Px(30f)) * 0.5f), modeWidth + Px(20f), Px(30f));
        painter.FillRect(modeChip, Accent.WithAlpha(40));
        painter.DrawLabel(type.Heading, mode, new UiVec2(modeChip.X + Px(10f), UiRowStyle.CapsCenterY(type.Heading, modeSize, modeChip)), modeSize, Accent, 0.12f);
        float titleSize = Px(TitleText);
        painter.DrawLabel(type.Body, title, new UiVec2(modeChip.Right + Px(16f), UiRowStyle.CapsCenterY(type.Body, titleSize, header)), titleSize, Text, 0.01f);

        int up = board.Lines.Count(static l => l.IsAlive);
        string status = teamed
            ? string.Create(CultureInfo.InvariantCulture, $"Round {board.Round}")
            : string.Create(CultureInfo.InvariantCulture, $"{up} of {board.Lines.Length} alive");
        RightLabel(painter, type.Body, status, x + width - pad, UiRowStyle.CapsCenterY(type.Body, Px(RowText - 2f), header), Px(RowText - 2f), up > 0 || teamed ? UiPalette.TextSecondary : UiPalette.Danger);
        painter.FillRect(new UiRect(x + pad, header.Bottom - 1f, width - (pad * 2f), 1f), Bgra32.FromRgba(255, 255, 255, 22));

        // The columns' heads.
        float pingRight = x + width - pad;
        float deathsRight = pingRight - Px(120f);
        float killsRight = deathsRight - Px(110f);
        float nameLeft = x + pad + Px(54f);
        var heads = new UiRect(x, header.Bottom, width, Px(ColumnsHeight));
        float headSize = Px(SmallText);
        float headY = UiRowStyle.CapsCenterY(type.Body, headSize, heads);
        painter.DrawLabel(type.Body, "PLAYER", new UiVec2(nameLeft, headY), headSize, UiPalette.TextMuted, 0.14f);
        RightLabel(painter, type.Body, "KILLS", killsRight, headY, headSize, UiPalette.TextMuted);
        RightLabel(painter, type.Body, "DEATHS", deathsRight, headY, headSize, UiPalette.TextMuted);
        RightLabel(painter, type.Body, "PING", pingRight, headY, headSize, UiPalette.TextMuted);

        float y = heads.Bottom;
        foreach (IGrouping<int, ScoreboardLine> team in teams)
        {
            if (teamed)
            {
                Bgra32 colour = team.Key == TeamRoster.Good ? UiPalette.Info : Accent;
                int score = team.Key == TeamRoster.Good ? board.GoodScore : board.BadScore;
                var band = new UiRect(x + pad, y, width - (pad * 2f), teamHeader - Px(6f));
                painter.FillGradient(band, [new UiGradientStop(0f, colour.WithAlpha(60)), new UiGradientStop(1f, colour.WithAlpha(0))], UiGradientDirection.Horizontal);
                float teamSize = Px(RowText);
                string name = string.Create(CultureInfo.InvariantCulture, $"TEAM {team.Key + 1}");
                painter.DrawLabel(type.Heading, name, new UiVec2(band.X + Px(12f), UiRowStyle.CapsCenterY(type.Heading, teamSize, band)), teamSize, colour, 0.08f);
                string alive = string.Create(CultureInfo.InvariantCulture, $"{team.Count(static l => l.IsAlive)} alive");
                float nameWidth = type.Heading.MeasureWidth(name, teamSize) * 1.08f;
                painter.DrawLabel(type.Body, alive, new UiVec2(band.X + Px(28f) + nameWidth, UiRowStyle.CapsCenterY(type.Body, Px(SmallText + 2f), band)), Px(SmallText + 2f), UiPalette.TextSecondary, 0.02f);
                RightLabel(painter, type.Heading, score.ToString(CultureInfo.InvariantCulture), band.Right - Px(12f), UiRowStyle.CapsCenterY(type.Heading, Px(28f), band), Px(28f), colour);
                y += teamHeader;
            }

            int index = 0;
            foreach (ScoreboardLine line in team.OrderByDescending(static l => l.Kills).ThenBy(static l => l.Deaths))
            {
                var row = new UiRect(x + pad, y, width - (pad * 2f), Px(RowHeight));
                bool own = localEntity is { } self && line.Entity == self;
                DrawPlayerRow(painter, type, context, row, line, own, index++, nameLeft, killsRight, deathsRight, pingRight);
                y += Px(RowHeight + RowGap);
            }
        }
    }

    // One player: a striped row, lit in the accent when it is this player's own, the badge, the name, the numbers.
    private void DrawPlayerRow(
        UiPainter painter,
        UiTheme type,
        LayoutContext context,
        UiRect row,
        ScoreboardLine line,
        bool own,
        int index,
        float nameLeft,
        float killsRight,
        float deathsRight,
        float pingRight)
    {
        if (own)
        {
            painter.FillGradient(row, [new UiGradientStop(0f, Accent.WithAlpha(72)), new UiGradientStop(1f, Accent.WithAlpha(14))], UiGradientDirection.Horizontal);
            painter.FillRect(new UiRect(row.X, row.Y, MathF.Max(3f, context.Px(4f)), row.Height), Accent);
        }
        else
        {
            painter.FillRect(row, Bgra32.FromRgba(255, 255, 255, (byte)(index % 2 == 0 ? 14 : 7)));
        }

        // The badge: the player's initial on their own colour, greyed when they are down.
        float badgeSize = context.Px(32f);
        var badge = new UiRect(row.X + context.Px(12f), row.Y + ((row.Height - badgeSize) * 0.5f), badgeSize, badgeSize);
        Bgra32 badgeColour = line.IsAlive ? BadgeColours[(int)((uint)line.Slot % (uint)BadgeColours.Length)] : UiPalette.TextDim;
        painter.FillRoundedRect(badge, _surfaces?.Rounded ?? default, badgeColour, badgeSize * 0.22f);
        string initial = line.Name.Length > 0 ? char.ToUpperInvariant(line.Name[0]).ToString() : "?";
        float initialSize = badgeSize * 0.6f;
        float initialWidth = type.Heading.MeasureWidth(initial, initialSize);
        painter.DrawLabel(
            type.Heading, initial, new UiVec2(badge.X + ((badge.Width - initialWidth) * 0.5f), UiRowStyle.CapsCenterY(type.Heading, initialSize, badge)), initialSize, Bgra32.FromRgb(12, 14, 16), 0f);

        float textSize = context.Px(RowText);
        float textY = UiRowStyle.CapsCenterY(type.Body, textSize, row);
        Bgra32 colour = own ? UiPalette.TextBright : line.IsAlive ? Text : UiPalette.TextDim;
        painter.DrawLabel(type.Body, line.Name, new UiVec2(nameLeft, textY), textSize, colour, 0.01f);

        // Down, said in so many words beside the name: a dimmed row alone reads as a rendering fault.
        float afterName = nameLeft + type.Body.MeasureWidth(line.Name, textSize) + context.Px(14f);
        if (!line.IsAlive)
        {
            Tag(painter, type, context, row, afterName, "DOWN", UiPalette.Danger);
        }
        else if (line.Slot == ServerSession.HostSlot)
        {
            Tag(painter, type, context, row, afterName, "HOST", Accent);
        }

        RightLabel(painter, type.Body, line.Kills.ToString(CultureInfo.InvariantCulture), killsRight, textY, textSize, colour);
        RightLabel(painter, type.Body, line.Deaths.ToString(CultureInfo.InvariantCulture), deathsRight, textY, textSize, colour);
        string ping = line.Slot == ServerSession.HostSlot ? string.Empty : line.Ping.ToString(CultureInfo.InvariantCulture);
        RightLabel(painter, type.Body, ping, pingRight, textY, textSize, line.Slot == ServerSession.HostSlot ? UiPalette.TextMuted : PingColour(line.Ping));
    }

    // A small word in a tinted box beside a name: HOST, DOWN.
    private static void Tag(UiPainter painter, UiTheme type, LayoutContext context, UiRect row, float left, string text, Bgra32 colour)
    {
        float size = context.Px(SmallText - 1f);
        float width = (type.Heading.MeasureWidth(text, size) * 1.1f) + context.Px(16f);
        var box = new UiRect(left, row.Y + ((row.Height - context.Px(26f)) * 0.5f), width, context.Px(26f));
        painter.FillRect(box, colour.WithAlpha(44));
        painter.DrawLabel(type.Heading, text, new UiVec2(box.X + context.Px(8f), UiRowStyle.CapsCenterY(type.Heading, size, box)), size, colour, 0.1f);
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
