using OpenIGI.Game.Ai;
using OpenIGI.Game.Replays;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What of Project I.G.I.'s running mission its fingerprint folds (<see cref="ISimulationDigest"/>).</summary>
/// <remarks>
/// The level's one global stream (every draw a tick made moves it), the player's body, view, health and weapon, and each soldier
/// as the simulation last left it, in the simulation's own order. The guards' spread stream is a <see cref="Random"/>, whose
/// state cannot be read; what it decided shows in where the rounds went.
/// </remarks>
internal sealed class Igi1SimulationDigest
{
    private readonly List<SoldierSnapshot> _soldiers = [];

    /// <summary>Folds where a mission's world stands.</summary>
    /// <param name="world">The world.</param>
    /// <param name="hash">The fingerprint so far.</param>
    public void Fold(MissionWorld world, ref SimulationHash hash)
    {
        ArgumentNullException.ThrowIfNull(world);
        hash.Add(world.SoundRandom);
        hash.Add(world.IsPlayerDead);

        if (world.Player is { } player)
        {
            hash.Add(true);
            hash.Add(player.Body.Position);
            hash.Add(player.Body.Yaw);
            hash.Add(player.Body.Pitch);
            hash.Add(player.Body.Health.Damage);
            if (player.Weapons is { } weapons)
            {
                hash.Add((int)weapons.State);
                hash.Add(weapons.ChosenWeaponId);
                hash.Add(weapons.ShotSequence);
                hash.Add(weapons.Carried.Count);
                hash.Add(weapons.RecoilPitch);
                hash.Add(weapons.RecoilYaw);
            }
        }
        else
        {
            hash.Add(false);
        }

        _soldiers.Clear();
        world.Soldiers?.Snapshot(_soldiers);
        hash.Add(_soldiers.Count);
        foreach (SoldierSnapshot soldier in _soldiers)
        {
            hash.Add(soldier.Position);
            hash.Add(soldier.IsDead);
            hash.Add(soldier.Animation);
            hash.Add(soldier.HealthFraction);
            hash.Add(soldier.Facing);
            hash.Add(soldier.Alerted);
            hash.Add(soldier.SeesTarget);
        }

        if (world.Objectives is { } objectives)
        {
            hash.Add(objectives.IsComplete);
            hash.Add(objectives.IsFailed);
            foreach (OpenIGI.Game.World.MissionObjective objective in objectives.Objectives)
            {
                hash.Add((int)objective.State);
            }
        }
    }
}
