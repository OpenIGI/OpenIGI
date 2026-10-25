namespace OpenIGI.Game.World;

/// <summary>A task the level creates while it runs: one of <c>Level_ptDynamicQTask</c>'s children.</summary>
public interface IDynamicTask
{
    /// <summary>A tick of the task, its run handler.</summary>
    /// <returns><see langword="false"/> once the task has deleted itself.</returns>
    bool Run();
}

/// <summary>
/// The tasks the level made while it runs, <c>Level_ptDynamicQTask</c>'s children: grenades, explosions and what they leave,
/// each run once a tick in the order they were made.
/// </summary>
/// <remarks>
/// The original walks a task's children through its protected task stack: before a child runs, the one after it is
/// remembered, and the walk goes on to it. A task made during a child's run is added at the end, so it runs the same tick
/// only when the child was not the last; one made by the last child waits for the next tick.
/// </remarks>
public sealed class DynamicTasks
{
    private readonly List<IDynamicTask> _tasks = [];

    /// <summary>Gets the tasks, in the order they were made.</summary>
    public IReadOnlyList<IDynamicTask> Tasks => _tasks;

    /// <summary>Adds a task at the end, <c>QTask_NewFn</c> under the dynamic task.</summary>
    /// <param name="task">The task.</param>
    public void Add(IDynamicTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _tasks.Add(task);
    }

    /// <summary>A tick of every task, in order.</summary>
    public void Run()
    {
        int i = 0;
        while (i < _tasks.Count)
        {
            bool hasNext = i + 1 < _tasks.Count;
            IDynamicTask task = _tasks[i];
            if (task.Run())
            {
                i++;
            }
            else
            {
                _tasks.RemoveAt(i);
            }

            if (!hasNext)
            {
                break;
            }
        }
    }
}
