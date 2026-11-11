using OpenIGI.Game.Level;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A panel that raises the alarm when its script says so: Project I.G.I.'s <c>AlarmControl</c>, a kind of <c>Terminal</c>.</summary>
/// <remarks>
/// Its run handler (<c>0x4408A0</c>) runs its children, then the terminal's part -- whether it is on -- and its trigger and
/// alarm expressions, which <see cref="AiAlarmControlRegistry"/> holds the state and the level's tick for. The task runs it
/// where the level's walk reaches it. Its parameters are still the ones the level declares, read by name.
/// </remarks>
public sealed class Igi1AlarmControlTask : DeclaredLevelTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AlarmControl";

    /// <summary>Gets or sets the mission's alarm controls, which step this one; none until they are built.</summary>
    internal AiAlarmControlRegistry? Alarms { get; set; }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        Alarms?.RunFromWalk(this);
    }
}
