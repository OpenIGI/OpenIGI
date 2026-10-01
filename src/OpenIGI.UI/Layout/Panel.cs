using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;

namespace OpenIGI.UI.Layout;

/// <summary>
/// A container that optionally fills a background and lays its children on top of one another, each
/// filling the padded content area.
/// </summary>
/// <remarks>
/// The simplest container and the layering primitive: a background colour behind a stack of children
/// that each occupy the whole content box, positioned by their own alignment. A translucent
/// <see cref="Background"/> over the resolved scene is exactly what the console panel and the shell's
/// screen backdrops are.
/// </remarks>
public class Panel : UiElement
{
    /// <summary>Gets or sets the padding inside the panel, in design units.</summary>
    public Thickness Padding { get; set; }

    /// <summary>Gets or sets the background fill, or <see langword="null"/> for none.</summary>
    public Bgra32? Background { get; set; }

    /// <summary>Gets or sets how the background combines with what is behind it.</summary>
    public UiBlend BackgroundBlend { get; set; } = UiBlend.AlphaBlend;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        Thickness padding = context.Px(Padding);
        var inner = new UiVec2(
            MathF.Max(0f, available.X - padding.Horizontal),
            MathF.Max(0f, available.Y - padding.Vertical));

        float width = 0f;
        float height = 0f;
        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Measure(context, inner);
            width = MathF.Max(width, child.DesiredSize.X);
            height = MathF.Max(height, child.DesiredSize.Y);
        }

        return new UiVec2(width + padding.Horizontal, height + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        UiRect inner = contentRect.Deflate(context.Px(Padding));
        foreach (UiElement child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                child.Arrange(inner);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        if (Background is { } color)
        {
            painter.FillRect(Bounds, color, BackgroundBlend);
        }
    }
}
