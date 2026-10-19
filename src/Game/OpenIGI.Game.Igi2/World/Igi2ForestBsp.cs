using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One node of a stand's tree index: a tree, its two children, and its subtree's height range.</summary>
/// <param name="Tree">The tree this node holds, an index into the stand's tree list.</param>
/// <param name="Low">The child holding the trees below the split, or <see cref="Igi2ForestBsp.NoNode"/>.</param>
/// <param name="High">The child holding the trees above it, or <see cref="Igi2ForestBsp.NoNode"/>.</param>
/// <param name="MinZ">The lowest foot in this node's subtree, stand-local.</param>
/// <param name="MaxZ">The highest crown in it, stand-local.</param>
/// <remarks>
/// The original's node is 24 bytes — <c>Forest_CreateBSPTree</c> (<c>igi2ed 0x64C400</c>) bump-allocates
/// <c>24 * nTrees</c> of them — laid out as the tree pointer, the two heights, a packed clip word, then
/// the two children at <c>+16</c> and <c>+20</c>. <c>+16</c> is the low child and <c>+20</c> the high
/// one: <c>Forest_BuildBSPTree</c> (<c>0x64C610</c>) recurses into <c>node + 20</c> with the half above
/// the median and iterates into <c>node + 16</c> with the half below.
/// </remarks>
public readonly record struct Igi2ForestBspNode(int Tree, int Low, int High, float MinZ, float MaxZ);

/// <summary>
/// A stand's trees indexed the way <c>Forest_CreateBSPTree</c> indexes them, and walked the way
/// <c>Forest_DrawTrees1_Optimized</c> walks them.
/// </summary>
/// <remarks>
/// <para>
/// Despite the name the engine gives it, this is a two-dimensional k-d tree, not a BSP: the split plane
/// is always axis-aligned and the axis alternates with depth. <c>Forest_BuildBSPTree</c>
/// (<c>igi2ed 0x64C610</c>) sorts the sub-range it is given with
/// <c>Forest_TreeObjectCompareX</c> (<c>0x649180</c>) at even depths and
/// <c>Forest_TreeObjectCompareY</c> (<c>0x6491C0</c>) at odd ones — it picks the callback with
/// <c>depth &amp; 1</c> — takes the element at <c>count &gt;&gt; 1</c> as the node, and recurses on the
/// two halves either side of it. Every tree is a node; there is no leaf bucket.
/// </para>
/// <para>
/// Before the tree is built, <c>Forest_RemoveOutsiders</c> (<c>0x64C6D0</c>) drops every tree whose
/// local X or Y is beyond <c>AreaSize * 2048</c> and lowers the stand's tree count to match, so the root
/// region is exactly the square the stand is culled against.
/// </para>
/// <para>
/// <c>Forest_GenerateHeightDataRecursively</c> (<c>0x64C940</c>) then fills each node's height range
/// bottom-up: the minimum is the lowest of the node's own foot and its children's minima, the maximum
/// the highest of its children's maxima and its own crown, which is
/// <c>position.z + scale.z * modelHeight</c>. The walk needs it because the k-d tree partitions X and Y
/// only — Z has to come from somewhere, and it comes from here.
/// </para>
/// <para>
/// <b>Tie-breaking may differ from the original.</b> The engine sorts with <c>QVector_QSortPart</c>, an
/// unstable quicksort, so two trees sharing a coordinate can land either way round and pick a different
/// median. The tree that results is a different tree with the same contents; it culls the same trees but
/// its node order is not guaranteed to match the engine's.
/// </para>
/// </remarks>
public sealed class Igi2ForestBsp
{
    /// <summary>The child index that means "no child".</summary>
    public const int NoNode = -1;

    private readonly Igi2ForestBspNode[] _nodes;
    private readonly Vector3f[] _positions;
    private readonly Vector3f[] _scales;
    private readonly int[] _trees;

    private Igi2ForestBsp(
        Igi2ForestBspNode[] nodes,
        Vector3f[] positions,
        Vector3f[] scales,
        int[] trees,
        int root,
        int depth,
        float halfExtent)
    {
        _nodes = nodes;
        _positions = positions;
        _scales = scales;
        _trees = trees;
        Root = root;
        Depth = depth;
        HalfExtent = halfExtent;
    }

    /// <summary>Gets the root node's index, or <see cref="NoNode"/> when the stand holds no tree.</summary>
    public int Root { get; }

    /// <summary>Gets how many nodes the tree holds, which is how many trees survived the outsider pass.</summary>
    public int Count => _nodes.Length;

    /// <summary>Gets the deepest level the build reached, counting the root as zero.</summary>
    /// <remarks>
    /// The original tracks the same number in the stand (<c>ptForest + 1580</c>) for one reason: it sizes
    /// the explicit traversal stack, which it allocates as <c>24 * (depth + 1)</c> bytes.
    /// </remarks>
    public int Depth { get; }

    /// <summary>Gets how far the root region reaches either side of the stand's centre, in world units.</summary>
    public float HalfExtent { get; }

    /// <summary>Gets the stand-local positions of the trees the tree indexes, in tree-list order.</summary>
    public ReadOnlySpan<Vector3f> Positions => _positions;

    /// <summary>Gets the nodes, in the order the build allocated them.</summary>
    public ReadOnlySpan<Igi2ForestBspNode> Nodes => _nodes;

    /// <summary>Builds the index over a stand's trees.</summary>
    /// <param name="trees">The stand's trees, in file order.</param>
    /// <param name="centre">Where the stand sits, in world units — what the tree positions are relative to.</param>
    /// <param name="halfExtent">Half the stand's square, <c>AreaSize * 2048</c> in world units.</param>
    /// <param name="modelHeight">The tree model's Z extent in model units, before the per-tree scale.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trees"/> is <see langword="null"/>.</exception>
    public static Igi2ForestBsp Build(
        IReadOnlyList<Igi2Tree> trees,
        Vector3d centre,
        double halfExtent,
        float modelHeight)
    {
        ArgumentNullException.ThrowIfNull(trees);

        // Local, because the tree is: the engine keeps the stand's world position in the task and every
        // tree's position relative to it, and only adds the two back together inside the clip test. Doing
        // the same keeps the walk in single precision without losing anything — a stand is a few hundred
        // metres across and the world is millions of units wide.
        List<Vector3f> positions = new(trees.Count);
        List<Vector3f> scales = new(trees.Count);
        List<int> kept = new(trees.Count);

        float limit = (float)halfExtent;
        for (int i = 0; i < trees.Count; i++)
        {
            Igi2Tree tree = trees[i];
            Vector3f local = new(
                (float)(tree.Position.X - centre.X),
                (float)(tree.Position.Y - centre.Y),
                (float)(tree.Position.Z - centre.Z));

            // Forest_RemoveOutsiders, 0x64C6D0: |x| or |y| past the half-extent and the tree is dropped
            // from the vector and from the stand's count, before anything is built over it.
            if (Math.Abs(local.X) > limit || Math.Abs(local.Y) > limit)
            {
                continue;
            }

            positions.Add(local);
            scales.Add(tree.Scale);
            kept.Add(i);
        }

        Igi2ForestBspNode[] nodes = new Igi2ForestBspNode[positions.Count];
        Vector3f[] pos = [.. positions];
        int[] order = new int[pos.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        int allocated = 0;
        int deepest = 0;
        int root = Split(pos, order, nodes, ref allocated, ref deepest, 0, pos.Length, 0);

        Igi2ForestBsp bsp = new(nodes, pos, [.. scales], [.. kept], root, deepest, limit);
        bsp.GenerateHeights(root, modelHeight);
        return bsp;
    }

    /// <summary>Gets the index into the stand's original tree list that a node's tree came from.</summary>
    /// <param name="node">A node index.</param>
    /// <returns>The tree's position in the list the stand was built from.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The node index is not one of this tree's.</exception>
    public int TreeOf(int node)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, _nodes.Length);

        return _trees[_nodes[node].Tree];
    }

    /// <summary>Walks the tree near to far, handing every tree the clip planes keep to a visitor.</summary>
    /// <param name="planes">The stand's clip planes, with their origins relative to the camera.</param>
    /// <param name="standOffset">The stand's centre relative to the camera, in world units.</param>
    /// <param name="margin">How far a tree may lean out of its node's region; see <see cref="MarginOf"/>.</param>
    /// <param name="modelRadius">The tree model's radius in model units, before the per-tree scale.</param>
    /// <param name="modelHeight">The tree model's Z extent in model units, before the per-tree scale.</param>
    /// <param name="visit">Called with the index into the stand's tree list of every visible tree.</param>
    /// <returns>How many trees were handed to <paramref name="visit"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="visit"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This is <c>Forest_DrawTrees1_Optimized</c> (<c>igi2ed 0x64AE60</c>). It carries a running region —
    /// four floats the original calls <c>avCoords</c>, ordered max X, min X, max Y, min Y and seeded with
    /// the stand's square — and tightens one side of it on the way down, which is what gives each node a
    /// box to be tested against without storing one.
    /// </para>
    /// <para>
    /// The order is near to far. The child on the camera's own side of the split is descended first, then
    /// the node's own tree is drawn, then the far child — so a stand comes out roughly front to back,
    /// which is what the sorted foliage pass wants and what fills the depth buffer early for the rest.
    /// </para>
    /// <para>
    /// A node's box is inflated by <paramref name="margin"/> and a tree's is not. The region only bounds
    /// tree <em>positions</em>; a tree standing on the edge of it still has a crown hanging outside, and
    /// the margin is what stops the walk cutting that crown off. The per-tree test is exact, so it needs
    /// no such slack.
    /// </para>
    /// </remarks>
    public int Cull(
        in Igi2ForestClipPlanes planes,
        Vector3f standOffset,
        float margin,
        float modelRadius,
        float modelHeight,
        Action<int> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);

        if (Root == NoNode)
        {
            return 0;
        }

        Region region = new(HalfExtent, -HalfExtent, HalfExtent, -HalfExtent);
        int drawn = 0;
        Walk(
            planes,
            standOffset,
            margin,
            modelRadius,
            modelHeight,
            Root,
            0,
            region,
            Igi2ForestClipPlanes.NoPlanesInside,
            visit,
            ref drawn);
        return drawn;
    }

    /// <summary>How far a tree may lean out of the region its position sits in, in world units.</summary>
    /// <param name="modelRadius">The tree model's radius in model units.</param>
    /// <param name="randomScaleRangeX">The stand's <c>Random X-scale range</c>.</param>
    /// <param name="randomScaleRangeY">Its <c>Random Y-scale range</c>.</param>
    /// <returns>The margin every node's box is inflated by.</returns>
    /// <remarks>
    /// <c>(max(rangeX, rangeY) * 0.5 + 1) * modelRadius</c>, computed once at the top of
    /// <c>Forest_DrawTrees1_Optimized</c> from <c>ptForest + 1548</c>, <c>+ 1552</c> and the model radius
    /// at <c>+ 1560</c>. The two ranges are the scatter the editor's brush scales with, so
    /// <c>1 + range / 2</c> is the largest a tree in the stand can have been scaled to and the product is
    /// the widest crown the stand can hold. It is a bound on the data rather than a fudge factor.
    /// </remarks>
    public static float MarginOf(float modelRadius, float randomScaleRangeX, float randomScaleRangeY) =>
        ((Math.Max(randomScaleRangeX, randomScaleRangeY) * 0.5f) + 1f) * modelRadius;

    /// <inheritdoc/>
    public override string ToString() => $"forest index, {Count} node(s), depth {Depth}";

    private static int Split(
        Vector3f[] positions,
        int[] order,
        Igi2ForestBspNode[] nodes,
        ref int allocated,
        ref int deepest,
        int start,
        int count,
        int depth)
    {
        if (count <= 0)
        {
            return NoNode;
        }

        if (depth > deepest)
        {
            deepest = depth;
        }

        // The axis alternates with depth, and the median of the sorted sub-range is the node. The
        // original sorts the whole sub-range every time rather than partitioning around the median, which
        // is what makes the build O(n log^2 n) — kept, because the resulting tree is what is being
        // reproduced, not the cost of building it.
        Span<int> range = order.AsSpan(start, count);
        if ((depth & 1) == 0)
        {
            range.Sort((a, b) => positions[a].X.CompareTo(positions[b].X));
        }
        else
        {
            range.Sort((a, b) => positions[a].Y.CompareTo(positions[b].Y));
        }

        int mid = count >> 1;
        int node = allocated++;

        int low = Split(positions, order, nodes, ref allocated, ref deepest, start, mid, depth + 1);
        int high = Split(
            positions,
            order,
            nodes,
            ref allocated,
            ref deepest,
            start + mid + 1,
            count - mid - 1,
            depth + 1);

        nodes[node] = new Igi2ForestBspNode(order[start + mid], low, high, 0f, 0f);
        return node;
    }

    private void GenerateHeights(int node, float modelHeight)
    {
        if (node == NoNode)
        {
            return;
        }

        GenerateHeights(_nodes[node].Low, modelHeight);
        GenerateHeights(_nodes[node].High, modelHeight);

        Igi2ForestBspNode self = _nodes[node];
        float foot = _positions[self.Tree].Z;
        float crown = foot + (_scales[self.Tree].Z * modelHeight);

        float minZ = foot;
        float maxZ = crown;
        if (self.Low != NoNode)
        {
            minZ = Math.Min(minZ, _nodes[self.Low].MinZ);
            maxZ = Math.Max(maxZ, _nodes[self.Low].MaxZ);
        }

        if (self.High != NoNode)
        {
            minZ = Math.Min(minZ, _nodes[self.High].MinZ);
            maxZ = Math.Max(maxZ, _nodes[self.High].MaxZ);
        }

        _nodes[node] = self with { MinZ = minZ, MaxZ = maxZ };
    }

    private void Walk(
        in Igi2ForestClipPlanes planes,
        Vector3f standOffset,
        float margin,
        float modelRadius,
        float modelHeight,
        int node,
        int depth,
        Region region,
        int inside,
        Action<int> visit,
        ref int drawn)
    {
        Igi2ForestBspNode self = _nodes[node];
        Vector3f position = _positions[self.Tree];

        int axis = depth & 1;
        float split = axis == 0 ? position.X : position.Y;
        float relative = split + (axis == 0 ? standOffset.X : standOffset.Y);

        // The near child is the one on the camera's own side of the split. The original takes the sign
        // bit of the tree's camera-relative coordinate and indexes the two children with it, which is the
        // same choice written without a branch.
        int near = relative < 0f ? 1 : 0;
        int nearChild = near == 0 ? self.Low : self.High;
        int farChild = near == 0 ? self.High : self.Low;

        if (nearChild != NoNode)
        {
            Region tightened = region.With((2 * axis) + near, split);
            Igi2ForestBspNode child = _nodes[nearChild];
            if (!planes.IsBoxOutOfSight(
                    tightened.MaxX,
                    tightened.MinX,
                    tightened.MaxY,
                    tightened.MinY,
                    child.MinZ,
                    child.MaxZ,
                    standOffset,
                    margin,
                    inside,
                    out int childInside))
            {
                Walk(
                    planes,
                    standOffset,
                    margin,
                    modelRadius,
                    modelHeight,
                    nearChild,
                    depth + 1,
                    tightened,
                    childInside,
                    visit,
                    ref drawn);
            }
        }

        // The tree's own box, with the model's radius and height taken through its per-axis scale. No
        // margin: this bound is exact, and the original passes 0.0 for it here.
        Vector3f scale = _scales[self.Tree];
        float radiusX = scale.X * modelRadius;
        float radiusY = scale.Y * modelRadius;
        if (!planes.IsBoxOutOfSight(
                position.X + radiusX,
                position.X - radiusX,
                position.Y + radiusY,
                position.Y - radiusY,
                position.Z,
                position.Z + (scale.Z * modelHeight),
                standOffset,
                0f,
                inside,
                out _))
        {
            visit(_trees[self.Tree]);
            drawn++;
        }

        if (farChild != NoNode)
        {
            Region tightened = region.With((2 * axis) + (1 - near), split);
            Igi2ForestBspNode child = _nodes[farChild];
            if (!planes.IsBoxOutOfSight(
                    tightened.MaxX,
                    tightened.MinX,
                    tightened.MaxY,
                    tightened.MinY,
                    child.MinZ,
                    child.MaxZ,
                    standOffset,
                    margin,
                    inside,
                    out int childInside))
            {
                Walk(
                    planes,
                    standOffset,
                    margin,
                    modelRadius,
                    modelHeight,
                    farChild,
                    depth + 1,
                    tightened,
                    childInside,
                    visit,
                    ref drawn);
            }
        }
    }

    /// <summary>The running region, ordered the way the original's four running coordinates are.</summary>
    private readonly record struct Region(float MaxX, float MinX, float MaxY, float MinY)
    {
        public Region With(int slot, float value) => slot switch
        {
            0 => this with { MaxX = value },
            1 => this with { MinX = value },
            2 => this with { MaxY = value },
            _ => this with { MinY = value },
        };
    }
}

/// <summary>One of the forest's clip planes: a point on it and the normal that points out of the frustum.</summary>
/// <param name="Origin">A point on the plane, relative to the camera, in world units.</param>
/// <param name="Normal">The unit normal, pointing away from the visible side.</param>
public readonly record struct Igi2ForestClipPlane(Vector3f Origin, Vector3f Normal);

/// <summary>
/// The plane set a forest is culled against: the four frustum sides, the near plane, and the two that cut
/// the stand off at its view distance.
/// </summary>
/// <remarks>
/// <para>
/// <c>Forest_SetUpClippingPlanes</c> (<c>igi2ed 0x64B550</c>) builds these in view space and then rotates
/// them into world space by the inverted view matrix, offsetting each origin by the view point — which is
/// why a plane here is a point and a normal rather than a normal and a distance. The origins are kept
/// relative to the camera so the whole test stays in single precision.
/// </para>
/// <para>
/// A box is out of sight when <b>every</b> one of its eight corners is strictly on a single plane's
/// positive side. Straddling planes never reject, which is what makes this test conservative and cheap:
/// one plane, eight dot products, an early out on the first plane that rejects.
/// </para>
/// <para>
/// <b>The two cut-off planes are not a far plane.</b> They sit at <c>cutoff / halfTangentX</c> along the
/// view axis with normals turned <c>±halfFovX / 2</c> off it, so the pair meets the frustum's own side
/// planes at the same range and approximates a cylinder rather than a slab — a stand fades at the same
/// distance ahead as it does in the corner of the eye. <c>Forest</c>'s
/// <c>View cutoff (m) (limited by object lod settings)</c> is what sets the range;
/// <c>Forest_UpdateInternalDataHandler</c> converts it with <c>* 4096</c> into <c>ptForest + 1536</c>,
/// and it is that field the plane origin is built from.
/// </para>
/// </remarks>
public readonly struct Igi2ForestClipPlanes
{
    /// <summary>The inherited mask a walk starts with: nothing yet known to be wholly inside.</summary>
    public const int NoPlanesInside = 0;

    /// <summary>How many planes the set holds when the stand has a view cut-off.</summary>
    /// <remarks>
    /// The original's own count, <c>dword_8261A20</c>, is 7 for an ordinary draw and 5 for
    /// <c>eDrawMode == 1</c> — the mode that draws the forest into a lightmap, where a view distance
    /// would bake the cut-off into the light.
    /// </remarks>
    public const int PlaneCount = 7;

    private readonly Igi2ForestClipPlane[] _planes;

    private Igi2ForestClipPlanes(Igi2ForestClipPlane[] planes) => _planes = planes;

    /// <summary>Gets the planes, frustum sides first, then the near plane, then the two cut-off planes.</summary>
    public ReadOnlySpan<Igi2ForestClipPlane> Planes => _planes ?? [];

    /// <summary>Builds the set for one stand.</summary>
    /// <param name="camera">Where the world is seen from; its orientation's rows are the view axes.</param>
    /// <param name="horizontalHalfTangent">The tangent of the horizontal half field of view.</param>
    /// <param name="verticalHalfTangent">The tangent of the vertical half field of view.</param>
    /// <param name="cutoff">How far the stand is drawn, in world units; non-positive drops the cut-off pair.</param>
    /// <param name="near">Where the near plane sits along the view axis, in world units.</param>
    /// <returns>The plane set, with every origin relative to the camera.</returns>
    public static Igi2ForestClipPlanes Create(
        in Camera3D camera,
        float horizontalHalfTangent,
        float verticalHalfTangent,
        float cutoff,
        float near = 0f)
    {
        Matrix3f view = camera.Orientation;
        Vector3f right = new(view.M11, view.M12, view.M13);
        Vector3f up = new(view.M21, view.M22, view.M23);
        Vector3f forward = new(view.M31, view.M32, view.M33);

        float fovX = horizontalHalfTangent;
        float fovY = verticalHalfTangent;
        float kx = 1f / MathF.Sqrt((fovX * fovX) + 1f);
        float ky = 1f / MathF.Sqrt((fovY * fovY) + 1f);

        // The top plane's Y term is 1 / sqrt(fovY * fovX + 1) and not 1 / sqrt(fovY * fovY + 1). That is
        // what the binary computes -- Forest_tClipPlanes + 40 -- and it reads like a slip, because every
        // other side plane pairs an axis with its own half-tangent. It is reproduced rather than
        // corrected because it only ever widens the plane: the mixed term is the larger of the two under
        // any aspect wider than tall, so the normal tips out and the cull keeps more than a true top
        // plane would. Correcting it would cull trees the original draws.
        float mixed = 1f / MathF.Sqrt((fovY * fovX) + 1f);

        bool cut = cutoff > 0f && fovX > 0f;
        Igi2ForestClipPlane[] planes = new Igi2ForestClipPlane[cut ? PlaneCount : PlaneCount - 2];

        planes[0] = World(right, up, forward, default, new Vector3f(-kx, 0f, -fovX * kx));
        planes[1] = World(right, up, forward, default, new Vector3f(0f, mixed, -fovY * ky));
        planes[2] = World(right, up, forward, default, new Vector3f(kx, 0f, -fovX * kx));
        planes[3] = World(right, up, forward, default, new Vector3f(0f, -ky, -fovY * ky));
        planes[4] = World(right, up, forward, new Vector3f(0f, 0f, near), new Vector3f(0f, 0f, -1f));

        if (cut)
        {
            // Half of the horizontal half-angle, from the half-angle formulas on cos(halfFovX) = kx.
            float sin = MathF.Sqrt((1f - kx) * 0.5f);
            float cos = MathF.Sqrt((1f + kx) * 0.5f);
            Vector3f origin = new(0f, 0f, cutoff / fovX);
            planes[5] = World(right, up, forward, origin, new Vector3f(sin, 0f, cos));
            planes[6] = World(right, up, forward, origin, new Vector3f(-sin, 0f, cos));
        }

        return new Igi2ForestClipPlanes(planes);
    }

    /// <summary>Tests one stand-local box against the set.</summary>
    /// <param name="maxX">The box's greatest stand-local X.</param>
    /// <param name="minX">Its least.</param>
    /// <param name="maxY">Its greatest stand-local Y.</param>
    /// <param name="minY">Its least.</param>
    /// <param name="minZ">Its lowest stand-local Z.</param>
    /// <param name="maxZ">Its highest.</param>
    /// <param name="standOffset">The stand's centre relative to the camera, in world units.</param>
    /// <param name="margin">Added to the box's X and Y on both sides; Z is not widened.</param>
    /// <param name="inside">The bit set of planes an ancestor already found the box wholly inside of.</param>
    /// <param name="stillInside">Receives that set for this box, to hand to its children.</param>
    /// <returns><see langword="true"/> when a single plane has all eight corners on its outside.</returns>
    /// <remarks>
    /// <para>
    /// <c>Forest_IsAABBOutOfSight</c> (<c>igi2ed 0x64B200</c>), and the mask is the reason it is worth
    /// porting rather than replacing with a sphere test. A box wholly inside a plane has every descendant
    /// wholly inside it too, so the plane never has to be tested again down that branch — which is what
    /// makes a deep tree cheap near the middle of the screen, where most of the trees are.
    /// </para>
    /// <para>
    /// <b>Not ported:</b> the original also caches, in the node itself, the index of the plane that last
    /// rejected it, and tests that one first the next time round. That survives across frames, so it
    /// needs a mutable node; the mask here is a walk parameter and needs nothing.
    /// </para>
    /// </remarks>
    public bool IsBoxOutOfSight(
        float maxX,
        float minX,
        float maxY,
        float minY,
        float minZ,
        float maxZ,
        Vector3f standOffset,
        float margin,
        int inside,
        out int stillInside)
    {
        stillInside = inside;
        ReadOnlySpan<Igi2ForestClipPlane> planes = Planes;

        float x0 = minX - margin + standOffset.X;
        float x1 = maxX + margin + standOffset.X;
        float y0 = minY - margin + standOffset.Y;
        float y1 = maxY + margin + standOffset.Y;
        float z0 = minZ + standOffset.Z;
        float z1 = maxZ + standOffset.Z;

        int known = inside;
        for (int i = 0; i < planes.Length; i++)
        {
            if ((known & (1 << i)) != 0)
            {
                continue;
            }

            Igi2ForestClipPlane plane = planes[i];
            int outside = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                float x = ((corner & 2) != 0 ? x1 : x0) - plane.Origin.X;
                float y = ((corner & 4) != 0 ? y1 : y0) - plane.Origin.Y;
                float z = ((corner & 1) != 0 ? z1 : z0) - plane.Origin.Z;

                if ((x * plane.Normal.X) + (y * plane.Normal.Y) + (z * plane.Normal.Z) > 0f)
                {
                    outside |= 1 << corner;
                }
            }

            if (outside == 0xFF)
            {
                return true;
            }

            if (outside == 0)
            {
                known |= 1 << i;
            }
        }

        stillInside = known;
        return false;
    }

    private static Igi2ForestClipPlane World(
        Vector3f right,
        Vector3f up,
        Vector3f forward,
        Vector3f origin,
        Vector3f normal) =>
        new(
            (right * origin.X) + (up * origin.Y) + (forward * origin.Z),
            (right * normal.X) + (up * normal.Y) + (forward * normal.Z));
}

/// <summary>Which level of detail a tree draws at, and how far it has morphed towards the next one.</summary>
/// <param name="Level">The level to draw, or <see cref="Igi2ForestLod.NotDrawn"/> when it is past the cut-off.</param>
/// <param name="MorphLevel">The level being morphed towards, or <see cref="Igi2ForestLod.NotDrawn"/> when none.</param>
/// <param name="MorphFactor">How far between the two, in <c>[0, 1]</c>.</param>
public readonly record struct Igi2ForestLodChoice(int Level, int MorphLevel, float MorphFactor);

/// <summary>Picks a tree's level of detail from how far away it is.</summary>
/// <remarks>
/// <para>
/// <c>ForestRender_GetLOD</c> (<c>igi2ed 0x57D680</c>), the branch a model with a distance table takes.
/// The distances are stored in the model's LOD virtual model as five floats — the four level thresholds
/// and then the cut-off — and each is scaled by the largest of the tree's three per-axis scales before it
/// is compared, so a tree scattered half again as large stays detailed half again as far out.
/// </para>
/// <para>
/// <b>The last band is the one that matters here.</b> Past every threshold the tree is compared against
/// the cut-off, and beyond it the function returns −1 and <c>ForestRender_Draw</c> (<c>0x57CA90</c>)
/// returns without drawing anything at all. That is the per-tree half of the view distance the two extra
/// clip planes cut whole subtrees at.
/// </para>
/// <para>
/// <b>What could not be read.</b> The distance table lives in the <c>MTP</c> LOD virtual model, which
/// this port does not parse, so nothing here can supply the retail thresholds; a caller with one level
/// passes the stand's own view cut-off and gets exactly the retail behaviour for a one-level model.
/// And <c>ForestRender_vLODBiasInversed</c>, the factor the last band's distance is scaled by, is read at
/// <c>0x57D8B8</c> and <b>written nowhere in <c>igi2ed.exe</c></b> — so in the editor it is zero and the
/// cut-off never fires. Its companion <c>ForestRender_vLODBiasSquared</c> is <c>(bias * 16 + 1)²</c>, and
/// since the distance has already been multiplied by that under the square root, the reciprocal
/// <c>1 / (bias * 16 + 1)</c> is the only value that makes the comparison a comparison of real distances.
/// That is an inference from the arithmetic, not something read.
/// </para>
/// </remarks>
public static class Igi2ForestLod
{
    /// <summary>The level that means the tree is past its cut-off and is not drawn at all.</summary>
    public const int NotDrawn = -1;

    /// <summary>The fraction of a threshold the morph band spans; the original passes it as a literal.</summary>
    /// <remarks><c>ForestRender_Draw</c> calls <c>GetLOD</c> with <c>0.5</c> and nothing else ever does.</remarks>
    public const float MorphStep = 0.5f;

    /// <summary>Turns a level-of-detail bias into the factor the cut-off comparison uses.</summary>
    /// <param name="bias">The renderer's LOD bias, <c>ModelObj_GetLODBias()</c>.</param>
    /// <returns><c>1 / (bias * 16 + 1)</c>.</returns>
    public static float BiasInverseOf(float bias) => 1f / ((bias * 16f) + 1f);

    /// <summary>Turns the same bias into the factor the squared distance is scaled by.</summary>
    /// <param name="bias">The renderer's LOD bias.</param>
    /// <returns><c>(bias * 16 + 1)²</c>, which <c>ForestRender_Draw</c> computes once per forest.</returns>
    public static float BiasSquaredOf(float bias)
    {
        float scaled = (bias * 16f) + 1f;
        return scaled * scaled;
    }

    /// <summary>Picks the level of detail one tree draws at.</summary>
    /// <param name="distance">How far the tree's centre is from the camera, in world units, already biased.</param>
    /// <param name="scale">The largest of the tree's three per-axis scales.</param>
    /// <param name="thresholds">The level distances, then the cut-off last; at least <paramref name="levels"/> of them.</param>
    /// <param name="levels">How many levels the model has.</param>
    /// <param name="biasInverse">See <see cref="BiasInverseOf"/>; scales the distance for the last band only.</param>
    /// <returns>The level, the level it morphs towards, and how far.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="levels"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="thresholds"/> is too short for that many levels.</exception>
    public static Igi2ForestLodChoice Select(
        float distance,
        float scale,
        ReadOnlySpan<float> thresholds,
        int levels,
        float biasInverse)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        if (thresholds.Length < levels)
        {
            throw new ArgumentException(
                $"A {levels}-level model needs {levels} distances and the last of them is the cut-off; "
                    + $"only {thresholds.Length} were given.",
                nameof(thresholds));
        }

        // The morph band is the *first* threshold's half, whichever level is chosen -- the original
        // hoists it out of the loop rather than taking each level's own. A one-level model has no band.
        float band = levels <= 1 ? 0f : scale * thresholds[0] * MorphStep;

        for (int level = 0; level < levels - 1; level++)
        {
            float threshold = scale * thresholds[level];
            if (distance >= threshold)
            {
                continue;
            }

            float start = threshold - band;
            return distance < start
                ? new Igi2ForestLodChoice(level, NotDrawn, 0f)
                : new Igi2ForestLodChoice(level, level + 1, (distance - start) / band);
        }

        // Past every level: the last band fades the coarsest level out and then stops drawing.
        float cutoff = scale * thresholds[levels - 1];
        float fade = levels <= 1 ? 0f : scale * (thresholds[levels - 1] - thresholds[levels - 2]) * MorphStep;
        float faded = biasInverse * distance;
        if (faded >= cutoff)
        {
            return new Igi2ForestLodChoice(NotDrawn, NotDrawn, 0f);
        }

        float begin = cutoff - fade;
        return new Igi2ForestLodChoice(
            levels - 1,
            NotDrawn,
            faded < begin || fade <= 0f ? 0f : (faded - begin) / fade);
    }
}
