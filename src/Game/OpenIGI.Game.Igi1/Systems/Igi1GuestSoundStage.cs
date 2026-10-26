using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// Another game's level as Project I.G.I.'s humans' sounds play in it (<see cref="Igi1HumanSounds"/>): the game's sounds through
/// the host's audio, picked from the level's stream and the game's material table.
/// </summary>
/// <param name="audio">Where the sounds play, the game's own definitions heard through the host's device; <see langword="null"/> for silence.</param>
/// <param name="materials">The game's material table, which names the sound a round makes on a surface.</param>
/// <param name="random">The stream the sounds are picked from.</param>
/// <param name="tick">The level's tick.</param>
/// <param name="listener">Where the listener stands, in world units: a sound of the player's own plays there, flat.</param>
public sealed class Igi1GuestSoundStage(ITitleAudio? audio, GameMaterialRegistry materials, IgiRandom random, Func<int> tick, Func<Vector3d> listener)
    : IIgi1SoundStage
{
    /// <inheritdoc/>
    public bool IsHeard => audio is not null;

    /// <inheritdoc/>
    public int Tick => tick();

    /// <inheritdoc/>
    public IgiRandom SoundRandom => random;

    /// <inheritdoc/>
    public GameMaterialRegistry? Materials => materials;

    /// <inheritdoc/>
    public PlayerCamera? Player { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The host's audio plays a definition at its own pitch and priority. A sound with no position is the player's own, played
    /// where the listener stands: no distance, so full gain and no pan, as the game attaches it to the listener.
    /// </remarks>
    public void Play(string definition, Vector3d? positionInUnits, float pitch, int priority) =>
        audio?.Play(definition, positionInUnits ?? listener());
}
