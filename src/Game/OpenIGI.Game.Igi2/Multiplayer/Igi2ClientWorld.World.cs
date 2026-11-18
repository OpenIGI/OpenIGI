using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The rest of the world from each new snapshot: what the others fired, and the doors.</content>
public sealed partial class Igi2ClientWorld
{
    private long _worldTaken = long.MinValue;
    private Interaction.Igi2LiftTask[]? _lifts;

    /// <inheritdoc/>
    public void ApplyWorld(Snapshot snapshot, int? ownEntity, int ticksAhead, PredictionReconciler predictions)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Tick <= _worldTaken)
        {
            return;
        }

        _worldTaken = snapshot.Tick;

        // What the others fired since the last snapshot, fired again here from this client's copies of them.
        _fired.Replay(snapshot.Fired, ownEntity, _level.Weapons?.Inventory.Tables, ReplicaWeaponOf);
        if (snapshot.Doors is { } doors)
        {
            ApplyDoors(doors);
        }

        // The vehicles and guns, where the server's are.
        if (snapshot.Vehicles is { } vehicles)
        {
            _vehicles.Apply(vehicles, ticksAhead, _level.Level?.LevelFlow?.Tick ?? 0);
        }

        // The lifts, on the server's travel, each led by as far as this client's own player runs ahead.
        if (snapshot.Lifts is { } lifts)
        {
            _lifts ??= [.. _level.LevelTasks().OfType<Interaction.Igi2LiftTask>()];
            for (int i = 0; i < lifts.Length && i < _lifts.Length; i++)
            {
                ReplicatedLift lift = lifts[i];
                _lifts[i].Follow(new Interaction.Igi2LiftLeg(lift.Floor, lift.Target, lift.Position.ToVector(), lift.Speed / Igi2LevelState.TicksPerSecond), ticksAhead);
            }
        }
    }
}
