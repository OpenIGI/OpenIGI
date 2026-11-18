using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The level's soldiers, which only the server's minds run, sent for every client to pose its copies by.</content>
public sealed partial class Igi2ReplicatedWorld
{
    /// <summary>Gets the entity a soldier goes by in snapshots: its place among the level's soldiers, past the players.</summary>
    /// <param name="index">Its place in <see cref="Igi2Soldiers.Bodies"/>, which every machine playing the level makes alike.</param>
    /// <returns>The entity.</returns>
    public static int SoldierEntity(int index) => ReplicatedEntity.SoldierBase + index;

    /// <summary>Gets a soldier's place among the level's soldiers from its entity.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The place, or <see langword="null"/> for a player.</returns>
    public static int? SoldierIndex(int entity) => ReplicatedEntity.IsSoldier(entity) ? entity - ReplicatedEntity.SoldierBase : null;

    /// <summary>One soldier as a snapshot carries it.</summary>
    /// <param name="index">Its place among the level's soldiers.</param>
    /// <param name="soldier">The soldier.</param>
    /// <returns>Its replicated state.</returns>
    public static ReplicatedHuman ReplicateSoldier(int index, Igi2HumanSoldier soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        Igi2SoldierPose pose = soldier.PuppetPose();
        float max = soldier.Damage.MaxDamage;
        float left = max > 0 ? Math.Clamp(1f - (soldier.Damage.Damage / max), 0f, 1f) : 1f;
        return new ReplicatedHuman(
            SoldierEntity(index),
            Int3.From(pose.Position),
            default,
            Quantize.AngleToBits(pose.Gamma),
            Quantize.AngleToBits(pose.Pitch),
            (ushort)Math.Clamp(pose.State, 0, ushort.MaxValue),
            0,
            (ushort)Math.Clamp(soldier.Weapon.Inventory?.Current?.WeaponType.Id ?? 0, 0, ushort.MaxValue),
            (byte)Math.Round(left * 255),
            (byte)Math.Clamp(soldier.Team, 0, 255),
            pose.Alive ? ReplicatedHumanCondition.Alive : ReplicatedHumanCondition.None,
            pose.Shots);
    }

    /// <summary>Gets the pose a client stands its copy of a soldier in, from what a snapshot carries.</summary>
    /// <param name="human">The soldier as sent.</param>
    /// <returns>The pose.</returns>
    public static Igi2SoldierPose PoseOf(in ReplicatedHuman human) => new(
        human.Position.ToVector(),
        Quantize.AngleFromBits(human.Yaw),
        Quantize.AngleFromBits(human.Pitch),
        human.State,
        (human.Flags & ReplicatedHumanCondition.Alive) != 0,
        human.Shots);

    // Every soldier the level has in play; one its container keeps out of the world is left out, there and here.
    private void CaptureSoldiers(List<ReplicatedHuman> into)
    {
        if (_level.Soldiers is not { } soldiers)
        {
            return;
        }

        for (int i = 0; i < soldiers.Bodies.Count; i++)
        {
            if (soldiers.Bodies[i] is { IsActive: true } soldier)
            {
                into.Add(ReplicateSoldier(i, soldier));
            }
        }
    }
}
