using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One solid world triangle, in world units.</summary>
/// <param name="A">The first corner.</param>
/// <param name="B">The second corner.</param>
/// <param name="C">The third corner.</param>
/// <remarks>
/// The winding carries no meaning: level collision meshes wind into the solid and terrain winds out
/// of it, so a consumer decides which side it is on from where it stands, not from the order.
/// </remarks>
public readonly record struct CollisionTriangle(Vector3d A, Vector3d B, Vector3d C)
{
    /// <summary>Tests the triangle's bounds against a box.</summary>
    /// <param name="min">The box's low corner.</param>
    /// <param name="max">The box's high corner.</param>
    /// <returns><see langword="true"/> when the two overlap.</returns>
    public bool Overlaps(Vector3d min, Vector3d max) =>
        Math.Max(A.X, Math.Max(B.X, C.X)) >= min.X && Math.Min(A.X, Math.Min(B.X, C.X)) <= max.X
        && Math.Max(A.Y, Math.Max(B.Y, C.Y)) >= min.Y && Math.Min(A.Y, Math.Min(B.Y, C.Y)) <= max.Y
        && Math.Max(A.Z, Math.Max(B.Z, C.Z)) >= min.Z && Math.Min(A.Z, Math.Min(B.Z, C.Z)) <= max.Z;

    /// <summary>The point of this triangle nearest a point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The nearest point on the triangle, edges and corners included.</returns>
    public Vector3d ClosestPoint(Vector3d point) => ClosestPoint(point, A, B, C);

    /// <summary>The point of a triangle nearest a point (Ericson, <i>Real-Time Collision Detection</i>, 5.1.5).</summary>
    /// <param name="p">The point.</param>
    /// <param name="a">The triangle's first corner.</param>
    /// <param name="b">Its second corner.</param>
    /// <param name="c">Its third corner.</param>
    /// <returns>The nearest point on the triangle, edges and corners included.</returns>
    public static Vector3d ClosestPoint(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
    {
        Vector3d ab = b - a;
        Vector3d ac = c - a;
        Vector3d ap = p - a;
        double d1 = Vector3d.Dot(ab, ap);
        double d2 = Vector3d.Dot(ac, ap);
        if (d1 <= 0.0 && d2 <= 0.0)
        {
            return a;
        }

        Vector3d bp = p - b;
        double d3 = Vector3d.Dot(ab, bp);
        double d4 = Vector3d.Dot(ac, bp);
        if (d3 >= 0.0 && d4 <= d3)
        {
            return b;
        }

        double vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
        {
            return a + (ab * (d1 / (d1 - d3)));
        }

        Vector3d cp = p - c;
        double d5 = Vector3d.Dot(ab, cp);
        double d6 = Vector3d.Dot(ac, cp);
        if (d6 >= 0.0 && d5 <= d6)
        {
            return c;
        }

        double vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
        {
            return a + (ac * (d2 / (d2 - d6)));
        }

        double va = (d3 * d6) - (d5 * d4);
        if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
        {
            return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
        }

        double denominator = 1.0 / (va + vb + vc);
        return a + (ab * (vb * denominator)) + (ac * (vc * denominator));
    }

    /// <summary>The nearest pair of points between this triangle and a line segment.</summary>
    /// <param name="from">The segment's start.</param>
    /// <param name="to">The segment's end.</param>
    /// <param name="onSegment">Receives the segment's point nearest the triangle.</param>
    /// <param name="onTriangle">Receives the triangle's point nearest the segment.</param>
    /// <returns>
    /// <see langword="true"/> when the segment passes through the triangle, in which case the two points
    /// are the same point and say nothing about which way is out.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A capsule against a triangle is this and a radius. The segment is first tested for passing straight
    /// through the face; otherwise the nearest pair is found by projecting back and forth between the two,
    /// which converges because both are convex — the same alternation the ragdoll's capsule pairs use, run
    /// a few more rounds because a triangle has corners for it to settle into.
    /// </para>
    /// </remarks>
    public bool NearestToSegment(Vector3d from, Vector3d to, out Vector3d onSegment, out Vector3d onTriangle)
    {
        Vector3d normal = Vector3d.Cross(B - A, C - A);
        double start = Vector3d.Dot(from - A, normal);
        double end = Vector3d.Dot(to - A, normal);
        if ((start < 0.0 && end > 0.0) || (start > 0.0 && end < 0.0))
        {
            Vector3d crossing = from + ((to - from) * (start / (start - end)));
            Vector3d onFace = ClosestPoint(crossing);
            if (Vector3d.DistanceSquared(onFace, crossing) < 1e-6)
            {
                onSegment = crossing;
                onTriangle = crossing;
                return true;
            }
        }

        onSegment = Vector3d.ClosestOnSegment(from, to, (A + B + C) / 3.0);
        onTriangle = ClosestPoint(onSegment);
        for (int round = 0; round < 4; round++)
        {
            onSegment = Vector3d.ClosestOnSegment(from, to, onTriangle);
            onTriangle = ClosestPoint(onSegment);
        }

        return false;
    }
}

/// <summary>Collects every solid world triangle that touches a box.</summary>
/// <param name="min">The box's low corner, in world units.</param>
/// <param name="max">The box's high corner.</param>
/// <param name="into">Receives the triangles; not cleared first.</param>
/// <remarks>
/// The query a rigid body's narrow phase runs once per tick over the space it can reach, so its
/// contacts are measured against the real surfaces -- slopes, ledges and corners -- rather than
/// sampled through rays.
/// </remarks>
public delegate void CollisionTriangleGather(Vector3d min, Vector3d max, List<CollisionTriangle> into);
