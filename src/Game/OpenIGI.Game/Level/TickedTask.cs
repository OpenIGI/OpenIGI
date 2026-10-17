using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Level;

/// <summary>A task with a class of its own: one that runs itself each tick, the original's run handler.</summary>
/// <remarks>
/// The base of every typed task of either title, and of any a mod adds. The default runs nothing; a container that should
/// run its children calls <see cref="LevelRun.RunChildren"/> from its own <see cref="Run"/>.
/// </remarks>
public abstract class TickedTask : GameTask
{
    /// <summary>Runs this task for one tick.</summary>
    /// <param name="level">The level's run, which runs any children it is handed.</param>
    public virtual void Run(LevelRun level)
    {
    }
}
