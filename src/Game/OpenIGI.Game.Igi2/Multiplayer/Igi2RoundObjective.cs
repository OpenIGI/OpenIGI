using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>A <c>Network_Objective</c> as the round judges it: its expressions evaluated by the level, as the objective's run handler does.</summary>
/// <param name="task">The objective.</param>
/// <param name="level">The level, which evaluates its expressions.</param>
public sealed class Igi2RoundObjective(Igi2NetworkObjectiveTask task, Igi2LevelRun level) : IRoundObjective
{
    private LevelExpression? _completed;
    private LevelExpression? _failed;

    /// <summary>Gets the objective.</summary>
    public Igi2NetworkObjectiveTask Task { get; } = task ?? throw new ArgumentNullException(nameof(task));

    /// <inheritdoc/>
    public int Order => Task.Order;

    /// <inheritdoc/>
    public int Team => Task.Team;

    /// <inheritdoc/>
    /// <remarks>An empty expression never holds, as <c>Level_RunLevelExpression</c> is not called for one.</remarks>
    public bool IsCompleted()
    {
        _completed ??= Igi2LevelExpressions.Create(Task.ObjectiveExpression, Task.Name, level.Natives);
        return !_completed.IsEmpty && level.Evaluate(_completed) != 0.0;
    }

    /// <inheritdoc/>
    public bool IsFailed()
    {
        _failed ??= Igi2LevelExpressions.Create(Task.FailedExpression, Task.Name, level.Natives);
        return !_failed.IsEmpty && level.Evaluate(_failed) != 0.0;
    }
}
