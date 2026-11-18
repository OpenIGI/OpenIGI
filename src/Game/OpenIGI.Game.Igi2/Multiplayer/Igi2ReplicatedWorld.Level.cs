using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The level's running state, written for the clients (<see cref="Igi2LevelState"/>).</content>
public sealed partial class Igi2ReplicatedWorld
{
    private ScriptVariableMirror? _variables;
    private Pickups.Igi2GunPickupTask[] _guns = [];
    private Interaction.Igi2DoorTask[]? _doorTasks;
    private bool _variablesMade;
    private readonly Igi2BlastLog _blasts = new();
    private readonly Igi2FiredLog _fired = new();
    private readonly Igi2VehicleStates _vehicles;

    private Interaction.Igi2LiftTask[]? _liftTasks;

    // The level's doors, found once.
    private Interaction.Igi2DoorTask[] DoorTasks => _doorTasks ??= Igi2DoorCommands.Of(_level);

    // A player as a snapshot carries it, with the lift it rides: a client draws it on its own copy of that car, as Project
    // I.G.I.'s does, not where the snapshots had it -- under the floor of a car going up, in the roof of one coming down.
    private ReplicatedHuman OnLift(ReplicatedHuman replicated, Player.Igi2HumanPlayer human)
    {
        _liftTasks ??= [.. _level.LevelTasks().OfType<Interaction.Igi2LiftTask>()];
        if (human.IsDead || human.GroundCollider is not { } ground)
        {
            return replicated;
        }

        for (int i = 0; i < _liftTasks.Length && i < ReplicatedLift.MaxPerSnapshot; i++)
        {
            if (_liftTasks[i].Carries(ground))
            {
                return replicated with { Platform = (byte)(i + 1) };
            }
        }

        return replicated;
    }

    // The guns lying that the level did not place, numbered for the clients.
    private readonly Igi2DroppedGuns _dropped = new();

    // The mines lying in the level, numbered for the clients.
    private readonly Igi2LaidMines _mines = new();

    // The level's lifts, in its tree's order, as a snapshot carries them; none for a level without any.
    private ReplicatedLift[]? Lifts()
    {
        _liftTasks ??= [.. _level.LevelTasks().OfType<Interaction.Igi2LiftTask>()];
        if (_liftTasks.Length == 0)
        {
            return null;
        }

        ReplicatedLift[] lifts = new ReplicatedLift[Math.Min(_liftTasks.Length, ReplicatedLift.MaxPerSnapshot)];
        for (int i = 0; i < lifts.Length; i++)
        {
            Interaction.Igi2LiftLeg leg = _liftTasks[i].Leg();
            lifts[i] = new ReplicatedLift(Int3.From(leg.Position), leg.Floor, leg.Target, leg.UnitsPerTick * Igi2LevelState.TicksPerSecond);
        }

        return lifts;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Never nothing, even on a level without scripts: a client holds its level behind the loading card until the server's
    /// state has come.
    /// </remarks>
    public byte[]? CaptureLevel()
    {
        if (!_variablesMade)
        {
            _variables = Igi2LevelState.MirrorOf(_level);
            _guns = Igi2LevelState.LaidOutGuns(_level);
            _variablesMade = true;
        }

        return Igi2LevelState.Capture(_guns, _variables, CurrentTick, _blasts, _level.Flow?.IsBreakKeyDown == true, (_dropped, _level.Human.Pickups), (_mines, _level.Mines));
    }
}
