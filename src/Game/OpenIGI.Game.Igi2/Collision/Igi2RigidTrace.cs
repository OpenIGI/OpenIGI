using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// Lines against a placed solid model: <c>RigidDynCubeObj_TraceLinesHandler</c> (<c>igi2 0x58B3B0</c>), with the
/// arithmetic the client's FPU performs.
/// </summary>
/// <remarks>
/// <para>
/// The same search as the first game's model ray (<see cref="RetailModelRay"/>): each line is taken into the
/// model's frame, a padded box around it culls the sphere tree and the faces, and each face is met with a plane
/// test and a barycentric test on the face's dominant axis. A face a little behind the best is still taken if it
/// faces the line and the best does not, so a line through two coincident faces reports the one it enters.
/// </para>
/// <para>
/// What the sequel changed: the box pad and the tie window are 40.96 units (one centimetre) where the first game
/// used a tenth of a unit; portal faces stop a line only when asked, and then only from their front; one material
/// row can be ignored; a hit reports its barycentric position and how lit the surface is; and every operation
/// rounds to 24 bits, toward zero. The order of each sum and where each value is stored as a float are the
/// disassembly's.
/// </para>
/// </remarks>
public static class Igi2RigidTrace
{
    private const float Pad = 40.96f;
    private const double TieWindow = 40.96;
    private const int None = 0xFFFF;

    /// <summary>Traces every line of a batch against one model, replacing each line's result where this model is nearer.</summary>
    /// <param name="model">The model.</param>
    /// <param name="trace">The lines and their running results.</param>
    public static void Trace(Igi2RigidObject model, Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(trace);

        Igi2CollisionMesh? mesh = model.Mesh(trace.Mesh);
        if (mesh is null || mesh.Arrays.FaceCount == 0)
        {
            return;
        }

        if (trace.Includes is { } includes && !includes(model.Owner))
        {
            return;
        }

        for (int i = 0; i < trace.Starts.Length; i++)
        {
            Line(model, mesh, trace, trace.Starts[i], trace.Ends[i], trace.Intersections[i]);
        }
    }

    private static void Line(Igi2RigidObject o, Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3d s, Vector3d e, Igi2Intersection hit)
    {
        float[] m = o.Orientation;
        Vector3d p = o.Position;
        float sx = o.Scale.X, sy = o.Scale.Y, sz = o.Scale.Z;

        // The broad test, against the model's radius scaled by its largest scale.
        float largest = sy > sz ? sy : sz;
        float smax = largest < sx ? sx : largest;
        float r = ((Fpu)o.Radius * smax).F;
        if (!Straddles(((Fpu)p.X - r).Value, ((Fpu)r + p.X).Value, s.X, e.X)
            || !Straddles(((Fpu)p.Y - r).Value, ((Fpu)r + p.Y).Value, s.Y, e.Y)
            || !Straddles(((Fpu)p.Z - r).Value, ((Fpu)r + p.Z).Value, s.Z, e.Z))
        {
            return;
        }

        // The line into the model's frame. The start and the end group their sums differently.
        float isx = ((Fpu)1f / sx).F, isy = ((Fpu)1f / sy).F, isz = ((Fpu)1f / sz).F;
        Fpu dx = (Fpu)s.X - p.X, dy = (Fpu)s.Y - p.Y, dz = (Fpu)s.Z - p.Z;
        float rx = ((dz * m[6]) + (dx * m[0]) + (dy * m[3])).F;
        float ry = ((dy * m[4]) + (dz * m[7]) + (dx * m[1])).F;
        float rz = ((dz * m[8]) + (dx * m[2]) + (dy * m[5])).F;
        Vector3f start = new(((Fpu)isx * rx).F, ((Fpu)isy * ry).F, ((Fpu)isz * rz).F);
        Vector3f end = ToLocal(e, p, m, isx, isy, isz);

        Fpu ddy = (Fpu)end.Y - start.Y;
        Fpu ddx = (Fpu)end.X - start.X;
        Fpu ddz = (Fpu)end.Z - start.Z;
        Fpu length2 = (ddz * ddz) + (ddx * ddx) + (ddy * ddy);
        float lineLength = Fpu.Sqrt(length2).F;

        Search search = new()
        {
            IsHit = hit.IsHit,
            PreviousFront = hit.IsHit && hit.EnterMaterial is not null,
            Point = end,
        };

        // Against an earlier hit the box reaches only as far as it, and a face must come nearer than it.
        Vector3f partner = end;
        if (search.IsHit)
        {
            partner = ToLocal(hit.Position, p, m, isx, isy, isz);
            Fpu cy = (Fpu)partner.Y - start.Y, cx = (Fpu)partner.X - start.X, cz = (Fpu)partner.Z - start.Z;
            search.Closest = Fpu.Sqrt((cz * cz) + (cx * cx) + (cy * cy)).F;
        }

        search.Box(start, partner);
        Fpu inverse = (Fpu)1.0 / length2;
        Vector3f d = new(ddx.F, ddy.F, ddz.F);

        if (mesh.Arrays.SphereCount == 0)
        {
            return;
        }

        Walk(mesh, trace, start, d, inverse, lineLength, search);
        if (!search.HitThisObject)
        {
            return;
        }

        Record(o, mesh, trace, start, search, hit);
    }

    /// <summary><c>(lo ≤ S or lo ≤ E) and (hi ≥ S or hi ≥ E)</c>, an unordered comparison passing.</summary>
    private static bool Straddles(double lo, double hi, double start, double end) =>
        (!(lo > start) || !(lo > end)) && (!(hi < start) || !(hi < end));

    /// <summary>A world point into the model's frame with the end's grouping, divided by the scale.</summary>
    private static Vector3f ToLocal(Vector3d w, Vector3d p, float[] m, float isx, float isy, float isz)
    {
        Fpu dx = (Fpu)w.X - p.X, dy = (Fpu)w.Y - p.Y, dz = (Fpu)w.Z - p.Z;
        float x = ((dz * m[6]) + (dx * m[0]) + (dy * m[3])).F;
        float y = ((dz * m[7]) + (dx * m[1]) + (dy * m[4])).F;
        float z = ((dz * m[8]) + (dx * m[2]) + (dy * m[5])).F;
        return new Vector3f(((Fpu)isx * x).F, ((Fpu)isy * y).F, ((Fpu)isz * z).F);
    }

    /// <summary>The running state of one line's search through one model.</summary>
    private sealed class Search
    {
        public bool IsHit { get; set; }

        public bool PreviousFront { get; set; }

        public bool HitThisObject { get; set; }

        public bool IsDoubleFace { get; set; }

        public double Closest { get; set; }

        public Vector3f Point { get; set; }

        public int Face { get; set; } = -1;

        public (float U, float V) HitUV { get; set; }

        public Vector3f Min { get; private set; }

        public Vector3f Max { get; private set; }

        /// <summary>The search box: the start and a point, padded by a centimetre either way.</summary>
        public void Box(Vector3f a, Vector3f b)
        {
            Min = new Vector3f(Lower(a.X, b.X), Lower(a.Y, b.Y), Lower(a.Z, b.Z));
            Max = new Vector3f(Upper(a.X, b.X), Upper(a.Y, b.Y), Upper(a.Z, b.Z));
        }

        private static float Lower(float a, float b) => ((Fpu)(a < b ? a : b) - Pad).F;

        private static float Upper(float a, float b) => ((Fpu)(a > b ? a : b) + Pad).F;
    }

    /// <summary>The sphere-tree walk: depth first, each node culled by the line and the box, its faces tested.</summary>
    /// <remarks>
    /// A child that fails its test still has its own faces tested before the walk moves to its sibling — the
    /// first game's quirk, kept. The root is never tested. Siblings and children group their distance sums
    /// differently.
    /// </remarks>
    private static void Walk(Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3f start, Vector3f d, Fpu inverse, float lineLength, Search search)
    {
        int[] links = mesh.Arrays.Links;
        Span<int> stack = stackalloc int[100];
        int level = 0;
        int node = 0;
        stack[0] = 0;
        bool first = true;
        while (true)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                int next = links[(4 * node) + 3];
                if (next == 0)
                {
                    level--;
                    if (level < 0)
                    {
                        return;
                    }

                    node = stack[level];
                    continue;
                }

                node = next;
                stack[level] = node;
                if (!SphereOk(mesh, node, start, d, inverse, search, sibling: true))
                {
                    continue;
                }
            }

            while (true)
            {
                int child = links[(4 * node) + 2];
                if (child == 0)
                {
                    break;
                }

                node = child;
                stack[++level] = node;
                if (!SphereOk(mesh, node, start, d, inverse, search, sibling: false))
                {
                    break;
                }
            }

            int count = links[(4 * node) + 1];
            int firstFace = links[4 * node];
            for (int f = firstFace; f < firstFace + count; f++)
            {
                Face(mesh, trace, f, start, d, lineLength, search);
            }
        }
    }

    private static bool SphereOk(Igi2CollisionMesh mesh, int n, Vector3f start, Vector3f d, Fpu inverse, Search search, bool sibling)
    {
        float[] sp = mesh.Arrays.Spheres;
        float cx = sp[4 * n], cy = sp[(4 * n) + 1], cz = sp[(4 * n) + 2], r = sp[(4 * n) + 3];
        Fpu ex = (Fpu)cx - start.X, ey = (Fpu)cy - start.Y, ez = (Fpu)cz - start.Z;
        Fpu projection = (ez * d.Z) + (ey * d.Y) + (ex * d.X);
        Fpu c2 = sibling ? (ex * ex) + (ey * ey) + (ez * ez) : (ez * ez) + (ex * ex) + (ey * ey);
        Fpu d2 = c2 - (projection * projection * inverse);
        if (((Fpu)r * r).Value < d2.Value)
        {
            return false;
        }

        Vector3f lo = search.Min, hi = search.Max;
        if (((Fpu)r + cx).Value < lo.X || ((Fpu)r + cy).Value < lo.Y || ((Fpu)r + cz).Value < lo.Z)
        {
            return false;
        }

        return !(((Fpu)cx - r).Value > hi.X) && !(((Fpu)cy - r).Value > hi.Y) && !(((Fpu)cz - r).Value > hi.Z);
    }

    /// <summary>One face against the line: the box, the plane, the dominant-axis barycentric test, and the tie rules.</summary>
    private static void Face(Igi2CollisionMesh mesh, Igi2LineTrace trace, int f, Vector3f s, Vector3f d, float lineLength, Search search)
    {
        int[] faces = mesh.Arrays.Faces;
        float[] v = mesh.Arrays.Vertices;
        int i0 = 3 * faces[4 * f], i1 = 3 * faces[(4 * f) + 1], i2 = 3 * faces[(4 * f) + 2];
        int material = faces[(4 * f) + 3];
        int portal = (uint)material < (uint)mesh.Portals.Length ? mesh.Portals[material] : None;
        if (portal != None && !trace.TracePortals)
        {
            return;
        }

        Vector3f lo = search.Min, hi = search.Max;
        for (int k = 0; k < 3; k++)
        {
            float bound = Component(lo, k);
            if (bound > v[i0 + k] && bound > v[i1 + k] && bound > v[i2 + k])
            {
                return;
            }
        }

        for (int k = 0; k < 3; k++)
        {
            float bound = Component(hi, k);
            if (bound < v[i0 + k] && bound < v[i1 + k] && bound < v[i2 + k])
            {
                return;
            }
        }

        float v0x = v[i0], v0y = v[i0 + 1], v0z = v[i0 + 2];
        float e0x = ((Fpu)v[i2] - v0x).F, e0y = ((Fpu)v[i2 + 1] - v0y).F, e0z = ((Fpu)v[i2 + 2] - v0z).F;
        float e1x = ((Fpu)v[i1] - v0x).F, e1y = ((Fpu)v[i1 + 1] - v0y).F, e1z = ((Fpu)v[i1 + 2] - v0z).F;
        float nx = (((Fpu)e1z * e0y) - ((Fpu)e1y * e0z)).F;
        float nyp = (((Fpu)e1z * e0x) - ((Fpu)e1x * e0z)).F;
        float ny = -nyp;
        float nz = (((Fpu)e1y * e0x) - ((Fpu)e1x * e0y)).F;

        Fpu dot = ((Fpu)nz * d.Z) + ((Fpu)ny * d.Y) + ((Fpu)nx * d.X);
        if (dot.Value == 0.0)
        {
            return;
        }

        // A portal stops a line only from its front.
        if (portal != None && dot.Value > 0.0)
        {
            return;
        }

        Fpu t = ((((Fpu)v0y - s.Y) * ny) + (((Fpu)v0x - s.X) * nx) + (((Fpu)v0z - s.Z) * nz)) / dot;
        float tf = t.F;
        if (t.Value < 0.0 || tf > 1f)
        {
            return;
        }

        float px = (((Fpu)tf * d.X) + s.X).F, py = (((Fpu)tf * d.Y) + s.Y).F, pz = (((Fpu)tf * d.Z) + s.Z).F;
        float ax = nx < 0f ? -nx : nx, ay = ny < 0f ? -ny : ny, az = nz < 0f ? -nz : nz;

        Fpu vv;
        Fpu u;
        if (ax > ay && ax > az)
        {
            if (nx == 0f)
            {
                return;
            }

            float q2 = ((Fpu)pz - v0z).F, q1 = ((Fpu)py - v0y).F;
            vv = (((Fpu)q2 * e0y) - ((Fpu)q1 * e0z)) / nx;
            if (!InUnit(vv))
            {
                return;
            }

            if (Abs(e0y) > Abs(e0z))
            {
                u = ((Fpu)q1 - ((Fpu)e1y * vv)) / e0y;
            }
            else if (e0z == 0f)
            {
                return;
            }
            else
            {
                u = ((Fpu)q2 - ((Fpu)e1z * vv)) / e0z;
            }
        }
        else if (ay > az)
        {
            if (nyp == 0f)
            {
                return;
            }

            float q2 = ((Fpu)pz - v0z).F, q0 = ((Fpu)px - v0x).F;
            vv = (((Fpu)q2 * e0x) - ((Fpu)q0 * e0z)) / nyp;
            if (!InUnit(vv))
            {
                return;
            }

            if (Abs(e0x) > Abs(e0z))
            {
                u = ((Fpu)q0 - ((Fpu)e1x * vv)) / e0x;
            }
            else if (e0z == 0f)
            {
                return;
            }
            else
            {
                u = ((Fpu)q2 - ((Fpu)e1z * vv)) / e0z;
            }
        }
        else
        {
            if (nz == 0f)
            {
                return;
            }

            float q1 = ((Fpu)py - v0y).F, q0 = ((Fpu)px - v0x).F;
            vv = (((Fpu)q1 * e0x) - ((Fpu)q0 * e0y)) / nz;
            if (!InUnit(vv))
            {
                return;
            }

            if (Abs(e0x) > Abs(e0y))
            {
                u = ((Fpu)q0 - ((Fpu)e1x * vv)) / e0x;
            }
            else if (e0y == 0f)
            {
                return;
            }
            else
            {
                u = ((Fpu)q1 - ((Fpu)e1y * vv)) / e0y;
            }
        }

        if (u.Value < 0.0 || (vv + u).Value > 1.0)
        {
            return;
        }

        Fpu distance = (Fpu)tf * lineLength;
        bool front = dot.Value < 0.0;
        if (!search.IsHit || (distance + TieWindow).Value < search.Closest)
        {
            search.IsDoubleFace = false;
        }
        else if (distance.Value < search.Closest)
        {
            search.IsDoubleFace = true;
            if (!front && search.PreviousFront)
            {
                return;
            }
        }
        else
        {
            if (!((distance - TieWindow).Value < search.Closest))
            {
                return;
            }

            search.IsDoubleFace = true;
            if (!front || search.PreviousFront)
            {
                return;
            }
        }

        int row = (uint)material < (uint)mesh.Rows.Length ? mesh.Rows[material] : material;
        if (row == trace.IgnoreMaterial)
        {
            return;
        }

        search.HitUV = (u.F, vv.F);
        search.Closest = distance.Value;
        search.PreviousFront = front;
        search.Point = new Vector3f(px, py, pz);
        search.Face = f;
        search.HitThisObject = true;
        search.IsHit = true;
        search.Box(s, search.Point);
    }

    /// <summary>A barycentric weight in the unit interval: not below zero, not above one; an unordered value passes.</summary>
    private static bool InUnit(Fpu w) => !(w.Value < 0.0) && !(w.Value > 1.0);

    private static float Abs(float a) => a < 0f ? -a : a;

    private static float Component(Vector3f v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>Writes this model's hit into the line's result, if it is nearer than what is there.</summary>
    private static void Record(Igi2RigidObject o, Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3f start, Search search, Igi2Intersection hit)
    {
        float[] m = o.Orientation;
        float sx = o.Scale.X, sy = o.Scale.Y, sz = o.Scale.Z;
        Vector3f point = search.Point;

        double closest = search.Closest;
        if (sx != 1f || sy != 1f || sz != 1f)
        {
            Fpu hx = (Fpu)point.X * sx, hy = (Fpu)point.Y * sy;
            float hz = ((Fpu)point.Z * sz).F;
            float tsx = ((Fpu)start.X * sx).F;
            Fpu tsy = (Fpu)start.Y * sy;
            float tsz = ((Fpu)start.Z * sz).F;
            Fpu cy = tsy - hy, cx = (Fpu)tsx - hx, cz = (Fpu)tsz - hz;
            closest = Fpu.Sqrt((cz * cz) + (cx * cx) + (cy * cy)).Value;
        }

        if (hit.IsHit && !(closest < hit.Distance))
        {
            return;
        }

        int[] faces = mesh.Arrays.Faces;
        float[] v = mesh.Arrays.Vertices;
        int f = search.Face;
        int i0 = 3 * faces[4 * f], i1 = 3 * faces[(4 * f) + 1], i2 = 3 * faces[(4 * f) + 2];
        int material = faces[(4 * f) + 3];
        int levelMaterial = mesh.Arrays.GlobalMaterial[material];

        // The face's normal again, normalised, in the model's frame and then the world's.
        Fpu a2x = (Fpu)v[i2] - v[i0], a2y = (Fpu)v[i2 + 1] - v[i0 + 1], a2z = (Fpu)v[i2 + 2] - v[i0 + 2];
        float b1x = ((Fpu)v[i1] - v[i0]).F, b1y = ((Fpu)v[i1 + 1] - v[i0 + 1]).F;
        Fpu b1z = (Fpu)v[i1 + 2] - v[i0 + 2];
        float nx = ((b1z * a2y) - ((Fpu)b1y * a2z)).F;
        float ny = (-((b1z * a2x) - ((Fpu)b1x * a2z))).F;
        float nz = (((Fpu)b1y * a2x) - ((Fpu)b1x * a2y)).F;
        if (nx != 0f || ny != 0f || nz != 0f)
        {
            Fpu length = Fpu.Sqrt(((Fpu)nx * nx) + ((Fpu)ny * ny) + ((Fpu)nz * nz));
            Fpu inverse = (Fpu)1.0 / length;
            nx = ((Fpu)nx * inverse).F;
            ny = ((Fpu)ny * inverse).F;
            nz = ((Fpu)nz * inverse).F;
        }

        Vector3f normal = new(
            (((Fpu)m[2] * nz) + ((Fpu)ny * m[1]) + ((Fpu)m[0] * nx)).F,
            (((Fpu)m[4] * ny) + ((Fpu)m[3] * nx) + ((Fpu)nz * m[5])).F,
            (((Fpu)m[8] * nz) + ((Fpu)ny * m[7]) + ((Fpu)m[6] * nx)).F);

        // The hit back into the world: scaled, turned, and added to the position at 24 bits.
        Fpu wx0 = (Fpu)point.X * sx, wy0 = (Fpu)point.Y * sy, wz0 = (Fpu)point.Z * sz;
        float wx = ((wz0 * m[2]) + (wx0 * m[0]) + (wy0 * m[1])).F;
        float wy = ((wz0 * m[5]) + (wx0 * m[3]) + (wy0 * m[4])).F;
        Fpu wz = (wz0 * m[8]) + (wx0 * m[6]) + (wy0 * m[7]);
        Vector3d p = o.Position;

        hit.HitObject = o.Owner;
        hit.Position = new Vector3d(((Fpu)wx + p.X).Value, ((Fpu)wy + p.Y).Value, (wz + p.Z).Value);
        hit.LocalPosition = point;
        hit.LocalNormal = new Vector3f(nx, ny, nz);
        hit.Normal = normal;
        hit.IsHit = true;
        hit.EnterMaterial = search.PreviousFront ? levelMaterial : null;
        hit.LeaveMaterial = search.PreviousFront ? null : levelMaterial;
        hit.Distance = closest;
        hit.IsDoubleFace = search.IsDoubleFace;
        hit.Face = f;
        hit.Portal = (uint)material < (uint)mesh.Portals.Length ? mesh.Portals[material] : None;
        hit.HitUV = search.HitUV;
        hit.Light = Light(o, mesh, f, normal, trace.Lighting);
    }

    /// <summary>How lit the struck surface is: zero on a lightmapped building face, else ambient plus the sun on the face, averaged.</summary>
    private static float Light(Igi2RigidObject o, Igi2CollisionMesh mesh, int face, Vector3f normal, IIgi2SceneLighting? lighting)
    {
        if (o.IsBuildingMesh && mesh.Lightmaps[face] != None && o.IsLightmapped)
        {
            return 0f;
        }

        Vector3f ambient = lighting?.Ambient ?? default;
        float lx = ambient.X, ly = ambient.Y;
        Fpu lz = ambient.Z;
        if (lighting is not null && lighting.TryGetSun(out Vector3f direction, out Vector3f colour))
        {
            Fpu dot = ((Fpu)direction.Z * normal.Z) + ((Fpu)direction.Y * normal.Y) + ((Fpu)normal.X * direction.X);
            if (dot.Value > 0.0)
            {
                lx = ((dot * colour.X) + lx).F;
                ly = ((dot * colour.Y) + ly).F;
                lz = (dot * colour.Z) + lz;
            }
        }

        return ((lz + ly + lx) * 0.33333334f).F;
    }
}
