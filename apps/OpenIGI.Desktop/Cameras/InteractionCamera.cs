using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The third-person view the player is shown from while on a ladder — the original's camera mode 3
/// (interaction). A mouse-orbitable camera around the climbing body, following it up and down.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="DeathCamera"/>, which is the same idea (a stateful azimuth / elevation /
/// distance orbit around a pivot, mouse-controlled) already read out of I.G.I. 2's named camera. The
/// difference is that the pivot <b>follows</b> the body as it climbs rather than resting on a corpse, and
/// the elevation is free (<c>HumanCamera_ThirdPersonControlInput</c> clamps it to ±1.5533 rad). The body
/// facing convention is the shared one: a body facing <c>g</c> looks along <c>(−sin g, cos g)</c>, so the
/// camera behind it sits along <c>(sin g, −cos g)</c>.
/// </para>
/// </remarks>
public sealed class InteractionCamera
{
    /// <summary>World units in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>Where on the body the camera aims — a metre above the feet, roughly the chest.</summary>
    private const double ChestAboveFeetInUnits = 1.2 * UnitsPerMetre;

    /// <summary>The resting distance from the body, in world units — about 2.5 m.</summary>
    private const double RestingDistanceInUnits = 2.5 * UnitsPerMetre;

    /// <summary>The starting elevation above the body, in radians — a gentle look down.</summary>
    private const double StartElevation = 0.25;

    /// <summary>Radians of orbit per pixel of mouse motion — the player's own look sensitivity.</summary>
    private const float OrbitSensitivity = 0.0035f;

    /// <summary>The elevation limit, in radians (<c>1.5533431</c>, from the third-person control input).</summary>
    private const double ElevationLimit = 1.5533431;

    private Camera3D _camera = Camera3D.Default;
    private Vector3d _pivot;
    private double _alpha; // elevation, radians
    private double _gamma; // azimuth, radians
    private double _distance = RestingDistanceInUnits;

    /// <summary>Gets whether the interaction camera is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the eye to draw the world from.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Takes over the view: places the camera behind the climbing body.</summary>
    /// <param name="bodyFeetUnits">The body's feet on the ladder, in world units.</param>
    /// <param name="facing">The yaw the body faces — into the ladder.</param>
    public void Begin(Vector3d bodyFeetUnits, float facing)
    {
        _gamma = facing;
        _alpha = StartElevation;
        _distance = RestingDistanceInUnits;
        Follow(bodyFeetUnits);
        IsActive = true;
    }

    /// <summary>Moves the pivot to follow the body as it climbs, keeping the current orbit.</summary>
    /// <param name="bodyFeetUnits">Where the body is now, in world units.</param>
    public void Follow(Vector3d bodyFeetUnits)
    {
        _pivot = new Vector3d(bodyFeetUnits.X, bodyFeetUnits.Y, bodyFeetUnits.Z + ChestAboveFeetInUnits);
        BuildCamera();
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

        _gamma += mouseDeltaX * OrbitSensitivity;

        // Mouse up (a negative vertical delta) raises the camera.
        _alpha = Math.Clamp(_alpha - (mouseDeltaY * OrbitSensitivity), -ElevationLimit, ElevationLimit);
        BuildCamera();
    }

    /// <summary>Hands the view back — the player has left the ladder.</summary>
    public void Reset()
    {
        IsActive = false;
        _camera = Camera3D.Default;
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
}
