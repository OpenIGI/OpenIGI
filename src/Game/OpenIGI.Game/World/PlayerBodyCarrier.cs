using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.World;

/// <summary>
/// Carries a <see cref="PlayerBody"/> on what moves under it in a level whose collision another game keeps
/// (<see cref="PlacementCollisionFollower"/>): it rides a lift, a train or a helicopter it stands on and turns with it, and a
/// conveyor belt's moving face pushes it along.
/// </summary>
/// <remarks>
/// The level's own humans are carried by their own game; a body that walks the followed index is not, so whoever stands one
/// there carries it here once a tick, after the index has followed the level.
/// </remarks>
/// <param name="follower">The index kept where the level's objects stand.</param>
/// <param name="pushes">What the level's surfaces push a body standing on them with, or <see langword="null"/> for nothing.</param>
public sealed class PlayerBodyCarrier(PlacementCollisionFollower follower, ISurfacePush? pushes = null)
{
    private readonly PlacementCollisionFollower _follower = follower ?? throw new ArgumentNullException(nameof(follower));
    private int _carrier = -1;

    /// <summary>Carries the body a tick: along with what it rides, then by the push of the face it stands on.</summary>
    /// <param name="body">The body, standing in the world (not on a ladder or in an interaction).</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is <see langword="null"/>.</exception>
    public void Carry(PlayerBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // A lift, a train, a helicopter: the body rides what it stands on as that moves, and turns with it.
        if (_follower.TryCarry(ref _carrier, body.Position, out Vector3d carried, out float turn))
        {
            body.Position = carried;
            body.Yaw += turn;
        }

        // A conveyor belt: standing on its moving face, the body is pushed along it, through the walls as its own step is.
        if (pushes is not null
            && body.IsGrounded
            && _follower.TryStandingOn(body.Position, out WorldPlacement? under, out WorldRayHit footing)
            && pushes.PushOn(under, footing.Material) is { } push)
        {
            body.SlideBy(new Vector3d(push.X, push.Y, push.Z));
        }
    }
}
