using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// What Project I.G.I.'s player is hurt and dies in where it stands in another game's level: its cries and its rounds' impacts
/// as the game's own (<see cref="Igi1HumanSounds"/>), heard through the host's audio; what only the host can do -- hear it
/// land, pull the camera back from it -- the host's to fill in.
/// </summary>
/// <param name="stage">Where its sounds play.</param>
/// <param name="logger">Where its harm is reported.</param>
/// <remarks>
/// It holds no voice of its own -- the binoculars' and the ladder's are the game's mission audio's -- so its death silences
/// nothing; and it has no third-person body for the level to throw down.
/// </remarks>
public sealed class Igi1GuestPlayerSurroundings(Igi1GuestSoundStage stage, IOpenIgiLogger logger) : IIgi1PlayerSurroundings
{
    /// <summary>Gets where the player's sounds play.</summary>
    public Igi1GuestSoundStage Stage => stage;

    /// <summary>Gets the player's sounds: its cries, and its rounds' impacts, played each tick (<see cref="Igi1HumanSounds.PlayPlayerImpacts"/>).</summary>
    public Igi1HumanSounds Sounds { get; } = new(stage);

    /// <summary>Gets or sets what hears the player land, at its feet and as far as it carries; <see langword="null"/> for no one.</summary>
    public Action<Vector3d, double>? Heard { get; set; }

    /// <summary>Gets or sets what pulls the camera back from the player's body as it falls; <see langword="null"/> to leave the view.</summary>
    public Action<Vector3d, float>? DeathCamera { get; set; }

    /// <inheritdoc/>
    public int Tick => stage.Tick;

    /// <inheritdoc/>
    public IgiRandom SoundRandom => stage.SoundRandom;

    /// <inheritdoc/>
    public Random ShotRandom { get; init; } = new();

    /// <inheritdoc/>
    public IOpenIgiLogger Logger => logger;

    /// <inheritdoc/>
    public void PlayHit(string prefix, int variants) => Sounds.PlayHit(prefix, variants, null);

    /// <inheritdoc/>
    public void PlayDeath(string prefix, int variants) => Sounds.PlayDeath(prefix, variants, null);

    /// <inheritdoc/>
    public void PlayDefinition(string name, int priority) => stage.Play(name, null, 1f, priority);

    /// <inheritdoc/>
    public void PlayBulletImpact(Vector3d position) => Sounds.PlayBulletImpact(position, struckHuman: true, surfaceMaterial: 0);

    /// <inheritdoc/>
    public void PlayLanding((string Sound, Vector3d PositionInUnits) landing) => stage.Play(landing.Sound, landing.PositionInUnits, 1f, priority: 60);

    /// <inheritdoc/>
    public void SilenceForDeath()
    {
    }

    /// <inheritdoc/>
    public void KillBody(int fall, Vector3d feet, float facing, int bone, Vector3d travel, WeaponCategory category, Vector3d velocity)
    {
    }

    /// <inheritdoc/>
    public void BeginDeathCamera(Vector3d feet, float facing) => DeathCamera?.Invoke(feet, facing);

    /// <inheritdoc/>
    public void HearLanding(Vector3d feet, double radiusUnits) => Heard?.Invoke(feet, radiusUnits);

    /// <inheritdoc/>
    /// <remarks>The host draws the guest's body; it has none of its own to stand up.</remarks>
    public void ReviveBody()
    {
    }

    /// <inheritdoc/>
    /// <remarks>The host's player owns the view; there is no camera of the guest's to end.</remarks>
    public void EndDeathCamera()
    {
    }
}
