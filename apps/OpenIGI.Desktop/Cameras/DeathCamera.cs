using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The camera the game hands control to when the player dies — it detaches from the eye, pulls back to a
/// third-person view of the body, and lets the mouse orbit around it: the original's <c>HumanCamera</c>
/// death mode.
/// </summary>
/// <remarks>
/// <para>
/// On death <c>DeathCameraInit</c> (<c>0x4d51d0</c>) and <c>FindStartPosition</c> (<c>0x4d36a0</c>) choose
/// a starting vantage that frames the body against open space — that search is <see cref="DeathCameraVantage"/>.
/// Then every frame <c>DeathCameraControlInput</c> (<c>0x4d53d0</c>) feeds the mouse to
/// <c>UpdateAlphaAndGammaWithCollisions</c> (<c>0x4d5220</c>): the look turns the camera's azimuth freely
/// and its elevation between <c>−71°</c> and <c>+89°</c> — at most six degrees a frame — while the
/// distance eases back out to the resting two and a half metres whenever the way is clear and pulls in
/// when a wall gets between the camera and the body.
/// </para>
/// <para>
/// So the orbit is stateful — an azimuth, an elevation and a distance around a fixed pivot (the body's
/// chest) — which is what this holds. The world is already drawn at the ninety-degree horizontal field of
/// view <c>DeathCameraInit</c> also sets (<see cref="HumanPlayerBody.DeathCameraFieldOfView"/>), so only
/// the eye changes. Read from I.G.I. 2's named camera; see <see cref="DeathCameraVantage"/> for why this
/// is a documented lift from the sequel rather than I.G.I. 1's own (different) death mode.
/// </para>
/// </remarks>
public sealed class DeathCamera
{
    /// <summary>World units in a metre — the game's own scale.</summary>
    public const double UnitsPerMetre = 4096.0;

    // Where on the body the camera aims: a metre above the feet, roughly the chest.
    private const double ChestAboveFeetInUnits = UnitsPerMetre;

    private Camera3D _camera = Camera3D.Default;
    private Vector3d _pivot;
    private double _alpha;      // elevation, radians
    private double _gamma;      // azimuth, radians
    private double _distance;   // engine units
    private DeathCameraVantage.Blocked? _walls;

    /// <summary>Gets a value indicating whether the death camera has taken over.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets or sets how far the mouse orbits — the player's own look settings.</summary>
    /// <remarks>
    /// The same rate the view turned at a moment before, so dying does not change how the mouse feels.
    /// The per-frame six-degree clamp in <see cref="Control"/> is the original's and sits on top of
    /// this; the rate only decides how much a given motion asks for before that clamp.
    /// </remarks>
    public MouseLookSettings LookSettings { get; set; } = MouseLookSettings.Default;

    /// <summary>Gets the eye to draw the world from while the death camera is active.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Takes over the view: searches for a clear vantage on the body and looks at it.</summary>
    /// <param name="bodyFeetUnits">Where the body fell — the player's own position, in engine units.</param>
    /// <param name="gamma">The heading the body faces.</param>
    /// <param name="walls">A world ray test to keep the camera out of geometry, or <see langword="null"/>.</param>
    public void Begin(Vector3d bodyFeetUnits, float gamma, DeathCameraVantage.Blocked? walls = null)
    {
        _pivot = new Vector3d(
            bodyFeetUnits.X, bodyFeetUnits.Y, bodyFeetUnits.Z + ChestAboveFeetInUnits);
        _walls = walls;

        DeathCameraVantage.Vantage vantage = DeathCameraVantage.Find(_pivot, gamma, walls);

        // Recover the elevation and azimuth the chosen direction stands for, so the orbit can carry on
        // from it: direction = (sin(az)cos(el), -cos(az)cos(el), sin(el)).
        _alpha = Math.Asin(Math.Clamp(vantage.Direction.Z, -1.0, 1.0));
        _gamma = Math.Atan2(vantage.Direction.X, -vantage.Direction.Y);
        _distance = vantage.DistanceInUnits;

        BuildCamera();
        IsActive = true;
    }

    /// <summary>Orbits the camera around the body from a frame's mouse motion.</summary>
    /// <param name="mouseDeltaX">The horizontal mouse motion this frame, in pixels.</param>
    /// <param name="mouseDeltaY">The vertical mouse motion this frame, in pixels.</param>
    /// <remarks>
    /// <c>UpdateAlphaAndGammaWithCollisions</c>: the turn is clamped to six degrees a frame, the elevation
    /// held between its limits, the azimuth free, and the distance eased back toward the resting one when
    /// the way is clear or pulled in short of a wall when it is not.
    /// </remarks>
    public void Control(float mouseDeltaX, float mouseDeltaY)
    {
        if (!IsActive)
        {
            return;
        }

        float limit = HumanPlayerBody.DeathCameraOrbitStepLimit;
        double deltaGamma = Math.Clamp(LookSettings.YawDelta(mouseDeltaX), -limit, limit);
        double deltaAlpha = Math.Clamp(LookSettings.PitchDelta(mouseDeltaY), -limit, limit);

        // Mouse up raises the camera, as the original's `alpha -= delta` does. The sign now lives in
        // PitchDelta, which is also where the profile's invert is applied, so this adds.
        _alpha = Math.Clamp(
            _alpha + deltaAlpha,
            HumanPlayerBody.DeathCameraElevationMin,
            HumanPlayerBody.DeathCameraElevationMax);
        _gamma += deltaGamma;

        // Collision: the distance eases back out to the resting one when the ray to the ideal is clear,
        // and snaps in short of whatever blocks it when it is not.
        Vector3d direction = DirectionOf(_alpha, _gamma);
        double resting = HumanPlayerBody.DeathCameraRestingDistanceInUnits;
        double ideal = HumanPlayerBody.DeathCameraDistanceInUnits;
        double ease = HumanPlayerBody.DeathCameraDistanceEaseInUnits;
        double pullIn = HumanPlayerBody.DeathCameraPullInInUnits;

        Vector3d far = new(
            _pivot.X + (direction.X * ideal),
            _pivot.Y + (direction.Y * ideal),
            _pivot.Z + (direction.Z * ideal));

        if (_walls is { } walls && walls(_pivot, far, out Vector3d hit))
        {
            double maxDistance = Math.Max(0.0, Distance(_pivot, hit) - pullIn);
            _distance = Math.Min(_distance, maxDistance);
        }
        else if (_distance < resting)
        {
            _distance = Math.Min(resting, _distance + ease);
        }
        else if (_distance > resting)
        {
            _distance = Math.Max(resting, _distance - ease);
        }

        BuildCamera();
    }

    /// <summary>Hands the view back — what starting a fresh mission does.</summary>
    public void Reset()
    {
        IsActive = false;
        _camera = Camera3D.Default;
        _walls = null;
    }

    private static Vector3d DirectionOf(double alpha, double gamma)
    {
        double ce = Math.Cos(alpha);
        return new Vector3d(Math.Sin(gamma) * ce, -Math.Cos(gamma) * ce, Math.Sin(alpha));
    }

    private void BuildCamera()
    {
        Vector3d direction = DirectionOf(_alpha, _gamma);
        Vector3d eye = new(
            _pivot.X + (direction.X * _distance),
            _pivot.Y + (direction.Y * _distance),
            _pivot.Z + (direction.Z * _distance));

        Vector3d look = new(_pivot.X - eye.X, _pivot.Y - eye.Y, _pivot.Z - eye.Z);
        double lookLength = Math.Sqrt((look.X * look.X) + (look.Y * look.Y) + (look.Z * look.Z));
        float yaw = (float)Math.Atan2(look.X, look.Y);
        float pitch = lookLength > 0.0 ? (float)Math.Asin(look.Z / lookLength) : 0f;

        Vector3d eyeMetres = new(eye.X / UnitsPerMetre, eye.Y / UnitsPerMetre, eye.Z / UnitsPerMetre);
        _camera = new Camera3D(eyeMetres, Camera3D.LookRotation(yaw, pitch));
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
