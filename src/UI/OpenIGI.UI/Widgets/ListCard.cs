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

    /// <summary>Gets or sets the mark drawn before the tag in the large look: a tick for done, a lock for shut.</summary>
    public ListCardMark Mark { get; set; }

    // The large look: the number, the name, and at the right a mark and a word for where the row stands. The current
    // row is washed with the accent, barred down its leading edge and outlined, so it is the current one in grey too.
    private void DrawLarge(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float appear = Visual.Appear;
        float selected = _slide.Value;
        float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f) * (1f - selected);
        UiRect bounds = Bounds;
        bool small = RowHeight < 60f;

        if (selected > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.AccentAt(0.13f * selected * appear));
            painter.StrokeRect(bounds, context.Px(1f), UiPalette.AccentAt(0.45f * selected * appear));
            painter.FillRect(new UiRect(bounds.X, bounds.Y, context.Px(4f), bounds.Height), UiPalette.Fade(Theme.Accent, selected * appear));
        }

        if (hot > 0.001f)
        {
            painter.FillRect(bounds, UiPalette.White(0.07f * hot * appear));
        }

        painter.FillRect(new UiRect(bounds.X, bounds.Bottom - context.Px(1f), bounds.Width, context.Px(1f)), UiPalette.White(0.07f * appear));

        float pad = context.Px(small ? 12f : 20f);
        float numberSize = context.Px(small ? 14f : 15f);
        float titleSize = context.Px(TitleSize);
        float x = bounds.X + pad;
        Bgra32 numberColor = !IsEnabled ? Theme.TextDisabled : Interpolate.Lerp(Theme.TextMuted, Theme.Accent, selected);
        painter.DrawText(Theme.Mono, Badge, new UiVec2(x, UiRowStyle.CenterY(Theme.Mono, numberSize, bounds)), numberSize, UiPalette.Fade(numberColor, appear));
        x += context.Px(small ? 22f + 10f : 32f + 16f);

        // The mark and the word, from the right; a short row keeps the mark alone.
        float right = bounds.Right - pad;
        Bgra32 tag = UiPalette.Fade(IsEnabled ? TagColor : Theme.TextDisabled, appear);
        if (Tag.Length > 0 && !small)
        {
            float tagSize = context.Px(14f);
            float tagWidth = UiPainter.MeasureLabel(Theme.Heading, Tag, tagSize, 0.08f);
            painter.DrawLabel(Theme.Heading, Tag, new UiVec2(right - tagWidth, UiRowStyle.CapsCenterY(Theme.Heading, tagSize, bounds)), tagSize, tag, 0.08f);
            right -= tagWidth + context.Px(6f);
        }

        if (Mark != ListCardMark.None)
        {
            float glyph = context.Px(small ? 16f : 14f);
            var at = new UiVec2(right - (glyph * 0.5f), bounds.Center.Y);
            float stroke = MathF.Max(1f, glyph * 0.13f);
            float h = glyph * 0.5f;
            switch (Mark)
            {
                case ListCardMark.Done:
                {
                    Bgra32 ink = UiPalette.Fade(IsEnabled ? Theme.Success : Theme.TextDisabled, appear);
                    painter.StrokeLine(new UiVec2(at.X - h, at.Y), new UiVec2(at.X - (h * 0.3f), at.Y + (h * 0.65f)), stroke, ink);
                    painter.StrokeLine(new UiVec2(at.X - (h * 0.3f), at.Y + (h * 0.65f)), new UiVec2(at.X + h, at.Y - (h * 0.65f)), stroke, ink);
                    break;
                }

                case ListCardMark.Open:
                    painter.FillTriangle(new UiVec2(at.X - (h * 0.6f), at.Y - (h * 0.75f)), new UiVec2(at.X + (h * 0.7f), at.Y), new UiVec2(at.X - (h * 0.6f), at.Y + (h * 0.75f)), tag);
                    break;
                default:
                    painter.StrokeRect(new UiRect(at.X - (h * 0.75f), at.Y - (h * 0.1f), h * 1.5f, h * 1.05f), stroke, tag);
                    painter.StrokeLine(new UiVec2(at.X - (h * 0.45f), at.Y - (h * 0.1f)), new UiVec2(at.X - (h * 0.45f), at.Y - (h * 0.7f)), stroke, tag);
                    painter.StrokeLine(new UiVec2(at.X + (h * 0.45f), at.Y - (h * 0.1f)), new UiVec2(at.X + (h * 0.45f), at.Y - (h * 0.7f)), stroke, tag);
                    painter.StrokeLine(new UiVec2(at.X - (h * 0.45f), at.Y - (h * 0.7f)), new UiVec2(at.X + (h * 0.45f), at.Y - (h * 0.7f)), stroke, tag);
                    break;
            }

            right -= glyph + context.Px(10f);
        }

        if (!ReferenceEquals(_titleSource, Title))
        {
            _titleSource = Title;
            _titleUpper = Title.ToUpperInvariant();
        }

        Bgra32 titleColor = !IsEnabled ? Theme.TextDisabled : Theme.Text;
        string title = Text.TextFit.ToWidth(Theme.Heading, _titleUpper, titleSize, right - x);
        painter.DrawLabel(Theme.Heading, title, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Heading, titleSize, bounds)), titleSize, UiPalette.Fade(titleColor, appear), 0.06f);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (Theme.Large)
        {
            DrawLarge(painter);
            return;
        }

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

/// <summary>The mark a <see cref="ListCard"/> draws beside its tag.</summary>
public enum ListCardMark
{
    /// <summary>None.</summary>
    None,

    /// <summary>A tick: done.</summary>
    Done,

    /// <summary>A pointer: open, the one to do next.</summary>
    Open,

    /// <summary>A padlock: not open yet.</summary>
    Locked,
}
