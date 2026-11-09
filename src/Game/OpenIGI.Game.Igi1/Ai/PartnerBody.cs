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
/// <param name="ClipTime">
/// In a whole-body clip -- a ladder's, a computer's, a lock's, a vault's -- where its body is in <paramref name="Animation"/>; it is
/// posed there, not played.
/// </param>
/// <param name="Name">Its player's name, shown over its head.</param>
/// <param name="Dead">Whether it has fallen: it lies where it fell until a teammate revives it.</param>
/// <param name="Revive">How far its revival has gone, in <c>[0,1]</c>, while it lies there.</param>
/// <param name="Model">The model its look is built from, or <see langword="null"/> for the level's own player's.</param>
/// <param name="CarriesBomb">Whether it carries the bomb the bomb modes plant, drawn on its back.</param>
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
    float? ClipTime = null,
    string? Name = null,
    bool Dead = false,
    float? Revive = null,
    string? Model = null,
    bool CarriesBomb = false)
{
    /// <summary>Gets a partner's body from its rig, followed as the mission's own player's is.</summary>
    /// <param name="key">What the body follows.</param>
    /// <param name="player">The rig.</param>
    /// <param name="clipTime">Where its body is in its ladder or interaction clip, or <see langword="null"/>.</param>
    /// <returns>The body.</returns>
    public static PartnerBody Of(object key, PlayerCamera player, float? clipTime = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        // In a whole-body clip the body is where the clip has it, facing as the clip faces it: the same clip the server's
        // snapshot carries to every other window.
        PlayerBodyClip? clip = clipTime is null ? null : player.BodyClip;
        return new PartnerBody(
            key,
            clip?.Feet ?? player.Body.Position,
            clip?.Facing ?? -player.Body.Yaw,
            clip?.Animation ?? player.LocomotionAnimation,
            clip is not null || player.LocomotionAnimationLoops,
            new PlayerBodyPose(
                player.UpperBodyAnimation,
                player.UpperBodyAnimationLoops,
                player.UpperBodyBlendSeconds,
                player.UpperBodySequence,
                player.AimPitch),
            player.Weapons?.InHand?.Weapon,
            player.Weapons?.ShotSequence ?? 0,
            player.FiredThisTick,
            clip is null ? null : clipTime);
    }
}
