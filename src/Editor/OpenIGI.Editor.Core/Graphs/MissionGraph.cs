using System.Globalization;
using System.Runtime.CompilerServices;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>A mission as a graph: a view of its <see cref="MissionDocument"/>, which stays the one source of truth.</summary>
/// <remarks>
/// <para>
/// Nothing here is stored but layout. Task nodes are the document's tasks; expression nodes are read from the text of their
/// parameters; wires are the field reads and task references that text and those parameters hold. An edit made through a
/// node is a command on the document, after which the graph is built again, so a mission edited without the graph and one
/// edited with it are the same mission, and an unedited mission saves byte for byte.
/// </para>
/// <para>
/// Every task gets a node; <see cref="TaskGraphNode.IsShown"/> marks the ones the graph shows by default (logic, frames and
/// whatever is wired), so a level's hundreds of props stay out of the way until something references them.
/// </para>
/// </remarks>
public sealed class MissionGraph
{
    /// <summary>The output pin every task node has: a reference to the task itself, for pins that take a task.</summary>
    public const string Self = "self";

    private readonly Dictionary<GraphNodeKey, GraphNode> _nodes = [];
    private readonly ConditionalWeakTable<MissionTask, TaskGraphNode> _byTask = [];
    private readonly List<GraphLink> _links = [];
    private readonly Dictionary<(GraphNodeKey Task, string Parameter), GraphPinRef> _roots = [];

    private MissionGraph()
    {
    }

    /// <summary>Gets every node, by key.</summary>
    public IReadOnlyDictionary<GraphNodeKey, GraphNode> Nodes => _nodes;

    /// <summary>Gets every wire.</summary>
    public IReadOnlyList<GraphLink> Links => _links;

    /// <summary>Gets the pin that feeds each expression parameter: the root of what its text says.</summary>
    public IReadOnlyDictionary<(GraphNodeKey Task, string Parameter), GraphPinRef> ExpressionRoots => _roots;

    /// <summary>Gets the task nodes, in document order.</summary>
    public IEnumerable<TaskGraphNode> TaskNodes => _nodes.Values.OfType<TaskGraphNode>();

    /// <summary>Builds a mission's graph.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="catalogue">Its task types as nodes.</param>
    /// <param name="vocabulary">The title's vocabulary.</param>
    /// <param name="dialect">Which game's expression rules apply.</param>
    /// <returns>The graph.</returns>
    public static MissionGraph Build(MissionDocument document, TaskCatalogue catalogue, ScriptVocabulary vocabulary, LevelExpressionDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(vocabulary);
        MissionGraph graph = new();
        Dictionary<int, TaskGraphNode> byId = [];
        HashSet<int> repeated = [.. document.EnumerateTasks().Where(static t => t.Id >= 0).GroupBy(static t => t.Id)
            .Where(static g => g.Count() > 1).Select(static g => g.Key)];

        int index = 0;
        foreach (MissionTask root in document.RootTasks)
        {
            graph.AddTasks(root, index.ToString(CultureInfo.InvariantCulture), frame: null, catalogue, repeated, byId);
            index++;
        }

        ExpressionGraphReader reader = new(vocabulary, dialect, id => byId.GetValueOrDefault(id));
        foreach (TaskGraphNode node in graph.TaskNodes.ToList())
        {
            graph.Wire(node, reader, byId);
        }

        graph.MarkShown();
        return graph;
    }

    /// <summary>The node of a task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="node">Receives its node.</param>
    /// <returns><see langword="true"/> when the task is in this graph.</returns>
    public bool TryGetNode(MissionTask task, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TaskGraphNode? node) =>
        _byTask.TryGetValue(task, out node);

    private void AddTasks(
        MissionTask task,
        string path,
        GraphNodeKey? frame,
        TaskCatalogue catalogue,
        HashSet<int> repeated,
        Dictionary<int, TaskGraphNode> byId)
    {
        GraphNodeKey key = task.Id >= 0 && !repeated.Contains(task.Id)
            ? new GraphNodeKey(string.Create(CultureInfo.InvariantCulture, $"t{task.Id}"))
            : new GraphNodeKey("p" + path);
        TaskNodeDefinition definition = catalogue.TryGet(task.TypeName, out TaskNodeDefinition? known)
            ? known
            : new TaskNodeDefinition(task.TypeName, [], []) { IsContainer = task.Children.Count > 0 };
        TaskGraphNode node = new(key, task, definition) { Frame = frame };
        _nodes[key] = node;
        _byTask.AddOrUpdate(task, node);
        if (task.Id >= 0)
        {
            byId.TryAdd(task.Id, node);
        }

        for (int i = 0; i < task.Children.Count; i++)
        {
            AddTasks(task.Children[i], string.Create(CultureInfo.InvariantCulture, $"{path}.{i}"), key, catalogue, repeated, byId);
        }
    }

    private void Wire(TaskGraphNode node, ExpressionGraphReader reader, Dictionary<int, TaskGraphNode> byId)
    {
        foreach (TaskPin pin in node.Definition.Inputs.Where(static p => p.IsLinkable))
        {
            if (node.Task.FindParameter(pin.Name) is not { } parameter)
            {
                continue;
            }

            if (pin.Role is { } role && TaskPinRoles.IsExpression(role)
                && parameter.Arguments is [QscStringLiteral { Value: { } text }])
            {
                ExpressionReadResult read = reader.Read(text, node, pin.Name);
                foreach (ExpressionGraphNode expression in read.Nodes)
                {
                    expression.Frame = node.Frame;
                    _nodes[expression.Key] = expression;
                }

                _links.AddRange(read.Links);
                if (read.Root is { } root)
                {
                    _roots[(node.Key, pin.Name)] = root;
                    _links.Add(new GraphLink(root, new GraphPinRef(node.Key, pin.Name)));
                }
            }
            else if (pin.Type.Kind is ScriptTypeKind.TaskRef && parameter.TryGetNumbers(out double[] ids))
            {
                foreach (int id in ids.Select(static v => (int)v).Where(static v => v > 0))
                {
                    if (byId.TryGetValue(id, out TaskGraphNode? target))
                    {
                        _links.Add(new GraphLink(new GraphPinRef(target.Key, Self), new GraphPinRef(node.Key, pin.Name)));
                    }
                }
            }
        }
    }

    // What this mission wires is shown, with the frames that hold it; a type that could take part (IsLogic) but here does not,
    // such as an I.G.I. 2 prop that exports a field nothing reads, stays out of the way with the rest.
    private void MarkShown()
    {
        HashSet<GraphNodeKey> wired = [.. _links.SelectMany(static l => new[] { l.From.Node, l.To.Node })];
        foreach (TaskGraphNode node in TaskNodes)
        {
            node.IsShown = wired.Contains(node.Key);
        }

        foreach (TaskGraphNode node in TaskNodes.Where(static n => n.IsShown).ToList())
        {
            for (GraphNodeKey? frame = node.Frame; frame is { } key && _nodes[key] is TaskGraphNode container; frame = container.Frame)
            {
                container.IsShown = true;
            }
        }
    }
}
