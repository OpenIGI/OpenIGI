using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Desktop;

/// <summary>One line of a context menu.</summary>
/// <param name="Label">What the reader sees.</param>
/// <param name="Invoke">What choosing it does.</param>
/// <param name="IsEnabled">Whether it can be chosen; a disabled item is shown greyed rather than hidden.</param>
public readonly record struct ContextMenuItem(string Label, Action Invoke, bool IsEnabled = true);

/// <summary>
/// The small list that opens under a right click.
/// </summary>
/// <remarks>
/// <para>
/// Its own thing rather than a window, because a menu is not something you drag, resize, raise or
/// close by a button — it exists until something is chosen or the pointer goes elsewhere. Modelling it
/// as a <see cref="OpenIGI.UI.Widgets.UiWindow"/> would have meant switching most of that off.
/// </para>
/// <para>
/// Disabled items are drawn greyed rather than omitted, so the menu keeps the same shape and the same
/// item stays under the same place on screen. A menu whose entries move depending on what is selected
/// cannot be used without reading it every time.
/// </para>
/// </remarks>
public sealed class ContextMenu
{
    private readonly List<ContextMenuItem> _items = [];

    /// <summary>Gets a value indicating whether the menu is showing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets the menu's rectangle while it is showing.</summary>
    public UiRect Bounds { get; private set; }

    /// <summary>Gets the items currently offered.</summary>
    public IReadOnlyList<ContextMenuItem> Items => _items;

    /// <summary>Gets or sets the item under the pointer, or -1 for none.</summary>
    public int HoveredIndex { get; set; } = -1;

    /// <summary>Opens the menu at a point with a set of items.</summary>
    /// <param name="at">Where the pointer was, in back-buffer pixels.</param>
    /// <param name="items">What to offer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public void Open(UiVec2 at, IEnumerable<ContextMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
        Anchor = at;
        HoveredIndex = -1;
        IsOpen = _items.Count > 0;
    }

    /// <summary>Closes the menu.</summary>
    public void Close()
    {
        IsOpen = false;
        HoveredIndex = -1;
        _items.Clear();
    }

    /// <summary>Gets where the menu was opened.</summary>
    public UiVec2 Anchor { get; private set; }

    /// <summary>Returns the item under a point, or -1.</summary>
    /// <param name="point">The point in back-buffer pixels.</param>
    /// <param name="rowHeight">How tall one item is.</param>
    /// <returns>The index into <see cref="Items"/>, or -1.</returns>
    public int ItemAt(UiVec2 point, float rowHeight)
    {
        if (!IsOpen || Bounds.IsEmpty || !Bounds.Contains(point))
        {
            return -1;
        }

        int index = (int)((point.Y - Bounds.Y - 1f) / MathF.Max(1f, rowHeight));
        return index >= 0 && index < _items.Count ? index : -1;
    }

    /// <summary>Runs an item and closes the menu.</summary>
    /// <param name="index">The item to run.</param>
    /// <returns><see langword="true"/> when an enabled item ran.</returns>
    public bool Invoke(int index)
    {
        if (index < 0 || index >= _items.Count || !_items[index].IsEnabled)
        {
            return false;
        }

        Action action = _items[index].Invoke;
        Close();
        action();
        return true;
    }

    /// <summary>Draws the menu over everything else.</summary>
    /// <param name="painter">The painter to draw into.</param>
    /// <param name="theme">The palette and type scale.</param>
    /// <param name="viewport">The back buffer's size, so the menu can stay on screen.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Flipped rather than clamped when it would run off an edge: a menu opened near the bottom of the
    /// screen appears above the pointer instead of being pushed up over it, which is what every
    /// desktop does and what keeps the pointer beside the menu rather than on top of it.
    /// </remarks>
    public void Draw(UiPainter painter, DevConsoleTheme theme, UiVec2 viewport)
    {
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(theme);

        if (!IsOpen || _items.Count == 0)
        {
            Bounds = UiRect.Empty;
            return;
        }

        float rowHeight = theme.RowHeight;
        float width = MathF.Round(theme.Cell * 18f);
        float height = MathF.Round((rowHeight * _items.Count) + 2f);

        float x = Anchor.X + width > viewport.X ? MathF.Max(0f, Anchor.X - width) : Anchor.X;
        float y = Anchor.Y + height > viewport.Y ? MathF.Max(0f, Anchor.Y - height) : Anchor.Y;

        var rect = new UiRect(MathF.Round(x), MathF.Round(y), width, height);
        Bounds = rect;

        painter.FillRect(rect, theme.Ui.SurfaceRaised);
        painter.StrokeRect(rect, 1f, theme.Ui.Border);

        for (int i = 0; i < _items.Count; i++)
        {
            ContextMenuItem item = _items[i];
            var row = new UiRect(rect.X + 1f, MathF.Round(rect.Y + 1f + (i * rowHeight)), rect.Width - 2f, rowHeight);

            if (i == HoveredIndex && item.IsEnabled)
            {
                painter.FillRect(row, theme.Ui.Selection);
            }

            painter.DrawText(
                theme.BodyFont,
                item.Label,
                new UiVec2(
                    MathF.Round(row.X + (theme.Unit * 2f)),
                    MathF.Round(row.Y + ((row.Height - theme.BodyFont.LineHeight(theme.BodySize)) * 0.5f))),
                theme.BodySize,
                item.IsEnabled ? theme.Ui.Text : theme.Ui.TextDisabled);
        }
    }
}
