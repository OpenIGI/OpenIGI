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
        new(UiPainter.MeasureLabel(Theme.Mono, _text, context.Px(TextSize), 0.3f), Theme.Mono.LineHeight(context.Px(TextSize)));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter) =>
        KitDraw.Eyebrow(painter, Theme, _text, new UiVec2(Bounds.X, UiRowStyle.CenterY(Theme.Mono, painter.Context.Px(TextSize), Bounds)), TextSize, Color);
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

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float width = MathF.Max(
            UiPainter.MeasureLabel(Theme.Mono, _caption, context.Px(11f), 0.24f),
            UiPainter.MeasureLabel(Theme.Heading, _value, context.Px(24f), 0.1f));
        return new UiVec2(width, Theme.Mono.LineHeight(context.Px(11f)) + context.Px(6f + 24f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        painter.DrawLabel(Theme.Mono, _caption, Bounds.TopLeft, context.Px(11f), Theme.TextMuted, 0.24f);
        float size = context.Px(24f);
        var box = new UiRect(Bounds.X, Bounds.Y + Theme.Mono.LineHeight(context.Px(11f)) + context.Px(6f), Bounds.Width, size);
        painter.DrawLabel(Theme.Heading, _value, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Heading, size, box)), size, ValueColor ?? Theme.Text, 0.1f);
    }
}
