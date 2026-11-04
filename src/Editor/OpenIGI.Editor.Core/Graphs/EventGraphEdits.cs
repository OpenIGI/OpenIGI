using System.Globalization;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>
/// One undoable change to an AI script's graph: nodes and wires taken out, then nodes and wires put in. Every edit the graph
/// panel makes (wiring, unwiring, deleting, adding a native, setting a value) is one of these.
/// </summary>
public sealed class EventGraphEditCommand : IEditorCommand
{
    private readonly EventGraph _graph;
    private readonly Action _changed;
    private readonly List<GraphNode> _removedNodes;
    private readonly List<GraphLink> _removedLinks;
    private readonly List<GraphNode> _addedNodes;
    private readonly List<GraphLink> _addedLinks;

    /// <summary>Initializes a new instance of the <see cref="EventGraphEditCommand"/> class.</summary>
    /// <param name="name">What the history lists it as.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="removedNodes">Nodes taken out (their wires are found and restored on undo).</param>
    /// <param name="removedLinks">Wires taken out.</param>
    /// <param name="addedNodes">Nodes put in.</param>
    /// <param name="addedLinks">Wires put in.</param>
    /// <param name="changed">Called after it applies or undoes, so views rebuild.</param>
    public EventGraphEditCommand(
        string name,
        EventGraph graph,
        IEnumerable<GraphNode> removedNodes,
        IEnumerable<GraphLink> removedLinks,
        IEnumerable<GraphNode> addedNodes,
        IEnumerable<GraphLink> addedLinks,
        Action changed)
    {
        Name = name;
        _graph = graph;
        _changed = changed;
        _removedNodes = [.. removedNodes];
        _addedNodes = [.. addedNodes];
        _addedLinks = [.. addedLinks];

        // A removed node takes its wires with it: remember them all, so undo puts every one back.
        HashSet<GraphNodeKey> gone = [.. _removedNodes.Select(static n => n.Key)];
        _removedLinks = [.. removedLinks.Concat(graph.Links.Where(l => gone.Contains(l.From.Node) || gone.Contains(l.To.Node))).Distinct()];
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public void Apply()
    {
        foreach (GraphLink link in _removedLinks)
        {
            _graph.RemoveLink(link);
        }

        foreach (GraphNode node in _removedNodes)
        {
            _graph.RemoveNode(node.Key);
        }

        foreach (GraphNode node in _addedNodes)
        {
            _graph.AddNode(node);
        }

        foreach (GraphLink link in _addedLinks)
        {
            _graph.AddLink(link);
        }

        _changed();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        foreach (GraphLink link in _addedLinks)
        {
            _graph.RemoveLink(link);
        }

        foreach (GraphNode node in _addedNodes)
        {
            _graph.RemoveNode(node.Key);
        }

        foreach (GraphNode node in _removedNodes)
        {
            _graph.AddNode(node);
        }

        foreach (GraphLink link in _removedLinks)
        {
            _graph.AddLink(link);
        }

        _changed();
    }
}

/// <summary>Builds the edits the graph panel makes to an AI script, each as one <see cref="EventGraphEditCommand"/>.</summary>
public static class EventGraphEdits
{
    /// <summary>
    /// Wires an output into an input. An input takes one wire and an exec output gives one, so whatever was there is replaced,
    /// and a value node no longer read is dropped with it.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="from">The output.</param>
    /// <param name="to">The input.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command, or <see langword="null"/> when the wire would join exec to a value.</returns>
    public static EventGraphEditCommand? Link(EventGraph graph, GraphPinRef from, GraphPinRef to, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (IsExec(graph, from) != IsExec(graph, to))
        {
            return null;
        }

        List<GraphLink> removed = [.. graph.Links.Where(l => l.To == to)];
        if (IsExec(graph, from))
        {
            removed.AddRange(graph.Links.Where(l => l.From == from));
        }

        List<GraphNode> gone = Orphans(graph, removed);
        List<GraphNode> added = [];
        List<GraphLink> wires = [new GraphLink(from, to)];

        // An if written without an else gets one when its False side is wired, or the script would drop that side.
        if (from.Pin == EventStatementNode.False && graph.Nodes.GetValueOrDefault(from.Node) is EventStatementNode { Kind: EventStatementKind.Branch, HasElse: false } branch)
        {
            gone.Add(branch);
            added.Add(new EventStatementNode(branch.Key, branch.Kind, branch.Text) { Native = branch.Native, ArgumentCount = branch.ArgumentCount, HasElse = true, Event = branch.Event });
            wires.AddRange(graph.Links.Where(l => (l.From.Node == branch.Key || l.To.Node == branch.Key) && !removed.Contains(l)));
        }

        return new EventGraphEditCommand("Wire", graph, gone, removed, added, wires, changed);
    }

    /// <summary>Takes the wires off a pin; a value no longer read goes with them.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="pin">The pin.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command, or <see langword="null"/> when nothing is wired there.</returns>
    public static EventGraphEditCommand? Unlink(EventGraph graph, GraphPinRef pin, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<GraphLink> removed = [.. graph.Links.Where(l => l.To == pin || l.From == pin)];
        return removed.Count == 0 ? null : new EventGraphEditCommand("Unwire", graph, Orphans(graph, removed), removed, [], [], changed);
    }

    /// <summary>
    /// Deletes nodes: a statement with the values it reads, its exec chain closed over the gap so the statements after it still
    /// run; a value by itself. The script's start and its events are kept.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="keys">The nodes.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command, or <see langword="null"/> when nothing could be deleted.</returns>
    public static EventGraphEditCommand? Delete(EventGraph graph, IEnumerable<GraphNodeKey> keys, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        HashSet<GraphNodeKey> doomed = [];
        List<GraphLink> bridges = [];
        foreach (GraphNodeKey key in keys)
        {
            switch (graph.Nodes.GetValueOrDefault(key))
            {
                case EventStatementNode { Kind: EventStatementKind.Start } or EventStatementNode { Event: not null }:
                    continue;

                case EventStatementNode:
                    doomed.Add(key);
                    doomed.UnionWith(ValuesInto(graph, key));
                    GraphPinRef? after = graph.Links.FirstOrDefault(l => l.From == new GraphPinRef(key, EventStatementNode.Next))?.To;
                    if (after is { } next)
                    {
                        bridges.AddRange(graph.Links.Where(l => l.To == new GraphPinRef(key, EventStatementNode.In)).Select(l => new GraphLink(l.From, next)));
                    }

                    break;

                case EventValueNode:
                    doomed.Add(key);
                    doomed.UnionWith(ValuesInto(graph, key));
                    break;
            }
        }

        // A bridge through two deleted statements in a row ends at the first one kept.
        bridges = [.. bridges.Where(b => !doomed.Contains(b.From.Node) && !doomed.Contains(b.To.Node))];
        return doomed.Count == 0
            ? null
            : new EventGraphEditCommand(doomed.Count == 1 ? "Delete node" : "Delete nodes", graph, [.. doomed.Select(k => graph.Nodes[k])], [], [], bridges, changed);
    }

    /// <summary>Adds a call to a native as a statement, each argument given a default literal to edit.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="native">The native.</param>
    /// <param name="scope">The event it is added to, whose body its key goes in so it is drawn there.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command and the new node's key.</returns>
    public static (EventGraphEditCommand Command, GraphNodeKey Key) AddCall(EventGraph graph, NativeSignature native, EventScope scope, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(scope);
        GraphNodeKey key = NewKey(graph, scope);
        List<GraphNode> nodes = [new EventStatementNode(key, EventStatementKind.Call, native.Name) { Native = native, ArgumentCount = native.Parameters.Count }];
        List<GraphLink> links = [];
        for (int i = 0; i < native.Parameters.Count; i++)
        {
            string pin = i.ToString(CultureInfo.InvariantCulture);
            GraphNodeKey value = new($"{key.Value}/{pin}");
            nodes.Add(Default(value, native.Parameters[i].Type));
            links.Add(new GraphLink(new GraphPinRef(value, GraphNode.Output), new GraphPinRef(key, pin)));
        }

        return (new EventGraphEditCommand($"Add {native.Name}", graph, [], [], nodes, links, changed), key);
    }

    /// <summary>
    /// Adds a Branch (an <c>if</c>, with an else to wire) or a While, its condition a literal to replace: a branch's true,
    /// a loop's false, so a new loop never spins before its condition is wired.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="kind"><see cref="EventStatementKind.Branch"/> or <see cref="EventStatementKind.Loop"/>.</param>
    /// <param name="scope">The event it is added to.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command and the new node's key.</returns>
    public static (EventGraphEditCommand Command, GraphNodeKey Key) AddFlow(EventGraph graph, EventStatementKind kind, EventScope scope, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scope);
        if (kind is not (EventStatementKind.Branch or EventStatementKind.Loop))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only a branch or a loop is flow.");
        }

        GraphNodeKey key = NewKey(graph, scope);
        GraphNodeKey condition = new($"{key.Value}/c");
        GraphNode[] nodes =
        [
            new EventStatementNode(key, kind, string.Empty) { HasElse = kind == EventStatementKind.Branch },
            new EventValueNode(condition, EventValueKind.Integer, 0) { Integer = kind == EventStatementKind.Branch ? 1 : 0 },
        ];
        GraphLink[] links = [new GraphLink(new GraphPinRef(condition, GraphNode.Output), new GraphPinRef(key, EventStatementNode.Condition))];
        return (new EventGraphEditCommand(kind == EventStatementKind.Branch ? "Add Branch" : "Add While", graph, [], [], nodes, links, changed), key);
    }

    /// <summary>
    /// Adds a value node, free until its output is wired into an input: a pure native (a query, such as a distance), a binary
    /// operator (a comparison, AND/OR, arithmetic) or a unary one (NOT, negate), each operand a literal to replace.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="value">The value, its kind, operator or native set and its inputs counted; its key is replaced.</param>
    /// <param name="scope">The event it is added to.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command and the new node's key.</returns>
    public static (EventGraphEditCommand Command, GraphNodeKey Key) AddValue(EventGraph graph, EventValueNode value, EventScope scope, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(scope);

        // Keyed as a value of a statement in the event's body, so the event's graph draws it.
        GraphNodeKey key = new(NewKey(graph, scope).Value + "/v");
        EventValueNode node = new(key, value.Kind, value.InputCount)
        {
            Text = value.Text,
            Native = value.Native,
            Unary = value.Unary,
            Binary = value.Binary,
        };
        List<GraphNode> nodes = [node];
        List<GraphLink> links = [];
        for (int i = 0; i < value.InputCount; i++)
        {
            string pin = i.ToString(CultureInfo.InvariantCulture);
            GraphNodeKey operand = new($"{key.Value}.{pin}");
            ScriptType type = value.Native is { } native && i < native.Parameters.Count ? native.Parameters[i].Type : ScriptType.Int;
            nodes.Add(Default(operand, type));
            links.Add(new GraphLink(new GraphPinRef(operand, GraphNode.Output), new GraphPinRef(key, pin)));
        }

        string name = value.Native?.Name ?? (value.Kind == EventValueKind.Unary ? value.Unary.ToString() : value.Binary.ToString());
        return (new EventGraphEditCommand($"Add {name}", graph, [], [], nodes, links, changed), key);
    }

    // A key in the event's body, so it is drawn in the event's graph: <event>.txN, or sxN for the setup.
    private static GraphNodeKey NewKey(EventGraph graph, EventScope scope)
    {
        string body = scope.Key == EventGraphView.SetupKey ? "sx" : scope.Key + ".tx";
        int n = 1;
        while (graph.Nodes.Keys.Any(k => k.Value == body + n.ToString(CultureInfo.InvariantCulture)
            || k.Value.StartsWith(body + n.ToString(CultureInfo.InvariantCulture) + "/", StringComparison.Ordinal)))
        {
            n++;
        }

        return new GraphNodeKey(body + n.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Writes a literal straight onto an input, as a node editor's inline field does: a whole number, a real, <c>"text"</c>, or
    /// a name (a constant such as <c>AIEVENT_IDLE</c>, or a variable).
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="input">The input.</param>
    /// <param name="text">What was typed.</param>
    /// <param name="changed">Called after it applies or undoes.</param>
    /// <returns>The command, or <see langword="null"/> when the text is not a value.</returns>
    public static EventGraphEditCommand? SetValue(EventGraph graph, GraphPinRef input, string text, Action changed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(text);
        GraphNodeKey key = new($"{input.Node.Value}/{input.Pin}");
        for (int n = 2; graph.Nodes.ContainsKey(key) && !graph.Links.Any(l => l.From.Node == key && l.To == input); n++)
        {
            key = new GraphNodeKey($"{input.Node.Value}/{input.Pin}~{n.ToString(CultureInfo.InvariantCulture)}");
        }

        if (Parse(key, text.Trim()) is not { } literal)
        {
            return null;
        }

        List<GraphLink> removed = [.. graph.Links.Where(l => l.To == input)];
        List<GraphNode> gone = Orphans(graph, removed);
        if (graph.Nodes.TryGetValue(key, out GraphNode? same) && !gone.Contains(same))
        {
            gone.Add(same);
        }

        return new EventGraphEditCommand("Set value", graph, gone, removed, [literal], [new GraphLink(new GraphPinRef(key, GraphNode.Output), input)], changed);
    }

    /// <summary>Whether a typed text is a value <see cref="SetValue"/> takes.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsValue(string text) => Parse(new GraphNodeKey("probe"), (text ?? string.Empty).Trim()) is not null;

    private static EventValueNode? Parse(GraphNodeKey key, string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
        {
            return new EventValueNode(key, EventValueKind.Integer, 0) { Integer = integer };
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float real))
        {
            return new EventValueNode(key, EventValueKind.Real, 0) { Real = real };
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return new EventValueNode(key, EventValueKind.Text, 0) { Text = text[1..^1] };
        }

        bool name = (char.IsLetter(text[0]) || text[0] == '_') && text.All(static c => char.IsLetterOrDigit(c) || c == '_');
        return name ? new EventValueNode(key, EventValueKind.Symbol, 0) { Text = text } : null;
    }

    private static EventValueNode Default(GraphNodeKey key, ScriptType type) => type.Kind switch
    {
        ScriptTypeKind.Real => new EventValueNode(key, EventValueKind.Real, 0) { Real = 0f },
        ScriptTypeKind.Text => new EventValueNode(key, EventValueKind.Text, 0) { Text = string.Empty },
        _ => new EventValueNode(key, EventValueKind.Integer, 0) { Integer = 0 },
    };

    private static bool IsExec(EventGraph graph, GraphPinRef pin) =>
        graph.Nodes.GetValueOrDefault(pin.Node) is EventStatementNode
        && pin.Pin is EventStatementNode.In or EventStatementNode.Next or EventStatementNode.True or EventStatementNode.False;

    // The values a node reads, however deep.
    private static HashSet<GraphNodeKey> ValuesInto(EventGraph graph, GraphNodeKey key)
    {
        HashSet<GraphNodeKey> found = [];
        Stack<GraphNodeKey> open = new([key]);
        while (open.TryPop(out GraphNodeKey at))
        {
            foreach (GraphLink link in graph.Links.Where(l => l.To.Node == at && graph.Nodes.GetValueOrDefault(l.From.Node) is EventValueNode))
            {
                if (found.Add(link.From.Node))
                {
                    open.Push(link.From.Node);
                }
            }
        }

        return found;
    }

    // Value nodes that the removed wires leave read by nothing, with what they read.
    private static List<GraphNode> Orphans(EventGraph graph, List<GraphLink> removed)
    {
        List<GraphNode> orphans = [];
        foreach (GraphLink link in removed)
        {
            if (graph.Nodes.GetValueOrDefault(link.From.Node) is EventValueNode value
                && !graph.Links.Any(l => l.From.Node == value.Key && !removed.Contains(l)))
            {
                orphans.Add(value);
                orphans.AddRange(ValuesInto(graph, value.Key).Select(k => graph.Nodes[k]));
            }
        }

        return [.. orphans.Distinct()];
    }
}
