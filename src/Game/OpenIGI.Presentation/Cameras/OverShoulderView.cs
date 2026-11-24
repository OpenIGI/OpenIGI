using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// The remaster's move over the shoulder as a frame drives it, for either game: the player's settings, the wall clock it is timed
/// on, the pause that holds it, the cut that ends it, and the ray the crosshair then sits on. <b>Not retail.</b>
/// </summary>
/// <remarks>
/// The camera itself is <see cref="ThirdPersonCamera"/>; this is what every caller of it would otherwise repeat. Who may be watched
/// from behind is each caller's to decide and hand in as <c>eligible</c>: the first game's viewpoint, or the host for a title's own
/// player (<see cref="ITitleOverShoulder"/>).
/// </remarks>
public sealed class OverShoulderView
{
    private readonly ThirdPersonCamera _camera = new();
    private int _lastMilliseconds;

    /// <summary>Gets a value indicating whether the camera is anywhere other than exactly at the eye.</summary>
    public bool IsEngaged => _camera.IsEngaged;

    /// <summary>Gets a value indicating whether the player's body is drawn this frame.</summary>
    public bool ShowsBody => _camera.ShowsBody;

    /// <summary>Gets a value indicating whether the first-person arms and weapon are drawn this frame.</summary>
    public bool ShowsViewModel => _camera.ShowsViewModel;

    /// <summary>
    /// Gets the ray the crosshair sits on, from the camera last composed, in world units; <see langword="null"/> while the camera is
    /// at the eye, which is what leaves first person firing along the body's forward with no branch anywhere to say so.
    /// </summary>
    public (Vector3d OriginUnits, Vector3f Direction)? AimRay { get; private set; }

    /// <summary>Puts the camera back at the eye at once, as a view taken by something else does.</summary>
    /// <param name="nowMilliseconds">The loop's clock, which the next move is timed from.</param>
    public void Reset(int nowMilliseconds)
    {
        _camera.Reset();
        _lastMilliseconds = nowMilliseconds;
        AimRay = null;
    }

    /// <summary>Places the frame's camera off the eye it would have had.</summary>
    /// <param name="eligible">
    /// Whether the player may be watched from behind at all this frame. When not, the camera is cut back to the eye, not moved:
    /// what took the view put the camera somewhere else entirely, and a move back from there is a line between unrelated places.
    /// </param>
    /// <param name="wanted">Whether the move is asked to go out rather than come back: the setting, less an optic the eye has up.</param>
    /// <param name="eye">The eye the frame would have been drawn from.</param>
    /// <param name="nowMilliseconds">The loop's clock.</param>
    /// <param name="paused">Whether the game is paused, which holds the camera where it is.</param>
    /// <param name="walls">What the camera keeps out of, or <see langword="null"/>.</param>
    /// <returns>The eye to draw from: <paramref name="eye"/> itself while the camera is at it.</returns>
    public Camera3D Compose(bool eligible, bool wanted, in Camera3D eye, int nowMilliseconds, bool paused, WorldWalls.Blocked? walls)
    {
        if (!eligible)
        {
            Reset(nowMilliseconds);
            return eye;
        }

        float elapsed = Math.Clamp((nowMilliseconds - _lastMilliseconds) / 1000f, 0f, 0.1f);
        _lastMilliseconds = nowMilliseconds;

        _camera.DistanceInMetres = GameplayCVars.ThirdPersonDistance.Value;
        _camera.ShoulderInMetres = GameplayCVars.ThirdPersonShoulder.Value;
        _camera.HeightInMetres = GameplayCVars.ThirdPersonHeight.Value;
        _camera.BlendSeconds = GameplayCVars.ThirdPersonBlend.Value;

        // A paused view does not move. Advancing the blend behind the menu would have the camera quietly travel while the player
        // was reading, and arrive somewhere else.
        Camera3D drawn = _camera.Update(paused ? 0f : elapsed, wanted, eye, walls);
        AimRay = _camera.IsEngaged
            ? (new Vector3d(
                drawn.Position.X * ThirdPersonCamera.UnitsPerMetre,
                drawn.Position.Y * ThirdPersonCamera.UnitsPerMetre,
                drawn.Position.Z * ThirdPersonCamera.UnitsPerMetre), drawn.Orientation.Row3)
            : null;
        return drawn;
    }
}
