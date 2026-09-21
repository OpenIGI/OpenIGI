using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// The slow orbital camera behind the menu, circling one location at a time and moving on to the next
/// after a while.
/// </summary>
/// <remarks>
/// <para>
/// A circular path around a point is the simplest spline that reads as "cinematic and alive" behind a
/// menu, and it advances on real elapsed time, never a simulation tick, so it drifts smoothly however fast
/// the frame runs. The orbit faces its centre throughout, tilted down by the authored pitch, which is what
/// keeps the level filling the frame while the camera moves.
/// </para>
/// <para>
/// It circles a <em>place</em> rather than the level. Given several places
/// (<see cref="MissionShowcasePoints"/>) it holds each for <see cref="DwellSeconds"/> and then moves to the
/// next, so a mission is shown as a handful of establishing shots instead of one distant survey of the
/// whole map. The move is a cut, not a fly-through: a path between two points a level apart would run
/// through terrain and buildings, where a cut costs nothing and — dissolved into by the caller — reads as
/// deliberate.
/// </para>
/// <para>
/// The cut is not taken by the camera itself. <see cref="IsCutDue"/> says a move is wanted and
/// <see cref="CommitCut"/> performs it, so the caller can hold the outgoing frame first and dissolve the
/// two together. Nothing happens until it does.
/// </para>
/// </remarks>
public sealed class MenuShowcaseCamera
{
    /// <summary>How long each place is held before the camera moves on, in seconds.</summary>
    public const float DwellSeconds = 20f;

    private readonly IReadOnlyList<ShowcasePoint> _points;
    private int _index;
    private double _angle;
    private float _held;

    /// <summary>Initializes a new instance of the <see cref="MenuShowcaseCamera"/> class over several places.</summary>
    /// <param name="points">The places to circle in turn; an empty set falls back to <paramref name="fallbackCentre"/>.</param>
    /// <param name="fallbackCentre">The point to orbit when no places were found.</param>
    /// <param name="fallbackOrbit">The orbit to use for that fallback.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    public MenuShowcaseCamera(
        IReadOnlyList<ShowcasePoint> points,
        Vector3d fallbackCentre,
        MenuCameraSpec fallbackOrbit)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = points.Count > 0 ? points : [new ShowcasePoint(fallbackCentre, fallbackOrbit)];
        _angle = Current.Orbit.StartAngleRadians;
    }

    /// <summary>Gets which place is being circled.</summary>
    public int PointIndex => _index;

    /// <summary>Gets how many places there are to circle.</summary>
    public int PointCount => _points.Count;

    /// <summary>Gets a value indicating whether the current place has been held long enough to move on.</summary>
    /// <remarks>Always <see langword="false"/> when there is only one place; there is nowhere to move to.</remarks>
    public bool IsCutDue => _points.Count > 1 && _held >= DwellSeconds;

    /// <summary>Moves to the next place, restarting its orbit and its dwell.</summary>
    public void CommitCut()
    {
        if (_points.Count <= 1)
        {
            return;
        }

        _index = (_index + 1) % _points.Count;
        _held = 0f;
        _angle = Current.Orbit.StartAngleRadians;
    }

    /// <summary>Advances the orbit and returns the camera for this frame.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last call.</param>
    /// <returns>The camera looking at the current place.</returns>
    public Camera3D Update(float elapsedSeconds)
    {
        ShowcasePoint point = Current;
        MenuCameraSpec spec = point.Orbit;

        // The dwell stops accumulating once a cut is due, so a caller that takes a moment to hold the
        // outgoing frame does not find the next place already part-way through its own turn.
        if (!IsCutDue)
        {
            _held += MathF.Max(0f, elapsedSeconds);
        }

        _angle += spec.SpeedRadiansPerSecond * elapsedSeconds;

        double x = point.Centre.X + (Math.Cos(_angle) * spec.Radius);
        double y = point.Centre.Y + (Math.Sin(_angle) * spec.Radius);
        double z = point.Centre.Z + spec.Height;
        var position = new Vector3d(x, y, z);

        // Face the centre: yaw so the camera's forward (+Y at yaw 0, Z-up) points along the horizontal
        // direction to the centre; pitch down by the height over the horizontal distance.
        double dx = point.Centre.X - x;
        double dy = point.Centre.Y - y;
        float yaw = (float)Math.Atan2(-dx, dy);
        float pitch = (float)-Math.Atan2(spec.Height, Math.Max(1.0, spec.Radius)) + spec.PitchBiasRadians;

        return new Camera3D(position, Camera3D.LookRotation(yaw, pitch));
    }

    private ShowcasePoint Current => _points[Math.Clamp(_index, 0, _points.Count - 1)];
}
