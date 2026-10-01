using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>A closed convex hull over a point cloud, with its faces and uniform-density mass properties.</summary>
/// <remarks>
/// <para>
/// Built incrementally: an initial tetrahedron, then every remaining point either falls inside or
/// replaces the faces it can see with a fan to their horizon. The point clouds this serves are a
/// weapon's rendered vertices — a few thousand at most, hulled once per drop — so the plain
/// <c>O(n·F)</c> visibility scan is quicker to trust than a conflict-list quickhull and fast enough.
/// </para>
/// <para>
/// The faces are what make it a <i>mesh</i> collider rather than a cloud of probe points: they let a
/// world edge or corner that cuts into the gun find the face it entered by, which a vertex test alone
/// never sees.
/// </para>
/// </remarks>
public sealed class ConvexHull
{
    private ConvexHull(Vector3d[] vertices, HullFace[] faces)
    {
        Vertices = vertices;
        Faces = faces;
        ComputeMassProperties(vertices, faces, out double volume, out Vector3d centre, out Matrix3d inertia);
        Volume = volume;
        Centroid = centre;
        UnitInertia = inertia;
    }

    /// <summary>Gets the hull's vertices, in the source cloud's space.</summary>
    public Vector3d[] Vertices { get; }

    /// <summary>Gets the hull's faces, wound outward.</summary>
    public HullFace[] Faces { get; }

    /// <summary>Gets the enclosed volume, in the source space's cubed units.</summary>
    public double Volume { get; }

    /// <summary>Gets the centre of mass of a uniformly dense solid filling the hull.</summary>
    public Vector3d Centroid { get; }

    /// <summary>Gets the inertia tensor about <see cref="Centroid"/> for a unit mass.</summary>
    public Matrix3d UnitInertia { get; }

    /// <summary>Hulls a point cloud.</summary>
    /// <param name="points">The points; at least four that are not coplanar.</param>
    /// <param name="maximumVertices">
    /// The hull is re-hulled over its most extreme vertices in a fixed set of directions until it has
    /// no more than this many, which bounds the contact work per step whatever the model's detail.
    /// </param>
    /// <returns>The hull, or <see langword="null"/> when the points span no volume.</returns>
    public static ConvexHull? Build(IReadOnlyList<Vector3d> points, int maximumVertices = 64)
    {
        ArgumentNullException.ThrowIfNull(points);
        ConvexHull? hull = BuildOnce(points);
        if (hull is null || hull.Vertices.Length <= maximumVertices)
        {
            return hull;
        }

        return BuildOnce(SupportPoints(hull.Vertices, maximumVertices)) ?? hull;
    }

    private static ConvexHull? BuildOnce(IReadOnlyList<Vector3d> source)
    {
        Vector3d[] points = Deduplicate(source);
        if (points.Length < 4)
        {
            return null;
        }

        double scale = 0.0;
        foreach (Vector3d point in points)
        {
            scale = Math.Max(scale, Math.Max(Math.Abs(point.X), Math.Max(Math.Abs(point.Y), Math.Abs(point.Z))));
        }

        double epsilon = Math.Max(scale, 1.0) * 1e-7;
        if (!TryInitialTetrahedron(points, epsilon, out int i0, out int i1, out int i2, out int i3))
        {
            return null;
        }

        Vector3d interior = (points[i0] + points[i1] + points[i2] + points[i3]) / 4.0;
        List<Face> faces =
        [
            Face.Create(points, i0, i1, i2, interior),
            Face.Create(points, i0, i1, i3, interior),
            Face.Create(points, i0, i2, i3, interior),
            Face.Create(points, i1, i2, i3, interior),
        ];

        HashSet<(int, int)> visibleEdges = [];
        List<(int A, int B)> horizon = [];
        for (int p = 0; p < points.Length; p++)
        {
            if (p == i0 || p == i1 || p == i2 || p == i3)
            {
                continue;
            }

            Vector3d point = points[p];
            visibleEdges.Clear();
            bool any = false;
            foreach (Face face in faces)
            {
                if (face.Distance(point) > epsilon)
                {
                    any = true;
                    visibleEdges.Add((face.A, face.B));
                    visibleEdges.Add((face.B, face.C));
                    visibleEdges.Add((face.C, face.A));
                }
            }

            if (!any)
            {
                continue;
            }

            horizon.Clear();
            foreach ((int a, int b) in visibleEdges)
            {
                if (!visibleEdges.Contains((b, a)))
                {
                    horizon.Add((a, b));
                }
            }

            faces.RemoveAll(face => face.Distance(point) > epsilon);
            foreach ((int a, int b) in horizon)
            {
                faces.Add(Face.Create(points, a, b, p, interior));
            }
        }

        Dictionary<int, int> remap = [];
        List<Vector3d> vertices = [];
        HullFace[] hullFaces = new HullFace[faces.Count];
        for (int f = 0; f < faces.Count; f++)
        {
            Face face = faces[f];
            hullFaces[f] = new HullFace(Map(face.A), Map(face.B), Map(face.C), face.Normal, face.Offset);
        }

        return new ConvexHull([.. vertices], hullFaces);

        int Map(int index)
        {
            if (!remap.TryGetValue(index, out int mapped))
            {
                mapped = vertices.Count;
                vertices.Add(points[index]);
                remap.Add(index, mapped);
            }

            return mapped;
        }
    }

    private static Vector3d[] Deduplicate(IReadOnlyList<Vector3d> source)
    {
        HashSet<(long, long, long)> seen = [];
        List<Vector3d> points = new(source.Count);
        foreach (Vector3d point in source)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            {
                continue;
            }

            // A tenth of an engine unit -- a fortieth of a millimetre -- is below anything a render
            // mesh distinguishes, and merging closer points keeps the hull free of sliver faces.
            if (seen.Add(((long)Math.Round(point.X * 10.0), (long)Math.Round(point.Y * 10.0), (long)Math.Round(point.Z * 10.0))))
            {
                points.Add(point);
            }
        }

        return [.. points];
    }

    private static bool TryInitialTetrahedron(
        Vector3d[] points,
        double epsilon,
        out int i0,
        out int i1,
        out int i2,
        out int i3)
    {
        i0 = i1 = i2 = i3 = 0;

        // The two points furthest apart along the widest axis...
        int minX = 0, maxX = 0, minY = 0, maxY = 0, minZ = 0, maxZ = 0;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i].X < points[minX].X) { minX = i; }
            if (points[i].X > points[maxX].X) { maxX = i; }
            if (points[i].Y < points[minY].Y) { minY = i; }
            if (points[i].Y > points[maxY].Y) { maxY = i; }
            if (points[i].Z < points[minZ].Z) { minZ = i; }
            if (points[i].Z > points[maxZ].Z) { maxZ = i; }
        }

        (int a, int b)[] pairs = [(minX, maxX), (minY, maxY), (minZ, maxZ)];
        (i0, i1) = pairs.MaxBy(pair => (points[pair.b] - points[pair.a]).LengthSquared);
        Vector3d axis = points[i1] - points[i0];
        if (axis.LengthSquared <= epsilon * epsilon)
        {
            return false;
        }

        // ...the point furthest from the line through them...
        double best = 0.0;
        for (int i = 0; i < points.Length; i++)
        {
            double distance = Vector3d.Cross(points[i] - points[i0], axis).LengthSquared;
            if (distance > best)
            {
                best = distance;
                i2 = i;
            }
        }

        if (best <= epsilon * epsilon * axis.LengthSquared)
        {
            return false;
        }

        // ...and the point furthest from the plane of the three.
        Vector3d normal = Vector3d.Cross(axis, points[i2] - points[i0]).Normalized();
        best = 0.0;
        for (int i = 0; i < points.Length; i++)
        {
            double distance = Math.Abs(Vector3d.Dot(points[i] - points[i0], normal));
            if (distance > best)
            {
                best = distance;
                i3 = i;
            }
        }

        return best > epsilon;
    }

    private static Vector3d[] SupportPoints(Vector3d[] vertices, int maximum)
    {
        // Evenly spread directions on a sphere (a Fibonacci lattice), each keeping its most extreme
        // hull vertex. Extremes are what a surface meets first, so the reduced hull loses sliver
        // detail rather than reach.
        HashSet<int> kept = [];
        int directions = maximum;
        double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (int d = 0; d < directions && kept.Count < maximum; d++)
        {
            double z = 1.0 - ((2.0 * d) + 1.0) / directions;
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - (z * z)));
            double angle = golden * d;
            Vector3d direction = new(Math.Cos(angle) * radius, Math.Sin(angle) * radius, z);

            int support = 0;
            double furthest = double.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                double along = Vector3d.Dot(vertices[i], direction);
                if (along > furthest)
                {
                    furthest = along;
                    support = i;
                }
            }

            kept.Add(support);
        }

        return [.. kept.Select(index => vertices[index])];
    }

    // Eberly, "Polyhedral Mass Properties (Revisited)": exact volume integrals over the closed hull.
    private static void ComputeMassProperties(
        Vector3d[] vertices,
        HullFace[] faces,
        out double volume,
        out Vector3d centre,
        out Matrix3d inertia)
    {
        const double mult0 = 1.0 / 6.0, mult1 = 1.0 / 24.0, mult2 = 1.0 / 60.0, mult3 = 1.0 / 120.0;
        double[] integral = new double[10];

        // Integrate relative to a point inside the hull to keep the sums well conditioned far from
        // the model origin.
        Vector3d origin = default;
        foreach (Vector3d vertex in vertices)
        {
            origin += vertex;
        }

        origin /= Math.Max(vertices.Length, 1);

        foreach (HullFace face in faces)
        {
            Vector3d v0 = vertices[face.A] - origin;
            Vector3d v1 = vertices[face.B] - origin;
            Vector3d v2 = vertices[face.C] - origin;
            Vector3d d = Vector3d.Cross(v1 - v0, v2 - v0);

            Subexpressions(v0.X, v1.X, v2.X, out double f1x, out double f2x, out double f3x, out double g0x, out double g1x, out double g2x);
            Subexpressions(v0.Y, v1.Y, v2.Y, out double f1y, out double f2y, out double f3y, out double g0y, out double g1y, out double g2y);
            Subexpressions(v0.Z, v1.Z, v2.Z, out double f1z, out double f2z, out double f3z, out double g0z, out double g1z, out double g2z);

            integral[0] += d.X * f1x;
            integral[1] += d.X * f2x;
            integral[2] += d.Y * f2y;
            integral[3] += d.Z * f2z;
            integral[4] += d.X * f3x;
            integral[5] += d.Y * f3y;
            integral[6] += d.Z * f3z;
            integral[7] += d.X * ((v0.Y * g0x) + (v1.Y * g1x) + (v2.Y * g2x));
            integral[8] += d.Y * ((v0.Z * g0y) + (v1.Z * g1y) + (v2.Z * g2y));
            integral[9] += d.Z * ((v0.X * g0z) + (v1.X * g1z) + (v2.X * g2z));
        }

        integral[0] *= mult0;
        integral[1] *= mult1;
        integral[2] *= mult1;
        integral[3] *= mult1;
        integral[4] *= mult2;
        integral[5] *= mult2;
        integral[6] *= mult2;
        integral[7] *= mult3;
        integral[8] *= mult3;
        integral[9] *= mult3;

        volume = integral[0];
        if (!(volume > 0.0))
        {
            volume = 0.0;
            centre = origin;
            inertia = Matrix3d.Identity;
            return;
        }

        Vector3d local = new(integral[1] / volume, integral[2] / volume, integral[3] / volume);
        centre = origin + local;

        // Per unit mass: divide the volume integrals by the volume, then move to the centre.
        double xx = ((integral[5] + integral[6]) / volume) - ((local.Y * local.Y) + (local.Z * local.Z));
        double yy = ((integral[4] + integral[6]) / volume) - ((local.Z * local.Z) + (local.X * local.X));
        double zz = ((integral[4] + integral[5]) / volume) - ((local.X * local.X) + (local.Y * local.Y));
        double xy = -((integral[7] / volume) - (local.X * local.Y));
        double yz = -((integral[8] / volume) - (local.Y * local.Z));
        double xz = -((integral[9] / volume) - (local.Z * local.X));
        inertia = new Matrix3d(xx, xy, xz, xy, yy, yz, xz, yz, zz);
    }

    private static void Subexpressions(
        double w0,
        double w1,
        double w2,
        out double f1,
        out double f2,
        out double f3,
        out double g0,
        out double g1,
        out double g2)
    {
        double temp0 = w0 + w1;
        f1 = temp0 + w2;
        double temp1 = w0 * w0;
        double temp2 = temp1 + (w1 * temp0);
        f2 = temp2 + (w2 * f1);
        f3 = (w0 * temp1) + (w1 * temp2) + (w2 * f2);
        g0 = f2 + (w0 * (f1 + w0));
        g1 = f2 + (w1 * (f1 + w1));
        g2 = f2 + (w2 * (f1 + w2));
    }

    private readonly record struct Face(int A, int B, int C, Vector3d Normal, double Offset)
    {
        public static Face Create(Vector3d[] points, int a, int b, int c, Vector3d interior)
        {
            Vector3d normal = Vector3d.Cross(points[b] - points[a], points[c] - points[a]);
            double length = normal.Length;
            normal = length > 0.0 ? normal / length : Vector3d.UnitZ;
            double offset = Vector3d.Dot(normal, points[a]);

            // Wind every face so its normal points away from the interior.
            if (Vector3d.Dot(normal, interior) - offset > 0.0)
            {
                return new Face(a, c, b, -normal, -offset);
            }

            return new Face(a, b, c, normal, offset);
        }

        public double Distance(Vector3d point) => Vector3d.Dot(Normal, point) - Offset;
    }
}

/// <summary>One outward-wound face of a <see cref="ConvexHull"/>.</summary>
/// <param name="A">The first vertex index.</param>
/// <param name="B">The second vertex index.</param>
/// <param name="C">The third vertex index.</param>
/// <param name="Normal">The outward unit normal.</param>
/// <param name="Offset">The plane offset: a point is inside when <c>Normal · p ≤ Offset</c>.</param>
public readonly record struct HullFace(int A, int B, int C, Vector3d Normal, double Offset);
