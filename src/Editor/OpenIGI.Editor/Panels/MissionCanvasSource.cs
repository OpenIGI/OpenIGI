using OpenIGI.Core.Graphics;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>One small graph of a mission as the canvas draws it: nodes, pins coloured by type, wires, its gate frame, comments and problem badges.</summary>
/// <remarks>
/// A gated graph (a <c>ConditionalContainer</c>) is framed; the container keeps a node of its own inside the frame,
/// for its condition and run-at-start/stop pins. Only wireable parameters are pins; values (a position, a
/// model) are edited in the Inspector, so nodes stay compact.
/// </remarks>
internal sealed partial class MissionCanvasSource : INodeCanvasSource
{
    private const float FramePadding = 24f;
    private const float FrameTitle = 34f;

    private readonly List<CanvasNode> _nodes = [];
    private readonly List<CanvasWire> _wires = [];
    private readonly List<CanvasFrame> _frames = [];

    /// <summary>Initializes a new instance of the <see cref="MissionCanvasSource"/> class.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">Where its nodes sit.</param>
    /// <param name="scope">The small graph drawn: its own nodes, and what they wire to as references.</param>
    /// <param name="homes">Each node's home scope title, to label a reference node with where its own logic is.</param>
    /// <param name="firstScope">The scope a comment with none is drawn in: comments made before graphs were split.</param>
    /// <param name="vocabulary">The title's vocabulary, to name constants and natives.</param>
    /// <param name="text">Turns a text resource into its words, for the settings shown on nodes.</param>
    /// <param name="allPins">Whether outputs nothing reads are drawn too.</param>
    /// <param name="theme">The theme, for colours.</param>
    /// <param name="problems">Each task's first problem, to badge its node with.</param>
    public MissionCanvasSource(
        MissionGraph graph,
        GraphLayout layout,
        GraphScope scope,
        Func<GraphNodeKey, string?> homes,
        string firstScope,
        ScriptVocabulary? vocabulary,
        Func<string, string?> text,
        bool allPins,
        EditorTheme theme,
        IReadOnlyDictionary<MissionTask, MissionIssue> problems)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(homes);
        Scope = scope;
        Dictionary<string, ScriptType> outputTypes = new(StringComparer.Ordinal);
        HashSet<GraphNodeKey> drawn = [];
        foreach (TaskGraphNode node in graph.TaskNodes.Where(n => scope.Contains(n.Key)))
        {
            drawn.Add(node.Key);
            CanvasNode drawnNode = TaskNode(node, Position(layout, scope.LayoutKey(node.Key)), theme, problems, outputTypes, vocabulary) with
            {
                Collapsed = layout.Collapsed.Contains(node.Key),
                Details = scope.References.Contains(node.Key) ? [] : [.. TaskNodeDetails.For(node, text).Select(static d => new CanvasDetail(d.Label, d.Value, d.Full))],
            };
            if (scope.References.Contains(node.Key))
            {
                // A reference: drawn here for its pins; its own logic, if any, is another graph's.
                string? home = homes(node.Key);
                drawnNode = drawnNode with
                {
                    Subtitle = home is null ? $"Reference · {drawnNode.Subtitle}" : $"Reference · in {home}",
                    Header = drawnNode.Header.WithAlpha(150),
                };
            }

            _nodes.Add(drawnNode);
        }

        foreach (ExpressionGraphNode node in graph.Nodes.Values.OfType<ExpressionGraphNode>().Where(n => scope.Members.Contains(n.Key)))
        {
            drawn.Add(node.Key);
            outputTypes[$"{node.Key.Value}|{GraphNode.Output}"] = node.OutputType;
            CanvasNode drawnNode = ExpressionNode(node, Position(layout, node.Key), theme, vocabulary);
            if (scope.Chains.TryGetValue(node.Key, out List<GraphPinRef>? operands))
            {
                drawnNode = Chain(drawnNode, graph, scope, operands, theme);
            }

            _nodes.Add(drawnNode);
        }

        // Wires into a folded chain's inner nodes land on the chain's operand pins; the chain's own inner wires are not drawn.
        Dictionary<GraphPinRef, Bgra32> wiredInputs = [];
        foreach (GraphLink link in graph.Links)
        {
            GraphNodeKey from = scope.DrawnAs(link.From.Node);
            (GraphNodeKey toNode, string toPin) = scope.DrawnPin(link.To);
            if (from == toNode || !drawn.Contains(from) || !drawn.Contains(toNode) || scope.Folded.ContainsKey(link.From.Node))
            {
                continue;
            }

            ScriptType type = outputTypes.GetValueOrDefault($"{link.From.Node.Value}|{link.From.Pin}", ScriptType.Any);
            _wires.Add(new CanvasWire(from.Value, link.From.Pin, toNode.Value, toPin, PinColour(type, theme))
            {
                Via = ViaOf(layout, from.Value, link.From.Pin, toNode.Value, toPin),
            });
            wiredInputs[new GraphPinRef(toNode, toPin)] = PinColour(type, theme);
        }

        // A pin with a wire in is filled, and an operand that takes anything takes the colour of what feeds it.
        for (int i = 0; i < _nodes.Count; i++)
        {
            CanvasNode node = _nodes[i];
            GraphNodeKey key = new(node.Id);
            _nodes[i] = node with
            {
                Inputs = [.. node.Inputs.Select(p => wiredInputs.TryGetValue(new GraphPinRef(key, p.Id), out Bgra32 fed)
                    ? p with { IsConnected = true, Colour = p.Colour == PinColour(ScriptType.Any, theme) ? fed : p.Colour }
                    : scope.Inline.TryGetValue(GraphScope.Resolve(node.Id, p.Id), out (GraphNodeKey Node, string Text) inline)
                        ? p with
                        {
                            Value = inline.Text,
                            IsToggle = inline.Text is "TRUE" or "FALSE" || (p.Colour == PinColour(ScriptType.Bool, theme) && inline.Text is "0" or "1"),
                            IsOn = inline.Text is "TRUE" or "1",
                        }
                        : p)],
            };

            // A reference shows only what this graph uses of it, as a node editor shows a variable with just the value read.
            if (scope.References.Contains(key))
            {
                HashSet<string> feeding = [.. _wires.Where(w => w.FromNode == node.Id).Select(static w => w.FromPin)];
                _nodes[i] = _nodes[i] with
                {
                    Inputs = [.. _nodes[i].Inputs.Where(static p => p.IsConnected)],
                    Outputs = [.. _nodes[i].Outputs.Where(p => feeding.Contains(p.Id))],
                };
            }
            else if (!allPins && graph.Nodes.GetValueOrDefault(key) is TaskGraphNode)
            {
                // A task's outputs nothing reads stay out of the way, as node editors hide unconnected pins; Show All Pins
                // brings them back to wire from.
                HashSet<string> feeding = [.. _wires.Where(w => w.FromNode == node.Id).Select(static w => w.FromPin)];
                _nodes[i] = _nodes[i] with { Outputs = [.. _nodes[i].Outputs.Where(p => feeding.Contains(p.Id))] };
            }
        }

        AddFrame(graph, layout, scope, theme);
        foreach (GraphComment comment in layout.Comments.Where(c => (c.Scope ?? firstScope) == scope.Key))
        {
            _frames.Add(new CanvasFrame(comment.Id, comment.Text, ToUi(comment.Bounds), Bgra32.FromPacked(0xFF000000u | comment.Colour)) { IsComment = true });
        }

        AddActions(layout, scope.Key, firstScope, drawn, theme);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CanvasNode> Nodes => _nodes;

    /// <summary>Gets the small graph drawn.</summary>
    public GraphScope Scope { get; }


    /// <inheritdoc/>
    public IReadOnlyList<CanvasWire> Wires => _wires;

    /// <inheritdoc/>
    public IReadOnlyList<CanvasFrame> Frames => _frames;

    /// <summary>The colour a type's pins and wires are drawn in: the colours node editors use.</summary>
    /// <param name="type">The type.</param>
    /// <param name="theme">The theme, for the exec and any colours.</param>
    /// <returns>The colour.</returns>
    public static Bgra32 PinColour(ScriptType type, EditorTheme theme) => type.Kind switch
    {
        ScriptTypeKind.Exec => theme.Text,
        ScriptTypeKind.Bool => Bgra32.FromRgb(0xC8, 0x2E, 0x2E),
        ScriptTypeKind.Int => Bgra32.FromRgb(0x1F, 0xD8, 0xAA),
        ScriptTypeKind.Real => Bgra32.FromRgb(0x9C, 0xE5, 0x3C),
        ScriptTypeKind.Text => Bgra32.FromRgb(0xEC, 0x6C, 0xD0),
        ScriptTypeKind.Vector3 => Bgra32.FromRgb(0xF4, 0xC4, 0x30),
        ScriptTypeKind.Orientation => Bgra32.FromRgb(0xA0, 0x8C, 0xF0),
        ScriptTypeKind.TaskRef => Bgra32.FromRgb(0x2E, 0x8B, 0xF5),
        ScriptTypeKind.NodeRef or ScriptTypeKind.PathRef => Bgra32.FromRgb(0x46, 0xD2, 0xE6),
        ScriptTypeKind.Enum => Bgra32.FromRgb(0x00, 0x8A, 0x60),
        _ => theme.TextMuted,
    };

    private static CanvasNode TaskNode(
        TaskGraphNode node,
        UiVec2 position,
        EditorTheme theme,
        IReadOnlyDictionary<MissionTask, MissionIssue> problems,
        Dictionary<string, ScriptType> outputTypes,
        ScriptVocabulary? vocabulary)
    {
        MissionTask task = node.Task;
        List<CanvasPin> inputs = [.. node.Definition.Inputs.Where(static p => p.IsLinkable).Select(p => Pin(p.Name, p.Type, theme, Describe(p)))];
        ScriptType self = ScriptType.TaskOf(task.TypeName);
        List<CanvasPin> outputs = [Pin(MissionGraph.Self, self, theme, $"This {task.TypeName}, for pins that take a task") with { Label = "Self" }];
        outputTypes[$"{node.Key.Value}|{MissionGraph.Self}"] = self;
        foreach (TaskPin field in node.Definition.Outputs)
        {
            outputs.Add(Pin(field.Name, field.Type, theme, $"{field.Type}: {task.TypeName}_{task.Id}.{field.Name}"));
            outputTypes[$"{node.Key.Value}|{field.Name}"] = field.Type;
        }

        problems.TryGetValue(task, out MissionIssue? problem);
        return new CanvasNode(node.Key.Value, Title(node), position, Header(task.TypeName, theme), inputs, outputs)
        {
            Subtitle = (task.Id >= 0 ? $"{task.TypeName} #{task.Id}" : task.TypeName) + OnlyOpenIgi(task.TypeName, vocabulary),
            Problem = problem?.Message,
            IsError = problem?.Severity == IssueSeverity.Error,
            IsEvent = node.LatchCondition is not null,
            Tooltip = node.LatchCondition is { } condition ? $"Counts to one the first tick {condition} holds." : null,
        };
    }

    // A folded chain: one input an operand, lettered as node editors letter them, each pin standing for its real operand.
    private static CanvasNode Chain(CanvasNode node, MissionGraph graph, GraphScope scope, List<GraphPinRef> operands, EditorTheme theme)
    {
        List<CanvasPin> inputs = [];
        for (int i = 0; i < operands.Count; i++)
        {
            GraphPinRef operand = operands[i];
            ScriptType type = graph.Nodes[operand.Node] is ExpressionGraphNode owner && int.TryParse(operand.Pin, out int index) && index < owner.Inputs.Count
                ? owner.Inputs[index].Type
                : ScriptType.Any;
            string letter = i < 26 ? ((char)('A' + i)).ToString() : (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            inputs.Add(Pin(scope.DrawnPin(operand).Pin, type, theme, $"{letter}: {type}") with { Label = letter });
        }

        return node with
        {
            Inputs = inputs,
            Subtitle = $"{operands.Count} inputs · {node.Subtitle}",
        };
    }

    // A mod's addition says so on its node: the original game cannot run it.
    private static string OnlyOpenIgi(string name, ScriptVocabulary? vocabulary) =>
        vocabulary?.RuntimeOf(name) == ScriptRuntime.OpenIgi ? " · OpenIGI only" : string.Empty;

    private static CanvasNode ExpressionNode(ExpressionGraphNode node, UiVec2 position, EditorTheme theme, ScriptVocabulary? vocabulary)
    {
        ExpressionNodeLabel label = ExpressionNodeText.Describe(node, vocabulary);
        List<CanvasPin> inputs = [.. node.Inputs.Select((p, i) => Pin(i.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Type, theme, $"{label.Inputs.ElementAtOrDefault(i)}: {p.Type}" + (p.Doc is { Length: > 0 } doc ? $" — {doc}" : string.Empty)) with
        {
            Label = label.Inputs.ElementAtOrDefault(i) ?? string.Empty,
        })];
        string? problem = node.Kind switch
        {
            ExpressionNodeKind.Raw or ExpressionNodeKind.UnresolvedField or ExpressionNodeKind.UnknownName => label.Doc,
            ExpressionNodeKind.Call when node.Native is null => $"{node.Text} is not a native the game has.",
            _ => null,
        };

        // By what they are, as node editors colour them: functions blue, maths and logic green, values teal.
        Bgra32 header = node.Kind switch
        {
            ExpressionNodeKind.Call => Bgra32.FromRgb(0x2F, 0x6C, 0xB5),
            ExpressionNodeKind.Operator or ExpressionNodeKind.Conditional => Bgra32.FromRgb(0x4E, 0x7A, 0x3C),
            ExpressionNodeKind.Constant or ExpressionNodeKind.Global => Bgra32.FromRgb(0x24, 0x7E, 0x80),
            _ => Bgra32.FromRgb(0x5A, 0x5A, 0x66),
        };
        CanvasPin output = Pin(GraphNode.Output, node.OutputType, theme, $"{label.Output}: {node.OutputType}") with { Label = label.Output };
        return new CanvasNode(node.Key.Value, label.Title, position, header, inputs, [output])
        {
            Subtitle = label.Subtitle + (node.Kind is ExpressionNodeKind.Call or ExpressionNodeKind.Constant ? OnlyOpenIgi(node.Native?.Name ?? node.Text, vocabulary) : string.Empty),
            Problem = problem,
            Tooltip = label.Doc,
        };
    }

    // A gated graph's frame, round its own nodes (its references stand outside it, on the left): the gate's condition and
    // run-at-start pins are on the container's node inside it.
    private void AddFrame(MissionGraph graph, GraphLayout layout, GraphScope scope, EditorTheme theme)
    {
        if (!scope.IsGated || scope.Container is not { } gate || graph.Nodes[gate] is not TaskGraphNode container)
        {
            return;
        }

        UiRect[] inside = [.. _nodes.Where(n => scope.Members.Contains(new GraphNodeKey(n.Id))).Select(NodeCanvas.SizeOf)];
        if (inside.Length == 0)
        {
            return;
        }

        float left = inside.Min(static b => b.X) - FramePadding;
        float top = inside.Min(static b => b.Y) - FramePadding - FrameTitle;
        float right = inside.Max(static b => b.Right) + FramePadding;
        float bottom = inside.Max(static b => b.Bottom) + FramePadding;
        UiRect bounds = UiRect.FromEdges(left, top, right, bottom);
        if (layout.FrameSizes.TryGetValue(gate, out GraphPoint size))
        {
            bounds = bounds with { Width = MathF.Max(bounds.Width, (float)size.X), Height = MathF.Max(bounds.Height, (float)size.Y) };
        }

        _frames.Add(new CanvasFrame(gate.Value, Title(container), bounds, theme.Accent));
    }

    private static CanvasPin Pin(string id, ScriptType type, EditorTheme theme, string? tooltip) =>
        new(id, id, PinColour(type, theme), type.Kind == ScriptTypeKind.Exec) { Tooltip = tooltip };

    private static string Describe(TaskPin pin) => pin.Role switch
    {
        TextRole.Condition => "A condition the task reads every tick",
        TextRole.Action => "Calls run when the task starts",
        TextRole.Number => "A number the task reads every tick",
        _ => pin.Type.Kind == ScriptTypeKind.TaskRef ? $"A task: {pin.Type.Family ?? "any type"}" : pin.Type.ToString(),
    };

    internal static string Title(TaskGraphNode node) => node.LatchCondition is { } condition
        ? $"When {condition} first holds"
        : node.Task.Label.Length > 0 ? node.Task.Label : node.Task.TypeName;

    // Logic tasks by what they do: flow, triggers, objectives and the rest.
    private static Bgra32 Header(string type, EditorTheme theme) => type switch
    {
        "ConditionalContainer" or "LevelFlow" or "LevelTimer" or "EditVariable" or "CutScene" => Bgra32.FromRgb(0x6A, 0x3E, 0x9C),
        "AreaActivate" or "Switch" or "Terminal" or "AlarmControl" or "SCamera" or "SCameraControl" => Bgra32.FromRgb(0x8C, 0x5A, 0x1E),
        "DefineComputerObjective" or "StatusMessage" or "ComputerHilight" => Bgra32.FromRgb(0x1E, 0x7A, 0x55),
        _ when type.StartsWith("Human", StringComparison.Ordinal) => Bgra32.FromRgb(0x8C, 0x2E, 0x2E),
        _ => theme.Chrome,
    };

    /// <summary>The reroute points a layout keeps for a wire, in canvas units.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="fromNode">The node the wire leaves.</param>
    /// <param name="fromPin">Its output pin.</param>
    /// <param name="toNode">The node it enters.</param>
    /// <param name="toPin">Its input pin.</param>
    /// <returns>The points, empty for a straight wire.</returns>
    internal static IReadOnlyList<UiVec2> ViaOf(GraphLayout layout, string fromNode, string fromPin, string toNode, string toPin) =>
        layout.Reroutes.TryGetValue(GraphLayout.WireKey(fromNode, fromPin, toNode, toPin), out List<GraphPoint>? points)
            ? [.. points.Select(static p => new UiVec2((float)p.X, (float)p.Y))]
            : [];

    private static UiVec2 Position(GraphLayout layout, GraphNodeKey key) =>
        layout.Positions.TryGetValue(key, out GraphPoint at) ? new UiVec2((float)at.X, (float)at.Y) : default;

    private static UiRect ToUi(GraphRect rect) => new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
}
