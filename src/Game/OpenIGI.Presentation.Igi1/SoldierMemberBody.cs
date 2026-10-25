using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Presentation.Igi1;

/// <summary>A soldier as the shots find it: its task, and its colliders in the drawn pose.</summary>
/// <param name="hits">What casts a round at the soldier's colliders.</param>
/// <param name="group">The soldier's group, whose collision mesh it is cast at.</param>
/// <param name="member">The soldier.</param>
internal sealed class SoldierMemberBody(SoldierHitTesting hits, SoldierGroup group, SoldierMember member) : IShootableBody
{
    /// <inheritdoc/>
    public GameTask Task => member.Agent.View.Soldier;

    /// <inheritdoc/>
    public bool TryMeet(Vector3d origin, Vector3d target, out HumanHit hit) =>
        hits.TryCastMember(group, member, origin, target, out hit);
}
