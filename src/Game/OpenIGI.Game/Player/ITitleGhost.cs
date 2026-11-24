using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Player;

/// <summary>
/// A title player that can show a race's ghost beside itself: a body posed each tick from the runner's track, drawn only and part
/// of nothing in the level. Its models are its <see cref="ITitleWorldModels.GhostModels"/>.
/// </summary>
public interface ITitleGhost
{
    /// <summary>Poses the ghost after a tick: the runner's body as its track has it then.</summary>
    /// <param name="frame">The runner's body, or <see langword="null"/> to take the ghost away.</param>
    void FollowGhost(ReplicatedHuman? frame);
}
