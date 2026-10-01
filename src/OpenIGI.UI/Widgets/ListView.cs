using OpenIGI.Core.Graphics;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Widgets;

/// <summary>
/// A scrolling list of text rows with a moving selection, driven by up/down and activated by accept.
/// </summary>
/// <remarks>
/// One focusable control for the whole list rather than a focusable per row: up and down move the
/// selection (and are consumed so focus stays on the list), the view scrolls to keep the selection in
/// sight, and a spring glides both the highlight and the scroll. It is what a save-slot list or the
/// console's completion suggestions are built on.
/// </remarks>
public class ListView : Control
{
    private readonly List<string> _items;
    private readonly Spring _scroll = new(0f, smoothTime: 0.12f);
    private readonly Spring _highlight = new(0f, smoothTime: 0.10f);
    private int _selected;
    private int _hovered = -1;

    /// <summary>Initializes a new instance of the <see cref="ListView"/> class.</summary>
    /// <param name="theme">The theme to draw from.</param>
    /// <param name="items">The initial items, or <see langword="null"/> for an empty list.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public ListView(UiTheme theme, IEnumerable<string>? items = null)
        : base(theme)
    {
        _items = items is null ? [] : [.. items];
    }

    /// <summary>Raised when a row is activated, with its index.</summary>
    public event Action<int>? ItemActivated;

    /// <summary>Raised when the selection changes, with the new index.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets the items.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Gets or sets the row height in design units.</summary>
    public float RowHeight { get; set; } = 34f;

    /// <summary>Gets or sets the number of rows the list shows; drives its measured height.</summary>
    public int VisibleRows { get; set; } = 8;

    /// <summary>Gets or sets the selected index, clamped to the item range.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (_items.Count == 0)
            {
                _selected = 0;
                return;
            }

            int clamped = Math.Clamp(value, 0, _items.Count - 1);
            if (clamped == _selected)
            {
                return;
            }

            _selected = clamped;
            SelectionChanged?.Invoke(_selected);
        }
    }

    /// <summary>Replaces the list's items, keeping the selection in range.</summary>
    /// <param name="items">The new items.</param>
    public void SetItems(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items.Clear();
        _items.AddRange(items);
        _selected = _items.Count > 0 ? Math.Clamp(_selected, 0, _items.Count - 1) : 0;
    }

    /// <inheritdoc/>
    public override bool HandleNavigate(NavDirection direction)
    {
        switch (direction)
        {
            case NavDirection.Up when _selected > 0:
                SelectedIndex = _selected - 1;
                return true;
            case NavDirection.Down when _selected < _items.Count - 1:
                SelectedIndex = _selected + 1;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Gets the index of the row under a point, or -1 when the point is over none.</summary>
    /// <param name="point">The point in pixels.</param>
    /// <returns>The row index, or -1.</returns>
    public int IndexAt(UiVec2 point)
    {
        float rowHeight = Context?.Px(RowHeight) ?? RowHeight;
        if (rowHeight <= 0f || !Bounds.Contains(point))
        {
            return -1;
        }

        int index = (int)MathF.Floor((point.Y - Bounds.Y + Math.Clamp(_scroll.Value, 0f, MaxScrollPixels(rowHeight))) / rowHeight);
        return index >= 0 && index < _items.Count ? index : -1;
    }

    /// <inheritdoc/>
    /// <remarks>Clicking a row picks that row and activates it, rather than activating whatever was current.</remarks>
    public override void ActivateAt(UiVec2 point)
    {
        int index = IndexAt(point);
        if (index >= 0)
        {
            SelectedIndex = index;
        }

        Activate();
    }

    /// <inheritdoc/>
    public override void PointerMoved(UiVec2 point) => _hovered = IndexAt(point);

    /// <inheritdoc/>
    protected override void OnPointerOverChanged(bool over)
    {
        if (!over)
        {
            _hovered = -1;
        }
    }

    /// <inheritdoc/>
    protected override void OnActivate()
    {
        if (_items.Count > 0)
        {
            ItemActivated?.Invoke(_selected);
        }
    }

    private float MaxScrollPixels(float rowHeight) => MathF.Max(0f, (_items.Count * rowHeight) - Bounds.Height);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float rows = Math.Min(VisibleRows, Math.Max(1, _items.Count));
        return new UiVec2(available.X, context.Px(RowHeight) * rows);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _scroll.Update(elapsedSeconds);
        _highlight.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float rowHeight = painter.Context.Px(RowHeight);
        float sizePx = painter.Context.Px(Theme.BodyTextSize);

        // Keep the selection within the visible window by nudging the scroll target.
        float selectionTop = _selected * rowHeight;
        float selectionBottom = selectionTop + rowHeight;
        float viewTop = _scroll.Target;
        float viewBottom = viewTop + Bounds.Height;
        if (selectionTop < viewTop)
        {
            _scroll.Target = selectionTop;
        }
        else if (selectionBottom > viewBottom)
        {
            _scroll.Target = selectionBottom - Bounds.Height;
        }

        float maxScroll = MathF.Max(0f, (_items.Count * rowHeight) - Bounds.Height);
        _scroll.Target = Math.Clamp(_scroll.Target, 0f, maxScroll);
        float scroll = Math.Clamp(_scroll.Value, 0f, maxScroll);

        _highlight.Target = selectionTop;

        painter.PushClip(Bounds);

        // The selection highlight, gliding on its own spring.
        if (_items.Count > 0 && CanFocus)
        {
            float highlightY = Bounds.Y + _highlight.Value - scroll;
            byte fillAlpha = (byte)(Theme.Selection.A * MathF.Max(0.5f, Visual.Focus));
            painter.FillRect(new UiRect(Bounds.X, highlightY, Bounds.Width, rowHeight), Theme.Selection.WithAlpha(fillAlpha), UiBlend.AlphaBlend);
            painter.FillRect(new UiRect(Bounds.X, highlightY, painter.Context.Px(3f), rowHeight), Theme.Accent.WithAlpha((byte)(255 * MathF.Max(0.5f, Visual.Focus))), UiBlend.AlphaBlend);
        }

        float pad = painter.Context.Px(10f);
        for (int i = 0; i < _items.Count; i++)
        {
            float y = Bounds.Y + (i * rowHeight) - scroll;
            if (y + rowHeight < Bounds.Y || y > Bounds.Bottom)
            {
                continue; // fully outside the viewport
            }

            bool selected = i == _selected;
            if (i == _hovered && !selected)
            {
                painter.FillRect(
                    new UiRect(Bounds.X, y, Bounds.Width, rowHeight),
                    Theme.FocusFill.WithAlpha((byte)(Theme.FocusFill.A * 0.5f)),
                    UiBlend.AlphaBlend);
            }

            Bgra32 color = !IsEnabled ? Theme.TextDisabled
                : selected ? Interpolate.Lerp(Theme.Text, Theme.Accent, Visual.Focus)
                : i == _hovered ? Theme.Text
                : Theme.TextMuted;
            float textY = y + ((rowHeight - Theme.Body.LineHeight(sizePx)) * 0.5f);
            painter.DrawText(Theme.Body, _items[i], new UiVec2(Bounds.X + pad, textY), sizePx, color);
        }

        painter.PopClip();
    }
}
