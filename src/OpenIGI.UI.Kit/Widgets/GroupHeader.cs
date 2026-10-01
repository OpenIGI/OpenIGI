using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>A group's heading inside a settings panel: an accent caption in monospace, then a hairline to the edge.</summary>
/// <remarks>Not focusable, so up and down step straight over it from the last row of one group to the first of the next.</remarks>
public class GroupHeader : UiElement
{
    private string _upper;

    /// <summary>Initializes a new instance of the <see cref="GroupHeader"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="title">The group's name, shown in capitals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public GroupHeader(UiTheme theme, string title)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
        _upper = (title ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets or sets the group's name.</summary>
    public string Title
    {
        get => _upper;
        set => _upper = (value ?? string.Empty).ToUpperInvariant();
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, Theme.Mono.LineHeight(context.Px(11f)) + context.Px(10f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float size = context.Px(11f);
        float line = Theme.Mono.LineHeight(size);
        UiVec2 pen = painter.DrawLabel(Theme.Mono, _upper, Bounds.TopLeft, size, Theme.Accent, 0.28f);
        float ruleX = pen.X + context.Px(12f);
        if (ruleX < Bounds.Right)
        {
            painter.FillRect(new UiRect(ruleX, Bounds.Y + (line * 0.5f), Bounds.Right - ruleX, context.Px(1f)), Theme.Border);
        }
    }
}
