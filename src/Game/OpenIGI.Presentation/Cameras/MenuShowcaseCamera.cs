using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

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
/// <para>
/// A place marked <see cref="MenuCameraSpec.WallAware"/> is inside something: the last mission's hall,
/// which stands in nothing but fog. A fixed circle there swept the camera through the walls and out, and
/// from outside, the level is nothing but haze. So the orbit asks the level's solids how far it can go: a
/// ray from the centre to the ceiling caps the height, and rays from the centre out along the orbit cap
/// the radius, short of the wall by <see cref="WallMargin"/>. It looks a little ahead, so it draws in as a
/// wall approaches rather than snapping in when it arrives, and eases back out once the wall is past.
/// </para>
/// <para>
/// A place with a <see cref="ShowcasePoint.Rail"/> is travelled rather than circled: the camera runs along
/// the path at its pace, looking at the point <see cref="RailLookAheadMetres"/> further on, and the place is
/// done when the path is, rather than after the dwell. The cut at its end goes to the next place, or back to
/// the start of the same rail when it is the only one.
/// </para>
/// </remarks>
public sealed class MenuShowcaseCamera
{
    /// <summary>How long each place is held before the camera moves on, in seconds.</summary>
    public const float DwellSeconds = 20f;

    /// <summary>How far along a rail the camera looks ahead of itself, in metres.</summary>
    public const double RailLookAheadMetres = 8.0;

    /// <summary>The steepest a rail camera looks up or down, in radians: up a ramp, not at the ceiling.</summary>
    private const double RailPitchLimit = 0.3;

    /// <summary>How far a wall-aware orbit keeps from a wall or a ceiling, in metres.</summary>
    public const double WallMargin = 1.5;

    /// <summary>How far round the orbit a wall-aware camera looks for the next wall, in radians.</summary>
    private const double LookAheadRadians = 0.45;

    /// <summary>How many rays the look-ahead casts, the camera's own direction included.</summary>
    private const int LookAheadRays = 4;

    /// <summary>How quickly the radius follows the room: the time it takes to close most of a gap, in seconds.</summary>
    private const double ReachSettleSeconds = 1.2;

    private readonly IReadOnlyList<ShowcasePoint> _points;
    private readonly WorldRayCast? _walls;
    private int _index;
    private double _angle;
    private float _held;

    // The radius a wall-aware orbit is actually flying at, eased towards what the room allows; negative
    // until the first frame at a place has measured it.
    private double _reach = -1.0;

    // How far along the current place's rail the camera has travelled, in metres.
    private double _travelled;

    /// <summary>Initializes a new instance of the <see cref="MenuShowcaseCamera"/> class over several places.</summary>
    /// <param name="points">The places to circle in turn; an empty set falls back to <paramref name="fallbackCentre"/>.</param>
    /// <param name="fallbackCentre">The point to orbit when no places were found.</param>
    /// <param name="fallbackOrbit">The orbit to use for that fallback.</param>
    /// <param name="walls">
    /// The level's solids, in engine units, for the places marked <see cref="MenuCameraSpec.WallAware"/>;
    /// without it every orbit is the plain circle.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    public MenuShowcaseCamera(
        IReadOnlyList<ShowcasePoint> points,
        Vector3d fallbackCentre,
        MenuCameraSpec fallbackOrbit,
        WorldRayCast? walls = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        _walls = walls;
        _points = points.Count > 0 ? points : [new ShowcasePoint(fallbackCentre, fallbackOrbit)];
        _angle = Current.Orbit.StartAngleRadians;
    }

    /// <summary>Gets which place is being circled.</summary>
    public int PointIndex => _index;

    /// <summary>Gets how many places there are to circle.</summary>
    public int PointCount => _points.Count;

    /// <summary>Gets a value indicating whether the current place has been held long enough to move on.</summary>
    /// <remarks>
    /// An orbit is held for <see cref="DwellSeconds"/>, and never left when it is the only place, since there is
    /// nowhere to move to. A rail is held until its end, and then always cut, if only back to its own start.
    /// </remarks>
    public bool IsCutDue => Current.Rail is { } rail
        ? _travelled >= rail.Length
        : _points.Count > 1 && _held >= DwellSeconds;

    /// <summary>Moves to the next place, restarting its orbit and its dwell.</summary>
    public void CommitCut()
    {
        if (_points.Count <= 1 && Current.Rail is null)
        {
            return;
        }

        _index = (_index + 1) % _points.Count;
        _held = 0f;
        _angle = Current.Orbit.StartAngleRadians;
        _reach = -1.0;
        _travelled = 0.0;
    }

    /// <summary>Advances the orbit and returns the camera for this frame.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last call.</param>
    /// <returns>The camera looking at the current place.</returns>
    public Camera3D Update(float elapsedSeconds)
    {
        ShowcasePoint point = Current;
        MenuCameraSpec spec = point.Orbit;
        if (point.Rail is { } rail)
        {
            return Travel(rail, spec, MathF.Max(0f, elapsedSeconds));
        }

        // The dwell stops accumulating once a cut is due, so a caller that takes a moment to hold the
        // outgoing frame does not find the next place already part-way through its own turn.
        if (!IsCutDue)
        {
            _held += MathF.Max(0f, elapsedSeconds);
        }

        _angle += spec.SpeedRadiansPerSecond * elapsedSeconds;

        double radius = spec.Radius;
        double height = spec.Height;
        if (spec.WallAware && _walls is not null)
        {
            (radius, height) = Enclosed(point.Centre, spec, MathF.Max(0f, elapsedSeconds));
        }

        double x = point.Centre.X + (Math.Cos(_angle) * radius);
        double y = point.Centre.Y + (Math.Sin(_angle) * radius);
        double z = point.Centre.Z + height;
        var position = new Vector3d(x, y, z);

        // Face the centre: yaw so the camera's forward (+Y at yaw 0, Z-up) points along the horizontal
        // direction to the centre; pitch down by the height over the horizontal distance.
        double dx = point.Centre.X - x;
        double dy = point.Centre.Y - y;
        float yaw = (float)Math.Atan2(-dx, dy);
        float pitch = (float)-Math.Atan2(height, Math.Max(1.0, radius)) + spec.PitchBiasRadians;

        return new Camera3D(position, Camera3D.LookRotation(yaw, pitch));
    }

    /// <summary>Moves along a rail and returns the camera there, looking ahead along it.</summary>
    private Camera3D Travel(CameraRail rail, MenuCameraSpec spec, float elapsedSeconds)
    {
        // Held at the end once there, like an orbit's dwell, until the caller takes the cut.
        _travelled = Math.Min(rail.Length, _travelled + (rail.Speed * elapsedSeconds));
        Vector3d position = rail.At(_travelled);

        // The look-ahead runs off the end of the path as it nears it, so past the end it follows the last leg.
        Vector3d ahead = rail.At(_travelled + RailLookAheadMetres);
        double overshoot = (_travelled + RailLookAheadMetres) - rail.Length;
        if (overshoot > 0.0)
        {
            Vector3d before = rail.At(rail.Length - 1.0);
            Vector3d end = rail.At(rail.Length);
            Vector3d along = end - before;
            double length = Math.Max(1e-6, along.Length);
            ahead = new Vector3d(
                end.X + (along.X / length * overshoot),
                end.Y + (along.Y / length * overshoot),
                end.Z + (along.Z / length * overshoot));
        }

        double dx = ahead.X - position.X;
        double dy = ahead.Y - position.Y;
        double dz = ahead.Z - position.Z;
        float yaw = (float)Math.Atan2(-dx, dy);
        double flat = Math.Max(0.5, Math.Sqrt((dx * dx) + (dy * dy)));
        float pitch = (float)(Math.Clamp(Math.Atan2(dz, flat), -RailPitchLimit, RailPitchLimit) + spec.PitchBiasRadians);
        return new Camera3D(position, Camera3D.LookRotation(yaw, pitch));
    }

    /// <summary>The radius and height a wall-aware orbit can fly at this frame without leaving its room.</summary>
    private (double Radius, double Height) Enclosed(Vector3d centre, MenuCameraSpec spec, float elapsedSeconds)
    {
        double unit = WorldScene.UnitsPerMetre;
        var origin = new Vector3d(centre.X * unit, centre.Y * unit, centre.Z * unit);

        // The ceiling first: the orbit flies at the height the room has, short of the margin.
        double height = spec.Height;
        if (Free(origin, new Vector3d(0.0, 0.0, 1.0), spec.Height + WallMargin) is { } up)
        {
            height = Math.Max(0.0, up - WallMargin);
        }

        // Then outwards, from the centre at that height, along where the camera is and where it is going.
        var eye = new Vector3d(origin.X, origin.Y, origin.Z + (height * unit));
        double now = spec.Radius;
        double ahead = spec.Radius;
        double direction = spec.SpeedRadiansPerSecond < 0.0 ? -1.0 : 1.0;
        for (int i = 0; i < LookAheadRays; i++)
        {
            double angle = _angle + (direction * LookAheadRadians * i / (LookAheadRays - 1));
            var along = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0.0);
            double allowed = Free(eye, along, spec.Radius + WallMargin) is { } wall
                ? Math.Max(0.0, wall - WallMargin)
                : spec.Radius;
            ahead = Math.Min(ahead, allowed);
            if (i == 0)
            {
                now = allowed;
            }
        }

        // Eased towards what lies ahead, and never further out than the wall beside it now.
        _reach = _reach < 0.0
            ? ahead
            : _reach + ((ahead - _reach) * (1.0 - Math.Exp(-elapsedSeconds / ReachSettleSeconds)));
        _reach = Math.Min(_reach, now);
        return (_reach, height);
    }

    /// <summary>How far a ray runs from a point before it meets a solid, in metres, or null when it meets none.</summary>
    private double? Free(Vector3d originUnits, Vector3d direction, double lengthMetres)
    {
        double length = lengthMetres * WorldScene.UnitsPerMetre;
        var end = new Vector3d(
            originUnits.X + (direction.X * length),
            originUnits.Y + (direction.Y * length),
            originUnits.Z + (direction.Z * length));
        return _walls!(originUnits, end, out WorldRayHit hit) ? hit.Distance / WorldScene.UnitsPerMetre : null;
    }

    private ShowcasePoint Current => _points[Math.Clamp(_index, 0, _points.Count - 1)];
}
