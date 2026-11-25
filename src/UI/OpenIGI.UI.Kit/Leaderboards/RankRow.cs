using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// One player's row on a leaderboard, in its columns (<see cref="BoardColumns"/>): their place, the week's movement, their mark
/// and name (DISCORD after it where it is a linked account's, YOU where it is the player's own), the metric ranked by over a bar
/// of its share of the leader's, their kills / deaths / assists, a bar of the matches they won and how many they played. The row
/// with the keys says RECORD and its key. Pressed, it opens their record. On a phone: the place, the movement, the player, the
/// share won and the metric.
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

    /// <summary>Gets or sets a value indicating whether the player is new on the board this week.</summary>
    public bool IsNew { get; set; }

    /// <summary>Gets or sets the leader's row, whose figure the row's bar is a share of.</summary>
    public BoardRow? Leader { get; set; }

    /// <summary>Gets or sets how much room the page has.</summary>
    public BoardSize Size { get; set; }

    /// <summary>The id a player's row goes by, for focus to find it again once the board is drawn anew.</summary>
    /// <param name="player">The player's record id.</param>
    /// <returns>The id.</returns>
    public static string KeyOf(string player) => "leaderboard-row-" + player;

    /// <summary>The height of a board's row, in design units.</summary>
    /// <param name="size">How much room the page has.</param>
    /// <returns>The height.</returns>
    public static float HeightOf(BoardSize size) => size == BoardSize.Compact ? 44f : 42f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(HeightOf(Size)));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect bounds = Bounds;
        if (IsYou)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.1f * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        }

        BoardLook.Lit(painter, Theme, bounds, Visual.Focus, IsPointerOver, appear);
        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.1f * appear));
        float mid = bounds.Center.Y;
        Bgra32 placeInk = _row.Rank > 10 ? Theme.TextMuted : Theme.TextBright;

        if (Size == BoardSize.Compact)
        {
            CompactColumns at = CompactColumns.Lay(context, bounds);
            DrawPlace(painter, at.Rank, 15f, placeInk, appear);
            BoardLook.Movement(painter, Theme, at.Week, mid, _row.Climbed, IsNew, 12f, appear);
            float winSize = context.Px(13f);
            float compactFigure = context.Px(15f);
            string figure = _metric.Figure(_row);
            float figureLeft = BoardLook.Right(painter, Theme.Mono, figure, bounds.Right - context.Px(BoardColumns.Pad), UiRowStyle.CenterY(Theme.Mono, compactFigure, bounds), compactFigure, UiPalette.Fade(Theme.TextBright, appear));
            BoardLook.Right(painter, Theme.Mono, BoardColumns.Percent(_row.WinRate), at.WinRight, UiRowStyle.CenterY(Theme.Mono, winSize, bounds), winSize, UiPalette.Fade(Theme.TextSecondary, appear));
            DrawWho(painter, at.Player, MathF.Min(at.WinRight - context.Px(56f), figureLeft) - context.Px(12f), 28f, 15f, compactTags: true, appear);
            return;
        }

        BoardColumnPlaces cols = BoardColumns.Lay(context, bounds);
        DrawPlace(painter, cols.Rank, 15f, placeInk, appear);
        BoardLook.Movement(painter, Theme, cols.Week, mid, _row.Climbed, IsNew, 12f, appear);

        // The player, and RECORD with its key on the row that has the keys.
        float playerRight = cols.Player + cols.PlayerWidth;
        if (Visual.Focus > 0.5f)
        {
            float keyWidth = BoardLook.KeyWidth(context, Theme, "Enter");
            float keyLeft = playerRight - keyWidth;
            BoardLook.Key(painter, Theme, "Enter", keyLeft, mid, appear: appear);
            float captionSize = context.Px(12f);
            float captionWidth = UiPainter.MeasureLabel(Theme.Heading, "RECORD", captionSize, 0.12f);
            painter.DrawLabel(Theme.Heading, "RECORD", new UiVec2(keyLeft - context.Px(6f) - captionWidth, UiRowStyle.CapsCenterY(Theme.Heading, captionSize, bounds)), captionSize, UiPalette.Fade(Theme.Accent, appear), 0.12f);
            playerRight = keyLeft - captionWidth - context.Px(20f);
        }

        DrawWho(painter, cols.Player, playerRight, 28f, 15f, compactTags: false, appear);

        // The metric over its bar, a share of the leader's.
        float figureSize = context.Px(15f);
        float metricRight = cols.Metric + cols.MetricWidth;
        float barWidth = context.Px(BoardColumns.MetricBarWidth);
        (float figureTop, float barTop) = FigureAndBar(context, Theme, figureSize, mid);
        BoardLook.Right(painter, Theme.Mono, _metric.Figure(_row), metricRight, figureTop, figureSize, UiPalette.Fade(Theme.TextBright, appear));
        BoardLook.Bar(painter, new UiRect(metricRight - barWidth, barTop, barWidth, context.Px(2f)), BoardColumns.ShareOf(_metric, _row, Leader), Theme.Accent.WithAlpha(204), appear);

        float small = context.Px(13f);
        float smallY = UiRowStyle.CenterY(Theme.Mono, small, bounds);
        float kdaWidth = BoardColumns.MeasureKda(Theme, small, _row.Kills, _row.Deaths, _row.Assists);
        BoardColumns.DrawKda(painter, Theme, new UiVec2(cols.Kda + cols.KdaWidth - kdaWidth, smallY), small, _row.Kills, _row.Deaths, _row.Assists, appear);

        var bar = new UiRect(cols.WinRate, mid - context.Px(2f), context.Px(BoardColumns.WinBarWidth), context.Px(4f));
        BoardLook.Bar(painter, bar, _row.WinRate, UiPalette.White(0.75f), appear);
        painter.DrawText(Theme.Mono, BoardColumns.Percent(_row.WinRate), new UiVec2(bar.Right + context.Px(10f), smallY), small, UiPalette.Fade(Theme.Text, appear));
        BoardLook.Right(painter, Theme.Mono, LeaderboardMetric.Count(_row.Matches), cols.MatchesRight, smallY, small, UiPalette.Fade(Theme.TextSecondary, appear));
    }

    /// <summary>Where a row's figure and the bar under it stand: one block, four units apart, about the row's middle.</summary>
    /// <param name="context">The layout.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="figureSize">The figure's size, in pixels.</param>
    /// <param name="middle">The row's middle.</param>
    /// <returns>The figure's top and the bar's.</returns>
    internal static (float FigureTop, float BarTop) FigureAndBar(LayoutContext context, UiTheme theme, float figureSize, float middle)
    {
        float caps = figureSize * 0.72f;
        float block = caps + context.Px(6f) + context.Px(2f);
        float capsTop = middle - (block / 2f);
        float figureTop = capsTop - ((theme.Mono.LineHeight(figureSize) - caps) / 2f);
        return (figureTop, capsTop + caps + context.Px(6f));
    }

    private void DrawPlace(UiPainter painter, float x, float size, Bgra32 ink, float appear)
    {
        float px = painter.Context.Px(size);
        painter.DrawText(Theme.Mono, LeaderboardMetric.Count(_row.Rank), new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, px, Bounds)), px, UiPalette.Fade(IsYou ? Theme.Accent : ink, appear));
    }

    // The mark, then the name with its tags after it, in the room up to the right edge given.
    private void DrawWho(UiPainter painter, float left, float right, float markSize, float nameSize, bool compactTags, float appear)
    {
        LayoutContext context = painter.Context;
        float mid = Bounds.Center.Y;
        float side = context.Px(markSize);
        Bgra32 edge = _row.Rank > 10 && !IsYou ? Theme.TextMuted : Theme.Accent;
        BoardLook.Mark(painter, Theme, new UiRect(left, mid - (side / 2f), side, side), _row.Name, _avatar(_row.Account?.Avatar), edge, 1f, appear);
        float x = left + side + context.Px(12f);
        float px = context.Px(nameSize);
        float tags = (IsYou ? BoardLook.TagWidth(context, Theme, "You") + context.Px(8f) : 0f)
            + (_row.Account is not null && !compactTags ? BoardLook.TagWidth(context, Theme, "Discord") + context.Px(8f) : 0f);
        string name = Theme.BodyStrong.Fit(_row.Name, px, MathF.Max(0f, right - x - tags));
        float end = painter.DrawText(Theme.BodyStrong, name, new UiVec2(x, UiRowStyle.CenterY(Theme.BodyStrong, px, Bounds)), px, UiPalette.Fade(Theme.TextBright, appear)).X;
        if (IsYou)
        {
            end = BoardLook.You(painter, Theme, end + context.Px(8f), mid, appear);
        }

        if (_row.Account is not null && !compactTags)
        {
            BoardLook.Discord(painter, Theme, end + context.Px(8f), mid, appear);
        }
    }
}

/// <summary>A ranked row's shape while the board is read: the place, mark, name and figures as quiet blocks.</summary>
/// <param name="index">Which of the rows it is, for its fade.</param>
/// <param name="size">How much room the page has.</param>
public sealed class RankRowSkeleton(int index, BoardSize size) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(RankRow.HeightOf(size)));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        Bgra32 block = UiPalette.White(0.07f * MathF.Max(0.3f, 1f - (index * 0.08f)));
        float mid = Bounds.Center.Y;
        void Block(float x, float width, float height) => painter.FillRect(new UiRect(x, mid - (context.Px(height) / 2f), context.Px(width), context.Px(height)), block);
        if (size == BoardSize.Compact)
        {
            CompactColumns at = CompactColumns.Lay(context, Bounds);
            Block(at.Rank, 14f, 12f);
            Block(at.Player, 28f, 28f);
            Block(at.Player + context.Px(40f), 110f, 12f);
            Block(Bounds.Right - context.Px(BoardColumns.Pad + 56f), 56f, 12f);
        }
        else
        {
            BoardColumnPlaces cols = BoardColumns.Lay(context, Bounds);
            Block(cols.Rank, 18f, 12f);
            Block(cols.Week, 22f, 10f);
            Block(cols.Player, 28f, 28f);
            Block(cols.Player + context.Px(40f), 150f + ((index * 37) % 60), 12f);
            Block(cols.Metric + cols.MetricWidth - context.Px(70f), 70f, 12f);
            Block(cols.Kda + cols.KdaWidth - context.Px(110f), 110f, 12f);
            Block(cols.WinRate, 100f, 10f);
            Block(cols.MatchesRight - context.Px(28f), 28f, 12f);
        }

        painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - context.Px(1f), Bounds.Width, context.Px(1f)), UiPalette.White(0.05f));
    }
}

/// <summary>A line across a board where a band of places ends: TOP 10 CUT-OFF between two rules in the accent.</summary>
/// <param name="theme">The theme.</param>
/// <param name="text">What it says.</param>
public sealed class BoardCutOff(UiTheme theme, string text) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(22f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float width = BoardLook.CaptionWidth(context, theme, text, 10f);
        float mid = Bounds.Center.Y;
        float gap = context.Px(12f);
        float pad = context.Px(BoardColumns.Pad);
        Bgra32 rule = UiPalette.AccentAt(0.35f);
        painter.FillRect(new UiRect(Bounds.X + pad, mid, (Bounds.Width / 2f) - pad - (width / 2f) - gap, context.Px(1f)), rule);
        float right = Bounds.Center.X + (width / 2f) + gap;
        painter.FillRect(new UiRect(right, mid, Bounds.Right - pad - right, context.Px(1f)), rule);
        BoardLook.Caption(painter, theme, text, new UiVec2(Bounds.Center.X - (width / 2f), UiRowStyle.CapsCenterY(theme.Mono, context.Px(10f), Bounds)), 10f, theme.Accent);
    }
}

/// <summary>A quiet line in a board's list: how many more there are, a gap in the places shown.</summary>
/// <param name="theme">The theme.</param>
/// <param name="text">What it says.</param>
public sealed class BoardNote(UiTheme theme, string text) : UiElement
{
    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(30f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float px = painter.Context.Px(11f);
        BoardLook.Centred(painter, theme.Mono, text, Bounds.Center.X, UiRowStyle.CenterY(theme.Mono, px, Bounds), px, theme.TextMuted);
    }
}

/// <summary>How a board's pinned row stands: the player placed, signed in but not ranked yet, or not signed in.</summary>
public enum YourPlaceState
{
    /// <summary>Placed: their place, their figures.</summary>
    Placed,

    /// <summary>Signed in, but not on the board yet.</summary>
    NotRanked,

    /// <summary>Not signed in: the board cannot know them.</summary>
    SignedOut,
}

/// <summary>
/// The player's own row, pinned under a board wherever they stand: washed in the accent from the left, their place large and the
/// week's movement; their mark, YOUR PLACE over their name and tags; how many are ranked and the share above them, and how far
/// they are from the next place up; then their figures in the board's columns. Not ranked or not signed in, it says so, and what
/// to do about it, with the way there.
/// </summary>
public sealed class YourPlaceRow : Control
{
    private readonly BoardRow? _row;
    private readonly LeaderboardMetric _metric;
    private readonly Func<string?, UiPicture?> _avatar;

    /// <summary>Initializes a new instance of the <see cref="YourPlaceRow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="row">The player's row, or null where they are not placed.</param>
    /// <param name="metric">The metric the board ranks by.</param>
    /// <param name="avatar">A picture by its address, once it has come down.</param>
    public YourPlaceRow(UiTheme theme, BoardRow? row, LeaderboardMetric metric, Func<string?, UiPicture?> avatar)
        : base(theme)
    {
        _row = row;
        _metric = metric ?? throw new ArgumentNullException(nameof(metric));
        _avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
        Id = "leaderboard-your-place";
    }

    /// <summary>Gets or sets how the row stands.</summary>
    public YourPlaceState State { get; set; }

    /// <summary>Gets or sets the player's name, shown where they are not placed.</summary>
    public string Name { get; set; } = "Operative";

    /// <summary>Gets or sets a value indicating whether the player's account is linked to Discord.</summary>
    public bool Linked { get; set; }

    /// <summary>Gets or sets how many the board ranks.</summary>
    public int Players { get; set; }

    /// <summary>Gets or sets the row just above the player's: the one to pass.</summary>
    public BoardRow? Ahead { get; set; }

    /// <summary>Gets or sets the leader's row, whose figure the row's bar is a share of.</summary>
    public BoardRow? Leader { get; set; }

    /// <summary>Gets or sets a value indicating whether the player is new on the board this week.</summary>
    public bool IsNew { get; set; }

    /// <summary>Gets or sets the line saying what to do, where the player is not placed.</summary>
    public string Advice { get; set; } = string.Empty;

    /// <summary>Gets or sets the action's caption, where the player is not placed (FIND A MATCH, GO TO PROFILE); empty for none.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets the action's key.</summary>
    public string ActionKey { get; set; } = string.Empty;

    /// <summary>Gets or sets how much room the page has.</summary>
    public BoardSize Size { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(Size == BoardSize.Compact ? 48f : 64f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect bounds = Bounds;
        bool compact = Size == BoardSize.Compact;
        bool placed = State == YourPlaceState.Placed && _row is not null;
        Span<UiGradientStop> stops = placed
            ? [new(0f, UiPalette.AccentAt(0.22f * appear)), new(0.55f, UiPalette.AccentAt(0.07f * appear)), new(1f, Bgra32.FromRgba(17, 18, 19, (byte)(235f * appear)))]
            : [new(0f, UiPalette.AccentAt(0.1f * appear)), new(1f, Bgra32.FromRgba(17, 18, 19, (byte)(235f * appear)))];
        painter.FillGradient(bounds, stops, UiGradientDirection.Horizontal);
        painter.StrokeRect(bounds, context.Px(1f), UiPalette.AccentAt((placed ? 0.45f : 0.3f) * appear));
        painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, appear));
        BoardLook.Lit(painter, Theme, bounds, Visual.Focus, IsPointerOver, appear);
        float mid = bounds.Center.Y;

        float rankLeft = bounds.X + context.Px(compact ? 14f : 16f);
        float rankSize = context.Px(compact ? 18f : 20f);
        string place = placed ? LeaderboardMetric.Count(_row!.Rank) : "—";
        painter.DrawText(Theme.Mono, place, new UiVec2(rankLeft, UiRowStyle.CenterY(Theme.Mono, rankSize, bounds)), rankSize, UiPalette.Fade(placed ? Theme.Accent : Theme.TextMuted, appear));
        float week = compact ? rankLeft + context.Px(46f) : BoardColumns.Lay(context, bounds).Week;
        if (placed)
        {
            BoardLook.Movement(painter, Theme, week, mid, _row!.Climbed, IsNew, 13f, appear);
        }

        // The mark and who: YOUR PLACE over the name and its tags.
        float player = compact ? week + context.Px(46f) : BoardColumns.Lay(context, bounds).Player;
        float side = context.Px(compact ? 32f : 42f);
        var mark = new UiRect(player, mid - (side / 2f), side, side);
        string name = _row?.Name ?? Name;
        if (State == YourPlaceState.SignedOut)
        {
            painter.StrokeRect(mark, context.Px(1.5f), UiPalette.AccentAt(0.6f * appear));
            BoardLook.Icon(painter, Theme, KitIcon.Person, mark.Center, side * 0.5f, UiPalette.Fade(Theme.Accent, appear));
        }
        else
        {
            BoardLook.Mark(painter, Theme, mark, name, _avatar(_row?.Account?.Avatar), Theme.Accent, 1.5f, appear);
        }

        float x = mark.Right + context.Px(12f);
        float eyebrowSize = context.Px(10f);
        float nameSize = context.Px(compact ? 15f : 17f);
        float top = mid - ((eyebrowSize + context.Px(5f) + Theme.BodyStrong.LineHeight(nameSize)) / 2f);
        string eyebrow = State switch
        {
            YourPlaceState.NotRanked => "Your place · not ranked yet",
            _ => "Your place",
        };
        BoardLook.Caption(painter, Theme, eyebrow, new UiVec2(x, top), 10f, UiPalette.Fade(Theme.Accent, appear));
        float nameTop = top + eyebrowSize + context.Px(5f);
        string shown = State == YourPlaceState.SignedOut ? "You're not on the board" : name;
        float end = painter.DrawText(Theme.BodyStrong, shown, new UiVec2(x, nameTop), nameSize, UiPalette.Fade(Theme.TextBright, appear)).X;
        float nameMid = nameTop + (Theme.BodyStrong.LineHeight(nameSize) / 2f);
        if (State != YourPlaceState.SignedOut)
        {
            end = BoardLook.You(painter, Theme, end + context.Px(8f), nameMid, appear);
            if (Linked && !compact)
            {
                end = BoardLook.Discord(painter, Theme, end + context.Px(8f), nameMid, appear);
            }
        }

        if (!placed)
        {
            DrawAdvice(painter, end, appear);
            return;
        }

        BoardRow own = _row!;
        float small = context.Px(compact ? 13f : 14f);
        float smallY = UiRowStyle.CenterY(Theme.Mono, small, bounds);
        if (compact)
        {
            float figureSize = context.Px(16f);
            float right = bounds.Right - context.Px(BoardColumns.Pad);
            float figureLeft = BoardLook.Right(painter, Theme.Mono, _metric.Figure(own), right, UiRowStyle.CenterY(Theme.Mono, figureSize, bounds), figureSize, UiPalette.Fade(Theme.Accent, appear));
            if (Players > 0)
            {
                BoardLook.Right(painter, Theme.Mono, string.Create(CultureInfo.InvariantCulture, $"of {Players:N0}"), figureLeft - context.Px(12f), smallY, context.Px(12f), UiPalette.Fade(Theme.TextSecondary, appear));
            }

            return;
        }

        BoardColumnPlaces cols = BoardColumns.Lay(context, bounds);

        // How many are ranked, the share above them, and the gap to the next place up.
        float standRight = cols.Metric - context.Px(BoardColumns.Gap);
        float line = context.Px(12f);
        float lineHeight = Theme.Mono.LineHeight(line);
        string Of() => Players > 0
            ? string.Create(CultureInfo.InvariantCulture, $"of {Players:N0}  ·  top {Math.Max(1, (int)Math.Ceiling(own.Rank * 100.0 / Players))}%")
            : string.Empty;
        string? pass = Ahead is { } ahead ? PassLine(own, ahead) : null;
        float standTop = pass is null ? mid - (lineHeight / 2f) : mid - lineHeight - context.Px(1f);
        BoardLook.Right(painter, Theme.Mono, Of(), standRight, standTop, line, UiPalette.Fade(Theme.TextSecondary, appear));
        if (pass is not null)
        {
            BoardLook.Right(painter, Theme.Mono, pass, standRight, mid + context.Px(2f), line, UiPalette.Fade(Theme.Success, appear));
        }

        float figurePx = context.Px(16f);
        float metricRight = cols.Metric + cols.MetricWidth;
        float barWidth = context.Px(BoardColumns.MetricBarWidth);
        (float figureTop, float barTop) = RankRow.FigureAndBar(context, Theme, figurePx, mid);
        BoardLook.Right(painter, Theme.Mono, _metric.Figure(own), metricRight, figureTop, figurePx, UiPalette.Fade(Theme.Accent, appear));
        BoardLook.Bar(painter, new UiRect(metricRight - barWidth, barTop, barWidth, context.Px(2f)), BoardColumns.ShareOf(_metric, own, Leader), Theme.Accent, appear);
        float kdaWidth = BoardColumns.MeasureKda(Theme, small, own.Kills, own.Deaths, own.Assists);
        BoardColumns.DrawKda(painter, Theme, new UiVec2(cols.Kda + cols.KdaWidth - kdaWidth, smallY), small, own.Kills, own.Deaths, own.Assists, appear);
        var bar = new UiRect(cols.WinRate, mid - context.Px(2f), context.Px(BoardColumns.WinBarWidth), context.Px(4f));
        BoardLook.Bar(painter, bar, own.WinRate, UiPalette.White(0.75f), appear);
        painter.DrawText(Theme.Mono, BoardColumns.Percent(own.WinRate), new UiVec2(bar.Right + context.Px(10f), smallY), small, UiPalette.Fade(Theme.Text, appear));
        BoardLook.Right(painter, Theme.Mono, LeaderboardMetric.Count(own.Matches), cols.MatchesRight, smallY, small, UiPalette.Fade(Theme.TextSecondary, appear));
    }

    // What the gap to the next place up comes to, in the metric ranked by: 40 pts to pass #211.
    private string? PassLine(BoardRow own, BoardRow ahead)
    {
        string unit = _metric.Id switch
        {
            "score" => "pts",
            "kills" => "kills",
            "assists" => "assists",
            "wins" => "wins",
            "matches" => "matches",
            _ => string.Empty,
        };
        string gap = _metric.Id == "kd"
            ? (Math.Max(0.01, ahead.Kd - own.Kd)).ToString("0.00", CultureInfo.InvariantCulture)
            : Math.Max(1, Value(ahead) - Value(own) + 1).ToString("N0", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{gap}{(unit.Length > 0 ? " " + unit : " K/D")} to pass #{ahead.Rank:N0}");

        int Value(BoardRow r) => _metric.Id switch
        {
            "kills" => r.Kills,
            "assists" => r.Assists,
            "wins" => r.Wins,
            "matches" => r.Matches,
            _ => r.Score,
        };
    }

    // Not placed: the advice in the middle, and the way there as a button at the right.
    private void DrawAdvice(UiPainter painter, float after, float appear)
    {
        LayoutContext context = painter.Context;
        UiRect bounds = Bounds;
        float mid = bounds.Center.Y;
        float right = bounds.Right - context.Px(BoardColumns.Pad);
        if (Action.Length > 0)
        {
            float captionSize = context.Px(13f);
            string caption = Action.ToUpperInvariant();
            float captionWidth = UiPainter.MeasureLabel(Theme.Heading, caption, captionSize, 0.14f);
            float keyWidth = ActionKey.Length > 0 ? BoardLook.KeyWidth(context, Theme, ActionKey) + context.Px(10f) : 0f;
            float arrow = context.Px(16f);
            float width = context.Px(16f) + captionWidth + context.Px(14f) + keyWidth + arrow + context.Px(14f);
            var button = new UiRect(right - width, mid - context.Px(18f), width, context.Px(36f));
            painter.FillRect(button, UiPalette.Fade(Visual.Focus > 0.5f || IsPointerOver ? Theme.AccentHover : Theme.Accent, appear));
            painter.DrawLabel(Theme.Heading, caption, new UiVec2(button.X + context.Px(16f), UiRowStyle.CapsCenterY(Theme.Heading, captionSize, button)), captionSize, UiPalette.Fade(BoardLook.OnAccent, appear), 0.14f);
            float x = button.X + context.Px(16f) + captionWidth + context.Px(14f);
            if (ActionKey.Length > 0)
            {
                x = BoardLook.Key(painter, Theme, ActionKey, x, mid, onAccent: true, appear) + context.Px(10f);
            }

            BoardLook.Icon(painter, Theme, KitIcon.ArrowRight, new UiVec2(x + (arrow / 2f), mid), arrow, UiPalette.Fade(BoardLook.OnAccent, appear));
            right = button.X - context.Px(18f);
        }

        if (Advice.Length > 0 && Size != BoardSize.Compact)
        {
            float px = context.Px(12f);
            float room = MathF.Max(0f, right - after - context.Px(24f));
            BoardLook.Right(painter, Theme.Mono, Theme.Mono.Fit(Advice, px, room), right, UiRowStyle.CenterY(Theme.Mono, px, bounds), px, UiPalette.Fade(Theme.TextSecondary, appear));
        }
    }
}
