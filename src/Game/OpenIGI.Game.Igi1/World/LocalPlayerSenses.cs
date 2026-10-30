using OpenIGI.Core.Numerics;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The player's reactions on this machine's screen and through its ears, as the original's player handlers make them.</summary>
/// <param name="mission">The mission, whose player, flash and sound stream these are.</param>
/// <param name="sounds">The player's cries.</param>
/// <param name="audio">The player's held voices, which a death stops.</param>
/// <param name="host">What plays and shows them.</param>
/// <remarks>
/// Every draw is the sound stream's, made where the original's handlers make it, so a reaction keeps the stream where
/// retail's would be whichever harm raised it.
/// </remarks>
internal sealed class LocalPlayerSenses(MissionWorld mission, Igi1HumanSounds sounds, Igi1MissionAudio audio, IPlayerHarmHost host) : IPlayerSenses
{
    /// <inheritdoc/>
    /// <remarks>
    /// <c>player_hit_</c> through <c>0x4637C0</c>'s per-human rate limit. Flat, not positional: the sound belongs to the
    /// player, who is the listener. The limit is what lets sustained fire stagger the player without stuttering the sound.
    /// </remarks>
    public void Grunt()
    {
        if (host.Game.Clock.TickCount < mission.PlayerHitQuietUntil)
        {
            return;
        }

        mission.PlayerHitQuietUntil = host.Game.Clock.TickCount + HumanVariantSound.HitCooldownTicks(mission.SoundRandom);
        sounds.PlayHit(HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants, null);
    }

    /// <inheritdoc/>
    /// <remarks><c>0x412CE0</c>: <c>punched_01</c> or <c>punched_02</c> by the parity of a draw made after the grunt's.</remarks>
    public void Blow() =>
        host.PlayDefinition((mission.SoundRandom.NextRaw() & 1) != 0 ? "punched_01" : "punched_02", positionInUnits: null, priority: 250);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>0x412440</c> and <c>0x412CE0</c>: a draw clamped to <c>[0.25, 1]</c> scales the full 5 cm and 0.75 degrees, for
    /// fifteen ticks.
    /// </remarks>
    public void HitShake()
    {
        float share = Math.Clamp(mission.SoundRandom.NextSingle(), 0.25f, 1f);
        Shake(HumanViewShake.HitPositionUnits * share, HumanViewShake.HitAngleRadians * share, HumanViewShake.HitTicks);
    }

    /// <inheritdoc/>
    public void Shake(float positionUnits, float angleRadians, int ticks) =>
        mission.Player?.InstallViewShake(positionUnits, angleRadians, ticks);

    /// <inheritdoc/>
    /// <remarks>
    /// The retail handler transforms the round into camera space before writing the four corner alphas. It picks 0.4-0.8
    /// rather than one opaque wash and replaces the prior hit, so automatic fire remains a directional cue instead of a
    /// full-screen strobe.
    /// </remarks>
    public void FlashFrom(Vector3f travel)
    {
        if (mission.Player is { } player)
        {
            mission.DamageFlash.Hit(player.Camera.Orientation.Transposed().Transform(travel), mission.SoundRandom.NextSingle());
        }
    }

    /// <inheritdoc/>
    public void FlashCorners() =>
        mission.DamageFlash.HitEveryCorner(
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle());

    /// <inheritdoc/>
    public void FlashCentre() => mission.DamageFlash.Hit(Vector3f.Zero, mission.SoundRandom.NextSingle());

    /// <inheritdoc/>
    public void Sound(string definition) => host.PlayDefinition(definition, positionInUnits: null, priority: 250);

    /// <inheritdoc/>
    public void Died(in PlayerFall fall)
    {
        audio.StopBinocularTransitionSound();
        audio.StopLadderSlideSound();

        // The player receives no further simulation ticks after death, so the weapon sound player cannot observe a quiet
        // tick and retire an automatic loop on its own.
        audio.ResetWeaponSounds();

        // The lethal hit has already written the retail corner flash. Do not replace it with a uniform full-screen wash
        // here: 0x412440 writes the flash as part of damage receipt, before it changes the locomotion state to death.
        mission.SoldierPresenter?.KillPlayerBody(fall.Clip, fall.Feet, fall.ThrowFacing, fall.Bone, fall.Travel, fall.Category, fall.Velocity);
        if (mission.Solids is { } deathSolids)
        {
            host.BeginDeathCamera(fall.Feet, fall.ThrowFacing, deathSolids.Caster());
        }

        // And the death sound -- state 47's own sub_4638A0(human, "player_death_", 3).
        sounds.PlayDeath(HumanPlayerDeath.SoundPrefix, HumanPlayerDeath.SoundVariants, null);
    }

    /// <inheritdoc/>
    public void Revived()
    {
        mission.DamageFlash = new();
        mission.SoldierPresenter?.RevivePlayerBody();
        host.EndDeathCamera();
    }
}
