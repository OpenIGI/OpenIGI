using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>
/// Every task's parameter values as the mission was last loaded or saved: what an inspector marks a changed value against,
/// and what Revert puts back.
/// </summary>
/// <remarks>
/// Tasks have no defaults of their own (a mission writes every value out), so "modified" means changed since the file, as
/// a text editor marks unsaved lines. A task made since then has no baseline and nothing on it counts as modified.
/// </remarks>
public sealed class MissionBaseline
{
    private readonly Dictionary<MissionTask, Dictionary<string, (QscExpression[] Arguments, string Printed)>> _values =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Records every task's values as they are now.</summary>
    /// <param name="document">The document, or <see langword="null"/> to forget everything.</param>
    public void Capture(MissionDocument? document)
    {
        _values.Clear();
        if (document is null)
        {
            return;
        }

        foreach (MissionTask task in document.EnumerateTasks())
        {
            Dictionary<string, (QscExpression[], string)> values = new(StringComparer.Ordinal);
            foreach (MissionParameter parameter in task.Parameters)
            {
                values[parameter.Name] = ([.. parameter.Arguments], Print(parameter.Arguments));
            }

            _values[task] = values;
        }
    }

    /// <summary>Gets a parameter's value as recorded.</summary>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <returns>The arguments, or <see langword="null"/> when the task or parameter was not recorded.</returns>
    public IReadOnlyList<QscExpression>? Original(MissionTask task, string parameter) =>
        _values.TryGetValue(task, out var values) && values.TryGetValue(parameter, out var value) ? value.Arguments : null;

    /// <summary>Gets whether a parameter differs from its recorded value.</summary>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <returns><see langword="true"/> when it was recorded and has changed since.</returns>
    public bool IsModified(MissionTask task, string parameter) =>
        _values.TryGetValue(task, out var values)
        && values.TryGetValue(parameter, out var value)
        && task.FindParameter(parameter) is { } now
        && Print(now.Arguments) != value.Printed;

    /// <summary>Gets the parameters of a task that differ from their recorded values.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Their names.</returns>
    public IEnumerable<string> ModifiedParameters(MissionTask task) =>
        task.Parameters.Select(static p => p.Name).Where(name => IsModified(task, name));

    private static string Print(IEnumerable<QscExpression> arguments) => string.Join(",", arguments.Select(QscPrinter.Print));
}
