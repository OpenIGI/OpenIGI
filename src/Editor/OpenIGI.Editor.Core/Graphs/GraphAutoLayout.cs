namespace OpenIGI.Editor.Core.Graphs;

/// <summary>Places the shown nodes a layout has no position for, so a graph reads the first time it is opened.</summary>
/// <remarks>
/// Values flow left to right: a node's column is one past the furthest column of anything wired into it, so a condition's
/// operators stand left of the task that reads it, and the tasks whose fields they read stand further left again. Within a
/// column nodes keep the mission's order. Nodes the layout already places are left where they are, and their columns are
/// still counted, so placed and new nodes line up. Two tasks reading each other's fields make a cycle, which only bounds
/// how far columns grow.
/// </remarks>
public static class GraphAutoLayout
{
    /// <summary>The horizontal distance between columns, in canvas units.</summary>
    public const double ColumnWidth = 300;

    /// <summary>The vertical distance between nodes in a column, in canvas units.</summary>
    public const double RowHeight = 110;

    /// <summary>Gives every shown node without a position one.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">The layout to fill in.</param>
    /// <returns>How many nodes were placed.</returns>
    public static int Place(MissionGraph graph, GraphLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        List<GraphNode> shown = [.. graph.Nodes.Values.Where(IsShown)];
        Dictionary<GraphNodeKey, int> column = shown.ToDictionary(static n => n.Key, static _ => 0);
        ILookup<GraphNodeKey, GraphNodeKey> inputs = graph.Links
            .Where(l => column.ContainsKey(l.From.Node) && column.ContainsKey(l.To.Node) && l.From.Node != l.To.Node)
            .ToLookup(static l => l.To.Node, static l => l.From.Node);

        // Longest path by relaxation; a cycle stops growing once columns reach the node count.
        int limit = Math.Max(1, shown.Count);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (GraphNode node in shown)
            {
                int wanted = inputs[node.Key].Select(k => column[k] + 1).DefaultIfEmpty(0).Max();
                if (wanted > column[node.Key] && wanted < limit)
                {
                    column[node.Key] = wanted;
                    changed = true;
                }
            }
        }

        Dictionary<int, double> next = [];
        foreach (GraphNode node in shown)
        {
            int c = column[node.Key];
            double y = next.GetValueOrDefault(c);
            if (layout.Positions.TryGetValue(node.Key, out GraphPoint placed))
            {
                next[c] = Math.Max(y, placed.Y + RowHeight);
            }
        }

        int count = 0;
        foreach (GraphNode node in shown.Where(n => !layout.Positions.ContainsKey(n.Key)))
        {
            int c = column[node.Key];
            double y = next.GetValueOrDefault(c);
            layout.Positions[node.Key] = new GraphPoint(c * ColumnWidth, y);
            next[c] = y + RowHeight;
            count++;
        }

        return count;
    }

    // Expression nodes belong to their task: shown when it is.
    private static bool IsShown(GraphNode node) => node switch
    {
        TaskGraphNode task => task.IsShown,
        ExpressionGraphNode => true,
        _ => false,
    };
}
