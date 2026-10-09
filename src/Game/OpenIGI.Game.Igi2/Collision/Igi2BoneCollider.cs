using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>A boned model's collision mesh level, <c>MultiMesh_Mesh_t</c>, with the bone each vertex is carried by.</summary>
/// <remarks>A boned mesh is traced face by face, its sphere tree unused, each vertex placed by its bone's pose.</remarks>
public sealed class Igi2BoneMesh
{
    /// <summary>Initializes a new instance of the <see cref="Igi2BoneMesh"/> class.</summary>
    /// <param name="mesh">The collision mesh level.</param>
    public Igi2BoneMesh(MefCollisionMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Arrays = new RetailRayMesh(mesh);
        Bones = [.. mesh.VertexBones];
    }

    /// <summary>Gets the faces, vertices (each in its bone's frame) and material rows.</summary>
    public RetailRayMesh Arrays { get; }

    /// <summary>Gets the bone each vertex is carried by, <c>MultiMesh_Vertex_t</c>'s fourth word.</summary>
    public int[] Bones { get; }

    /// <summary>A model's collision mesh levels, or <see langword="null"/> when it has none.</summary>
    /// <param name="model">The model.</param>
    /// <returns>One per level, <see langword="null"/> for an empty level.</returns>
    public static Igi2BoneMesh?[]? Of(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Igi2BoneMesh?[] levels = new Igi2BoneMesh?[MefCollisionMesh.MeshLevels];
        bool any = false;
        for (int level = 0; level < levels.Length; level++)
        {
            if (MefCollisionMesh.TryRead(model.Container, level, out MefCollisionMesh? mesh, Formats.AssetFormatProfile.Igi2.Collision)
                && mesh is { Faces.Length: > 0 })
            {
                levels[level] = new Igi2BoneMesh(mesh);
                any = true;
            }
        }

        return any ? levels : null;
    }
}

/// <summary>A boned object as its line handler reads it, <c>BoneDynCubeObj_s</c>.</summary>
public interface IIgi2BonedBody
{
    /// <summary>Gets where the object stands, <c>tPos</c>.</summary>
    Vector3d Position { get; }

    /// <summary>Gets how it is turned, <c>tOrientation</c>: rows are its local X, Y and Z axes.</summary>
    Matrix3f Orientation { get; }

    /// <summary>Gets its root bone's translation, <c>tRootTranslation</c> (<c>+0x130</c>).</summary>
    Vector3f RootTranslation { get; }

    /// <summary>Gets its bounding radius about the root, <c>vRadius</c> (<c>+0x148</c>).</summary>
    float ModelRadius { get; }

    /// <summary>Gets a collision mesh level of its model, or <see langword="null"/> for none.</summary>
    /// <param name="level">The level a trace asks for.</param>
    /// <returns>The level.</returns>
    Igi2BoneMesh? BoneMesh(int level);

    /// <summary>Gets its bones as they now stand, <c>patBone</c> after <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>igi2 0x564B50</c>).</summary>
    /// <returns>The pose, or <see langword="null"/> when it has none.</returns>
    BonePose? CurrentPose();
}

/// <summary>
/// A boned object in the collision world: lines strike its mesh as its bones pose it, <c>BoneDynCubeObj_TraceLinesHandler</c>
/// (<c>igi2 0x5655A0</c>).
/// </summary>
/// <param name="body">The object.</param>
/// <param name="owner">What the object stands for, which a hit's events go to.</param>
/// <remarks>
/// The type links no ground-query handler, so vertical queries pass through it; only line traces meet it. A human's own line
/// handler (<c>Human_TraceLinesHandler</c>, <c>0x4FF070</c>) hands single-player traces straight to this one.
/// </remarks>
public sealed class Igi2BoneCollider(IIgi2BonedBody body, object owner) : IIgi2Collider
{
    /// <summary>Gets the object.</summary>
    public IIgi2BonedBody Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    /// <inheritdoc/>
    public object Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>Gets the object's query group, a human's <c>1</c>.</summary>
    public uint Group { get; init; } = Igi2CollisionWorld.HumanGroup;

    /// <inheritdoc/>
    /// <remarks>Nothing: <c>BoneDynCubeObj_GenericOpen</c> (<c>igi2 0x567930</c>) links no handler for the ground query.</remarks>
    public void NextZ(Igi2LineTrace trace)
    {
    }

    /// <summary>Traces lines against the object, replacing each line's result where it is struck nearer.</summary>
    /// <param name="trace">The lines and their running results.</param>
    public void TraceLines(Igi2LineTrace trace) => Igi2BoneTrace.Trace(this, trace);
}

/// <summary>
/// Lines against a boned object's posed mesh, <c>BoneDynCubeObj_TraceLinesHandler</c> (<c>igi2 0x5655A0</c>), with the arithmetic
/// the client's FPU performs.
/// </summary>
/// <remarks>
/// <para>
/// The trace's evaluate event is asked first, then each line in turn: a line that misses the object's sphere
/// (<c>BoneDynCubeObj_SphereIntersectionCheck</c>, <c>0x565180</c>) is passed over. When the first line reaches it, every vertex
/// is placed once by its bone and then turned by the object's orientation rows. Each line is taken relative to the object's
/// position, unturned; a box padded by a centimetre around it culls the faces, which are all tested in order, the sphere tree
/// unused, with the shared plane and barycentric test and tie window (<see cref="Igi2MeshSearch"/>).
/// </para>
/// <para>
/// A hit records the struck point, the face's normal (normalised, then turned by the orientation's columns), the face and
/// material, a light of one, and the bone: the first vertex's, or the second's when the second and third share one the first
/// does not. Portals and an ignored material are not tested for.
/// </para>
/// </remarks>
public static class Igi2BoneTrace
{
    /// <summary>Traces every line of a batch against one boned object.</summary>
    /// <param name="collider">The object.</param>
    /// <param name="trace">The lines and their running results.</param>
    public static void Trace(Igi2BoneCollider collider, Igi2LineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(collider);
        ArgumentNullException.ThrowIfNull(trace);
        IIgi2BonedBody body = collider.Body;
        if (body.BoneMesh(trace.Mesh) is not { } mesh || mesh.Arrays.FaceCount == 0)
        {
            return;
        }

        if (trace.Includes is { } includes && !includes(collider.Owner))
        {
            return;
        }

        float[]? posed = null;
        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i], end = trace.Ends[i];
            if (!SphereMeets(body, start, end))
            {
                continue;
            }

            if (posed is null)
            {
                if (body.CurrentPose() is not { } pose)
                {
                    return;
                }

                posed = Pose(mesh, pose, body.Orientation);
            }

            Line(collider, mesh, posed, start, end, trace.Intersections[i]);
        }
    }

    /// <summary>
    /// Whether a line comes within the object's radius of its root, <c>BoneDynCubeObj_SphereIntersectionCheck</c>
    /// (<c>igi2 0x565180</c>): the line's distance from the centre, and its box against the sphere's on each axis.
    /// </summary>
    /// <param name="body">The object.</param>
    /// <param name="start">The line's start.</param>
    /// <param name="end">Its end.</param>
    /// <returns>Whether the line may reach the object.</returns>
    public static bool SphereMeets(IIgi2BonedBody body, Vector3d start, Vector3d end)
    {
        ArgumentNullException.ThrowIfNull(body);
        Matrix3f o = body.Orientation;
        Vector3f root = body.RootTranslation;
        Vector3d p = body.Position;
        float r = body.ModelRadius;

        // The root turned by the orientation's rows: the sphere's centre less the position.
        float tx = ((((Fpu)o.M12 * root.Y) + ((Fpu)o.M11 * root.X)) + ((Fpu)o.M13 * root.Z)).F;
        float ty = ((((Fpu)o.M22 * root.Y) + ((Fpu)o.M21 * root.X)) + ((Fpu)o.M23 * root.Z)).F;
        float tz = ((((Fpu)o.M32 * root.Y) + ((Fpu)o.M31 * root.X)) + ((Fpu)o.M33 * root.Z)).F;

        float sz0 = ((Fpu)start.Z - p.Z).F;
        float ex0 = ((Fpu)end.X - p.X).F, ey0 = ((Fpu)end.Y - p.Y).F, ez0 = ((Fpu)end.Z - p.Z).F;
        float sx = (((Fpu)start.X - p.X) - tx).F;
        float sy = (((Fpu)start.Y - p.Y) - ty).F;
        float sz = ((Fpu)sz0 - tz).F;
        float ex = ((Fpu)ex0 - tx).F, ey = ((Fpu)ey0 - ty).F, ez = ((Fpu)ez0 - tz).F;

        float minX = sx < ex ? sx : ex, minY = sy < ey ? sy : ey, minZ = sz < ez ? sz : ez;
        float maxX = sx > ex ? sx : ex, maxY = sy > ey ? sy : ey, maxZ = sz > ez ? sz : ez;

        float dx = ((Fpu)end.X - start.X).F, dy = ((Fpu)end.Y - start.Y).F, dz = ((Fpu)end.Z - start.Z).F;
        float cx = -sx, cy = -sy, cz = -sz;
        float along = ((((Fpu)cz * dz) + ((Fpu)cy * dy)) + ((Fpu)cx * dx)).F;
        Fpu centre2 = (((Fpu)cx * cx) + ((Fpu)cy * cy)) + ((Fpu)cz * cz);
        Fpu inverse = (Fpu)1.0 / ((((Fpu)dx * dx) + ((Fpu)dy * dy)) + ((Fpu)dz * dz));
        Fpu distance2 = centre2 - (inverse * ((Fpu)along * along));
        if (!(((Fpu)r * r).Value >= distance2.Value) || !(minX <= r) || !(minY <= r) || !(minZ <= r))
        {
            return false;
        }

        float negative = -r;
        return maxX >= negative && maxY >= negative && maxZ >= negative;
    }

    /// <summary>
    /// Every vertex placed by its bone and turned by the object's orientation rows, in the object's frame less its turn: the
    /// first four-vertex blocks sum the X row z, y, x and the rest y, x, z, as the unrolled loop and its tail do.
    /// </summary>
    private static float[] Pose(Igi2BoneMesh mesh, BonePose pose, Matrix3f o)
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
            float bx = (((((Fpu)m.M12 * y) + ((Fpu)m.M13 * z)) + ((Fpu)m.M11 * x)) + at.X).F;
            float by = (((((Fpu)m.M22 * y) + ((Fpu)m.M23 * z)) + ((Fpu)m.M21 * x)) + at.Y).F;
            float bz = (((((Fpu)m.M32 * y) + ((Fpu)m.M33 * z)) + ((Fpu)m.M31 * x)) + at.Z).F;
            posed[3 * k] = k < unrolled
                ? ((((Fpu)bz * o.M13) + ((Fpu)by * o.M12)) + ((Fpu)bx * o.M11)).F
                : ((((Fpu)by * o.M12) + ((Fpu)bx * o.M11)) + ((Fpu)bz * o.M13)).F;
            posed[(3 * k) + 1] = ((((Fpu)by * o.M22) + ((Fpu)bz * o.M23)) + ((Fpu)bx * o.M21)).F;
            posed[(3 * k) + 2] = ((((Fpu)by * o.M32) + ((Fpu)bz * o.M33)) + ((Fpu)bx * o.M31)).F;
        }

        return posed;
    }

    private static void Line(Igi2BoneCollider collider, Igi2BoneMesh mesh, float[] v, Vector3d start, Vector3d end, Igi2Intersection hit)
    {
        IIgi2BonedBody body = collider.Body;
        Vector3d p = body.Position;
        Vector3f s = new(((Fpu)start.X - p.X).F, ((Fpu)start.Y - p.Y).F, ((Fpu)start.Z - p.Z).F);
        Vector3f e = new(((Fpu)end.X - p.X).F, ((Fpu)end.Y - p.Y).F, ((Fpu)end.Z - p.Z).F);
        Vector3f d = new(((Fpu)end.X - start.X).F, ((Fpu)end.Y - start.Y).F, ((Fpu)end.Z - start.Z).F);
        Fpu lx = (Fpu)e.X - s.X, ly = (Fpu)e.Y - s.Y, lz = (Fpu)e.Z - s.Z;
        float length = Fpu.Sqrt(((lx * lx) + (ly * ly)) + (lz * lz)).F;

        Igi2MeshSearch search = new()
        {
            IsHit = hit.IsHit,
            PreviousFront = hit.IsHit && hit.EnterMaterial is not null,
            Point = e,
        };
        if (search.IsHit)
        {
            float hx = ((Fpu)hit.Position.X - p.X).F, hy = ((Fpu)hit.Position.Y - p.Y).F;
            Fpu hz = (Fpu)hit.Position.Z - p.Z;
            Fpu cx = (Fpu)hx - s.X, cy = (Fpu)hy - s.Y, cz = hz - s.Z;
            search.Closest = Fpu.Sqrt(((cx * cx) + (cy * cy)) + (cz * cz)).Value;
        }

        search.Box(s, e);
        int[] faces = mesh.Arrays.Faces;
        for (int f = 0; f < mesh.Arrays.FaceCount; f++)
        {
            int i0 = 3 * faces[4 * f], i1 = 3 * faces[(4 * f) + 1], i2 = 3 * faces[(4 * f) + 2];
            if (!search.Cross(v, i0, i1, i2, s, d, flip: false, Igi2PlaneSum.YXZ, out Igi2FaceCrossing crossing))
            {
                continue;
            }

            Fpu distance = (Fpu)crossing.T * length;
            bool front = crossing.Dot.Value < 0.0;
            if (search.Accept(distance, front))
            {
                search.Take(distance, front, crossing.Point, f, s);
            }
        }

        if (!search.HitThisObject || (hit.IsHit && !(search.Closest < hit.Distance)))
        {
            return;
        }

        Record(collider, mesh, v, search, hit);
    }

    private static void Record(Igi2BoneCollider collider, Igi2BoneMesh mesh, float[] v, Igi2MeshSearch search, Igi2Intersection hit)
    {
        IIgi2BonedBody body = collider.Body;
        Matrix3f o = body.Orientation;
        Vector3d p = body.Position;
        int[] faces = mesh.Arrays.Faces;
        int f = search.Face;
        int k0 = faces[4 * f], k1 = faces[(4 * f) + 1], k2 = faces[(4 * f) + 2];
        int i0 = 3 * k0, i1 = 3 * k1, i2 = 3 * k2;
        int material = faces[(4 * f) + 3];
        int row = (uint)material < (uint)mesh.Arrays.GlobalMaterial.Length ? mesh.Arrays.GlobalMaterial[material] : 1;

        Vector3f n = Igi2MeshSearch.UnitNormal(
            (Fpu)v[i2] - v[i0],
            (Fpu)v[i2 + 1] - v[i0 + 1],
            (Fpu)v[i2 + 2] - v[i0 + 2],
            ((Fpu)v[i1] - v[i0]).F,
            ((Fpu)v[i1 + 1] - v[i0 + 1]).F,
            (Fpu)v[i1 + 2] - v[i0 + 2]);
        Vector3f point = search.Point;

        hit.HitObject = collider;
        hit.Position = new Vector3d(((Fpu)point.X + p.X).Value, ((Fpu)point.Y + p.Y).Value, ((Fpu)point.Z + p.Z).Value);
        hit.LocalPosition = point;
        hit.LocalNormal = n;
        hit.Normal = new Vector3f(
            ((((Fpu)o.M31 * n.Z) + ((Fpu)o.M21 * n.Y)) + ((Fpu)o.M11 * n.X)).F,
            ((((Fpu)o.M32 * n.Z) + ((Fpu)o.M22 * n.Y)) + ((Fpu)o.M12 * n.X)).F,
            ((((Fpu)o.M33 * n.Z) + ((Fpu)o.M23 * n.Y)) + ((Fpu)o.M13 * n.X)).F);
        hit.IsHit = true;
        hit.EnterMaterial = search.PreviousFront ? row : null;
        hit.LeaveMaterial = search.PreviousFront ? null : row;
        hit.IsDoubleFace = search.IsDoubleFace;
        hit.Distance = search.Closest;
        hit.Face = f;
        hit.Light = 1f;
        int b0 = mesh.Bones[k0], b1 = mesh.Bones[k1];
        hit.Sector = b1 != b0 && b1 == mesh.Bones[k2] ? b1 : b0;
    }
}
