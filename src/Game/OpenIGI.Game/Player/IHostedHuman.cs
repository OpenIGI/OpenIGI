using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Player;

/// <summary>
/// A human a level places that another game's body may stand for: the level's own game leaves it to that game, and finds the
/// stand-in where it would find its own body.
/// </summary>
/// <remarks>
/// The level's own game keeps the task, which its scripts and AI name by id. The other game claims it
/// (<see cref="Driver"/>) before the level's humans are given bodies, and stands its own body in for it
/// (<see cref="StandIn"/>), which answers what the level asks of the human and is struck where it stands.
/// </remarks>
public interface IHostedHuman
{
    /// <summary>Gets the human's level id.</summary>
    int Id { get; }

    /// <summary>Gets where the level places the human, in world units.</summary>
    Vector3d Position { get; }

    /// <summary>Gets the way the level faces the human, in radians.</summary>
    float Gamma { get; }

    /// <summary>Gets the human's team.</summary>
    int Team { get; }

    /// <summary>
    /// Gets or sets the game whose body stands for the human, or <see langword="null"/> while the level's own game gives it its own.
    /// </summary>
    GameTitle? Driver { get; set; }

    /// <summary>Gets or sets the other game's body standing for the human, once it stands; what the level's AI and scripts find.</summary>
    IAiHuman? StandIn { get; set; }
}
