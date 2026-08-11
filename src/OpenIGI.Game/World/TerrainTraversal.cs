using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// The terrain octree descent: <c>sub_4D14E0</c>. Walks <c>terrain.ctr</c> from the camera, culls what
/// the frustum misses, applies the level's <c>DiscardTerrain</c> cubes, and emits one
/// <see cref="TerrainChunk"/> per node that is the coarsest detail good enough at its distance.
/// </summary>
/// <remarks>
/// <para>
/// A node's position is <b>accumulated during the descent</b> — the octant offset halves at every level —
/// so nothing in any terrain file holds a coordinate. The descent carries two of these accumulators at
/// once, one in world space for the distance and floor tests and one in view space for the frustum, both
/// as integers relative to the camera. That is the original's arrangement and it is reproduced here
/// including the arithmetic shifts, because culling decisions have to match at the boundary.
/// </para>
/// <para>
/// The original's descent also drives the object and sector lists that share the tree. Most of that half
/// is not reproduced here, but <c>DiscardTerrain</c> is: its area proxy forces descent to the authored
/// level, then its handler clears the matching node's terrain mask. Ignoring that interaction draws a
/// coarse terrain sheet over intentionally exposed model geometry such as mission 3's ammunition ramp.
/// </para>
/// </remarks>
public static class TerrainTraversal
{
    /// <summary>The shallowest chunk the original will draw; nodes above this are descended through only.</summary>
    /// <remarks>A chunk is emitted when its exponent exceeds 6, so the coarsest drawn chunk has exponent 7.</remarks>
    public const int MinimumExponent = 7;

    /// <summary>The finest level the descent will resolve.</summary>
    public const int MaximumExponent = 16;

    /// <summary>The exponent at which the heightmap starts to contribute; see <see cref="TerrainChunk.AppliesHeightmap"/>.</summary>
    public const int HeightmapExponent = 14;

    /// <summary>How many chunks the original's descriptor buffer holds.</summary>
    public const int MaximumChunks = 4096;

    /// <summary>Descends the tree and collects the chunks to draw.</summary>
    /// <param name="tree">The level's octree.</param>
    /// <param name="view">The camera and its frustum.</param>
    /// <param name="chunks">Receives the chunks; cleared first.</param>
    /// <param name="discards">The level's authored terrain holes, or <see langword="null"/> for none.</param>
    /// <returns>What the descent did.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A tree with <see cref="TerrainOctree.Defects"/> is walked anyway but cannot run off the end: every
    /// child index is re-checked here. That is a deliberate divergence — the original trusts the file and
    /// would follow a dangling link into whatever follows it in memory.
    /// </remarks>
    public static TerrainTraversalResult Collect(
        TerrainOctree tree,
        in TerrainView view,
        List<TerrainChunk> chunks,
        IReadOnlyList<TerrainDiscardRegion>? discards = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(chunks);

        chunks.Clear();

        // The camera is truncated to integers before anything else, as the original does: the whole
        // descent then runs in the same integer lattice the octant offsets live in.
        long cameraX = IgiMath.Trunc(view.Camera.Position.X);
        long cameraY = IgiMath.Trunc(view.Camera.Position.Y);
        long cameraZ = IgiMath.Trunc(view.Camera.Position.Z);

        Matrix3f rotation = view.Camera.Orientation;

        // The eight octant offsets of the root, in world space and rotated into view space. Every level
        // below reuses them shifted right, which is why they are built once.
        Span<Octant> octants = stackalloc Octant[TerrainOrientation.Count];
        for (int k = 0; k < TerrainOrientation.Count; k++)
        {
            int x = (k & 1) != 0 ? TerrainOctree.RootExtent : -TerrainOctree.RootExtent;
            int y = (k & 2) != 0 ? TerrainOctree.RootExtent : -TerrainOctree.RootExtent;
            int z = (k & 4) != 0 ? TerrainOctree.RootExtent : -TerrainOctree.RootExtent;
            octants[k] = new Octant(x, y, z);
        }

        // The root is centred on the world origin, so the accumulators start at minus the camera.
        Vector3i world = new((int)-cameraX, (int)-cameraY, (int)-cameraZ);

        var state = new State(
            tree,
            view,
            chunks,
            discards ?? Array.Empty<TerrainDiscardRegion>(),
            new Vector3i((int)cameraX, (int)cameraY, (int)cameraZ),
            octants);

        Descend(ref state, TerrainOctree.RootIndex, TerrainOrientation.Identity, 0, world, view.SlackX, view.SlackY);

        return new TerrainTraversalResult(chunks.Count, state.Dropped, state.Visited);
    }

    /// <summary>Visits one node and every child of it the view can see.</summary>
    /// <param name="state">The descent's invariants.</param>
    /// <param name="index">The node's record index.</param>
    /// <param name="orientation">The orientation the descent reached the node with.</param>
    /// <param name="depth">The node's depth, which is also its level-of-detail exponent.</param>
    /// <param name="world">The node's centre relative to the camera, in world space.</param>
    /// <param name="slackX">The frustum slack a child of this node is tested with, along X.</param>
    /// <param name="slackY">The same along Y.</param>
    private static void Descend(
        ref State state,
        int index,
        int orientation,
        int depth,
        Vector3i world,
        double slackX,
        double slackY)
    {
        state.Visited++;

        TerrainNode node = state.Tree[index];
        if (node.IsLeaf)
        {
            return;
        }

        int childDepth = depth + 1;
        int extent = TerrainOctree.RootExtent >> childDepth;

        // The slack halves once per level, so it is always the circumradius of the children being tested.
        slackX *= 0.5;
        slackY *= 0.5;

        for (int octant = 0; octant < TerrainOrientation.Count; octant++)
        {
            int slot = TerrainOrientation.ChildSlot(orientation, octant);
            if (!node.HasChild(slot))
            {
                continue;
            }

            ushort child = node.Child(slot);
            if (child >= state.Tree.Count)
            {
                continue;
            }

            Octant offset = state.Octants[octant];

            Vector3i childWorld = new(
                world.X + (offset.World.X >> childDepth),
                world.Y + (offset.World.Y >> childDepth),
                world.Z + (offset.World.Z >> childDepth));

            // The view-space position is computed from the world one rather than accumulated alongside
            // it. The original carries a second accumulator of pre-rotated octant offsets, but those are
            // single-precision products of numbers that reach 2^30 — a float holds 24 bits, so each is
            // already rounded to the nearest 128 units before the descent starts halving it, and the
            // rounding is not symmetric about the view axis. That is what tore the frustum away from the
            // picture: whole octants on one side were rejected while their mirror images were kept.
            (double x, double y, double z) = ToView(state.View.Camera.Orientation, childWorld);

            // The near plane rejects the most and costs the least.
            if (TerrainView.NearPlane - slackY >= z)
            {
                continue;
            }

            double limitX = (z * state.View.TangentX) + slackX;
            if (x >= limitX || x <= -limitX)
            {
                continue;
            }

            double limitY = (z * state.View.TangentY) + slackY;
            if (y >= limitY || y <= -limitY)
            {
                continue;
            }

            if (state.Camera.Z + childWorld.Z + extent < state.View.FloorZ)
            {
                continue;
            }

            Vector3d childCentre = new(
                state.Camera.X + childWorld.X,
                state.Camera.Y + childWorld.Y,
                state.Camera.Z + childWorld.Z);

            bool discarded = false;
            bool refineForDiscard = false;
            foreach (TerrainDiscardRegion discard in state.Discards)
            {
                if (discard.ContainsCube(childCentre, extent))
                {
                    discarded = true;
                    break;
                }

                if (childDepth < discard.Level && discard.IntersectsCube(childCentre, extent))
                {
                    refineForDiscard = true;
                }
            }

            if (discarded)
            {
                continue;
            }

            // Chebyshev distance to the chunk's near face, scaled down by its own size: the coarser the
            // chunk, the further it may be before it stops being worth resolving further.
            int reach = Math.Max(Math.Abs(childWorld.X), Math.Max(Math.Abs(childWorld.Y), Math.Abs(childWorld.Z)));
            int detail = (reach - extent) >> (25 - childDepth);

            int childOrientation = TerrainOrientation.Compose(orientation, node.Orientation(slot) & 7);

            if ((refineForDiscard || detail < state.View.ViewDistance) && childDepth < MaximumExponent)
            {
                Descend(ref state, child, childOrientation, childDepth, childWorld, slackX, slackY);
                continue;
            }

            if (childDepth <= MinimumExponent - 1)
            {
                continue;
            }

            if (state.Chunks.Count >= MaximumChunks)
            {
                state.Dropped++;
                continue;
            }

            state.Chunks.Add(new TerrainChunk(
                childCentre,
                childDepth,
                childOrientation,
                state.Tree[child].CommandOffset));
        }
    }

    /// <summary>Rotates a camera-relative world offset into view space, in double precision.</summary>
    private static (double X, double Y, double Z) ToView(in Matrix3f rotation, Vector3i offset)
    {
        double x = offset.X;
        double y = offset.Y;
        double z = offset.Z;

        return (
            (rotation.M11 * x) + (rotation.M12 * y) + (rotation.M13 * z),
            (rotation.M21 * x) + (rotation.M22 * y) + (rotation.M23 * z),
            (rotation.M31 * x) + (rotation.M32 * y) + (rotation.M33 * z));
    }

    /// <summary>An octant offset of the root, in world space.</summary>
    private readonly record struct Octant(int X, int Y, int Z)
    {
        public Vector3i World => new(X, Y, Z);
    }

    /// <summary>Everything the recursion needs that does not change as it goes.</summary>
    private ref struct State(
        TerrainOctree tree,
        in TerrainView view,
        List<TerrainChunk> chunks,
        IReadOnlyList<TerrainDiscardRegion> discards,
        Vector3i camera,
        Span<Octant> octants)
    {
        public readonly TerrainOctree Tree = tree;
        public readonly TerrainView View = view;
        public readonly List<TerrainChunk> Chunks = chunks;
        public readonly IReadOnlyList<TerrainDiscardRegion> Discards = discards;
        public readonly Vector3i Camera = camera;
        public readonly Span<Octant> Octants = octants;
        public int Dropped;
        public int Visited;
    }
}

/// <summary>What one terrain descent did.</summary>
/// <param name="Emitted">How many chunks were collected.</param>
/// <param name="Dropped">How many chunks passed every test but did not fit; see <see cref="TerrainTraversal.MaximumChunks"/>.</param>
/// <param name="Visited">How many nodes were entered, which is a measure of the descent's cost.</param>
public readonly record struct TerrainTraversalResult(int Emitted, int Dropped, int Visited);
