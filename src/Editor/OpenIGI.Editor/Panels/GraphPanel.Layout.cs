using OpenIGI.Editor.Core.Graphs;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// What the canvas's layout handles do: a frame or comment resized by its corner, a comment's menu (colour, rename,
/// delete), and the reroute points a wire runs through. Each is one undoable step on the open graph's layout.
/// </summary>
internal sealed partial class GraphPanel
{
    // The colours a comment can take, as node editors offer them.
    private static readonly (string Name, uint Colour)[] CommentColours =
    [
        ("Blue", 0x4A6FA5), ("Teal", 0x2F8A8A), ("Green", 0x4E8A3C), ("Yellow", 0xA8902E),
        ("Orange", 0xB86A2C), ("Red", 0xA84040), ("Purple", 0x7A4FA0), ("Grey", 0x6A6A72),
    ];

    private void ResizeFrame(string id, UiVec2 size)
    {
        GraphLayout layout = OpenLayout;
        if (layout.Comments.Find(c => c.Id == id) is { } comment)
        {
            _workspace.Execute(new SetCommentCommand(layout, comment, comment with { Bounds = comment.Bounds with { Width = size.X, Height = size.Y } }));
        }
        else
        {
            _workspace.Execute(new ResizeFrameCommand(layout, new GraphNodeKey(id), new GraphPoint(size.X, size.Y)));
        }
    }

    // A right click on a frame's title: a comment's colour, name and removal; a gate's size back to its contents.
    private void OpenFrameMenu(string id, UiVec2 at)
    {
        if (Root is not { } root)
        {
            return;
        }

        GraphLayout layout = OpenLayout;
        if (layout.Comments.Find(c => c.Id == id) is not { } comment)
        {
            CommandPalette.Open(root, [new PaletteEntry("Fit Frame to Contents", () => _workspace.Execute(new ResizeFrameCommand(layout, new GraphNodeKey(id), null)))], "Frame");
            return;
        }

        List<PaletteEntry> entries =
        [
            new("Rename Comment…", () => RenameComment(layout, comment)),
            new("Delete Comment", () => _workspace.Execute(new SetCommentCommand(layout, comment, null))),
        ];
        entries.AddRange(CommentColours.Select(c => new PaletteEntry($"Colour: {c.Name}", () => _workspace.Execute(new SetCommentCommand(layout, comment, comment with { Colour = c.Colour })))
        {
            Detail = c.Colour == comment.Colour ? "current" : null,
        }));
        CommandPalette.Open(root, entries, comment.Text);
    }

    private void RenameComment(GraphLayout layout, GraphComment comment)
    {
        if (Root is not { } root)
        {
            return;
        }

        CommandPalette palette = CommandPalette.Open(root, [], "The comment's name");
        palette.Typed = text => text.Trim().Length == 0
            ? null
            : new PaletteEntry($"Rename to {text.Trim()}", () => _workspace.Execute(new SetCommentCommand(layout, comment, comment with { Text = text.Trim() })));
        palette.Query = comment.Text;
    }

    private void AddReroute(CanvasWire wire, int index, UiVec2 at)
    {
        List<GraphPoint> points = PointsOf(wire);
        points.Insert(Math.Clamp(index, 0, points.Count), new GraphPoint(at.X, at.Y));
        _workspace.Execute(new SetRerouteCommand(OpenLayout, KeyOf(wire), points));
    }

    private void MoveReroute(CanvasWire wire, int index, UiVec2 at)
    {
        List<GraphPoint> points = PointsOf(wire);
        if (index >= 0 && index < points.Count)
        {
            points[index] = new GraphPoint(at.X, at.Y);
            _workspace.Execute(new SetRerouteCommand(OpenLayout, KeyOf(wire), points));
        }
    }

    private void RemoveReroute(CanvasWire wire, int index)
    {
        List<GraphPoint> points = PointsOf(wire);
        if (index >= 0 && index < points.Count)
        {
            points.RemoveAt(index);
            _workspace.Execute(new SetRerouteCommand(OpenLayout, KeyOf(wire), points));
        }
    }

    private static List<GraphPoint> PointsOf(CanvasWire wire) => [.. wire.Via.Select(static p => new GraphPoint(p.X, p.Y))];

    private static string KeyOf(CanvasWire wire) => GraphLayout.WireKey(wire.FromNode, wire.FromPin, wire.ToNode, wire.ToPin);
}
