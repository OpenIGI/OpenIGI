using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The slow orbital camera that circles the showcase level behind the main menu.
/// </summary>
/// <remarks>
/// <para>
/// A circular path around a point is the simplest spline that reads as "cinematic and alive" behind a
/// menu, and it is authored the way the R11 note asks — the numbers (radius, height, speed, pitch)
/// live in <c>Assets/menu-cameras.json</c> and were arrived at by flying the free camera with
/// <c>-camlog</c> and copying values down. It advances on real elapsed time, never a simulation tick,
/// so it drifts smoothly however fast the frame runs.
/// </para>
/// <para>
/// The orbit faces its centre throughout, tilted down by the authored pitch, which is what keeps the
/// level filling the frame while the camera moves.
/// </para>
/// </remarks>
public sealed class MenuShowcaseCamera
{
    private readonly Vector3d _centre;
    private readonly MenuCameraSpec _spec;
    private double _angle;

    /// <summary>Initializes a new instance of the <see cref="MenuShowcaseCamera"/> class.</summary>
    /// <param name="centre">The world point to orbit, in metres.</param>
    /// <param name="spec">The orbit parameters.</param>
    public MenuShowcaseCamera(Vector3d centre, MenuCameraSpec spec)
    {
        _centre = centre;
        _spec = spec;
        _angle = spec.StartAngleRadians;
    }

    /// <summary>Advances the orbit and returns the camera for this frame.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last call.</param>
    /// <returns>The camera looking at the orbit's centre.</returns>
    public Camera3D Update(float elapsedSeconds)
    {
        _angle += _spec.SpeedRadiansPerSecond * elapsedSeconds;

        double x = _centre.X + (Math.Cos(_angle) * _spec.Radius);
        double y = _centre.Y + (Math.Sin(_angle) * _spec.Radius);
        double z = _centre.Z + _spec.Height;
        var position = new Vector3d(x, y, z);

        // Face the centre: yaw so the camera's forward (+Y at yaw 0, Z-up) points along the horizontal
        // direction to the centre; pitch down by the height over the horizontal distance.
        double dx = _centre.X - x;
        double dy = _centre.Y - y;
        float yaw = (float)Math.Atan2(-dx, dy);
        float pitch = (float)-Math.Atan2(_spec.Height, Math.Max(1.0, _spec.Radius)) + _spec.PitchBiasRadians;

        return new Camera3D(position, Camera3D.LookRotation(yaw, pitch));
    }
}
