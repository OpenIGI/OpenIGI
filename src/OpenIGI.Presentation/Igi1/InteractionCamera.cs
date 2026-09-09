using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// The third-person view the player is shown from while on a ladder — the original's camera mode 3
/// (interaction). A mouse-orbitable camera around the climbing body, following it up and down.
/// </summary>
/// <remarks>
/// <para>
/// Transcribed from IGI1's mode-3 functions <c>0x483360</c>, <c>0x483390</c> and <c>0x4834D0</c>.
/// The eye begins 0.25 m from the body, eases toward 2.5 m over 27 ticks, orbits within ±75 degrees,
/// and pulls 0.05 m short of blocking geometry. The pivot follows the authored ladder body.
/// </para>
/// </remarks>
public sealed class InteractionCamera
{
    /// <summary>World units in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Where on the body the camera aims.</summary>
    private const double ChestAboveFeetInUnits = UnitsPerMetre;

    /// <summary>The mode-3 initial distance, 1024 world units (0.25 m).</summary>
    public const double InitialDistanceInUnits = 1024.0;

    /// <summary>The mode-3 ideal distance, 10240 world units (2.5 m).</summary>
    public const double IdealDistanceInUnits = 10240.0;

    /// <summary>The fixed-tick distance adjustment in <c>sub_483390</c>.</summary>
    public const double DistanceEaseInUnits = 341.33334;

    /// <summary>How far the collision result pulls the eye away from a wall.</summary>
    public const double CollisionPullInUnits = 204.8;

    /// <summary>The mode-3 elevation limit, 75 degrees.</summary>
    public const double ElevationLimit = 1.3089969;

    private Camera3D _camera = Camera3D.Default;
    private Vector3d _pivot;
    private double _alpha; // elevation, radians
    private double _gamma; // azimuth, radians
    private double _distance = InitialDistanceInUnits;
    private DeathCameraVantage.Blocked? _walls;

    // The tick pair the presentation blends across. The pivot follows the climbing body and the
    // distance eases out over the mount, and both of those advance on the 30 Hz simulation tick --
    // while the body they are watching is drawn interpolated between its own tick pair. Without a
    // matching pair here the camera steps thirty times a second against a subject moving smoothly,
    // which is exactly what a jittery climb looks like.
    private Vector3d _pivotPrevTick;
    private double _distancePrevTick = InitialDistanceInUnits;
    private bool _hasTickPair;
    private float _presentedAlpha = 1f;

    /// <summary>Gets whether the interaction camera is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets or sets how far the mouse orbits — the player's own look settings.</summary>
    /// <remarks>Climbing a ladder should not change how the mouse feels, so this is the same rate the
    /// first-person view turns at, taken from the active profile.</remarks>
    public MouseLookSettings LookSettings { get; set; } = MouseLookSettings.Default;

    /// <summary>Gets the eye to draw the world from.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Takes over the view: places the camera behind the climbing body.</summary>
    /// <param name="bodyFeetUnits">The body's feet on the ladder, in world units.</param>
    /// <param name="facing">The yaw the body faces — into the ladder.</param>
    /// <param name="walls">A world ray test for the mode-3 collision pull-in.</param>
    public void Begin(
        Vector3d bodyFeetUnits,
        float facing,
        DeathCameraVantage.Blocked? walls = null)
    {
        _gamma = facing;
        _alpha = 0.0;
        _distance = InitialDistanceInUnits;
        _distancePrevTick = InitialDistanceInUnits;
        _walls = walls;

        // Mounting a ladder is a discontinuity: the camera cuts from the first-person eye to a pose
        // behind the body, and there is no previous tick worth blending from.
        _hasTickPair = false;
        Follow(bodyFeetUnits);
        IsActive = true;
    }

    /// <summary>Eases the camera's distance once on the fixed 30 Hz simulation tick.</summary>
    public void Tick()
    {
        if (!IsActive)
        {
            return;
        }

        _distancePrevTick = _distance;
        _distance += Math.Clamp(
            IdealDistanceInUnits - _distance,
            -DistanceEaseInUnits,
            DistanceEaseInUnits);
        BuildCamera(_pivot, _distance, commit: true);
    }

    /// <summary>Moves the pivot to follow the body as it climbs, keeping the current orbit.</summary>
    /// <param name="bodyFeetUnits">Where the body is now, in world units.</param>
    /// <remarks>Called on the simulation tick; the frames between it are covered by <see cref="Present"/>.</remarks>
    public void Follow(Vector3d bodyFeetUnits)
    {
        Vector3d pivot = new(bodyFeetUnits.X, bodyFeetUnits.Y, bodyFeetUnits.Z + ChestAboveFeetInUnits);
        _pivotPrevTick = _hasTickPair ? _pivot : pivot;
        _pivot = pivot;
        _hasTickPair = true;
        BuildCamera(_pivot, _distance, commit: true);
    }

    /// <summary>Places the camera for a frame drawn between two simulation ticks.</summary>
    /// <param name="alpha">How far through the tick this frame is, 0 at the last one and 1 at the next.</param>
    /// <remarks>
    /// <para>
    /// The half of the smoothing the orbit could not do for itself. <see cref="Control"/> already runs
    /// per frame, so the <em>angles</em> were always smooth; the pivot and the distance advance on the
    /// tick, so without this the camera's position stepped at 30 Hz however fast the display ran.
    /// </para>
    /// <para>
    /// Presentation only. The interpolated pose is never written back into the tick state — see the
    /// <c>commit</c> argument of <see cref="BuildCamera"/> — so the simulation still sees exactly the
    /// distances and pull-ins the fixed tick produced, and a frame rate cannot change where the camera
    /// ends up.
    /// </para>
    /// </remarks>
    public void Present(float alpha)
    {
        if (!IsActive)
        {
            return;
        }

        _presentedAlpha = Math.Clamp(alpha, 0f, 1f);
        if (!_hasTickPair)
        {
            BuildCamera(_pivot, _distance, commit: false);
            return;
        }

        BuildCamera(
            PresentationBlend.Lerp(_pivotPrevTick, _pivot, _presentedAlpha),
            _distancePrevTick + ((_distance - _distancePrevTick) * _presentedAlpha),
            commit: false);
    }

    /// <summary>Orbits the camera around the body from a frame's mouse motion.</summary>
    /// <param name="mouseDeltaX">The horizontal mouse motion this frame, in pixels.</param>
    /// <param name="mouseDeltaY">The vertical mouse motion this frame, in pixels.</param>
    public void Control(float mouseDeltaX, float mouseDeltaY)
    {
        if (!IsActive)
        {
            return;
        }

        _gamma += LookSettings.YawDelta(mouseDeltaX);

        // Mouse up raises the camera; the sign, and the profile's invert, live in PitchDelta.
        _alpha = Math.Clamp(_alpha + LookSettings.PitchDelta(mouseDeltaY), -ElevationLimit, ElevationLimit);

        // Rebuilt where the last frame put it, not where the last tick did, so an orbit made mid-tick
        // does not snap the camera back to the tick's pivot for the rest of the frame.
        Present(_presentedAlpha);
    }

    /// <summary>Hands the view back — the player has left the ladder.</summary>
    public void Reset()
    {
        IsActive = false;
        _camera = Camera3D.Default;
        _walls = null;
        _hasTickPair = false;
        _presentedAlpha = 1f;
    }

    private static Vector3d DirectionOf(double alpha, double gamma)
    {
        double ce = Math.Cos(alpha);
        return new Vector3d(Math.Sin(gamma) * ce, -Math.Cos(gamma) * ce, Math.Sin(alpha));
    }

    /// <summary>Places the eye for a pivot and a distance.</summary>
    /// <param name="pivot">Where the camera is looking, in world units.</param>
    /// <param name="distance">How far back from it the eye sits, in world units.</param>
    /// <param name="commit">
    /// Whether a collision pull-in may be kept. True on the simulation tick, where the shortened
    /// distance is state the next tick eases out from — that is <c>sub_483390</c>'s own behaviour, and it
    /// is what makes the camera stay close after squeezing past a wall rather than snapping back out.
    /// False for a presented frame: writing an interpolated pull-in back into the tick state would let
    /// the frame rate decide how close the camera ends up.
    /// </param>
    private void BuildCamera(Vector3d pivot, double distance, bool commit)
    {
        Vector3d direction = DirectionOf(_alpha, _gamma);
        Vector3d eye = new(
            pivot.X + (direction.X * distance),
            pivot.Y + (direction.Y * distance),
            pivot.Z + (direction.Z * distance));

        if (_walls is { } walls && walls(pivot, eye, out Vector3d hit))
        {
            double blockedDistance = Math.Max(0.0, Distance(pivot, hit) - CollisionPullInUnits);
            distance = Math.Min(distance, blockedDistance);
            if (commit)
            {
                _distance = distance;
            }

            eye = new Vector3d(
                pivot.X + (direction.X * distance),
                pivot.Y + (direction.Y * distance),
                pivot.Z + (direction.Z * distance));
        }

        Vector3d look = new(pivot.X - eye.X, pivot.Y - eye.Y, pivot.Z - eye.Z);
        double lookLength = Math.Sqrt((look.X * look.X) + (look.Y * look.Y) + (look.Z * look.Z));
        float yaw = (float)Math.Atan2(look.X, look.Y);
        float pitch = lookLength > 0.0 ? (float)Math.Asin(look.Z / lookLength) : 0f;

        Vector3d eyeMetres = new(eye.X / UnitsPerMetre, eye.Y / UnitsPerMetre, eye.Z / UnitsPerMetre);
        _camera = new Camera3D(eyeMetres, Camera3D.LookRotation(yaw, pitch));
    }

    private static double Distance(Vector3d left, Vector3d right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        double z = left.Z - right.Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}
