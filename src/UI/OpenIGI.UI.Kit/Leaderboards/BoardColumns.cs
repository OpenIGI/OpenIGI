using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>Where a ranked row's columns stand, in pixels.</summary>
/// <param name="Rank">The place's left edge.</param>
/// <param name="Avatar">The picture's left edge.</param>
/// <param name="Name">The name's left edge.</param>
/// <param name="NameWidth">The name's room.</param>
/// <param name="MetricRight">The metric's right edge, which its figure is set against.</param>
/// <param name="Kda">The kills, deaths and assists' left edge.</param>
/// <param name="WinRate">The win rate's left edge.</param>
/// <param name="MatchesRight">The matches' right edge.</param>
public readonly record struct BoardColumnPlaces(float Rank, float Avatar, float Name, float NameWidth, float MetricRight, float Kda, float WinRate, float MatchesRight);

/// <summary>
/// The leaderboard's columns, shared by its heading and its rows so the two line up: the place, the player's picture and name
/// taking what room is left, then the metric ranked by, the kills / deaths / assists, the win rate and the matches.
/// </summary>
public static class BoardColumns
{
    /// <summary>The room left of the place, in design units.</summary>
    public const float PadLeft = 18f;

    /// <summary>The room right of the matches, in design units.</summary>
    public const float PadRight = 20f;

    /// <summary>The room between two columns, in design units.</summary>
    public const float Gap = 18f;

    /// <summary>The place's column, in design units.</summary>
    public const float RankWidth = 44f;

    /// <summary>The picture's side, in design units.</summary>
    public const float AvatarSize = 36f;

    /// <summary>The metric's column, in design units.</summary>
    public const float MetricWidth = 110f;

    /// <summary>The kills / deaths / assists' column, in design units.</summary>
    public const float KdaWidth = 150f;

    /// <summary>The win rate's column, in design units.</summary>
    public const float WinRateWidth = 150f;

    /// <summary>The matches' column, in design units.</summary>
    public const float MatchesWidth = 84f;

    /// <summary>The win rate's bar, in design units.</summary>
    public const float BarWidth = 84f;

    /// <summary>Where each column stands across a row.</summary>
    /// <param name="context">The layout context.</param>
    /// <param name="bounds">The row.</param>
    /// <returns>The columns' places, in pixels.</returns>
    public static BoardColumnPlaces Lay(LayoutContext context, UiRect bounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        float gap = context.Px(Gap);
        float matchesRight = bounds.Right - context.Px(PadRight);
        float winRate = matchesRight - context.Px(MatchesWidth) - gap - context.Px(WinRateWidth);
        float kda = winRate - gap - context.Px(KdaWidth);
        float metricRight = kda - gap;
        float rank = bounds.X + context.Px(PadLeft);
        float avatar = rank + context.Px(RankWidth);
        float name = avatar + context.Px(AvatarSize) + context.Px(14f);
        float nameWidth = MathF.Max(0f, metricRight - context.Px(MetricWidth) - gap - name);
        return new BoardColumnPlaces(rank, avatar, name, nameWidth, metricRight, kda, winRate, matchesRight);
    }

    /// <summary>The headings over the ranked rows, lined up with them.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="metric">The metric's name, for its column.</param>
    /// <param name="compact">Whether the rows are the narrow ones, which head only the place, the player and the metric.</param>
    /// <returns>The heading.</returns>
    public static UiElement Header(UiTheme theme, string metric, bool compact) => new Heading(theme, metric, compact);

    /// <summary>Draws a row's kills / deaths / assists, the slashes quiet.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="topLeft">Where the text starts, in pixels.</param>
    /// <param name="size">The text's size, in pixels.</param>
    /// <param name="kills">The kills.</param>
    /// <param name="deaths">The deaths.</param>
    /// <param name="assists">The assists.</param>
    /// <param name="appear">How far it has faded in.</param>
    /// <returns>Where the text ended.</returns>
    public static float DrawKda(UiPainter painter, UiTheme theme, UiVec2 topLeft, float size, int kills, int deaths, int assists, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        float x = topLeft.X;
        Bgra32 figure = UiPalette.Fade(theme.Text, appear);
        Bgra32 slash = UiPalette.Fade(theme.TextDim, appear);
        string[] parts = [LeaderboardMetric.Count(kills), " / ", LeaderboardMetric.Count(deaths), " / ", LeaderboardMetric.Count(assists)];
        for (int i = 0; i < parts.Length; i++)
        {
            x = painter.DrawText(theme.Mono, parts[i], new UiVec2(x, topLeft.Y), size, i % 2 == 0 ? figure : slash).X;
        }

        return x;
    }

    /// <summary>Measures a row's kills / deaths / assists as <see cref="DrawKda"/> draws them.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="size">The text's size, in pixels.</param>
    /// <param name="kills">The kills.</param>
    /// <param name="deaths">The deaths.</param>
    /// <param name="assists">The assists.</param>
    /// <returns>The width, in pixels.</returns>
    public static float MeasureKda(UiTheme theme, float size, int kills, int deaths, int assists)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.Mono.MeasureWidth(KdaText(kills, deaths, assists), size);
    }

    /// <summary>A row's kills / deaths / assists as plain text.</summary>
    /// <param name="kills">The kills.</param>
    /// <param name="deaths">The deaths.</param>
    /// <param name="assists">The assists.</param>
    /// <returns>The text.</returns>
    public static string KdaText(int kills, int deaths, int assists) =>
        $"{LeaderboardMetric.Count(kills)} / {LeaderboardMetric.Count(deaths)} / {LeaderboardMetric.Count(assists)}";

    /// <summary>A share as said: <c>63%</c>.</summary>
    /// <param name="share">The share, nought to one.</param>
    /// <returns>The text.</returns>
    public static string Percent(double share) => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(share * 100):0}%");

    /// <summary>Draws a bar filled to a share: a quiet track and the accent over it.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="box">The track, in pixels.</param>
    /// <param name="share">The share, nought to one.</param>
    /// <param name="appear">How far it has faded in.</param>
    public static void DrawBar(UiPainter painter, UiTheme theme, UiRect box, double share, float appear = 1f)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        painter.FillRect(box, UiPalette.Fade(theme.Track, appear));
        float fill = box.Width * (float)Math.Clamp(share, 0d, 1d);
        if (fill > 0f)
        {
            painter.FillRect(new UiRect(box.X, box.Y, fill, box.Height), UiPalette.Fade(theme.Accent, appear));
        }
    }

    /// <summary>Draws a small outlined tag, such as a row's DISCORD or YOU.</summary>
    /// <param name="painter">The painter.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="left">Its left edge, in pixels.</param>
    /// <param name="middle">The line it sits on the middle of, in pixels.</param>
    /// <param name="text">What it says, in capitals.</param>
    /// <param name="colour">Its colour.</param>
    /// <returns>Its right edge.</returns>
    public static float DrawTag(UiPainter painter, UiTheme theme, float left, float middle, string text, Bgra32 colour)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);
        LayoutContext context = painter.Context;
        float size = context.Px(11f);
        float width = UiPainter.MeasureLabel(theme.Heading, text, size, 0.12f) + context.Px(12f);
        var box = new UiRect(left, middle - context.Px(10f), width, context.Px(20f));
        painter.StrokeRect(box, context.Px(1f), colour);
        painter.DrawLabel(theme.Heading, text, new UiVec2(box.X + context.Px(6f), UiRowStyle.CapsCenterY(theme.Heading, size, box)), size, colour, 0.12f);
        return box.Right;
    }

    // The headings, in the columns the rows lay.
    private sealed class Heading(UiTheme theme, string metric, bool compact) : UiElement
    {
        private const float TextSize = 13f;
        private const float Tracking = 0.14f;

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(36f));

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float size = context.Px(TextSize);
            float y = UiRowStyle.CapsCenterY(theme.Heading, size, Bounds);
            Bgra32 ink = theme.TextMuted;
            void Left(string text, float x) => painter.DrawLabel(theme.Heading, text, new UiVec2(x, y), size, ink, Tracking);
            void Right(string text, float right) => Left(text, right - UiPainter.MeasureLabel(theme.Heading, text, size, Tracking));

            string label = metric.ToUpperInvariant();
            if (compact)
            {
                Left("#", Bounds.X + context.Px(14f));
                Left("PLAYER", Bounds.X + context.Px(14f + 34f + 34f + 12f));
                Right(label, Bounds.Right - context.Px(16f));
            }
            else
            {
                BoardColumnPlaces at = Lay(context, Bounds);
                Left("#", at.Rank);
                Left("PLAYER", at.Avatar);
                Right(label, at.MetricRight);
                Left("K / D / A", at.Kda);
                Left("WIN RATE", at.WinRate);
                Right("MATCHES", at.MatchesRight);
            }

            painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - context.Px(1f), Bounds.Width, context.Px(1f)), UiPalette.White(0.1f));
        }
    }
}
