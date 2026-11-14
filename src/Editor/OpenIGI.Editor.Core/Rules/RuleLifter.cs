using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Rules;

/// <summary>A rule the level itself holds: one of a task's conditions that the title has words for, and what it waits for.</summary>
/// <remarks>
/// It is not made into anything: it is the task's own parameter, read as a sentence (<c>when the terminal is hacked, show
/// the message</c>). Changing what it waits for writes that parameter, and nothing else, so a level whose rules are only
/// looked at is saved exactly as it was.
/// </remarks>
/// <param name="Task">The task.</param>
/// <param name="Parameter">The condition parameter.</param>
/// <param name="Condition">What it waits for, as level-expression text.</param>
public sealed record LevelRule(TaskGraphNode Task, string Parameter, string Condition)
{
    /// <summary>The step that changes what the rule waits for.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="condition">The new condition, as level-expression text.</param>
    /// <returns>The step.</returns>
    public Commands.IEditorCommand WithCondition(MissionDocument document, string condition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(condition);
        return new SetParameterCommand(document, Task.Task, Parameter, [new QscStringLiteral(condition)]);
    }
}

/// <summary>Reads the rules a level already holds out of its tasks.</summary>
public static class RuleLifter
{
    /// <summary>Finds every condition the title has words for that a level has written, in the level's order.</summary>
    /// <param name="graph">The mission's graph.</param>
    /// <param name="vocabulary">The title's vocabulary, whose named actions say which conditions are rules.</param>
    /// <param name="actions">The mission's action layer: a condition one of its steps drives belongs to that rule, and is left out.</param>
    /// <returns>The rules.</returns>
    public static IReadOnlyList<LevelRule> Lift(MissionGraph graph, ScriptVocabulary vocabulary, ExecLayer actions)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(actions);
        HashSet<(GraphNodeKey, string)> driven = [.. actions.Nodes
            .Where(static n => n is { Kind: ExecNodeKind.Drive, Target: not null })
            .Select(static n => (n.Target!.Value, n.TargetPin))];
        List<LevelRule> rules = [];
        foreach (TaskGraphNode node in graph.TaskNodes.Where(static n => !n.IsGenerated))
        {
            foreach (MissionParameter parameter in node.Task.Parameters)
            {
                if (vocabulary.ActionOf(node.Task.TypeName, parameter.Name) is not null
                    && parameter.Arguments is [QscStringLiteral { Value: { } text }] && !string.IsNullOrWhiteSpace(text)
                    && !driven.Contains((node.Key, parameter.Name)))
                {
                    rules.Add(new LevelRule(node, parameter.Name, text));
                }
            }
        }

        return rules;
    }
}
