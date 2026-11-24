using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// One player's row on a leaderboard, in its columns (<see cref="BoardColumns"/>): their place, picture and name (DISCORD after
/// it where it is a linked account's, YOU where it is the player's own), the metric ranked by in the accent, their kills /
/// deaths / assists, a bar of the matches they won and how many they played. The player's own row is washed in the accent with
/// it down its edge. Pressed, it opens their record. Narrow, it is two deep: the name over the K / D / A and win rate.
/// </summary>
public sealed class RankRow : Control
{
    private readonly BoardRow _row;
    private readonly LeaderboardMetric _metric;
    private readonly Func<string?, UiPicture?> _avatar;

    /// <summary>Initializes a new instance of the <see cref="RankRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="row">The player's row.</param>
    /// <param name="metric">The metric the board ranks by.</param>
    /// <param name="avatar">A picture by its address, once it has come down.</param>
    public RankRow(UiTheme theme, BoardRow row, LeaderboardMetric metric, Func<string?, UiPicture?> avatar)
        : base(theme)
    {
        _row = row ?? throw new ArgumentNullException(nameof(row));
        _metric = metric ?? throw new ArgumentNullException(nameof(metric));
        _avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
        Id = KeyOf(row.RecordId);
    }

    /// <summary>Gets the row shown.</summary>
    public BoardRow Row => _row;

    /// <summary>Gets or sets a value indicating whether the row is the player's own.</summary>
    public bool IsYou { get; set; }

    /// <summary>Gets or sets a value indicating whether the row is drawn two deep, for a narrow page.</summary>
    public bool Compact { get; set; }

    /// <summary>The id a player's row goes by, for focus to find it again once the board is drawn anew.</summary>
    /// <param name="player">The player's record id.</param>
    /// <returns>The id.</returns>
    public static string KeyOf(string player) => "leaderboard-row-" + player;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Compact ? 58f : 60f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect bounds = Bounds;
        if (IsYou)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.12f * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }

        if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.06f * hot * appear));
        }

        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(bounds, context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.07f * appear));
        if (Compact)
        {
            DrawCompact(painter, appear);
            return;
        }

        BoardColumnPlaces at = BoardColumns.Lay(context, bounds);
        float mid = bounds.Center.Y;
        DrawRank(painter, at.Rank, appear);
        DrawWho(painter, at.Avatar, at.Name, at.NameWidth, mid, appear);

        float metricSize = context.Px(19f);
        string figure = _metric.Figure(_row);
        float figureWidth = Theme.Mono.MeasureWidth(figure, metricSize);
        painter.DrawText(Theme.Mono, figure, new UiVec2(at.MetricRight - figureWidth, UiRowStyle.CenterY(Theme.Mono, metricSize, bounds)), metricSize, UiPalette.Fade(Theme.Accent, appear));

        float small = context.Px(15f);
        float smallY = UiRowStyle.CenterY(Theme.Mono, small, bounds);
        BoardColumns.DrawKda(painter, Theme, new UiVec2(at.Kda, smallY), small, _row.Kills, _row.Deaths, _row.Assists, appear);

        var bar = new UiRect(at.WinRate, mid - context.Px(2f), context.Px(BoardColumns.BarWidth), context.Px(4f));
        BoardColumns.DrawBar(painter, Theme, bar, _row.WinRate, appear);
        painter.DrawText(Theme.Mono, BoardColumns.Percent(_row.WinRate), new UiVec2(bar.Right + context.Px(12f), smallY), small, UiPalette.Fade(Theme.TextSecondary, appear));

        string matches = LeaderboardMetric.Count(_row.Matches);
        painter.DrawText(Theme.Mono, matches, new UiVec2(at.MatchesRight - Theme.Mono.MeasureWidth(matches, small), smallY), small, UiPalette.Fade(Theme.TextSecondary, appear));
    }

    private void DrawRank(UiPainter painter, float x, float appear)
    {
        float size = painter.Context.Px(17f);
        string place = LeaderboardMetric.Count(_row.Rank);
        painter.DrawText(Theme.Mono, place, new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, size, Bounds)), size, UiPalette.Fade(IsYou ? Theme.Accent : Theme.TextSecondary, appear));
    }

    // The picture, then the name with its tags after it.
    private void DrawWho(UiPainter painter, float avatarX, float nameX, float room, float mid, float appear)
    {
        LayoutContext context = painter.Context;
        float side = context.Px(BoardColumns.AvatarSize);
        KitDraw.PlayerMark(painter, Theme, new UiRect(avatarX, mid - (side / 2f), side, side), _row.Name, _avatar(_row.Account?.Avatar), _row.Account is not null, appear);

        float nameSize = context.Px(17f);
        float tags = (_row.Account is not null ? context.Px(84f) : 0f) + (IsYou ? context.Px(50f) : 0f);
        string name = Theme.BodyStrong.Fit(_row.Name, nameSize, MathF.Max(0f, room - tags));
        float end = painter.DrawText(Theme.BodyStrong, name, new UiVec2(nameX, UiRowStyle.CenterY(Theme.BodyStrong, nameSize, Bounds)), nameSize, UiPalette.Fade(Theme.Text, appear)).X;
        if (IsYou)
        {
            end = BoardColumns.DrawTag(painter, Theme, end + context.Px(10f), mid, "YOU", UiPalette.Fade(Theme.Accent, appear));
        }

        if (_row.Account is not null)
        {
            BoardColumns.DrawTag(painter, Theme, end + context.Px(10f), mid, "DISCORD", UiPalette.Fade(Theme.TextMuted, appear));
        }
    }

    // Two deep: the place, the picture, the name over the K / D / A and win rate, and the metric against the right edge.
    private void DrawCompact(UiPainter painter, float appear)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        float mid = bounds.Center.Y;
        float x = bounds.X + context.Px(14f);
        DrawRank(painter, x, appear);
        float side = context.Px(34f);
        float avatarX = x + context.Px(34f);
        KitDraw.PlayerMark(painter, Theme, new UiRect(avatarX, mid - (side / 2f), side, side), _row.Name, _avatar(_row.Account?.Avatar), _row.Account is not null, appear);

        float metricSize = context.Px(17f);
        string figure = _metric.Figure(_row);
        float figureWidth = Theme.Mono.MeasureWidth(figure, metricSize);
        float right = bounds.Right - context.Px(16f);
        painter.DrawText(Theme.Mono, figure, new UiVec2(right - figureWidth, UiRowStyle.CenterY(Theme.Mono, metricSize, bounds)), metricSize, UiPalette.Fade(Theme.Accent, appear));

        float left = avatarX + side + context.Px(12f);
        float room = MathF.Max(0f, right - figureWidth - context.Px(12f) - left);
        float nameSize = context.Px(16f);
        float underSize = context.Px(13f);
        float gap = context.Px(3f);
        float top = mid - ((Theme.BodyStrong.LineHeight(nameSize) + gap + Theme.Mono.LineHeight(underSize)) / 2f);
        string name = Theme.BodyStrong.Fit(IsYou ? _row.Name + "  ·  YOU" : _row.Name, nameSize, room);
        painter.DrawText(Theme.BodyStrong, name, new UiVec2(left, top), nameSize, UiPalette.Fade(IsYou ? Theme.Accent : Theme.Text, appear));
        string under = $"{BoardColumns.KdaText(_row.Kills, _row.Deaths, _row.Assists)}  ·  {BoardColumns.Percent(_row.WinRate)} won";
        painter.DrawText(Theme.Mono, Theme.Mono.Fit(under, underSize, room), new UiVec2(left, top + Theme.BodyStrong.LineHeight(nameSize) + gap), underSize, UiPalette.Fade(Theme.TextMuted, appear));
    }
}

/// <summary>A ranked row's shape while the board is read: the place, picture, name and figures as quiet blocks.</summary>
/// <param name="index">Which of the rows it is, for its fade.</param>
/// <param name="compact">Whether the rows are the narrow ones.</param>
public sealed class RankRowSkeleton(int index, bool compact) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(compact ? 58f : 60f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        Bgra32 block = UiPalette.White(0.07f * MathF.Max(0.3f, 1f - (index * 0.1f)));
        float mid = Bounds.Center.Y;
        float x = Bounds.X + context.Px(BoardColumns.PadLeft);
        painter.FillRect(new UiRect(x, mid - context.Px(7f), context.Px(22f), context.Px(14f)), block);
        x += context.Px(BoardColumns.RankWidth);
        float side = context.Px(compact ? 34f : BoardColumns.AvatarSize);
        painter.FillRect(new UiRect(x, mid - (side / 2f), side, side), block);
        x += side + context.Px(14f);
        painter.FillRect(new UiRect(x, mid - context.Px(7f), context.Px(160f), context.Px(14f)), block);
        painter.FillRect(new UiRect(Bounds.Right - context.Px(BoardColumns.PadRight + 70f), mid - context.Px(7f), context.Px(70f), context.Px(14f)), block);
        painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - context.Px(1f), Bounds.Width, context.Px(1f)), UiPalette.White(0.05f));
    }
}
