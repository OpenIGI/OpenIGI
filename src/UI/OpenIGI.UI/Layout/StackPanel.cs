namespace OpenIGI.UI.Layout;

/// <summary>
/// Lays children out in a single row or column, each at its desired size along the stacking axis and
/// stretched (or aligned) across it, with a fixed gap between them.
/// </summary>
/// <remarks>
/// The workhorse container: a vertical stack is a menu's list of options, a horizontal stack a row of
/// chips or buttons. Children keep their desired size along the main axis, so the stack grows to fit
/// them; across the axis they fill the stack unless their own alignment says otherwise.
/// </remarks>
public class StackPanel : UiElement
{
    /// <summary>Gets or sets the axis children are placed along.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Gets or sets the gap between adjacent children, in design units.</summary>
    public float Spacing { get; set; }

    /// <summary>Gets or sets the padding inside the panel, in design units.</summary>
    public Thickness Padding { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        Thickness padding = context.Px(Padding);
        float spacing = context.Px(Spacing);
        var inner = new UiVec2(
            MathF.Max(0f, available.X - padding.Horizontal),
            MathF.Max(0f, available.Y - padding.Vertical));

        bool horizontal = Orientation == Orientation.Horizontal;
        float main = 0f;
        float cross = 0f;
        int counted = 0;

        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            // Offer each child the full cross extent and the space left along the main axis, so a child
            // that sizes to content measures against what remains rather than the whole panel.
            float remainingMain = horizontal
                ? MathF.Max(0f, inner.X - main - (counted > 0 ? spacing : 0f))
                : MathF.Max(0f, inner.Y - main - (counted > 0 ? spacing : 0f));
            var offer = horizontal ? new UiVec2(remainingMain, inner.Y) : new UiVec2(inner.X, remainingMain);
            child.Measure(context, offer);

            float childMain = horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
            float childCross = horizontal ? child.DesiredSize.Y : child.DesiredSize.X;
            main += childMain + (counted > 0 ? spacing : 0f);
            cross = MathF.Max(cross, childCross);
            counted++;
        }

        var content = horizontal ? new UiVec2(main, cross) : new UiVec2(cross, main);
        return new UiVec2(content.X + padding.Horizontal, content.Y + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        UiRect inner = contentRect.Deflate(context.Px(Padding));
        float spacing = context.Px(Spacing);
        bool horizontal = Orientation == Orientation.Horizontal;
        float cursor = horizontal ? inner.X : inner.Y;

        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (horizontal)
            {
                float w = child.DesiredSize.X;
                child.Arrange(new UiRect(cursor, inner.Y, w, inner.Height));
                cursor += w + spacing;
            }
            else
            {
                float h = child.DesiredSize.Y;
                child.Arrange(new UiRect(inner.X, cursor, inner.Width, h));
                cursor += h + spacing;
            }
        }
    }
}
