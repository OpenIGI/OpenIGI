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
/// disassembly's. The walk and the face test are shared with the spline segments' handler
/// (<see cref="MeshRaySearch{TFpu}"/>).
/// </para>
/// </remarks>
public static class Igi2RigidTrace
{
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

        for (int i = 0; i < trace.Count; i++)
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
        if (!MeshRay.Straddles(MeshRayDialect.Igi2, ((Fpu)p.X - r).Value, ((Fpu)r + p.X).Value, s.X, e.X)
            || !MeshRay.Straddles(MeshRayDialect.Igi2, ((Fpu)p.Y - r).Value, ((Fpu)r + p.Y).Value, s.Y, e.Y)
            || !MeshRay.Straddles(MeshRayDialect.Igi2, ((Fpu)p.Z - r).Value, ((Fpu)r + p.Z).Value, s.Z, e.Z))
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

        MeshRaySearch<Fpu> search = new(MeshRayDialect.Igi2)
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

        Faces faces = new(mesh, trace, start, d, lineLength, search);
        search.Walk(mesh.Arrays.Links, mesh.Arrays.Spheres, start, d, inverse, MeshSphereSums.ZxyChildXyzSibling, cull: true, ref faces);
        if (!search.HitThisObject)
        {
            return;
        }

        Record(o, mesh, trace, start, search, hit);
    }

    /// <summary>A world point into the model's frame with the end's grouping, divided by the scale.</summary>
    private static Vector3f ToLocal(Vector3d w, Vector3d p, float[] m, float isx, float isy, float isz)
    {
        Fpu dx = (Fpu)w.X - p.X, dy = (Fpu)w.Y - p.Y, dz = (Fpu)w.Z - p.Z;
        float x = ((dz * m[6]) + (dx * m[0]) + (dy * m[3])).F;
        float y = ((dz * m[7]) + (dx * m[1]) + (dy * m[4])).F;
        float z = ((dz * m[8]) + (dx * m[2]) + (dy * m[5])).F;
        return new Vector3f(((Fpu)isx * x).F, ((Fpu)isy * y).F, ((Fpu)isz * z).F);
    }

    /// <summary>Writes this model's hit into the line's result, if it is nearer than what is there.</summary>
    private static void Record(Igi2RigidObject o, Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3f start, MeshRaySearch<Fpu> search, Igi2Intersection hit)
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
        Vector3f local = Igi2MeshSearch.UnitNormal(
            (Fpu)v[i2] - v[i0],
            (Fpu)v[i2 + 1] - v[i0 + 1],
            (Fpu)v[i2 + 2] - v[i0 + 2],
            ((Fpu)v[i1] - v[i0]).F,
            ((Fpu)v[i1 + 1] - v[i0 + 1]).F,
            (Fpu)v[i1 + 2] - v[i0 + 2]);
        float nx = local.X, ny = local.Y, nz = local.Z;
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

        hit.HitObject = o;
        hit.Position = new Vector3d(((Fpu)wx + p.X).Value, ((Fpu)wy + p.Y).Value, (wz + p.Z).Value);
        hit.LocalPosition = point;
        hit.LocalNormal = local;
        hit.Normal = normal;
        hit.IsHit = true;
        hit.EnterMaterial = search.PreviousFront ? levelMaterial : null;
        hit.LeaveMaterial = search.PreviousFront ? null : levelMaterial;
        hit.Distance = closest;
        hit.IsDoubleFace = search.IsDoubleFace;
        hit.Face = f;
        hit.Portal = (uint)material < (uint)mesh.Portals.Length ? mesh.Portals[material] : Igi2MeshSearch.None;
        hit.HitUV = search.HitUV;
        hit.Light = Igi2MeshSearch.Light(
            o.IsBuildingMesh && mesh.Lightmaps[f] != Igi2MeshSearch.None && o.IsLightmapped,
            normal,
            trace.Lighting);
    }

    /// <summary>One face against the line: the shared crossing, then this handler's portal, tie and ignored-material rules.</summary>
    private readonly struct Faces(Igi2CollisionMesh mesh, Igi2LineTrace trace, Vector3f s, Vector3f d, float lineLength, MeshRaySearch<Fpu> search)
        : IMeshFaceVisitor
    {
        public void Visit(int face)
        {
            int[] faces = mesh.Arrays.Faces;
            int material = faces[(4 * face) + 3];
            int portal = (uint)material < (uint)mesh.Portals.Length ? mesh.Portals[material] : Igi2MeshSearch.None;
            if (portal != Igi2MeshSearch.None && !trace.TracePortals)
            {
                return;
            }

            int i0 = 3 * faces[4 * face], i1 = 3 * faces[(4 * face) + 1], i2 = 3 * faces[(4 * face) + 2];
            if (!search.Cross(mesh.Arrays.Vertices, i0, i1, i2, s, d, flip: false, MeshPlaneSum.YXZ, out MeshFaceCrossing<Fpu> crossing))
            {
                return;
            }

            // A portal stops a line only from its front.
            if (portal != Igi2MeshSearch.None && crossing.Dot.Value > 0.0)
            {
                return;
            }

            Fpu distance = (Fpu)crossing.T * lineLength;
            bool front = crossing.Dot.Value < 0.0;
            if (!search.Accept(distance, front))
            {
                return;
            }

            int row = (uint)material < (uint)mesh.Rows.Length ? mesh.Rows[material] : material;
            if (row == trace.IgnoreMaterial)
            {
                return;
            }

            search.HitUV = (crossing.U.F, crossing.V.F);
            search.Take(distance, front, crossing.Point, face, s);
        }
    }
}
