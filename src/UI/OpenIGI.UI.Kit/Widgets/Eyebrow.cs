using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A small monospaced, widely tracked caption over a block: ACTIVE PROFILE, PROFILE SAVES.</summary>
public class Eyebrow : UiElement
{
    private string _text;

    /// <summary>Initializes a new instance of the <see cref="Eyebrow"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="text">The caption, shown in capitals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public Eyebrow(UiTheme theme, string text)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        _text = (text ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the caption.</summary>
    public string Text
    {
        get => _text;
        set => _text = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the caption's size, in design units.</summary>
    public float TextSize { get; set; } = 12f;

    /// <summary>Gets or sets the caption's colour, or <see langword="null"/> for the accent.</summary>
    public Bgra32? Color { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        Theme.Large
            ? new(UiPainter.MeasureLabel(Theme.Heading, _text, context.Px(LargeSize), 0.14f), context.Px(LargeSize * 1.2f))
            : new(UiPainter.MeasureLabel(Theme.Mono, _text, context.Px(TextSize), 0.3f), Theme.Mono.LineHeight(context.Px(TextSize)));

    // The large look sets it in the label face, two units up from the mono it was.
    private float LargeSize => TextSize + 2f;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (Theme.Large)
        {
            float px = painter.Context.Px(LargeSize);
            painter.DrawLabel(Theme.Heading, _text, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Heading, px, Bounds)), px, Color ?? Theme.TextSecondary, 0.14f);
            return;
        }

        KitDraw.Eyebrow(painter, Theme, _text, new UiVec2(Bounds.X, UiRowStyle.CenterY(Theme.Mono, painter.Context.Px(TextSize), Bounds)), TextSize, Color);
    }
}

/// <summary>One fact in a row of them: a monospaced caption over a condensed value — DIFFICULTY / HARD.</summary>
public class Stat : UiElement
{
    private readonly string _caption;
    private string _value = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="Stat"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="caption">The caption, shown in capitals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public Stat(UiTheme theme, string caption)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        _caption = (caption ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the value, shown in capitals.</summary>
    public string Value
    {
        get => _value;
        set => _value = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the value's colour, or <see langword="null"/> for the theme's text colour.</summary>
    public Bgra32? ValueColor { get; set; }

    /// <summary>Gets or sets the value's size on the large look, in design units.</summary>
    public float ValueSize { get; set; } = 24f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        if (Theme.Large)
        {
            float wide = MathF.Max(
                UiPainter.MeasureLabel(Theme.Heading, _caption, context.Px(13f), 0.14f),
                UiPainter.MeasureLabel(Theme.Heading, _value, context.Px(ValueSize), 0.1f));
            return new UiVec2(wide, context.Px((13f * 1.2f) + 6f + ValueSize));
        }

        float width = MathF.Max(
            UiPainter.MeasureLabel(Theme.Mono, _caption, context.Px(11f), 0.24f),
            UiPainter.MeasureLabel(Theme.Heading, _value, context.Px(24f), 0.1f));
        return new UiVec2(width, Theme.Mono.LineHeight(context.Px(11f)) + context.Px(6f + 24f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        if (Theme.Large)
        {
            float small = context.Px(13f);
            var line = new UiRect(Bounds.X, Bounds.Y, Bounds.Width, small * 1.2f);
            painter.DrawLabel(Theme.Heading, _caption, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Heading, small, line)), small, Theme.TextSecondary, 0.14f);
            float large = context.Px(ValueSize);
            var value = new UiRect(Bounds.X, line.Bottom + context.Px(6f), Bounds.Width, large);
            painter.DrawLabel(Theme.Heading, _value, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Heading, large, value)), large, ValueColor ?? Theme.Text, 0.1f);
            return;
        }

        painter.DrawLabel(Theme.Mono, _caption, Bounds.TopLeft, context.Px(11f), Theme.TextMuted, 0.24f);
        float size = context.Px(24f);
        var box = new UiRect(Bounds.X, Bounds.Y + Theme.Mono.LineHeight(context.Px(11f)) + context.Px(6f), Bounds.Width, size);
        painter.DrawLabel(Theme.Heading, _value, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Heading, size, box)), size, ValueColor ?? Theme.Text, 0.1f);
    }
}
