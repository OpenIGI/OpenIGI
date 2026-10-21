using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// A line of sight against a placed solid model: <c>RigidDynCubeObj_LineOfSightHandler</c> (<c>igi2 0x58CCA0</c>), with the
/// arithmetic the client's FPU performs.
/// </summary>
/// <remarks>
/// <para>
/// The trace's search (<see cref="Igi2RigidTrace"/>) with its own sums: the line into the model's frame, a box around it
/// with no pad, the same sphere-tree walk summing every node alike, and the same face test with the plane's products
/// summed x, z, y. Portal faces are never met, nor is a model once the sight has been stopped.
/// </para>
/// <para>
/// Every face the line crosses counts, not only the nearest: each marks the line crossed, keeps the nearer material, and
/// lets through its material's transparency when entered from the front; a face of no material, or of one that lets
/// nothing through, stops the line and the search.
/// </para>
/// </remarks>
public static class Igi2RigidLineOfSight
{
    /// <summary>Meets one model.</summary>
    /// <param name="model">The model.</param>
    /// <param name="sight">The line of sight.</param>
    public static void Meet(Igi2RigidObject model, Igi2LineOfSight sight)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sight);

        Igi2CollisionMesh? mesh = model.Mesh(0);
        if (mesh is null || mesh.Arrays.FaceCount == 0 || sight.IsBlocked)
        {
            return;
        }

        if (sight.Includes is { } includes && !includes(model.Owner))
        {
            return;
        }

        float[] m = model.Orientation;
        Vector3d p = model.Position;
        Vector3d s = sight.Start, e = sight.End;
        float sx = model.Scale.X, sy = model.Scale.Y, sz = model.Scale.Z;
        float largest = sy > sz ? sy : sz;
        float smax = largest < sx ? sx : largest;
        float r = ((Fpu)model.Radius * smax).F;
        if (!MeshRay.Straddles(MeshRayDialect.Igi2, ((Fpu)p.X - r).Value, ((Fpu)r + p.X).Value, s.X, e.X)
            || !MeshRay.Straddles(MeshRayDialect.Igi2, ((Fpu)p.Y - r).Value, ((Fpu)r + p.Y).Value, s.Y, e.Y)
            || !MeshRay.Straddles(MeshRayDialect.Igi2, ((Fpu)p.Z - r).Value, ((Fpu)r + p.Z).Value, s.Z, e.Z))
        {
            return;
        }

        // Both ends into the model's frame: the start sums its middle row z, x, y; the end y, z, x.
        Fpu dx = (Fpu)s.X - p.X, dy = (Fpu)s.Y - p.Y, dz = (Fpu)s.Z - p.Z;
        float rx = ((dz * m[6]) + (dx * m[0]) + (dy * m[3])).F;
        float ry = ((dz * m[7]) + (dx * m[1]) + (dy * m[4])).F;
        float rz = ((dz * m[8]) + (dx * m[2]) + (dy * m[5])).F;
        float isx = ((Fpu)1f / sx).F, isy = ((Fpu)1f / sy).F, isz = ((Fpu)1f / sz).F;
        Vector3f start = new(((Fpu)rx * isx).F, ((Fpu)ry * isy).F, ((Fpu)isz * rz).F);

        Fpu ex = (Fpu)e.X - p.X, ey = (Fpu)e.Y - p.Y, ez = (Fpu)e.Z - p.Z;
        float lx = ((ez * m[6]) + (ex * m[0]) + (ey * m[3])).F;
        float ly = ((ey * m[4]) + (ez * m[7]) + (ex * m[1])).F;
        float lz = ((ez * m[8]) + (ex * m[2]) + (ey * m[5])).F;
        Vector3f end = new(((Fpu)isx * lx).F, ((Fpu)isy * ly).F, ((Fpu)isz * lz).F);

        MeshRaySearch<Fpu> search = new(MeshRayDialect.Igi2);
        search.TightBox(start, end);
        float ddz = ((Fpu)end.Z - start.Z).F, ddy = ((Fpu)end.Y - start.Y).F, ddx = ((Fpu)end.X - start.X).F;
        Fpu inverse = (Fpu)1.0 / ((((Fpu)ddx * ddx) + ((Fpu)ddy * ddy)) + ((Fpu)ddz * ddz));
        if (mesh.Arrays.SphereCount == 0)
        {
            return;
        }

        Vector3f d = new(ddx, ddy, ddz);
        Faces faces = new(mesh, sight, start, d, search);
        search.Walk(mesh.Arrays.Links, mesh.Arrays.Spheres, start, d, inverse, MeshSphereSums.XyzAlways, cull: true, ref faces);
    }

    /// <summary>One face against the line: the shared crossing on a face that is no portal, then what the face lets through.</summary>
    private readonly struct Faces(Igi2CollisionMesh mesh, Igi2LineOfSight sight, Vector3f s, Vector3f d, MeshRaySearch<Fpu> search)
        : IMeshFaceVisitor
    {
        public void Visit(int face)
        {
            int[] faces = mesh.Arrays.Faces;
            int material = faces[(4 * face) + 3];
            int portal = (uint)material < (uint)mesh.Portals.Length ? mesh.Portals[material] : Igi2MeshSearch.None;
            if (portal != Igi2MeshSearch.None)
            {
                return;
            }

            int i0 = 3 * faces[4 * face], i1 = 3 * faces[(4 * face) + 1], i2 = 3 * faces[(4 * face) + 2];
            if (!search.Cross(mesh.Arrays.Vertices, i0, i1, i2, s, d, flip: false, MeshPlaneSum.XZY, out MeshFaceCrossing<Fpu> crossing))
            {
                return;
            }

            sight.IsHit = true;
            int row = (uint)material < (uint)mesh.Rows.Length ? mesh.Rows[material] : Igi2MeshSearch.None;
            if (row == Igi2MeshSearch.None)
            {
                Stop();
                return;
            }

            float transparency = sight.Materials[row]?.QMaterial.Transparency ?? 0f;
            if (crossing.Dot.Value < 0.0)
            {
                sight.Transparency = ((Fpu)transparency * sight.Transparency).F;
            }

            if (sight.Material is null || sight.T > crossing.T)
            {
                sight.Material = row;
                sight.T = crossing.T;
            }

            if (transparency == 0f)
            {
                Stop();
            }
        }

        private void Stop()
        {
            sight.Transparency = 0f;
            search.Stopped = true;
        }
    }
}
