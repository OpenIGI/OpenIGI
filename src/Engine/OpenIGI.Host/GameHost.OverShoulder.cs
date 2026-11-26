using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Host;

/// <content>
/// The remaster's view over the shoulder of a title's own player (<see cref="ITitleOverShoulder"/>), for a title with no viewpoint
/// of its own: the same camera the first game's viewpoint drives (<see cref="OverShoulderView"/>), off the title's eye. Not retail.
/// </content>
public sealed partial class GameHost
{
    private readonly OverShoulderView _titleOverShoulder = new();

    // What the camera keeps out of, made once for the title player's collision; and the collision it was made for.
    private WorldWalls.Blocked? _titleOverShoulderWalls;
    private ICollisionWorld? _titleOverShoulderWallsFor;

    /// <summary>Gets a value indicating whether the title's first-person hands are drawn: not while the camera is off the eye.</summary>
    private bool TitleShowsViewModel => _titleOverShoulder.ShowsViewModel;

    /// <summary>Puts the camera back at the title player's eye, as a title player is stood up or put away.</summary>
    private void ResetTitleOverShoulder()
    {
        _titleOverShoulder.Reset(_loop.TimeSource.Milliseconds);
        _titleOverShoulderWalls = null;
        _titleOverShoulderWallsFor = null;
    }

    /// <summary>Moves the title player's eye over its shoulder, and tells the player what the move left showing.</summary>
    /// <param name="eye">The title player's own eye.</param>
    /// <param name="hostAllows">Whether nothing the host shows has taken the view.</param>
    /// <returns>The eye to draw from: <paramref name="eye"/> itself while the camera is at it.</returns>
    private Camera3D ComposeTitleOverShoulder(in Camera3D eye, bool hostAllows)
    {
        if (_titleFeatures?.OverShoulder is not { } player)
        {
            return eye;
        }

        Camera3D drawn = _titleOverShoulder.Compose(
            hostAllows && _playerMode && player.AllowsOverShoulder,
            GameplayCVars.ThirdPerson.Value && !player.HasOpticUp,
            eye,
            _loop.TimeSource.Milliseconds,
            _menuFlow.MenuMode,
            TitleOverShoulderWalls());
        player.PresentOverShoulder(_titleOverShoulder.ShowsBody);
        return drawn;
    }

    // The title's walls and ground, without its people, which the title's eye collision already leaves its own player out of.
    private WorldWalls.Blocked? TitleOverShoulderWalls()
    {
        ICollisionWorld? collision = TitlePlayer?.Collision;
        if (!ReferenceEquals(collision, _titleOverShoulderWallsFor))
        {
            _titleOverShoulderWallsFor = collision;
            _titleOverShoulderWalls = collision is null ? null : Blocked(collision);
        }

        return _titleOverShoulderWalls;

        static WorldWalls.Blocked Blocked(ICollisionWorld world) => (Vector3d from, Vector3d to, out Vector3d hitUnits) =>
        {
            bool met = world.Cast(from, to, out WorldRayHit hit);
            hitUnits = hit.Position;
            return met;
        };
    }
}
