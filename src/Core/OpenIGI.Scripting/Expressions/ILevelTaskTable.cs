namespace OpenIGI.Scripting.Expressions;

/// <summary>The level's tasks by script id, as a level expression's compile looks them up.</summary>
/// <remarks>
/// Project I.G.I.'s compile (<c>0x4F1400</c>) reads a word's trailing id out of the task table (<c>sub_4F1030</c>, over
/// <c>dword_A71890</c>), keeps a reference to each task it finds, and writes <c>0</c> for a word whose task is not there.
/// Before each run it checks those references (<c>sub_4F1740</c>) and compiles again once any of them has died.
/// </remarks>
public interface ILevelTaskTable
{
    /// <summary>Finds the task bound to a script id.</summary>
    /// <param name="id">The id, which may lie outside the table.</param>
    /// <returns>The task, or <see langword="null"/> when none is bound to it.</returns>
    object? FindTask(int id);

    /// <summary>Gets whether a task the table gave out is still alive.</summary>
    /// <param name="task">A task <see cref="FindTask"/> returned.</param>
    /// <returns>Whether it has not been deleted since.</returns>
    bool IsLive(object task);
}
