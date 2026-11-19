using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Something another game's level keeps that a human works by hand, behind a <see cref="WorldInteraction"/>: the level it
/// belongs to says whether it is offered and where, and is told as it is pressed, opened, worked and finished.
/// </summary>
/// <remarks>
/// A <see cref="WorldInteraction"/> is what Project I.G.I.'s humans activate. In its own levels the interaction holds the
/// task's state itself; standing for another game's door, switch or terminal it holds none, and asks this instead, so the
/// thing keeps its own rules and its level's scripts read it as they always do.
/// </remarks>
public interface IHostedInteraction
{
    /// <summary>Gets the point the thing was last offered at, in world units: where its marker goes and a human reaches for.</summary>
    Vector3d Anchor { get; }

    /// <summary>Gets a value indicating whether the thing is in its level's world.</summary>
    bool IsInWorld { get; }

    /// <summary>Gets a value indicating whether it is a door that stands locked.</summary>
    bool IsLocked { get; }

    /// <summary>Gets a value indicating whether it is a locked door a human may pick.</summary>
    bool IsPickable { get; }

    /// <summary>Asks the thing whether it offers itself to a human looking from a point, by its own level's rule.</summary>
    /// <param name="eye">Where the human looks from, in world units.</param>
    /// <returns><see langword="true"/> when it does; <see cref="Anchor"/> is then where.</returns>
    bool Offer(Vector3d eye);

    /// <summary>A human presses it: a switch.</summary>
    void Press();

    /// <summary>A human opens or closes it: a door that is not locked.</summary>
    /// <param name="from">Where the human stands, in world units, which says which way it swings.</param>
    void Toggle(Vector3d from);

    /// <summary>A human sets to work on it: a lock, a terminal, a cabinet, a timed action.</summary>
    void Begin();

    /// <summary>A human works on it this tick.</summary>
    /// <param name="ticks">The ticks the human has worked on it so far.</param>
    void Use(int ticks);

    /// <summary>The human's work on it is done: the lock picked, the terminal hacked, the cabinet searched, the action finished.</summary>
    void Complete();

    /// <summary>The human lets go before the work is done.</summary>
    void Cancel();
}
