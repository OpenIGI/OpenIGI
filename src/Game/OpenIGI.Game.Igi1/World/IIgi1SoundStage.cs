using OpenIGI.Core.Numerics;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Where Project I.G.I.'s humans' sounds play and what picks them (<see cref="Igi1HumanSounds"/>): one of the game's own missions,
/// or another game's level its humans stand in.
/// </summary>
public interface IIgi1SoundStage
{
    /// <summary>
    /// Gets a value indicating whether anything is heard: without, no sound is picked, so the sound stream draws only as it
    /// would where one plays.
    /// </summary>
    bool IsHeard { get; }

    /// <summary>Gets the level's tick, which a sound's quiet window is counted in.</summary>
    int Tick { get; }

    /// <summary>Gets the level's sound stream, which picks a variant and the pitch it plays at.</summary>
    IgiRandom SoundRandom { get; }

    /// <summary>Gets the material table that names the sound a round makes on a surface; <see langword="null"/> for none.</summary>
    GameMaterialRegistry? Materials { get; }

    /// <summary>Gets the player whose rounds' impacts are heard; <see langword="null"/> while none stands.</summary>
    PlayerCamera? Player { get; }

    /// <summary>Plays a sound definition.</summary>
    /// <param name="definition">The definition's name.</param>
    /// <param name="positionInUnits">Where, in world units, or <see langword="null"/> at the listener -- the player's own.</param>
    /// <param name="pitch">The pitch, one for the definition's own.</param>
    /// <param name="priority">How much it outranks others when voices run short.</param>
    void Play(string definition, Vector3d? positionInUnits, float pitch, int priority);
}

/// <summary>One of the game's own missions as its humans' sounds play in it: its sound stream and materials, the host's sound output.</summary>
/// <param name="mission">The mission.</param>
/// <param name="host">Where its sounds play.</param>
public sealed class Igi1MissionSoundStage(MissionWorld mission, IMissionSoundHost host) : IIgi1SoundStage
{
    /// <inheritdoc/>
    public bool IsHeard => host.SoundOutput is not null;

    /// <inheritdoc/>
    public int Tick => host.Game.Clock.TickCount;

    /// <inheritdoc/>
    public IgiRandom SoundRandom => mission.SoundRandom;

    /// <inheritdoc/>
    public GameMaterialRegistry? Materials => mission.Materials;

    /// <inheritdoc/>
    public PlayerCamera? Player => mission.Player;

    /// <inheritdoc/>
    public void Play(string definition, Vector3d? positionInUnits, float pitch, int priority) =>
        host.SoundOutput?.PlayDefinition(definition, positionInUnits, priority: priority, pitch: pitch);
}
