using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What Project I.G.I.'s player is hurt and dies in, in the game's own mission: its streams, its sounds, its soldiers and its host.</summary>
/// <param name="mission">The mission's world.</param>
/// <param name="sounds">The cries a hit and a death make.</param>
/// <param name="audio">The player's held voices, which a death stops, and a landing's sound.</param>
/// <param name="host">What plays and shows them.</param>
public sealed class Igi1MissionPlayerSurroundings(MissionWorld mission, Igi1HumanSounds sounds, Igi1MissionAudio audio, IPlayerHarmHost host)
    : IIgi1PlayerSurroundings
{
    /// <inheritdoc/>
    public int Tick => host.Game.Clock.TickCount;

    /// <inheritdoc/>
    public IgiRandom SoundRandom => mission.SoundRandom;

    /// <inheritdoc/>
    public Random ShotRandom => mission.ShotRandom;

    /// <inheritdoc/>
    public IOpenIgiLogger Logger => host.Logger;

    /// <inheritdoc/>
    public void PlayHit(string prefix, int variants) => sounds.PlayHit(prefix, variants, null);

    /// <inheritdoc/>
    public void PlayDeath(string prefix, int variants) => sounds.PlayDeath(prefix, variants, null);

    /// <inheritdoc/>
    public void PlayDefinition(string name, int priority) => host.PlayDefinition(name, positionInUnits: null, priority);

    /// <inheritdoc/>
    public void PlayBulletImpact(Vector3d position) => sounds.PlayBulletImpact(position, struckHuman: true, surfaceMaterial: 0);

    /// <inheritdoc/>
    public void PlayLanding((string Sound, Vector3d PositionInUnits) landing) => audio.PlayLandingSound(landing);

    /// <inheritdoc/>
    public void SilenceForDeath()
    {
        audio.StopBinocularTransitionSound();
        audio.StopLadderSlideSound();
        audio.StopTimedActionSound();
        audio.ResetWeaponSounds();
    }

    /// <inheritdoc/>
    public void KillBody(int fall, Vector3d feet, float facing, int bone, Vector3d travel, WeaponCategory category, Vector3d velocity) =>
        mission.SoldierPresenter?.KillPlayerBody(fall, feet, facing, bone, travel, category, velocity);

    /// <inheritdoc/>
    public void BeginDeathCamera(Vector3d feet, float facing)
    {
        if (mission.Solids is { } solids)
        {
            host.BeginDeathCamera(feet, facing, solids.Caster());
        }
    }

    /// <inheritdoc/>
    public void HearLanding(Vector3d feet, double radiusUnits) => mission.Soldiers?.NotifyGroundImpact(feet, feet, radiusUnits);

    /// <inheritdoc/>
    public void ReviveBody() => mission.SoldierPresenter?.RevivePlayerBody();

    /// <inheritdoc/>
    public void EndDeathCamera() => host.EndDeathCamera();
}
