namespace OpenIGI.UI.Layout;

/// <summary>
/// Lays children out along one axis and distributes the free space, either by growing flexible children
/// to fill it or by spacing rigid children with a justification rule.
/// </summary>
/// <remarks>
/// A single-line flexbox. Where a <see cref="StackPanel"/> packs children at their desired size, a flex
/// panel decides what to do with the room left over: a child given a grow weight expands to take a share
/// of it, and when nothing grows the whole run is positioned by <see cref="Justify"/> — start, centre,
/// end, or one of the even-spacing rules. It is what a toolbar or a row of dialog buttons wants.
/// Wrapping onto multiple lines is intentionally out of scope; a grid covers that case.
/// </remarks>
public class FlexPanel : UiElement
{
    private readonly Dictionary<UiElement, float> _grow = [];

    /// <summary>Gets or sets the axis children are placed along.</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Gets or sets the gap between adjacent children, in design units.</summary>
    public float Gap { get; set; }

    /// <summary>Gets or sets the padding inside the panel, in design units.</summary>
    public Thickness Padding { get; set; }

    /// <summary>Gets or sets how free space is distributed when no child grows.</summary>
    public FlexJustify Justify { get; set; } = FlexJustify.Start;

    /// <summary>Adds a child with a grow weight.</summary>
    /// <param name="child">The child to add.</param>
    /// <param name="grow">The share of free space the child claims; zero keeps it at its desired size.</param>
    /// <returns>The child, so calls can be chained.</returns>
    public UiElement Add(UiElement child, float grow = 0f)
    {
        AddChild(child);
        _grow[child] = MathF.Max(0f, grow);
        return child;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        Thickness padding = context.Px(Padding);
        float gap = context.Px(Gap);
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

            child.Measure(context, inner);
            main += (horizontal ? child.DesiredSize.X : child.DesiredSize.Y) + (counted > 0 ? gap : 0f);
            cross = MathF.Max(cross, horizontal ? child.DesiredSize.Y : child.DesiredSize.X);
            counted++;
        }

        var content = horizontal ? new UiVec2(main, cross) : new UiVec2(cross, main);
        return new UiVec2(content.X + padding.Horizontal, content.Y + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        UiRect inner = contentRect.Deflate(context.Px(Padding));
        float gap = context.Px(Gap);
        bool horizontal = Orientation == Orientation.Horizontal;

        var visible = new List<UiElement>();
        float childrenMain = 0f;
        float totalGrow = 0f;
        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            visible.Add(child);
            childrenMain += horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
            totalGrow += _grow.GetValueOrDefault(child);
        }

        if (visible.Count == 0)
        {
            return;
        }

        float axisLength = horizontal ? inner.Width : inner.Height;
        float free = axisLength - childrenMain - (gap * (visible.Count - 1));

        // Leading offset and the extra gap between items depend on whether anything grows.
        float cursor = horizontal ? inner.X : inner.Y;
        float extraGap = 0f;
        if (totalGrow <= 0f)
        {
            (cursor, extraGap) = Distribute(cursor, MathF.Max(0f, free), visible.Count, gap, Justify);
        }

        foreach (UiElement child in visible)
        {
            float grow = _grow.GetValueOrDefault(child);
            float baseMain = horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
            float mainSize = totalGrow > 0f && grow > 0f ? baseMain + (free * (grow / totalGrow)) : baseMain;
            mainSize = MathF.Max(0f, mainSize);

            child.Arrange(horizontal
                ? new UiRect(cursor, inner.Y, mainSize, inner.Height)
                : new UiRect(inner.X, cursor, inner.Width, mainSize));
            cursor += mainSize + gap + extraGap;
        }
    }

    private static (float Start, float ExtraGap) Distribute(float start, float free, int count, float gap, FlexJustify justify)
    {
        return justify switch
        {
            FlexJustify.Start => (start, 0f),
            FlexJustify.Center => (start + (free * 0.5f), 0f),
            FlexJustify.End => (start + free, 0f),
            FlexJustify.SpaceBetween => (start, count > 1 ? free / (count - 1) : 0f),
            FlexJustify.SpaceAround => (start + (free / (count * 2f)), free / count),
            FlexJustify.SpaceEvenly => (start + (free / (count + 1f)), free / (count + 1f)),
            _ => (start, 0f),
        };
    }
}
