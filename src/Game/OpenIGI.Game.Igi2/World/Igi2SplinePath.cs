using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>A whole spline, end to end, as something that can be walked by distance.</summary>
/// <remarks>
/// <para>
/// The segments drawn between waypoints are also the track a <c>Train</c> rides. A train carries no
/// position: it names a spline and a distance along it, and everything else follows from walking the
/// curve. <c>SplineObj_GetPosAndMatrix</c> (<c>igi2ed 0x63FA10</c>) is that walk, and this is its
/// position half — a train asks for no matrix, passing null for it, and builds each carriage's facing
/// from the two points the carriage spans.
/// </para>
/// <para>
/// <b>A carriage is rigid and the track is not straight.</b> Which is why stepping back from the
/// locomotive is not simply subtracting arc length. The original walks areas until it finds the pair
/// whose ends straddle the wanted STRAIGHT-LINE distance, then solves the triangle they make with the
/// point it is stepping from: a cosine rule for the angle at the near frame, a sine rule for where along
/// that area the chord comes out right. A train on a curve is shorter along the track than it is long.
/// </para>
/// </remarks>
public sealed class Igi2SplinePath
{
    /// <summary>How close to straight the solved triangle has to get before it is treated as straight.</summary>
    /// <remarks>The original's own threshold, in radians, from <c>0x63FA10</c>.</remarks>
    private const float Degenerate = 0.001f;

    private readonly Igi2SplineSweep[] _segments;

    /// <summary>Builds a track out of the curves between a spline's waypoints.</summary>
    /// <param name="levelId">The level task id the spline was declared with.</param>
    /// <param name="segments">The curves, in waypoint order, including any that draw nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="segments"/> is <see langword="null"/>.</exception>
    public Igi2SplinePath(int levelId, Igi2SplineSweep[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        LevelId = levelId;
        _segments = segments;

        float total = 0f;
        foreach (Igi2SplineSweep segment in segments)
        {
            total += segment.Length;
        }

        Length = total;
    }

    /// <summary>Gets the level task id the spline was declared with, which a train names it by.</summary>
    public int LevelId { get; }

    /// <summary>Gets the whole spline's length, in engine units.</summary>
    /// <remarks>
    /// Summed over the segments, which is how <c>SplineObjWaypoint_UpdateInternalDataHandler</c>
    /// (<c>igi2ed 0x6417C0</c>) fills the spline's own <c>+60</c> at the end of every update.
    /// </remarks>
    public float Length { get; }

    /// <summary>Gets the segments, from the first waypoint pair to the last.</summary>
    public IReadOnlyList<Igi2SplineSweep> Segments => _segments;

    /// <summary>
    /// Walks a chain of couplings along the spline, from a distance and a run of straight-line steps.
    /// </summary>
    /// <param name="distance">Where the first coupling sits, in engine units along the spline.</param>
    /// <param name="steps">
    /// Each following coupling's straight-line distance from the one before, in engine units. Negative
    /// runs back down the spline, which is what a train with no <c>Flip Direction</c> does.
    /// </param>
    /// <param name="points">Receives <c>steps.Count + 1</c> world points, in engine units.</param>
    /// <returns><see langword="false"/> when the spline is empty or has no length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> is <see langword="null"/>.</exception>
    public bool TryWalk(double distance, IReadOnlyList<float> steps, out Vector3d[] points)
    {
        ArgumentNullException.ThrowIfNull(steps);

        points = [];
        if (_segments.Length == 0)
        {
            return false;
        }

        Vector3d[] walked = new Vector3d[steps.Count + 1];

        // Off either end of the spline is on it: the walk wraps. A train authored past the end of its
        // track is at the start of it, which is how a loop of track is driven round for ever.
        double along = distance;
        if (Length == 0f)
        {
            for (int i = 0; i < walked.Length; i++)
            {
                walked[i] = _segments[0].PointAt(0);
            }

            points = walked;
            return true;
        }

        while (along < 0.0)
        {
            along += Length;
        }

        while (along >= Length)
        {
            along -= Length;
        }

        // Which segment holds it, then which of that segment's areas, then where in the area. Measured in
        // arc from the spline's start throughout, which is what the following couplings step from.
        int segment = 0;
        double arc = 0.0;
        while (segment < _segments.Length - 1 && arc + _segments[segment].Length < along)
        {
            arc += _segments[segment].Length;
            segment++;
        }

        int area = 0;
        while (area < _segments[segment].AreaLengths.Count - 1
            && arc + _segments[segment].AreaLengths[area] < along)
        {
            arc += _segments[segment].AreaLengths[area];
            area++;
        }

        walked[0] = Between(segment, area, Fraction(along - arc, _segments[segment].AreaLengths[area]));

        double reached = along;
        for (int i = 0; i < steps.Count; i++)
        {
            float step = steps[i];
            reached += step;
            walked[i + 1] = Couple(walked[i], step, reached, ref segment, ref area, ref arc);
        }

        points = walked;
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 spline {LevelId}, {_segments.Length} segment(s), {Length / 4096f:F0}m";

    private static float Fraction(double into, float length) =>
        length > 0f ? (float)(into / length) : 0f;

    /// <summary>Finds the point one straight-line step from the last, walking the curve to reach it.</summary>
    private Vector3d Couple(Vector3d from, float step, double reached, ref int segment, ref int area, ref double arc)
    {
        float target = step * step;
        return step < 0f
            ? Backwards(from, step, target, reached, ref segment, ref area, ref arc)
            : Forwards(from, step, target, reached, ref segment, ref area, ref arc);
    }

    private Vector3d Backwards(
        Vector3d from,
        float step,
        float target,
        double reached,
        ref int segment,
        ref int area,
        ref double arc)
    {
        // Still inside this area? Then the original does not solve the triangle at all -- it takes the
        // arc fraction, which is the same answer wherever the curve is nearly straight.
        if (SquaredDistance(from, Between(segment, area, 0f)) > target)
        {
            return Between(segment, area, Fraction(reached - arc, _segments[segment].AreaLengths[area]));
        }

        float near;
        float far;
        while (true)
        {
            if (area > 0)
            {
                area--;
                arc -= _segments[segment].AreaLengths[area];
            }
            else if (segment > 0)
            {
                segment--;
                area = _segments[segment].AreaLengths.Count - 1;
                arc -= _segments[segment].AreaLengths[area];
            }
            else
            {
                // Off the start of the track. The original clamps to the segment's own first frame.
                return _segments[segment].PointAt(0);
            }

            near = SquaredDistance(from, Between(segment, area, 0f));
            far = SquaredDistance(from, Between(segment, area, 1f));
            if (near > target != far > target)
            {
                break;
            }
        }

        return Between(segment, area, Solve(near, far, _segments[segment].AreaLengths[area], -step, fromFar: true));
    }

    private Vector3d Forwards(
        Vector3d from,
        float step,
        float target,
        double reached,
        ref int segment,
        ref int area,
        ref double arc)
    {
        if (SquaredDistance(from, Between(segment, area, 1f)) > target)
        {
            return Between(segment, area, Fraction(reached - arc, _segments[segment].AreaLengths[area]));
        }

        float near;
        float far;
        while (true)
        {
            if (area < _segments[segment].AreaLengths.Count - 1)
            {
                arc += _segments[segment].AreaLengths[area];
                area++;
            }
            else if (segment < _segments.Length - 1)
            {
                arc += _segments[segment].AreaLengths[area];
                segment++;
                area = 0;
            }
            else
            {
                // Off the end of the track, and clamped to the segment's own last frame.
                return _segments[segment].PointAt(_segments[segment].Frames.Count - 1);
            }

            near = SquaredDistance(from, Between(segment, area, 0f));
            far = SquaredDistance(from, Between(segment, area, 1f));
            if (near > target != far > target)
            {
                break;
            }
        }

        return Between(segment, area, Solve(near, far, _segments[segment].AreaLengths[area], step, fromFar: false));
    }

    /// <summary>
    /// Solves where along one area a point sits at a given straight-line distance from somewhere else.
    /// </summary>
    /// <param name="nearSquared">The squared distance from the anchor to the area's first frame.</param>
    /// <param name="farSquared">The squared distance from the anchor to the area's second frame.</param>
    /// <param name="length">The area's own length.</param>
    /// <param name="reach">The straight-line distance wanted, as a positive length.</param>
    /// <param name="fromFar">
    /// Whether the walk arrived from the far end, which is what stepping BACK down the spline does. It
    /// decides which end the sine rule measures from, and therefore which way round the answer is.
    /// </param>
    /// <returns>The fraction along the area, from its first frame to its second.</returns>
    /// <remarks>
    /// The triangle is the anchor, the end the walk arrived at, and the answer. The cosine rule gives the
    /// angle the area leaves that end at; the sine rule then gives how far along the area the answer
    /// lies. Where the triangle collapses — anchor, end and answer in a line — the original interpolates
    /// the two end distances instead, and does so from the FIRST frame either way round.
    /// </remarks>
    private static float Solve(float nearSquared, float farSquared, float length, float reach, bool fromFar)
    {
        float anchored = MathF.Sqrt(fromFar ? farSquared : nearSquared);
        float opposite = fromFar ? nearSquared : farSquared;
        float squared = fromFar ? farSquared : nearSquared;
        if (length <= 0f || anchored <= 0f || reach <= 0f)
        {
            return fromFar ? 1f : 0f;
        }

        float angle = MathF.Acos(
            Math.Clamp((opposite - squared - (length * length)) / (-2f * anchored * length), -1f, 1f));
        float sine = MathF.Sin(angle);
        float rest = MathF.PI - angle - MathF.Asin(Math.Clamp(sine / reach * anchored, -1f, 1f));
        if (rest <= Degenerate || sine == 0f)
        {
            // Both ways round this reads from the first frame, because at a fraction of zero the distance
            // is the near one and at one it is the far one, whichever end the walk came in from.
            float near = MathF.Sqrt(nearSquared);
            float spread = MathF.Sqrt(farSquared) - near;
            return spread == 0f ? (fromFar ? 1f : 0f) : (reach - near) / spread;
        }

        float into = MathF.Sin(rest) * reach / sine / length;
        return fromFar ? 1f - into : into;
    }

    private Vector3d Between(int segment, int area, float fraction)
    {
        Vector3d low = _segments[segment].PointAt(area);
        Vector3d high = _segments[segment].PointAt(area + 1);
        return new Vector3d(
            ((high.X - low.X) * fraction) + low.X,
            ((high.Y - low.Y) * fraction) + low.Y,
            ((high.Z - low.Z) * fraction) + low.Z);
    }

    private static float SquaredDistance(Vector3d left, Vector3d right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        double z = left.Z - right.Z;
        return (float)((x * x) + (y * y) + (z * z));
    }
}

/// <summary>What reading one <c>SplineObj</c> gives back: what it draws, and what rides over it.</summary>
/// <param name="Path">The whole spline as a track, including the pairs that draw nothing.</param>
/// <param name="Segments">One placement per pair whose waypoint names a <c>Segment Model</c>.</param>
public readonly record struct Igi2SplineRun(Igi2SplinePath Path, IReadOnlyList<Igi2Placement> Segments);
