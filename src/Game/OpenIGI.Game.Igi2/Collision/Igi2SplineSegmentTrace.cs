using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// Lines against a straight spline segment: the linear path of <c>SplineObjSegment_TraceLinesHandler</c>
/// (<c>igi2 0x5B7390</c>), with the arithmetic the client's FPU performs.
/// </summary>
/// <remarks>
/// <para>
/// The handler answers both the line and the ground events. Each line is taken into the segment model's own frame
/// by the inverse of the stretch matrix and walked through the model's own sphere tree and faces, the walk and face
/// test the solid models use (<see cref="Igi2MeshSearch"/>) with this handler's sums: every sphere sums its squared
/// distance x, y, z, and the plane distance sums z, y, x. Portal faces and the ignored material are not consulted.
/// </para>
/// <para>
/// A hit is taken back into the world by the stretch matrix and measured from the line's start there; its normal is
/// the struck face's edges stretched, crossed and normalised, reversed on a flipped segment. Every hit reports a
/// double face, and the sector, portal and barycentric fields are left as they were.
/// </para>
/// <para>
/// Two things are kept that look like slips. An earlier hit is taken into the model's frame by the stretch matrix's
/// transpose rather than its inverse, and from the segment's position rather than the stretch's middle, so the box
/// it shortens and the distance a face must beat are only roughly where that hit is. And a line whose bounds miss
/// the segment ends the whole call, not just that line.
/// </para>
/// </remarks>
public static class Igi2SplineSegmentTrace
{
    /// <summary>Traces every line of a batch against one segment, replacing each line's result where the segment is nearer.</summary>
    /// <param name="segment">The segment.</param>
    /// <param name="trace">The lines and their running results.</param>
    public static void Trace(Igi2SplineSegment segment, Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(trace);

        Igi2CollisionMesh? mesh = segment.Mesh(trace.Mesh);
        if (mesh is null || mesh.Arrays.FaceCount == 0)
        {
            return;
        }

        if (trace.Includes is { } includes && !includes(segment.Owner))
        {
            return;
        }

        for (int i = 0; i < trace.Count; i++)
        {
            if (!Line(segment, mesh, trace, trace.Starts[i], trace.Ends[i], trace.Intersections[i]))
            {
                return;
            }
        }
    }

    /// <summary>One line; <see langword="false"/> when its bounds miss the segment, which ends the call.</summary>
    private static bool Line(Igi2SplineSegment o, Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3d s, Vector3d e, Igi2Intersection hit)
    {
        Vector3d p = o.Position;
        float r = o.Radius;
        if (!Igi2MeshSearch.Straddles(((Fpu)p.X - r).Value, ((Fpu)r + p.X).Value, s.X, e.X)
            || !Igi2MeshSearch.Straddles(((Fpu)p.Y - r).Value, ((Fpu)r + p.Y).Value, s.Y, e.Y)
            || !Igi2MeshSearch.Straddles(((Fpu)p.Z - r).Value, ((Fpu)r + p.Z).Value, s.Z, e.Z))
        {
            return false;
        }

        // The line into the model's frame: the inverse, then the extent's middle. The two ends group their y sums
        // differently.
        IReadOnlyList<float> m = o.ToMesh;
        float middle = o.Middle;
        Fpu sx = (Fpu)s.X - p.X, sy = (Fpu)s.Y - p.Y, sz = (Fpu)s.Z - p.Z;
        float rx = ((sz * m[2]) + (sy * m[1]) + (sx * m[0])).F;
        float ry = ((sy * m[4]) + (sx * m[3]) + (sz * m[5])).F;
        float rz = ((sx * m[6]) + (sz * m[8]) + (sy * m[7])).F;
        Vector3f start = new(((Fpu)middle + rx).F, ry, rz);

        Fpu ex = (Fpu)e.X - p.X, ey = (Fpu)e.Y - p.Y, ez = (Fpu)e.Z - p.Z;
        float tx = ((ez * m[2]) + (ey * m[1]) + (ex * m[0])).F;
        float ty = ((ex * m[3]) + (ez * m[5]) + (ey * m[4])).F;
        float tz = ((ex * m[6]) + (ez * m[8]) + (ey * m[7])).F;
        Vector3f end = new(((Fpu)middle + tx).F, ty, tz);

        float lineLength = Distance(s, e).F;
        Igi2MeshSearch search = new()
        {
            IsHit = hit.IsHit,
            PreviousFront = hit.IsHit && hit.EnterMaterial is not null,
            Point = end,
        };

        // An earlier hit, by the stretch's transpose from the segment's position; the segment's scale is unit.
        bool wasHit = hit.IsHit;
        Vector3f partner = end;
        if (wasHit)
        {
            IReadOnlyList<float> o9 = o.Orientation;
            Fpu hx = (Fpu)hit.Position.X - p.X, hy = (Fpu)hit.Position.Y - p.Y, hz = (Fpu)hit.Position.Z - p.Z;
            partner = new Vector3f(
                ((hz * o9[6]) + (hy * o9[3]) + (hx * o9[0])).F,
                ((hz * o9[7]) + (hy * o9[4]) + (hx * o9[1])).F,
                ((hz * o9[8]) + (hy * o9[5]) + (hx * o9[2])).F);
            Fpu cy = (Fpu)partner.Y - start.Y, cx = (Fpu)partner.X - start.X, cz = (Fpu)partner.Z - start.Z;
            search.Closest = Fpu.Sqrt((cz * cz) + (cx * cx) + (cy * cy)).Value;
        }

        search.Box(start, partner);
        Fpu ddy = (Fpu)end.Y - start.Y, ddx = (Fpu)end.X - start.X, ddz = (Fpu)end.Z - start.Z;
        Fpu inverse = (Fpu)1.0 / ((ddz * ddz) + (ddx * ddx) + (ddy * ddy));
        Vector3f d = new(ddx.F, ddy.F, ddz.F);
        if (mesh.Arrays.SphereCount == 0)
        {
            return true;
        }

        Faces faces = new(mesh.Arrays, start, d, lineLength, search);
        search.Walk(mesh.Arrays, start, d, inverse, uniformSums: true, ref faces);
        if (search.HitThisObject)
        {
            Record(o, mesh, trace, s, wasHit, search, hit);
        }

        return true;
    }

    /// <summary>Writes the segment's hit into the line's result, if it is nearer than what was there before the segment.</summary>
    private static void Record(Igi2SplineSegment o, Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3d s, bool wasHit, Igi2MeshSearch search, Igi2Intersection hit)
    {
        // The crossing back into the world: from the middle, stretched, and added to the position at 24 bits.
        IReadOnlyList<float> m = o.Orientation;
        Vector3f point = search.Point;
        Fpu lx = (Fpu)point.X - o.Middle;
        float ly = point.Y, lz = point.Z;
        Fpu wx = ((Fpu)ly * m[1]) + (lx * m[0]) + ((Fpu)lz * m[2]);
        float wy = (((Fpu)lz * m[5]) + ((Fpu)ly * m[4]) + (lx * m[3])).F;
        float wz = (((Fpu)ly * m[7]) + (lx * m[6]) + ((Fpu)lz * m[8])).F;
        Vector3d p = o.Position;
        Vector3d world = new((wx + p.X).Value, ((Fpu)wy + p.Y).Value, ((Fpu)wz + p.Z).Value);

        double closest = Distance(s, world).Value;
        if (wasHit && !(closest < hit.Distance))
        {
            return;
        }

        int[] faces = mesh.Arrays.Faces;
        float[] v = mesh.Arrays.Vertices;
        int f = search.Face;
        int i0 = 3 * faces[4 * f], i1 = 3 * faces[(4 * f) + 1], i2 = 3 * faces[(4 * f) + 2];
        int material = faces[(4 * f) + 3];
        int levelMaterial = mesh.Arrays.GlobalMaterial[material];

        // The struck face's two edges, stretched into the world, crossed and normalised.
        Fpu a2x = (Fpu)v[i2] - v[i0], a2y = (Fpu)v[i2 + 1] - v[i0 + 1], a2z = (Fpu)v[i2 + 2] - v[i0 + 2];
        float b1x = ((Fpu)v[i1] - v[i0]).F, b1y = ((Fpu)v[i1 + 1] - v[i0 + 1]).F, b1z = ((Fpu)v[i1 + 2] - v[i0 + 2]).F;
        float r2x = ((a2y * m[1]) + (a2z * m[2]) + (a2x * m[0])).F;
        float r2y = ((a2z * m[5]) + (a2x * m[3]) + (a2y * m[4])).F;
        Fpu r2z = (a2y * m[7]) + (a2z * m[8]) + (a2x * m[6]);
        Fpu r1x = ((Fpu)b1y * m[1]) + ((Fpu)b1z * m[2]) + ((Fpu)b1x * m[0]);
        Fpu r1y = ((Fpu)b1z * m[5]) + ((Fpu)b1x * m[3]) + ((Fpu)b1y * m[4]);
        Fpu r1z = ((Fpu)b1y * m[7]) + ((Fpu)b1z * m[8]) + ((Fpu)b1x * m[6]);
        Vector3f normal = Igi2MeshSearch.UnitNormal(r2x, r2y, r2z, r1x.F, r1y.F, r1z);
        if (o.IsFlipped)
        {
            normal = new Vector3f(-normal.X, -normal.Y, -normal.Z);
        }

        hit.HitObject = o;
        hit.Position = world;
        hit.LocalPosition = new Vector3f(((Fpu)world.X - p.X).F, ((Fpu)world.Y - p.Y).F, ((Fpu)world.Z - p.Z).F);
        hit.LocalNormal = normal;
        hit.Normal = normal;
        hit.IsHit = true;
        hit.EnterMaterial = search.PreviousFront ? levelMaterial : null;
        hit.LeaveMaterial = search.PreviousFront ? null : levelMaterial;
        hit.IsDoubleFace = true;
        hit.Distance = closest;
        hit.Face = f;
        hit.Light = Igi2MeshSearch.Light(
            o.IsBuildingMesh && mesh.Lightmaps[f] != Igi2MeshSearch.None && o.IsLightmapped,
            normal,
            trace.Lighting);
    }

    /// <summary><c>Real64x3_Distance</c> (<c>igi2 0x5624C0</c>): the first point less the second, squared z, y, x.</summary>
    private static Fpu Distance(Vector3d a, Vector3d b)
    {
        Fpu dx = (Fpu)a.X - b.X, dy = (Fpu)a.Y - b.Y, dz = (Fpu)a.Z - b.Z;
        return Fpu.Sqrt((dz * dz) + (dy * dy) + (dx * dx));
    }

    /// <summary>One face against the line: the shared crossing and the tie rules, nothing skipped.</summary>
    private readonly struct Faces(RetailRayMesh mesh, Vector3f s, Vector3f d, float lineLength, Igi2MeshSearch search)
        : IIgi2FaceVisitor
    {
        public void Visit(int face)
        {
            int[] faces = mesh.Faces;
            int i0 = 3 * faces[4 * face], i1 = 3 * faces[(4 * face) + 1], i2 = 3 * faces[(4 * face) + 2];
            if (!search.Cross(mesh.Vertices, i0, i1, i2, s, d, flip: false, Igi2PlaneSum.ZYX, out Igi2FaceCrossing crossing))
            {
                return;
            }

            Fpu distance = (Fpu)crossing.T * lineLength;
            bool front = crossing.Dot.Value < 0.0;
            if (search.Accept(distance, front))
            {
                search.Take(distance, front, crossing.Point, face, s);
            }
        }
    }
}
