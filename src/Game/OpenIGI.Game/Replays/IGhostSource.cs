using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Replays;

/// <summary>
/// A title's running mission as a ghost of its player is taken from it: the player's body as a co-op partner's copy would be
/// sent it (<see cref="ReplicatedHuman"/>), read once a tick as a run is recorded. Read only: taking it changes nothing.
/// </summary>
public interface IGhostSource
{
    /// <summary>The player's body as it stands after the tick, or <see langword="null"/> while there is no player.</summary>
    /// <returns>The body.</returns>
    ReplicatedHuman? GhostFrame();
}
