namespace OpenIGI.Game.Level;

/// <summary>A task that runs itself each tick: one whose type links a run handler.</summary>
/// <remarks>
/// What the level's run looks for in a task it reaches (<see cref="LevelRun.Run"/>). <see cref="TickedTask"/> is one whose
/// class spells out its parameters; <see cref="DeclaredLevelTask"/> one that keeps the parameters its level declared.
/// </remarks>
public interface ILevelTicked
{
    /// <summary>Runs the task for one tick.</summary>
    /// <param name="level">The level's run, which runs any children it is handed.</param>
    void Run(LevelRun level);
}
