using OpenIGI.Core.Collections;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Docking;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>Docking, the tree view and the property grid, driven like a user.</summary>
public sealed class EditorWidgetTests
{
    // ---- Docking ----

    private static (EditorHarness Ui, DockSpace Dock) Docked(string layout)
    {
        EditorHarness ui = new();
        DockSpace dock = new(ui.Theme);
        foreach (string id in new[] { "outliner", "assets", "viewport", "inspector", "history" })
        {
            dock.Register(new DockPanel(id, id, new PropertyGrid(ui.Theme)));
        }

        Assert.True(dock.LoadLayout(layout));
        ui.Root.Content = dock;
        ui.Frame();
        return (ui, dock);
    }

    /// <summary>A layout writes back as it was read, and ids the editor no longer has are dropped.</summary>
    [Fact]
    public void A_layout_round_trips_and_forgets_unknown_panels()
    {
        const string layout = "H(0.2:T(outliner|assets*),0.6:T(viewport*),0.2:V(0.6:T(inspector*),0.4:T(history*)))";
        (_, DockSpace dock) = Docked(layout);
        Assert.Equal(layout, dock.SaveLayout());

        (_, DockSpace pruned) = Docked("H(0.5:T(outliner*|gone),0.5:T(ghost))");
        Assert.Equal("T(outliner*)", pruned.SaveLayout());
    }

    /// <summary>Dragging the divider between two groups moves the split.</summary>
    [Fact]
    public void Dragging_a_divider_resizes()
    {
        (EditorHarness ui, DockSpace dock) = Docked("H(0.5:T(outliner*),0.5:T(viewport*))");
        DockTabs left = dock.GroupOf("outliner")!;
        float before = left.Bounds.Width;
        UiVec2 divider = new(left.Bounds.Right + (ui.Theme.SplitterSize * 0.5f), left.Bounds.Center.Y);

        ui.Drag(divider, new UiVec2(divider.X + 100f, divider.Y));
        Assert.InRange(dock.GroupOf("outliner")!.Bounds.Width, before + 95f, before + 105f);
    }

    /// <summary>A tab dragged onto another group's right edge splits it; dragged onto a strip it joins as a tab.</summary>
    [Fact]
    public void Dragging_a_tab_redocks_it()
    {
        (EditorHarness ui, DockSpace dock) = Docked("H(0.5:T(outliner*|assets),0.5:T(viewport*))");
        DockTabs viewport = dock.GroupOf("viewport")!;
        UiVec2 assetsTab = TabCenter(ui, dock, "assets");

        ui.Drag(assetsTab, new UiVec2(viewport.Bounds.Right - 10f, viewport.Bounds.Center.Y));
        Assert.Equal("H(0.5:T(outliner*),0.25:T(viewport*),0.25:T(assets*))", dock.SaveLayout());

        DockTabs outliner = dock.GroupOf("outliner")!;
        ui.Drag(TabCenter(ui, dock, "assets"), new UiVec2(outliner.Bounds.Right - 5f, outliner.Bounds.Y + 5f));
        Assert.Equal("H(0.5:T(outliner|assets*),0.5:T(viewport*))", dock.SaveLayout());
    }

    /// <summary>A middle click closes a tab, and Show docks a closed panel again.</summary>
    [Fact]
    public void Tabs_close_and_reopen()
    {
        (EditorHarness ui, DockSpace dock) = Docked("H(0.5:T(outliner*|assets),0.5:T(viewport*))");
        ui.Click(TabCenter(ui, dock, "assets"), MouseButton.Middle);
        Assert.False(dock.IsOpen("assets"));

        dock.Show("assets");
        Assert.True(dock.IsOpen("assets"));
    }

    // ---- Tree view ----

    private sealed class Node(string name, params Node[] children)
    {
        public string Name { get; } = name;

        public List<Node> Children { get; } = [.. children];

        public Node? Parent { get; set; }
    }

    private static (EditorHarness Ui, TreeView<Node> Tree, Node Root) Tree()
    {
        Node root = new("level", new Node("bunker", new Node("door"), new Node("crate")), new Node("tower", new Node("guard")));
        static void Link(Node n)
        {
            foreach (Node c in n.Children)
            {
                c.Parent = n;
                Link(c);
            }
        }

        Link(root);
        EditorHarness ui = new();
        TreeView<Node> tree = new(ui.Theme, new TreeSource<Node>
        {
            Roots = () => [root],
            Children = static n => n.Children,
            Parent = static n => n.Parent,
            Label = static n => n.Name,
        }, new Selection<Node>());
        ui.Root.Content = tree;
        ui.Frame();
        return (ui, tree, root);
    }

    /// <summary>Clicking, Ctrl-clicking, Shift-clicking and the arrows select like a desktop tree.</summary>
    [Fact]
    public void A_tree_selects_and_navigates()
    {
        (EditorHarness ui, TreeView<Node> tree, Node root) = Tree();
        tree.SetExpanded(root, true);
        ui.Frame();
        Assert.Equal(3, tree.RowCount);

        ui.Click(tree.RowRect(1).Center);
        Assert.Equal("bunker", tree.Selection.Primary!.Name);
        ui.Key(KeyCode.Right);
        Assert.Equal(5, tree.RowCount);
        ui.Key(KeyCode.Down, KeyModifiers.Shift);
        ui.Key(KeyCode.Down, KeyModifiers.Shift);
        Assert.Equal(["bunker", "crate", "door"], tree.Selection.Items.Select(static n => n.Name).Order());

        ui.Press(tree.RowRect(4).Center);
        ui.Release(tree.RowRect(4).Center);
        Assert.Equal("tower", Assert.Single(tree.Selection.Items).Name);
        ui.Input.Keyboard.SetKey(KeyCode.LeftControl, down: true);
        ui.Click(tree.RowRect(0).Center);
        ui.Input.Keyboard.SetKey(KeyCode.LeftControl, down: false);
        Assert.Equal(2, tree.Selection.Count);
    }

    /// <summary>A search shows matches with their ancestors, expanded or not.</summary>
    [Fact]
    public void A_tree_filters_to_matches_and_their_ancestors()
    {
        (_, TreeView<Node> tree, _) = Tree();
        tree.Filter = "guard";
        Assert.Equal(3, tree.RowCount);
        Assert.Equal("guard", tree.ItemAt(2).Name);
    }

    /// <summary>Dragging a row onto the middle of another asks to put it inside; onto its own child is refused.</summary>
    [Fact]
    public void A_tree_drag_requests_a_drop()
    {
        (EditorHarness ui, TreeView<Node> tree, Node root) = Tree();
        tree.SetExpanded(root, true);
        tree.SetExpanded(root.Children[0], true);
        ui.Frame();
        (IReadOnlyList<Node> Items, Node Target, TreeDropPosition Position)? dropped = null;
        tree.DropRequested += (items, target, position) => dropped = (items, target, position);

        ui.Drag(tree.RowRect(3).Center, tree.RowRect(4).Center);
        Assert.NotNull(dropped);
        Assert.Equal("crate", Assert.Single(dropped.Value.Items).Name);
        Assert.Equal("tower", dropped.Value.Target.Name);
        Assert.Equal(TreeDropPosition.Inside, dropped.Value.Position);

        dropped = null;
        ui.Click(tree.RowRect(1).Center);
        ui.Drag(tree.RowRect(1).Center, tree.RowRect(2).Center);
        Assert.Null(dropped);
    }

    // ---- Property grid ----

    /// <summary>Rows read their values each frame, write with a final flag, and collapse with their section.</summary>
    [Fact]
    public void A_property_grid_binds_and_collapses()
    {
        EditorHarness ui = new();
        PropertyGrid grid = new(ui.Theme);
        double x = 1.0;
        List<bool> finals = [];
        grid.Section("Transform");
        NumberField field = grid.Number("X", () => x, (value, final) =>
        {
            x = value;
            finals.Add(final);
        });
        field.Step = 1.0;
        ui.Root.Content = grid;
        ui.Frame();

        x = 5.0;
        ui.Frame();
        Assert.Equal(5.0, field.Value);

        UiVec2 start = field.Bounds.Center;
        ui.Drag(start, new UiVec2(start.X + 10f, start.Y));
        Assert.Equal(15.0, x);
        Assert.True(finals[^1]);
        Assert.Contains(false, finals);

        ui.Click(new UiVec2(grid.Bounds.X + 40f, grid.Bounds.Y + ui.Theme.Px(4f) + (ui.Theme.RowHeight * 0.5f)));
        Assert.Equal(Visibility.Hidden, field.Visibility);
    }

    private static UiVec2 TabCenter(EditorHarness ui, DockSpace dock, string id)
    {
        DockTabs group = dock.GroupOf(id)!;
        float x = group.Bounds.X;
        foreach (DockPanel panel in group.Panels)
        {
            float width = ui.Theme.Px(14f) + ui.Theme.Font.MeasureWidth(panel.Title, ui.Theme.TextSize) + ui.Theme.Px(30f);
            if (panel.Id == id)
            {
                return new UiVec2(x + Math.Min(20f, width * 0.3f), group.Bounds.Y + (ui.Theme.TabHeight * 0.5f));
            }

            x += width;
        }

        throw new InvalidOperationException(id);
    }
}
