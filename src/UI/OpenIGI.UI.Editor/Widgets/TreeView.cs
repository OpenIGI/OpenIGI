using OpenIGI.Core.Collections;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>Where a dragged row would land relative to the row under the pointer.</summary>
public enum TreeDropPosition
{
    /// <summary>Just before the target, under the same parent.</summary>
    Before,

    /// <summary>As the target's last child.</summary>
    Inside,

    /// <summary>Just after the target, under the same parent.</summary>
    After,
}

/// <summary>Describes the items a <see cref="TreeView{T}"/> shows.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class TreeSource<T>
    where T : class
{
    /// <summary>Gets the top-level items.</summary>
    public required Func<IEnumerable<T>> Roots { get; init; }

    /// <summary>Gets an item's children.</summary>
    public required Func<T, IReadOnlyList<T>> Children { get; init; }

    /// <summary>Gets an item's parent, for revealing it; <see langword="null"/> for a root.</summary>
    public required Func<T, T?> Parent { get; init; }

    /// <summary>Gets an item's label.</summary>
    public required Func<T, string> Label { get; init; }

    /// <summary>Gets an item's icon.</summary>
    public Func<T, KitIcon>? Icon { get; init; }

    /// <summary>Gets faint text drawn at the row's right, such as the item's type.</summary>
    public Func<T, string?>? Detail { get; init; }

    /// <summary>Gets an item's text colour, or <see langword="null"/> for the normal one.</summary>
    public Func<T, Bgra32?>? Tint { get; init; }
}

/// <summary>
/// A virtualised tree: expand and collapse, multi-select with Ctrl and Shift, keyboard navigation, search
/// filtering that keeps the matches' ancestors, drag and drop to reorder and reparent, and context menus.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// Only the rows on screen are drawn, so a mission with twenty thousand tasks scrolls as smoothly as one with
/// twenty. The tree owns no data: it reads <see cref="TreeSource{T}"/> and reports what the user asked for
/// (<see cref="ItemActivated"/>, <see cref="DropRequested"/>…), so the edit itself goes through the editor's
/// history like any other.
/// </remarks>
public sealed class TreeView<T> : EditorElement
    where T : class
{
    private readonly TreeSource<T> _source;
    private readonly HashSet<T> _expanded = new(ReferenceEqualityComparer.Instance);
    private readonly List<(T Item, int Depth, bool HasChildren)> _rows = [];
    private readonly ScrollModel _scroll = new();
    private string _filter = string.Empty;
    private bool _dirty = true;
    private T? _anchor;
    private (T Item, UiVec2 From)? _pressed;
    private bool _dragging;
    private (T Target, TreeDropPosition Position)? _drop;

    /// <summary>Initializes a new instance of the <see cref="TreeView{T}"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What to show.</param>
    /// <param name="selection">The selection the tree shows and changes, shared with the rest of the editor.</param>
    public TreeView(EditorTheme theme, TreeSource<T> source, Selection<T> selection)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selection);
        _source = source;
        Selection = selection;
    }

    /// <summary>Raised on a double click or Enter: the editor frames the item in the viewport.</summary>
    public event Action<T>? ItemActivated;

    /// <summary>Raised on a right click, with the point to open a menu at; the item is already selected.</summary>
    public event Action<T?, UiVec2>? ContextRequested;

    /// <summary>Raised when the user drops the selected items onto a row.</summary>
    public event Action<IReadOnlyList<T>, T, TreeDropPosition>? DropRequested;

    /// <summary>Raised on Delete.</summary>
    public event Action<IReadOnlyList<T>>? DeleteRequested;

    /// <summary>Gets the selection.</summary>
    public Selection<T> Selection { get; }

    /// <summary>Gets or sets whether rows can be dragged.</summary>
    public bool AllowDrag { get; set; } = true;

    /// <summary>Gets or sets the search text; rows that do not match, and are not ancestors of a match, are hidden.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            _filter = value ?? string.Empty;
            Invalidate();
        }
    }

    /// <summary>Gets the number of visible rows.</summary>
    public int RowCount
    {
        get
        {
            Rebuild();
            return _rows.Count;
        }
    }

    /// <inheritdoc/>
    public override bool Focusable => true;

    private float Row => Theme.RowHeight;

    /// <summary>Marks the rows stale after the data changed.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Gets whether an item is expanded.</summary>
    /// <param name="item">The item.</param>
    /// <returns><see langword="true"/> when its children are shown.</returns>
    public bool IsExpanded(T item) => _expanded.Contains(item);

    /// <summary>Expands or collapses an item.</summary>
    /// <param name="item">The item.</param>
    /// <param name="expanded">Whether to show its children.</param>
    public void SetExpanded(T item, bool expanded)
    {
        if (expanded ? _expanded.Add(item) : _expanded.Remove(item))
        {
            Invalidate();
        }
    }

    /// <summary>Expands an item's ancestors and scrolls it into view.</summary>
    /// <param name="item">The item.</param>
    public void Reveal(T item)
    {
        for (T? parent = _source.Parent(item); parent is not null; parent = _source.Parent(parent))
        {
            _expanded.Add(parent);
        }

        _dirty = true;
        Rebuild();
        int index = IndexOf(item);
        if (index >= 0)
        {
            _scroll.Reveal(index * Row, Row);
        }
    }

    /// <summary>Gets the row index at a point, or -1.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The index.</returns>
    public int RowAt(UiVec2 point)
    {
        Rebuild();
        if (!Bounds.Contains(point))
        {
            return -1;
        }

        int index = (int)((point.Y - Bounds.Y + _scroll.Offset) / Row);
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    /// <summary>Gets the rectangle of a row, which may be off screen.</summary>
    /// <param name="index">The row index.</param>
    /// <returns>The rectangle.</returns>
    public UiRect RowRect(int index) => new(Bounds.X, Bounds.Y + (index * Row) - _scroll.Offset, Bounds.Width, Row);

    /// <summary>Gets the item shown in a row.</summary>
    /// <param name="index">The row index.</param>
    /// <returns>The item.</returns>
    public T ItemAt(int index)
    {
        Rebuild();
        return _rows[index].Item;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : Theme.Px(240f), float.IsFinite(available.Y) ? available.Y : Theme.Px(300f));

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        Rebuild();
        _scroll.Measure(_rows.Count * Row, contentRect.Height);
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button == MouseButton.Left && _scroll.TryGrab(Theme, Bounds, e.Position))
        {
            return true;
        }

        int index = RowAt(e.Position);
        if (index < 0)
        {
            if (e.Button == MouseButton.Left && !e.Control && !e.Shift)
            {
                Selection.Clear();
            }

            if (e.Button == MouseButton.Right)
            {
                ContextRequested?.Invoke(null, e.Position);
            }

            return e.Button == MouseButton.Left;
        }

        (T item, int depth, bool hasChildren) = _rows[index];
        if (e.Button == MouseButton.Right)
        {
            if (!Selection.Contains(item))
            {
                Selection.Set(item);
            }

            ContextRequested?.Invoke(item, e.Position);
            return false;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        if (hasChildren && ArrowRect(index, depth).Contains(e.Position))
        {
            SetExpanded(item, !IsExpanded(item));
            return true;
        }

        if (e.ClickCount == 2)
        {
            ItemActivated?.Invoke(item);
            return true;
        }

        // A plain press on an already-selected row keeps the group selected, so the group can be dragged.
        if (!(Selection.Contains(item) && !e.Control && !e.Shift))
        {
            Select(index, e.Control, e.Shift);
        }

        _pressed = (item, e.Position);
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (_scroll.FollowGrab(Theme, Bounds, e.Position))
        {
            return;
        }

        if (!IsCapturing || _pressed is not { } pressed || !AllowDrag)
        {
            return;
        }

        if (!_dragging && MathF.Abs(e.Position.Y - pressed.From.Y) + MathF.Abs(e.Position.X - pressed.From.X) > Theme.Px(6f))
        {
            _dragging = true;
        }

        if (_dragging)
        {
            _drop = DropAt(e.Position);
            AutoScroll(e.Position.Y);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        if (_scroll.ReleaseGrab())
        {
            return;
        }

        if (_dragging && _drop is { } drop)
        {
            DropRequested?.Invoke([.. Selection.Items], drop.Target, drop.Position);
        }
        else if (_pressed is { } pressed && !_dragging && !e.Control && !e.Shift && Selection.Count > 1)
        {
            // Pressing inside a multi-selection and releasing without dragging narrows it to that row.
            int index = IndexOf(pressed.Item);
            if (index >= 0)
            {
                Select(index, control: false, shift: false);
            }
        }

        EndDrag();
    }

    /// <inheritdoc/>
    protected internal override void OnCaptureLost()
    {
        _scroll.ReleaseGrab();
        EndDrag();
    }

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers) =>
        _scroll.Wheel(notches, Row * 3f);

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        Rebuild();
        int current = Selection.Primary is { } primary ? IndexOf(primary) : -1;
        int page = Math.Max(1, (int)(Bounds.Height / Row) - 1);
        switch (e.Key)
        {
            case KeyCode.Down:
                Move(current < 0 ? 0 : current + 1, e.Shift);
                return true;
            case KeyCode.Up:
                Move(current < 0 ? 0 : current - 1, e.Shift);
                return true;
            case KeyCode.PageDown:
                Move(current + page, e.Shift);
                return true;
            case KeyCode.PageUp:
                Move(current - page, e.Shift);
                return true;
            case KeyCode.Home:
                Move(0, e.Shift);
                return true;
            case KeyCode.End:
                Move(_rows.Count - 1, e.Shift);
                return true;
            case KeyCode.Right when current >= 0:
                if (_rows[current].HasChildren && !IsExpanded(_rows[current].Item))
                {
                    SetExpanded(_rows[current].Item, true);
                }
                else
                {
                    Move(current + 1, false);
                }

                return true;
            case KeyCode.Left when current >= 0:
                if (IsExpanded(_rows[current].Item))
                {
                    SetExpanded(_rows[current].Item, false);
                }
                else if (_source.Parent(_rows[current].Item) is { } parent)
                {
                    Move(IndexOf(parent), false);
                }

                return true;
            case KeyCode.Return when Selection.Primary is { } item:
                ItemActivated?.Invoke(item);
                return true;
            case KeyCode.Delete when Selection.Count > 0:
                DeleteRequested?.Invoke([.. Selection.Items]);
                return true;
            case KeyCode.A when e.Control:
                Selection.Set(_rows.Select(static r => r.Item));
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        Rebuild();
        painter.FillRect(Bounds, Theme.Panel);
        painter.PushClip(Bounds);

        int first = Math.Max(0, (int)(_scroll.Offset / Row));
        int last = Math.Min(_rows.Count - 1, (int)((_scroll.Offset + Bounds.Height) / Row));
        int hovered = IsPointerOver && !_dragging ? RowAt(Root?.PointerPosition ?? default) : -1;
        bool active = IsFocusWithin;
        for (int i = first; i <= last; i++)
        {
            DrawRow(painter, i, i == hovered, active);
        }

        if (_drop is { } drop && IndexOf(drop.Target) is var target and >= 0)
        {
            UiRect row = RowRect(target);
            float x = row.X + Theme.Px(8f) + (_rows[target].Depth * Theme.Indent);
            if (drop.Position == TreeDropPosition.Inside)
            {
                EditorDraw.Frame(painter, Theme, row.Deflate(new Thickness(Theme.Px(2f), 0f)), Bgra32.Transparent, Theme.Accent);
            }
            else
            {
                float y = drop.Position == TreeDropPosition.Before ? row.Y : row.Bottom - Theme.Px(2f);
                painter.FillRect(new UiRect(x, y, row.Right - x, Theme.Px(2f)), Theme.Accent);
            }
        }

        painter.PopClip();
        _scroll.DrawVertical(painter, Theme, Bounds, IsPointerOver ? Root?.PointerPosition : null);
    }

    private UiRect ArrowRect(int index, int depth)
    {
        UiRect row = RowRect(index);
        return new UiRect(row.X + Theme.Px(4f) + (depth * Theme.Indent), row.Y, Theme.Indent, row.Height);
    }

    private void DrawRow(UiPainter painter, int index, bool hovered, bool active)
    {
        (T item, int depth, bool hasChildren) = _rows[index];
        UiRect row = RowRect(index);
        bool selected = Selection.Contains(item);
        if (selected)
        {
            painter.FillRect(row, active ? Theme.Selection : Theme.SelectionInactive);
            if (ReferenceEquals(item, Selection.Primary) && active)
            {
                painter.FillRect(new UiRect(row.X, row.Y, Theme.Px(2f), row.Height), Theme.Accent);
            }
        }
        else if (hovered)
        {
            painter.FillRect(row, Theme.Hover);
        }

        UiRect arrow = ArrowRect(index, depth);
        if (hasChildren)
        {
            painter.DrawChevron(arrow.Center, Theme.Px(3.5f), IsExpanded(item) ? UiDirection.Down : UiDirection.Right, Theme.Hairline * 1.5f, Theme.TextMuted);
        }

        float x = arrow.Right;
        KitIcon icon = _source.Icon?.Invoke(item) ?? KitIcon.None;
        if (icon != KitIcon.None)
        {
            EditorDraw.Icon(painter, Theme, icon, new UiRect(x, row.Y, Theme.IconSize, row.Height), selected && active ? Theme.Accent : null);
            x += Theme.IconSize + Theme.Px(6f);
        }

        float right = row.Right - Theme.Px(12f);
        if (_source.Detail?.Invoke(item) is { Length: > 0 } detail)
        {
            float width = MathF.Min(Theme.SmallFont.MeasureWidth(detail, Theme.SmallTextSize), (right - x) * 0.45f);
            string shown = EditorDraw.Fit(Theme.SmallFont, detail, Theme.SmallTextSize, width);
            float y = MathF.Round(row.Y + ((row.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f));
            painter.DrawText(Theme.SmallFont, shown, new UiVec2(right - width, y), Theme.SmallTextSize, Theme.TextDisabled);
            right -= width + Theme.Px(8f);
        }

        Bgra32 ink = _source.Tint?.Invoke(item) ?? Theme.Text;
        EditorDraw.Label(painter, Theme, _source.Label(item), new UiRect(x, row.Y, right - x, row.Height), ink);
    }

    private void Rebuild()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _rows.Clear();
        bool filtering = _filter.Length > 0;
        foreach (T root in _source.Roots())
        {
            Add(root, 0, filtering);
        }
    }

    // Adds an item and its visible descendants; while filtering, returns whether anything in the subtree matched.
    private bool Add(T item, int depth, bool filtering)
    {
        IReadOnlyList<T> children = _source.Children(item);
        if (!filtering)
        {
            _rows.Add((item, depth, children.Count > 0));
            if (IsExpanded(item))
            {
                foreach (T child in children)
                {
                    Add(child, depth + 1, filtering: false);
                }
            }

            return true;
        }

        int at = _rows.Count;
        bool matches = _source.Label(item).Contains(_filter, StringComparison.OrdinalIgnoreCase);
        _rows.Add((item, depth, children.Count > 0));
        bool any = false;
        foreach (T child in children)
        {
            any |= Add(child, depth + 1, filtering: true);
        }

        if (!matches && !any)
        {
            _rows.RemoveAt(at);
            return false;
        }

        return true;
    }

    private int IndexOf(T item) => _rows.FindIndex(r => ReferenceEquals(r.Item, item));

    private void Select(int index, bool control, bool shift)
    {
        T item = _rows[index].Item;
        if (shift && _anchor is not null && IndexOf(_anchor) is var from and >= 0)
        {
            int a = Math.Min(from, index);
            int b = Math.Max(from, index);
            IEnumerable<T> range = _rows.Skip(a).Take(b - a + 1).Select(static r => r.Item);
            Selection.Set(control ? Selection.Items.Concat(range) : range.Where(r => !ReferenceEquals(r, item)).Append(item));
            return;
        }

        if (control)
        {
            Selection.Toggle(item);
        }
        else
        {
            Selection.Set(item);
        }

        _anchor = item;
    }

    private void Move(int index, bool extend)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _rows.Count - 1);
        Select(index, control: false, shift: extend);
        _scroll.Reveal(index * Row, Row);
    }

    private (T Target, TreeDropPosition Position)? DropAt(UiVec2 point)
    {
        int index = RowAt(point);
        if (index < 0)
        {
            return null;
        }

        T target = _rows[index].Item;

        // Dropping a row into itself or its own subtree is meaningless, so those targets are refused here.
        for (T? walk = target; walk is not null; walk = _source.Parent(walk))
        {
            if (Selection.Contains(walk))
            {
                return null;
            }
        }

        UiRect row = RowRect(index);
        float t = (point.Y - row.Y) / row.Height;
        return (target, t < 0.25f ? TreeDropPosition.Before : t > 0.75f ? TreeDropPosition.After : TreeDropPosition.Inside);
    }

    private void AutoScroll(float y)
    {
        float edge = Row;
        if (y < Bounds.Y + edge)
        {
            _scroll.Offset -= Row * 0.5f;
        }
        else if (y > Bounds.Bottom - edge)
        {
            _scroll.Offset += Row * 0.5f;
        }
    }

    private void EndDrag()
    {
        _pressed = null;
        _dragging = false;
        _drop = null;
    }
}
