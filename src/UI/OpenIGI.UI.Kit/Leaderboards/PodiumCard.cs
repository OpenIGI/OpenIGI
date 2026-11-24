using OpenIGI.Core.Graphics;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Leaderboards;

/// <summary>
/// One of a board's top three, as a card on its podium: the place in its medal's colour, the player's picture large, their
/// name, the metric ranked by as the card's big figure, and under it their kills / deaths / assists and the share they won.
/// The first stands taller than the two beside it. Pressed, it opens their record.
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

    /// <summary>Gets or sets a value indicating whether the cards are the small ones of a narrow page.</summary>
    public bool Compact { get; set; }

    /// <summary>A place's medal colour: gold, silver, bronze.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="rank">The place.</param>
    /// <returns>The colour; the theme's quiet text past the third.</returns>
    public static Bgra32 MedalColour(UiTheme theme, int rank)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return rank switch
        {
            1 => theme.Accent,
            2 => Bgra32.FromRgb(196, 202, 208),
            3 => Bgra32.FromRgb(205, 133, 72),
            _ => theme.TextMuted,
        };
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float tall = Compact ? 164f : 236f;
        float height = _row.Rank == 1 ? tall : tall - (Compact ? 20f : 32f);
        return new UiVec2(available.X, context.Px(height));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
        UiRect bounds = Bounds;
        Bgra32 medal = MedalColour(Theme, _row.Rank);

        // The card: dark glass, lit from the top in its medal's colour, the colour along its top edge.
        Span<UiGradientStop> stops = [new(0f, medal.WithAlpha((byte)(46f * appear))), new(0.6f, UiPalette.InkAt(0.82f * appear)), new(1f, UiPalette.InkAt(0.9f * appear))];
        painter.FillGradient(bounds, stops, UiGradientDirection.Vertical);
        painter.FillRect(new UiRect(bounds.X, bounds.Y, bounds.Width, context.Px(3f)), UiPalette.Fade(medal, appear));
        painter.StrokeRect(bounds, context.Px(1f), UiPalette.Fade(IsYou ? Theme.Accent : UiPalette.White(0.1f), appear));
        if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.05f * hot * appear));
        }

        if (Visual.Focus > 0.001f)
        {
            painter.StrokeRect(bounds, context.Px(2f), UiPalette.Fade(Theme.AccentHover, Visual.Focus * appear));
        }

        float pad = context.Px(Compact ? 10f : 20f);
        float placeSize = context.Px(Compact ? 16f : 22f);
        painter.DrawText(Theme.Mono, $"#{_row.Rank}", new UiVec2(bounds.X + pad, bounds.Y + pad), placeSize, UiPalette.Fade(medal, appear));
        if (IsYou)
        {
            float youSize = context.Px(11f);
            float youWidth = UiPainter.MeasureLabel(Theme.Heading, "YOU", youSize, 0.12f) + context.Px(12f);
            BoardColumns.DrawTag(painter, Theme, bounds.Right - pad - youWidth, bounds.Y + pad + (placeSize / 2f), "YOU", UiPalette.Fade(Theme.Accent, appear));
        }

        // From the bottom up: the line, the figure, the name, and the picture over them, in the middle.
        float lineSize = context.Px(Compact ? 11f : 14f);
        float figureSize = context.Px(Compact ? 26f : 44f);
        float nameSize = context.Px(Compact ? 14f : 19f);
        float y = bounds.Bottom - pad - Theme.Mono.LineHeight(lineSize);
        string line = Compact
            ? BoardColumns.Percent(_row.WinRate) + " won"
            : $"{BoardColumns.KdaText(_row.Kills, _row.Deaths, _row.Assists)}  ·  {BoardColumns.Percent(_row.WinRate)} won";
        Centred(painter, Theme.Mono, Theme.Mono.Fit(line, lineSize, bounds.Width - (2f * pad)), y, lineSize, Theme.TextMuted, appear);

        y -= figureSize * 0.82f + context.Px(Compact ? 4f : 8f);
        string figure = _metric.Figure(_row);
        float labelSize = context.Px(Compact ? 10f : 12f);
        string label = _metric.Label.ToUpperInvariant();
        float figureWidth = Theme.Display.MeasureWidth(figure, figureSize);
        float labelWidth = UiPainter.MeasureLabel(Theme.Heading, label, labelSize, 0.14f);
        float gap = context.Px(8f);
        float start = bounds.Center.X - ((figureWidth + gap + labelWidth) / 2f);
        var figureBox = new UiRect(start, y, figureWidth, figureSize * 0.74f);
        painter.DrawText(Theme.Display, figure, new UiVec2(start, UiRowStyle.CapsCenterY(Theme.Display, figureSize, figureBox)), figureSize, UiPalette.Fade(_row.Rank == 1 ? Theme.Accent : Theme.TextBright, appear));
        painter.DrawLabel(Theme.Heading, label, new UiVec2(start + figureWidth + gap, figureBox.Bottom - (labelSize * 0.72f)), labelSize, UiPalette.Fade(Theme.TextMuted, appear), 0.14f);

        y -= Theme.BodyStrong.LineHeight(nameSize) + context.Px(Compact ? 2f : 6f);
        Centred(painter, Theme.BodyStrong, Theme.BodyStrong.Fit(_row.Name, nameSize, bounds.Width - (2f * pad)), y, nameSize, Theme.Text, appear);

        float room = y - (bounds.Y + pad + placeSize) - context.Px(Compact ? 4f : 12f);
        float side = MathF.Min(context.Px(Compact ? 48f : 76f), room);
        if (side > context.Px(16f))
        {
            var box = new UiRect(bounds.Center.X - (side / 2f), y - context.Px(Compact ? 4f : 12f) - side, side, side);
            KitDraw.PlayerMark(painter, Theme, box, _row.Name, _avatar(_row.Account?.Avatar), _row.Account is not null, appear);
            painter.StrokeRect(box, context.Px(2f), UiPalette.Fade(medal, appear));
        }
    }

    // A line set in the card's middle.
    private void Centred(UiPainter painter, Text.UiFont font, string text, float y, float size, Bgra32 colour, float appear) =>
        painter.DrawText(font, text, new UiVec2(Bounds.Center.X - (font.MeasureWidth(text, size) / 2f), y), size, UiPalette.Fade(colour, appear));
}
