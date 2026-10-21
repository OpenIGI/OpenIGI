using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// A line of sight against a boned object's posed mesh, <c>BoneDynCubeObj_LineOfSightHandler</c> (<c>igi2 0x566B90</c>), with the
/// arithmetic the client's FPU performs.
/// </summary>
/// <remarks>
/// <para>
/// The bones are brought up to date, then the line's evaluate event is asked, then the line must come within the object's sphere
/// (<see cref="Igi2BoneTrace.SphereMeets"/>). Every vertex of the first mesh level is placed by its bone alone: unlike the trace,
/// the object's orientation is not applied, so the mesh stands as if the object faced along the world's axes. The line is taken
/// relative to the object's position; a box padded by 40.96 around it culls the faces, which are all tested in order.
/// </para>
/// <para>
/// A face is crossed where the line meets its plane within the line, and within the face by the two coordinates of the plane's
/// strongest axes. Each crossing marks the line crossed; one entered from the front lets through its material's share of what is
/// left. A face of no material, or of one that lets nothing through, stops the line and this object. Unlike a solid model's, a
/// boned object leaves the nearest material and its distance as they were.
/// </para>
/// <para>
/// A human answers a line with no evaluate event through this handler too (<c>Human_LineOfSightHandler</c>, <c>0x4FF1E0</c>),
/// except its own first-person view's body, which only the player has.
/// </para>
/// </remarks>
public static class Igi2BoneLineOfSight
{
    /// <summary>How far the line's box is padded on each side, the float <c>0x4223D70A</c>.</summary>
    private const float BoxPad = 40.96f;

    /// <summary>Meets one boned object.</summary>
    /// <param name="collider">The object.</param>
    /// <param name="sight">The line of sight.</param>
    public static void Meet(Igi2BoneCollider collider, Igi2LineOfSight sight)
    {
        ArgumentNullException.ThrowIfNull(collider);
        ArgumentNullException.ThrowIfNull(sight);
        IIgi2BonedBody body = collider.Body;
        if (body.BoneMesh(0) is not { } mesh || mesh.Arrays.FaceCount == 0)
        {
            return;
        }

        BonePose? pose = body.CurrentPose();
        if (sight.Includes is { } includes && !includes(collider.Owner))
        {
            return;
        }

        if (pose is null || !Igi2BoneTrace.SphereMeets(body, sight.Start, sight.End))
        {
            return;
        }

        Vector3d p = body.Position;
        Vector3d start = sight.Start, end = sight.End;
        float sx = ((Fpu)start.X - p.X).F, sy = ((Fpu)start.Y - p.Y).F, sz = ((Fpu)start.Z - p.Z).F;
        float ex = ((Fpu)end.X - p.X).F, ey = ((Fpu)end.Y - p.Y).F, ez = ((Fpu)end.Z - p.Z).F;
        Box box = new(
            ((Fpu)(sx >= ex ? ex : sx) - BoxPad).F,
            ((Fpu)(sy >= ey ? ey : sy) - BoxPad).F,
            ((Fpu)(sz >= ez ? ez : sz) - BoxPad).F,
            ((Fpu)(sx <= ex ? ex : sx) + BoxPad).F,
            ((Fpu)(sy <= ey ? ey : sy) + BoxPad).F,
            ((Fpu)(sz <= ez ? ez : sz) + BoxPad).F);
        Vector3f s = new(sx, sy, sz);
        Vector3f d = new(((Fpu)end.X - start.X).F, ((Fpu)end.Y - start.Y).F, ((Fpu)end.Z - start.Z).F);

        float[] v = Pose(mesh, pose);
        int[] faces = mesh.Arrays.Faces;
        for (int f = 0; f < mesh.Arrays.FaceCount; f++)
        {
            int i0 = 3 * faces[4 * f], i1 = 3 * faces[(4 * f) + 1], i2 = 3 * faces[(4 * f) + 2];
            if (box.Culls(v, i0, i1, i2) || !Crosses(v, i0, i1, i2, s, d, out float den))
            {
                continue;
            }

            sight.IsHit = true;
            int material = faces[(4 * f) + 3];
            int row = (uint)material < (uint)mesh.Rows.Length ? mesh.Rows[material] : Igi2MeshSearch.None;
            if (row == Igi2MeshSearch.None)
            {
                sight.Transparency = 0f;
                return;
            }

            float transparency = sight.Materials[row]?.QMaterial.Transparency ?? 0f;
            if (den < 0f)
            {
                sight.Transparency = ((Fpu)sight.Transparency * transparency).F;
            }

            if (transparency == 0f)
            {
                sight.Transparency = 0f;
                return;
            }
        }
    }

    /// <summary>
    /// Every vertex placed by its bone, unturned: the last vertex of each unrolled four sums its rows y, x, z; every other vertex,
    /// the tail's included, z, y, x.
    /// </summary>
    private static float[] Pose(Igi2BoneMesh mesh, BonePose pose)
    {
        float[] v = mesh.Arrays.Vertices;
        int count = v.Length / 3;
        int unrolled = count & ~3;
        float[] posed = new float[v.Length];
        for (int k = 0; k < count; k++)
        {
            int bone = mesh.Bones[k];
            Matrix3f m = (uint)bone < (uint)pose.BoneCount ? pose.Rotations[bone] : Matrix3f.Identity;
            Vector3f at = (uint)bone < (uint)pose.BoneCount ? pose.Positions[bone] : default;
            float x = v[3 * k], y = v[(3 * k) + 1], z = v[(3 * k) + 2];
            if (k < unrolled && (k & 3) == 3)
            {
                posed[3 * k] = (((((Fpu)m.M12 * y) + ((Fpu)m.M11 * x)) + ((Fpu)m.M13 * z)) + at.X).F;
                posed[(3 * k) + 1] = (((((Fpu)m.M22 * y) + ((Fpu)m.M21 * x)) + ((Fpu)m.M23 * z)) + at.Y).F;
                posed[(3 * k) + 2] = (((((Fpu)m.M32 * y) + ((Fpu)m.M31 * x)) + ((Fpu)m.M33 * z)) + at.Z).F;
            }
            else
            {
                posed[3 * k] = (((((Fpu)m.M13 * z) + ((Fpu)m.M12 * y)) + ((Fpu)m.M11 * x)) + at.X).F;
                posed[(3 * k) + 1] = (((((Fpu)m.M23 * z) + ((Fpu)m.M22 * y)) + ((Fpu)m.M21 * x)) + at.Y).F;
                posed[(3 * k) + 2] = (((((Fpu)m.M33 * z) + ((Fpu)m.M32 * y)) + ((Fpu)m.M31 * x)) + at.Z).F;
            }
        }

        return posed;
    }

    /// <summary>
    /// Whether the line crosses a face: its plane met within the line, then the crossing's two coordinates on the plane's
    /// strongest axes within the face.
    /// </summary>
    /// <param name="v">The posed vertices.</param>
    /// <param name="i0">The first vertex's offset.</param>
    /// <param name="i1">The second's.</param>
    /// <param name="i2">The third's.</param>
    /// <param name="s">The line's start, relative to the object.</param>
    /// <param name="d">The line, start to end.</param>
    /// <param name="den">The plane's normal along the line: negative when the face is entered from the front.</param>
    private static bool Crosses(float[] v, int i0, int i1, int i2, Vector3f s, Vector3f d, out float den)
    {
        float v0x = v[i0], v0y = v[i0 + 1], v0z = v[i0 + 2];
        float e1x = ((Fpu)v[i2] - v0x).F, e1y = ((Fpu)v[i2 + 1] - v0y).F, e1z = ((Fpu)v[i2 + 2] - v0z).F;
        float e0x = ((Fpu)v[i1] - v0x).F, e0y = ((Fpu)v[i1 + 1] - v0y).F, e0z = ((Fpu)v[i1 + 2] - v0z).F;
        float nx = (((Fpu)e0z * e1y) - ((Fpu)e0y * e1z)).F;
        float ny0 = (((Fpu)e0z * e1x) - ((Fpu)e0x * e1z)).F;
        float ny = -ny0;
        float nz = (((Fpu)e0y * e1x) - ((Fpu)e0x * e1y)).F;
        den = ((((Fpu)nz * d.Z) + ((Fpu)ny * d.Y)) + ((Fpu)nx * d.X)).F;
        if (den == 0f)
        {
            return false;
        }

        Fpu num = ((((Fpu)v0x - s.X) * nx) + (((Fpu)v0y - s.Y) * ny)) + (((Fpu)v0z - s.Z) * nz);
        float t = (num / den).F;
        if (t < 0f || t > 1f)
        {
            return false;
        }

        float ix = (((Fpu)t * d.X) + s.X).F, iy = (((Fpu)t * d.Y) + s.Y).F, iz = (((Fpu)t * d.Z) + s.Z).F;
        float ax = MathF.Abs(nx), ay = MathF.Abs(ny), az = MathF.Abs(nz);
        Fpu u, w;
        if (ax > ay && ax > az)
        {
            float pz = ((Fpu)iz - v0z).F, py = ((Fpu)iy - v0y).F;
            u = (((Fpu)pz * e1y) - ((Fpu)py * e1z)) / nx;
            if (!Within(u) || !Second(MathF.Abs(e1y) <= MathF.Abs(e1z), pz, e0z, e1z, py, e0y, e1y, u, out w))
            {
                return false;
            }
        }
        else if (ay > az)
        {
            if (ny0 == 0f)
            {
                return false;
            }

            float pz = ((Fpu)iz - v0z).F, px = ((Fpu)ix - v0x).F;
            u = (((Fpu)pz * e1x) - ((Fpu)px * e1z)) / ny0;
            if (!Within(u) || !Second(MathF.Abs(e1x) <= MathF.Abs(e1z), pz, e0z, e1z, px, e0x, e1x, u, out w))
            {
                return false;
            }
        }
        else
        {
            if (nz == 0f)
            {
                return false;
            }

            float py = ((Fpu)iy - v0y).F, px = ((Fpu)ix - v0x).F;
            u = (((Fpu)py * e1x) - ((Fpu)px * e1y)) / nz;
            if (!Within(u) || !Second(MathF.Abs(e1x) <= MathF.Abs(e1y), py, e0y, e1y, px, e0x, e1x, u, out w))
            {
                return false;
            }
        }

        return !(w.Value < 0.0) && !((w + u).Value > 1.0);
    }

    private static bool Within(Fpu u) => !(u.Value < 0.0) && !(u.Value > 1.0);

    /// <summary>
    /// The second coordinate, by the stronger of the two axes the first leaves: the first pair's when <paramref name="useFirst"/>,
    /// which must not be flat, else the second pair's.
    /// </summary>
    private static bool Second(bool useFirst, float pa, float ea0, float ea1, float pb, float eb0, float eb1, Fpu u, out Fpu w)
    {
        if (useFirst)
        {
            if (ea1 == 0f)
            {
                w = default;
                return false;
            }

            w = ((Fpu)pa - ((Fpu)ea0 * u)) / ea1;
            return true;
        }

        w = ((Fpu)pb - ((Fpu)eb0 * u)) / eb1;
        return true;
    }

    /// <summary>The line's box less the object's position, padded.</summary>
    private readonly record struct Box(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
    {
        /// <summary>Whether a face lies wholly to one side of the box on an axis.</summary>
        public bool Culls(float[] v, int i0, int i1, int i2) =>
            (v[i0] < MinX && v[i1] < MinX && v[i2] < MinX)
            || (v[i0 + 1] < MinY && v[i1 + 1] < MinY && v[i2 + 1] < MinY)
            || (v[i0 + 2] < MinZ && v[i1 + 2] < MinZ && v[i2 + 2] < MinZ)
            || (v[i0] > MaxX && v[i1] > MaxX && v[i2] > MaxX)
            || (v[i0 + 1] > MaxY && v[i1 + 1] > MaxY && v[i2 + 1] > MaxY)
            || (v[i0 + 2] > MaxZ && v[i1 + 2] > MaxZ && v[i2 + 2] > MaxZ);
    }
}
