using System.Globalization;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>A pin of an AI script node as drawn.</summary>
/// <param name="Id">The pin's id in the graph.</param>
/// <param name="Label">Its name.</param>
/// <param name="Type">What it carries.</param>
/// <param name="IsExec">Whether it carries control flow.</param>
/// <param name="Value">The literal written straight onto it, for an input fed by one; else <see langword="null"/>.</param>
public sealed record EventPinView(string Id, string Label, ScriptType Type, bool IsExec, string? Value = null);

/// <summary>An AI script node as drawn: a name for what it does, a line on what it is, and its pins.</summary>
/// <param name="Key">The node.</param>
/// <param name="Title">Its name.</param>
/// <param name="Subtitle">What it is.</param>
/// <param name="IsEvent">Whether it starts a chain: an event, or the script's start.</param>
/// <param name="IsPure">Whether it is a value, with no exec pins.</param>
/// <param name="Inputs">Its inputs.</param>
/// <param name="Outputs">Its outputs.</param>
/// <param name="Doc">A sentence on what it does.</param>
public sealed record EventNodeView(
    GraphNodeKey Key,
    string Title,
    string Subtitle,
    bool IsEvent,
    bool IsPure,
    IReadOnlyList<EventPinView> Inputs,
    IReadOnlyList<EventPinView> Outputs,
    string Doc);

/// <summary>One small graph of an AI script: what it does on one event, or what it does before any.</summary>
/// <param name="Key">Its key: the event branch's node key, or <c>setup</c>.</param>
/// <param name="Title">Its name: <c>On See Enemy</c>.</param>
/// <param name="Entry">The node its flow starts at.</param>
public sealed record EventScope(string Key, string Title, GraphNodeKey Entry)
{
    /// <summary>Gets the nodes drawn: its statements and the values they read, less literals written onto pins.</summary>
    public HashSet<GraphNodeKey> Members { get; } = [];
}

/// <summary>
/// Shows an AI script the way a node editor shows an event graph: split one graph per event, statements as exec nodes, the
/// values they read as pure nodes, literals and constants written straight onto the pins they feed.
/// </summary>
/// <remarks>
/// An AI script is a chain of <c>if (GetCurrentEventType() == AIEVENT_X)</c> branches; each one is an event, its body the
/// event's graph. Everything else at the top (statements before the chain, or after it) is the script's setup.
/// </remarks>
public static class EventGraphView
{
    /// <summary>The key of the scope holding what no event does.</summary>
    public const string SetupKey = "setup";

    private const double StatementGap = 90;
    private const double LaneGap = 40;
    private const double ValueWidth = 250;

    /// <summary>Splits a script graph into its events.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The setup first when it holds anything, then each event in the order the script tests them.</returns>
    public static IReadOnlyList<EventScope> Scopes(EventGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        EventStatementNode[] events = [.. graph.Nodes.Values.OfType<EventStatementNode>().Where(static n => n.Event is not null)
            .OrderBy(static n => n.Key.Value.Length).ThenBy(static n => n.Key.Value, StringComparer.Ordinal)];
        EventScope setup = new(SetupKey, "Setup", EventGraph.StartKey);
        List<EventScope> scopes = [];
        Dictionary<string, EventScope> byEvent = new(StringComparer.Ordinal);
        foreach (EventStatementNode node in events)
        {
            EventScope scope = new(node.Key.Value, "On " + EventName(node.Event!), node.Key);
            byEvent[node.Key.Value] = scope;
            scopes.Add(scope);
        }

        foreach (GraphNode node in graph.Nodes.Values)
        {
            if (IsInlined(node))
            {
                continue;
            }

            string statement = node.Key.Value.Split('/')[0];
            bool eventCondition = node is EventValueNode && byEvent.ContainsKey(statement) && node.Key.Value.StartsWith(statement + "/c", StringComparison.Ordinal);
            if (eventCondition)
            {
                continue;
            }

            // A statement belongs to the event whose body (path ".t") holds it; the events themselves head their own.
            EventScope? owner = byEvent.TryGetValue(statement, out EventScope? own) ? own : null;
            foreach ((string key, EventScope scope) in byEvent)
            {
                if (statement.StartsWith(key + ".t", StringComparison.Ordinal))
                {
                    owner = scope;
                    break;
                }
            }

            (owner ?? setup).Members.Add(node.Key);
        }

        bool setupDoesSomething = setup.Members.Any(k => graph.Nodes[k] is EventStatementNode { Event: null, Kind: not EventStatementKind.Start });
        return setupDoesSomething || scopes.Count == 0 ? [setup, .. scopes] : scopes;
    }

    /// <summary>Whether a value is written onto the pin it feeds rather than drawn: a literal or a name.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsInlined(GraphNode node) =>
        node is EventValueNode { Kind: EventValueKind.Integer or EventValueKind.Real or EventValueKind.Text or EventValueKind.Symbol };

    /// <summary>An event's name in words: <c>AIEVENT_SEE_ENEMY</c> is <c>See Enemy</c>.</summary>
    /// <param name="name">The event constant.</param>
    /// <returns>The words.</returns>
    public static string EventName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string rest = name.StartsWith("AIEVENT_", StringComparison.OrdinalIgnoreCase) ? name[8..] : name;
        return string.Join(' ', rest.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(static w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    /// <summary>Describes a node as it is drawn in a scope.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="scope">The scope it is drawn in.</param>
    /// <param name="key">The node.</param>
    /// <returns>Its view.</returns>
    public static EventNodeView Describe(EventGraph graph, EventScope scope, GraphNodeKey key)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scope);
        return graph.Nodes[key] switch
        {
            EventStatementNode node => Statement(graph, scope, node),
            EventValueNode value => Value(graph, value),
            _ => new EventNodeView(key, key.Value, string.Empty, false, true, [], [], string.Empty),
        };
    }

    /// <summary>A node's drawn height, in canvas units, matching the canvas: title bar, then a row for each pin on its longer side.</summary>
    /// <param name="view">The node's view.</param>
    /// <returns>The height.</returns>
    public static double HeightOf(EventNodeView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return 42 + 10 + (Math.Max(1, Math.Max(view.Inputs.Count, view.Outputs.Count)) * 24);
    }

    /// <summary>
    /// Lays out a scope as a node editor lays out exec flow: statements left to right along their wire, a branch's false
    /// path in a lane under its true path, and the values each statement reads in a band under it, leftwards by depth.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="layout">The layout to fill in; nodes already placed stay where they are.</param>
    /// <returns>How many nodes were placed.</returns>
    public static int Place(EventGraph graph, EventScope scope, GraphLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(layout);
        if (scope.Members.All(k => layout.Positions.ContainsKey(k)))
        {
            return 0;
        }

        ILookup<GraphPinRef, GraphNodeKey> next = graph.Links.ToLookup(static l => l.From, static l => l.To.Node);
        ILookup<GraphNodeKey, GraphNodeKey> valuesInto = graph.Links
            .Where(l => graph.Nodes.TryGetValue(l.From.Node, out GraphNode? from) && from is EventValueNode && scope.Members.Contains(l.From.Node))
            .ToLookup(static l => l.To.Node, static l => l.From.Node);
        int count = 0;

        void Put(GraphNodeKey key, double x, double y)
        {
            if (layout.Positions.TryAdd(key, new GraphPoint(x, y)))
            {
                count++;
            }
        }

        // A value tree under a node, its leaves furthest left: returns the bottom of what it used.
        double Values(GraphNodeKey into, double right, double top)
        {
            double y = top;
            foreach (GraphNodeKey value in valuesInto[into])
            {
                double x = right - ValueWidth;
                Put(value, x, y);
                double bottom = y + HeightOf(Describe(graph, scope, value));
                bottom = Math.Max(bottom, Values(value, x, y));
                y = bottom + LaneGap * 0.5;
            }

            return y;
        }

        // How far left a statement's values reach, so the statement before it leaves room.
        int Depth(GraphNodeKey into) => valuesInto[into].Select(v => 1 + Depth(v)).DefaultIfEmpty(0).Max();

        // A chain of statements from an exec pin: returns the right edge and bottom it reached.
        (double Right, double Bottom) Chain(GraphPinRef from, double x, double y)
        {
            double right = x;
            double bottom = y;
            foreach (GraphNodeKey key in next[from])
            {
                // An event of another graph on the way (the dispatch chain, read from the setup): pass through it, along
                // its else and what follows, without drawing it.
                if (!scope.Members.Contains(key))
                {
                    if (graph.Nodes[key] is EventStatementNode { Event: not null })
                    {
                        (double Right, double Bottom) otherwise = Chain(new GraphPinRef(key, EventStatementNode.False), x, y);
                        (double Right, double Bottom) after = Chain(new GraphPinRef(key, EventStatementNode.Next), Math.Max(x, otherwise.Right + StatementGap), y);
                        right = Math.Max(right, Math.Max(otherwise.Right, after.Right));
                        bottom = Math.Max(bottom, Math.Max(otherwise.Bottom, after.Bottom));
                    }

                    continue;
                }

                EventNodeView view = Describe(graph, scope, key);
                double at = x + (Depth(key) * ValueWidth);
                Put(key, at, y);
                double nodeBottom = y + HeightOf(view);
                double band = Values(key, at, nodeBottom + LaneGap);
                (double Right, double Bottom) reached = (at + 220, Math.Max(nodeBottom, band));
                if (graph.Nodes[key] is EventStatementNode { Kind: EventStatementKind.Branch or EventStatementKind.Loop } branch && branch.Event is null)
                {
                    (double Right, double Bottom) yes = Chain(new GraphPinRef(key, EventStatementNode.True), at + 220 + StatementGap, y);
                    (double Right, double Bottom) no = Chain(new GraphPinRef(key, EventStatementNode.False), at + 220 + StatementGap, Math.Max(yes.Bottom, reached.Bottom) + LaneGap);
                    reached = (Math.Max(yes.Right, no.Right), Math.Max(Math.Max(yes.Bottom, no.Bottom), reached.Bottom));
                }

                (double Right, double Bottom) rest = Chain(new GraphPinRef(key, EventStatementNode.Next), reached.Right + StatementGap, y);
                right = Math.Max(reached.Right, rest.Right);
                bottom = Math.Max(Math.Max(bottom, reached.Bottom), rest.Bottom);
            }

            return (right, bottom);
        }

        Put(scope.Entry, 0, 0);
        double entryBottom = HeightOf(Describe(graph, scope, scope.Entry));
        string first = scope.Entry == EventGraph.StartKey ? EventStatementNode.Next : EventStatementNode.True;
        Chain(new GraphPinRef(scope.Entry, first), 220 + StatementGap, 0);

        // Anything the walk missed (a statement no exec wire reaches) goes under the rest, so nothing is ever lost.
        double floor = scope.Members.Where(layout.Positions.ContainsKey).Select(k => layout.Positions[k].Y + HeightOf(Describe(graph, scope, k))).DefaultIfEmpty(entryBottom).Max() + LaneGap;
        foreach (GraphNodeKey key in scope.Members.Where(k => !layout.Positions.ContainsKey(k)).ToList())
        {
            Put(key, 0, floor);
            floor += HeightOf(Describe(graph, scope, key)) + LaneGap;
        }


        return count;
    }

    private static EventNodeView Statement(EventGraph graph, EventScope scope, EventStatementNode node)
    {
        static EventPinView ExecIn() => new(EventStatementNode.In, string.Empty, ScriptType.Exec, true);
        static EventPinView ExecOut(string id, string label) => new(id, label, ScriptType.Exec, true);
        if (node.Kind == EventStatementKind.Start)
        {
            return new(node.Key, "Script Start", "Runs once, before any event", true, false, [], [ExecOut(EventStatementNode.Next, string.Empty)], "Where the script begins.");
        }

        if (node.Event is { } name && scope.Entry == node.Key)
        {
            return new(node.Key, "On " + EventName(name), name, true, false, [], [ExecOut(EventStatementNode.True, string.Empty)], $"Runs when the soldier's current event is {name}.");
        }

        switch (node.Kind)
        {
            case EventStatementKind.Call:
            {
                (string words, string prefix) = ExpressionNodeText.Humanise(node.Text);
                List<EventPinView> inputs = [ExecIn(), .. Arguments(graph, node.Key, node.ArgumentCount, node.Native)];
                string subtitle = prefix.Length > 0 ? $"{prefix} · {(node.Native?.Category is { Length: > 0 } c ? ExpressionNodeText.Category(c) : "Native")}" : "Native";
                return new(node.Key, words, subtitle, false, false, inputs, [ExecOut(EventStatementNode.Next, string.Empty)], node.Native?.Doc ?? $"Calls {node.Text}.");
            }

            case EventStatementKind.Branch:
                return new(node.Key, "Branch", node.HasElse ? "if · else" : "if", false, false,
                    [ExecIn(), Input(graph, node.Key, EventStatementNode.Condition, "Condition", ScriptType.Bool)],
                    [ExecOut(EventStatementNode.True, "True"), ExecOut(EventStatementNode.False, "False"), ExecOut(EventStatementNode.Next, "Then")],
                    "Takes True when the condition holds, else False; then carries on.");

            case EventStatementKind.Loop:
                return new(node.Key, "While", "Loop", false, false,
                    [ExecIn(), Input(graph, node.Key, EventStatementNode.Condition, "Condition", ScriptType.Bool)],
                    [ExecOut(EventStatementNode.True, "Loop Body"), ExecOut(EventStatementNode.Next, "Completed")],
                    "Runs the body while the condition holds, then carries on.");

            default:
                return new(node.Key, "Do", "Statement", false, false,
                    [ExecIn(), Input(graph, node.Key, "0", "Value", ScriptType.Any)],
                    [ExecOut(EventStatementNode.Next, string.Empty)],
                    "Works out its value for what that does, such as setting a variable.");
        }
    }

    private static EventNodeView Value(EventGraph graph, EventValueNode node)
    {
        static EventPinView Out(ScriptType type) => new(GraphNode.Output, "Result", type, false);
        switch (node.Kind)
        {
            case EventValueKind.Call:
            {
                (string words, string prefix) = ExpressionNodeText.Humanise(node.Text);
                return new(node.Key, words, prefix.Length > 0 ? $"{prefix} · Pure" : "Pure", false, true,
                    Arguments(graph, node.Key, node.InputCount, node.Native), [Out(node.Native?.Returns ?? ScriptType.Any)], node.Native?.Doc ?? $"Calls {node.Text} for its value.");
            }

            case EventValueKind.Unary:
                (string unaryTitle, string symbol) = node.Unary switch
                {
                    QscUnaryOperator.LogicalNot => ("NOT", "!"),
                    QscUnaryOperator.Minus => ("Negate", "-"),
                    QscUnaryOperator.BitwiseNot => ("Bitwise NOT", "~"),
                    _ => ("Plus", "+"),
                };
                return new(node.Key, unaryTitle, symbol + " value", false, true, [Input(graph, node.Key, "0", "Value", ScriptType.Any)], [Out(node.Unary == QscUnaryOperator.LogicalNot ? ScriptType.Bool : ScriptType.Any)], $"{unaryTitle} of the value.");

            case EventValueKind.Binary:
                (string title, string op, bool boolean) = Binary(node.Binary);
                return new(node.Key, title, $"A {op} B", false, true,
                    [Input(graph, node.Key, "0", "A", ScriptType.Any), Input(graph, node.Key, "1", "B", ScriptType.Any)], [Out(boolean ? ScriptType.Bool : ScriptType.Any)], $"A {op} B.");

            case EventValueKind.Assignment:
                return new(node.Key, "Set", "target = value", false, true,
                    [Input(graph, node.Key, "0", "Target", ScriptType.Any), Input(graph, node.Key, "1", "Value", ScriptType.Any)], [Out(ScriptType.Any)], "Writes the value into the target.");

            default:
                return new(node.Key, Literal(node) ?? node.Key.Value, "Value", false, true, [], [Out(ScriptType.Any)], "A value.");
        }
    }

    private static List<EventPinView> Arguments(EventGraph graph, GraphNodeKey key, int count, NativeSignature? native)
    {
        List<EventPinView> pins = [];
        for (int i = 0; i < count; i++)
        {
            NativeParameter? parameter = native is not null && i < native.Parameters.Count ? native.Parameters[i] : null;
            string id = i.ToString(CultureInfo.InvariantCulture);
            pins.Add(Input(graph, key, id, parameter?.Name is { Length: > 0 } name ? name : $"Argument {i + 1}", parameter?.Type ?? ScriptType.Any));
        }

        return pins;
    }

    // An input, with the literal feeding it written onto it.
    private static EventPinView Input(EventGraph graph, GraphNodeKey key, string pin, string label, ScriptType type)
    {
        GraphLink? link = graph.Links.FirstOrDefault(l => l.To.Node == key && l.To.Pin == pin);
        string? value = link is not null && graph.Nodes.TryGetValue(link.From.Node, out GraphNode? from) && IsInlined(from) ? Literal((EventValueNode)from) : null;
        return new EventPinView(pin, label, type, false, value);
    }

    private static string? Literal(EventValueNode node) => node.Kind switch
    {
        EventValueKind.Integer => node.Integer.ToString(CultureInfo.InvariantCulture),
        EventValueKind.Real => node.Real.ToString("0.###", CultureInfo.InvariantCulture),
        EventValueKind.Text => $"\"{node.Text}\"",
        EventValueKind.Symbol => node.Text,
        _ => null,
    };

    private static (string Title, string Symbol, bool Boolean) Binary(QscBinaryOperator op) => op switch
    {
        QscBinaryOperator.Add => ("Add", "+", false),
        QscBinaryOperator.Subtract => ("Subtract", "-", false),
        QscBinaryOperator.Multiply => ("Multiply", "*", false),
        QscBinaryOperator.Divide => ("Divide", "/", false),
        QscBinaryOperator.ShiftLeft => ("Shift Left", "<<", false),
        QscBinaryOperator.ShiftRight => ("Shift Right", ">>", false),
        QscBinaryOperator.BitwiseAnd => ("Bitwise AND", "&", false),
        QscBinaryOperator.BitwiseOr => ("Bitwise OR", "|", false),
        QscBinaryOperator.BitwiseXor => ("Bitwise XOR", "^", false),
        QscBinaryOperator.LogicalAnd => ("AND", "&&", true),
        QscBinaryOperator.LogicalOr => ("OR", "||", true),
        QscBinaryOperator.Equal => ("Equal", "==", true),
        QscBinaryOperator.NotEqual => ("Not Equal", "!=", true),
        QscBinaryOperator.Less => ("Less", "<", true),
        QscBinaryOperator.LessOrEqual => ("Less or Equal", "<=", true),
        QscBinaryOperator.Greater => ("Greater", ">", true),
        _ => ("Greater or Equal", ">=", true),
    };
}
