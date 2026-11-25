using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Leaderboards;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The landing page's glance at the boards, at its bottom right on a monitor: the player's place this season and the week's
/// movement over a bar of the season gone; their best on the mission they are up to against its record; and the ways in --
/// the Leaderboard section, and a race against the record.
/// </summary>
internal sealed class LeaderboardTeaser : Grid
{
    /// <summary>Initializes a new instance of the <see cref="LeaderboardTeaser"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="board">This season's board, or null while it is read.</param>
    /// <param name="mission">The mission the player is up to.</param>
    /// <param name="difficulty">Its difficulty's name.</param>
    /// <param name="record">Its board's record and the player's best there, or null.</param>
    /// <param name="open">Opens the Leaderboard section.</param>
    /// <param name="race">Races the record; null where its run can't be raced.</param>
    public LeaderboardTeaser(UiTheme theme, Leaderboard? board, string mission, string difficulty, ShellRecord? record, Action open, Action? race)
    {
        ArgumentNullException.ThrowIfNull(open);
        Width = 470f;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        ColumnDefinitions.Add(GridLength.Star);
        var buttons = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 10f, Margin = new Thickness(22f, 16f, 22f, 16f) };
        var openButton = new BoardButton(theme, "Open leaderboard", BoardButtonLook.Solid) { Key = "L", Arrow = true, ButtonHeight = 50f, TextSize = 15f, HorizontalAlignment = HorizontalAlignment.Stretch };
        openButton.Activated += _ => open();
        buttons.Add(openButton, grow: 1f);
        if (race is not null)
        {
            var raceButton = new BoardButton(theme, "Race WR", BoardButtonLook.Accent) { Icon = KitIcon.Play, ButtonHeight = 50f, ButtonWidth = 140f, TextSize = 15f };
            raceButton.Activated += _ => race();
            buttons.Add(raceButton);
        }

        UiElement[] parts =
        [
            new Head(theme),
            new Standing(theme, board),
            new Best(theme, mission, difficulty, record),
            buttons,
        ];
        for (int i = 0; i < parts.Length; i++)
        {
            RowDefinitions.Add(GridLength.Auto);
            Add(parts[i], i, 0);
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        ArgumentNullException.ThrowIfNull(painter);
        BoardLook.Panel(painter, Bounds);
        painter.FillRect(new UiRect(Bounds.X, Bounds.Y, Bounds.Width, painter.Context.Px(2f)), UiPalette.Accent);
        base.OnDraw(painter);
    }

    // A rule across the teaser under a part.
    private static void Rule(UiPainter painter, UiRect bounds) =>
        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - painter.Context.Px(1f), bounds.Width, painter.Context.Px(1f)), UiPalette.White(0.1f));

    // LEADERBOARD and the season.
    private sealed class Head(UiTheme theme) : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(56f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float x = Bounds.X + context.Px(22f);
            KitIcons.Draw(painter, KitIcon.Leaderboard, new UiVec2(x + context.Px(8f), Bounds.Center.Y), context.Px(15f), theme.Accent);
            float px = context.Px(18f);
            painter.DrawLabel(theme.Display, "LEADERBOARD", new UiVec2(x + context.Px(26f), UiRowStyle.CapsCenterY(theme.Display, px, Bounds)), px, theme.TextBright, 0.08f);
            string season = "Ranked  ·  " + DateTimeOffset.UtcNow.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            BoardLook.Right(painter, theme.Mono, season, Bounds.Right - context.Px(22f), UiRowStyle.CenterY(theme.Mono, context.Px(11f), Bounds), context.Px(11f), theme.TextSecondary);
            Rule(painter, Bounds);
        }
    }

    // The player's place this season, its movement, and the season gone.
    private sealed class Standing(UiTheme theme, Leaderboard? board) : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(142f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float x = Bounds.X + context.Px(22f);
            float right = Bounds.Right - context.Px(22f);
            float y = Bounds.Y + context.Px(20f);
            BoardLook.Caption(painter, theme, "Your season rank", new UiVec2(x, y), 10f, theme.Accent);
            BoardRow? own = board?.You;
            if (own is not null && (own.Climbed is not null || board!.IsNewThisWeek(own)))
            {
                float end = right - theme.Mono.MeasureWidth("this week", context.Px(11f));
                painter.DrawText(theme.Mono, "this week", new UiVec2(end, y - context.Px(2f)), context.Px(11f), theme.TextSecondary);
                float width = context.Px(own.Climbed is { } m ? 14f + (8f * m.ToString(CultureInfo.InvariantCulture).Length) : 34f);
                BoardLook.Movement(painter, theme, end - context.Px(8f) - width, y + context.Px(5f), own.Climbed, board!.IsNewThisWeek(own), 12f);
            }

            y += context.Px(20f);
            float big = context.Px(56f);
            string place = own is null ? "—" : "#" + own.Rank.ToString("N0", CultureInfo.InvariantCulture);
            float placeEnd = painter.DrawLabel(theme.Display, place, new UiVec2(x, y - (big * 0.08f)), big, own is null ? theme.TextMuted : theme.TextBright, 0f).X;
            string under = own is null
                ? board is null ? "Reading the board…" : "Not ranked yet this season"
                : board!.Players > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"of {board.Players:N0}  ·  top {Math.Max(1, (int)Math.Ceiling(own.Rank * 100.0 / board.Players))}%")
                    : string.Empty;
            painter.DrawText(theme.Mono, under, new UiVec2(placeEnd + context.Px(12f), y + (big * 0.45f)), context.Px(13f), theme.TextSecondary);

            int days = LeaderboardSeasons.DaysLeft(DateTimeOffset.UtcNow);
            int total = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);
            string left = string.Create(CultureInfo.InvariantCulture, $"{days} {(days == 1 ? "day" : "days")} left");
            float leftWidth = theme.Mono.MeasureWidth(left, context.Px(12f));
            float barY = Bounds.Bottom - context.Px(24f);
            BoardLook.Bar(painter, new UiRect(x, barY, right - x - leftWidth - context.Px(12f), context.Px(4f)), 1.0 - ((double)days / total), theme.Accent);
            painter.DrawText(theme.Mono, left, new UiVec2(right - leftWidth, barY - context.Px(6f)), context.Px(12f), theme.Accent);
            Rule(painter, Bounds);
        }
    }

    // The player's best on the mission they are up to, against its record.
    private sealed class Best(UiTheme theme, string mission, string difficulty, ShellRecord? record) : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(132f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float x = Bounds.X + context.Px(22f);
            float right = Bounds.Right - context.Px(22f);
            float y = Bounds.Y + context.Px(20f);
            BoardLook.Caption(painter, theme, $"{mission}  ·  your best", new UiVec2(x, y), 10f, theme.Accent);
            if (record?.YourSeconds is not null)
            {
                float width = BoardLook.TagWidth(context, theme, "Verified") + context.Px(17f);
                BoardLook.Verified(painter, theme, right - width, y + context.Px(5f));
            }

            y += context.Px(20f);
            float big = context.Px(40f);
            if (record?.YourSeconds is { } seconds)
            {
                float end = painter.DrawText(theme.Mono, MissionRow.Time((int)Math.Round(seconds * 30)), new UiVec2(x, y - (big * 0.1f)), big, theme.Accent).X;
                string place = string.Create(CultureInfo.InvariantCulture, $"#{record.YourPlace:N0}  ·  {difficulty}  ·  {record.Rules}");
                painter.DrawText(theme.Mono, place, new UiVec2(end + context.Px(12f), y + (big * 0.42f)), context.Px(13f), theme.TextSecondary);
            }
            else
            {
                painter.DrawLabel(theme.Display, "NO RUN YET", new UiVec2(x, y), context.Px(30f), theme.TextSecondary, 0.04f);
            }

            float lineY = Bounds.Bottom - context.Px(30f);
            if (record is null)
            {
                painter.DrawText(theme.Mono, "No verified run on this board yet", new UiVec2(x, lineY), context.Px(12f), theme.TextMuted);
                return;
            }

            string wr = "World record " + MissionRow.Time((int)Math.Round(record.Seconds * 30));
            float wrEnd = painter.DrawText(theme.Mono, wr, new UiVec2(x, lineY), context.Px(12f), BoardLook.Gold).X;
            if (record.YourSeconds is { } yours)
            {
                string gap = "  ·  you are " + SplitsBar.Delta((int)Math.Round((yours - record.Seconds) * 30)) + " behind";
                painter.DrawText(theme.Mono, gap, new UiVec2(wrEnd, lineY), context.Px(12f), theme.TextSecondary);
            }
        }
    }
}
