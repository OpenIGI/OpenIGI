
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Where a point descent stopped: the node it reached and the cube that node covers.
/// </summary>
/// <param name="NodeIndex">The record the descent ended on.</param>
/// <param name="Depth">How many levels it managed before the tree ran out.</param>
/// <param name="Extent">The node's half-extent, halving at each level from <see cref="TerrainOctree.RootExtent"/>.</param>
/// <param name="Centre">The node's centre, in the same biased space the descent was given.</param>
/// <param name="Orientation">
/// The orientation folded through the descent, which every read of the node's contents has to be
/// interpreted under.
/// </param>
/// <param name="Shift">The bit the next level would test; <see cref="TerrainPointDescent.RootShift"/> minus <see cref="Depth"/>.</param>
public readonly record struct TerrainDescentCursor(
    int NodeIndex,
    int Depth,
    int Extent,
    Vector3i Centre,
    int Orientation,
    int Shift);

/// <summary>
/// Descends the octree to the deepest node containing a point — the walk every collision query starts with.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>0x4C9A30</c>, and it is the shared front half of the whole collision module: the downward
/// cast (<c>0x4CBE40</c>), the ray cast the bullets use (<c>0x4CC2A0</c>) and the area query the
/// grenade uses (<c>0x4CABF0</c>) all reach the world through it.
/// </para>
/// <para>
/// <b>It is the same octree M6 already reads.</b> The descent indexes
/// <see cref="TerrainOrientation.Slots"/> — the table at <c>0x548060</c> — and folds orientations with
/// the same composition the terrain traversal uses, which was verified branch by branch rather than
/// assumed. The world cube is the same <c>0x40000000</c>. So the collision query and the renderer walk
/// one structure, which is why a collision hit and a drawn triangle cannot disagree.
/// </para>
/// <para>
/// One detail the original writes confusingly and this does not: the X and Y octant bits are taken
/// through the orientation table while the Z bit is <c>OR</c>-ed in afterwards. That is equivalent to
/// looking the full three-bit octant up directly, because bit 2 of every row of the table is fixed —
/// the group acts on X and Y only. Both forms are exercised by the tests.
/// </para>
/// </remarks>
public static class TerrainPointDescent
{
    /// <summary>The bit the root level tests.</summary>
    /// <remarks>
    /// Thirty, set alongside the root extent by every caller. With the <c>0x40000000</c> bias flip that
    /// makes the root's test the top bit of the cube.
    /// </remarks>
    public const int RootShift = 30;

    /// <summary>The deepest a query may descend.</summary>
    /// <remarks>
    /// Sixteen, clamped explicitly by <c>0x4CBE40</c> — and the same cap M6's terrain traversal reaches
    /// from the other direction.
    /// </remarks>
    public const int MaximumDepth = 16;

    /// <summary>Descends to the deepest node containing a point.</summary>
    /// <param name="tree">The octree to walk.</param>
    /// <param name="point">
    /// The point, <b>already biased</b> by the level's world origin. The original adds that bias at the
    /// call site, and it is not the octree's business.
    /// </param>
    /// <param name="maximumDepth">How many levels to allow; clamped to <see cref="MaximumDepth"/>.</param>
    /// <returns>Where the descent stopped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The descent stops early wherever the tree does — a node without the child the point falls in is
    /// as deep as that part of the world is authored, and its cube is the answer.
    /// </remarks>
    public static TerrainDescentCursor Descend(TerrainOctree tree, Vector3i point, int maximumDepth = MaximumDepth)
    {
        ArgumentNullException.ThrowIfNull(tree);

        int limit = Math.Min(maximumDepth, MaximumDepth);

        // The bias flip: the top bit is inverted so a signed world coordinate indexes the cube from its
        // corner, which is what makes the per-level test a plain bit read.
        int x = point.X ^ TerrainOctree.RootExtent;
        int y = point.Y ^ TerrainOctree.RootExtent;
        int z = point.Z ^ TerrainOctree.RootExtent;

        int extent = TerrainOctree.RootExtent;
        int shift = RootShift;
        int orientation = TerrainOrientation.Identity;
        int centreX = 0;
        int centreY = 0;
        int centreZ = 0;
        int nodeIndex = TerrainOctree.RootIndex;
        int depth = 0;

        while (depth < limit)
        {
            int octantX = (x >> shift) & 1;
            int octantY = (y >> shift) & 1;
            int octantZ = (z >> shift) & 1;

            int slot = (4 * octantZ) | TerrainOrientation.ChildSlot(orientation, octantX | (2 * octantY));

            TerrainNode node = tree[nodeIndex];
            if (!node.HasChild(slot))
            {
                break;
            }

            extent >>= 1;
            shift--;

            // The original spells each step as `centre -= octant + (extent ^ -octant)`, which is
            // `centre - extent` for a low octant and `centre + extent` for a high one.
            centreX += octantX == 0 ? -extent : extent;
            centreY += octantY == 0 ? -extent : extent;
            centreZ += octantZ == 0 ? -extent : extent;

            orientation = TerrainOrientation.Compose(orientation, node.Orientation(slot));
            nodeIndex = node.Child(slot);
            depth++;
        }

        return new TerrainDescentCursor(
            nodeIndex,
            depth,
            extent,
            new Vector3i(centreX, centreY, centreZ),
            orientation,
            shift);
    }

    /// <summary>
    /// How deep a query over a box may usefully descend before the box straddles a boundary.
    /// </summary>
    /// <param name="minimum">The box's low corner, already biased.</param>
    /// <param name="maximum">The box's high corner, already biased.</param>
    /// <returns>The depth, clamped to <see cref="MaximumDepth"/>.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x4CBE40</c> computes this once for a whole batch of query points: it <c>OR</c>s the three
    /// axes' corner differences together, takes the highest set bit, and subtracts it from
    /// <see cref="RootShift"/>. That is the length of the common prefix the whole box shares, and so the
    /// last level at which one node still contains all of it.
    /// </para>
    /// <para>
    /// A box that collapses to a point shares every bit. The original's <c>BitScanReverse</c> leaves its
    /// output untouched on a zero input, so it would carry whatever the register held; this returns the
    /// full depth, which is what the descent would reach anyway.
    /// </para>
    /// </remarks>
    public static int DepthForBounds(Vector3i minimum, Vector3i maximum)
    {
        int spread = (minimum.X ^ maximum.X) | (minimum.Y ^ maximum.Y) | (minimum.Z ^ maximum.Z);
        if (spread == 0)
        {
            return MaximumDepth;
        }

        int highestBit = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)spread);
        return Math.Min(RootShift - highestBit, MaximumDepth);
    }
}
