using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>
/// The level's collision as a player's own eye looks across it: every wall, object, body and the ground, but not the player's own
/// body, which a line from the eye starts inside. <b>Not retail</b>: what the host asks of the view, a co-op mark's place.
/// </summary>
/// <param name="player">The player whose eye it is; its world is read at each cast, as a level loaded anew gives it another.</param>
internal sealed class Igi2EyeCollision(Igi2TitlePlayer player) : ICollisionWorld
{
    private readonly Func<object, bool> _notSelf = owner => !ReferenceEquals(owner, player.Human);

    /// <inheritdoc/>
    public IGroundHeightField? Ground => player.CollisionWorld.Ground;

    /// <inheritdoc/>
    public bool Cast(Vector3d origin, Vector3d target, out WorldRayHit hit) =>
        player.CollisionWorld.Cast(origin, target, _notSelf, out hit);
}
