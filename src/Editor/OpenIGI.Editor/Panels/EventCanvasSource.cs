using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>One event of an AI script as the canvas draws it: exec nodes on white wires, the values they read as pure nodes.</summary>
internal sealed class EventCanvasSource : INodeCanvasSource
{
    private readonly List<CanvasNode> _nodes = [];
    private readonly List<CanvasWire> _wires = [];
    private readonly List<CanvasFrame> _frames = [];

    /// <summary>Initializes a new instance of the <see cref="EventCanvasSource"/> class.</summary>
    /// <param name="graph">The script's graph.</param>
    /// <param name="scope">The event drawn.</param>
    /// <param name="layout">Where its nodes sit.</param>
    /// <param name="theme">The theme, for colours.</param>
    public EventCanvasSource(EventGraph graph, EventScope scope, GraphLayout layout, EditorTheme theme)
    {
        Graph = graph;
        Scope = scope;
        Dictionary<string, ScriptType> outputTypes = new(StringComparer.Ordinal);
        foreach (GraphNodeKey key in graph.Nodes.Keys.Where(scope.Members.Contains))
        {
            EventNodeView view = EventGraphView.Describe(graph, scope, key);
            foreach (EventPinView output in view.Outputs)
            {
                outputTypes[$"{key.Value}|{output.Id}"] = output.Type;
            }

            UiVec2 at = layout.Positions.TryGetValue(key, out GraphPoint point) ? new UiVec2((float)point.X, (float)point.Y) : default;
            _nodes.Add(new CanvasNode(key.Value, view.Title, at, Header(graph.Nodes[key], view), [.. view.Inputs.Select(p => Pin(p, theme))], [.. view.Outputs.Select(p => Pin(p, theme))])
            {
                Subtitle = view.Subtitle,
                IsEvent = view.IsEvent,
                Tooltip = view.Doc,
            });
        }

        HashSet<(string, string)> wired = [];
        foreach (GraphLink link in graph.Links.Where(l => scope.Members.Contains(l.From.Node) && scope.Members.Contains(l.To.Node)))
        {
            ScriptType type = outputTypes.GetValueOrDefault($"{link.From.Node.Value}|{link.From.Pin}", ScriptType.Any);
            _wires.Add(new CanvasWire(link.From.Node.Value, link.From.Pin, link.To.Node.Value, link.To.Pin, MissionCanvasSource.PinColour(type, theme))
            {
                IsExec = type.Kind == ScriptTypeKind.Exec,
                Via = MissionCanvasSource.ViaOf(layout, link.From.Node.Value, link.From.Pin, link.To.Node.Value, link.To.Pin),
            });
            wired.Add((link.To.Node.Value, link.To.Pin));
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            CanvasNode node = _nodes[i];
            _nodes[i] = node with { Inputs = [.. node.Inputs.Select(p => wired.Contains((node.Id, p.Id)) ? p with { IsConnected = true } : p)] };
        }

        // The event's comments, which move what they cover when dragged by their title.
        foreach (GraphComment comment in layout.Comments.Where(c => c.Scope == scope.Key))
        {
            _frames.Add(new CanvasFrame(comment.Id, comment.Text, new UiRect((float)comment.Bounds.X, (float)comment.Bounds.Y, (float)comment.Bounds.Width, (float)comment.Bounds.Height), Bgra32.FromPacked(0xFF000000u | comment.Colour)) { IsComment = true });
        }
    }

    /// <summary>Gets the script's graph.</summary>
    public EventGraph Graph { get; }

    /// <summary>Gets the event drawn.</summary>
    public EventScope Scope { get; }

    /// <inheritdoc/>
    public IReadOnlyList<CanvasNode> Nodes => _nodes;

    /// <inheritdoc/>
    public IReadOnlyList<CanvasWire> Wires => _wires;

    /// <inheritdoc/>
    public IReadOnlyList<CanvasFrame> Frames => _frames;

    private static CanvasPin Pin(EventPinView pin, EditorTheme theme) =>
        new(pin.Id, pin.Label, MissionCanvasSource.PinColour(pin.Type, theme), pin.IsExec)
        {
            Value = pin.Value,
            IsToggle = !pin.IsExec && pin.Value is { } v && (pin.Type.Kind == ScriptTypeKind.Bool || v is "TRUE" or "FALSE"),
            IsOn = pin.Value is "TRUE" or "1",
            Tooltip = pin.IsExec ? "Exec: what runs next" : $"{pin.Label}: {pin.Type}",
        };

    // Node editor colours: actions blue, flow control grey, pure functions teal, maths and logic green.
    private static Bgra32 Header(GraphNode node, EventNodeView view) => node switch
    {
        EventStatementNode { Kind: EventStatementKind.Call } => Bgra32.FromRgb(0x2F, 0x6C, 0xB5),
        EventStatementNode => Bgra32.FromRgb(0x5A, 0x5A, 0x66),
        EventValueNode { Kind: EventValueKind.Call } => Bgra32.FromRgb(0x24, 0x7E, 0x80),
        _ when view.IsPure => Bgra32.FromRgb(0x4E, 0x7A, 0x3C),
        _ => Bgra32.FromRgb(0x5A, 0x5A, 0x66),
    };
}
