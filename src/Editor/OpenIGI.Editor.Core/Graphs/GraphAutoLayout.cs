using OpenIGI.Core.Collections;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>Places the nodes of a scope that its layout has no position for, so a graph reads the first time it is opened.</summary>
/// <remarks>
/// <para>
/// One scope at a time (<see cref="GraphScopes"/>): its reference nodes stack in a column on the left, and its own nodes flow
/// left to right after them, a node's column one past the furthest of the members wired into it, so a condition's operators
/// stand left of the task that reads them.
/// </para>
/// <para>
/// Nodes already placed are left where they are; new ones in a scope that already has a layout go below it. Two tasks reading
/// each other's fields make a cycle, which only bounds how far columns grow.
/// </para>
/// </remarks>
public static class GraphAutoLayout
{
    /// <summary>The horizontal distance between columns, in canvas units.</summary>
    public const double ColumnWidth = 280;

    /// <summary>The vertical distance a reference column steps by, in canvas units.</summary>
    public const double RowHeight = 120;

    private const double NodeGap = 28;

    /// <summary>Places every scope of a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">The layout to fill in.</param>
    /// <returns>How many nodes were placed.</returns>
    public static int Place(MissionGraph graph, GraphLayout layout)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return GraphScopes.Build(graph).Sum(scope => Place(graph, layout, scope));
    }

    /// <summary>Gives every node of one scope without a position one, under the scope's own key for it.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="layout">The layout to fill in.</param>
    /// <param name="scope">The scope.</param>
    /// <returns>How many nodes were placed.</returns>
    public static int Place(MissionGraph graph, GraphLayout layout, GraphScope scope)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(scope);
        List<GraphNodeKey> members = [.. graph.Nodes.Keys.Where(scope.Members.Contains)];
        List<GraphNodeKey> references = [.. graph.Nodes.Keys.Where(scope.References.Contains)];
        GraphNodeKey[] all = [.. references, .. members];
        if (Array.TrueForAll(all, k => layout.Positions.ContainsKey(scope.LayoutKey(k))))
        {
            return 0;
        }

        // What is new goes below what the scope already has.
        GraphPoint[] placed = [.. all.Select(scope.LayoutKey).Where(layout.Positions.ContainsKey).Select(k => layout.Positions[k])];
        double top = placed.Length == 0 ? 0 : placed.Max(static p => p.Y) + 400;
        double left = placed.Length == 0 ? 0 : placed.Min(static p => p.X);

        // Members flow left to right by data, from column one; each is placed level with what feeds it, so wires run straight.
        HashSet<GraphNodeKey> drawn = [.. all];
        List<GraphLink> links = [.. graph.Links
            .Select(l => new GraphLink(l.From with { Node = scope.DrawnAs(l.From.Node) }, l.To with { Node = scope.DrawnAs(l.To.Node) }))
            .Where(l => drawn.Contains(l.From.Node) && drawn.Contains(l.To.Node) && l.From.Node != l.To.Node)];
        Dictionary<GraphNodeKey, (int Column, int Row)> cells = Columns(links, members, 1);
        Dictionary<GraphNodeKey, double> y = [];
        Dictionary<int, List<(double Top, double Bottom)>> columns = [];
        int count = 0;

        void Put(GraphNodeKey key, int column, double target)
        {
            double height = HeightOf(graph, key, scope);
            List<(double Top, double Bottom)> taken = columns.GetOrAdd(column);
            double at = Math.Max(top, target);
            for (bool moved = true; moved;)
            {
                moved = false;
                foreach ((double t, double b) in taken)
                {
                    if (at < b + NodeGap && t < at + height + NodeGap)
                    {
                        at = b + NodeGap;
                        moved = true;
                    }
                }
            }

            taken.Add((at, at + height));
            y[key] = at;
            if (layout.Positions.TryAdd(scope.LayoutKey(key), new GraphPoint(left + (column * ColumnWidth), at)))
            {
                count++;
            }
            else
            {
                y[key] = layout.Positions[scope.LayoutKey(key)].Y;
            }
        }

        foreach ((GraphNodeKey key, (int column, int _)) in cells.OrderBy(static c => c.Value.Column).ThenBy(static c => c.Value.Row))
        {
            double[] fed = [.. links.Where(l => l.To.Node == key && y.ContainsKey(l.From.Node)).Select(l => y[l.From.Node])];
            Put(key, column, fed.Length > 0 ? fed.Average() : columns.TryGetValue(column, out List<(double, double)>? list) && list.Count > 0 ? list.Max(static t => t.Item2) + NodeGap : top);
        }

        // A reference stands just left of the first member that reads it, level with it; one a member feeds, just right.
        foreach (GraphNodeKey key in references.OrderBy(k => Readers(links, k, cells).Select(r => y[r]).DefaultIfEmpty(double.MaxValue).Min()))
        {
            GraphNodeKey[] readers = Readers(links, key, cells);
            GraphNodeKey[] feeders = [.. links.Where(l => l.To.Node == key && cells.ContainsKey(l.From.Node)).Select(static l => l.From.Node)];
            if (readers.Length > 0)
            {
                GraphNodeKey first = readers.MinBy(r => cells[r].Column);
                Put(key, cells[first].Column - 1, readers.Where(r => cells[r].Column == cells[first].Column).Average(r => y[r]));
            }
            else if (feeders.Length > 0)
            {
                GraphNodeKey last = feeders.MaxBy(f => cells[f].Column);
                Put(key, cells[last].Column + 1, y[last]);
            }
            else
            {
                Put(key, 0, top);
            }
        }

        return count;
    }

    /// <summary>
    /// A node's drawn height, in canvas units: a title bar with its subtitle, then a row for each pin on its longer side. The
    /// same sizes the canvas draws with, so the layout leaves room for what is drawn.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="key">The node.</param>
    /// <returns>The height.</returns>
    public static double HeightOf(MissionGraph graph, GraphNodeKey key)
    {
        ArgumentNullException.ThrowIfNull(graph);
        int rows = graph.Nodes[key] switch
        {
            TaskGraphNode task => Math.Max(task.Definition.Inputs.Count(static p => p.IsLinkable), task.Definition.Outputs.Count + 1),
            ExpressionGraphNode expression => Math.Max(expression.Inputs.Count, 1),
            _ => 1,
        };
        return 42 + 10 + (rows * 24);
    }
    /// <summary>The height of one setting row under a task node's pins, in canvas units.</summary>
    public const double DetailRowHeight = 20;

    /// <summary>
    /// A node's drawn height in one scope, as the canvas draws it: a reference shows only the pins the scope wires (as node
    /// editors show a variable with just the value read); a task shows its inputs, its outputs something reads (or all of them,
    /// with <paramref name="allPins"/>), then its settings.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="key">The node.</param>
    /// <param name="scope">The scope it is drawn in.</param>
    /// <param name="allPins">Whether outputs nothing reads are drawn too.</param>
    /// <returns>The height.</returns>
    public static double HeightOf(MissionGraph graph, GraphNodeKey key, GraphScope scope, bool allPins = false)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Chains.TryGetValue(key, out List<GraphPinRef>? operands))
        {
            return 42 + 10 + (operands.Count * 24);
        }

        (int usedInputs, int usedOutputs) = UsedPins(graph, key, scope);
        if (scope.References.Contains(key))
        {
            return 42 + 10 + (Math.Max(1, Math.Max(usedInputs, usedOutputs)) * 24);
        }

        if (graph.Nodes[key] is not TaskGraphNode task)
        {
            return HeightOf(graph, key);
        }

        int inputs = task.Definition.Inputs.Count(static p => p.IsLinkable);
        int outputs = allPins ? task.Definition.Outputs.Count + 1 : usedOutputs;
        int details = TaskNodeDetails.For(task).Count;
        return 42 + 10 + (Math.Max(1, Math.Max(inputs, outputs)) * 24) + (details > 0 ? 6 + (details * DetailRowHeight) : 0);
    }


    /// <summary>How many of a reference's input and output pins a scope wires: the only ones it draws.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="key">The reference.</param>
    /// <param name="scope">The scope.</param>
    /// <returns>The wired input and output pin counts.</returns>
    public static (int Inputs, int Outputs) UsedPins(MissionGraph graph, GraphNodeKey key, GraphScope scope)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(scope);
        int inputs = graph.Links.Where(l => l.To.Node == key && scope.Contains(scope.DrawnAs(l.From.Node))).Select(static l => l.To.Pin).Distinct(StringComparer.Ordinal).Count();
        int outputs = graph.Links.Where(l => l.From.Node == key && scope.Contains(scope.DrawnAs(l.To.Node))).Select(static l => l.From.Pin).Distinct(StringComparer.Ordinal).Count();
        return (inputs, outputs);
    }

    // The members a node feeds.
    private static GraphNodeKey[] Readers(List<GraphLink> links, GraphNodeKey key, Dictionary<GraphNodeKey, (int Column, int Row)> cells) =>
        [.. links.Where(l => l.From.Node == key && cells.ContainsKey(l.To.Node)).Select(static l => l.To.Node).Distinct()];

    // Columns by data flow among the members (longest path, bounded for cycles), from the first column given; rows by order.
    private static Dictionary<GraphNodeKey, (int Column, int Row)> Columns(List<GraphLink> links, List<GraphNodeKey> members, int first)
    {
        Dictionary<GraphNodeKey, int> column = members.ToDictionary(static k => k, static _ => 0);
        ILookup<GraphNodeKey, GraphNodeKey> inputs = links
            .Where(l => column.ContainsKey(l.From.Node) && column.ContainsKey(l.To.Node) && l.From.Node != l.To.Node)
            .ToLookup(static l => l.To.Node, static l => l.From.Node);
        int limit = Math.Max(1, members.Count);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (GraphNodeKey key in members)
            {
                int wanted = inputs[key].Select(k => column[k] + 1).DefaultIfEmpty(0).Max();
                if (wanted > column[key] && wanted < limit)
                {
                    column[key] = wanted;
                    changed = true;
                }
            }
        }

        Dictionary<int, int> next = [];
        Dictionary<GraphNodeKey, (int, int)> cells = [];
        foreach (GraphNodeKey key in members)
        {
            int c = column[key] + first;
            int row = next.GetValueOrDefault(c);
            next[c] = row + 1;
            cells[key] = (c, row);
        }

        return cells;
    }
}
