using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// One of a board's top three, as a card on its podium: lit from the top in its medal's colour with its place's numeral large
/// behind it; the place, the Discord tag and the week's movement along the top; the player's mark and name, and the metric ranked
/// by as the card's big figure; their kills / deaths / assists, the share they won and the matches they played under. The first
/// stands taller than the two beside it. On a phone it is a short card in a column: the place, the mark, the name over the
/// figure. Pressed, it opens their record.
/// </summary>
public sealed class PodiumCard : Control
{
    private readonly BoardRow _row;
    private readonly LeaderboardMetric _metric;
    private readonly Func<string?, UiPicture?> _avatar;

    /// <summary>Initializes a new instance of the <see cref="PodiumCard"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="row">The player's row: one of the first three.</param>
    /// <param name="metric">The metric the board ranks by.</param>
    /// <param name="avatar">A picture by its address, once it has come down.</param>
    public PodiumCard(UiTheme theme, BoardRow row, LeaderboardMetric metric, Func<string?, UiPicture?> avatar)
        : base(theme)
    {
        _row = row ?? throw new ArgumentNullException(nameof(row));
        _metric = metric ?? throw new ArgumentNullException(nameof(metric));
        _avatar = avatar ?? throw new ArgumentNullException(nameof(avatar));
        Id = RankRow.KeyOf(row.RecordId);
        VerticalAlignment = VerticalAlignment.Bottom;
    }

    /// <summary>Gets the row shown.</summary>
    public BoardRow Row => _row;

    /// <summary>Gets or sets a value indicating whether the card is the player's own.</summary>
    public bool IsYou { get; set; }

    /// <summary>Gets or sets a value indicating whether the player is new on the board this week.</summary>
    public bool IsNew { get; set; }

    /// <summary>Gets or sets how much room the page has.</summary>
    public BoardSize Size { get; set; }

    private bool Compact => Size == BoardSize.Compact;

    /// <summary>The height a place's card stands, in design units.</summary>
    /// <param name="place">The place.</param>
    /// <param name="size">The page's room.</param>
    /// <returns>The height.</returns>
    public static float HeightOf(int place, BoardSize size) => size == BoardSize.Compact
        ? 62f
        : place switch
        {
            1 => 200f,
            2 => 184f,
            _ => 176f,
        };

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => new(available.X, context.Px(HeightOf(_row.Rank, Size)));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (Compact)
        {
            DrawCompact(painter);
            return;
        }

        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect bounds = Bounds;
        Bgra32 medal = BoardLook.Medal(Theme, _row.Rank);
        bool first = _row.Rank == 1;

        // The card: lit from the top in its medal's colour, the colour along its top, its place's numeral large behind.
        if (first)
        {
            BoardLook.Glow(painter, bounds, medal, 40f, 0.12f * appear);
        }

        Span<UiGradientStop> stops =
        [
            new(0f, medal.WithAlpha((byte)((first ? 51f : 33f) * appear))),
            new(0.72f, Bgra32.FromRgba(17, 18, 19, (byte)(235f * appear))),
            new(1f, Bgra32.FromRgba(17, 18, 19, (byte)(235f * appear))),
        ];
        painter.FillGradient(bounds, stops, UiGradientDirection.Vertical);
        painter.PushClip(bounds);
        string numeral = _row.Rank.ToString(CultureInfo.InvariantCulture);
        float numeralSize = context.Px(first ? 210f : _row.Rank == 2 ? 193f : 184f);
        float numeralWidth = Theme.Display.MeasureWidth(numeral, numeralSize);
        painter.DrawText(Theme.Display, numeral, new UiVec2(bounds.Right - context.Px(10f) - numeralWidth, bounds.Bottom - (numeralSize * 0.74f)), numeralSize, medal.WithAlpha((byte)(20f * appear)));
        painter.PopClip();
        painter.StrokeRect(bounds, context.Px(1f), medal.WithAlpha((byte)(89f * appear)));
        painter.FillRect(new UiRect(bounds.X, bounds.Y, bounds.Width, context.Px(3f)), UiPalette.Fade(medal, appear));
        BoardLook.Lit(painter, Theme, bounds, Visual.Focus, IsPointerOver, appear);

        float padX = context.Px(22f);
        float padY = context.Px(18f);
        float left = bounds.X + padX;
        float right = bounds.Right - padX;

        // The top: the place, its ordinal, the Discord tag and the week's movement.
        float topMid = bounds.Y + padY + context.Px(12f);
        float placeSize = context.Px(22f);
        float x = painter.DrawLabel(Theme.Display, $"#{_row.Rank}", new UiVec2(left, UiRowStyle.CapsCenterY(Theme.Display, placeSize, Band(topMid, placeSize))), placeSize, UiPalette.Fade(medal, appear), 0.04f).X;
        x += context.Px(10f);
        float ordinalSize = context.Px(10f);
        x = painter.DrawLabel(Theme.Mono, Ordinal(_row.Rank), new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Mono, ordinalSize, Band(topMid, ordinalSize))), ordinalSize, UiPalette.Fade(medal, appear), BoardLook.CaptionTracking).X;
        if (_row.Account is not null)
        {
            x = BoardLook.Discord(painter, Theme, x + context.Px(10f), topMid, appear);
        }

        if (IsYou)
        {
            BoardLook.You(painter, Theme, x + context.Px(8f), topMid, appear);
        }

        float moveWidth = MovementWidth(context);
        BoardLook.Movement(painter, Theme, right - moveWidth, topMid, _row.Climbed, IsNew, 13f, appear);

        // The middle: the mark and the name; the figure at the right with the metric under it.
        float side = context.Px(first ? 68f : 56f);
        float middle = bounds.Center.Y + context.Px(first ? 2f : 0f);
        var mark = new UiRect(left, middle - (side / 2f), side, side);
        BoardLook.Mark(painter, Theme, mark, _row.Name, _avatar(_row.Account?.Avatar), medal, 2f, appear);

        float figureSize = context.Px(first ? 44f : 36f);
        string figure = _metric.Figure(_row);
        float figureWidth = Theme.Mono.MeasureWidth(figure, figureSize);
        float labelSize = context.Px(10f);
        float blockHeight = (figureSize * 0.9f) + context.Px(4f) + labelSize;
        float figureTop = middle - (blockHeight / 2f) - (figureSize * 0.12f);
        painter.DrawText(Theme.Mono, figure, new UiVec2(right - figureWidth, figureTop), figureSize, UiPalette.Fade(first ? BoardLook.Gold : Theme.TextBright, appear));
        string label = _metric.Label.ToUpperInvariant();
        painter.DrawLabel(Theme.Mono, label, new UiVec2(right - BoardLook.CaptionWidth(context, Theme, label, 10f), middle + (blockHeight / 2f) - labelSize), labelSize, UiPalette.Fade(Theme.TextSecondary, appear), BoardLook.CaptionTracking);

        float nameSize = context.Px(first ? 30f : 26f);
        float nameLeft = mark.Right + context.Px(16f);
        float nameRoom = MathF.Max(0f, right - figureWidth - context.Px(16f) - nameLeft);
        string name = Theme.Display.Fit(_row.Name.ToUpperInvariant(), nameSize, nameRoom);
        painter.DrawLabel(Theme.Display, name, new UiVec2(nameLeft, UiRowStyle.CapsCenterY(Theme.Display, nameSize, Band(middle, nameSize))), nameSize, UiPalette.Fade(Theme.TextBright, appear), 0.02f);

        // The bottom: kills / deaths / assists, the share won and the matches.
        float lineSize = context.Px(13f);
        float lineY = bounds.Bottom - padY - Theme.Mono.LineHeight(lineSize);
        float after = BoardColumns.DrawKda(painter, Theme, new UiVec2(left, lineY), lineSize, _row.Kills, _row.Deaths, _row.Assists, appear, Theme.TextSecondary);
        string tail = string.Create(CultureInfo.InvariantCulture, $"  ·  {BoardColumns.Percent(_row.WinRate)} won  ·  {_row.Matches:N0} matches");
        painter.DrawText(Theme.Mono, Theme.Mono.Fit(tail, lineSize, MathF.Max(0f, right - after)), new UiVec2(after, lineY), lineSize, UiPalette.Fade(Theme.TextSecondary, appear));
    }

    // A phone's card: the place large, the mark, the name over the figure.
    private void DrawCompact(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        UiRect bounds = Bounds;
        Bgra32 medal = BoardLook.Medal(Theme, _row.Rank);
        Span<UiGradientStop> stops = [new(0f, medal.WithAlpha((byte)(40f * appear))), new(1f, Bgra32.FromRgba(17, 18, 19, (byte)(235f * appear)))];
        painter.FillGradient(bounds, stops, UiGradientDirection.Horizontal);
        painter.StrokeRect(bounds, context.Px(1f), medal.WithAlpha((byte)(89f * appear)));
        painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height), UiPalette.Fade(medal, appear));
        BoardLook.Lit(painter, Theme, bounds, Visual.Focus, IsPointerOver, appear);

        float mid = bounds.Center.Y;
        float placeSize = context.Px(22f);
        float x = bounds.X + context.Px(14f);
        painter.DrawLabel(Theme.Display, _row.Rank.ToString(CultureInfo.InvariantCulture), new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Display, placeSize, bounds)), placeSize, UiPalette.Fade(medal, appear), 0f);
        x += context.Px(28f);
        float side = context.Px(32f);
        var mark = new UiRect(x, mid - (side / 2f), side, side);
        BoardLook.Mark(painter, Theme, mark, _row.Name, _avatar(_row.Account?.Avatar), medal, 1.5f, appear);
        float left = mark.Right + context.Px(12f);
        float room = MathF.Max(0f, bounds.Right - context.Px(10f) - left);
        float nameSize = context.Px(15f);
        float figureSize = context.Px(15f);
        float top = mid - ((Theme.BodyStrong.LineHeight(nameSize) + Theme.Mono.LineHeight(figureSize)) / 2f);
        painter.DrawText(Theme.BodyStrong, Theme.BodyStrong.Fit(_row.Name, nameSize, room), new UiVec2(left, top), nameSize, UiPalette.Fade(Theme.TextBright, appear));
        painter.DrawText(Theme.Mono, _metric.Figure(_row), new UiVec2(left, top + Theme.BodyStrong.LineHeight(nameSize)), figureSize, UiPalette.Fade(_row.Rank == 1 ? BoardLook.Gold : Theme.Accent, appear));
    }

    private float MovementWidth(LayoutContext context) =>
        IsNew
            ? BoardLook.TagWidth(context, Theme, "New", 13f * 0.75f)
            : _row.Climbed is { } moved
                ? (moved == 0 ? 0f : context.Px(13f) * 0.62f) + context.Px(3f) + Theme.Mono.MeasureWidth(moved == 0 ? "—" : Math.Abs(moved).ToString("N0", CultureInfo.InvariantCulture), context.Px(13f))
                : 0f;

    private static UiRect Band(float middle, float size) => new(0f, middle - size, 0f, 2f * size);

    private static string Ordinal(int place) => place switch
    {
        1 => "1ST",
        2 => "2ND",
        3 => "3RD",
        _ => string.Create(CultureInfo.InvariantCulture, $"{place}TH"),
    };
}
