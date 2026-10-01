using OpenIGI.Core.Graphics;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A fact on glass: a monospaced key over a condensed value — VERSION 1.4.2, ON DISK 1.3 GB.</summary>
public class Chip : UiElement
{
    private string _key;
    private string _value;

    /// <summary>Initializes a new instance of the <see cref="Chip"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key, shown in capitals.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public Chip(UiTheme theme, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        _key = (key ?? string.Empty).ToUpperInvariant();
        _value = value ?? string.Empty;
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the key.</summary>
    public string Key
    {
        get => _key;
        set => _key = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets or sets the value.</summary>
    public string Value
    {
        get => _value;
        set => _value = value ?? string.Empty;
    }

    /// <summary>Gets or sets the value's colour, or <see langword="null"/> for the theme's text colour.</summary>
    public Bgra32? ValueColor { get; set; }

    /// <summary>Gets or sets the key's size in design units.</summary>
    public float KeySize { get; set; } = 13f;

    /// <summary>Gets or sets the value's size in design units.</summary>
    public float ValueSize { get; set; } = 19f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float width = MathF.Max(
            UiPainter.MeasureLabel(Theme.Mono, _key, context.Px(KeySize), 0.24f),
            UiPainter.MeasureLabel(Theme.Heading, _value, context.Px(ValueSize), 0.08f));
        float height = Theme.Mono.LineHeight(context.Px(KeySize)) + context.Px(5f) + context.Px(ValueSize);
        return new UiVec2(width + context.Px(32f), height + context.Px(20f));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        KitDraw.Glass(painter, Theme, Bounds, UiPalette.Ink.WithAlpha(140), 14f);
        var at = new UiVec2(Bounds.X + context.Px(16f), Bounds.Y + context.Px(10f));
        painter.DrawLabel(Theme.Mono, _key, at, context.Px(KeySize), Theme.TextMuted, 0.24f);
        float valueTop = at.Y + Theme.Mono.LineHeight(context.Px(KeySize)) + context.Px(5f);
        float size = context.Px(ValueSize);
        var box = new UiRect(at.X, valueTop, Bounds.Width, size);
        painter.DrawLabel(Theme.Heading, _value, new UiVec2(at.X, UiRowStyle.CapsCenterY(Theme.Heading, size, box)), size, ValueColor ?? Theme.Text, 0.08f);
    }
}
