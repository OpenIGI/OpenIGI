namespace OpenIGI.UI.Layout;

/// <summary>
/// Columns side by side while each can be at least <see cref="MinColumnWidth"/> wide, and one above the other
/// when the window is too narrow for that.
/// </summary>
/// <remarks>
/// A page of settings groups reads best in two columns on a wide window and in one on a narrow window. Two
/// squeezed columns are worse than one: the rows have no room left for their captions. Side by side, the
/// columns share the width evenly and each is as tall as its own content, top-aligned.
/// </remarks>
public class AdaptiveColumns : UiElement
{
    /// <summary>Gets or sets the narrowest a column may be before the columns stack, in design units.</summary>
    public float MinColumnWidth { get; set; } = 520f;

    /// <summary>Gets or sets the gap between columns side by side, and between them stacked, in design units.</summary>
    public float Spacing { get; set; } = 20f;

    /// <summary>Gets a value indicating whether the columns were last laid out one above the other.</summary>
    public bool IsStacked { get; private set; }

    /// <summary>Adds a column.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The column.</returns>
    public UiElement Add(UiElement column)
    {
        AddChild(column);
        return column;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        List<UiElement> columns = Columns();
        if (columns.Count == 0)
        {
            return UiVec2.Zero;
        }

        float spacing = context.Px(Spacing);
        IsStacked = available.X < (columns.Count * context.Px(MinColumnWidth)) + ((columns.Count - 1) * spacing);
        if (IsStacked)
        {
            float height = 0f;
            foreach (UiElement column in columns)
            {
                column.Measure(context, new UiVec2(available.X, float.PositiveInfinity));
                height += column.DesiredSize.Y;
            }

            return new UiVec2(available.X, height + ((columns.Count - 1) * spacing));
        }

        float width = ColumnWidth(available.X, columns.Count, spacing);
        float tallest = 0f;
        foreach (UiElement column in columns)
        {
            column.Measure(context, new UiVec2(width, float.PositiveInfinity));
            tallest = MathF.Max(tallest, column.DesiredSize.Y);
        }

        return new UiVec2(available.X, tallest);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        List<UiElement> columns = Columns();
        float spacing = context.Px(Spacing);
        if (IsStacked)
        {
            float y = contentRect.Y;
            foreach (UiElement column in columns)
            {
                column.Arrange(new UiRect(contentRect.X, y, contentRect.Width, column.DesiredSize.Y));
                y += column.DesiredSize.Y + spacing;
            }

            return;
        }

        float width = ColumnWidth(contentRect.Width, columns.Count, spacing);
        float x = contentRect.X;
        foreach (UiElement column in columns)
        {
            column.Arrange(new UiRect(x, contentRect.Y, width, column.DesiredSize.Y));
            x += width + spacing;
        }
    }

    private static float ColumnWidth(float total, int count, float spacing) =>
        MathF.Max(0f, (total - ((count - 1) * spacing)) / count);

    private List<UiElement> Columns() => [.. Children.Where(static c => c.Visibility != Visibility.Collapsed)];
}
