using System.Globalization;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>Why an event graph cannot be written as a script.</summary>
/// <param name="message">What is wrong.</param>
public sealed class EventGraphWriteException(string message) : Exception(message);

/// <summary>Writes an event graph back as the script's syntax tree, which compiles to its image.</summary>
/// <remarks>
/// A script is a tree of statement lists, so the graph's exec wires must form one: each statement reached by exactly one
/// wire, every chain ending. Two wires into one statement (both arms of a branch going on to the same action) cannot be
/// written without copying the statement, and the writer refuses rather than copy behind the user's back; the graph check
/// reports it with a fix. A value input with nothing wired in is an error for the same reason: a script has no "nothing".
/// </remarks>
public static class EventGraphWriter
{
    /// <summary>Writes the graph as a script.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The script, with the header the graph was read with.</returns>
    /// <exception cref="EventGraphWriteException">The exec wires do not form a tree, or an input has nothing wired in.</exception>
    public static QscScript Write(EventGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Dictionary<GraphPinRef, List<GraphNodeKey>> exec = [];
        Dictionary<GraphPinRef, GraphNodeKey> values = [];
        Dictionary<GraphNodeKey, int> entries = [];
        foreach (GraphLink link in graph.Links)
        {
            if (link.To.Pin == EventStatementNode.In)
            {
                if (!exec.TryGetValue(link.From, out List<GraphNodeKey>? targets))
                {
                    exec[link.From] = targets = [];
                }

                targets.Add(link.To.Node);
                entries[link.To.Node] = entries.GetValueOrDefault(link.To.Node) + 1;
            }
            else
            {
                values[link.To] = link.From.Node;
            }
        }

        if (entries.FirstOrDefault(static e => e.Value > 1) is { Key.Value: not null } merged)
        {
            throw new EventGraphWriteException($"{graph.Nodes[merged.Key].Title} is reached by more than one exec wire.");
        }

        Writer writer = new(graph, exec, values);
        IReadOnlyList<QscStatement> statements = writer.List(new GraphPinRef(EventGraph.StartKey, EventStatementNode.Next));
        QscScript header = graph.Header;
        return new QscScript([.. statements], header.Version)
        {
            RequestedStackSizeBytes = header.RequestedStackSizeBytes,
            RequestedMaxLocals = header.RequestedMaxLocals,
            FileInfo = header.FileInfo,
        };
    }

    private sealed class Writer(
        EventGraph graph,
        Dictionary<GraphPinRef, List<GraphNodeKey>> exec,
        Dictionary<GraphPinRef, GraphNodeKey> values)
    {
        public List<QscStatement> List(GraphPinRef from)
        {
            List<QscStatement> statements = [];
            HashSet<GraphNodeKey> seen = [];
            GraphPinRef at = from;
            while (exec.TryGetValue(at, out List<GraphNodeKey>? targets))
            {
                if (targets.Count > 1)
                {
                    throw new EventGraphWriteException($"{graph.Nodes[at.Node].Title} has more than one wire out of {at.Pin}.");
                }

                GraphNodeKey key = targets[0];
                if (!seen.Add(key))
                {
                    throw new EventGraphWriteException($"{graph.Nodes[key].Title} loops back into its own chain.");
                }

                statements.Add(Statement((EventStatementNode)graph.Nodes[key]));
                at = new GraphPinRef(key, EventStatementNode.Next);
            }

            return statements;
        }

        private QscStatement Statement(EventStatementNode node) => node.Kind switch
        {
            EventStatementKind.Call => new QscExpressionStatement(
                new QscCall(node.Text, [.. Enumerable.Range(0, node.ArgumentCount).Select(i => Input(node.Key, Pin(i)))])),
            EventStatementKind.Expression => new QscExpressionStatement(Input(node.Key, "0")),
            EventStatementKind.Branch => new QscIf(
                Input(node.Key, EventStatementNode.Condition),
                List(new GraphPinRef(node.Key, EventStatementNode.True)),
                node.HasElse ? List(new GraphPinRef(node.Key, EventStatementNode.False)) : null),
            EventStatementKind.Loop => new QscWhile(
                Input(node.Key, EventStatementNode.Condition),
                List(new GraphPinRef(node.Key, EventStatementNode.True))),
            _ => throw new EventGraphWriteException($"{node.Title} cannot stand inside a script."),
        };

        private QscExpression Input(GraphNodeKey node, string pin) =>
            values.TryGetValue(new GraphPinRef(node, pin), out GraphNodeKey source)
                ? Value((EventValueNode)graph.Nodes[source])
                : throw new EventGraphWriteException($"{graph.Nodes[node].Title} has nothing wired into {pin}.");

        private QscExpression Value(EventValueNode node) => node.Kind switch
        {
            EventValueKind.Integer => new QscIntLiteral(node.Integer),
            EventValueKind.Real => new QscRealLiteral(node.Real),
            EventValueKind.Text => new QscStringLiteral(node.Text),
            EventValueKind.Symbol => new QscSymbol(node.Text),
            EventValueKind.Call => new QscCall(node.Text, [.. Enumerable.Range(0, node.InputCount).Select(i => Input(node.Key, Pin(i)))]),
            EventValueKind.Unary => new QscUnary(node.Unary, Input(node.Key, "0")),
            EventValueKind.Binary => new QscBinary(node.Binary, Input(node.Key, "0"), Input(node.Key, "1")),
            EventValueKind.Assignment => new QscAssignment(Input(node.Key, "0"), Input(node.Key, "1")),
            _ => throw new EventGraphWriteException($"Unknown value {node.Kind}."),
        };

        private static string Pin(int index) => index.ToString(CultureInfo.InvariantCulture);
    }
}
