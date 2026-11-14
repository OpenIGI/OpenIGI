using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Engine.Tasks;

/// <summary>A reference to a task that reads as empty once the task has been deleted.</summary>
/// <typeparam name="T">The task's class.</typeparam>
/// <remarks>
/// <para>
/// The original hands out a cell holding the task's address and clears every such cell when the task is deleted
/// (<c>TaskRef_Add</c> <c>0x401AE0</c>, <c>Task_Delete</c> <c>0x4015F0</c>; I.G.I. 2 <c>QTask_NewQTaskRef</c>), so what
/// held the reference finds nothing there instead of freed memory. A deleted task is still an object here, and holding one
/// by a plain field is how a door goes on opening for a soldier who is gone; this reads the task's own deleted flag, which
/// is set before any of the deletion's handlers run.
/// </para>
/// <para>The default value refers to nothing.</para>
/// </remarks>
public readonly struct TaskRef<T> : IEquatable<TaskRef<T>>
    where T : GameTask
{
    private readonly T? _task;

    /// <summary>Initializes a new instance of the <see cref="TaskRef{T}"/> struct.</summary>
    /// <param name="task">The task referred to, or <see langword="null"/> for none.</param>
    public TaskRef(T? task) => _task = task;

    /// <summary>Gets the task, or <see langword="null"/> when there is none or it has been deleted.</summary>
    public T? Target => _task is { IsDeleted: false } ? _task : null;

    /// <summary>Gets a value indicating whether the task is still there.</summary>
    public bool IsAlive => _task is { IsDeleted: false };

    /// <summary>Makes a reference to a task.</summary>
    /// <param name="task">The task, or <see langword="null"/> for none.</param>
    public static implicit operator TaskRef<T>(T? task) => new(task);

    /// <summary>Compares two references.</summary>
    /// <param name="left">One reference.</param>
    /// <param name="right">The other.</param>
    /// <returns>Whether they refer to the same task.</returns>
    public static bool operator ==(TaskRef<T> left, TaskRef<T> right) => left.Equals(right);

    /// <summary>Compares two references.</summary>
    /// <param name="left">One reference.</param>
    /// <param name="right">The other.</param>
    /// <returns>Whether they refer to different tasks.</returns>
    public static bool operator !=(TaskRef<T> left, TaskRef<T> right) => !left.Equals(right);

    /// <summary>Gets the task when it is still there.</summary>
    /// <param name="task">Receives the task.</param>
    /// <returns>Whether there is one and it has not been deleted.</returns>
    public bool TryGet([NotNullWhen(true)] out T? task)
    {
        task = Target;
        return task is not null;
    }

    /// <inheritdoc/>
    /// <remarks>Two references to the same task are equal whether or not it has since been deleted.</remarks>
    public bool Equals(TaskRef<T> other) => ReferenceEquals(_task, other._task);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TaskRef<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _task is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_task);

    /// <inheritdoc/>
    public override string ToString() => Target?.ToString() ?? "(none)";
}
