using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>What kind of change a <see cref="MissionChange"/> reports.</summary>
public enum MissionChangeKind
{
    /// <summary>A parameter's value changed.</summary>
    Parameter,

    /// <summary>A task's label or id changed.</summary>
    Identity,

    /// <summary>A task was added to the tree.</summary>
    Added,

    /// <summary>A task was taken out of the tree.</summary>
    Removed,

    /// <summary>A task moved to another parent or position.</summary>
    Moved,
}

/// <summary>One change to a mission document, for the views and the live world to follow.</summary>
/// <param name="Kind">What changed.</param>
/// <param name="Task">The task it changed.</param>
/// <param name="Parameter">For a parameter change, the parameter's name.</param>
public readonly record struct MissionChange(MissionChangeKind Kind, MissionTask Task, string? Parameter = null);

/// <summary>Where a task sits: under a parent at a child index, or at the top level at a statement index.</summary>
/// <param name="Parent">The parent, or <see langword="null"/> for a top-level task.</param>
/// <param name="Index">The child index, or for a top-level task the index among the document's statements.</param>
public readonly record struct TaskLocation(MissionTask? Parent, int Index);

/// <content>The primitive edits the editor's commands are built from, each announcing itself through <see cref="Changed"/>.</content>
public sealed partial class MissionDocument
{
    /// <summary>Raised after every edit made through the document.</summary>
    public event Action<MissionChange>? Changed;

    /// <summary>Gets where a task is.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Its location.</returns>
    /// <exception cref="ArgumentException">The task is not in this document.</exception>
    public TaskLocation Locate(MissionTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Parent is { } parent)
        {
            return new TaskLocation(parent, parent.Children.IndexOf(task));
        }

        int index = Statements.FindIndex(s => s is MissionTaskStatement root && ReferenceEquals(root.Task, task));
        return index >= 0 ? new TaskLocation(null, index) : throw new ArgumentException($"{task} is not in this document.", nameof(task));
    }

    /// <summary>Gets whether a task is in the document's trees.</summary>
    /// <param name="task">The task.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(MissionTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        MissionTask top = task;
        while (top.Parent is { } parent)
        {
            top = parent;
        }

        return Statements.Exists(s => s is MissionTaskStatement root && ReferenceEquals(root.Task, top));
    }

    /// <summary>Puts a detached task into the tree.</summary>
    /// <param name="task">The task, with no parent.</param>
    /// <param name="location">Where it goes.</param>
    public void Insert(MissionTask task, TaskLocation location)
    {
        ArgumentNullException.ThrowIfNull(task);
        Attach(task, location);

        Changed?.Invoke(new MissionChange(MissionChangeKind.Added, task));
    }

    /// <summary>Takes a task, and everything under it, out of the tree.</summary>
    /// <param name="task">The task.</param>
    /// <returns>Where it was, so the removal can be reversed.</returns>
    public TaskLocation Remove(MissionTask task)
    {
        TaskLocation location = Locate(task);
        Detach(task, location);

        Changed?.Invoke(new MissionChange(MissionChangeKind.Removed, task));
        return location;
    }

    /// <summary>Moves a task to a new parent or position.</summary>
    /// <param name="task">The task.</param>
    /// <param name="location">Where it goes, measured after it has been taken out of where it was.</param>
    /// <returns>Where it was.</returns>
    /// <exception cref="InvalidOperationException">The move would put a task inside its own subtree.</exception>
    public TaskLocation Move(MissionTask task, TaskLocation location)
    {
        for (MissionTask? ancestor = location.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, task))
            {
                throw new InvalidOperationException($"{task} cannot be moved inside itself.");
            }
        }

        TaskLocation from = Locate(task);
        Detach(task, from);
        Attach(task, location);

        Changed?.Invoke(new MissionChange(MissionChangeKind.Moved, task));
        return from;
    }

    /// <summary>Replaces a parameter's script arguments.</summary>
    /// <param name="task">The task.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="arguments">The new arguments, as many as the parameter's width.</param>
    /// <returns>The arguments it had.</returns>
    /// <exception cref="ArgumentException">The task has no such parameter, or the width differs.</exception>
    public QscExpression[] SetArguments(MissionTask task, string parameter, IReadOnlyList<QscExpression> arguments)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(arguments);
        MissionParameter target = task.FindParameter(parameter)
            ?? throw new ArgumentException($"{task} has no parameter '{parameter}'.", nameof(parameter));

        // AnimData is the one parameter whose width follows its own value; every other one keeps its width.
        bool variable = target.Declaration.Type is { } type && ScriptParamTypeInfo.IsVariableLength(type);
        if (!variable && arguments.Count != target.Arguments.Count)
        {
            throw new ArgumentException($"'{parameter}' takes {target.Arguments.Count} arguments, not {arguments.Count}.", nameof(arguments));
        }

        QscExpression[] previous = [.. target.Arguments];
        target.Arguments.Clear();
        target.Arguments.AddRange(arguments);
        Changed?.Invoke(new MissionChange(MissionChangeKind.Parameter, task, parameter));
        return previous;
    }

    /// <summary>Changes a task's label and id.</summary>
    /// <param name="task">The task.</param>
    /// <param name="label">The new label.</param>
    /// <param name="id">The new id expression.</param>
    /// <returns>The label and id expression it had.</returns>
    public (string Label, QscExpression Id) SetIdentity(MissionTask task, string label, QscExpression id)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(id);
        (string Label, QscExpression Id) previous = (task.Label, task.IdExpression);
        task.Label = label;
        task.IdExpression = id;
        Changed?.Invoke(new MissionChange(MissionChangeKind.Identity, task));
        return previous;
    }

    /// <summary>Declares a task type's parameters, after the declarations already there, so tasks of it can be written.</summary>
    /// <param name="declaration">The declaration.</param>
    /// <returns><see langword="false"/> when the type is declared already, and nothing changed.</returns>
    public bool Declare(TaskDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (Declarations.ContainsKey(declaration.TypeName))
        {
            return false;
        }

        int index = Statements.FindLastIndex(static s => s is MissionDeclarationStatement) + 1;
        Statements.Insert(index, new MissionDeclarationStatement(declaration));
        Declarations[declaration.TypeName] = declaration;
        return true;
    }

    /// <summary>Takes a declaration <see cref="Declare"/> added out again.</summary>
    /// <param name="typeName">The declared type.</param>
    /// <returns><see langword="false"/> when the type was not declared.</returns>
    public bool Undeclare(string typeName)
    {
        int index = Statements.FindIndex(s => s is MissionDeclarationStatement d && d.Declaration.TypeName == typeName);
        if (index < 0)
        {
            return false;
        }

        Statements.RemoveAt(index);
        Declarations.Remove(typeName);
        return true;
    }

    private void Attach(MissionTask task, TaskLocation location)
    {
        if (location.Parent is { } parent)
        {
            parent.AddChild(task, location.Index);
        }
        else
        {
            Statements.Insert(Math.Clamp(location.Index, 0, Statements.Count), new MissionTaskStatement(task));
        }
    }

    private void Detach(MissionTask task, TaskLocation from)
    {
        if (from.Parent is { } parent)
        {
            parent.RemoveChild(task);
        }
        else
        {
            Statements.RemoveAt(from.Index);
        }
    }
}
