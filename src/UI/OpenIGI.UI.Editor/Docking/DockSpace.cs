using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Docking;

/// <summary>Where a dragged tab will dock.</summary>
/// <param name="Target">The tab group it lands on.</param>
/// <param name="Side">
/// <see langword="null"/> to join the group as a tab; otherwise the edge it splits from: left, right, top, bottom.
/// </param>
/// <param name="TabIndex">For joining, the tab position to insert at.</param>
public readonly record struct DockDrop(DockTabs Target, DockSide? Side, int TabIndex);

/// <summary>An edge of a tab group a panel can split from.</summary>
public enum DockSide
{
    /// <summary>The left edge.</summary>
    Left,

    /// <summary>The right edge.</summary>
    Right,

    /// <summary>The top edge.</summary>
    Top,

    /// <summary>The bottom edge.</summary>
    Bottom,
}

/// <summary>
/// The editor's window body: panels in tab groups, split side by side and stacked, with dividers to drag, tabs
/// to reorder, close and drag out onto any group's centre or edges to re-dock.
/// </summary>
public sealed class DockSpace : EditorElement
{
    private readonly Dictionary<string, DockPanel> _panels = new(StringComparer.Ordinal);
    private DockNode? _root;
    private (DockSplit Split, int Index, float From, float RatioA, float RatioB)? _divider;
    private (DockTabs Group, DockPanel Panel, UiVec2 From)? _tabPress;
    private bool _draggingTab;
    private DockDrop? _drop;

    /// <summary>Initializes a new instance of the <see cref="DockSpace"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public DockSpace(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised whenever the user changes the layout, so it can be saved.</summary>
    public event Action? LayoutChanged;

    /// <summary>Raised when a panel's tab is closed.</summary>
    public event Action<DockPanel>? PanelClosed;

    /// <summary>Gets the registered panels by id.</summary>
    public IReadOnlyDictionary<string, DockPanel> Panels => _panels;

    /// <summary>Gets or sets the layout's root node.</summary>
    public DockNode? RootNode
    {
        get => _root;
        set
        {
            _root = DockLayoutText.Prune(value);
            if (_root is not null)
            {
                _root.Parent = null;
            }
        }
    }

    /// <inheritdoc/>
    public override EditorCursor Cursor
    {
        get
        {
            if (_draggingTab)
            {
                return EditorCursor.Move;
            }

            (DockSplit Split, int Index)? divider = _divider is { } held ? (held.Split, held.Index) : DividerAt(Root?.PointerPosition ?? default);
            return divider is { } d
                ? d.Split.Orientation == Orientation.Horizontal ? EditorCursor.ResizeHorizontal : EditorCursor.ResizeVertical
                : EditorCursor.Arrow;
        }
    }

    /// <summary>Gets the drop the current tab drag would make, for tests and drawing.</summary>
    public DockDrop? PendingDrop => _drop;

    /// <summary>Registers a panel so layouts can name it.</summary>
    /// <param name="panel">The panel.</param>
    public void Register(DockPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panels.Add(panel.Id, panel);
        panel.Content.Visibility = Visibility.Collapsed;
        AddChild(panel.Content);
    }

    /// <summary>Writes the layout as text.</summary>
    /// <returns>The text, or empty when nothing is docked.</returns>
    public string SaveLayout() => _root is null ? string.Empty : DockLayoutText.Write(_root);

    /// <summary>Reads a layout written by <see cref="SaveLayout"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it was understood.</returns>
    public bool LoadLayout(string text)
    {
        if (DockLayoutText.Read(text ?? string.Empty, _panels) is not { } node)
        {
            return false;
        }

        RootNode = node;
        return true;
    }

    /// <summary>Finds the group a panel is docked in.</summary>
    /// <param name="id">The panel id.</param>
    /// <returns>The group, or <see langword="null"/> when the panel is closed.</returns>
    public DockTabs? GroupOf(string id) => Groups(_root).FirstOrDefault(g => g.Panels.Exists(p => p.Id == id));

    /// <summary>Gets whether a panel is docked.</summary>
    /// <param name="id">The panel id.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsOpen(string id) => GroupOf(id) is not null;

    /// <summary>Shows a panel: brings its tab forward, or docks it into the largest group when it is closed.</summary>
    /// <param name="id">The panel id.</param>
    /// <param name="beside">A panel whose tab group a closed panel opens in, when that one is open; otherwise the largest group.</param>
    public void Show(string id, string? beside = null)
    {
        DockPanel panel = _panels[id];
        if (GroupOf(id) is { } group)
        {
            group.Active = group.Panels.IndexOf(panel);
            return;
        }

        DockTabs target = (beside is not null ? GroupOf(beside) : null)
            ?? Groups(_root).MaxBy(static g => g.Bounds.Width * g.Bounds.Height)
            ?? new DockTabs();
        _root ??= target;
        target.Panels.Add(panel);
        target.Active = target.Panels.Count - 1;
        LayoutChanged?.Invoke();
    }

    /// <summary>Closes a panel's tab.</summary>
    /// <param name="id">The panel id.</param>
    public void Close(string id)
    {
        if (GroupOf(id) is not { } group)
        {
            return;
        }

        DockPanel panel = _panels[id];
        group.Panels.Remove(panel);
        RootNode = _root;
        PanelClosed?.Invoke(panel);
        LayoutChanged?.Invoke();
    }

    /// <summary>Moves a panel as a drop would.</summary>
    /// <param name="panel">The panel.</param>
    /// <param name="drop">Where it goes.</param>
    public void Dock(DockPanel panel, DockDrop drop)
    {
        ArgumentNullException.ThrowIfNull(panel);
        DockTabs? source = GroupOf(panel.Id);
        if (drop.Side is null)
        {
            int index = drop.TabIndex;
            if (ReferenceEquals(source, drop.Target) && source!.Panels.IndexOf(panel) < index)
            {
                index--;
            }

            source?.Panels.Remove(panel);
            drop.Target.Panels.Insert(Math.Clamp(index, 0, drop.Target.Panels.Count), panel);
            drop.Target.Active = drop.Target.Panels.IndexOf(panel);
        }
        else
        {
            if (ReferenceEquals(source, drop.Target) && source!.Panels.Count == 1)
            {
                return;
            }

            source?.Panels.Remove(panel);
            DockTabs added = new();
            added.Panels.Add(panel);
            SplitBeside(drop.Target, added, drop.Side.Value);
        }

        RootNode = _root;
        LayoutChanged?.Invoke();
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        foreach (DockPanel panel in _panels.Values)
        {
            panel.Content.Visibility = Visibility.Collapsed;
        }

        if (_root is not null)
        {
            ArrangeNode(context, _root, contentRect);
        }
    }

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        if (DividerAt(e.Position) is { } divider && e.Button == MouseButton.Left)
        {
            DockSplit split = divider.Split;
            float from = split.Orientation == Orientation.Horizontal ? e.Position.X : e.Position.Y;
            _divider = (split, divider.Index, from, split.Ratios[divider.Index], split.Ratios[divider.Index + 1]);
            return true;
        }

        if (TabAt(e.Position) is not { } tab)
        {
            return false;
        }

        if (e.Button == MouseButton.Middle || (e.Button == MouseButton.Left && tab.Panel.CanClose && CloseRect(tab.Rect).Contains(e.Position) && IsTabHot(tab)))
        {
            Close(tab.Panel.Id);
            return false;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        tab.Group.Active = tab.Group.Panels.IndexOf(tab.Panel);
        _tabPress = (tab.Group, tab.Panel, e.Position);
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e)
    {
        if (_divider is { } held)
        {
            DockNode a = held.Split.Children[held.Index];
            DockNode b = held.Split.Children[held.Index + 1];
            bool horizontal = held.Split.Orientation == Orientation.Horizontal;
            float span = (horizontal ? a.Bounds.Width + b.Bounds.Width : a.Bounds.Height + b.Bounds.Height) / (held.RatioA + held.RatioB);
            float delta = ((horizontal ? e.Position.X : e.Position.Y) - held.From) / MathF.Max(1f, span);
            float minimum = Theme.Px(80f) / MathF.Max(1f, span);
            float ratioA = Math.Clamp(held.RatioA + delta, minimum, held.RatioA + held.RatioB - minimum);
            held.Split.Ratios[held.Index] = ratioA;
            held.Split.Ratios[held.Index + 1] = held.RatioA + held.RatioB - ratioA;
            return;
        }

        if (_tabPress is not { } press)
        {
            return;
        }

        if (!_draggingTab && MathF.Abs(e.Position.X - press.From.X) + MathF.Abs(e.Position.Y - press.From.Y) > Theme.Px(8f))
        {
            _draggingTab = true;
        }

        if (_draggingTab)
        {
            _drop = DropAt(e.Position);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnPointerUp(in PointerEvent e)
    {
        if (_divider is not null)
        {
            _divider = null;
            LayoutChanged?.Invoke();
        }

        if (_draggingTab && _tabPress is { } press && _drop is { } drop)
        {
            Dock(press.Panel, drop);
        }

        OnCaptureLost();
    }

    /// <inheritdoc/>
    protected internal override void OnCaptureLost()
    {
        _divider = null;
        _tabPress = null;
        _draggingTab = false;
        _drop = null;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // Only the gaps between groups get the background, never the whole space: a transparent panel's body has to
        // stay unpainted for what is drawn beneath to show through.
        if (_root is null)
        {
            painter.FillRect(Bounds, Theme.Background);
            return;
        }

        foreach (DockSplit split in Splits(_root))
        {
            bool horizontal = split.Orientation == Orientation.Horizontal;
            for (int i = 0; i < split.Children.Count - 1; i++)
            {
                UiRect a = split.Children[i].Bounds;
                painter.FillRect(
                    horizontal
                        ? new UiRect(a.Right, a.Y, Theme.SplitterSize, a.Height)
                        : new UiRect(a.X, a.Bottom, a.Width, Theme.SplitterSize),
                    Theme.Background);
            }
        }

        foreach (DockTabs group in Groups(_root))
        {
            DrawGroup(painter, group);
        }
    }

    /// <inheritdoc/>
    protected override void OnDrawOverlay(UiPainter painter)
    {
        if (!_draggingTab || _tabPress is not { } press)
        {
            return;
        }

        if (_drop is { } drop)
        {
            UiRect area = drop.Side is { } side ? SideRect(drop.Target.Bounds, side) : drop.Target.Bounds;
            EditorDraw.Fill(painter, Theme, area.Deflate(new Thickness(Theme.Px(2f))), Theme.Accent.WithAlpha(56));
            EditorDraw.Frame(painter, Theme, area.Deflate(new Thickness(Theme.Px(2f))), Bgra32.Transparent, Theme.Accent);
        }

        // The tab follows the pointer, so the user can see what they are carrying.
        UiVec2 at = Root?.PointerPosition ?? default;
        float width = TabWidth(press.Panel);
        UiRect ghost = new(at.X - (width * 0.5f), at.Y - (Theme.TabHeight * 0.5f), width, Theme.TabHeight);
        EditorDraw.Shadow(painter, Theme, ghost);
        EditorDraw.Frame(painter, Theme, ghost, Theme.Popup, Theme.Accent);
        DrawTabLabel(painter, press.Panel, ghost, active: true);
    }

    private static IEnumerable<DockTabs> Groups(DockNode? node)
    {
        switch (node)
        {
            case DockTabs tabs:
                yield return tabs;
                break;
            case DockSplit split:
                foreach (DockTabs tabs in split.Children.SelectMany(Groups))
                {
                    yield return tabs;
                }

                break;
        }
    }

    private static IEnumerable<DockSplit> Splits(DockNode? node) =>
        node is DockSplit split ? split.Children.SelectMany(Splits).Prepend(split) : [];

    private static UiRect SideRect(UiRect area, DockSide side) => side switch
    {
        DockSide.Left => new UiRect(area.X, area.Y, area.Width * 0.5f, area.Height),
        DockSide.Right => new UiRect(area.X + (area.Width * 0.5f), area.Y, area.Width * 0.5f, area.Height),
        DockSide.Top => new UiRect(area.X, area.Y, area.Width, area.Height * 0.5f),
        _ => new UiRect(area.X, area.Y + (area.Height * 0.5f), area.Width, area.Height * 0.5f),
    };

    private void ArrangeNode(LayoutContext context, DockNode node, UiRect rect)
    {
        node.Bounds = rect;
        if (node is DockTabs tabs)
        {
            if (tabs.ActivePanel is { } panel)
            {
                UiRect body = new(rect.X, rect.Y + Theme.TabHeight, rect.Width, MathF.Max(0f, rect.Height - Theme.TabHeight));
                panel.Content.Visibility = Visibility.Visible;
                panel.Content.Place(context, body);
            }

            return;
        }

        DockSplit split = (DockSplit)node;
        split.Normalize();
        bool horizontal = split.Orientation == Orientation.Horizontal;
        float gap = Theme.SplitterSize;
        float total = (horizontal ? rect.Width : rect.Height) - (gap * (split.Children.Count - 1));
        float cursor = horizontal ? rect.X : rect.Y;
        for (int i = 0; i < split.Children.Count; i++)
        {
            float length = i == split.Children.Count - 1
                ? (horizontal ? rect.Right : rect.Bottom) - cursor
                : MathF.Round(total * split.Ratios[i]);
            UiRect child = horizontal
                ? new UiRect(cursor, rect.Y, length, rect.Height)
                : new UiRect(rect.X, cursor, rect.Width, length);
            ArrangeNode(context, split.Children[i], child);
            cursor += length + gap;
        }
    }

    private (DockSplit Split, int Index)? DividerAt(UiVec2 point)
    {
        float slop = Theme.Px(2f);
        foreach (DockSplit split in Splits(_root))
        {
            bool horizontal = split.Orientation == Orientation.Horizontal;
            for (int i = 0; i < split.Children.Count - 1; i++)
            {
                UiRect a = split.Children[i].Bounds;
                UiRect gap = horizontal
                    ? new UiRect(a.Right - slop, a.Y, Theme.SplitterSize + (slop * 2f), a.Height)
                    : new UiRect(a.X, a.Bottom - slop, a.Width, Theme.SplitterSize + (slop * 2f));
                if (gap.Contains(point))
                {
                    return (split, i);
                }
            }
        }

        return null;
    }

    private float TabWidth(DockPanel panel) =>
        Theme.Px(14f) + (panel.Icon != KitIcon.None ? Theme.IconSize + Theme.Px(6f) : 0f)
        + Theme.Font.MeasureWidth(panel.Title, Theme.TextSize) + Theme.Px(panel.CanClose ? 32f : 14f);

    private IEnumerable<(DockTabs Group, DockPanel Panel, UiRect Rect)> Tabs(DockTabs group)
    {
        float x = group.Bounds.X;
        foreach (DockPanel panel in group.Panels)
        {
            float width = MathF.Min(TabWidth(panel), MathF.Max(Theme.Px(40f), group.Bounds.Right - x));
            yield return (group, panel, new UiRect(x, group.Bounds.Y, width, Theme.TabHeight));
            x += width;
        }
    }

    private (DockTabs Group, DockPanel Panel, UiRect Rect)? TabAt(UiVec2 point)
    {
        foreach (DockTabs group in Groups(_root))
        {
            foreach ((DockTabs Group, DockPanel Panel, UiRect Rect) tab in Tabs(group))
            {
                if (tab.Rect.Contains(point))
                {
                    return tab;
                }
            }
        }

        return null;
    }

    private UiRect CloseRect(UiRect tab)
    {
        float size = Theme.Px(18f);
        return new UiRect(tab.Right - size - Theme.Px(4f), tab.Y + ((tab.Height - size) * 0.5f), size, size);
    }

    private bool IsTabHot((DockTabs Group, DockPanel Panel, UiRect Rect) tab) =>
        ReferenceEquals(tab.Group.ActivePanel, tab.Panel) || tab.Rect.Contains(Root?.PointerPosition ?? default);

    private DockDrop? DropAt(UiVec2 point)
    {
        foreach (DockTabs group in Groups(_root))
        {
            UiRect area = group.Bounds;
            if (!area.Contains(point))
            {
                continue;
            }

            // Over the tab strip: join at the tab position under the pointer.
            if (point.Y < area.Y + Theme.TabHeight)
            {
                int index = 0;
                foreach ((DockTabs _, DockPanel _, UiRect rect) in Tabs(group))
                {
                    if (point.X > rect.Center.X)
                    {
                        index++;
                    }
                }

                return new DockDrop(group, null, index);
            }

            // The outer quarter of each side splits; the middle joins.
            float fx = (point.X - area.X) / area.Width;
            float fy = (point.Y - area.Y) / area.Height;
            DockSide? side = fx < 0.25f ? DockSide.Left : fx > 0.75f ? DockSide.Right : fy < 0.25f ? DockSide.Top : fy > 0.75f ? DockSide.Bottom : null;
            return new DockDrop(group, side, group.Panels.Count);
        }

        return null;
    }

    private void SplitBeside(DockTabs target, DockTabs added, DockSide side)
    {
        Orientation orientation = side is DockSide.Left or DockSide.Right ? Orientation.Horizontal : Orientation.Vertical;
        bool before = side is DockSide.Left or DockSide.Top;
        DockSplit? parent = target.Parent;
        if (parent is not null && parent.Orientation == orientation)
        {
            // Same direction as the enclosing split: become a sibling and take half the target's share.
            int index = parent.Children.IndexOf(target);
            float share = parent.Ratios[index] * 0.5f;
            parent.Ratios[index] = share;
            parent.Insert(added, share, before ? index : index + 1);
            return;
        }

        DockSplit split = new(orientation);
        if (parent is null)
        {
            _root = split;
        }
        else
        {
            int index = parent.Children.IndexOf(target);
            parent.Children[index] = split;
            split.Parent = parent;
        }

        split.Insert(before ? added : target, 0.5f);
        split.Insert(before ? target : added, 0.5f);
    }

    private void DrawGroup(UiPainter painter, DockTabs group)
    {
        UiRect area = group.Bounds;
        UiRect strip = new(area.X, area.Y, area.Width, Theme.TabHeight);
        if (group.ActivePanel is not { IsTransparent: true })
        {
            painter.FillRect(area, Theme.Panel);
        }

        // The tabs sit on the window's ground; only the shown tab takes the panel's colour, joined to its body.
        painter.FillRect(strip, Theme.Background);
        bool focused = group.ActivePanel?.Content is { } content && Root?.Focused is { } f && f.IsWithin(content);
        float radius = Theme.Radius;
        foreach ((DockTabs _, DockPanel panel, UiRect rect) in Tabs(group))
        {
            bool active = ReferenceEquals(panel, group.ActivePanel);
            bool hovered = rect.Contains(Root?.PointerPosition ?? default) && !_draggingTab;
            UiRect shape = new(rect.X, rect.Y + Theme.Px(3f), rect.Width - Theme.Px(2f), rect.Height - Theme.Px(3f));
            if (active)
            {
                EditorDraw.Fill(painter, Theme, shape, Theme.Panel, radius);
                painter.FillRect(new UiRect(shape.X, shape.Bottom - radius, shape.Width, radius), Theme.Panel);
                if (focused)
                {
                    EditorDraw.Fill(painter, Theme, new UiRect(shape.X + radius, shape.Y, shape.Width - (radius * 2f), Theme.Px(2f)), Theme.Accent, Theme.Px(1f));
                }
            }
            else if (hovered)
            {
                EditorDraw.Fill(painter, Theme, shape, Theme.Panel.WithAlpha(110), radius);
            }

            DrawTabLabel(painter, panel, shape, active || hovered);
            if (panel.CanClose && hovered)
            {
                UiRect close = CloseRect(shape);
                if (close.Contains(Root?.PointerPosition ?? default))
                {
                    EditorDraw.Fill(painter, Theme, close, Theme.Pressed);
                }

                EditorDraw.Icon(painter, Theme, KitIcon.Close, close, Theme.TextMuted, Theme.Px(13f));
            }
        }
    }

    private void DrawTabLabel(UiPainter painter, DockPanel panel, UiRect rect, bool active)
    {
        float x = rect.X + Theme.Px(10f);
        if (panel.Icon != KitIcon.None)
        {
            EditorDraw.Icon(painter, Theme, panel.Icon, new UiRect(x, rect.Y, Theme.IconSize, rect.Height), active ? Theme.Icon : Theme.TextDisabled);
            x += Theme.IconSize + Theme.Px(6f);
        }

        float right = rect.Right - Theme.Px(panel.CanClose ? 24f : 10f);
        EditorDraw.Label(painter, Theme, panel.Title, new UiRect(x, rect.Y, right - x, rect.Height), active ? Theme.Text : Theme.TextMuted);
    }
}
