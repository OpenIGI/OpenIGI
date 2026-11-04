using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>The canvas's handles on frames and wires: corner resizing, the frame menu, reroute points, back and forward.</summary>
public sealed class NodeCanvasEdgeTests
{
    private static readonly Bgra32 Ink = Bgra32.FromRgb(0x5A, 0x9B, 0xD5);

    /// <summary>Dragging a frame's corner asks for one resize, to where it was let go.</summary>
    [Fact]
    public void A_corner_drag_asks_for_one_resize()
    {
        CanvasFrame frame = new("c1", "Comment", new UiRect(100, 100, 300, 200), Ink) { IsComment = true };
        (EditorHarness ui, NodeCanvas canvas) = Mount([], [], [frame]);
        List<(string, UiVec2)> resized = [];
        canvas.FrameResizeRequested += (id, size) => resized.Add((id, size));

        UiVec2 corner = canvas.ToScreen(new UiVec2(395, 295));
        ui.Drag(corner, canvas.ToScreen(new UiVec2(495, 345)));

        (string id, UiVec2 size) = Assert.Single(resized);
        Assert.Equal("c1", id);
        Assert.InRange(size.X, 395, 405);
        Assert.InRange(size.Y, 245, 255);
    }

    /// <summary>A right click on a frame's title asks for its menu, not the canvas's.</summary>
    [Fact]
    public void A_right_click_on_a_title_asks_for_the_frame_menu()
    {
        CanvasFrame frame = new("c1", "Comment", new UiRect(100, 100, 300, 200), Ink) { IsComment = true };
        (EditorHarness ui, NodeCanvas canvas) = Mount([], [], [frame]);
        string? menu = null;
        bool canvasMenu = false;
        canvas.FrameContextRequested += (id, _) => menu = id;
        canvas.ContextRequested += (_, _) => canvasMenu = true;

        ui.Click(canvas.ToScreen(new UiVec2(200, 110)), MouseButton.Right);

        Assert.Equal("c1", menu);
        Assert.False(canvasMenu);
    }

    /// <summary>A double click on a wire asks for a reroute point there; a point is dragged and Alt-clicked away.</summary>
    [Fact]
    public void Reroute_points_are_added_moved_and_removed()
    {
        CanvasNode a = Node("a", 0, 0), b = Node("b", 600, 0);
        CanvasWire straight = new("a", "out", "b", "in1", Ink);
        (EditorHarness ui, NodeCanvas canvas) = Mount([a, b], [straight], []);
        (string, int, UiVec2)? added = null;
        canvas.RerouteAddRequested += (wire, index, at) => added = (wire.ToNode, index, at);

        // Halfway along the wire, on its straight middle.
        UiVec2 from = NodeCanvas.PinAt(a, "out", output: true), to = NodeCanvas.PinAt(b, "in1", output: false);
        UiVec2 middle = canvas.ToScreen(new UiVec2((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        ui.Click(middle);
        ui.Click(middle);
        Assert.NotNull(added);
        Assert.Equal(("b", 0), (added.Value.Item1, added.Value.Item2));

        // With a point on it: dragged, then Alt-clicked away.
        UiVec2 knot = new((from.X + to.X) / 2, from.Y + 120);
        (EditorHarness ui2, NodeCanvas routed) = Mount([a, b], [straight with { Via = [knot] }], []);
        (int, UiVec2)? moved = null;
        int? removed = null;
        routed.RerouteMoveRequested += (_, index, at) => moved = (index, at);
        routed.RerouteRemoveRequested += (_, index) => removed = index;
        ui2.Drag(routed.ToScreen(knot), routed.ToScreen(new UiVec2(knot.X + 50, knot.Y + 40)));
        Assert.Equal(0, moved?.Item1);
        Assert.InRange(moved!.Value.Item2.X, knot.X + 45, knot.X + 55);

        ui2.Hold(KeyModifiers.Alt, down: true);
        ui2.Click(routed.ToScreen(knot));
        ui2.Hold(KeyModifiers.Alt, down: false);
        Assert.Equal(0, removed);
    }

    /// <summary>Alt+Left and Alt+Right ask to go back and forward.</summary>
    [Fact]
    public void Alt_arrows_go_back_and_forward()
    {
        (EditorHarness ui, NodeCanvas canvas) = Mount([Node("a", 0, 0)], [], []);
        UiVec2 empty = canvas.ToScreen(new UiVec2(500, 400));
        ui.Click(empty);
        int back = 0, forward = 0;
        canvas.BackRequested += () => back++;
        canvas.ForwardRequested += () => forward++;

        ui.Key(KeyCode.Left, KeyModifiers.Alt);
        ui.Key(KeyCode.Right, KeyModifiers.Alt);
        ui.Key(KeyCode.Left);

        Assert.Equal((1, 1), (back, forward));
    }

    private static CanvasNode Node(string id, float x, float y) => new(
        id,
        id.ToUpperInvariant(),
        new UiVec2(x, y),
        Ink,
        [new CanvasPin("in1", "One", Ink)],
        [new CanvasPin("out", "Out", Ink)]);

    private static (EditorHarness, NodeCanvas) Mount(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasWire> wires, IReadOnlyList<CanvasFrame> frames)
    {
        EditorHarness ui = new();
        NodeCanvas canvas = new(ui.Theme, new Source(nodes, wires, frames)) { ShowMinimap = false };
        ui.Root.Content = canvas;
        ui.Frame();
        return (ui, canvas);
    }

    private sealed class Source(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasWire> wires, IReadOnlyList<CanvasFrame> frames) : INodeCanvasSource
    {
        public IReadOnlyList<CanvasNode> Nodes => nodes;

        public IReadOnlyList<CanvasWire> Wires => wires;

        public IReadOnlyList<CanvasFrame> Frames => frames;
    }
}
