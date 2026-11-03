using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>One parameter whose text the graph now says differently.</summary>
/// <param name="Task">The task.</param>
/// <param name="Parameter">The parameter.</param>
/// <param name="Text">The text the graph writes for it.</param>
public sealed record ExpressionChange(MissionTask Task, string Parameter, string Text);

/// <summary>Compiles a mission graph back into its document: the parameter edits that make the text say what the graph says.</summary>
/// <remarks>
/// Tasks, values and references are the document's own and need no compiling; what the graph holds of its own is how each
/// expression is wired. A parameter whose text already means the same to the game as the graph's (<see cref="ExpressionCanonical"/>)
/// is left as level data wrote it, so a graph nobody edited compiles to no edits and its mission saves byte for byte.
/// </remarks>
public static class MissionGraphCompiler
{
    /// <summary>The parameters the graph says differently from the document.</summary>
    /// <param name="graph">The graph, as edited.</param>
    /// <param name="dialect">Which game's rules apply.</param>
    /// <returns>Each change.</returns>
    public static IReadOnlyList<ExpressionChange> Changes(MissionGraph graph, LevelExpressionDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(graph);
        List<ExpressionChange> changes = [];
        foreach (TaskGraphNode node in graph.TaskNodes)
        {
            foreach (TaskPin pin in node.Definition.Inputs.Where(static p => p.Role is { } role && TaskPinRoles.IsExpression(role)))
            {
                if (node.Task.FindParameter(pin.Name) is not { Arguments: [QscStringLiteral { Value: { } current }] })
                {
                    continue;
                }

                string written = ExpressionGraphWriter.Write(graph, node.Key, pin.Name);
                if (!ExpressionCanonical.Same(current, written, $"{node.Task.TypeName}_{node.Task.Id}", dialect))
                {
                    changes.Add(new ExpressionChange(node.Task, pin.Name, written));
                }
            }
        }

        return changes;
    }

    /// <summary>The changes as one undoable step on the document.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="changes">The changes.</param>
    /// <param name="name">The step's name in the history.</param>
    /// <returns>The step, or <see langword="null"/> when there is nothing to change.</returns>
    public static IEditorCommand? ToCommand(MissionDocument document, IReadOnlyList<ExpressionChange> changes, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Count == 0
            ? null
            : new CompositeCommand(name, [.. changes.Select(c => new SetParameterCommand(document, c.Task, c.Parameter, [new QscStringLiteral(c.Text)]))]);
    }
}
