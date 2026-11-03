using OpenIGI.Core.Collections;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>What a <see cref="TileView{T}"/> shows of its items.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class TileSource<T>
    where T : class
{
    /// <summary>Gets the items, in order.</summary>
    public required Func<IReadOnlyList<T>> Items { get; init; }

    /// <summary>Gets an item's name.</summary>
    public required Func<T, string> Label { get; init; }

    /// <summary>Gets an item's icon, drawn when it has no picture.</summary>
    public Func<T, KitIcon>? Icon { get; init; }

    /// <summary>Gets an item's icon colour, or <see langword="null"/> for the theme's.</summary>
    public Func<T, Bgra32?>? IconTint { get; init; }

    /// <summary>Gets an item's picture and its size in pixels, or <see langword="null"/> while it has none (yet).</summary>
    public Func<T, (UiImage Image, int Width, int Height)?>? Thumbnail { get; init; }

    /// <summary>Gets a short line under the name, such as a type, or <see langword="null"/>.</summary>
    public Func<T, string?>? Detail { get; init; }

    /// <summary>Gets what dragging an item carries to another widget, or <see langword="null"/> when it cannot be dragged.</summary>
    public Func<T, DragDropData?>? Drag { get; init; }
}

/// <summary>
/// Items as a grid of tiles, a picture or icon over a name, as an asset browser shows files: click to select (Ctrl to add,
/// Shift for a run), double-click or Enter to open, drag out to another widget, Ctrl+wheel to size the tiles.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class TileView<T> : EditorElement
    where T : class
{
    private const float MinTile = 56f;
    private const float MaxTile = 192f;

    private readonly TileSource<T> _source;
    private readonly ScrollModel _scroll = new();
    private IReadOnlyList<T> _items = [];
    private T? _pressed;
    private UiVec2 _pressPoint;
    private int _anchor = -1;

    /// <summary>Initializes a new instance of the <see cref="TileView{T}"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="source">What to show.</param>
    /// <param name="selection">The selection it shows and changes.</param>
    public TileView(EditorTheme theme, TileSource<T> source, Selection<T> selection)
        : base(theme)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
    }

    /// <inheritdoc/>
    public override bool Focusable => true;

    /// <summary>Raised when an item is double-clicked or Enter is pressed on it.</summary>
    public event Action<T>? ItemActivated;

    /// <summary>Raised on a right-click, with the item under the pointer (or none) and the point.</summary>
    public event Action<T?, UiVec2>? ContextRequested;

    /// <summary>Gets the selection.</summary>
    public Selection<T> Selection { get; }

    /// <summary>Gets or sets a tile's width, in unscaled pixels.</summary>
    public float TileSize { get; set; } = 96f;

    /// <summary>Gets or sets the text shown when there are no items.</summary>
    public string EmptyText { get; set; } = "Nothing here";

    /// <summary>Gets the items shown at the moment, after the last layout.</summary>
    public IReadOnlyList<T> Shown => _items;

    private float Tile => Theme.Px(TileSize);

    private float TileHeight => Tile + (Theme.SmallFont.LineHeight(Theme.SmallTextSize) * 2f) + Theme.Px(8f);

    private float Gap => Theme.Px(6f);

    private int Columns => Math.Max(1, (int)((Bounds.Width - Gap - Theme.Px(10f)) / (Tile + Gap)));

    /// <summary>Scrolls an item into view.</summary>
    /// <param name="item">The item.</param>
    public void Reveal(T item)
    {
        int index = IndexOf(item);
        if (index >= 0)
        {
            _scroll.Reveal((index / Columns) * (TileHeight + Gap), TileHeight + Gap);
        }
    }

    /// <summary>Scrolls back to the top, as when another folder is opened.</summary>
    public void ScrollToTop() => _scroll.Offset = 0f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : Theme.Px(400f), float.IsFinite(available.Y) ? available.Y : Theme.Px(300f));

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        _items = _source.Items();
        int rows = (_items.Count + Columns - 1) / Columns;
        _scroll.Measure((rows * (TileHeight + Gap)) + Gap, contentRect.Height);
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (e.Button == MouseButton.Left && _scroll.TryGrab(Theme, Bounds, e.Position))
        {
            return true;
        }

        int index = IndexAt(e.Position);
        T? item = index >= 0 ? _items[index] : null;
        if (e.Button == MouseButton.Right)
        {
            if (item is not null && !Selection.Contains(item))
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

        if (item is null)
        {
            if (!e.Control && !e.Shift)
            {
                Selection.Clear();
            }

            return true;
        }

        if (e.ClickCount == 2)
        {
            ItemActivated?.Invoke(item);
            return true;
        }

        if (e.Shift && _anchor >= 0 && _anchor < _items.Count)
        {
            (int from, int to) = _anchor < index ? (_anchor, index) : (index, _anchor);
            Selection.Set(_items.Skip(from).Take(to - from + 1));
        }
        else if (e.Control)
        {
            Selection.Toggle(item);
            _anchor = index;
        }
        else
        {
            if (!Selection.Contains(item))
            {
                Selection.Set(item);
            }

            _anchor = index;
        }

        _pressed = item;
        _pressPoint = e.Position;
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (_scroll.FollowGrab(Theme, Bounds, e.Position))
        {
            return;
        }

        // A press that travels becomes a drag of the item to another widget.
        if (_pressed is { } pressed && IsCapturing && UiVec2.Distance(e.Position, _pressPoint) > Theme.Px(6f))
        {
            _pressed = null;
            if (_source.Drag?.Invoke(pressed) is { } drag && Root is EditorRoot root)
            {
                root.BeginDragDrop(drag);
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        _scroll.ReleaseGrab();

        // A plain click on one of several selected items narrows the selection to it, once it is clear no drag was meant.
        if (_pressed is { } pressed && !e.Control && !e.Shift && Selection.Count > 1)
        {
            Selection.Set(pressed);
        }

        _pressed = null;
    }

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            TileSize = Math.Clamp(TileSize + (notches * 12f), MinTile, MaxTile);
            return true;
        }

        return _scroll.Wheel(notches, TileHeight);
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        int current = Selection.Primary is { } primary ? IndexOf(primary) : -1;
        int next = e.Key switch
        {
            KeyCode.Left => current - 1,
            KeyCode.Right => current + 1,
            KeyCode.Up => current - Columns,
            KeyCode.Down => current < 0 ? 0 : current + Columns,
            KeyCode.Home => 0,
            KeyCode.End => _items.Count - 1,
            _ => int.MinValue,
        };
        if (e.Key is KeyCode.Return or KeyCode.NumpadEnter && current >= 0)
        {
            ItemActivated?.Invoke(_items[current]);
            return true;
        }

        if (next == int.MinValue)
        {
            return false;
        }

        next = Math.Clamp(next, 0, _items.Count - 1);
        Selection.Set(_items[next]);
        _anchor = next;
        Reveal(_items[next]);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Panel);
        if (_items.Count == 0)
        {
            float width = Theme.Font.MeasureWidth(EmptyText, Theme.TextSize);
            EditorDraw.Label(painter, Theme, EmptyText, new UiRect(Bounds.Center.X - (width * 0.5f), Bounds.Y + Theme.Px(24f), width + 1f, Theme.RowHeight), Theme.TextDisabled);
            return;
        }

        painter.PushClip(Bounds);
        int columns = Columns;
        float rowStep = TileHeight + Gap;
        int firstRow = Math.Max(0, (int)(_scroll.Offset / rowStep));
        int lastRow = (int)((_scroll.Offset + Bounds.Height) / rowStep);
        int hovered = IsPointerOver && Root?.Dragging is null ? IndexAt(Root?.PointerPosition ?? default) : -1;
        bool active = IsFocusWithin;
        for (int index = firstRow * columns; index < Math.Min(_items.Count, (lastRow + 1) * columns); index++)
        {
            DrawTile(painter, index, index == hovered, active);
        }

        painter.PopClip();
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter) =>
        _scroll.DrawVertical(painter, Theme, Bounds, IsPointerOver ? Root?.PointerPosition : null);

    private UiRect TileRect(int index)
    {
        int columns = Columns;
        float x = Bounds.X + Gap + ((index % columns) * (Tile + Gap));
        float y = Bounds.Y + Gap + ((index / columns) * (TileHeight + Gap)) - _scroll.Offset;
        return new UiRect(x, y, Tile, TileHeight);
    }

    private int IndexAt(UiVec2 point)
    {
        if (!Bounds.Contains(point))
        {
            return -1;
        }

        for (int index = 0; index < _items.Count; index++)
        {
            UiRect tile = TileRect(index);
            if (tile.Y > point.Y)
            {
                break;
            }

            if (tile.Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    private int IndexOf(T item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private void DrawTile(UiPainter painter, int index, bool hovered, bool active)
    {
        T item = _items[index];
        UiRect tile = TileRect(index);
        bool selected = Selection.Contains(item);
        if (selected)
        {
            EditorDraw.Fill(painter, Theme, tile, active ? Theme.Selection : Theme.SelectionInactive);
        }
        else if (hovered)
        {
            EditorDraw.Fill(painter, Theme, tile, Theme.Hover);
        }

        // The picture, fitted into the square above the name over a dark backing, or the icon large in its middle.
        UiRect picture = new(tile.X + Theme.Px(4f), tile.Y + Theme.Px(4f), tile.Width - Theme.Px(8f), tile.Width - Theme.Px(8f));
        if (_source.Thumbnail?.Invoke(item) is { } thumbnail)
        {
            EditorDraw.Fill(painter, Theme, picture, Theme.Input);
            float scale = MathF.Min(picture.Width / thumbnail.Width, picture.Height / thumbnail.Height);
            float w = MathF.Round(thumbnail.Width * scale);
            float h = MathF.Round(thumbnail.Height * scale);
            painter.DrawSprite(thumbnail.Image, new UiRect(MathF.Round(picture.Center.X - (w * 0.5f)), MathF.Round(picture.Center.Y - (h * 0.5f)), w, h), new UiRect(0f, 0f, 1f, 1f), Bgra32.White);
        }
        else
        {
            KitIcon icon = _source.Icon?.Invoke(item) ?? KitIcon.Document;
            float size = MathF.Round(picture.Width * 0.42f);
            EditorDraw.Icon(painter, Theme, icon, picture, selected && active ? Theme.Accent : _source.IconTint?.Invoke(item) ?? Theme.TextMuted, size);
        }

        float line = Theme.SmallFont.LineHeight(Theme.SmallTextSize);
        float textWidth = tile.Width - Theme.Px(6f);
        string name = EditorDraw.Fit(Theme.SmallFont, _source.Label(item), Theme.SmallTextSize, textWidth);
        float nameWidth = Theme.SmallFont.MeasureWidth(name, Theme.SmallTextSize);
        painter.DrawText(Theme.SmallFont, name, new UiVec2(MathF.Round(tile.Center.X - (nameWidth * 0.5f)), MathF.Round(picture.Bottom + Theme.Px(3f))), Theme.SmallTextSize, Theme.Text);
        if (_source.Detail?.Invoke(item) is { Length: > 0 } detail)
        {
            string shown = EditorDraw.Fit(Theme.SmallFont, detail, Theme.SmallTextSize, textWidth);
            float detailWidth = Theme.SmallFont.MeasureWidth(shown, Theme.SmallTextSize);
            painter.DrawText(Theme.SmallFont, shown, new UiVec2(MathF.Round(tile.Center.X - (detailWidth * 0.5f)), MathF.Round(picture.Bottom + Theme.Px(3f) + line)), Theme.SmallTextSize, Theme.TextDisabled);
        }
    }
}
