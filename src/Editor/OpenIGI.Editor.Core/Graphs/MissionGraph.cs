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
    private Dictionary<GraphPinRef, GraphPinRef>? _sources;

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
            graph.AddTasks(root, index.ToString(CultureInfo.InvariantCulture), frame: null, catalogue, repeated, byId, generated: false);
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

    /// <summary>What feeds an input pin: the output wired into it.</summary>
    /// <param name="input">The input pin.</param>
    /// <returns>The output, or <see langword="null"/> when nothing is wired in.</returns>
    /// <remarks>An expression operand takes one wire; a reference pin naming several tasks answers its first.</remarks>
    public GraphPinRef? SourceOf(GraphPinRef input)
    {
        _sources ??= _links.GroupBy(static l => l.To).ToDictionary(static g => g.Key, static g => g.First().From);
        return _sources.TryGetValue(input, out GraphPinRef from) ? from : null;
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
        Dictionary<int, TaskGraphNode> byId,
        bool generated)
    {
        GraphNodeKey key = task.Id >= 0 && !repeated.Contains(task.Id)
            ? new GraphNodeKey(string.Create(CultureInfo.InvariantCulture, $"t{task.Id}"))
            : new GraphNodeKey("p" + path);
        TaskNodeDefinition definition = catalogue.TryGet(task.TypeName, out TaskNodeDefinition? known)
            ? known
            : new TaskNodeDefinition(task.TypeName, [], []) { IsContainer = task.Children.Count > 0 };
        generated |= task.TypeName == TaskCatalogue.ContainerType && task.Label == Exec.ExecLayerCompiler.ContainerLabel;
        TaskGraphNode node = new(key, task, definition) { Frame = frame, IsGenerated = generated, LatchCondition = LatchOf(task) };
        _nodes[key] = node;
        _byTask.AddOrUpdate(task, node);
        if (task.Id >= 0)
        {
            byId.TryAdd(task.Id, node);
        }

        for (int i = 0; i < task.Children.Count; i++)
        {
            AddTasks(task.Children[i], string.Create(CultureInfo.InvariantCulture, $"{path}.{i}"), key, catalogue, repeated, byId, generated);
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

    // EditVariable_N.nValue == 0 && (X), no Sub: the condition X, as written after the "&&".
    private static string? LatchOf(MissionTask task)
    {
        if (task.TypeName != "EditVariable"
            || task.FindParameter("Add") is not { Arguments: [QscStringLiteral { Value: { Length: > 0 } add }] }
            || task.FindParameter("Sub") is { Arguments: [QscStringLiteral { Value: { Length: > 0 } }] })
        {
            return null;
        }

        string self = string.Create(CultureInfo.InvariantCulture, $"EditVariable_{task.Id}.nValue");
        try
        {
            ScriptExpression tree = ScriptExpressionParser.Parse(LevelExpression.Compile(add, self[..^7], LevelExpressionDialect.Igi1));
            bool latch = tree is { Kind: ScriptExpressionKind.Operator, Operator: ScriptOperator.LogicalAnd, Left: { } test }
                && test is { Kind: ScriptExpressionKind.Operator, Operator: ScriptOperator.Equal, Left.Text: { } name, Right: { Kind: ScriptExpressionKind.Number, Number: 0 } }
                && name == self;
            int and = add.IndexOf("&&", StringComparison.Ordinal);
            return latch && and >= 0 ? add[(and + 2)..].Trim() : null;
        }
        catch (ScriptExpressionException)
        {
            return null;
        }
    }

    // What this mission wires is shown, with the frames that hold it; a type that could take part (IsLogic) but here does not,
    // such as an I.G.I. 2 prop that exports a field nothing reads, stays out of the way with the rest.
    private void MarkShown()
    {
        HashSet<GraphNodeKey> wired = [.. _links.SelectMany(static l => new[] { l.From.Node, l.To.Node })];
        foreach (TaskGraphNode node in TaskNodes)
        {
            node.IsShown = wired.Contains(node.Key) && !node.IsGenerated;
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
