using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>What a debugging trace reads of the client's copy of the level (<see cref="NetProbe"/>). <b>Temporary.</b></content>
public sealed partial class Igi2ClientWorld
{
    /// <inheritdoc/>
    /// <remarks>A soldier as its body stands and its locomotion's state, which its snapshots carry; a partner where its body stands.</remarks>
    public ProbeDrawn? Drawn(int id)
    {
        if (Igi2ReplicatedWorld.SoldierIndex(id) is { } index)
        {
            return _level.Soldiers is { } soldiers && (uint)index < (uint)soldiers.Bodies.Count && soldiers.Bodies[index] is { } soldier
                ? new ProbeDrawn(soldier.Position, soldier.Locomotion.CurrentState, !soldier.IsDead)
                : null;
        }

        return _puppets.TryGetValue(id, out Igi2NetworkHuman? puppet)
            ? new ProbeDrawn(puppet.Human.Position, puppet.Human.Locomotion.CurrentState, !puppet.Human.IsDead)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>Its chest: two thirds of the way from where its body stands up to its eyes.</remarks>
    public bool AimAt(int id)
    {
        Humans.Igi2Human? human = Igi2ReplicatedWorld.SoldierIndex(id) is { } index
            ? _level.Soldiers is { } soldiers && (uint)index < (uint)soldiers.Bodies.Count ? soldiers.Bodies[index] : null
            : _puppets.TryGetValue(id, out Igi2NetworkHuman? puppet) ? puppet.Human : null;
        return human is not null && _level.AimAt(Vector3d.Lerp(human.Position, human.View.Position, 0.65));
    }
}
