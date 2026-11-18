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

        // The hits the server's copy of this client's player took, felt by this one: the flinch that stopped the server's in
        // its stride, the push along the shot, the cry, the shake and the red flash. The damage comes with the player's health.
        Player.Igi2HumanPlayer own = _level.Human;
        foreach (ReplicatedSense sense in snapshot.Senses ?? [])
        {
            if (sense.Entity == ownEntity && sense.Kind == Igi2NetworkHuman.HitSense && !own.IsDead
                && float.IsFinite(sense.A) && Enum.IsDefined((Humans.Igi2DamagePart)(int)sense.A))
            {
                own.FeelHit((Humans.Igi2DamagePart)(int)sense.A, sense.Vector);
            }
        }

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
            Interaction.Igi2LiftTask[] cars = Lifts;
            for (int i = 0; i < lifts.Length && i < cars.Length; i++)
            {
                ReplicatedLift lift = lifts[i];
                cars[i].Follow(new Interaction.Igi2LiftLeg(lift.Floor, lift.Target, lift.Position.ToVector(), lift.Speed / Igi2LevelState.TicksPerSecond), ticksAhead);
            }
        }
    }

    // The level's lifts, in its tree's order, as the snapshots carry them.
    private Interaction.Igi2LiftTask[] Lifts => _lifts ??= [.. _level.LevelTasks().OfType<Interaction.Igi2LiftTask>()];

    /// <inheritdoc/>
    /// <remarks>
    /// The car this client's player stands on, as Project I.G.I.'s client names it: a prediction made in a lift is weighed as a
    /// place in the car, since the server ran the command a tick or two earlier or later in the car's travel. Weighed in the
    /// world, every ride was an error of that many ticks of travel, and the player was put back through the floor again and again.
    /// </remarks>
    public PlatformFrame? LocalPlatform
    {
        get
        {
            Player.Igi2HumanPlayer own = _level.Human;
            if (own.IsDead || own.GroundCollider is not { } ground)
            {
                return null;
            }

            Interaction.Igi2LiftTask[] lifts = Lifts;
            for (int i = 0; i < lifts.Length && i < ReplicatedLift.MaxPerSnapshot; i++)
            {
                if (lifts[i].Carries(ground))
                {
                    return new PlatformFrame(i, lifts[i].CurrentPosition);
                }
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public Core.Numerics.Vector3d? PlatformOrigin(int index) =>
        (uint)index < (uint)Lifts.Length ? Lifts[index].CurrentPosition : null;
}
