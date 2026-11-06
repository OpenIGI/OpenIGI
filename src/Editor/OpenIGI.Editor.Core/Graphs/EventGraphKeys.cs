namespace OpenIGI.Editor.Core.Graphs;

/// <summary>
/// Matches the nodes of two graphs of one script: a graph as edited, and the graph read back from what it compiled to, whose
/// keys follow the script's shape anew. The layout an edited graph is saved with is moved onto the new keys, so the next time
/// the script is read its nodes come back where they were left.
/// </summary>
public static class EventGraphKeys
{
    /// <summary>Walks both graphs in step from their starts: along each exec output in turn, and into each value input.</summary>
    /// <param name="edited">The graph as edited.</param>
    /// <param name="read">The graph read back from the script it was written to.</param>
    /// <returns>Each edited node's key in the read graph; nodes the script does not hold (left unwired) are not in it.</returns>
    public static IReadOnlyDictionary<GraphNodeKey, GraphNodeKey> Map(EventGraph edited, EventGraph read)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(read);
        Dictionary<GraphNodeKey, GraphNodeKey> map = [];
        Dictionary<GraphPinRef, GraphNodeKey> editedNext = Targets(edited);
        Dictionary<GraphPinRef, GraphNodeKey> readNext = Targets(read);
        Dictionary<GraphPinRef, GraphNodeKey> editedInto = Sources(edited);
        Dictionary<GraphPinRef, GraphNodeKey> readInto = Sources(read);
        Stack<(GraphNodeKey A, GraphNodeKey B)> open = new([(EventGraph.StartKey, EventGraph.StartKey)]);
        while (open.TryPop(out (GraphNodeKey A, GraphNodeKey B) pair))
        {
            if (!map.TryAdd(pair.A, pair.B))
            {
                continue;
            }

            foreach (string pin in (string[])[EventStatementNode.Next, EventStatementNode.True, EventStatementNode.False])
            {
                if (editedNext.TryGetValue(new GraphPinRef(pair.A, pin), out GraphNodeKey a) && readNext.TryGetValue(new GraphPinRef(pair.B, pin), out GraphNodeKey b))
                {
                    open.Push((a, b));
                }
            }

            foreach ((GraphPinRef input, GraphNodeKey source) in editedInto.Where(p => p.Key.Node == pair.A))
            {
                if (readInto.TryGetValue(input with { Node = pair.B }, out GraphNodeKey other))
                {
                    open.Push((source, other));
                }
            }
        }

        return map;
    }

    // Where each exec output leads.
    private static Dictionary<GraphPinRef, GraphNodeKey> Targets(EventGraph graph) =>
        graph.Links.Where(l => l.To.Pin == EventStatementNode.In).GroupBy(static l => l.From).ToDictionary(static g => g.Key, static g => g.First().To.Node);

    // What feeds each value input.
    private static Dictionary<GraphPinRef, GraphNodeKey> Sources(EventGraph graph) =>
        graph.Links.Where(l => l.To.Pin != EventStatementNode.In && graph.Nodes.GetValueOrDefault(l.From.Node) is EventValueNode)
            .GroupBy(static l => l.To).ToDictionary(static g => g.Key, static g => g.First().From.Node);
}
