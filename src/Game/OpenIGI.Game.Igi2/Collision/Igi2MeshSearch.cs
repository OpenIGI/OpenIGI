using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>Tests one face of a mesh for a line, as the walk reaches it.</summary>
internal interface IIgi2FaceVisitor
{
    /// <summary>Tests one face.</summary>
    /// <param name="face">The face's index.</param>
    void Visit(int face);
}

/// <summary>The order a face test sums the plane distance's three products in, which a 24-bit FPU makes matter.</summary>
internal enum Igi2PlaneSum
{
    /// <summary>y, x, z: the solid-model line handler (<c>RigidDynCubeObj_TraceLinesHandler</c>).</summary>
    YXZ,

    /// <summary>z, y, x: the spline handler (<c>SplineObjSegment_TraceLinesHandler</c>).</summary>
    ZYX,

    /// <summary>x, z, y: the solid-model line-of-sight handler (<c>RigidDynCubeObj_LineOfSightHandler</c>).</summary>
    XZY,
}

/// <summary>Where a line crosses a face: the plane's side, the parameter along the line, the point and the barycentric weights.</summary>
/// <param name="Dot">The face's normal dotted with the line: below zero when the line enters the face's front.</param>
/// <param name="T">How far along the line, as stored: a float.</param>
/// <param name="Point">The crossing, in the mesh's frame.</param>
/// <param name="U">The weight of the face's third vertex.</param>
/// <param name="V">The weight of its second.</param>
internal readonly record struct Igi2FaceCrossing(Fpu Dot, float T, Vector3f Point, Fpu U, Fpu V);

/// <summary>
/// One line's search through one model's collision mesh: the box the faces must reach, the nearest crossing so far, and
/// the sphere-tree walk and face test the line handlers of solid models and spline segments share.
/// </summary>
/// <remarks>
/// <para>
/// <c>RigidDynCubeObj_TraceLinesHandler</c> (<c>igi2 0x58B3B0</c>) and both paths of
/// <c>SplineObjSegment_TraceLinesHandler</c> (<c>0x5B7390</c>) walk the same tree and test faces the same way: a box
/// around the line padded by a centimetre, a plane test, a barycentric test on the face's dominant axis, and a tie
/// window that lets a face a little behind the best through when it faces the line and the best does not.
/// </para>
/// <para>
/// They differ in a few sums' order, which a 24-bit FPU makes matter, and in what they skip; those are the
/// parameters here. Every operation rounds to 24 bits, toward zero, and each float the disassembly stores is stored.
/// </para>
/// </remarks>
internal sealed class Igi2MeshSearch
{
    /// <summary>How far the search box reaches past the line, one centimetre: <c>40.96f</c>.</summary>
    public const float Pad = 40.96f;

    /// <summary>How far behind the best a face may be and still win a tie, a double constant: <c>40.96</c>.</summary>
    public const double TieWindow = 40.96;

    /// <summary>The value an absent lightmap, portal or material row is stored as.</summary>
    public const int None = 0xFFFF;

    /// <summary>Gets or sets a value indicating whether the line has struck anything yet, in this object or before it.</summary>
    public bool IsHit { get; set; }

    /// <summary>Gets or sets a value indicating whether the best crossing so far entered a face's front.</summary>
    public bool PreviousFront { get; set; }

    /// <summary>Gets a value indicating whether a face of this object was taken.</summary>
    public bool HitThisObject { get; private set; }

    /// <summary>Gets a value indicating whether the last tie test met a face within the window of the best.</summary>
    public bool IsDoubleFace { get; private set; }

    /// <summary>Gets or sets the best crossing's distance along the line.</summary>
    public double Closest { get; set; }

    /// <summary>Gets or sets the best crossing, in the mesh's frame; the line's end until one is taken.</summary>
    public Vector3f Point { get; set; }

    /// <summary>Gets the best crossing's face.</summary>
    public int Face { get; private set; } = -1;

    /// <summary>Gets or sets the best crossing's barycentric weights.</summary>
    public (float U, float V) HitUV { get; set; }

    /// <summary>Gets the box's low corner.</summary>
    public Vector3f Min { get; private set; }

    /// <summary>Gets the box's high corner.</summary>
    public Vector3f Max { get; private set; }

    /// <summary>Gets or sets a value indicating whether the walk is to stop after the face it is testing.</summary>
    public bool Stopped { get; set; }

    /// <summary><c>(lo ≤ S or lo ≤ E) and (hi ≥ S or hi ≥ E)</c>, an unordered comparison passing: an object's bounds against a line on one axis.</summary>
    /// <param name="lo">The bounds' low side.</param>
    /// <param name="hi">The bounds' high side.</param>
    /// <param name="start">The line's start on the axis.</param>
    /// <param name="end">Its end.</param>
    /// <returns>Whether the line may reach the bounds on this axis.</returns>
    public static bool Straddles(double lo, double hi, double start, double end) =>
        (!(lo > start) || !(lo > end)) && (!(hi < start) || !(hi < end));

    /// <summary>The search box: two points, padded by a centimetre either way.</summary>
    /// <param name="a">One corner's point.</param>
    /// <param name="b">The other's.</param>
    public void Box(Vector3f a, Vector3f b)
    {
        Min = new Vector3f(Lower(a.X, b.X), Lower(a.Y, b.Y), Lower(a.Z, b.Z));
        Max = new Vector3f(Upper(a.X, b.X), Upper(a.Y, b.Y), Upper(a.Z, b.Z));
    }

    /// <summary>The search box with no pad: the two points' lower and higher components, as the line-of-sight handler takes them.</summary>
    /// <param name="a">One corner's point.</param>
    /// <param name="b">The other's.</param>
    public void TightBox(Vector3f a, Vector3f b)
    {
        Min = new Vector3f(a.X < b.X ? a.X : b.X, a.Y < b.Y ? a.Y : b.Y, a.Z < b.Z ? a.Z : b.Z);
        Max = new Vector3f(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y, a.Z > b.Z ? a.Z : b.Z);
    }

    /// <summary>
    /// The tie rules: whether a crossing at this distance replaces the best. A face nearer by more than the window
    /// always does; one within the window does only when it faces the line, or the best does not.
    /// </summary>
    /// <param name="distance">The crossing's distance along the line.</param>
    /// <param name="front">Whether the line enters the face's front.</param>
    /// <returns>Whether the crossing may be taken.</returns>
    /// <remarks>The double-face flag is the solid-model handler's: it is set as the window is tested, taken or not.</remarks>
    public bool Accept(Fpu distance, bool front)
    {
        if (!IsHit || (distance + TieWindow).Value < Closest)
        {
            IsDoubleFace = false;
            return true;
        }

        if (distance.Value < Closest)
        {
            IsDoubleFace = true;
            return front || !PreviousFront;
        }

        if (!((distance - TieWindow).Value < Closest))
        {
            return false;
        }

        IsDoubleFace = true;
        return front && !PreviousFront;
    }

    /// <summary>Takes a crossing as the best, and closes the box onto it.</summary>
    /// <param name="distance">Its distance along the line.</param>
    /// <param name="front">Whether the line entered the face's front.</param>
    /// <param name="point">The crossing.</param>
    /// <param name="face">Its face.</param>
    /// <param name="start">The line's start, the box's other corner.</param>
    public void Take(Fpu distance, bool front, Vector3f point, int face, Vector3f start)
    {
        Closest = distance.Value;
        PreviousFront = front;
        Point = point;
        Face = face;
        HitThisObject = true;
        IsHit = true;
        Box(start, point);
    }

    /// <summary>The sphere-tree walk: depth first, each node culled by the line and the box, its faces handed to the visitor.</summary>
    /// <typeparam name="TFaces">The face test.</typeparam>
    /// <param name="mesh">The tree and its faces.</param>
    /// <param name="start">The line's start, in the mesh's frame.</param>
    /// <param name="d">The line's direction, end less start.</param>
    /// <param name="inverse">One over the direction's squared length.</param>
    /// <param name="uniformSums">
    /// Whether every node sums its squared distance x, y, z (the spline handler); the solid-model handler sums a
    /// child's z, x, y.
    /// </param>
    /// <param name="faces">The face test.</param>
    /// <remarks>
    /// A child that fails its test still has its own faces tested before the walk moves to its sibling — the first
    /// game's quirk, kept by both handlers. The root is never tested.
    /// </remarks>
    public void Walk<TFaces>(RetailRayMesh mesh, Vector3f start, Vector3f d, Fpu inverse, bool uniformSums, ref TFaces faces)
        where TFaces : struct, IIgi2FaceVisitor
    {
        int[] links = mesh.Links;
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
                if (!SphereOk(mesh, node, start, d, inverse, sibling: true))
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
                if (!SphereOk(mesh, node, start, d, inverse, sibling: uniformSums))
                {
                    break;
                }
            }

            int count = links[(4 * node) + 1];
            int firstFace = links[4 * node];
            for (int f = firstFace; f < firstFace + count; f++)
            {
                faces.Visit(f);
                if (Stopped)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Where a line crosses one face, if it does within the box and the line: the box cull on each axis, the face's
    /// normal from its two edges, the plane, and the barycentric test on the normal's dominant axis.
    /// </summary>
    /// <param name="v">The mesh's vertices, three floats each.</param>
    /// <param name="i0">The face's first vertex's offset into <paramref name="v"/>.</param>
    /// <param name="i1">Its second's.</param>
    /// <param name="i2">Its third's.</param>
    /// <param name="s">The line's start.</param>
    /// <param name="d">The line's direction.</param>
    /// <param name="flip">
    /// Whether the normal is reversed for the plane test (a flipped spline segment); the dominant-axis divisions keep
    /// the unreversed components, as the handler does.
    /// </param>
    /// <param name="planeSum">The order the plane distance's products are summed in.</param>
    /// <param name="crossing">Receives the crossing.</param>
    /// <returns>Whether the line crosses the face.</returns>
    public bool Cross(float[] v, int i0, int i1, int i2, Vector3f s, Vector3f d, bool flip, Igi2PlaneSum planeSum, out Igi2FaceCrossing crossing)
    {
        crossing = default;
        Vector3f lo = Min, hi = Max;
        for (int k = 0; k < 3; k++)
        {
            float bound = Component(lo, k);
            if (bound > v[i0 + k] && bound > v[i1 + k] && bound > v[i2 + k])
            {
                return false;
            }
        }

        for (int k = 0; k < 3; k++)
        {
            float bound = Component(hi, k);
            if (bound < v[i0 + k] && bound < v[i1 + k] && bound < v[i2 + k])
            {
                return false;
            }
        }

        float v0x = v[i0], v0y = v[i0 + 1], v0z = v[i0 + 2];
        float e0x = ((Fpu)v[i2] - v0x).F, e0y = ((Fpu)v[i2 + 1] - v0y).F, e0z = ((Fpu)v[i2 + 2] - v0z).F;
        float e1x = ((Fpu)v[i1] - v0x).F, e1y = ((Fpu)v[i1 + 1] - v0y).F, e1z = ((Fpu)v[i1 + 2] - v0z).F;
        float nx = (((Fpu)e1z * e0y) - ((Fpu)e1y * e0z)).F;
        float nyp = (((Fpu)e1z * e0x) - ((Fpu)e1x * e0z)).F;
        float ny = -nyp;
        float nz = (((Fpu)e1y * e0x) - ((Fpu)e1x * e0y)).F;
        float px0 = nx, py0 = ny, pz0 = nz;
        if (flip)
        {
            px0 = -nx;
            py0 = -ny;
            pz0 = -nz;
        }

        Fpu dot = ((Fpu)pz0 * d.Z) + ((Fpu)py0 * d.Y) + ((Fpu)px0 * d.X);
        if (dot.Value == 0.0)
        {
            return false;
        }

        Fpu ox = (Fpu)v0x - s.X, oy = (Fpu)v0y - s.Y, oz = (Fpu)v0z - s.Z;
        Fpu plane = planeSum switch
        {
            Igi2PlaneSum.ZYX => (oz * pz0) + (oy * py0) + (ox * px0),
            Igi2PlaneSum.XZY => (ox * px0) + (oz * pz0) + (oy * py0),
            _ => (oy * py0) + (ox * px0) + (oz * pz0),
        };
        Fpu t = plane / dot;
        float tf = t.F;
        if (t.Value < 0.0 || tf > 1f)
        {
            return false;
        }

        float px = (((Fpu)tf * d.X) + s.X).F, py = (((Fpu)tf * d.Y) + s.Y).F, pz = (((Fpu)tf * d.Z) + s.Z).F;
        float ax = Abs(px0), ay = Abs(py0), az = Abs(pz0);

        Fpu vv;
        Fpu u;
        if (ax > ay && ax > az)
        {
            if (nx == 0f)
            {
                return false;
            }

            float q2 = ((Fpu)pz - v0z).F, q1 = ((Fpu)py - v0y).F;
            vv = (((Fpu)q2 * e0y) - ((Fpu)q1 * e0z)) / nx;
            if (!InUnit(vv) || !Second(q1, q2, e0y, e0z, e1y, e1z, vv, out u))
            {
                return false;
            }
        }
        else if (ay > az)
        {
            if (nyp == 0f)
            {
                return false;
            }

            float q2 = ((Fpu)pz - v0z).F, q0 = ((Fpu)px - v0x).F;
            vv = (((Fpu)q2 * e0x) - ((Fpu)q0 * e0z)) / nyp;
            if (!InUnit(vv) || !Second(q0, q2, e0x, e0z, e1x, e1z, vv, out u))
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

            float q1 = ((Fpu)py - v0y).F, q0 = ((Fpu)px - v0x).F;
            vv = (((Fpu)q1 * e0x) - ((Fpu)q0 * e0y)) / nz;
            if (!InUnit(vv) || !Second(q0, q1, e0x, e0y, e1x, e1y, vv, out u))
            {
                return false;
            }
        }

        if (u.Value < 0.0 || (vv + u).Value > 1.0)
        {
            return false;
        }

        crossing = new Igi2FaceCrossing(dot, tf, new Vector3f(px, py, pz), u, vv);
        return true;
    }

    /// <summary>
    /// How lit a struck surface is: zero on a lightmapped building face, else the level's ambient plus the sun on the
    /// face, the three channels averaged.
    /// </summary>
    /// <param name="lightmappedFace">Whether the face is a building face with a lightmap the object carries.</param>
    /// <param name="normal">The face's unit normal in the world.</param>
    /// <param name="lighting">The level's light, or <see langword="null"/> for none.</param>
    /// <returns>The light, <c>vLight</c>.</returns>
    public static float Light(bool lightmappedFace, Vector3f normal, IIgi2SceneLighting? lighting)
    {
        if (lightmappedFace)
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

    /// <summary>A face's normal from its edges, normalised: the handlers' record of the struck face.</summary>
    /// <param name="a2x">The third vertex less the first, x, as the FPU holds it.</param>
    /// <param name="a2y">Its y.</param>
    /// <param name="a2z">Its z.</param>
    /// <param name="b1x">The second vertex less the first, x, stored.</param>
    /// <param name="b1y">Its y, stored.</param>
    /// <param name="b1z">Its z, as the FPU holds it.</param>
    /// <returns>The unit normal; a zero normal stays zero.</returns>
    public static Vector3f UnitNormal(Fpu a2x, Fpu a2y, Fpu a2z, float b1x, float b1y, Fpu b1z)
    {
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

        return new Vector3f(nx, ny, nz);
    }

    private static float Lower(float a, float b) => ((Fpu)(a < b ? a : b) - Pad).F;

    private static float Upper(float a, float b) => ((Fpu)(a > b ? a : b) + Pad).F;

    private bool SphereOk(RetailRayMesh mesh, int n, Vector3f start, Vector3f d, Fpu inverse, bool sibling)
    {
        float[] sp = mesh.Spheres;
        float cx = sp[4 * n], cy = sp[(4 * n) + 1], cz = sp[(4 * n) + 2], r = sp[(4 * n) + 3];
        Fpu ex = (Fpu)cx - start.X, ey = (Fpu)cy - start.Y, ez = (Fpu)cz - start.Z;
        Fpu projection = (ez * d.Z) + (ey * d.Y) + (ex * d.X);
        Fpu c2 = sibling ? (ex * ex) + (ey * ey) + (ez * ez) : (ez * ez) + (ex * ex) + (ey * ey);
        Fpu d2 = c2 - (projection * projection * inverse);
        if (((Fpu)r * r).Value < d2.Value)
        {
            return false;
        }

        Vector3f lo = Min, hi = Max;
        if (((Fpu)r + cx).Value < lo.X || ((Fpu)r + cy).Value < lo.Y || ((Fpu)r + cz).Value < lo.Z)
        {
            return false;
        }

        return !(((Fpu)cx - r).Value > hi.X) && !(((Fpu)cy - r).Value > hi.Y) && !(((Fpu)cz - r).Value > hi.Z);
    }

    /// <summary>
    /// The first barycentric weight from the larger of the two edge components on the plane's other axes; a zero
    /// second component ends the test.
    /// </summary>
    private static bool Second(float qa, float qb, float e0a, float e0b, float e1a, float e1b, Fpu vv, out Fpu u)
    {
        if (Abs(e0a) > Abs(e0b))
        {
            u = ((Fpu)qa - ((Fpu)e1a * vv)) / e0a;
            return true;
        }

        if (e0b == 0f)
        {
            u = default;
            return false;
        }

        u = ((Fpu)qb - ((Fpu)e1b * vv)) / e0b;
        return true;
    }

    /// <summary>A barycentric weight in the unit interval: not below zero, not above one; an unordered value passes.</summary>
    private static bool InUnit(Fpu w) => !(w.Value < 0.0) && !(w.Value > 1.0);

    private static float Abs(float a) => a < 0f ? -a : a;

    private static float Component(Vector3f v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
