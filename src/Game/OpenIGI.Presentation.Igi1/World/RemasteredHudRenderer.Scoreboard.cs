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
    /// <inheritdoc/>
    /// <remarks>
    /// A modern shooter's board, centred and drawn over the picture while its key is held: the server and map over a list of
    /// every player, their kills, deaths and round trip, this player's own line marked. A team match lists each team under
    /// its score; co-op is one list. Not the original's: it had no other players to list.
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
        float width = MathF.Min(context.Px(760f), context.SafeRect.Width * 0.7f);
        float x = context.SafeRect.X + ((context.SafeRect.Width - width) * 0.5f);
        float top = context.SafeRect.Y + (context.SafeRect.Height * 0.16f);
        float pad = context.Px(22f);
        float rowHeight = context.Px(34f);
        float labelSize = context.Px(11f);
        float nameSize = context.Px(19f);

        List<IGrouping<int, ScoreboardLine>> teams = [.. board.Lines.GroupBy(static l => l.Team).OrderBy(static g => g.Key)];
        bool teamed = teams.Count > 1;
        float headerHeight = context.Px(64f);
        float columnsHeight = context.Px(28f);
        float teamHeight = teamed ? context.Px(30f) : 0f;
        float height = headerHeight + columnsHeight + (teams.Count * teamHeight) + (board.Lines.Length * rowHeight) + pad;
        var panel = new UiRect(x, top, width, height);

        painter.FillRect(panel, UiPalette.InkAt(0.78f));
        painter.FillRect(new UiRect(x, top, width, context.Px(3f)), Accent);

        // The heading: where this is, and how many are in it.
        float headingSize = context.Px(24f);
        var headingBox = new UiRect(x + pad, top + context.Px(14f), width - (pad * 2f), headingSize);
        painter.DrawLabel(type.Heading, title.ToUpperInvariant(), new UiVec2(x + pad, UiRowStyle.CapsCenterY(type.Heading, headingSize, headingBox)), headingSize, Text, 0.04f);
        string count = board.Lines.Length == 1 ? "1 PLAYER" : $"{board.Lines.Length} PLAYERS";
        float countWidth = type.Mono.MeasureWidth(count, labelSize);
        painter.DrawLabel(type.Mono, count, new UiVec2(x + width - pad - countWidth, top + context.Px(24f)), labelSize, Muted, 0.28f);

        // The columns, right-aligned numbers under right-aligned heads.
        float pingRight = x + width - pad;
        float deathsRight = pingRight - context.Px(110f);
        float killsRight = deathsRight - context.Px(110f);
        float y = top + headerHeight;
        painter.DrawLabel(type.Mono, "PLAYER", new UiVec2(x + pad, y), labelSize, Muted, 0.28f);
        RightLabel(painter, type.Mono, "KILLS", killsRight, y, labelSize, Muted);
        RightLabel(painter, type.Mono, "DEATHS", deathsRight, y, labelSize, Muted);
        RightLabel(painter, type.Mono, "PING", pingRight, y, labelSize, Muted);
        y += columnsHeight;
        painter.FillRect(new UiRect(x + pad, y - context.Px(8f), width - (pad * 2f), context.Px(1f)), UiPalette.InkAt(0.4f).WithAlpha(90));

        foreach (IGrouping<int, ScoreboardLine> team in teams)
        {
            if (teamed)
            {
                int score = team.Key == TeamRoster.Good ? board.GoodScore : board.BadScore;
                painter.DrawLabel(
                    type.Mono, $"TEAM {team.Key + 1}  ·  {score}", new UiVec2(x + pad, y + context.Px(8f)), labelSize, Accent, 0.28f);
                y += teamHeight;
            }

            foreach (ScoreboardLine line in team.OrderByDescending(static l => l.Kills).ThenBy(static l => l.Deaths))
            {
                var row = new UiRect(x + context.Px(8f), y, width - context.Px(16f), rowHeight);
                bool own = localEntity is { } self && line.Entity == self;
                if (own)
                {
                    painter.FillRect(row, Accent.WithAlpha(46));
                    painter.FillRect(new UiRect(row.X, row.Y, context.Px(3f), row.Height), Accent);
                }

                Bgra32 colour = line.IsAlive ? Text : Muted;
                float textY = UiRowStyle.CapsCenterY(type.Body, nameSize, row);
                painter.DrawLabel(type.Body, line.Name, new UiVec2(x + pad, textY), nameSize, colour, 0.02f);
                RightLabel(painter, type.Body, line.Kills.ToString(System.Globalization.CultureInfo.InvariantCulture), killsRight, textY, nameSize, colour);
                RightLabel(painter, type.Body, line.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), deathsRight, textY, nameSize, colour);
                bool server = line.Slot == ServerSession.HostSlot;
                RightLabel(
                    painter,
                    server ? type.Mono : type.Body,
                    server ? "HOST" : line.Ping.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    pingRight,
                    server ? textY + context.Px(4f) : textY,
                    server ? labelSize : nameSize,
                    server ? Muted : PingColour(line.Ping));
                y += rowHeight;
            }
        }
    }

    // A label set with its right edge at a column's.
    private static void RightLabel(UiPainter painter, UiFont font, string text, float right, float y, float size, Bgra32 colour) =>
        painter.DrawLabel(font, text, new UiVec2(right - font.MeasureWidth(text, size), y), size, colour, 0.02f);

    // Green while a round trip is unnoticed, the accent while it shows, red when it is in the way.
    private static Bgra32 PingColour(int ping) =>
        ping < 60 ? Signal : ping < 130 ? Accent : Bgra32.FromRgb(232, 92, 80);
}
