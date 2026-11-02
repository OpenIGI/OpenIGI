using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>One entry of a menu: an action, a command, a submenu or a separator.</summary>
public sealed class MenuItem
{
    private MenuItem(string title)
    {
        Title = title;
    }

    /// <summary>Gets the title, used when no action names the item.</summary>
    public string Title { get; }

    /// <summary>Gets the action the item runs and takes its title, shortcut, state and icon from.</summary>
    public EditorAction? Action { get; private init; }

    /// <summary>Gets the command a plain item runs.</summary>
    public System.Action? Execute { get; private init; }

    /// <summary>Gets the submenu's items, or <see langword="null"/>.</summary>
    public IReadOnlyList<MenuItem>? Children { get; private init; }

    /// <summary>Gets whether the item is a separator line.</summary>
    public bool IsSeparator { get; private init; }

    /// <summary>Gets or sets the icon of a plain item.</summary>
    public KitIcon Icon { get; init; }

    /// <summary>Gets or sets whether a plain item is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets a separator.</summary>
    public static MenuItem Separator { get; } = new(string.Empty) { IsSeparator = true };

    /// <summary>Gets the title to show.</summary>
    public string ShownTitle => Action?.CurrentTitle ?? Title;

    /// <summary>Gets the shortcut text to show.</summary>
    public string? ShortcutText => Action?.Shortcut?.ToString();

    /// <summary>Gets whether the item can be chosen now.</summary>
    public bool IsEnabled => !IsSeparator && (Action?.IsEnabled ?? Enabled) && (Children is null || Children.Count > 0);

    /// <summary>Gets whether the item shows a tick.</summary>
    public bool IsChecked => Action?.Checked ?? false;

    /// <summary>Gets the icon to show.</summary>
    public KitIcon ShownIcon => Icon;

    /// <summary>Creates an item for an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The item.</returns>
    public static MenuItem For(EditorAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new MenuItem(action.Title) { Action = action };
    }

    /// <summary>Creates a plain item.</summary>
    /// <param name="title">The title.</param>
    /// <param name="execute">What it does.</param>
    /// <returns>The item.</returns>
    public static MenuItem Command(string title, System.Action execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return new MenuItem(title) { Execute = execute };
    }

    /// <summary>Creates a submenu.</summary>
    /// <param name="title">The title.</param>
    /// <param name="children">Its items.</param>
    /// <returns>The item.</returns>
    public static MenuItem Submenu(string title, IEnumerable<MenuItem> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return new MenuItem(title) { Children = [.. children] };
    }

    /// <summary>Runs the item.</summary>
    public void Invoke()
    {
        if (Action is { } action)
        {
            action.TryExecute();
        }
        else
        {
            Execute?.Invoke();
        }
    }
}

/// <summary>A popup list of menu items, with submenus to the side and full keyboard navigation.</summary>
public sealed class MenuPopup : Popup
{
    private readonly IReadOnlyList<MenuItem> _items;
    private MenuPopup? _submenu;
    private int _highlight = -1;

    /// <summary>Initializes a new instance of the <see cref="MenuPopup"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="items">The items.</param>
    public MenuPopup(EditorTheme theme, IReadOnlyList<MenuItem> items)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    /// <summary>Gets the items.</summary>
    public IReadOnlyList<MenuItem> Items => _items;

    /// <summary>Gets the highlighted index, or -1.</summary>
    public int Highlight => _highlight;

    private float Pad => Theme.Px(4f);

    private float SeparatorHeight => Theme.Px(9f);

    /// <summary>Opens a menu at the pointer: a context menu.</summary>
    /// <param name="root">The root to open it in.</param>
    /// <param name="at">Where to open it.</param>
    /// <param name="items">The items.</param>
    /// <returns>The menu.</returns>
    public static MenuPopup OpenAt(EditorRoot root, UiVec2 at, IReadOnlyList<MenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(root);
        MenuPopup menu = new(root.Theme, items) { Anchor = new UiRect(at.X, at.Y, 0f, 0f), Placement = PopupPlacement.At };
        root.CloseAllPopups();
        root.ShowPopup(menu);
        return menu;
    }

    /// <summary>Gets the item index at a point, or -1 for none (a separator or outside).</summary>
    /// <param name="point">The point.</param>
    /// <returns>The index.</returns>
    public int IndexAt(UiVec2 point)
    {
        if (!Bounds.Contains(point))
        {
            return -1;
        }

        float y = Bounds.Y + Pad;
        for (int i = 0; i < _items.Count; i++)
        {
            float height = RowHeightOf(i);
            if (point.Y < y + height)
            {
                return _items[i].IsSeparator ? -1 : i;
            }

            y += height;
        }

        return -1;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float titles = 0f;
        float shortcuts = 0f;
        float height = Pad * 2f;
        for (int i = 0; i < _items.Count; i++)
        {
            MenuItem item = _items[i];
            height += RowHeightOf(i);
            if (!item.IsSeparator)
            {
                titles = MathF.Max(titles, Theme.Font.MeasureWidth(item.ShownTitle, Theme.TextSize));
                shortcuts = MathF.Max(shortcuts, item.ShortcutText is { } s ? Theme.SmallFont.MeasureWidth(s, Theme.SmallTextSize) : 0f);
            }
        }

        float width = Theme.Px(28f) + titles + Theme.Px(32f) + shortcuts + Theme.Px(20f);
        return new UiVec2(MathF.Max(width, Theme.Px(180f)), height);
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        int index = IndexAt(e.Position);
        if (index >= 0 && index != _highlight)
        {
            SetHighlight(index, openSubmenu: true);
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e) => true;

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        int index = IndexAt(e.Position);
        if (index >= 0)
        {
            Choose(index);
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        // The deepest open submenu owns the keyboard.
        if (_submenu is { Highlight: >= 0 } open && Root?.Popups.Contains(open) == true)
        {
            return open.OnKeyDown(e);
        }

        switch (e.Key)
        {
            case KeyCode.Down:
                SetHighlight(Step(1), openSubmenu: false);
                return true;
            case KeyCode.Up:
                SetHighlight(Step(-1), openSubmenu: false);
                return true;
            case KeyCode.Right when _highlight >= 0 && _items[_highlight].Children is not null:
                OpenSubmenu(_highlight)?.SetHighlight(0, openSubmenu: false);
                return true;
            case KeyCode.Left when Owner is MenuPopup:
                Close();
                return true;
            case KeyCode.Return or KeyCode.Space when _highlight >= 0:
                Choose(_highlight);
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        base.OnDraw(painter);
        float y = Bounds.Y + Pad;
        for (int i = 0; i < _items.Count; i++)
        {
            MenuItem item = _items[i];
            float height = RowHeightOf(i);
            UiRect row = new(Bounds.X + Pad, y, Bounds.Width - (Pad * 2f), height);
            y += height;

            if (item.IsSeparator)
            {
                painter.FillRect(new UiRect(row.X + Theme.Px(8f), MathF.Round(row.Center.Y), row.Width - Theme.Px(16f), Theme.Hairline), Theme.Border);
                continue;
            }

            bool enabled = item.IsEnabled;
            if (i == _highlight && enabled)
            {
                EditorDraw.Fill(painter, Theme, row, Theme.Selection);
            }

            Bgra32 ink = enabled ? Theme.Text : Theme.TextDisabled;
            UiRect gutter = new(row.X, row.Y, Theme.Px(24f), row.Height);
            if (item.IsChecked)
            {
                KitIcons.Draw(painter, KitIcon.Check, gutter.Center, Theme.Px(12f), Theme.Accent);
            }
            else
            {
                EditorDraw.Icon(painter, Theme, item.ShownIcon, gutter, enabled ? Theme.Icon : Theme.TextDisabled);
            }

            EditorDraw.Label(painter, Theme, item.ShownTitle, new UiRect(gutter.Right, row.Y, row.Width - gutter.Width, row.Height), ink);
            if (item.Children is not null)
            {
                painter.DrawChevron(new UiVec2(row.Right - Theme.Px(10f), row.Center.Y), Theme.Px(4f), UiDirection.Right, Theme.Hairline * 1.5f, ink);
            }
            else if (item.ShortcutText is { } shortcut)
            {
                float width = Theme.SmallFont.MeasureWidth(shortcut, Theme.SmallTextSize);
                float ty = MathF.Round(row.Y + ((row.Height - Theme.SmallFont.LineHeight(Theme.SmallTextSize)) * 0.5f));
                painter.DrawText(Theme.SmallFont, shortcut, new UiVec2(row.Right - Theme.Px(8f) - width, ty), Theme.SmallTextSize, Theme.TextMuted);
            }
        }
    }

    private float RowHeightOf(int index) => _items[index].IsSeparator ? SeparatorHeight : Theme.RowHeight;

    private UiRect RowRect(int index)
    {
        float y = Bounds.Y + Pad;
        for (int i = 0; i < index; i++)
        {
            y += RowHeightOf(i);
        }

        return new UiRect(Bounds.X, y, Bounds.Width, RowHeightOf(index));
    }

    private int Step(int direction)
    {
        for (int n = 1; n <= _items.Count; n++)
        {
            int index = (((_highlight < 0 && direction < 0 ? 0 : _highlight) + (direction * n)) % _items.Count + _items.Count) % _items.Count;
            if (_items[index].IsEnabled)
            {
                return index;
            }
        }

        return -1;
    }

    private void SetHighlight(int index, bool openSubmenu)
    {
        _highlight = index;
        if (_submenu is { } previous && (index < 0 || !ReferenceEquals(previous.Items, _items[index].Children)))
        {
            previous.Close();
            _submenu = null;
        }

        if (openSubmenu && index >= 0)
        {
            OpenSubmenu(index);
        }
    }

    private MenuPopup? OpenSubmenu(int index)
    {
        if (_items[index].Children is not { } children || !_items[index].IsEnabled || Root is not { } root)
        {
            return null;
        }

        if (_submenu is not null && ReferenceEquals(_submenu.Items, children) && root.Popups.Contains(_submenu))
        {
            return _submenu;
        }

        _submenu = new MenuPopup(Theme, children) { Owner = this, Anchor = RowRect(index), Placement = PopupPlacement.Right };
        root.ShowPopup(_submenu);
        return _submenu;
    }

    private void Choose(int index)
    {
        MenuItem item = _items[index];
        if (!item.IsEnabled)
        {
            return;
        }

        if (item.Children is not null)
        {
            OpenSubmenu(index)?.SetHighlight(0, openSubmenu: false);
            return;
        }

        // The whole chain closes first, so a command that opens a dialog opens it on a clear screen.
        Root?.CloseAllPopups();
        item.Invoke();
    }
}

/// <summary>The menu bar along the top of the editor window.</summary>
public sealed class MenuBar : EditorElement
{
    private readonly List<(string Title, IReadOnlyList<MenuItem> Items)> _menus = [];
    private MenuPopup? _open;
    private int _openIndex = -1;

    /// <summary>Initializes a new instance of the <see cref="MenuBar"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public MenuBar(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Gets or sets text drawn at the bar's right end: the document's name and its dirty mark.</summary>
    public string? Caption { get; set; }

    /// <summary>Adds a top-level menu.</summary>
    /// <param name="title">The title.</param>
    /// <param name="items">Its items.</param>
    /// <returns>This bar, for chaining.</returns>
    public MenuBar Add(string title, IReadOnlyList<MenuItem> items)
    {
        _menus.Add((title, items));
        return this;
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(float.IsFinite(available.X) ? available.X : 0f, Theme.MenuBarHeight);

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        int index = TitleAt(e.Position);
        if (index < 0 || e.Button != MouseButton.Left)
        {
            return false;
        }

        Open(index);
        return false;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        // With a menu open, sliding along the bar opens its neighbours, as every desktop menu bar does.
        int index = TitleAt(e.Position);
        if (_open is not null && Root?.Popups.Contains(_open) == true && index >= 0 && index != _openIndex)
        {
            Open(index);
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.FillRect(Bounds, Theme.Chrome);
        painter.FillRect(new UiRect(Bounds.X, Bounds.Bottom - Theme.Hairline, Bounds.Width, Theme.Hairline), Theme.Border);
        bool anyOpen = _open is not null && Root?.Popups.Contains(_open) == true;
        for (int i = 0; i < _menus.Count; i++)
        {
            UiRect title = TitleRect(i);
            if (anyOpen && i == _openIndex)
            {
                EditorDraw.Fill(painter, Theme, title.Deflate(new Thickness(0f, Theme.Px(3f))), Theme.Selection);
            }
            else if (IsPointerOver && title.Contains(Root?.PointerPosition ?? default))
            {
                EditorDraw.Fill(painter, Theme, title.Deflate(new Thickness(0f, Theme.Px(3f))), Theme.Hover);
            }

            EditorDraw.Label(painter, Theme, _menus[i].Title, title.Deflate(new Thickness(Theme.Px(9f), 0f)), Theme.Text);
        }

        if (Caption is { } caption)
        {
            float width = Theme.Font.MeasureWidth(caption, Theme.TextSize);
            EditorDraw.Label(painter, Theme, caption, new UiRect(Bounds.Right - width - Theme.Padding, Bounds.Y, width, Bounds.Height), Theme.TextMuted);
        }
    }

    private void Open(int index)
    {
        if (Root is not { } root)
        {
            return;
        }

        root.CloseAllPopups();
        _openIndex = index;
        UiRect title = TitleRect(index);
        _open = new MenuPopup(Theme, _menus[index].Items) { Anchor = new UiRect(title.X, Bounds.Bottom, title.Width, 0f) };
        root.ShowPopup(_open);
    }

    private UiRect TitleRect(int index)
    {
        float x = Bounds.X + Theme.Px(4f);
        for (int i = 0; i < index; i++)
        {
            x += TitleWidth(i);
        }

        return new UiRect(x, Bounds.Y, TitleWidth(index), Bounds.Height);
    }

    // A pixel of slack: the label is fitted to exactly its measured width, and rounding would otherwise ellipsise it.
    private float TitleWidth(int index) => MathF.Ceiling(Theme.Font.MeasureWidth(_menus[index].Title, Theme.TextSize)) + Theme.Px(18f) + 1f;

    private int TitleAt(UiVec2 point)
    {
        for (int i = 0; i < _menus.Count; i++)
        {
            if (TitleRect(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }
}
