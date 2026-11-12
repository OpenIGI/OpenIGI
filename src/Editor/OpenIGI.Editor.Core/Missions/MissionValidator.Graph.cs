using System.Globalization;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Missions;

/// <content>The checks that read the mission as a graph: references and level expressions.</content>
public static partial class MissionValidator
{
    private const string LogicCheck = "Logic";

    // References and expressions, read through the same graph the graph editor draws, so the Problems panel and the
    // graph's badges never disagree. Without a vocabulary only what the mission's own structure shows is checked:
    // dangling ids and unreadable text.
    private static void CheckGraph(MissionDocument document, IMissionFacts? facts, List<MissionIssue> issues)
    {
        ScriptVocabulary vocabulary = facts?.Vocabulary ?? new ScriptVocabulary();
        bool knowsNames = vocabulary.Natives.Count > 0;
        TaskCatalogue catalogue = TaskCatalogue.Build(document.Declarations.Values, vocabulary);
        MissionGraph graph = MissionGraph.Build(document, catalogue, vocabulary, facts?.Dialect ?? LevelExpressionDialect.Igi1);

        foreach (ExpressionGraphNode node in graph.Nodes.Values.OfType<ExpressionGraphNode>())
        {
            MissionTask owner = ((TaskGraphNode)graph.Nodes[node.Owner.Task]).Task;
            string where = $"Its {node.Owner.Parameter}";
            switch (node.Kind)
            {
                case ExpressionNodeKind.UnresolvedField:
                    issues.Add(Warn(owner, ReferenceCheck, $"{where} reads {node.Text}, which the mission does not have: that read is always 0."));
                    break;

                case ExpressionNodeKind.Raw:
                    issues.Add(Warn(owner, LogicCheck, $"{where} is not an expression the game can read, so the game treats it as empty."));
                    break;

                case ExpressionNodeKind.UnknownName when knowsNames:
                    issues.Add(Warn(owner, LogicCheck, $"{where} names {node.Text}, which the game does not know: it reads as 0."));
                    break;

                case ExpressionNodeKind.Call when knowsNames && node.Native is null:
                    issues.Add(Warn(owner, LogicCheck, $"{where} calls {node.Text}, which the game does not have."));
                    break;
            }
        }

        foreach (TaskGraphNode node in graph.TaskNodes)
        {
            CheckWires(graph, node, vocabulary, issues);
            CheckTaskReferences(document, node, issues);
        }

        if (facts is { Target: MissionTarget.Retail })
        {
            CheckRuntime(graph, vocabulary, facts, issues);
        }
    }

    // In a mission made for the original game, whatever only OpenIGI has: a mod's task types, natives and constants.
    private static void CheckRuntime(MissionGraph graph, ScriptVocabulary vocabulary, IMissionFacts facts, List<MissionIssue> issues)
    {
        MissionIssue OpenIgiOnly(MissionTask task, string message) =>
            new(IssueSeverity.Error, task, message) { Check = TargetCheck, Fixes = [new IssueFix("Make It OpenIGI Only", facts.MakeOpenIgiOnly)] };

        foreach (TaskGraphNode node in graph.TaskNodes.Where(n => vocabulary.RuntimeOf(n.Task.TypeName) == ScriptRuntime.OpenIgi))
        {
            issues.Add(OpenIgiOnly(node.Task, $"It is a {node.Task.TypeName}, which only OpenIGI has, but the mission is made for the original game."));
        }

        HashSet<(GraphNodeKey, string)> named = [];
        foreach (ExpressionGraphNode node in graph.Nodes.Values.OfType<ExpressionGraphNode>()
            .Where(n => n.Kind is ExpressionNodeKind.Call or ExpressionNodeKind.Constant && vocabulary.RuntimeOf(NameOf(n)) == ScriptRuntime.OpenIgi))
        {
            if (named.Add((node.Owner.Task, NameOf(node))))
            {
                MissionTask owner = ((TaskGraphNode)graph.Nodes[node.Owner.Task]).Task;
                issues.Add(OpenIgiOnly(owner, $"Its {node.Owner.Parameter} uses {NameOf(node)}, which only OpenIGI has, but the mission is made for the original game."));
            }
        }

        // A call's text is its name; a native's signature names it exactly when the graph resolved one.
        static string NameOf(ExpressionGraphNode node) => node.Native?.Name ?? node.Text;
    }

    // Calls that misfit their natives, and reads of fields the task's type does not export.
    private static void CheckWires(MissionGraph graph, TaskGraphNode node, ScriptVocabulary vocabulary, List<MissionIssue> issues)
    {
        foreach (GraphLink link in graph.Links.Where(l => l.To.Node == node.Key))
        {
            if (graph.Nodes[link.From.Node] is TaskGraphNode source && link.From.Pin != MissionGraph.Self
                && vocabulary.TaskFields.ContainsKey(source.Task.TypeName)
                && source.Definition.Outputs.All(o => o.Name != link.From.Pin))
            {
                issues.Add(Warn(node.Task, ReferenceCheck, $"Its {link.To.Pin} reads {link.From.Pin} of {source.Task}, which a {source.Task.TypeName} does not export: that read is always 0."));
            }
        }

        foreach (ExpressionGraphNode call in graph.Nodes.Values.OfType<ExpressionGraphNode>()
            .Where(n => n.Owner.Task == node.Key && n.Kind == ExpressionNodeKind.Call && n.Native is { } native))
        {
            int arguments = graph.Links.Count(l => l.To.Node == call.Key);
            if (!call.Native!.Accepts(arguments))
            {
                issues.Add(Warn(node.Task, LogicCheck, string.Create(
                    CultureInfo.InvariantCulture,
                    $"Its {call.Owner.Parameter} calls {call.Text} with {arguments} argument(s), which the game does not run as written.")));
            }
        }
    }

    // Reference parameters naming no task, or a task of the wrong type.
    private static void CheckTaskReferences(MissionDocument document, TaskGraphNode node, List<MissionIssue> issues)
    {
        Dictionary<int, MissionTask> byId = [];
        foreach (TaskPin pin in node.Definition.Inputs.Where(static p => p.Type.Kind == ScriptTypeKind.TaskRef))
        {
            if (node.Task.FindParameter(pin.Name) is not { } parameter || !parameter.TryGetNumbers(out double[] values))
            {
                continue;
            }

            if (byId.Count == 0)
            {
                foreach (MissionTask task in document.EnumerateTasks().Where(static t => t.Id >= 0))
                {
                    byId.TryAdd(task.Id, task);
                }
            }

            foreach (int id in values.Select(static v => (int)v).Where(static v => v > 0))
            {
                MissionTask owner = node.Task;
                string name = pin.Name;
                if (!byId.TryGetValue(id, out MissionTask? target))
                {
                    issues.Add(Warn(owner, ReferenceCheck, string.Create(CultureInfo.InvariantCulture, $"Its {name} is task {id}, which the mission does not have: it drives nothing."))
                        with { Fixes = [new IssueFix("Clear the reference", () => SetParameterCommand.Numbers(document, owner, name, [-1]))] });
                }
                else if (pin.Type.Family is { } wanted && target.TypeName != wanted)
                {
                    issues.Add(Warn(owner, ReferenceCheck, $"Its {name} is {target}, but it wants a {wanted}: the game finds nothing there."));
                }
            }
        }

    }

    private static MissionIssue Warn(MissionTask task, string check, string message) =>
        new(IssueSeverity.Warning, task, message) { Check = check };
}
