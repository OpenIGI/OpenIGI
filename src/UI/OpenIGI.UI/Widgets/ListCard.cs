using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A focusable row built from a leading badge, a title, and a trailing tag — the shape a mission entry,
/// a save slot or a profile takes in a list.
/// </summary>
/// <remarks>
/// Where <see cref="ListView"/> is one control owning many rows of plain text, a card <em>is</em> one row:
/// it takes focus itself, so a list of them is navigated and hit-tested by the ordinary focus model, each
/// row can be styled differently — a locked mission reads muted, the current one keeps an accent — and the
/// list is an ordinary <see cref="Layout.StackPanel"/> inside a <see cref="ScrollView"/> rather than a
/// widget with its own scrolling. That is what lets the campaign list scroll, hover and click with no
/// special cases.
/// </remarks>
public class ListCard : Control
{
    private readonly Spring _slide = new(0f, smoothTime: 0.08f);
    private string? _titleSource;
    private string _titleUpper = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="ListCard"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="badge">The small leading marker — a mission number, a slot index.</param>
    /// <param name="title">The row's main text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public ListCard(UiTheme theme, string badge, string title)
        : base(theme)
    {
        Badge = badge ?? string.Empty;
        Title = title ?? string.Empty;
    }

    /// <summary>Gets or sets the small leading marker.</summary>
    public string Badge { get; set; }

    /// <summary>Gets or sets the row's main text.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the short trailing tag, or an empty string for none.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets or sets the trailing tag's colour.</summary>
    public Bgra32 TagColor { get; set; } = Bgra32.FromRgb(150, 162, 172);

    /// <summary>Gets or sets a value indicating whether the row is the current one in its list.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets or sets the row's height in design units.</summary>
    public float RowHeight { get; set; } = 84f;

    /// <summary>Gets or sets the size of the row's name, in design units.</summary>
    public float TitleSize { get; set; } = 28f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(RowHeight));

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _slide.Target = Selected ? 1f : 0f;
        _slide.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float selected = _slide.Value;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f) * (1f - selected);
        UiRect bounds = Bounds;

        // Current: an amber wash fading to the right and a bar down the leading edge. Under the pointer: a
        // faint white wash. A hairline under every row, so the list reads as ruled.
        if (selected > 0.001f)
        {
            Span<UiGradientStop> wash =
            [
                new(0f, UiPalette.AccentAt(0.16f * selected * appear)),
                new(1f, UiPalette.AccentAt(0.02f * selected * appear)),
            ];
            painter.FillGradient(bounds, wash, UiGradientDirection.Horizontal);
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(3f), bounds.Height), UiPalette.Fade(Theme.Accent, selected * appear));
        }

        if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.05f * hot * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.06f * appear));

        float numberSize = context.Px(14f);
        float titleSize = context.Px(TitleSize);
        float tagSize = context.Px(11f);
        float x = bounds.X + context.Px(28f);

        Bgra32 numberColor = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.TextDim, Theme.Accent, selected);
        Bgra32 titleColor = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Interpolate.Lerp(Theme.TextSecondary, Theme.TextBright, hot), Theme.TextBright, selected);

        painter.DrawText(Theme.Mono, Badge, new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, numberSize, bounds)), numberSize, UiPalette.Fade(numberColor, appear));
        x += context.Px(24f + 24f);

        if (!ReferenceEquals(_titleSource, Title))
        {
            _titleSource = Title;
            _titleUpper = Title.ToUpperInvariant();
        }

        painter.DrawLabel(Theme.Heading, _titleUpper, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, titleSize, bounds)), titleSize, UiPalette.Fade(titleColor, appear), 0.06f);

        if (Tag.Length > 0)
        {
            float right = bounds.Right - context.Px(24f);
            float tagWidth = UiPainter.MeasureLabel(Theme.Mono, Tag, tagSize, 0.18f);
            Bgra32 tag = UiPalette.Fade(IsEnabled ? TagColor : Theme.TextDisabled, appear);
            painter.DrawLabel(Theme.Mono, Tag, new UiVec2(right - tagWidth, UiRowStyle.CenterY(Theme.Mono, tagSize, bounds)), tagSize, tag, 0.18f);
            painter.FillDiamond(new UiVec2(right - tagWidth - context.Px(8f + 3f), bounds.Center.Y), context.Px(6f), tag);
        }
    }
}
