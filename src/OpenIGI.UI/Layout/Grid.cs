namespace OpenIGI.UI.Layout;

/// <summary>
/// Arranges children into a grid of rows and columns, each track sized absolutely, to its content, or
/// as a weighted share of what is left.
/// </summary>
/// <remarks>
/// <para>
/// The grid is what a settings screen is built on: a label column sized to its content, a control column
/// taking the rest, and a row per option. Tracks are declared with <see cref="GridLength"/>; a child is
/// placed with <see cref="Add(UiElement, int, int, int, int)"/> and may span several.
/// </para>
/// <para>
/// The sizing is the usual three-phase pass — absolute tracks take their length, auto tracks take the
/// largest desired size of the single-track children in them, and star tracks divide the remainder by
/// weight. Auto sizing considers only children that do not span, which keeps the solve a single pass
/// while covering every layout the shell actually builds; a spanning child is still placed and measured
/// against the tracks it covers.
/// </para>
/// </remarks>
public class Grid : UiElement
{
    private readonly Dictionary<UiElement, Cell> _cells = [];

    /// <summary>Gets the column definitions; a single star column is used when none are added.</summary>
    public IList<GridLength> ColumnDefinitions { get; } = [];

    /// <summary>Gets the row definitions; a single star row is used when none are added.</summary>
    public IList<GridLength> RowDefinitions { get; } = [];

    /// <summary>Gets or sets the gap between columns, in design units.</summary>
    public float ColumnSpacing { get; set; }

    /// <summary>Gets or sets the gap between rows, in design units.</summary>
    public float RowSpacing { get; set; }

    /// <summary>Gets or sets the padding inside the grid, in design units.</summary>
    public Thickness Padding { get; set; }

    /// <summary>Adds a child at a grid cell.</summary>
    /// <param name="child">The child to add.</param>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based column.</param>
    /// <param name="rowSpan">How many rows the child spans.</param>
    /// <param name="columnSpan">How many columns the child spans.</param>
    /// <returns>The child, so calls can be chained.</returns>
    public UiElement Add(UiElement child, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        AddChild(child);
        _cells[child] = new Cell(
            Math.Max(0, row),
            Math.Max(0, column),
            Math.Max(1, rowSpan),
            Math.Max(1, columnSpan));
        return child;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        Thickness padding = context.Px(Padding);
        float columnGap = context.Px(ColumnSpacing);
        float rowGap = context.Px(RowSpacing);
        var inner = new UiVec2(
            MathF.Max(0f, available.X - padding.Horizontal),
            MathF.Max(0f, available.Y - padding.Vertical));

        GridLength[] columns = ColumnDefinitions.Count > 0 ? [.. ColumnDefinitions] : [GridLength.Star];
        GridLength[] rows = RowDefinitions.Count > 0 ? [.. RowDefinitions] : [GridLength.Star];

        // First measure every child freely so auto tracks know how big their content wants to be.
        foreach (UiElement child in Children)
        {
            if (child.Visibility != Visibility.Collapsed)
            {
                child.Measure(context, inner);
            }
        }

        float[] columnWidths = ResolveTracks(columns, inner.X, columnGap, context, horizontal: true);
        float[] rowHeights = ResolveTracks(rows, inner.Y, rowGap, context, horizontal: false);

        _lastColumnWidths = columnWidths;
        _lastRowHeights = rowHeights;
        _lastColumnGap = columnGap;
        _lastRowGap = rowGap;

        float width = Sum(columnWidths) + (columnGap * Math.Max(0, columnWidths.Length - 1));
        float height = Sum(rowHeights) + (rowGap * Math.Max(0, rowHeights.Length - 1));
        return new UiVec2(width + padding.Horizontal, height + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        UiRect inner = contentRect.Deflate(context.Px(Padding));
        float[] columnWidths = _lastColumnWidths;
        float[] rowHeights = _lastRowHeights;

        float[] columnX = Offsets(inner.X, columnWidths, _lastColumnGap);
        float[] rowY = Offsets(inner.Y, rowHeights, _lastRowGap);

        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            Cell cell = _cells.TryGetValue(child, out Cell c) ? c : new Cell(0, 0, 1, 1);
            int col = Math.Min(cell.Column, columnWidths.Length - 1);
            int row = Math.Min(cell.Row, rowHeights.Length - 1);
            int colSpan = Math.Min(cell.ColumnSpan, columnWidths.Length - col);
            int rowSpan = Math.Min(cell.RowSpan, rowHeights.Length - row);

            float x = columnX[col];
            float y = rowY[row];
            float w = SpanSize(columnWidths, col, colSpan, _lastColumnGap);
            float h = SpanSize(rowHeights, row, rowSpan, _lastRowGap);
            child.Arrange(new UiRect(x, y, w, h));
        }
    }

    private static float Sum(float[] values)
    {
        float total = 0f;
        foreach (float value in values)
        {
            total += value;
        }

        return total;
    }

    private static float[] Offsets(float start, float[] sizes, float gap)
    {
        float[] offsets = new float[sizes.Length];
        float cursor = start;
        for (int i = 0; i < sizes.Length; i++)
        {
            offsets[i] = cursor;
            cursor += sizes[i] + gap;
        }

        return offsets;
    }

    private static float SpanSize(float[] sizes, int start, int span, float gap)
    {
        float total = 0f;
        for (int i = 0; i < span; i++)
        {
            total += sizes[start + i];
        }

        return total + (gap * Math.Max(0, span - 1));
    }

    private float[] ResolveTracks(GridLength[] tracks, float available, float gap, LayoutContext context, bool horizontal)
    {
        float[] sizes = new float[tracks.Length];
        float used = gap * Math.Max(0, tracks.Length - 1);
        float starWeight = 0f;

        for (int i = 0; i < tracks.Length; i++)
        {
            switch (tracks[i].Unit)
            {
                case GridUnit.Absolute:
                    sizes[i] = context.Px(tracks[i].Value);
                    used += sizes[i];
                    break;
                case GridUnit.Auto:
                    sizes[i] = AutoTrackSize(i, horizontal);
                    used += sizes[i];
                    break;
                case GridUnit.Star:
                    starWeight += MathF.Max(0f, tracks[i].Value);
                    break;
            }
        }

        if (starWeight > 0f)
        {
            float remainder = MathF.Max(0f, available - used);
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Unit == GridUnit.Star)
                {
                    sizes[i] = remainder * (MathF.Max(0f, tracks[i].Value) / starWeight);
                }
            }
        }

        return sizes;
    }

    private float AutoTrackSize(int track, bool horizontal)
    {
        float size = 0f;
        foreach (UiElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed || !_cells.TryGetValue(child, out Cell cell))
            {
                continue;
            }

            int index = horizontal ? cell.Column : cell.Row;
            int span = horizontal ? cell.ColumnSpan : cell.RowSpan;
            if (index == track && span == 1)
            {
                size = MathF.Max(size, horizontal ? child.DesiredSize.X : child.DesiredSize.Y);
            }
        }

        return size;
    }

    private float[] _lastColumnWidths = [];
    private float[] _lastRowHeights = [];
    private float _lastColumnGap;
    private float _lastRowGap;

    private readonly record struct Cell(int Row, int Column, int RowSpan, int ColumnSpan);
}
