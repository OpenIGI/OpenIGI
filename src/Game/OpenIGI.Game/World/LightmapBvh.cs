using System.Runtime.CompilerServices;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>A bounding-volume hierarchy over the lightmapped triangles, for the gather rays to trace.</summary>
/// <remarks>
/// A median-split BVH: the triangles are recursively partitioned by the midpoint of their centroids
/// along the widest axis, so a ray tests a logarithmic number of them rather than all thirty-odd
/// thousand. Distances are in world metres, matching the triangle corners.
/// </remarks>
public sealed class LightmapBvh
{
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private readonly Edges[] _edges;

    private LightmapBvh(LightmapTriangle[] triangles, int[] order, Node[] nodes)
    {
        _order = order;
        _nodes = nodes;

        // The corners a leaf actually intersects, copied into traversal order and reduced to what
        // Moeller-Trumbore reads. Two things were costing more than the arithmetic ever did.
        //
        // A leaf's triangles were reached as `_triangles[_order[start + k]]`: a dependent load, then
        // a hundred-and-twelve-byte record picked out of the original array at a scattered index.
        // Four triangles of one leaf could sit anywhere, so a leaf was four scattered lines rather
        // than a run. Here they are contiguous and in the order they are read.
        //
        // And the record carries a target index, six lightmap coordinates and an albedo, none of
        // which an intersection test looks at. Keeping only the corner and the two edges -- already
        // subtracted, since they are the same for every ray -- is seventy-two bytes rather than a
        // hundred and twelve, and no subtractions per test.
        _edges = new Edges[order.Length];

        for (int i = 0; i < order.Length; i++)
        {
            LightmapTriangle triangle = triangles[order[i]];

            _edges[i] = new Edges(
                triangle.A,
                new Vector3d(triangle.B.X - triangle.A.X, triangle.B.Y - triangle.A.Y, triangle.B.Z - triangle.A.Z),
                new Vector3d(triangle.C.X - triangle.A.X, triangle.C.Y - triangle.A.Y, triangle.C.Z - triangle.A.Z));
        }
    }

    /// <summary>A triangle as an intersection test wants it: one corner and the two edges from it.</summary>
    private readonly struct Edges(Vector3d a, Vector3d edge1, Vector3d edge2)
    {
        public readonly Vector3d A = a;
        public readonly Vector3d Edge1 = edge1;
        public readonly Vector3d Edge2 = edge2;
    }

    /// <summary>A node waiting to be visited, with the distance at which the ray enters it.</summary>
    private readonly struct StackEntry(int index, double entry)
    {
        public readonly int Index = index;
        public readonly double Entry = entry;
    }

    private struct Node
    {
        public Vector3d Min;
        public Vector3d Max;
        public int Start;
        public int Count;
        public int Left;
        public int Right;
    }

    /// <summary>Builds a hierarchy over the triangles.</summary>
    /// <param name="triangles">The triangles from <see cref="LightmapBaker.BuildTriangles"/>.</param>
    /// <returns>The built hierarchy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triangles"/> is null.</exception>
    public static LightmapBvh Build(LightmapTriangle[] triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);

        int count = triangles.Length;
        int[] order = new int[count];
        Vector3d[] centroid = new Vector3d[count];
        Vector3d[] triMin = new Vector3d[count];
        Vector3d[] triMax = new Vector3d[count];

        for (int i = 0; i < count; i++)
        {
            order[i] = i;
            LightmapTriangle t = triangles[i];
            triMin[i] = MinVec(MinVec(t.A, t.B), t.C);
            triMax[i] = MaxVec(MaxVec(t.A, t.B), t.C);
            centroid[i] = new Vector3d(
                (triMin[i].X + triMax[i].X) * 0.5,
                (triMin[i].Y + triMax[i].Y) * 0.5,
                (triMin[i].Z + triMax[i].Z) * 0.5);
        }

        List<Node> nodes = [];
        if (count > 0)
        {
            BuildRange(nodes, order, centroid, triMin, triMax, 0, count);
        }

        return new LightmapBvh(triangles, order, [.. nodes]);
    }

    private static int BuildRange(
        List<Node> nodes, int[] order, Vector3d[] centroid, Vector3d[] triMin, Vector3d[] triMax,
        int start, int count)
    {
        Vector3d min = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d max = new(double.MinValue, double.MinValue, double.MinValue);
        Vector3d centreMin = min;
        Vector3d centreMax = max;

        for (int i = start; i < start + count; i++)
        {
            int tri = order[i];
            min = MinVec(min, triMin[tri]);
            max = MaxVec(max, triMax[tri]);
            centreMin = MinVec(centreMin, centroid[tri]);
            centreMax = MaxVec(centreMax, centroid[tri]);
        }

        int node = nodes.Count;
        nodes.Add(default);

        // A small leaf, or a range whose centroids all coincide (no axis to split on), becomes a leaf.
        double ex = centreMax.X - centreMin.X;
        double ey = centreMax.Y - centreMin.Y;
        double ez = centreMax.Z - centreMin.Z;

        if (count <= 4 || (ex <= 1e-9 && ey <= 1e-9 && ez <= 1e-9))
        {
            nodes[node] = new Node { Min = min, Max = max, Start = start, Count = count, Left = -1, Right = -1 };
            return node;
        }

        int axis = ex >= ey && ex >= ez ? 0 : (ey >= ez ? 1 : 2);
        Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
            Axis(centroid[a], axis).CompareTo(Axis(centroid[b], axis))));

        int mid = count / 2;
        int left = BuildRange(nodes, order, centroid, triMin, triMax, start, mid);
        int right = BuildRange(nodes, order, centroid, triMin, triMax, start + mid, count - mid);
        nodes[node] = new Node { Min = min, Max = max, Start = 0, Count = 0, Left = left, Right = right };
        return node;
    }

    /// <summary>The nearest triangle a ray meets, with the barycentric coordinates of the hit.</summary>
    /// <param name="origin">The ray start, world metres.</param>
    /// <param name="direction">The ray direction, need not be unit length.</param>
    /// <param name="maxDistance">How far to look, in the same units as <paramref name="direction"/> is long.</param>
    /// <param name="triangle">The hit triangle's index, or −1 for a miss.</param>
    /// <param name="u">The hit's barycentric weight towards <see cref="LightmapTriangle.B"/>.</param>
    /// <param name="v">Its weight towards <see cref="LightmapTriangle.C"/>.</param>
    /// <returns>Whether anything was hit.</returns>
    public bool ClosestHit(Vector3d origin, Vector3d direction, double maxDistance, out int triangle, out double u, out double v)
    {
        triangle = -1;
        u = 0;
        v = 0;

        if (_nodes.Length == 0)
        {
            return false;
        }

        Vector3d inverse = new(1.0 / direction.X, 1.0 / direction.Y, 1.0 / direction.Z);
        double closest = maxDistance;

        // Front to back, and the stack carries the distance at which each node is entered alongside
        // its index. The first version pushed both children in a fixed order and slab-tested them on
        // the way out, which is correct but does almost no pruning: `closest` only tightens once a
        // triangle is actually hit, and visiting the far side of the level first means it tightens
        // last. Testing the children before pushing, visiting the nearer one first, and dropping a
        // node whose entry distance is already beyond the closest hit lets the very first triangle
        // found cut off most of what remains.
        //
        // Exact, not approximate: a node entered at a distance no nearer than the closest hit cannot
        // contain anything nearer, so nothing that could win is skipped.
        // Sized to the depth a median split can reach, not to a round number. The span is
        // zero-initialised on entry -- that is what C# does for a stackalloc unless the assembly
        // opts out -- so every unused entry is a store this ray pays for and never reads. At 128
        // entries of two spans that was a kilobyte and a half of zeroing per ray, against a
        // traversal that visits nine nodes.
        //
        // Sixty-four is past what the build can produce: it halves the range at every level, so a
        // level with a hundred million triangles would still fit inside forty.
        Span<StackEntry> stack = stackalloc StackEntry[64];
        int top = 0;

        ref readonly Node root = ref _nodes[0];

        if (!SlabTest(in root.Min, in root.Max, in origin, in inverse, closest, out double rootEntry))
        {
            return false;
        }

        stack[top] = new StackEntry(0, rootEntry);
        top++;

        while (top > 0)
        {
            top--;

            // Re-checked on the way out as well as on the way in: this node was worth visiting when
            // it was pushed, but a hit found since may have moved the closest distance in front of
            // it.
            if (stack[top].Entry >= closest)
            {
                continue;
            }

            ref readonly Node node = ref _nodes[stack[top].Index];

            if (node.Count > 0)
            {
                for (int k = 0; k < node.Count; k++)
                {
                    int slot = node.Start + k;

                    if (Intersect(origin, direction, in _edges[slot], out double t, out double bu, out double bv)
                        && t > 1e-5 && t < closest)
                    {
                        closest = t;

                        // The caller's index, read only when something is actually hit rather than
                        // once per candidate.
                        triangle = _order[slot];
                        u = bu;
                        v = bv;
                    }
                }

                continue;
            }

            ref readonly Node left = ref _nodes[node.Left];
            ref readonly Node right = ref _nodes[node.Right];

            bool takeLeft =
                SlabTest(in left.Min, in left.Max, in origin, in inverse, closest, out double leftEntry);
            bool takeRight =
                SlabTest(in right.Min, in right.Max, in origin, in inverse, closest, out double rightEntry);

            if (takeLeft && takeRight)
            {
                if (top + 2 > stack.Length)
                {
                    continue;
                }

                // The farther child goes on first so the nearer one comes off first.
                if (leftEntry <= rightEntry)
                {
                    stack[top++] = new StackEntry(node.Right, rightEntry);
                    stack[top++] = new StackEntry(node.Left, leftEntry);
                }
                else
                {
                    stack[top++] = new StackEntry(node.Left, leftEntry);
                    stack[top++] = new StackEntry(node.Right, rightEntry);
                }
            }
            else if (takeLeft && top < stack.Length)
            {
                stack[top++] = new StackEntry(node.Left, leftEntry);
            }
            else if (takeRight && top < stack.Length)
            {
                stack[top++] = new StackEntry(node.Right, rightEntry);
            }
        }

        return triangle >= 0;
    }

    private static double Axis(Vector3d value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

    private static Vector3d MinVec(Vector3d a, Vector3d b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Vector3d MaxVec(Vector3d a, Vector3d b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    // The ray-slab test: the ray hits the box if the entry into all three slabs precedes the exit from
    // any of them, and the exit is ahead of the ray and nearer than the current closest hit.
    //
    // Also reports where the ray enters, which is what lets the traversal visit the nearer child
    // first and drop a node the closest hit has already overtaken. Clamped at zero so a box the ray
    // starts inside reports an entry of zero rather than a negative distance, and therefore sorts
    // ahead of everything.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SlabTest(
        in Vector3d min, in Vector3d max, in Vector3d origin, in Vector3d inverse,
        double closest, out double entry)
    {
        double tMin = double.NegativeInfinity;
        double tMax = double.PositiveInfinity;

        Narrow(min.X, max.X, origin.X, inverse.X, ref tMin, ref tMax);
        Narrow(min.Y, max.Y, origin.Y, inverse.Y, ref tMin, ref tMax);
        Narrow(min.Z, max.Z, origin.Z, inverse.Z, ref tMin, ref tMax);

        entry = Math.Max(tMin, 0.0);
        return tMax >= entry && tMin < closest;
    }

    /// <summary>Narrows a ray's live range to one slab of a box.</summary>
    /// <remarks>
    /// <b>The NaN case is a real bug this had, not a hypothetical.</b> A ray parallel to an axis has
    /// a zero in that component of its direction, so the reciprocal is an infinity; if its origin
    /// also lies exactly on one of the box's two planes for that axis, the extent is zero and the
    /// product is zero times infinity, which is NaN. <see cref="Math.Min(double, double)"/> and
    /// <see cref="Math.Max(double, double)"/> propagate NaN, so the range came back NaN, the final
    /// comparison was false, and <b>the node was rejected</b> — the traversal quietly discarded
    /// geometry the ray genuinely meets.
    /// <para>
    /// Checked against a full search over every triangle, that lost 240 of 3,456 axis-aligned rays:
    /// a ray fired straight down from just above the ground reported open sky. In a bake that is a
    /// texel lit that should have been shadowed, on a level whose architecture is almost entirely
    /// axis-aligned, in a result that is cached and never compared against anything.
    /// </para>
    /// <para>
    /// A ray in that position lies exactly on the boundary of the slab, so the slab constrains
    /// nothing and the axis is skipped. Erring towards keeping the node is the safe direction: a
    /// node kept costs a triangle test, a node dropped costs the hit.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Narrow(
        double low, double high, double origin, double inverse, ref double tMin, ref double tMax)
    {
        double t1 = (low - origin) * inverse;
        double t2 = (high - origin) * inverse;

        // Min and Max propagate a NaN operand, so a poisoned axis arrives here as a NaN pair, and
        // MaxNumber and MinNumber are the IEEE forms that return the other operand instead of the
        // NaN -- which is exactly "leave the running range alone". An explicit `if (IsNaN)` measured
        // the same to within noise, so this is written for what it says rather than for speed.
        double near = Math.Min(t1, t2);
        double far = Math.Max(t1, t2);

        tMin = double.MaxNumber(tMin, near);
        tMax = double.MinNumber(tMax, far);
    }

    // Moeller-Trumbore, in double precision because the world coordinates are tens of kilometres from
    // the origin and a single-precision cross product there loses the triangle.
    private static bool Intersect(
        Vector3d origin, Vector3d direction, in Edges tri, out double t, out double u, out double v)
    {
        t = 0;
        u = 0;
        v = 0;

        Vector3d edge1 = tri.Edge1;
        Vector3d edge2 = tri.Edge2;

        Vector3d p = new(
            (direction.Y * edge2.Z) - (direction.Z * edge2.Y),
            (direction.Z * edge2.X) - (direction.X * edge2.Z),
            (direction.X * edge2.Y) - (direction.Y * edge2.X));

        double determinant = (edge1.X * p.X) + (edge1.Y * p.Y) + (edge1.Z * p.Z);

        if (determinant is > -1e-12 and < 1e-12)
        {
            return false;
        }

        double inverse = 1.0 / determinant;
        Vector3d s = new(origin.X - tri.A.X, origin.Y - tri.A.Y, origin.Z - tri.A.Z);
        u = ((s.X * p.X) + (s.Y * p.Y) + (s.Z * p.Z)) * inverse;

        if (u is < 0.0 or > 1.0)
        {
            return false;
        }

        Vector3d q = new(
            (s.Y * edge1.Z) - (s.Z * edge1.Y),
            (s.Z * edge1.X) - (s.X * edge1.Z),
            (s.X * edge1.Y) - (s.Y * edge1.X));

        v = ((direction.X * q.X) + (direction.Y * q.Y) + (direction.Z * q.Z)) * inverse;

        if (v < 0.0 || u + v > 1.0)
        {
            return false;
        }

        t = ((edge2.X * q.X) + (edge2.Y * q.Y) + (edge2.Z * q.Z)) * inverse;
        return t > 0.0;
    }
}
