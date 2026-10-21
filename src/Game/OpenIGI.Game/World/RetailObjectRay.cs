using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>One collision mesh level, unpacked into the arrays the original's ray handlers walk.</summary>
/// <remarks>
/// The <c>HSMC</c> level header's faces (<c>ECFC</c>), vertices (<c>XTVC</c>), materials (<c>TAMC</c>) and sphere
/// tree (<c>HPSC</c>), laid out as the original reads them.
/// </remarks>
public sealed class RetailRayMesh
{
    /// <summary>Initializes a new instance of the <see cref="RetailRayMesh"/> class.</summary>
    /// <param name="mesh">The collision mesh level.</param>
    public RetailRayMesh(MefCollisionMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ReadOnlySpan<MefCollisionFace> faces = mesh.Faces;
        Faces = new int[faces.Length * 4];
        for (int i = 0; i < faces.Length; i++)
        {
            Faces[(4 * i) + 0] = faces[i].I0;
            Faces[(4 * i) + 1] = faces[i].I1;
            Faces[(4 * i) + 2] = faces[i].I2;
            Faces[(4 * i) + 3] = faces[i].Material;
        }

        Vertices = new float[mesh.Vertices.Count * 3];
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            Vertices[(3 * i) + 0] = mesh.Vertices[i].X;
            Vertices[(3 * i) + 1] = mesh.Vertices[i].Y;
            Vertices[(3 * i) + 2] = mesh.Vertices[i].Z;
        }

        Spheres = new float[mesh.Spheres.Count * 4];
        Links = new int[mesh.Spheres.Count * 4];
        for (int i = 0; i < mesh.Spheres.Count; i++)
        {
            MefCollisionSphere sphere = mesh.Spheres[i];
            Spheres[(4 * i) + 0] = sphere.Centre.X;
            Spheres[(4 * i) + 1] = sphere.Centre.Y;
            Spheres[(4 * i) + 2] = sphere.Centre.Z;
            Spheres[(4 * i) + 3] = sphere.Radius;
            Links[(4 * i) + 0] = sphere.Field4 & 0xFFFF;
            Links[(4 * i) + 1] = (sphere.Field4 >> 16) & 0xFFFF;
            Links[(4 * i) + 2] = sphere.Field5 & 0xFFFF;
            Links[(4 * i) + 3] = (sphere.Field5 >> 16) & 0xFFFF;
        }

        Portal = new bool[Math.Max(0, mesh.Materials)];
        GlobalMaterial = new int[Portal.Length];
        for (int m = 0; m < Portal.Length; m++)
        {
            Portal[m] = mesh.IsPortal(m);
            GlobalMaterial[m] = mesh.GlobalMaterial(m);
        }
    }

    /// <summary>Initializes a new instance of the <see cref="RetailRayMesh"/> class: another mesh's faces, tree and materials over vertices and spheres of its own, as a mesh bent into a new shape keeps them.</summary>
    /// <param name="source">The mesh whose faces, tree links and materials are shared.</param>
    /// <param name="vertices">The vertices, three floats each.</param>
    /// <param name="spheres">The spheres, four floats each.</param>
    public RetailRayMesh(RetailRayMesh source, float[] vertices, float[] spheres)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(spheres);
        Faces = source.Faces;
        Links = source.Links;
        Portal = source.Portal;
        GlobalMaterial = source.GlobalMaterial;
        Vertices = vertices;
        Spheres = spheres;
    }

    /// <summary>Gets the faces, four ints each: three vertex indices and the mesh material.</summary>
    public int[] Faces { get; }

    /// <summary>Gets the vertices, three floats each, in the model's own space.</summary>
    public float[] Vertices { get; }

    /// <summary>Gets the spheres, four floats each: centre and radius.</summary>
    public float[] Spheres { get; }

    /// <summary>Gets the sphere links, four ints each: first face, face count, first child, next sibling.</summary>
    public int[] Links { get; }

    /// <summary>Gets, per mesh material, whether it is a portal (skipped by <c>RayCast</c>).</summary>
    public bool[] Portal { get; }

    /// <summary>Gets, per mesh material, the level material row a hit reports (row 1 for none).</summary>
    public int[] GlobalMaterial { get; }

    /// <summary>Gets the face count.</summary>
    public int FaceCount => Faces.Length / 4;

    /// <summary>Gets the sphere count.</summary>
    public int SphereCount => Spheres.Length / 4;
}

/// <summary>One placed model a car's rays can strike: a <c>RigidDynCubeObj</c> -- a building, a prop, another car.</summary>
public sealed class RetailRayModel
{
    /// <summary>Initializes a new instance of the <see cref="RetailRayModel"/> class.</summary>
    /// <param name="owner">What the struck-object slot reports.</param>
    /// <param name="mesh">Collision level 0 of the model's LOD 0.</param>
    /// <param name="modelRadius">The model's <c>MESH+120</c> (LOD 0).</param>
    public RetailRayModel(object owner, RetailRayMesh mesh, float modelRadius)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        ModelRadius = modelRadius;
        M[0] = M[4] = M[8] = 1f;
        Scale[0] = Scale[1] = Scale[2] = 1f;
    }

    /// <summary>Gets what a hit reports as struck.</summary>
    public object Owner { get; }

    /// <summary>Gets the collision mesh.</summary>
    public RetailRayMesh Mesh { get; }

    /// <summary>Gets the model radius (<c>0x4D0950</c>).</summary>
    public float ModelRadius { get; }

    /// <summary>Gets the position (<c>+32</c>), world units.</summary>
    public double[] Position { get; } = new double[3];

    /// <summary>Gets the orientation (<c>+112</c>), row-major, body to world.</summary>
    public float[] M { get; } = new float[9];

    /// <summary>Gets the scale factors (<c>+152</c>).</summary>
    public float[] Scale { get; } = new float[3];
}

/// <summary>
/// The model half of the original's ray query: <c>RigidDynCubeObj</c>'s op-9/10 handler <c>0x4C1840</c>, with its
/// exact arithmetic.
/// </summary>
public static class RetailModelRay
{
    /// <summary>Tests one ray against one model, writing the result when the model is hit strictly nearer.</summary>
    /// <param name="o">The model.</param>
    /// <param name="s">The ray start.</param>
    /// <param name="e">The ray end.</param>
    /// <param name="result">The ray's running result, whose earlier hit is the starting best.</param>
    public static void Cast(RetailRayModel o, Vector3d s, Vector3d e, ref RetailRayResult result)
    {
        ArgumentNullException.ThrowIfNull(o);
        RetailRayMesh mesh = o.Mesh;
        if (mesh.FaceCount == 0)
        {
            return;
        }

        float[] m = o.M;
        float s0 = o.Scale[0], s1 = o.Scale[1], s2 = o.Scale[2];
        double px = o.Position[0], py = o.Position[1], pz = o.Position[2];
        bool prevFront = result.Hit && result.HasFrontMaterial;
        bool hadHit = result.Hit;

        // The broad test, x against the unrounded radius.
        float m12 = s2 < s1 ? s1 : s2;
        float smax = s0 <= m12 ? m12 : s0;
        X87 r = (X87)o.ModelRadius * smax;
        float rf = r.F;
        if (!MeshRay.Straddles(MeshRayDialect.Igi1, ((X87)px - r).Value, ((X87)rf + px).Value, s.X, e.X)
            || !MeshRay.Straddles(MeshRayDialect.Igi1, ((X87)py - rf).Value, ((X87)rf + py).Value, s.Y, e.Y)
            || !MeshRay.Straddles(MeshRayDialect.Igi1, ((X87)pz - rf).Value, ((X87)rf + pz).Value, s.Z, e.Z))
        {
            return;
        }

        // The local ray.
        Local(o, s, fromStart: true, out float lsx0, out float lsy0, out float lsz0);
        float lsx = ((X87)lsx0 / s0).F, lsy = ((X87)lsy0 / s0).F, lsz = ((X87)lsz0 / s0).F;
        Local(o, e, fromStart: false, out float lex0, out float ley0, out float lez0);
        float lex = ((X87)lex0 / s0).F, ley = ((X87)ley0 / s0).F;
        X87 qz = (X87)lez0 / s0;
        float lez = qz.F;
        X87 dzl = qz - lsz, dyl = (X87)ley - lsy, dxl = (X87)lex - lsx;
        X87 len2 = dxl * dxl + dyl * dyl + dzl * dzl;
        float lenf = X87.Sqrt(len2).F;
        float[] ls = [lsx, lsy, lsz];

        float[] p0 = [lex, ley, lez];
        double best = 0d;
        if (hadHit)
        {
            Vector3d h = result.Point;
            X87 hx = (X87)h.X - px, hy = (X87)h.Y - py;
            float hz = ((X87)h.Z - pz).F;
            float x = (hy * m[3] + (X87)hz * m[6] + hx * m[0]).F;
            float y = ((X87)hz * m[7] + hx * m[1] + hy * m[4]).F;
            float z = (hy * m[5] + (X87)hz * m[8] + hx * m[2]).F;
            X87 q = (X87)z / s0;
            p0 = [((X87)x / s0).F, ((X87)y / s0).F, q.F];
            X87 ddz = q - lsz, ddy = (X87)p0[1] - lsy, ddx = (X87)p0[0] - lsx;
            best = X87.ToFloat(X87.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz).Value);
        }

        double inverse = ((X87)1.0 / len2).Value;
        Vector3f d = new(dxl.F, dyl.F, dzl.F);
        if (mesh.SphereCount == 0)
        {
            return;
        }

        Vector3f start = new(lsx, lsy, lsz);
        MeshRaySearch<X87> search = new(MeshRayDialect.Igi1)
        {
            Closest = best,
            IsHit = hadHit,
            PreviousFront = prevFront,
            Point = new Vector3f(lex, ley, lez),
        };
        search.Box(start, new Vector3f(p0[0], p0[1], p0[2]));
        WalkFaces(mesh, mesh.Vertices, mesh.Spheres, start, d, inverse, lenf, portals: false, search, mirror: false, cullSpheres: true);
        if (!search.HitThisObject)
        {
            return;
        }

        best = search.Closest;
        float[] point = [search.Point.X, search.Point.Y, search.Point.Z];
        if (!(s0 == 1f && s1 == 1f && s2 == 1f))
        {
            X87 a0 = (X87)point[0] * s0, b0 = (X87)point[1] * s1, c0 = (X87)point[2] * s2;
            float lxs = ((X87)lsx * s0).F, lys = ((X87)lsy * s1).F;
            X87 lzs = (X87)lsz * s2;
            X87 dz = lzs - c0, dy = (X87)lys - b0, dx = (X87)lxs - a0;
            best = X87.Sqrt(dx * dx + dy * dy + dz * dz).Value;
        }

        if (result.Hit && !(best < result.Distance))
        {
            return;
        }

        // The recomputed, unnormalised normal, carried into the world.
        FaceNormal(mesh, search.Face, mirror: false, out float nx, out float ny, out float nz);
        float wx = ((X87)m[2] * nz + (X87)m[1] * ny + (X87)m[0] * nx).F;
        float wy = ((X87)nx * m[3] + (X87)m[5] * nz + (X87)m[4] * ny).F;
        float wz = ((X87)m[8] * nz + (X87)m[7] * ny + (X87)m[6] * nx).F;
        X87 a = (X87)point[0] * s0, b = (X87)point[1] * s1, c = (X87)point[2] * s2;
        double hitX = ((X87)(b * m[1] + c * m[2] + a * m[0]).F + px).Value;
        double hitY = ((X87)(c * m[5] + a * m[3] + b * m[4]).F + py).Value;
        double hitZ = (b * m[7] + c * m[8] + a * m[6] + pz).Value;

        result.Struck = o.Owner;
        result.Point = new Vector3d(hitX, hitY, hitZ);
        result.NormalX = wx;
        result.NormalY = wy;
        result.NormalZ = wz;
        result.HasFrontMaterial = search.PreviousFront;
        result.HasSurface = !search.PreviousFront;
        result.SurfaceForce = 0f;
        result.Distance = best;
        result.Hit = true;
        result.Material = mesh.GlobalMaterial[mesh.Faces[(4 * search.Face) + 3]];
    }

    /// <summary>A world point into the model's frame: <c>Mᵀ·(P − pos)</c>, with the start's and the end's groupings.</summary>
    private static void Local(RetailRayModel o, Vector3d p, bool fromStart, out float x, out float y, out float z)
    {
        float[] m = o.M;
        X87 dx = (X87)p.X - o.Position[0], dy = (X87)p.Y - o.Position[1], dz = (X87)p.Z - o.Position[2];
        if (fromStart)
        {
            x = (dz * m[6] + dx * m[0] + dy * m[3]).F;
            y = (dz * m[7] + dx * m[1] + dy * m[4]).F;
            z = (dz * m[8] + dx * m[2] + dy * m[5]).F;
        }
        else
        {
            x = (dy * m[3] + dz * m[6] + dx * m[0]).F;
            y = (dz * m[7] + dx * m[1] + dy * m[4]).F;
            z = (dy * m[5] + dz * m[8] + dx * m[2]).F;
        }
    }

    /// <summary>
    /// The sphere-tree walk with its quirk and the face test of every node it reaches, on the engine's shared search with
    /// this build's pad, tie and comparisons (<see cref="MeshRayDialect.Igi1"/>), summing the plane z, y, x and the spheres z, x, y.
    /// </summary>
    /// <param name="mesh">The mesh's faces and materials.</param>
    /// <param name="vertices">Its vertices: its own, or a bent spline segment's.</param>
    /// <param name="spheres">Its sphere tree: its own, or a bent spline segment's.</param>
    /// <param name="start">The line's start, in the mesh's frame.</param>
    /// <param name="d">The line's direction.</param>
    /// <param name="inverse">One over the direction's squared length.</param>
    /// <param name="lenf">The line's length, for a crossing's distance.</param>
    /// <param name="portals">Whether portal faces stop the line.</param>
    /// <param name="search">The running best.</param>
    /// <param name="mirror">Whether the faces are mirrored (a reversed spline segment).</param>
    /// <param name="cullSpheres">Whether the spheres cull.</param>
    internal static void WalkFaces(
        RetailRayMesh mesh,
        float[] vertices,
        float[] spheres,
        Vector3f start,
        Vector3f d,
        double inverse,
        float lenf,
        bool portals,
        MeshRaySearch<X87> search,
        bool mirror,
        bool cullSpheres)
    {
        Faces faces = new(mesh, vertices, start, d, lenf, portals, search, mirror);
        search.Walk(mesh.Links, spheres, start, d, inverse, MeshSphereSums.ZxyAlways, cullSpheres, ref faces);
    }

    // One face: portals skipped unless asked for, then the shared crossing, its distance along the line, and the tie rules.
    private readonly struct Faces(
        RetailRayMesh mesh,
        float[] vertices,
        Vector3f s,
        Vector3f d,
        float lenf,
        bool portals,
        MeshRaySearch<X87> search,
        bool mirror) : IMeshFaceVisitor
    {
        public void Visit(int face)
        {
            int[] faces = mesh.Faces;
            int material = faces[(4 * face) + 3];
            if (!portals && (uint)material < (uint)mesh.Portal.Length && mesh.Portal[material])
            {
                return;
            }

            if (!search.Cross(vertices, 3 * faces[4 * face], 3 * faces[(4 * face) + 1], 3 * faces[(4 * face) + 2], s, d, mirror, MeshPlaneSum.ZYX, out MeshFaceCrossing<X87> crossing))
            {
                return;
            }

            X87 distance = (X87)crossing.T * lenf;
            bool front = crossing.Dot.Value < 0d;
            if (search.Accept(distance, front))
            {
                search.Take(distance, front, crossing.Point, face, s);
            }
        }
    }

    /// <summary>The normal: the face's cross product with the register differences, not unit length.</summary>
    internal static void FaceNormal(RetailRayMesh mesh, int f, bool mirror, out float nx, out float ny, out float nz, float[]? vertices = null)
    {
        float[] v = vertices ?? mesh.Vertices;
        int[] faces = mesh.Faces;
        int i0 = faces[4 * f], i1 = faces[(4 * f) + 1], i2 = faces[(4 * f) + 2];
        float v0x = v[3 * i0], v0y = v[(3 * i0) + 1], v0z = v[(3 * i0) + 2];
        X87 ax = (X87)v[3 * i2] - v0x, ay = (X87)v[(3 * i2) + 1] - v0y, az = (X87)v[(3 * i2) + 2] - v0z;
        float bx = ((X87)v[3 * i1] - v0x).F, by = ((X87)v[(3 * i1) + 1] - v0y).F;
        X87 bzr = (X87)v[(3 * i1) + 2] - v0z;
        nx = (bzr * ay - (X87)by * az).F;
        ny = (-(bzr * ax - (X87)bx * az)).F;
        nz = ((X87)by * ax - (X87)bx * ay).F;
        if (mirror)
        {
            nx = -nx;
            ny = -ny;
            nz = -nz;
        }
    }
}
