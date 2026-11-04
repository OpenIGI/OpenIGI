using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>The node canvas: geometry, zoom, selection and the requests its gestures raise.</summary>
public sealed class NodeCanvasTests
{
    private static readonly Bgra32 Ink = Bgra32.White;

    /// <summary>Pins sit down the node's sides, one row each, and a folded node's pins meet at its title.</summary>
    [Fact]
    public void Pins_sit_down_the_sides()
    {
        CanvasNode node = Node("a", 100, 50);
        UiVec2 first = NodeCanvas.PinAt(node, "in1", output: false);
        UiVec2 second = NodeCanvas.PinAt(node, "in2", output: false);
        UiVec2 output = NodeCanvas.PinAt(node, "out", output: true);

        Assert.Equal(100f + NodeCanvas.PinInset, first.X);
        Assert.Equal(NodeCanvas.RowHeight, second.Y - first.Y);
        Assert.Equal(100f + NodeCanvas.NodeWidth - NodeCanvas.PinInset, output.X);
        Assert.Equal(NodeCanvas.PinAt(node with { Collapsed = true }, "in2", false).Y, NodeCanvas.PinAt(node with { Collapsed = true }, "out", true).Y);
    }

    /// <summary>A drag from an output onto an input asks for the wire; dropping on empty canvas asks what to put there.</summary>
    [Fact]
    public void Dragging_a_wire_asks_for_a_link()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount(Node("a", 0, 0), Node("b", 400, 0));
        (string, string, string, string)? linked = null;
        (string, string, bool)? dropped = null;
        canvas.LinkRequested += (fromNode, fromPin, toNode, toPin) => linked = (fromNode, fromPin, toNode, toPin);
        canvas.LinkDroppedOnEmpty += (node, pin, isOutput, _) => dropped = (node, pin, isOutput);

        UiVec2 from = canvas.ToScreen(NodeCanvas.PinAt(canvas.Source.Nodes[0], "out", output: true));
        UiVec2 to = canvas.ToScreen(NodeCanvas.PinAt(canvas.Source.Nodes[1], "in2", output: false));
        ui.Drag(from, to);
        Assert.Equal(("a", "out", "b", "in2"), linked);

        ui.Drag(from, canvas.ToScreen(new UiVec2(300, 600)));
        Assert.Equal(("a", "out", true), dropped);
    }

    /// <summary>A box drawn on empty canvas selects what it touches; a drag of a selected node asks to move them all.</summary>
    [Fact]
    public void A_box_selects_and_a_drag_moves_the_selection()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount(Node("a", 40, 40), Node("b", 440, 40), Node("c", 40, 440));
        canvas.SnapToGrid = false;
        ui.Drag(canvas.ToScreen(new UiVec2(20, 20)), canvas.ToScreen(new UiVec2(700, 140)));
        Assert.Equal(["a", "b"], canvas.Selected.Order());

        float moved = 0f;
        canvas.MoveRequested += (ids, delta) =>
        {
            Assert.Equal(2, ids.Count);
            moved += delta.X;
        };
        UiVec2 title = canvas.ToScreen(new UiVec2(80, 50));
        ui.Drag(title, new UiVec2(title.X + (100f * canvas.Zoom), title.Y));
        Assert.InRange(moved, 99f, 101f);
    }

    /// <summary>The wheel zooms in steps about the pointer: the canvas point under it stays under it.</summary>
    [Fact]
    public void Zoom_keeps_the_point_under_the_pointer()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount(Node("a", 0, 0));
        UiVec2 pointer = new(canvas.Bounds.X + 300f, canvas.Bounds.Y + 200f);
        UiVec2 before = canvas.ToCanvas(pointer);
        float zoom = canvas.Zoom;
        ui.MoveTo(pointer);
        ui.Input.Mouse.AddWheel(1f);
        ui.Frame();

        Assert.True(canvas.Zoom > zoom);
        UiVec2 after = canvas.ToCanvas(pointer);
        Assert.InRange(after.X - before.X, -0.01f, 0.01f);
        Assert.InRange(after.Y - before.Y, -0.01f, 0.01f);
    }

    /// <summary>
    /// A level's whole graph in view, far out, stays well inside the 2D geometry ring (128K vertices a frame, shared with the
    /// rest of the editor): nodes drop to flat boxes and wires past the budget to single lines.
    /// </summary>
    [Fact]
    public void A_whole_level_in_view_fits_the_geometry_ring()
    {
        const int Columns = 40;
        List<CanvasNode> nodes = [];
        for (int i = 0; i < 1600; i++)
        {
            nodes.Add(Node("n" + i, i % Columns * 280f, i / Columns * 120f));
        }

        List<CanvasWire> wires = [];
        for (int i = 0; i < 2400; i++)
        {
            wires.Add(new CanvasWire("n" + (i % 1600), "out", "n" + ((i * 37) % 1600), "in1", Ink));
        }

        EditorHarness ui = new(1600f, 900f);
        NodeCanvas canvas = new(ui.Theme, new Source(nodes, wires));
        ui.Root.Content = canvas;
        ui.Frame();
        canvas.Frame();
        ui.Frame();

        RecordingUiRenderer renderer = new();
        canvas.Draw(new UiPainter(renderer, new LayoutContext(ui.Width, ui.Height)));
        int vertices = (renderer.Quads.Count + renderer.Glyphs.Count) * 4;
        Assert.InRange(vertices, 1, 128 * 1024 / 3);
    }

    /// <summary>A dragged node lands on the grid; Shift+W lines the selection up along its top edge.</summary>
    [Fact]
    public void Drags_snap_and_selections_align()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount(Node("a", 40, 40), Node("b", 440, 90));
        Dictionary<string, UiVec2> at = new() { ["a"] = new(40, 40), ["b"] = new(440, 90) };
        canvas.MoveRequested += (ids, delta) =>
        {
            foreach (string id in ids)
            {
                at[id] = new UiVec2(at[id].X + delta.X, at[id].Y + delta.Y);
            }
        };

        UiVec2 title = canvas.ToScreen(new UiVec2(80, 50));
        ui.Drag(title, new UiVec2(title.X + (37f * canvas.Zoom), title.Y + (5f * canvas.Zoom)));
        Assert.Equal(0f, at["a"].X % NodeCanvas.SnapStep);
        Assert.Equal(0f, at["a"].Y % NodeCanvas.SnapStep);

        canvas.Select(["a", "b"]);
        canvas.Align(CanvasEdge.Top);
        Assert.Equal(40f, at["b"].Y); // the top of the two as the source places them
    }

    /// <summary>Ctrl+F asks to find, Ctrl+C/Ctrl+D hand over the selection, Ctrl+V the point to paste at; plain F still frames.</summary>
    [Fact]
    public void Clipboard_and_find_keys_raise_their_requests()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount(Node("a", 40, 40));
        UiVec2 empty = canvas.ToScreen(new UiVec2(600, 400));
        ui.Press(empty);
        ui.Release(empty);
        int finds = 0;
        IReadOnlyCollection<string>? copied = null, duplicated = null;
        UiVec2? pasted = null;
        canvas.FindRequested += () => finds++;
        canvas.CopyRequested += ids => copied = ids;
        canvas.DuplicateRequested += ids => duplicated = ids;
        canvas.PasteRequested += at => pasted = at;

        ui.Key(KeyCode.F);
        Assert.Equal(0, finds);
        ui.Key(KeyCode.F, KeyModifiers.Control);
        Assert.Equal(1, finds);

        ui.Key(KeyCode.C, KeyModifiers.Control);
        Assert.Null(copied); // nothing selected
        canvas.Select(["a"]);
        ui.Key(KeyCode.C, KeyModifiers.Control);
        ui.Key(KeyCode.D, KeyModifiers.Control);
        ui.Key(KeyCode.V, KeyModifiers.Control);
        Assert.Equal(["a"], copied);
        Assert.Equal(["a"], duplicated);
        Assert.NotNull(pasted);
    }

    /// <summary>A click on an input's tick box asks to flip that value, and does not drag the node.</summary>
    [Fact]
    public void A_tick_box_click_asks_to_flip_its_value()
    {
        CanvasNode node = Node("a", 0, 0) with { Inputs = [new CanvasPin("in1", "On", Ink) { Value = "FALSE", IsToggle = true }] };
        (EditorHarness ui, NodeCanvas canvas) = Mount(node);
        (string, string)? clicked = null;
        bool moved = false;
        canvas.ValueClicked += (n, p) => clicked = (n, p);
        canvas.MoveRequested += (_, _) => moved = true;

        // The box sits just right of the pin's label.
        UiVec2 pin = canvas.ToScreen(NodeCanvas.PinAt(canvas.Source.Nodes[0], "in1", output: false));
        UiFont font = ui.Theme.Fonts.Font(OpenIGI.UI.Editor.Theme.EditorFontFace.Interface, 11.5f * canvas.Zoom);
        UiVec2 box = new(pin.X + (10f * canvas.Zoom) + font.MeasureWidth("On", 11.5f * canvas.Zoom) + (14f * canvas.Zoom), pin.Y);
        ui.Press(box);
        ui.Release(box);

        Assert.Equal(("a", "in1"), clicked);
        Assert.False(moved);
    }

    /// <summary>A drag moves nodes only on screen until it ends, then asks for one move: the owner rebuilds once, not every step.</summary>
    [Fact]
    public void A_drag_asks_for_one_move_when_it_ends()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount(Node("a", 40, 40));
        int moves = 0;
        canvas.MoveRequested += (_, _) => moves++;
        UiVec2 title = canvas.ToScreen(new UiVec2(80, 50));
        ui.Press(title);
        for (int i = 1; i <= 5; i++)
        {
            ui.MoveTo(new UiVec2(title.X + (i * 20f * canvas.Zoom), title.Y));
        }

        Assert.Equal(0, moves);
        ui.Release(new UiVec2(title.X + (100f * canvas.Zoom), title.Y));
        Assert.Equal(1, moves);
    }

    private static CanvasNode Node(string id, float x, float y) => new(
        id,
        id.ToUpperInvariant(),
        new UiVec2(x, y),
        Ink,
        [new CanvasPin("in1", "One", Ink), new CanvasPin("in2", "Two", Ink)],
        [new CanvasPin("out", "Out", Ink)]);

    private static (EditorHarness, NodeCanvas) Mount(params CanvasNode[] nodes)
    {
        EditorHarness ui = new();
        NodeCanvas canvas = new(ui.Theme, new Source(nodes)) { ShowMinimap = false };
        ui.Root.Content = canvas;
        ui.Frame();
        return (ui, canvas);
    }

    private sealed class Source(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasWire>? wires = null) : INodeCanvasSource
    {
        public IReadOnlyList<CanvasNode> Nodes => nodes;

        public IReadOnlyList<CanvasWire> Wires => wires ?? [];

        public IReadOnlyList<CanvasFrame> Frames => [];
    }
}
