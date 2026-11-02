using OpenIGI.Editor.Core.Commands;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>Sets a parameter's arguments; consecutive sets of the same parameter merge into one undo step.</summary>
public sealed class SetParameterCommand : IEditorCommand
{
    private readonly MissionDocument _document;
    private readonly MissionTask _task;
    private readonly string _parameter;
    private readonly QscExpression[] _before;
    private QscExpression[] _after;

    /// <summary>Initializes a new instance of the <see cref="SetParameterCommand"/> class.</summary>
    /// <param name="document">The document.</param>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The parameter's declared name.</param>
    /// <param name="arguments">The new arguments.</param>
    /// <exception cref="ArgumentException">The task has no such parameter.</exception>
    public SetParameterCommand(MissionDocument document, MissionTask task, string parameter, IReadOnlyList<QscExpression> arguments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(arguments);
        _document = document;
        _task = task;
        _parameter = parameter;
        _before = [.. (task.FindParameter(parameter) ?? throw new ArgumentException($"{task} has no parameter '{parameter}'.", nameof(parameter))).Arguments];
        _after = [.. arguments];
    }

    /// <summary>Creates a command that sets a parameter's numbers, keeping the spelling of every unchanged value.</summary>
    /// <param name="document">The document.</param>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The parameter's declared name.</param>
    /// <param name="values">One value per argument.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentException">The task has no such parameter, or the count differs from its width.</exception>
    public static SetParameterCommand Numbers(MissionDocument document, MissionTask task, string parameter, ReadOnlySpan<double> values)
    {
        ArgumentNullException.ThrowIfNull(task);
        QscExpression[] arguments = (task.FindParameter(parameter)
            ?? throw new ArgumentException($"{task} has no parameter '{parameter}'.", nameof(parameter))).WithNumbers(values);
        return new SetParameterCommand(document, task, parameter, arguments);
    }

    /// <inheritdoc/>
    public string Name => $"Set {_parameter}";

    /// <inheritdoc/>
    public void Apply() => _document.SetArguments(_task, _parameter, _after);

    /// <inheritdoc/>
    public void Undo() => _document.SetArguments(_task, _parameter, _before);

    /// <inheritdoc/>
    public bool TryMerge(IEditorCommand following)
    {
        if (following is not SetParameterCommand set || !ReferenceEquals(set._task, _task) || set._parameter != _parameter)
        {
            return false;
        }

        _after = set._after;
        return true;
    }
}

/// <summary>Changes a task's label and id.</summary>
/// <param name="document">The document.</param>
/// <param name="task">The task.</param>
/// <param name="label">The new label.</param>
/// <param name="id">The new id.</param>
public sealed class SetIdentityCommand(MissionDocument document, MissionTask task, string label, int id) : IEditorCommand
{
    private (string Label, QscExpression Id) _before;

    /// <inheritdoc/>
    public string Name => $"Rename {task.Label}";

    /// <inheritdoc/>
    public void Apply() => _before = document.SetIdentity(task, label, ScriptValues.Integer(id));

    /// <inheritdoc/>
    public void Undo() => document.SetIdentity(task, _before.Label, _before.Id);
}

/// <summary>Adds a task (with its subtree) to the document.</summary>
/// <param name="document">The document.</param>
/// <param name="task">The detached task.</param>
/// <param name="location">Where it goes.</param>
public sealed class InsertTaskCommand(MissionDocument document, MissionTask task, TaskLocation location) : IEditorCommand
{
    /// <inheritdoc/>
    public string Name => $"Add {task.Label}";

    /// <inheritdoc/>
    public void Apply() => document.Insert(task, location);

    /// <inheritdoc/>
    public void Undo() => document.Remove(task);
}

/// <summary>Removes a task (with its subtree) from the document.</summary>
/// <param name="document">The document.</param>
/// <param name="task">The task.</param>
public sealed class RemoveTaskCommand(MissionDocument document, MissionTask task) : IEditorCommand
{
    private TaskLocation _location;

    /// <inheritdoc/>
    public string Name => $"Delete {task.Label}";

    /// <inheritdoc/>
    public void Apply() => _location = document.Remove(task);

    /// <inheritdoc/>
    public void Undo() => document.Insert(task, _location);
}

/// <summary>Moves a task to a new parent or position.</summary>
/// <param name="document">The document.</param>
/// <param name="task">The task.</param>
/// <param name="location">Where it goes, measured after it has been taken out of where it was.</param>
public sealed class MoveTaskCommand(MissionDocument document, MissionTask task, TaskLocation location) : IEditorCommand
{
    private TaskLocation _from;

    /// <inheritdoc/>
    public string Name => $"Move {task.Label}";

    /// <inheritdoc/>
    public void Apply() => _from = document.Move(task, location);

    /// <inheritdoc/>
    public void Undo() => document.Move(task, _from);
}
