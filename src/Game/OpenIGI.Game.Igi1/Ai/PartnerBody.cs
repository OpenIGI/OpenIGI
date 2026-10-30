using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A co-op partner's body as the frame draws it: where it stands, which way it faces, and what it is doing.</summary>
/// <param name="Key">What the body follows, the same object every tick: a partner's rig, or its entity id on a client.</param>
/// <param name="Feet">Where its feet are, in world units.</param>
/// <param name="Facing">Its heading, a gamma (the look's yaw negated).</param>
/// <param name="Animation">Its legs' clip.</param>
/// <param name="Loops">Whether that clip loops.</param>
/// <param name="Pose">Its torso's clip and aim.</param>
/// <param name="Weapon">The weapon in its hand, or <see langword="null"/> for none.</param>
/// <param name="ShotSequence">Its shots so far, which tells a new shot from the one before.</param>
/// <param name="Fired">Whether it fired this tick.</param>
/// <param name="LadderTime">On a ladder, where its body is in <paramref name="Animation"/>; it is posed there, not played.</param>
/// <param name="Name">Its player's name, shown over its head.</param>
public readonly record struct PartnerBody(
    object Key,
    Vector3d Feet,
    float Facing,
    int Animation,
    bool Loops,
    PlayerBodyPose Pose,
    WeaponType? Weapon = null,
    int ShotSequence = 0,
    bool Fired = false,
    float? LadderTime = null,
    string? Name = null)
{
    /// <summary>Gets a partner's body from its rig, followed as the mission's own player's is.</summary>
    /// <param name="key">What the body follows.</param>
    /// <param name="player">The rig.</param>
    /// <param name="ladderTime">Where its climbing body is in its ladder clip, or <see langword="null"/>.</param>
    /// <returns>The body.</returns>
    public static PartnerBody Of(object key, PlayerCamera player, float? ladderTime = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        bool climbing = player.LadderAnimation >= 0;
        return new PartnerBody(
            key,
            climbing ? player.LadderFeet : player.Body.Position,
            climbing ? player.LadderFacing : -player.Body.Yaw,
            climbing ? player.LadderAnimation : player.LocomotionAnimation,
            climbing || player.LocomotionAnimationLoops,
            new PlayerBodyPose(
                player.UpperBodyAnimation,
                player.UpperBodyAnimationLoops,
                player.UpperBodyBlendSeconds,
                player.UpperBodySequence,
                player.AimPitch),
            player.Weapons?.InHand?.Weapon,
            player.Weapons?.ShotSequence ?? 0,
            player.FiredThisTick,
            climbing ? ladderTime : null);
    }
}
