using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation.Cameras;

/// <summary>
/// A path the menu camera travels along at a steady pace: a smooth curve through hand-placed waypoints, for
/// places an orbit cannot show, such as a level that is nothing but tunnels.
/// </summary>
/// <remarks>
/// <para>
/// The curve is a centripetal Catmull-Rom spline. It passes through every waypoint, and unlike the uniform
/// kind it never loops or overshoots where waypoints bunch up at a corner. Waypoints are laid so that the
/// straight line between each pair is clear of walls, and that variant keeps the curve close to those lines.
/// </para>
/// <para>
/// The camera's pace has to be constant in metres, not in waypoints, or it would rush down long stretches
/// and crawl round tight corners. So the curve is measured once into a table of arc lengths and sampled by
/// distance travelled.
/// </para>
/// </remarks>
public sealed class CameraRail
{
    /// <summary>The default pace, in metres a second: an unhurried walk.</summary>
    public const double DefaultSpeed = 2.2;

    /// <summary>How finely each span is measured for the arc-length table.</summary>
    private const int StepsPerSpan = 24;

    private readonly Vector3d[] _points;
    private readonly double[] _lengths;
    private readonly Vector3d[] _samples;

    /// <summary>Initializes a new instance of the <see cref="CameraRail"/> class.</summary>
    /// <param name="waypoints">The places the path runs through, in order, in metres; at least two.</param>
    /// <param name="speed">How fast the camera travels, in metres a second.</param>
    /// <exception cref="ArgumentNullException"><paramref name="waypoints"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Fewer than two waypoints.</exception>
    public CameraRail(IReadOnlyList<Vector3d> waypoints, double speed = DefaultSpeed)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        if (waypoints.Count < 2)
        {
            throw new ArgumentException("A rail needs at least two waypoints.", nameof(waypoints));
        }

        _points = [.. waypoints];
        Speed = speed > 0.0 ? speed : DefaultSpeed;

        int spans = _points.Length - 1;
        _samples = new Vector3d[(spans * StepsPerSpan) + 1];
        _lengths = new double[_samples.Length];
        for (int span = 0; span < spans; span++)
        {
            for (int step = 0; step < StepsPerSpan; step++)
            {
                _samples[(span * StepsPerSpan) + step] = Evaluate(span, (double)step / StepsPerSpan);
            }
        }

        _samples[^1] = _points[^1];
        for (int i = 1; i < _samples.Length; i++)
        {
            _lengths[i] = _lengths[i - 1] + (_samples[i] - _samples[i - 1]).Length;
        }
    }

    /// <summary>Gets the waypoints the path runs through.</summary>
    public IReadOnlyList<Vector3d> Waypoints => _points;

    /// <summary>Gets how fast the camera travels, in metres a second.</summary>
    public double Speed { get; }

    /// <summary>Gets the path's length, in metres.</summary>
    public double Length => _lengths[^1];

    /// <summary>Gets the point a given distance along the path, clamped to its ends.</summary>
    /// <param name="distance">How far along, in metres.</param>
    /// <returns>The point, in metres.</returns>
    public Vector3d At(double distance)
    {
        if (distance <= 0.0)
        {
            return _samples[0];
        }

        if (distance >= Length)
        {
            return _samples[^1];
        }

        int index = Array.BinarySearch(_lengths, distance);
        if (index >= 0)
        {
            return _samples[index];
        }

        int upper = ~index;
        int lower = upper - 1;
        double span = _lengths[upper] - _lengths[lower];
        double t = span > 1e-9 ? (distance - _lengths[lower]) / span : 0.0;
        Vector3d a = _samples[lower];
        Vector3d b = _samples[upper];
        return new Vector3d(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));
    }

    /// <summary>One point of the centripetal Catmull-Rom span between waypoints <paramref name="span"/> and the next.</summary>
    private Vector3d Evaluate(int span, double t)
    {
        Vector3d p1 = _points[span];
        Vector3d p2 = _points[span + 1];

        // The ends are continued in a straight line, so the path starts and stops along its first and last legs.
        Vector3d p0 = span > 0 ? _points[span - 1] : p1 + (p1 - p2);
        Vector3d p3 = span + 2 < _points.Length ? _points[span + 2] : p2 + (p2 - p1);

        // Knots spaced by the square root of the distance between points: the centripetal parameterisation.
        double t0 = 0.0;
        double t1 = t0 + Knot(p0, p1);
        double t2 = t1 + Knot(p1, p2);
        double t3 = t2 + Knot(p2, p3);
        double u = t1 + ((t2 - t1) * t);

        Vector3d a1 = Lerp(p0, p1, t0, t1, u);
        Vector3d a2 = Lerp(p1, p2, t1, t2, u);
        Vector3d a3 = Lerp(p2, p3, t2, t3, u);
        Vector3d b1 = Lerp(a1, a2, t0, t2, u);
        Vector3d b2 = Lerp(a2, a3, t1, t3, u);
        return Lerp(b1, b2, t1, t2, u);

        static double Knot(Vector3d a, Vector3d b) => Math.Max(1e-4, Math.Sqrt((b - a).Length));

        static Vector3d Lerp(Vector3d a, Vector3d b, double ta, double tb, double u)
        {
            double w = (u - ta) / (tb - ta);
            return new Vector3d(a.X + ((b.X - a.X) * w), a.Y + ((b.Y - a.Y) * w), a.Z + ((b.Z - a.Z) * w));
        }
    }
}
