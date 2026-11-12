using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// What a player on this machine is shown and told when it is hurt: the grunt, the view's shake, the red wash, the sounds
/// and the death's fall and camera.
/// </summary>
/// <remarks>
/// Done here, on the player's own rig and life and in the place it stands (<see cref="IIgi1PlayerSurroundings"/>), where a
/// player on another machine is sent the same calls instead. Each draws on the stream its handler in the original does, in
/// its order, so the level's chance runs the same whoever is hurt.
/// </remarks>
/// <param name="rig">The player's rig, or <see langword="null"/> while it has none.</param>
/// <param name="life">The player's life, which holds the wash and the grunt's quiet time.</param>
/// <param name="surroundings">What the player is hurt in.</param>
internal sealed class Igi1PlayerSenses(Func<PlayerCamera?> rig, Igi1PlayerLife life, IIgi1PlayerSurroundings surroundings) : IPlayerSenses
{
    /// <inheritdoc/>
    public void Grunt()
    {
        if (surroundings.Tick < life.HitQuietUntil)
        {
            return;
        }

        life.HitQuietUntil = surroundings.Tick + HumanVariantSound.HitCooldownTicks(surroundings.SoundRandom);
        surroundings.PlayHit(HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants);
    }

    /// <inheritdoc/>
    public void Blow() =>
        surroundings.PlayDefinition((surroundings.SoundRandom.NextRaw() & 1) != 0 ? "punched_01" : "punched_02", priority: 250);

    /// <inheritdoc/>
    public void HitShake()
    {
        float share = Math.Clamp(surroundings.SoundRandom.NextSingle(), 0.25f, 1f);
        Shake(HumanViewShake.HitPositionUnits * share, HumanViewShake.HitAngleRadians * share, HumanViewShake.HitTicks);
    }

    /// <inheritdoc/>
    public void Shake(float positionUnits, float angleRadians, int ticks) =>
        rig()?.InstallViewShake(positionUnits, angleRadians, ticks);

    /// <inheritdoc/>
    /// <remarks>
    /// The original's handler transforms the round into camera space before writing the four corner alphas. It picks
    /// 0.4-0.8 rather than one opaque wash and replaces the prior hit, so automatic fire remains a directional cue instead of
    /// a full-screen strobe.
    /// </remarks>
    public void FlashFrom(Vector3f travel)
    {
        if (rig() is { } player)
        {
            life.DamageFlash.Hit(player.Camera.Orientation.Transposed().Transform(travel), surroundings.SoundRandom.NextSingle());
        }
    }

    /// <inheritdoc/>
    public void FlashCorners() =>
        life.DamageFlash.HitEveryCorner(
            surroundings.SoundRandom.NextSingle(),
            surroundings.SoundRandom.NextSingle(),
            surroundings.SoundRandom.NextSingle(),
            surroundings.SoundRandom.NextSingle());

    /// <inheritdoc/>
    public void FlashCentre() => life.DamageFlash.Hit(Vector3f.Zero, surroundings.SoundRandom.NextSingle());

    /// <inheritdoc/>
    public void Sound(string definition) => surroundings.PlayDefinition(definition, priority: 250);

    /// <inheritdoc/>
    public void Died(in PlayerFall fall)
    {
        // The player receives no further simulation ticks after death, so the weapon sound player cannot observe a quiet
        // tick and retire an automatic loop on its own.
        surroundings.SilenceForDeath();

        // The lethal hit has already written the retail corner flash. Do not replace it with a uniform full-screen wash
        // here: 0x412440 writes the flash as part of damage receipt, before it changes the locomotion state to death.
        surroundings.KillBody(fall.Clip, fall.Feet, fall.ThrowFacing, fall.Bone, fall.Travel, fall.Category, fall.Velocity);
        surroundings.BeginDeathCamera(fall.Feet, fall.ThrowFacing);

        // And the death sound -- state 47's own sub_4638A0(human, "player_death_", 3).
        surroundings.PlayDeath(HumanPlayerDeath.SoundPrefix, HumanPlayerDeath.SoundVariants);
    }

    /// <inheritdoc/>
    public void Revived()
    {
        life.ClearDamageFlash();
        surroundings.ReviveBody();
        surroundings.EndDeathCamera();
    }
}
