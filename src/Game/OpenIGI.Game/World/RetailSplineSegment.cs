using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// One bent spline segment -- a piece of road, rail or bridge -- as the original builds it and casts rays against
/// it: <c>Segment_Build</c> (<c>0x4E4530</c>), <c>Segment_BendVertices</c> (<c>0x49A050</c>) and
/// <c>SplineObjSegment</c>'s ray handler, path A (<c>0x4E16E0</c>), all exact.
/// </summary>
/// <remarks>
/// The segment model is authored along its local X; the original samples a Hermite cubic between the two
/// waypoints into <c>N + 1</c> points and frames and bends each collision vertex by blending the two slices around
/// its X. The ray is cast in the segment's translated (never rotated) frame. Only the bent (non-rigid) branch is
/// transcribed; rigid segments (path B) are not.
/// </remarks>
public sealed class RetailSplineSegment
{
    private static readonly float PiF = BitConverter.Int32BitsToSingle(0x40490FDB);
    private static readonly float TwoPiF = BitConverter.Int32BitsToSingle(0x40C90FDB);

    private RetailSplineSegment(
        object owner,
        RetailRayMesh mesh,
        double[] position,
        float[] vertices,
        float[] spheres,
        float radius,
        bool reverse)
    {
        Owner = owner;
        Mesh = mesh;
        Position = position;
        Vertices = vertices;
        Spheres = spheres;
        Radius = radius;
        Reverse = reverse;
    }

    /// <summary>Gets what a hit reports as struck.</summary>
    public object Owner { get; }

    /// <summary>Gets the segment model's collision level 0.</summary>
    public RetailRayMesh Mesh { get; }

    /// <summary>Gets the segment's position: the waypoints' midpoint (<c>+32</c>).</summary>
    public double[] Position { get; }

    /// <summary>Gets the bent collision vertices, three floats each, relative to <see cref="Position"/>.</summary>
    public float[] Vertices { get; }

    /// <summary>
    /// Gets the segment's own sphere tree (<c>+0x5A0</c>), four floats a node: the model's tree with every centre bent
    /// like the vertices (<c>0x4E1440</c>) and every radius rebuilt around its subtree's bent faces (<c>0x4E51C0</c>).
    /// </summary>
    public float[] Spheres { get; }

    /// <summary>Gets the broad-phase radius (<c>+0xF4</c>).</summary>
    public float Radius { get; }

    /// <summary>Gets a value indicating whether the segment is laid reversed (<c>+0x5B5</c>).</summary>
    public bool Reverse { get; }

    /// <summary>Builds and bends a segment between two waypoints -- <c>0x4E4530</c>, Hermite branch, then <c>0x49A050</c>.</summary>
    /// <param name="owner">What a hit reports as struck.</param>
    /// <param name="mesh">The segment model's collision level 0.</param>
    /// <param name="radiusMeshes">Every collision level the radius is taken over (levels 0 and 1).</param>
    /// <param name="p0">The previous waypoint's position.</param>
    /// <param name="p1">This waypoint's position.</param>
    /// <param name="a3">The previous waypoint's orientation (<c>0x4B38E0</c>), row-major.</param>
    /// <param name="a4">This waypoint's orientation.</param>
    /// <param name="count">The matrix count <c>N</c>.</param>
    /// <param name="x0">The model's render-vertex X minimum (never above 0).</param>
    /// <param name="x1">The model's render-vertex X maximum (never below 0).</param>
    /// <param name="reverse">Whether the segment is laid reversed.</param>
    /// <returns>The segment.</returns>
    public static RetailSplineSegment Build(
        object owner,
        RetailRayMesh mesh,
        IReadOnlyList<RetailRayMesh> radiusMeshes,
        Vector3d p0,
        Vector3d p1,
        float[] a3,
        float[] a4,
        int count,
        float x0,
        float x1,
        bool reverse)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(radiusMeshes);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        double length = Dist(p0, p1);
        double[] cx = Hermite(p0.X, p1.X, ((X87)a3[0] * length).Value, ((X87)a4[0] * length).Value);
        double[] cy = Hermite(p0.Y, p1.Y, ((X87)a3[3] * length).Value, ((X87)a4[3] * length).Value);
        double[] cz = Hermite(p0.Z, p1.Z, ((X87)a3[6] * length).Value, ((X87)a4[6] * length).Value);
        double[] mid =
        [
            (((X87)p0.X + p1.X) * 0.5).Value,
            (((X87)p0.Y + p1.Y) * 0.5).Value,
            (((X87)p0.Z + p1.Z) * 0.5).Value,
        ];

        float invN = ((X87)1.0f / (double)count).F;
        float tw0 = Roll(a3), tw1 = Roll(a4);
        X87 window0 = tw0;
        while (((X87)tw1 - window0).Value > PiF)
        {
            window0 += TwoPiF;
        }

        tw0 = window0.F;
        while (((X87)tw1 - tw0).Value < -PiF)
        {
            tw1 = ((X87)tw1 + TwoPiF).F;
        }

        float[] points = new float[3 * (count + 1)];
        float[][] frames = new float[count + 1][];
        for (int k = 0; k <= count; k++)
        {
            double t = ((X87)(double)k * invN).Value;
            float tf = X87.ToFloat(t);
            points[3 * k] = ((X87)Eval(cx, t) - mid[0]).F;
            points[(3 * k) + 1] = ((X87)Eval(cy, t) - mid[1]).F;
            points[(3 * k) + 2] = ((X87)Eval(cz, t) - mid[2]).F;
            if (k == 0)
            {
                frames[k] = a3;
            }
            else if (k == count)
            {
                frames[k] = a4;
            }
            else
            {
                float dx = X87.ToFloat(Deriv(cx, t)), dy = X87.ToFloat(Deriv(cy, t)), dz = X87.ToFloat(Deriv(cz, t));
                float pitch = 0f, yaw = 0f;
                if (!(dx == 0f && dy == 0f && dz == 0f))
                {
                    DirectionToAngles(dx, dy, dz, out pitch, out yaw);
                }

                float twist = (((X87)tw1 - tw0) * tf + tw0).F;
                frames[k] = AnglesToMatrix(twist, pitch, yaw);
            }
        }

        float[] vertices = Bend(mesh.Vertices, points, frames, count, x0, x1, reverse);
        float radius = 1.0f;
        foreach (RetailRayMesh level in radiusMeshes)
        {
            float[] bent = ReferenceEquals(level, mesh) ? vertices : Bend(level.Vertices, points, frames, count, x0, x1, reverse);
            for (int i = 0; i < bent.Length; i += 3)
            {
                float r = X87.Sqrt((X87)bent[i] * bent[i] + (X87)bent[i + 1] * bent[i + 1] + (X87)bent[i + 2] * bent[i + 2]).F;
                if (r > radius)
                {
                    radius = r;
                }
            }
        }

        return new RetailSplineSegment(owner, mesh, mid, vertices, BentSpheres(mesh, vertices, points, frames, count, x0, x1, reverse), radius, reverse);
    }

    /// <summary>
    /// A segment's length (<c>+248</c>), which <c>0x4E4530</c> sums as it samples the segment, and a spline's knot parameters
    /// (<c>0x502180</c>) add up: what a lift's spline parameter runs along.
    /// </summary>
    /// <param name="p0">The previous waypoint's position.</param>
    /// <param name="p1">This waypoint's position.</param>
    /// <param name="a3">The previous waypoint's orientation, row-major.</param>
    /// <param name="a4">This waypoint's orientation.</param>
    /// <param name="count">The matrix count <c>N</c>.</param>
    /// <param name="linear">
    /// Whether the segment is straight: the spline's <c>Linear Segments</c> or the waypoint's <c>Align</c> (<c>+418</c>), which
    /// samples it once.
    /// </param>
    /// <returns>The length, in units.</returns>
    /// <remarks>
    /// A straight segment is its chord. A bent one is the Hermite cubic sampled at <c>N + 1</c> points, each stored as a float
    /// about the midpoint, and the distances between neighbours added in a float: the cubic leaves along each waypoint's X
    /// axis, so a lift's vertical run between waypoints facing level is longer than its rise.
    /// </remarks>
    public static float Length(Vector3d p0, Vector3d p1, float[] a3, float[] a4, int count, bool linear)
    {
        ArgumentNullException.ThrowIfNull(a3);
        ArgumentNullException.ThrowIfNull(a4);
        if (linear)
        {
            X87 dx = (X87)p1.X - p0.X, dy = (X87)p1.Y - p0.Y, dz = (X87)p1.Z - p0.Z;
            return (X87.Sqrt(dx * dx + dy * dy + dz * dz) * 1.0f).F;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        double length = Dist(p0, p1);
        double[] cx = Hermite(p0.X, p1.X, ((X87)a3[0] * length).Value, ((X87)a4[0] * length).Value);
        double[] cy = Hermite(p0.Y, p1.Y, ((X87)a3[3] * length).Value, ((X87)a4[3] * length).Value);
        double[] cz = Hermite(p0.Z, p1.Z, ((X87)a3[6] * length).Value, ((X87)a4[6] * length).Value);
        double midX = (((X87)p0.X + p1.X) * 0.5).Value;
        double midY = (((X87)p0.Y + p1.Y) * 0.5).Value;
        double midZ = (((X87)p0.Z + p1.Z) * 0.5).Value;
        float invN = ((X87)1.0f / (double)count).F;
        float sum = 0f;
        float lastX = 0f, lastY = 0f, lastZ = 0f;
        for (int k = 0; k <= count; k++)
        {
            double t = ((X87)(double)k * invN).Value;
            float x = ((X87)Eval(cx, t) - midX).F;
            float y = ((X87)Eval(cy, t) - midY).F;
            float z = ((X87)Eval(cz, t) - midZ).F;
            if (k > 0)
            {
                // A middle sample keeps its X and Y differences as floats (0x4E4B68); the last keeps all three on the stack.
                X87 dx = (X87)x - lastX, dy = (X87)y - lastY, dz = (X87)z - lastZ;
                if (k != count)
                {
                    dx = dx.F;
                    dy = dy.F;
                }

                sum = (X87.Sqrt(dx * dx + dy * dy + dz * dz) + sum).F;
            }

            (lastX, lastY, lastZ) = (x, y, z);
        }

        return sum;
    }

    /// <summary>
    /// The polyline <c>Segment_Build</c> (<c>0x4E4530</c>) leaves on a segment for the railroad solver: its
    /// <c>N + 1</c> points (<c>+260</c>, floats relative to the waypoints' midpoint at <c>+32</c>), each piece's
    /// length (<c>+1352</c>, the last entry 0) and their float sum (<c>+248</c>).
    /// </summary>
    /// <param name="p0">The previous waypoint's position.</param>
    /// <param name="p1">This waypoint's position.</param>
    /// <param name="a3">The previous waypoint's orientation (<c>0x4B38E0</c>), row-major.</param>
    /// <param name="a4">This waypoint's orientation.</param>
    /// <param name="count">The matrix count <c>N</c>; a straight segment uses 1.</param>
    /// <param name="straight">
    /// Whether the segment is laid straight (<c>+418</c> or <c>+417</c>): the points then lie on the chord,
    /// and only the first piece is given a length, the chord's over <c>N</c>.
    /// </param>
    /// <param name="middle">Receives the waypoints' midpoint.</param>
    /// <param name="lengths">Receives the piece lengths, <c>N + 1</c> of them.</param>
    /// <param name="total">Receives their sum.</param>
    /// <returns>The points, three floats each.</returns>
    internal static float[] Polyline(
        Vector3d p0,
        Vector3d p1,
        float[] a3,
        float[] a4,
        int count,
        bool straight,
        out Vector3d middle,
        out float[] lengths,
        out float total)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        double length = Dist(p0, p1);
        double[] cx = Hermite(p0.X, p1.X, ((X87)a3[0] * length).Value, ((X87)a4[0] * length).Value);
        double[] cy = Hermite(p0.Y, p1.Y, ((X87)a3[3] * length).Value, ((X87)a4[3] * length).Value);
        double[] cz = Hermite(p0.Z, p1.Z, ((X87)a3[6] * length).Value, ((X87)a4[6] * length).Value);
        middle = new Vector3d(
            (((X87)p0.X + p1.X) * 0.5).Value,
            (((X87)p0.Y + p1.Y) * 0.5).Value,
            (((X87)p0.Z + p1.Z) * 0.5).Value);

        float invN = ((X87)1.0f / (double)count).F;
        float[] points = new float[3 * (count + 1)];
        lengths = new float[count + 1];
        total = 0f;
        for (int k = 0; k <= count; k++)
        {
            double t = ((X87)(double)k * invN).Value;
            if (straight)
            {
                // X takes the double t, Y and Z its float copy (+0x5C), as the listing does.
                float tf = X87.ToFloat(t);
                points[3 * k] = ((X87)((X87)p1.X - p0.X) * t + p0.X - middle.X).F;
                points[(3 * k) + 1] = ((X87)((X87)p1.Y - p0.Y) * tf + p0.Y - middle.Y).F;
                points[(3 * k) + 2] = ((X87)((X87)p1.Z - p0.Z) * tf + p0.Z - middle.Z).F;
                if (k == 0)
                {
                    float dx = ((X87)p1.X - p0.X).F, dy = ((X87)p1.Y - p0.Y).F, dz = ((X87)p1.Z - p0.Z).F;
                    lengths[0] = (X87.Sqrt((X87)dx * dx + (X87)dy * dy + (X87)dz * dz) * invN).F;
                    total = ((X87)lengths[0] + total).F;
                }

                continue;
            }

            points[3 * k] = ((X87)Eval(cx, t) - middle.X).F;
            points[(3 * k) + 1] = ((X87)Eval(cy, t) - middle.Y).F;
            points[(3 * k) + 2] = ((X87)Eval(cz, t) - middle.Z).F;
            if (k > 0)
            {
                X87 dx = (X87)points[3 * k] - points[3 * (k - 1)];
                X87 dy = (X87)points[(3 * k) + 1] - points[(3 * (k - 1)) + 1];
                X87 dz = (X87)points[(3 * k) + 2] - points[(3 * (k - 1)) + 2];
                lengths[k - 1] = X87.Sqrt(dx * dx + dy * dy + dz * dz).F;
                total = ((X87)lengths[k - 1] + total).F;
            }
        }

        return points;
    }

    /// <summary>
    /// <c>Segment_Build</c>'s tree for a bent segment: the model's sphere tree copied, its centres bent by <c>0x4E1440</c>
    /// (the vertex bend over 24-byte records), then every radius rebuilt by <c>0x4E51C0</c> -- zeroed on entry, the
    /// children first, then each vertex of the node's own faces widening the node and every node above it.
    /// </summary>
    private static float[] BentSpheres(
        RetailRayMesh mesh,
        float[] vertices,
        float[] points,
        float[][] frames,
        int count,
        float x0,
        float x1,
        bool reverse)
    {
        int nodes = mesh.SphereCount;
        float[] centres = new float[3 * nodes];
        for (int n = 0; n < nodes; n++)
        {
            centres[3 * n] = mesh.Spheres[4 * n];
            centres[(3 * n) + 1] = mesh.Spheres[(4 * n) + 1];
            centres[(3 * n) + 2] = mesh.Spheres[(4 * n) + 2];
        }

        float[] bent = Bend(centres, points, frames, count, x0, x1, reverse);
        float[] spheres = new float[4 * nodes];
        for (int n = 0; n < nodes; n++)
        {
            spheres[4 * n] = bent[3 * n];
            spheres[(4 * n) + 1] = bent[(3 * n) + 1];
            spheres[(4 * n) + 2] = bent[(3 * n) + 2];
            spheres[(4 * n) + 3] = mesh.Spheres[(4 * n) + 3];
        }

        if (nodes > 0)
        {
            List<int> stack = [];
            Rebuild(0);

            void Rebuild(int node)
            {
                spheres[(4 * node) + 3] = 0f;
                stack.Add(node);
                int child = mesh.Links[(4 * node) + 2];
                if (child != 0)
                {
                    Rebuild(child);
                    for (int sibling = mesh.Links[(4 * child) + 3]; sibling != 0; sibling = mesh.Links[(4 * sibling) + 3])
                    {
                        Rebuild(sibling);
                    }
                }

                int first = mesh.Links[4 * node], faces = mesh.Links[(4 * node) + 1];
                for (int f = first; f < first + faces; f++)
                {
                    foreach (int held in stack)
                    {
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int v = 3 * mesh.Faces[(4 * f) + corner];
                            X87 dx = (X87)vertices[v] - spheres[4 * held];
                            X87 dy = (X87)vertices[v + 1] - spheres[(4 * held) + 1];
                            X87 dz = (X87)vertices[v + 2] - spheres[(4 * held) + 2];
                            X87 distance = X87.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (spheres[(4 * held) + 3] > distance.Value)
                            {
                                distance = spheres[(4 * held) + 3];
                            }

                            spheres[(4 * held) + 3] = distance.F;
                        }
                    }
                }

                stack.RemoveAt(stack.Count - 1);
            }
        }

        return spheres;
    }

    /// <summary>The waypoint orientation for a level's <c>(α, β, γ)</c>: <c>0x4B38E0</c>, <c>RotZ(γ)·RotY(β)·RotX(α)</c>.</summary>
    /// <param name="alpha">α.</param>
    /// <param name="beta">β.</param>
    /// <param name="gamma">γ.</param>
    /// <returns>The row-major matrix.</returns>
    public static float[] AnglesToMatrix(float alpha, float beta, float gamma) =>
        Multiply(Multiply(RotZ(gamma), RotY(beta)), RotX(alpha));

    /// <summary>Tests one ray against the segment (<c>0x4E16E0</c> path A), writing the result when strictly nearer.</summary>
    /// <param name="s">The ray start.</param>
    /// <param name="e">The ray end.</param>
    /// <param name="result">The ray's running result.</param>
    public void Cast(Vector3d s, Vector3d e, ref RetailRayResult result)
    {
        if (Broad(s, e))
        {
            Narrow(s, e, ref result);
        }
    }

    /// <summary>A batch of rays, in order, against this segment -- <c>0x4E16E0</c>'s own loop.</summary>
    /// <param name="starts">The rays' starts.</param>
    /// <param name="ends">The rays' ends.</param>
    /// <param name="results">The running results, one per ray.</param>
    /// <remarks>
    /// A ray outside the segment's box does not just skip itself: the handler jumps to its epilogue
    /// (<c>0x4E3F76</c>), so no later ray of the batch is tested against this segment at all. The placed-model
    /// handler <c>0x4C1840</c> moves on to the next ray instead (<c>0x4C2F3D</c>). Probed on Mission 7's T80: at
    /// tick 1459 its ray 2 lies below the road piece's box and its ray 3, over the road's far end, is never tested.
    /// </remarks>
    public void Cast(ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
    {
        for (int i = 0; i < results.Length; i++)
        {
            if (!Broad(starts[i], ends[i]))
            {
                return;
            }

            Narrow(starts[i], ends[i], ref results[i]);
        }
    }

    private bool Broad(Vector3d s, Vector3d e)
    {
        double px = Position[0], py = Position[1], pz = Position[2];
        X87 r = Radius;
        return Straddles((X87)px - r, r + px, s.X, e.X)
            && Straddles((X87)py - r, r + py, s.Y, e.Y)
            && Straddles((X87)pz - r, r + pz, s.Z, e.Z);
    }

    private void Narrow(Vector3d s, Vector3d e, ref RetailRayResult result)
    {
        double px = Position[0], py = Position[1], pz = Position[2];

        float[] ls = [((X87)s.X - px).F, ((X87)s.Y - py).F, ((X87)s.Z - pz).F];
        float[] le = [((X87)e.X - px).F, ((X87)e.Y - py).F, ((X87)e.Z - pz).F];
        float lenf = X87.ToFloat(Dist(s, e));
        double best = 0d;
        bool hadHit = result.Hit;
        if (hadHit)
        {
            // The previous hit in the segment's frame; a bent segment's matrix is the identity and its scale 1.
            X87 hx = (X87)result.Point.X - px, hy = (X87)result.Point.Y - py, hz = (X87)result.Point.Z - pz;
            X87 ddx = (X87)hx.F - ls[0], ddy = (X87)hy.F - ls[1], ddz = (X87)hz.F - ls[2];
            best = X87.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz).Value;
        }

        Vector3f d = new(((X87)e.X - s.X).F, ((X87)e.Y - s.Y).F, ((X87)e.Z - s.Z).F);
        Vector3f start = new(ls[0], ls[1], ls[2]);
        MeshRaySearch<X87> search = new(MeshRayDialect.Igi1)
        {
            Closest = best,
            IsHit = hadHit,
            PreviousFront = result.Hit && result.HasFrontMaterial,
            Point = new Vector3f(le[0], le[1], le[2]),
        };
        search.Box(start, search.Point);

        // The walk culls by the segment's own rebuilt tree; inv = 1 / |Le - Ls|² over register differences of floats.
        X87 lx = (X87)le[0] - ls[0], ly = (X87)le[1] - ls[1], lz = (X87)le[2] - ls[2];
        double inverse = ((X87)1.0 / (lz * lz + lx * lx + ly * ly)).Value;
        RetailModelRay.WalkFaces(Mesh, Vertices, Spheres, start, d, inverse, lenf, portals: true, search, mirror: Reverse, cullSpheres: true);
        if (!search.HitThisObject || (result.Hit && !(search.Closest < result.Distance)))
        {
            return;
        }

        RetailModelRay.FaceNormal(Mesh, search.Face, Reverse, out float nx, out float ny, out float nz, Vertices);
        float[] point = [search.Point.X, search.Point.Y, search.Point.Z];
        result.Struck = Owner;
        result.Point = new Vector3d(((X87)point[0] + px).Value, ((X87)point[1] + py).Value, ((X87)point[2] + pz).Value);
        result.NormalX = nx;
        result.NormalY = ny;
        result.NormalZ = nz;
        result.HasFrontMaterial = search.PreviousFront;
        result.HasSurface = !search.PreviousFront;
        result.SurfaceForce = 0f;
        result.Distance = search.Closest;
        result.Hit = true;
        result.Material = Mesh.GlobalMaterial[Mesh.Faces[(4 * search.Face) + 3]];
    }

    private static bool Straddles(X87 lo, X87 hi, double s, double e) => MeshRay.Straddles(MeshRayDialect.Igi1, lo.Value, hi.Value, s, e);

    /// <summary><c>0x4B32B0</c>: <c>sqrt(((dz·dz) + (dy·dy)) + (dx·dx))</c>, <c>d = a − b</c>.</summary>
    private static double Dist(Vector3d a, Vector3d b)
    {
        X87 dx = (X87)a.X - b.X, dy = (X87)a.Y - b.Y, dz = (X87)a.Z - b.Z;
        return X87.Sqrt(dz * dz + dy * dy + dx * dx).Value;
    }

    /// <summary><c>0x518130</c>: <c>{ t0, p0, a, b }</c> of the cubic.</summary>
    private static double[] Hermite(double p0, double p1, double t0, double t1)
    {
        X87 a = (X87)p0 + p0 - ((X87)p1 + p1) + t0 + t1;
        X87 b = (X87)p1 * 3.0 - (X87)p0 * 3.0 - ((X87)t0 + t0) - t1;
        return [t0, p0, a.Value, b.Value];
    }

    /// <summary><c>0x518190</c>.</summary>
    private static double Eval(double[] c, double t)
    {
        X87 t2 = (X87)t * t;
        X87 t3 = t2 * t;
        return (t3 * c[2] + t2 * c[3] + (X87)t * c[0] + c[1]).Value;
    }

    /// <summary><c>0x5181C0</c>.</summary>
    private static double Deriv(double[] c, double t)
    {
        X87 x = (X87)t * c[2] * t * 3.0;
        X87 y = (X87)t * c[3];
        return (x + (y + y) + c[0]).Value;
    }

    /// <summary><c>0x4B2D00</c>: yaw from <c>fpatan</c> (0 when x is 0), pitch from the clamped <c>asin</c>.</summary>
    private static void DirectionToAngles(float x, float y, float z, out float pitch, out float yaw)
    {
        yaw = x != 0f ? X87.ToFloat(Math.Atan2(y, x)) : 0f;
        X87 length = X87.Sqrt((X87)x * x + (X87)y * y + (X87)z * z);
        if (length.Value != 0d)
        {
            float sine = ((X87)z / length).F;
            sine = Math.Clamp(sine, -1f, 1f);
            pitch = X87.ToFloat(-Math.Asin(sine));
        }
        else
        {
            pitch = 0f;
        }
    }

    /// <summary>The roll <c>0x4B3C50</c> returns first: <c>atan2(M7, M8)</c> in [0, 2π).</summary>
    private static float Roll(float[] m)
    {
        double cy = X87.SquareRoot(((X87)m[0] * m[0] + (X87)m[3] * m[3]).Value);
        if (!(cy > 0.0001))
        {
            return 0f;
        }

        double roll = m[8] == 0f ? (m[7] < 0f ? -Math.PI / 2 : Math.PI / 2) : Math.Atan2(m[7], m[8]);
        if (roll < 0d)
        {
            roll += 2 * Math.PI;
        }

        return X87.ToFloat(roll);
    }

    private static float[] RotZ(float a)
    {
        float s = X87.ToFloat(Math.Sin(a)), c = X87.ToFloat(Math.Cos(a));
        return [c, -s, 0f, s, c, 0f, 0f, 0f, 1f];
    }

    private static float[] RotY(float a)
    {
        float s = X87.ToFloat(Math.Sin(a)), c = X87.ToFloat(Math.Cos(a));
        return [c, 0f, s, 0f, 1f, 0f, -s, 0f, c];
    }

    private static float[] RotX(float a)
    {
        float s = X87.ToFloat(Math.Sin(a)), c = X87.ToFloat(Math.Cos(a));
        return [1f, 0f, 0f, 0f, c, -s, 0f, s, c];
    }

    /// <summary><c>P[r][c] = (float)(((B[0][c]·A[r][0]) + (B[2][c]·A[r][2])) + (B[1][c]·A[r][1]))</c>.</summary>
    private static float[] Multiply(float[] a, float[] b)
    {
        float[] p = new float[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                p[(3 * r) + c] = ((X87)b[c] * a[3 * r] + (X87)b[6 + c] * a[(3 * r) + 2] + (X87)b[3 + c] * a[(3 * r) + 1]).F;
            }
        }

        return p;
    }

    /// <summary><c>0x49A050</c>, non-rigid branch: each vertex blended between the two slices around its X.</summary>
    private static float[] Bend(float[] source, float[] points, float[][] frames, int count, float x0, float x1, bool reverse)
    {
        float inverse = ((X87)1.0f / ((X87)x1 - x0)).F;
        float basis = -(reverse ? x1 : x0);
        X87 scale = (X87)(double)count * inverse;
        if (reverse)
        {
            scale = -scale;
        }

        float[] output = new float[source.Length];
        for (int i = 0; i < source.Length; i += 3)
        {
            float x = source[i], y = source[i + 1], z = source[i + 2];
            X87 u = ((X87)basis + x) * scale;
            int k = IgiMath.TruncToInt32(u.Value);
            if (k == count)
            {
                k = count - 1;
            }

            k = Math.Clamp(k, 0, count - 1);
            X87 f = u - k;
            if (!(f.Value >= 0d))
            {
                f = 0.0f;
            }
            else if (!(f.Value <= 1d))
            {
                f = 1.0f;
            }

            float[] m = frames[k], n = frames[k + 1];
            X87 ax = (X87)m[2] * z + (X87)m[1] * y + points[3 * k];
            float ay = ((X87)((X87)m[5] * z + (X87)m[4] * y).F + points[(3 * k) + 1]).F;
            float az = ((X87)((X87)m[8] * z + (X87)m[7] * y).F + points[(3 * k) + 2]).F;
            X87 bx = (X87)n[2] * z + (X87)n[1] * y + points[3 * (k + 1)];
            float by = ((X87)((X87)n[5] * z + (X87)n[4] * y).F + points[(3 * (k + 1)) + 1]).F;
            float bz = ((X87)((X87)n[8] * z + (X87)n[7] * y).F + points[(3 * (k + 1)) + 2]).F;
            output[i] = ((bx - ax) * f + ax).F;
            output[i + 1] = (((X87)by - ay) * f + ay).F;
            output[i + 2] = (((X87)bz - az) * f + az).F;
        }

        return output;
    }
}
