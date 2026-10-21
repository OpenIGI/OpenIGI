using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>A retail Train's railroad: the SplineObj's polyline and the coupler walk over it.</summary>
/// <remarks>
/// <para>
/// The original never evaluates the rail's curve while a train runs. Each waypoint's segment keeps the
/// polyline <c>Segment_Build</c> (<c>0x4E4530</c>) sampled from the Hermite between it and the waypoint
/// before (<see cref="RetailSplineSegment.Polyline"/>), and <c>0x4FEC10</c> walks that: the first coupler
/// sits the train's scalar position along it, each later one where a sphere of the car's coupling length
/// about the coupler before crosses it. A car is then laid on the chord between its two couplers
/// (<see cref="PlaceCar"/>), not on the curve.
/// </para>
/// <para>
/// Evaluating the curve itself put every car below the original wherever the rail bends upward: Mission 1's
/// intro wagon 607_01_1 ran 0.6 m low down the hill into the valley, so Jones, welded to it there, sat 0.64 m
/// high inside the crate he leans on once the wagon reached the flat, where the two agree again.
/// </para>
/// </remarks>
internal sealed class RailroadPath
{
    private const float Pi = 3.1415927f;

    private readonly Segment[] _segments;
    private readonly Vector3d _start;
    private readonly float _length;

    private RailroadPath(Segment[] segments, Vector3d start, float length, bool flipped, float initialPosition)
    {
        _segments = segments;
        _start = start;
        _length = length;
        Flipped = flipped;
        InitialPosition = initialPosition;
    }

    /// <summary>Gets whether the train runs flipped (<c>Train+2176</c>): its cars then trail ahead along the rail.</summary>
    public bool Flipped { get; }

    /// <summary>Gets the train's authored scalar position along the rail (<c>Train+1896</c>).</summary>
    public float InitialPosition { get; }

    /// <summary>Reads the rail a lead Train names.</summary>
    /// <param name="train">The lead Train.</param>
    /// <returns>Its railroad, or <see langword="null"/> when the rail is missing or has fewer than two waypoints.</returns>
    public static RailroadPath? FromTrain(DeclaredTask train)
    {
        ArgumentNullException.ThrowIfNull(train);
        if (!train.TryGetValue("RailroadQTaskID", out object? rawId) || rawId is not int railId
            || train.Tree.FromId(railId) is not DeclaredTask rail)
        {
            return null;
        }

        // The waypoint update (0x500EF0) clamps every segment's matrix count to the SplineObj's
        // own and to 20, and lays it straight under Align or the SplineObj's Snap Length.
        int matrices = rail.TryGetValue("Number of Matrices / Segment", out object? rawMatrices) && rawMatrices is int m ? m : 20;
        bool snap = rail.TryGetValue("Snap Length", out object? rawSnap) && rawSnap is true;

        List<Segment> segments = [];
        DeclaredTask? previous = null;
        Vector3d previousPosition = default;
        float length = 0f;
        Vector3d start = default;
        foreach (GameTask child in rail.Children)
        {
            if (child is not DeclaredTask waypoint
                || !waypoint.TryGetValue("Position", out object? rawPosition)
                || rawPosition is not Vector3d position)
            {
                continue;
            }

            if (previous is null)
            {
                start = position;
                previous = waypoint;
                previousPosition = position;
                continue;
            }

            int areas = waypoint.TryGetValue("NumAreas", out object? rawAreas) && rawAreas is int a ? a : 1;
            bool straight = snap || waypoint.TryGetValue("Align", out object? rawAlign) && rawAlign is true;
            int count = straight ? 1 : Math.Max(1, Math.Min(Math.Min(areas, matrices), 20));
            float[] points = RetailSplineSegment.Polyline(
                previousPosition,
                position,
                RetailWorldObjects.WaypointOrientation(previous),
                RetailWorldObjects.WaypointOrientation(waypoint),
                count,
                straight,
                out Vector3d middle,
                out float[] lengths,
                out float total);
            segments.Add(new Segment(middle, points, lengths, count, total));

            // SplineObj+40, the wrap length, is the float sum of every segment's +248 (0x500EF0's tail).
            length = ((X87)total + length).F;
            previous = waypoint;
            previousPosition = position;
        }

        if (segments.Count == 0)
        {
            return null;
        }

        return new RailroadPath(
            [.. segments],
            start,
            length,
            train.TryGetValue("Flip Direction", out object? rawFlip) && rawFlip is bool flip && flip,
            train.TryGetValue("Position", out object? rawTrackPosition) && rawTrackPosition is float trackPosition ? trackPosition : 0f);

    }

    /// <summary>
    /// Walks the couplers of a consist: <c>0x4FEC10</c> with no frames and no offset.
    /// </summary>
    /// <param name="position">The lead's scalar position along the rail.</param>
    /// <param name="steps">Each car's signed coupling length, in order.</param>
    /// <param name="points">Receives one more coupler than there are steps.</param>
    /// <returns>Whether the rail could place them (the original then leaves every car where it was).</returns>
    public bool TryWalk(float position, ReadOnlySpan<float> steps, Span<Vector3d> points)
    {
        if (points.Length < steps.Length + 1)
        {
            throw new ArgumentException("One more point than steps is needed.", nameof(points));
        }

        Span<Vector3d> placed = points[..(steps.Length + 1)];

        // The scalar wraps by the whole rail; a rail of no length, or a position on its very start,
        // puts every coupler on the first waypoint.
        double wrapped = position;
        if (_length != 0f)
        {
            if (wrapped < 0.0)
            {
                do
                {
                    wrapped += _length;
                }
                while (wrapped < 0.0);
                position = (float)wrapped;
            }

            if (wrapped >= _length)
            {
                do
                {
                    wrapped -= _length;
                }
                while (wrapped >= _length);
                position = (float)wrapped;
            }
        }

        if (_length == 0f || wrapped == 0.0)
        {
            placed.Fill(_start);
            return true;
        }

        if (wrapped <= 0.0)
        {
            return false;
        }

        // The segment holding the position: the running total is a float, compared in the register.
        int s = -1;
        float covered = 0f;
        double reached;
        do
        {
            if (++s >= _segments.Length)
            {
                return false;
            }

            reached = (double)covered + _segments[s].Length;
            covered = (float)reached;
        }
        while (reached < position);

        Segment segment = _segments[s];
        double segmentStart = (double)covered - segment.Length;
        if (segmentStart == position)
        {
            placed.Fill(segment.Point(0));
            return true;
        }

        int piece;
        float pieceStart;
        if (segmentStart >= position)
        {
            // The listing would index piece -1 here; only a rounding of the float total reaches it.
            piece = 0;
            pieceStart = (float)segmentStart;
        }
        else
        {
            int next = 0;
            double end = segmentStart;
            while (true)
            {
                if (next >= segment.Count)
                {
                    placed.Fill(segment.Point(segment.Count));
                    return true;
                }

                end += segment.Lengths[next++];
                if (end >= position)
                {
                    break;
                }
            }

            piece = next - 1;
            pieceStart = (float)(end - segment.Lengths[piece]);
        }

        float first = (float)((position - (double)pieceStart) / segment.Lengths[piece]);
        placed[0] = segment.Along(piece, first);

        // v200: a running arc estimate the original never corrects after a chord crossing. A coupler
        // whose sphere leaves the current piece through neither end is placed by it.
        float travelled = position;
        for (int c = 0; c < steps.Length; c++)
        {
            float step = steps[c];
            float reach = (float)((double)step * step);
            travelled = (float)((double)step + travelled);
            Vector3d from = placed[c];
            Vector3d a, b;
            float fraction;
            if (step < 0f)
            {
                if (Vector3d.DistanceSquared(from, segment.Point(piece)) <= reach)
                {
                    float toA;
                    double toB;
                    do
                    {
                        if (piece > 0)
                        {
                            pieceStart = (float)((double)pieceStart - segment.Lengths[--piece]);
                        }
                        else
                        {
                            if (s == 0)
                            {
                                placed[c + 1] = segment.Point(0);
                                goto Next;
                            }

                            segment = _segments[--s];
                            piece = segment.Count - 1;
                            pieceStart = (float)((double)pieceStart - segment.Lengths[piece]);
                        }

                        toA = (float)Vector3d.DistanceSquared(from, segment.Point(piece));
                        toB = Vector3d.DistanceSquared(from, segment.Point(piece + 1));
                    }
                    while (toA > reach == toB > reach);

                    // The crossing by the law of sines in the triangle (coupler, piece end, crossing).
                    float span = -step;
                    float pieceLength = segment.Lengths[piece];
                    float toEnd = (float)Math.Sqrt(toB);
                    double beta = Acos((float)((toA - toB - ((double)pieceLength * pieceLength)) / ((double)toEnd * pieceLength * -2.0)));
                    float sinBeta = (float)Math.Sin(beta);
                    float rest = (float)(Pi - beta);
                    double apex = rest - Asin((float)(sinBeta / span * toEnd));
                    if (apex <= 0.001)
                    {
                        double toStart = Math.Sqrt(toA);
                        fraction = (float)((span - toStart) / (toEnd - toStart));
                    }
                    else
                    {
                        fraction = (float)(1.0 - (Math.Sin(apex) * span / sinBeta / pieceLength));
                    }
                }
                else
                {
                    fraction = (float)((travelled - (double)pieceStart) / segment.Lengths[piece]);
                }
            }
            else if (Vector3d.DistanceSquared(from, segment.Point(piece + 1)) > reach)
            {
                fraction = (float)((travelled - (double)pieceStart) / segment.Lengths[piece]);
            }
            else
            {
                while (true)
                {
                    if (piece >= segment.Count - 1)
                    {
                        if (s + 1 >= _segments.Length)
                        {
                            placed[c + 1] = segment.Point(segment.Count);
                            goto Next;
                        }

                        double carried = (double)pieceStart + segment.Lengths[piece];
                        segment = _segments[++s];
                        piece = 0;
                        pieceStart = (float)carried;
                    }
                    else
                    {
                        pieceStart = (float)((double)pieceStart + segment.Lengths[piece++]);
                    }

                    float toA = (float)Vector3d.DistanceSquared(from, segment.Point(piece));
                    float toB = (float)Vector3d.DistanceSquared(from, segment.Point(piece + 1));
                    if (toA > reach != toB > reach)
                    {
                        float pieceLength = segment.Lengths[piece];
                        float toStart = (float)Math.Sqrt(toA);
                        double alpha = Acos((float)((toB - (double)toA - ((double)pieceLength * pieceLength)) / ((double)toStart * pieceLength * -2.0)));
                        float sinAlpha = (float)Math.Sin(alpha);
                        float rest = (float)(Pi - alpha);
                        double apex = rest - Asin((float)(sinAlpha / step * toStart));
                        fraction = apex <= 0.001
                            ? (float)((step - toStart) / (Math.Sqrt(toB) - toStart))
                            : (float)(Math.Sin(apex) * step / sinAlpha / pieceLength);
                        break;
                    }
                }
            }

            a = segment.Point(piece);
            b = segment.Point(piece + 1);
            placed[c + 1] = new Vector3d(
                ((b.X - a.X) * fraction) + a.X,
                ((b.Y - a.Y) * fraction) + a.Y,
                ((b.Z - a.Z) * fraction) + a.Z);
        Next:;
        }

        return true;
    }

    /// <summary>Lays one car on the chord between its couplers: <c>0x42A2C0</c> for the lead, <c>0x42A7C0</c> behind it.</summary>
    /// <param name="front">The car's leading coupler.</param>
    /// <param name="rear">Its trailing coupler.</param>
    /// <param name="pivot">The model origin's fraction between them (<c>Train+1864</c>).</param>
    /// <param name="displacementX">The car's <c>Displacement X</c> (<c>+1904</c>), in metres.</param>
    /// <param name="displacementY">The car's <c>Displacement Y</c> (<c>+1908</c>), in metres.</param>
    /// <param name="position">Receives the model origin.</param>
    /// <param name="orientation">Receives the car's frame: travel in its first column, up in its third.</param>
    public static void PlaceCar(
        Vector3d front,
        Vector3d rear,
        float pivot,
        float displacementX,
        float displacementY,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (front.X == rear.X && front.Y == rear.Y && front.Z == rear.Z)
        {
            orientation = Matrix3f.Identity;
            position = front;
        }
        else
        {
            // 0x4B4010 frames the chord rear - front as its third row; the caller swaps the first and third
            // rows (negating the chord) and transposes, so the columns are -chord, side, up.
            Frame(
                new Vector3f((float)(rear.X - front.X), (float)(rear.Y - front.Y), (float)(rear.Z - front.Z)),
                out Vector3f r0,
                out Vector3f r1,
                out Vector3f r2);
            orientation = new Matrix3f(
                -r2.X, r1.X, r0.X,
                -r2.Y, r1.Y, r0.Y,
                -r2.Z, r1.Z, r0.Z);
            position = new Vector3d(
                ((rear.X - front.X) * pivot) + front.X,
                ((rear.Y - front.Y) * pivot) + front.Y,
                ((rear.Z - front.Z) * pivot) + front.Z);
        }

        // The displacement is in metres, across the car and up it -- never along.
        float across = (float)(displacementX * 4096.0);
        float up = (float)(displacementY * 4096.0);
        position = new Vector3d(
            (up * (double)orientation.M13) + (across * (double)orientation.M12) + position.X,
            (across * (double)orientation.M22) + (up * (double)orientation.M23) + position.Y,
            (up * (double)orientation.M33) + (across * (double)orientation.M32) + position.Z);
    }

    /// <summary><c>0x4B4010</c>: rows built about a direction, which ends up (normalised) in the third.</summary>
    private static void Frame(Vector3f d, out Vector3f r0, out Vector3f r1, out Vector3f r2)
    {
        float sx, sy;
        if (d.X == 0f && d.Y == 0f)
        {
            sx = 0f;
            sy = 1f;
        }
        else
        {
            sx = d.Y;
            sy = -d.X;
            if (sx != 0f || sy != 0f)
            {
                double inverse = 1.0 / Math.Sqrt(((double)sx * sx) + ((double)sy * sy));
                sx = (float)(sx * inverse);
                sy = (float)(sy * inverse);
            }
        }

        // row0 = side x d (the side's z is 0), unnormalised values kept for the next cross product as the
        // listing does.
        float ax = (float)((double)d.Z * sy);
        float ay = (float)-((double)d.Z * sx);
        float az = (float)(((double)sx * d.Y) - ((double)sy * d.X));
        Vector3f n0 = Normalised(ax, ay, az);

        // row1 = d x row0, row2 = row0 x row1.
        float bx = (float)(((double)az * d.Y) - ((double)ay * d.Z));
        float by = (float)-(((double)az * d.X) - ((double)ax * d.Z));
        float bz = (float)(((double)ay * d.X) - ((double)ax * d.Y));
        float cx = (float)(((double)bz * n0.Y) - ((double)by * n0.Z));
        float cy = (float)-(((double)bz * n0.X) - ((double)bx * n0.Z));
        float cz = (float)(((double)by * n0.X) - ((double)bx * n0.Y));
        r0 = n0;
        r1 = Normalised(bx, by, bz);
        r2 = Normalised(cx, cy, cz);
    }

    private static Vector3f Normalised(float x, float y, float z)
    {
        if (x == 0f && y == 0f && z == 0f)
        {
            return new Vector3f(x, y, z);
        }

        double inverse = 1.0 / Math.Sqrt(((double)x * x) + ((double)y * y) + ((double)z * z));
        return new Vector3f((float)(x * inverse), (float)(y * inverse), (float)(z * inverse));
    }

    /// <summary><c>0x4B2760</c>: <c>acos</c> of its float argument clamped to [-1, 1].</summary>
    private static double Acos(float value) => Math.Acos(Math.Clamp(value, -1f, 1f));

    /// <summary><c>0x4B27D0</c>: <c>asin</c> likewise.</summary>
    private static double Asin(float value) => Math.Asin(Math.Clamp(value, -1f, 1f));

    /// <summary>One waypoint's segment as the railroad solver reads it.</summary>
    private sealed class Segment(Vector3d position, float[] points, float[] lengths, int count, float length)
    {
        public int Count { get; } = count;

        public float Length { get; } = length;

        public float[] Lengths { get; } = lengths;

        public Vector3d Point(int k) => new(
            points[3 * k] + position.X,
            points[(3 * k) + 1] + position.Y,
            points[(3 * k) + 2] + position.Z);

        /// <summary>The first coupler: the piece interpolated in the segment's own float frame, then placed.</summary>
        public Vector3d Along(int piece, float t)
        {
            int k = 3 * piece;
            return new Vector3d(
                (((X87)points[k + 3] - points[k]) * t + points[k]).Value + position.X,
                (((X87)points[k + 4] - points[k + 1]) * t + points[k + 1]).Value + position.Y,
                (((X87)points[k + 5] - points[k + 2]) * t + points[k + 2]).Value + position.Z);
        }
    }
}

/// <summary>A lead Train and the cars coupled behind it, placed together from the lead's scalar position.</summary>
/// <remarks>
/// <c>0x42A2C0</c> asks <c>0x4FEC10</c> for one coupler more than there are cars, the lead's coupling length
/// first and then each child Train's in order, all signed by the LEAD's flip; <c>0x42A7C0</c> lays each child on
/// the next pair. Each car keeps its own pivot fraction and displacement.
/// </remarks>
internal sealed class RailroadConsist(RailroadPath railroad)
{
    private readonly List<Car> _cars = [];

    /// <summary>Gets the rail the consist runs on.</summary>
    public RailroadPath Railroad { get; } = railroad;

    /// <summary>Appends a car behind the last.</summary>
    /// <param name="car">The car's Train task.</param>
    /// <param name="couplingDistance">Its coupling distance (<c>Train+1868</c>).</param>
    /// <param name="pivotFraction">Its origin's fraction between couplers (<c>Train+1864</c>).</param>
    /// <returns>The car's index in the consist, the lead's being 0.</returns>
    public int Add(DeclaredTask car, float couplingDistance, float pivotFraction)
    {
        ArgumentNullException.ThrowIfNull(car);
        _cars.Add(new Car(
            Railroad.Flipped ? couplingDistance : -couplingDistance,
            pivotFraction,
            Real(car, "Displacement X"),
            Real(car, "Displacement Y")));
        return _cars.Count - 1;
    }

    /// <summary>Places one car of the consist with the lead at a scalar position.</summary>
    /// <param name="trackPosition">The lead's scalar position along the rail.</param>
    /// <param name="index">The car's index.</param>
    /// <param name="position">Receives the car's model origin.</param>
    /// <param name="orientation">Receives its frame.</param>
    /// <returns>Whether the rail placed it.</returns>
    public bool TryPlace(float trackPosition, int index, out Vector3d position, out Matrix3f orientation)
    {
        // The walk is sequential, so the couplers up to this car do not depend on the cars behind it.
        Span<float> steps = stackalloc float[index + 1];
        for (int i = 0; i <= index; i++)
        {
            steps[i] = _cars[i].Step;
        }

        Span<Vector3d> points = stackalloc Vector3d[index + 2];
        if (!Railroad.TryWalk(trackPosition, steps, points))
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        Car car = _cars[index];
        RailroadPath.PlaceCar(points[index], points[index + 1], car.Pivot, car.DisplacementX, car.DisplacementY, out position, out orientation);
        return true;
    }

    private static float Real(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is float value ? value : 0f;

    private readonly record struct Car(float Step, float Pivot, float DisplacementX, float DisplacementY);
}
