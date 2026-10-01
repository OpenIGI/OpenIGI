using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A key cap standing on its own, such as the Q and E either side of the header's tabs.</summary>
public class KeyCapLabel : UiElement
{
    /// <summary>Initializes a new instance of the <see cref="KeyCapLabel"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="key">The key's name.</param>
    /// <param name="style">Header or footer styling.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public KeyCapLabel(UiTheme theme, string key, KeyCapStyle style = KeyCapStyle.Header)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        Key = key ?? string.Empty;
        Style = style;
        IsHitTestVisible = false;
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the key's name.</summary>
    public string Key { get; set; }

    /// <summary>Gets or sets the styling.</summary>
    public KeyCapStyle Style { get; set; }

    /// <summary>Gets or sets the key name's size, in design units.</summary>
    public float TextSize { get; set; } = 12f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        KitDraw.MeasureKeyCap(context, Theme, Key, TextSize);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        UiVec2 size = KitDraw.MeasureKeyCap(painter.Context, Theme, Key, TextSize);
        KitDraw.KeyCap(painter, Theme, Key, new UiVec2(Bounds.X, Bounds.Center.Y - (size.Y * 0.5f)), Style, TextSize);
    }
}
