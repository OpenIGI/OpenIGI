using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Resolves a retail Train's scalar rail position onto its SplineObj waypoints.</summary>
internal sealed class RailroadPath
{
    private readonly Vector3d[] _points;
    private readonly Vector3f[] _tangents;
    private readonly SplineSample[] _samples;

    private RailroadPath(
        Vector3d[] points,
        Vector3f[] tangents,
        bool flipped,
        float initialPosition,
        float offsetX,
        float offsetY)
    {
        _points = points;
        _tangents = tangents;
        Flipped = flipped;
        InitialPosition = initialPosition;
        OffsetX = offsetX;
        OffsetY = offsetY;
        _samples = BuildSamples();
    }

    public bool Flipped { get; }
    public float InitialPosition { get; }
    public float OffsetX { get; }
    public float OffsetY { get; }

    public static RailroadPath? FromTrain(DeclaredTask train)
    {
        if (!train.TryGetValue("RailroadQTaskID", out object? rawId) || rawId is not int railId
            || train.Tree.FromId(railId) is not DeclaredTask rail)
        {
            return null;
        }

        List<Vector3d> points = [];
        List<Vector3f> tangents = [];
        foreach (GameTask child in rail.Children)
        {
            if (child is DeclaredTask waypoint
                && waypoint.TryGetValue("Position", out object? rawPosition)
                && rawPosition is Vector3d position)
            {
                points.Add(position);
                Vector3f angles = waypoint.TryGetValue("Orientation", out object? rawOrientation)
                    && rawOrientation is Vector3f orientation
                        ? orientation
                        : default;
                // sub_4E4530 reads elements [0,3,6]: the local-X column of the waypoint frame.
                tangents.Add(Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z).Column1);
            }
        }

        if (points.Count < 2)
        {
            return null;
        }

        return new RailroadPath(
            [.. points],
            [.. tangents],
            train.TryGetValue("Flip Direction", out object? rawFlip) && rawFlip is bool flip && flip,
            train.TryGetValue("Position", out object? rawTrackPosition) && rawTrackPosition is float trackPosition ? trackPosition : 0f,
            train.TryGetValue("Displacement X", out object? rawX) && rawX is float x ? x : 0f,
            train.TryGetValue("Displacement Y", out object? rawY) && rawY is float y ? y : 0f);
    }

    public void Evaluate(float scalarPosition, out Vector3d position, out Matrix3f orientation)
    {
        double total = _samples[^1].Distance;
        // The spline scalar accepts the retail engine's signed end-relative convention: positive
        // values measure from the first knot, negative values measure backwards from the last.  A
        // Mission 1 train starts at -971400, which is near the far end of its 4.7-million-unit rail;
        // negating that value placed the whole consist roughly 500 metres away from its cameras.
        double distance = scalarPosition < 0f ? total + scalarPosition : scalarPosition;
        distance = Math.Clamp(distance, 0.0, total);

        int upper = Array.BinarySearch(
            _samples,
            new SplineSample(distance, 0, 0d),
            ByDistance);
        if (upper < 0)
        {
            upper = ~upper;
        }
        upper = Math.Clamp(upper, 1, _samples.Length - 1);

        SplineSample before = _samples[upper - 1];
        SplineSample after = _samples[upper];
        int segment = after.Segment;
        double span = after.Distance - before.Distance;
        double amount = span > 0d ? (distance - before.Distance) / span : 0d;
        double t = before.Segment == segment
            ? before.T + ((after.T - before.T) * amount)
            : after.T * amount;
        EvaluateHermite(segment, t, out position, out Vector3d derivative);

        Vector3d forward = Flipped ? -derivative : derivative;
        double forwardLength = forward.Length;
        if (forwardLength <= 1e-9)
        {
            forward = Vector3d.UnitX;
            forwardLength = 1d;
        }
        forward /= forwardLength;

        // The curve frame's first column is local X down the rail, its second is the level side
        // axis, and its third completes the zero-roll frame, matching sub_4E4530/sub_4FEC10.
        Vector3d side = new(-forward.Y, forward.X, 0d);
        double sideLength = side.Length;
        side = sideLength > 1e-9 ? side / sideLength : Vector3d.UnitY;
        Vector3d up = Vector3d.Cross(forward, side);
        orientation = new Matrix3f(
            (float)forward.X, (float)side.X, (float)up.X,
            (float)forward.Y, (float)side.Y, (float)up.Y,
            (float)forward.Z, (float)side.Z, (float)up.Z);

        Vector3f right = orientation.Column1;
        Vector3f ahead = orientation.Column2;
        position += new Vector3d(
            (right.X * OffsetX) + (ahead.X * OffsetY),
            (right.Y * OffsetX) + (ahead.Y * OffsetY),
            (right.Z * OffsetX) + (ahead.Z * OffsetY));
    }

    private SplineSample[] BuildSamples()
    {
        const int AreasPerSegment = 20;
        List<SplineSample> samples = [new(0d, 0, 0d)];
        Vector3d previous = _points[0];
        double distance = 0d;
        for (int segment = 0; segment + 1 < _points.Length; segment++)
        {
            for (int area = 1; area <= AreasPerSegment; area++)
            {
                double t = area / (double)AreasPerSegment;
                EvaluateHermite(segment, t, out Vector3d point, out _);
                distance += (point - previous).Length;
                samples.Add(new SplineSample(distance, segment, t));
                previous = point;
            }
        }
        return [.. samples];
    }

    private void EvaluateHermite(int segment, double t, out Vector3d position, out Vector3d derivative)
    {
        Vector3d from = _points[segment];
        Vector3d to = _points[segment + 1];
        Vector3d chordVector = to - from;
        double chord = chordVector.Length;
        Vector3d tangent0 = new(_tangents[segment].X, _tangents[segment].Y, _tangents[segment].Z);
        Vector3d tangent1 = new(_tangents[segment + 1].X, _tangents[segment + 1].Y, _tangents[segment + 1].Z);
        tangent0 *= chord;
        tangent1 *= chord;

        double t2 = t * t;
        double t3 = t2 * t;
        double h00 = (2d * t3) - (3d * t2) + 1d;
        double h10 = t3 - (2d * t2) + t;
        double h01 = (-2d * t3) + (3d * t2);
        double h11 = t3 - t2;
        position = (from * h00) + (tangent0 * h10) + (to * h01) + (tangent1 * h11);

        double d00 = (6d * t2) - (6d * t);
        double d10 = (3d * t2) - (4d * t) + 1d;
        double d01 = (6d * t) - (6d * t2);
        double d11 = (3d * t2) - (2d * t);
        derivative = (from * d00) + (tangent0 * d10) + (to * d01) + (tangent1 * d11);
    }

    private readonly record struct SplineSample(double Distance, int Segment, double T);

    // The samples are in distance order, and searched by it.
    private static readonly Comparer<SplineSample> ByDistance = Comparer<SplineSample>.Create(
        static (x, y) => x.Distance.CompareTo(y.Distance));
}
