using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Tests one face of a mesh for a line, as the walk reaches it.</summary>
public interface IMeshFaceVisitor
{
    /// <summary>Tests one face.</summary>
    /// <param name="face">The face's index.</param>
    void Visit(int face);
}

/// <summary>The order a face test sums the plane distance's three products in, which the precision makes matter.</summary>
public enum MeshPlaneSum
{
    /// <summary>y, x, z: I.G.I. 2's solid-model line handler (<c>RigidDynCubeObj_TraceLinesHandler</c>).</summary>
    YXZ,

    /// <summary>z, y, x: Project I.G.I.'s model ray (<c>0x4C1840</c>) and I.G.I. 2's spline handler.</summary>
    ZYX,

    /// <summary>x, z, y: I.G.I. 2's solid-model line-of-sight handler (<c>RigidDynCubeObj_LineOfSightHandler</c>).</summary>
    XZY,
}

/// <summary>The order the sphere test sums a node's squared distance in.</summary>
public enum MeshSphereSums
{
    /// <summary>z, x, y at every node: Project I.G.I.'s walk.</summary>
    ZxyAlways,

    /// <summary>z, x, y for a child and x, y, z for a sibling: I.G.I. 2's solid-model handlers.</summary>
    ZxyChildXyzSibling,

    /// <summary>x, y, z at every node: I.G.I. 2's spline handler.</summary>
    XyzAlways,
}

/// <summary>
/// What a game's build of the engine's model ray chose for itself: how far the search box reaches past the line, the tie
/// window, and whether a comparison against a value that is not a number lets it through.
/// </summary>
/// <param name="Pad">How far the search box reaches past the line.</param>
/// <param name="TieWindow">How far behind the best a face may be and still win a tie.</param>
/// <param name="UnorderedPasses">
/// Whether an unordered comparison passes a test: the sequel's handlers test with negated comparisons, which a value that is
/// not a number passes; the first game's reject it.
/// </param>
public readonly record struct MeshRayDialect(float Pad, double TieWindow, bool UnorderedPasses)
{
    /// <summary>Gets Project I.G.I.'s (<c>0x4C1840</c>): a tenth of a unit either way, a tenth's tie, unordered values rejected.</summary>
    public static MeshRayDialect Igi1 => new(0.1f, 0.1, UnorderedPasses: false);

    /// <summary>Gets I.G.I. 2's (<c>0x58B3B0</c>): a centimetre either way, a centimetre's tie, unordered values passed.</summary>
    public static MeshRayDialect Igi2 => new(40.96f, 40.96, UnorderedPasses: true);
}

/// <summary>Where a line crosses a face: the plane's side, the parameter along the line, the point and the barycentric weights.</summary>
/// <typeparam name="TFpu">The precision.</typeparam>
/// <param name="Dot">The face's normal dotted with the line: below zero when the line enters the face's front.</param>
/// <param name="T">How far along the line, as stored: a float.</param>
/// <param name="Point">The crossing, in the mesh's frame.</param>
/// <param name="U">The weight of the face's third vertex.</param>
/// <param name="V">The weight of its second.</param>
public readonly record struct MeshFaceCrossing<TFpu>(TFpu Dot, float T, Vector3f Point, TFpu U, TFpu V)
    where TFpu : struct, IX87<TFpu>;

/// <summary>The model ray's tests that need no search: the broad straddle of an object's bounds.</summary>
public static class MeshRay
{
    /// <summary>An object's bounds against a line on one axis: <c>(lo ≤ S or lo ≤ E) and (hi ≥ S or hi ≥ E)</c>.</summary>
    /// <param name="dialect">The game's build.</param>
    /// <param name="lo">The bounds' low side.</param>
    /// <param name="hi">The bounds' high side.</param>
    /// <param name="start">The line's start on the axis.</param>
    /// <param name="end">Its end.</param>
    /// <returns>Whether the line may reach the bounds on this axis.</returns>
    public static bool Straddles(in MeshRayDialect dialect, double lo, double hi, double start, double end) =>
        dialect.UnorderedPasses
            ? (!(lo > start) || !(lo > end)) && (!(hi < start) || !(hi < end))
            : (lo <= start || lo <= end) && (hi >= start || hi >= end);
}

/// <summary>
/// One line's search through one model's collision mesh: the box the faces must reach, the nearest crossing so far, and
/// the sphere-tree walk and face test every model and spline-segment line handler of both games shares.
/// </summary>
/// <remarks>
/// <para>
/// Project I.G.I.'s model ray (<c>0x4C1840</c>) and I.G.I. 2's line handlers (<c>RigidDynCubeObj_TraceLinesHandler</c>,
/// <c>igi2 0x58B3B0</c>, and <c>SplineObjSegment_TraceLinesHandler</c>, <c>0x5B7390</c>) walk the same tree and test faces the
/// same way: a padded box around the line, a plane test, a barycentric test on the face's dominant axis, and a tie window that
/// lets a face a little behind the best through when it faces the line and the best does not.
/// </para>
/// <para>
/// What each build chose differently is a parameter: its pad and tie window and how it compares a value that is not a number
/// (<see cref="MeshRayDialect"/>), the order a handler sums the plane distance in (<see cref="MeshPlaneSum"/>) and the sphere
/// distance in (<see cref="MeshSphereSums"/>), and the precision. Each float the disassembly stores is stored.
/// </para>
/// </remarks>
/// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
/// <param name="dialect">The game's build.</param>
public sealed class MeshRaySearch<TFpu>(MeshRayDialect dialect)
    where TFpu : struct, IX87<TFpu>
{
    private readonly MeshRayDialect _dialect = dialect;

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

    /// <summary>The search box: two points, padded by the build's pad either way.</summary>
    /// <param name="a">One corner's point: the line's start.</param>
    /// <param name="b">The other's.</param>
    public void Box(Vector3f a, Vector3f b)
    {
        Min = new Vector3f(Lower(a.X, b.X), Lower(a.Y, b.Y), Lower(a.Z, b.Z));
        Max = new Vector3f(Upper(a.X, b.X), Upper(a.Y, b.Y), Upper(a.Z, b.Z));
    }

    /// <summary>The search box with no pad: the two points' lower and higher components, as I.G.I. 2's line-of-sight handler takes them.</summary>
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
    /// <remarks>The double-face flag is I.G.I. 2's solid-model handler's: it is set as the window is tested, taken or not.</remarks>
    public bool Accept(TFpu distance, bool front)
    {
        if (!IsHit || (distance + _dialect.TieWindow).Value < Closest)
        {
            IsDoubleFace = false;
            return true;
        }

        if (distance.Value < Closest)
        {
            IsDoubleFace = true;
            return front || !PreviousFront;
        }

        if (!((distance - _dialect.TieWindow).Value < Closest))
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
    public void Take(TFpu distance, bool front, Vector3f point, int face, Vector3f start)
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
    /// <param name="links">The tree's links.</param>
    /// <param name="spheres">The tree's spheres, four floats each: centre and radius.</param>
    /// <param name="start">The line's start, in the mesh's frame.</param>
    /// <param name="d">The line's direction, end less start.</param>
    /// <param name="inverse">One over the direction's squared length.</param>
    /// <param name="sums">The order a node's squared distance is summed in.</param>
    /// <param name="cull">Whether the spheres cull at all; a bent spline segment's do not.</param>
    /// <param name="faces">The face test.</param>
    /// <remarks>
    /// A child that fails its test still has its own faces tested before the walk moves to its sibling — the first
    /// game's quirk, kept by both. The root is never tested.
    /// </remarks>
    public void Walk<TFaces>(int[] links, float[] spheres, Vector3f start, Vector3f d, TFpu inverse, MeshSphereSums sums, bool cull, ref TFaces faces)
        where TFaces : struct, IMeshFaceVisitor
    {
        Walker<TFaces> walker = new(this, spheres, start, d, inverse, sums, cull, faces);
        SphereTreeWalk.Walk(links, ref walker);
        faces = walker.Faces;
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
    /// Whether the normal is reversed for the plane test (a mirrored spline segment); the dominant-axis divisions keep
    /// the unreversed components, as both builds do.
    /// </param>
    /// <param name="planeSum">The order the plane distance's products are summed in.</param>
    /// <param name="crossing">Receives the crossing.</param>
    /// <returns>Whether the line crosses the face.</returns>
    public bool Cross(float[] v, int i0, int i1, int i2, Vector3f s, Vector3f d, bool flip, MeshPlaneSum planeSum, out MeshFaceCrossing<TFpu> crossing)
    {
        ArgumentNullException.ThrowIfNull(v);
        crossing = default;
        bool unordered = _dialect.UnorderedPasses;
        Vector3f lo = Min, hi = Max;
        for (int k = 0; k < 3; k++)
        {
            float bound = Component(lo, k);
            if (unordered
                ? bound > v[i0 + k] && bound > v[i1 + k] && bound > v[i2 + k]
                : !(bound <= v[i0 + k] || bound <= v[i1 + k] || bound <= v[i2 + k]))
            {
                return false;
            }
        }

        for (int k = 0; k < 3; k++)
        {
            float bound = Component(hi, k);
            if (unordered
                ? bound < v[i0 + k] && bound < v[i1 + k] && bound < v[i2 + k]
                : !(bound >= v[i0 + k] || bound >= v[i1 + k] || bound >= v[i2 + k]))
            {
                return false;
            }
        }

        float v0x = v[i0], v0y = v[i0 + 1], v0z = v[i0 + 2];
        float e0x = ((TFpu)v[i2] - v0x).F, e0y = ((TFpu)v[i2 + 1] - v0y).F, e0z = ((TFpu)v[i2 + 2] - v0z).F;
        float e1x = ((TFpu)v[i1] - v0x).F, e1y = ((TFpu)v[i1 + 1] - v0y).F;
        TFpu e1zr = (TFpu)v[i1 + 2] - v0z;
        float e1z = e1zr.F;
        float nx = (e1zr * e0y - (TFpu)e1y * e0z).F;
        float nyp = ((TFpu)e1z * e0x - (TFpu)e1x * e0z).F;
        float nz = ((TFpu)e1y * e0x - (TFpu)e1x * e0y).F;
        float px0 = nx, py0 = -nyp, pz0 = nz;
        if (flip)
        {
            px0 = -px0;
            py0 = -py0;
            pz0 = -pz0;
        }

        TFpu dot = (TFpu)pz0 * d.Z + (TFpu)py0 * d.Y + (TFpu)px0 * d.X;
        if (dot.Value == 0.0 || (!unordered && double.IsNaN(dot.Value)))
        {
            return false;
        }

        TFpu ox = (TFpu)v0x - s.X, oy = (TFpu)v0y - s.Y, oz = (TFpu)v0z - s.Z;
        TFpu plane = planeSum switch
        {
            MeshPlaneSum.ZYX => oz * pz0 + oy * py0 + ox * px0,
            MeshPlaneSum.XZY => ox * px0 + oz * pz0 + oy * py0,
            _ => oy * py0 + ox * px0 + oz * pz0,
        };
        TFpu t = plane / dot;
        float tf = t.F;
        if (t.Value < 0.0 || (unordered ? tf > 1f : !(tf <= 1f)))
        {
            return false;
        }

        float px = ((TFpu)tf * d.X + s.X).F, py = ((TFpu)tf * d.Y + s.Y).F, pz = ((TFpu)tf * d.Z + s.Z).F;
        float ax = MathF.Abs(px0), ay = MathF.Abs(py0), az = MathF.Abs(pz0);

        TFpu vv;
        TFpu u;
        if (ax > ay && ax > az)
        {
            if (nx == 0f)
            {
                return false;
            }

            float q2 = ((TFpu)pz - v0z).F, q1 = ((TFpu)py - v0y).F;
            vv = ((TFpu)q2 * e0y - (TFpu)q1 * e0z) / nx;
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

            float q2 = ((TFpu)pz - v0z).F, q0 = ((TFpu)px - v0x).F;
            vv = ((TFpu)q2 * e0x - (TFpu)q0 * e0z) / nyp;
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

            float q1 = ((TFpu)py - v0y).F, q0 = ((TFpu)px - v0x).F;
            vv = ((TFpu)q1 * e0x - (TFpu)q0 * e0y) / nz;
            if (!InUnit(vv) || !Second(q0, q1, e0x, e0y, e1x, e1y, vv, out u))
            {
                return false;
            }
        }

        if (u.Value < 0.0 || (unordered ? (vv + u).Value > 1.0 : !((u + vv).Value <= 1.0)))
        {
            return false;
        }

        crossing = new MeshFaceCrossing<TFpu>(dot, tf, new Vector3f(px, py, pz), u, vv);
        return true;
    }

    // The first barycentric weight from the larger of the two edge components on the plane's other axes; a zero second
    // component ends the test.
    private static bool Second(float qa, float qb, float e0a, float e0b, float e1a, float e1b, TFpu vv, out TFpu u)
    {
        if (MathF.Abs(e0a) > MathF.Abs(e0b))
        {
            u = ((TFpu)qa - (TFpu)e1a * vv) / e0a;
            return true;
        }

        if (e0b == 0f)
        {
            u = default;
            return false;
        }

        u = ((TFpu)qb - (TFpu)e1b * vv) / e0b;
        return true;
    }

    private static float Component(Vector3f v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    // A barycentric weight in the unit interval: not below zero, not above one. The sequel's negated tests pass a value that is
    // not a number; the first game's do not.
    private bool InUnit(TFpu w) => _dialect.UnorderedPasses ? !(w.Value < 0.0) && !(w.Value > 1.0) : !(w.Value < 0.0) && w.Value <= 1.0;

    private float Lower(float a, float b) => ((TFpu)(a < b ? a : b) - _dialect.Pad).F;

    private float Upper(float a, float b) =>
        ((TFpu)(_dialect.UnorderedPasses ? (a > b ? a : b) : (a <= b ? b : a)) + _dialect.Pad).F;

    /// <summary>
    /// Whether a line may reach a node of the sphere tree: its closest approach within the sphere, and the sphere within the search
    /// box.
    /// </summary>
    /// <param name="sp">The tree's spheres, four floats each.</param>
    /// <param name="n">The node.</param>
    /// <param name="start">The line's start.</param>
    /// <param name="d">The line's direction.</param>
    /// <param name="inverse">One over the direction's squared length.</param>
    /// <param name="xyz">Whether the squared distance is summed x, y, z rather than z, x, y.</param>
    /// <returns>Whether the line may reach it.</returns>
    public bool Meets(float[] sp, int n, Vector3f start, Vector3f d, TFpu inverse, bool xyz)
    {
        ArgumentNullException.ThrowIfNull(sp);
        float cx = sp[4 * n], cy = sp[(4 * n) + 1], cz = sp[(4 * n) + 2], r = sp[(4 * n) + 3];
        TFpu ex = (TFpu)cx - start.X, ey = (TFpu)cy - start.Y, ez = (TFpu)cz - start.Z;
        TFpu projection = ez * d.Z + ey * d.Y + ex * d.X;
        TFpu c2 = xyz ? ex * ex + ey * ey + ez * ez : ez * ez + ex * ex + ey * ey;
        TFpu d2 = c2 - (projection * projection * inverse);
        TFpu r2 = (TFpu)r * r;
        if (_dialect.UnorderedPasses ? r2.Value < d2.Value : !(r2.Value >= d2.Value))
        {
            return false;
        }

        Vector3f lo = Min, hi = Max;
        if (((TFpu)r + cx).Value < lo.X || ((TFpu)cy + r).Value < lo.Y || ((TFpu)cz + r).Value < lo.Z)
        {
            return false;
        }

        return _dialect.UnorderedPasses
            ? !(((TFpu)cx - r).Value > hi.X) && !(((TFpu)cy - r).Value > hi.Y) && !(((TFpu)cz - r).Value > hi.Z)
            : ((TFpu)cx - r).Value <= hi.X && ((TFpu)cy - r).Value <= hi.Y && ((TFpu)cz - r).Value <= hi.Z;
    }

    private struct Walker<TFaces>(
        MeshRaySearch<TFpu> search,
        float[] spheres,
        Vector3f start,
        Vector3f d,
        TFpu inverse,
        MeshSphereSums sums,
        bool cull,
        TFaces faces) : ISphereTreeVisitor
        where TFaces : struct, IMeshFaceVisitor
    {
        public TFaces Faces = faces;

        public readonly bool Sphere(int node, bool sibling) =>
            !cull || search.Meets(
                spheres,
                node,
                start,
                d,
                inverse,
                xyz: sums == MeshSphereSums.XyzAlways || (sums == MeshSphereSums.ZxyChildXyzSibling && sibling));

        public bool Face(int face)
        {
            Faces.Visit(face);
            return !search.Stopped;
        }
    }
}
