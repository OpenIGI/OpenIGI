using OpenIGI.Scripting.Expressions;
using OpenIGI.Editor.Core.Missions;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>
/// One small graph of a mission's logic: the tasks one container holds that read something, the expressions they read, and,
/// as reference nodes, whatever else those wire to.
/// </summary>
/// <param name="Key">The scope's key: its container's node key.</param>
/// <param name="Title">The name listed for it.</param>
/// <param name="Parent">The scope listed above it, or <see langword="null"/> for a top-level one.</param>
/// <param name="Container">The container it is the logic of, or <see langword="null"/> for logic no container holds.</param>
public sealed record GraphScope(string Key, string Title, string? Parent, GraphNodeKey? Container)
{
    /// <summary>Gets the nodes the scope owns: drawn and edited here.</summary>
    public HashSet<GraphNodeKey> Members { get; } = [];

    /// <summary>Gets the nodes drawn here only because a member wires to them; their own logic lives in another scope or none.</summary>
    public HashSet<GraphNodeKey> References { get; } = [];

    /// <summary>
    /// Gets the values written straight onto pins: a literal or constant feeding one pin is not drawn as a node of its own but
    /// as the pin's inline value, by the input it feeds. Its node stays in the graph, so the text it compiles to is the same.
    /// </summary>
    public Dictionary<GraphPinRef, (GraphNodeKey Node, string Text)> Inline { get; } = [];

    /// <summary>
    /// Gets the folded operator chains: a run of one associative operator (<c>a || b || c</c>) drawn as one node with an
    /// input for each operand, as node editors give OR and AND as many pins as wanted. Keyed by the chain's outermost node,
    /// its operand pins in reading order. The graph itself keeps its two-input nodes, so the text it compiles to is unchanged.
    /// </summary>
    public Dictionary<GraphNodeKey, List<GraphPinRef>> Chains { get; } = [];

    /// <summary>Gets the inner nodes of the folded chains, each with the chain's outermost node it is drawn as.</summary>
    public Dictionary<GraphNodeKey, GraphNodeKey> Folded { get; } = [];

    /// <summary>The node a graph node is drawn as here: itself, or the chain it is folded into.</summary>
    /// <param name="key">The node.</param>
    /// <returns>The drawn node.</returns>
    public GraphNodeKey DrawnAs(GraphNodeKey key) => Folded.TryGetValue(key, out GraphNodeKey root) ? root : key;

    /// <summary>The pin a graph input is drawn as here: a chain operand's pin on the chain's node, or the input itself.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The drawn node and pin id.</returns>
    public (GraphNodeKey Node, string Pin) DrawnPin(GraphPinRef input)
    {
        GraphNodeKey root = DrawnAs(input.Node);
        return Chains.ContainsKey(root) ? (root, Encode(input)) : (input.Node, input.Pin);
    }

    /// <summary>The graph input a drawn pin stands for: decodes a chain operand's pin, passes any other through.</summary>
    /// <param name="node">The drawn node's id.</param>
    /// <param name="pin">The drawn pin's id.</param>
    /// <returns>The input in the graph.</returns>
    public static GraphPinRef Resolve(string node, string pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        int split = pin.LastIndexOf(ChainPinSeparator);
        return split > 0 ? new GraphPinRef(new GraphNodeKey(pin[..split]), pin[(split + 1)..]) : new GraphPinRef(new GraphNodeKey(node), pin);
    }

    private const char ChainPinSeparator = '#';

    private static string Encode(GraphPinRef input) => input.Node.Value + ChainPinSeparator + input.Pin;

    /// <summary>Gets a value indicating whether the scope is gated: its container is a <c>ConditionalContainer</c>, drawn as a frame.</summary>
    public bool IsGated { get; init; }

    /// <summary>Whether the scope draws a node, as a member or a reference.</summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Contains(GraphNodeKey key) => Members.Contains(key) || References.Contains(key);

    /// <summary>
    /// The key a node's position is kept under in this scope. A member has one place, its own key; a reference is drawn in
    /// every scope that uses it, so each keeps its own place for it.
    /// </summary>
    /// <param name="key">The node.</param>
    /// <returns>The layout key.</returns>
    public GraphNodeKey LayoutKey(GraphNodeKey key) => References.Contains(key) ? new GraphNodeKey(Key + "|" + key.Value) : key;
}

/// <summary>Splits a mission graph into small scopes, the way a node editor splits a program into graphs.</summary>
/// <remarks>
/// <para>
/// A task that reads something (a wire into one of its pins) is logic, and it lives in the scope of the container holding
/// it directly, so a big gate's sub-containers are graphs of their own, listed under it. Each gated container is a scope of its
/// own, holding its condition. A task that only gives values (a world object's <c>Self</c>, an alarm's <c>isTrigger</c>)
/// belongs to no scope: it is drawn, as a reference, in each scope that reads it.
/// </para>
/// <para>Scopes are listed in mission order, nested as their containers are.</para>
/// </remarks>
public static class GraphScopes
{
    /// <summary>The key of the scope for logic no container holds.</summary>
    public const string LooseKey = "mission";

    /// <summary>About how many nodes a page of a big container holds before the next page starts.</summary>
    public const int PageSize = 40;

    // Readers past which a node counts as a hub when a big container is paged.
    private const int HubFanOut = 6;

    /// <summary>Builds the scopes of a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="placed">
    /// Whether a node has a position of its own in the layout. A task placed under its own key was put in a graph by hand (added
    /// from the palette, say) and stays in its container's graph though nothing wires it yet.
    /// </param>
    /// <returns>The scopes, in mission order.</returns>
    public static IReadOnlyList<GraphScope> Build(MissionGraph graph, Func<GraphNodeKey, bool>? placed = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        HashSet<GraphNodeKey> readers = [.. graph.Links.Select(static l => l.To.Node)];
        Dictionary<string, GraphScope> scopes = new(StringComparer.Ordinal);
        List<GraphScope> order = [];

        GraphScope ScopeFor(GraphNodeKey? container)
        {
            string key = container?.Value ?? LooseKey;
            if (scopes.TryGetValue(key, out GraphScope? scope))
            {
                return scope;
            }

            TaskGraphNode? task = container is { } c ? graph.Nodes[c] as TaskGraphNode : null;
            scope = new GraphScope(key, task is null ? "Mission" : Title(task), ParentOf(graph, container, scopes, ScopeFor), container)
            {
                IsGated = task?.Definition is { IsContainer: true, Inputs.Count: > 0 },
            };
            scopes[key] = scope;
            order.Add(scope);
            return scope;
        }

        foreach (TaskGraphNode node in graph.TaskNodes.Where(n => !n.IsGenerated && (n.IsShown || placed?.Invoke(n.Key) == true)))
        {
            bool gated = node.Definition is { IsContainer: true, Inputs.Count: > 0 };
            if (gated)
            {
                ScopeFor(node.Key).Members.Add(node.Key);
            }
            else if (readers.Contains(node.Key) || node.LatchCondition is not null || (placed?.Invoke(node.Key) == true && !node.Definition.IsContainer))
            {
                ScopeFor(HomeOf(node)).Members.Add(node.Key);
            }
        }

        // Expressions go with the task they are written in.
        foreach (ExpressionGraphNode expression in graph.Nodes.Values.OfType<ExpressionGraphNode>())
        {
            if (order.Find(s => s.Members.Contains(expression.Owner.Task)) is { } scope)
            {
                scope.Members.Add(expression.Key);
            }
        }

        // A literal or constant feeding just one pin is that pin's inline value, not a node.
        Dictionary<GraphNodeKey, GraphPinRef[]> feeds = graph.Links.GroupBy(static l => l.From.Node).ToDictionary(static g => g.Key, static g => g.Select(static l => l.To).ToArray());
        foreach (GraphScope scope in order)
        {
            foreach (ExpressionGraphNode literal in scope.Members.Select(k => graph.Nodes[k]).OfType<ExpressionGraphNode>()
                .Where(static n => n.Kind is ExpressionNodeKind.Number or ExpressionNodeKind.Text or ExpressionNodeKind.Constant).ToList())
            {
                if (feeds.TryGetValue(literal.Key, out GraphPinRef[]? to) && to.Length == 1 && scope.Members.Contains(to[0].Node))
                {
                    scope.Members.Remove(literal.Key);
                    scope.Inline[to[0]] = (literal.Key, literal.Text);
                }
            }
        }

        // A run of one associative operator (every guard's detection ORed together) is one node with an input an operand.
        foreach (GraphScope scope in order)
        {
            FoldChains(graph, scope, feeds);
        }

        // A flat container of many separate pieces (every door, every subtitle) is paged: its wired clusters, in mission
        // order, packed into pages of about PageSize nodes. A cluster is never cut, so related logic stays on one page.
        foreach (GraphScope scope in order.ToList())
        {
            if (scope.Members.Count > PageSize)
            {
                Page(graph, scope, order);
            }
        }

        // One step out: what a member wires to, and what wires into a member, is drawn here as a reference.
        foreach (GraphScope scope in order)
        {
            foreach (GraphLink link in graph.Links)
            {
                bool from = scope.Members.Contains(scope.DrawnAs(link.From.Node));
                bool to = scope.Members.Contains(scope.DrawnAs(link.To.Node));
                if (from != to)
                {
                    GraphNodeKey other = from ? link.To.Node : link.From.Node;
                    if (graph.Nodes[other] is TaskGraphNode)
                    {
                        scope.References.Add(other);
                    }
                }
            }
        }

        order.RemoveAll(static s => s.Members.Count == 0);
        return order;
    }

    /// <summary>The scope a node is a member of, if any.</summary>
    /// <param name="scopes">The scopes.</param>
    /// <param name="key">The node.</param>
    /// <returns>The scope, or <see langword="null"/> when the node is only ever a reference.</returns>
    public static GraphScope? HomeScope(IReadOnlyList<GraphScope> scopes, GraphNodeKey key)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        foreach (GraphScope scope in scopes)
        {
            if (scope.Members.Contains(key))
            {
                return scope;
            }
        }

        return null;
    }

    /// <summary>The name a task is listed and titled by: its label, or its type and id.</summary>
    /// <param name="node">The task's node.</param>
    /// <returns>The name.</returns>
    public static string Title(TaskGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        MissionTask task = node.Task;
        return !string.IsNullOrWhiteSpace(task.Label) ? task.Label : task.Id >= 0 ? $"{task.TypeName} #{task.Id}" : task.TypeName;
    }

    // Folds runs of one associative operator: a node whose only reader is the same operator is drawn inside that reader.
    private static void FoldChains(MissionGraph graph, GraphScope scope, Dictionary<GraphNodeKey, GraphPinRef[]> feeds)
    {
        static bool Associative(GraphNode? node, out ScriptOperator op)
        {
            op = node is ExpressionGraphNode { Kind: ExpressionNodeKind.Operator, Inputs.Count: 2 } e ? e.Operator : ScriptOperator.None;
            return op is ScriptOperator.LogicalOr or ScriptOperator.LogicalAnd or ScriptOperator.Add or ScriptOperator.Multiply;
        }

        // Each folded node, by the input of the same operator it feeds.
        Dictionary<GraphPinRef, GraphNodeKey> inner = [];
        foreach (GraphNodeKey key in scope.Members.ToList())
        {
            if (Associative(graph.Nodes[key], out ScriptOperator op)
                && feeds.TryGetValue(key, out GraphPinRef[]? to) && to.Length == 1
                && scope.Members.Contains(to[0].Node)
                && Associative(graph.Nodes[to[0].Node], out ScriptOperator outer) && outer == op)
            {
                inner[to[0]] = key;
            }
        }

        if (inner.Count == 0)
        {
            return;
        }

        HashSet<GraphNodeKey> folded = [.. inner.Values];
        foreach (GraphNodeKey root in scope.Members.Where(k => !folded.Contains(k) && inner.Keys.Any(p => p.Node == k)).ToList())
        {
            List<GraphPinRef> operands = [];
            void Walk(GraphNodeKey node)
            {
                for (int i = 0; i < 2; i++)
                {
                    GraphPinRef pin = new(node, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (inner.TryGetValue(pin, out GraphNodeKey child))
                    {
                        scope.Folded[child] = root;
                        Walk(child);
                    }
                    else
                    {
                        operands.Add(pin);
                    }
                }
            }

            Walk(root);
            scope.Chains[root] = operands;
        }

        scope.Members.ExceptWith(scope.Folded.Keys);
    }

    // Splits a big scope into pages: the first keeps the scope's key (and its gate, if any); the rest follow it in the list.
    private static void Page(MissionGraph graph, GraphScope scope, List<GraphScope> order)
    {
        Dictionary<GraphNodeKey, GraphNodeKey> parent = scope.Members.ToDictionary(static k => k, static k => k);
        GraphNodeKey Find(GraphNodeKey key)
        {
            while (parent[key] != key)
            {
                parent[key] = parent[parent[key]];
                key = parent[key];
            }

            return key;
        }

        // A hub (the lift every call button reads, a variable every message checks) would tie everything into one cluster;
        // its wires are left out, so it stays on one page and is drawn as a reference on the others.
        // Through folded chains: an operand wired into a chain's inner node belongs with the chain.
        List<GraphLink> inside = [.. graph.Links
            .Select(l => new GraphLink(l.From with { Node = scope.DrawnAs(l.From.Node) }, l.To with { Node = scope.DrawnAs(l.To.Node) }))
            .Where(l => l.From.Node != l.To.Node && parent.ContainsKey(l.From.Node) && parent.ContainsKey(l.To.Node))];
        HashSet<GraphNodeKey> hubs = [.. inside.GroupBy(static l => l.From.Node).Where(static g => g.Select(static l => l.To.Node).Distinct().Count() > HubFanOut).Select(static g => g.Key)];
        foreach (GraphLink link in inside.Where(l => !hubs.Contains(l.From.Node)))
        {
            GraphNodeKey a = Find(link.From.Node);
            GraphNodeKey b = Find(link.To.Node);
            if (a != b)
            {
                parent[b] = a;
            }
        }

        // Clusters in mission order (the order nodes were added), the gate's own cluster first.
        List<List<GraphNodeKey>> clusters = [];
        Dictionary<GraphNodeKey, List<GraphNodeKey>> byRoot = [];
        foreach (GraphNodeKey key in graph.Nodes.Keys.Where(scope.Members.Contains))
        {
            GraphNodeKey root = Find(key);
            if (!byRoot.TryGetValue(root, out List<GraphNodeKey>? cluster))
            {
                byRoot[root] = cluster = [];
                clusters.Add(cluster);
            }

            cluster.Add(key);
        }

        if (scope.Container is { } gate && clusters.FindIndex(c => c.Contains(gate)) is > 0 and int at)
        {
            List<GraphNodeKey> own = clusters[at];
            clusters.RemoveAt(at);
            clusters.Insert(0, own);
        }

        List<List<GraphNodeKey>> pages = [[]];
        foreach (List<GraphNodeKey> cluster in clusters)
        {
            if (pages[^1].Count > 0 && pages[^1].Count + cluster.Count > PageSize)
            {
                pages.Add([]);
            }

            pages[^1].AddRange(cluster);
        }

        if (pages.Count == 1)
        {
            return;
        }

        string title = scope.Title;
        scope.Members.Clear();
        scope.Members.UnionWith(pages[0]);
        int index = order.IndexOf(scope);
        order[index] = scope with { Title = $"{title} 1" }; // the same member set: a record copy shares it
        for (int i = 1; i < pages.Count; i++)
        {
            GraphScope next = new($"{scope.Key}#{i + 1}", $"{title} {i + 1}", scope.Parent, scope.Container) { IsGated = false };
            next.Members.UnionWith(pages[i]);
            foreach ((GraphNodeKey root, List<GraphPinRef> operands) in scope.Chains.Where(c => next.Members.Contains(c.Key)))
            {
                next.Chains[root] = operands;
                foreach ((GraphNodeKey child, GraphNodeKey _) in scope.Folded.Where(p => p.Value == root))
                {
                    next.Folded[child] = root;
                }
            }

            foreach ((GraphPinRef pin, (GraphNodeKey, string) value) in scope.Inline.Where(p => next.Members.Contains(p.Key.Node)))
            {
                next.Inline[pin] = value;
            }
            order.Insert(index + i, next);
        }
    }

    // A logic task's container: the one directly holding it, so each sub-container of a big gate is a graph of its own.
    private static GraphNodeKey? HomeOf(TaskGraphNode node) => node.Frame;

    // The scope listed above a container's: the nearest enclosing container that is a scope, made on demand.
    private static string? ParentOf(MissionGraph graph, GraphNodeKey? container, Dictionary<string, GraphScope> scopes, Func<GraphNodeKey?, GraphScope> scopeFor)
    {
        if (container is not { } key)
        {
            return null;
        }

        for (GraphNodeKey? at = graph.Nodes[key].Frame; at is { } up; at = graph.Nodes[up].Frame)
        {
            if (scopes.ContainsKey(up.Value) || graph.Nodes[up] is TaskGraphNode { Definition: { IsContainer: true, Inputs.Count: > 0 } })
            {
                return scopeFor(up).Key;
            }
        }

        return null;
    }
}
