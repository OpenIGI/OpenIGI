using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>Where a board row's columns stand across it, in pixels: each column's left edge and its width.</summary>
/// <param name="Rank">The place's left.</param>
/// <param name="Week">The week's movement's left.</param>
/// <param name="Player">The player's mark's left.</param>
/// <param name="PlayerWidth">The player's column's width.</param>
/// <param name="Metric">The metric's column's left.</param>
/// <param name="MetricWidth">The metric's column's width; its figure stands at its right.</param>
/// <param name="Kda">The kills / deaths / assists column's left.</param>
/// <param name="KdaWidth">Its width; the figures stand at its right.</param>
/// <param name="WinRate">The win rate's left.</param>
/// <param name="MatchesRight">The matches' right edge.</param>
public readonly record struct BoardColumnPlaces(float Rank, float Week, float Player, float PlayerWidth, float Metric, float MetricWidth, float Kda, float KdaWidth, float WinRate, float MatchesRight);

/// <summary>
/// The columns of a board's rows, shared by the rows and their headings so each figure stands under its heading: the place, the
/// week's movement, the player, the metric ranked by (with a bar of its share of the leader's), their kills / deaths / assists, a
/// bar of the matches they won and how many they played. On a phone: the place, the movement, the player, the share won and
/// the metric.
/// </summary>
public static class BoardColumns
{
    /// <summary>The padding either side of a row, in design units.</summary>
    public const float Pad = 16f;

    /// <summary>The gap between columns, in design units.</summary>
    public const float Gap = 16f;

    /// <summary>The place's column, in design units.</summary>
    public const float RankWidth = 56f;

    /// <summary>The week's movement's column, in design units.</summary>
    public const float WeekWidth = 64f;

    /// <summary>The metric's column, in design units.</summary>
    public const float MetricWidth = 190f;

    /// <summary>The kills / deaths / assists column, in design units.</summary>
    public const float KdaWidth = 170f;

    /// <summary>The win rate's column, in design units.</summary>
    public const float WinRateWidth = 150f;

    /// <summary>The matches' column, in design units.</summary>
    public const float MatchesWidth = 80f;

    /// <summary>The width of a share bar under a row's metric, in design units.</summary>
    public const float MetricBarWidth = 120f;

    /// <summary>The width of a row's win-rate bar, in design units.</summary>
    public const float WinBarWidth = 64f;

    /// <summary>Lays a row's columns across it.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="bounds">The row.</param>
    /// <returns>Where each column stands.</returns>
    public static BoardColumnPlaces Lay(LayoutContext context, UiRect bounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        float gap = context.Px(Gap);
        float right = bounds.Right - context.Px(Pad);
        float matchesRight = right;
        float winRate = matchesRight - context.Px(MatchesWidth) - gap - context.Px(WinRateWidth);
        float kda = winRate - gap - context.Px(KdaWidth);
        float metric = kda - gap - context.Px(MetricWidth);
        float rank = bounds.X + context.Px(Pad);
        float week = rank + context.Px(RankWidth) + gap;
        float player = week + context.Px(WeekWidth) + gap;
        return new BoardColumnPlaces(rank, week, player, MathF.Max(0f, metric - gap - player), metric, context.Px(MetricWidth), kda, context.Px(KdaWidth), winRate, matchesRight);
    }

    /// <summary>A board's headings, in the columns its rows lay.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="metric">The metric ranked by, which names its column.</param>
    /// <param name="size">How much room the page has.</param>
    /// <param name="weekly">Whether the board keeps the week's movement, which heads its column.</param>
    /// <returns>The headings.</returns>
    public static UiElement Header(UiTheme theme, string metric, BoardSize size, bool weekly) => new Heading(theme, metric, size, weekly);

    /// <summary>Draws kills / deaths / assists, the slashes between them quieter.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="topLeft">Where, in pixels.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="kills">The kills.</param>
    /// <param name="deaths">The deaths.</param>
    /// <param name="assists">The assists.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <param name="figureInk">The figures' colour; the theme's text when null.</param>
    /// <returns>Where it ends, across.</returns>
    public static float DrawKda(UiPainter painter, UiTheme theme, UiVec2 topLeft, float size, int kills, int deaths, int assists, float appear = 1f, Bgra32? figureInk = null)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float x = topLeft.X;
        Bgra32 figure = UiPalette.Fade(figureInk ?? theme.Text, appear);
        Bgra32 slash = UiPalette.Fade(theme.TextMuted, appear);
        string[] parts = [LeaderboardMetric.Count(kills), " / ", LeaderboardMetric.Count(deaths), " / ", LeaderboardMetric.Count(assists)];
        for (int i = 0; i < parts.Length; i++)
        {
            x = painter.DrawText(theme.Mono, parts[i], new UiVec2(x, topLeft.Y), size, i % 2 == 0 ? figure : slash).X;
        }

        return x;
    }

    /// <summary>Measures <see cref="DrawKda"/>.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="size">The size, in pixels.</param>
    /// <param name="kills">The kills.</param>
    /// <param name="deaths">The deaths.</param>
    /// <param name="assists">The assists.</param>
    /// <returns>Its width, in pixels.</returns>
    public static float MeasureKda(UiTheme theme, float size, int kills, int deaths, int assists)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.Mono.MeasureWidth(KdaText(kills, deaths, assists), size);
    }

    /// <summary>Kills / deaths / assists as text.</summary>
    /// <param name="kills">The kills.</param>
    /// <param name="deaths">The deaths.</param>
    /// <param name="assists">The assists.</param>
    /// <returns>The text.</returns>
    public static string KdaText(int kills, int deaths, int assists) =>
        $"{LeaderboardMetric.Count(kills)} / {LeaderboardMetric.Count(deaths)} / {LeaderboardMetric.Count(assists)}";

    /// <summary>A share as a whole percentage.</summary>
    /// <param name="share">The share, nought to one.</param>
    /// <returns>The text.</returns>
    public static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(share * 100):0}%");

    /// <summary>A row's figure in a metric, as a share of the leader's: how far its bar runs.</summary>
    /// <param name="metric">The metric.</param>
    /// <param name="row">The row.</param>
    /// <param name="leader">The leader's row.</param>
    /// <returns>The share, nought to one.</returns>
    public static double ShareOf(LeaderboardMetric metric, BoardRow row, BoardRow? leader)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(row);
        if (leader is null)
        {
            return 0d;
        }

        double Value(BoardRow r) => metric.Id switch
        {
            "kd" => r.Kd,
            "kills" => r.Kills,
            "assists" => r.Assists,
            "wins" => r.Wins,
            "matches" => r.Matches,
            _ => r.Score,
        };

        double top = Value(leader);
        return top > 0d ? Math.Clamp(Value(row) / top, 0d, 1d) : 0d;
    }

    // The headings, in the columns the rows lay.
    private sealed class Heading(UiTheme theme, string metric, BoardSize size, bool weekly) : UiElement
    {
        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(28f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float px = context.Px(11f);
            float y = UiRowStyle.CapsCenterY(theme.Mono, px, Bounds);
            Bgra32 ink = theme.TextMuted;
            void Left(string text, float x) => BoardLook.Caption(painter, theme, text, new UiVec2(x, y), 11f, ink);
            void Right(string text, float right) => Left(text, right - BoardLook.CaptionWidth(context, theme, text, 11f));

            if (size == BoardSize.Compact)
            {
                CompactColumns at = CompactColumns.Lay(context, Bounds);
                Left("#", at.Rank);
                Left("Player", at.Player);
                Right("Win", at.WinRight);
                Right(metric, Bounds.Right - context.Px(Pad));
            }
            else
            {
                BoardColumnPlaces at = Lay(context, Bounds);
                Left("#", at.Rank);
                if (weekly)
                {
                    Left("Week", at.Week);
                }

                Left("Player", at.Player);
                Right(metric, at.Metric + at.MetricWidth);
                Right("K / D / A", at.Kda + at.KdaWidth);
                Left("Win rate", at.WinRate);
                Right("Matches", at.MatchesRight);
            }

            painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - context.Px(1f), Bounds.Width, context.Px(1f)), UiPalette.White(0.22f));
        }
    }
}

/// <summary>Where a phone's board row's columns stand: the place, the movement, the player, the share won, the metric.</summary>
/// <param name="Rank">The place's left.</param>
/// <param name="Week">The movement's left.</param>
/// <param name="Player">The player's mark's left.</param>
/// <param name="WinRight">The share won's right edge.</param>
public readonly record struct CompactColumns(float Rank, float Week, float Player, float WinRight)
{
    /// <summary>Lays a phone's row.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="bounds">The row.</param>
    /// <returns>Where each column stands.</returns>
    public static CompactColumns Lay(LayoutContext context, UiRect bounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        float rank = bounds.X + context.Px(14f);
        float week = rank + context.Px(30f);
        float player = week + context.Px(40f);
        return new CompactColumns(rank, week, player, bounds.Right - context.Px(BoardColumns.Pad + 72f));
    }
}
