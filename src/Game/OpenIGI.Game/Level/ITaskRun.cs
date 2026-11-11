using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Level;

/// <summary>What carries out a task's run handler when the state it works on is kept beside the task rather than in it.</summary>
/// <typeparam name="TTask">The task's class.</typeparam>
/// <remarks>
/// <para>
/// A task says who runs it: a soldier's task is run by the game's soldier simulation, or by another title's squad standing
/// in for it; a lift's by what steps the lifts. The task's own <c>Run</c> hands the tick to it, at the task's place in the
/// tree, and a task nobody runs falls back to the level run's rule for a type with no handler.
/// </para>
/// <para>
/// It is a step on the way: where a type's state has moved into its task, the task runs itself and needs none of this.
/// </para>
/// </remarks>
public interface ITaskRun<in TTask>
    where TTask : GameTask
{
    /// <summary>Runs the task for one tick.</summary>
    /// <param name="task">The task.</param>
    /// <param name="level">The level's run, which runs any children it is handed.</param>
    void Run(TTask task, LevelRun level);
}
