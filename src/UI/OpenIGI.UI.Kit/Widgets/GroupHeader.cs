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
        Theme.Large
            ? new(available.X, context.Px(24f))
            : new(available.X, Theme.Mono.LineHeight(context.Px(11f)) + context.Px(10f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        if (Theme.Large)
        {
            // The group's name in the label face, readable grey, and a rule from it to the panel's edge.
            float px = context.Px(Theme.BodyTextSize < 18f ? 14f : 16f);
            UiVec2 end = painter.DrawLabel(Theme.Heading, _upper, new UiVec2(Bounds.X, UiRowStyle.CapsCenterY(Theme.Heading, px, Bounds)), px, Theme.TextSecondary, 0.14f);
            float from = end.X + context.Px(14f);
            if (from < Bounds.Right)
            {
                painter.FillRect(new UiRect(from, Bounds.Center.Y, Bounds.Right - from, context.Px(1f)), UiPalette.White(0.12f));
            }

            return;
        }

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
