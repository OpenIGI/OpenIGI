using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The octree point descent every collision query starts with.
/// </summary>
public sealed class TerrainPointDescentTests
{
    [Fact]
    public void TheOrientationTableAgreesWhetherZGoesThroughItOrAroundIt()
    {
        // The original takes X and Y through the table and ORs Z in afterwards. That is only equivalent
        // to looking the full three-bit octant up because bit 2 of every row is fixed - the group acts
        // on X and Y alone. This is the check that says so for all 64 entries.
        for (int orientation = 0; orientation < TerrainOrientation.Count; orientation++)
        {
            for (int octant = 0; octant < 8; octant++)
            {
                int direct = TerrainOrientation.ChildSlot(orientation, octant);
                int split = (octant & 4) | TerrainOrientation.ChildSlot(orientation, octant & 3);

                Assert.Equal(direct, split);
            }
        }
    }

    [Fact]
    public void TheDepthForABoxIsTheLengthOfItsCommonPrefix()
    {
        // A box spanning the whole cube shares nothing, so it can only be answered at the root.
        Assert.Equal(0, TerrainPointDescent.DepthForBounds(new Vector3i(0, 0, 0), new Vector3i(1 << 30, 0, 0)));

        // Halving the span buys one level each time.
        Assert.Equal(1, TerrainPointDescent.DepthForBounds(new Vector3i(0, 0, 0), new Vector3i(1 << 29, 0, 0)));
        Assert.Equal(2, TerrainPointDescent.DepthForBounds(new Vector3i(0, 0, 0), new Vector3i(1 << 28, 0, 0)));

        // Any axis straddling is enough to stop the descent - the three are OR-ed together.
        Assert.Equal(
            TerrainPointDescent.DepthForBounds(new Vector3i(0, 0, 0), new Vector3i(1 << 29, 0, 0)),
            TerrainPointDescent.DepthForBounds(new Vector3i(0, 0, 0), new Vector3i(0, 0, 1 << 29)));
    }

    [Fact]
    public void TheDepthIsCappedAtSixteenHoweverTightTheBox()
    {
        Assert.Equal(16, TerrainPointDescent.DepthForBounds(new Vector3i(0, 0, 0), new Vector3i(1, 0, 0)));

        // And a box that collapses to a point shares every bit; the original's bit scan is undefined
        // there, so this returns the full depth rather than whatever a register happened to hold.
        Assert.Equal(16, TerrainPointDescent.DepthForBounds(new Vector3i(5, 5, 5), new Vector3i(5, 5, 5)));
    }

    [Fact]
    public void ADescentThroughAnEmptyTreeStopsAtTheRoot()
    {
        // A root with no children is as deep as that part of the world is authored, and its cube - the
        // whole world - is the answer.
        TerrainOctree tree = SingleNodeTree();

        TerrainDescentCursor cursor = TerrainPointDescent.Descend(tree, new Vector3i(0, 0, 0));

        Assert.Equal(0, cursor.Depth);
        Assert.Equal(TerrainOctree.RootIndex, cursor.NodeIndex);
        Assert.Equal(TerrainOctree.RootExtent, cursor.Extent);
        Assert.Equal(new Vector3i(0, 0, 0), cursor.Centre);
        Assert.Equal(TerrainOrientation.Identity, cursor.Orientation);
        Assert.Equal(TerrainPointDescent.RootShift, cursor.Shift);
    }

    [Fact]
    public void AskingForNoDepthReturnsTheRootUntouched()
    {
        TerrainOctree tree = SingleNodeTree();

        TerrainDescentCursor cursor = TerrainPointDescent.Descend(tree, new Vector3i(1234, -5678, 90), maximumDepth: 0);

        Assert.Equal(0, cursor.Depth);
        Assert.Equal(TerrainOctree.RootExtent, cursor.Extent);
    }

    [Fact]
    public void TheCentreStepsTowardTheOctantThePointIsIn()
    {
        // One level down, the cube halves and its centre moves by the new extent - negative for a low
        // octant, positive for a high one. The bias flip means a world coordinate of 0 sits at the
        // boundary and reads as the HIGH octant on every axis.
        TerrainOctree tree = RootWithAllChildren();

        TerrainDescentCursor high = TerrainPointDescent.Descend(tree, new Vector3i(0, 0, 0), maximumDepth: 1);
        int half = TerrainOctree.RootExtent >> 1;

        Assert.Equal(1, high.Depth);
        Assert.Equal(half, high.Extent);
        Assert.Equal(new Vector3i(half, half, half), high.Centre);

        // A point below the boundary on every axis lands in the low octant instead.
        TerrainDescentCursor low = TerrainPointDescent.Descend(tree, new Vector3i(-1, -1, -1), maximumDepth: 1);
        Assert.Equal(new Vector3i(-half, -half, -half), low.Centre);
    }

    [Fact]
    public void TheCubeTheDescentReportsAlwaysContainsThePoint()
    {
        // The invariant that makes the cursor useful: whatever depth it reached, the point is inside.
        TerrainOctree tree = RootWithAllChildren();

        foreach (Vector3i point in new[]
        {
            new Vector3i(0, 0, 0),
            new Vector3i(-1, -1, -1),
            new Vector3i(1 << 20, -(1 << 20), 1 << 25),
            new Vector3i(int.MinValue / 2, int.MaxValue / 2, 0),
        })
        {
            TerrainDescentCursor cursor = TerrainPointDescent.Descend(tree, point, maximumDepth: 1);

            Assert.InRange(point.X, cursor.Centre.X - cursor.Extent, cursor.Centre.X + cursor.Extent);
            Assert.InRange(point.Y, cursor.Centre.Y - cursor.Extent, cursor.Centre.Y + cursor.Extent);
            Assert.InRange(point.Z, cursor.Centre.Z - cursor.Extent, cursor.Centre.Z + cursor.Extent);
        }
    }

    [Fact]
    public void TheShiftTracksTheDepth()
    {
        TerrainOctree tree = RootWithAllChildren();

        TerrainDescentCursor cursor = TerrainPointDescent.Descend(tree, new Vector3i(0, 0, 0), maximumDepth: 1);

        Assert.Equal(TerrainPointDescent.RootShift - cursor.Depth, cursor.Shift);
        Assert.Equal(TerrainOctree.RootExtent >> cursor.Depth, cursor.Extent);
    }

    /// <summary>A tree whose root has no children at all.</summary>
    private static TerrainOctree SingleNodeTree() => BuildTree(childMask: 0, childIndex: 0);

    /// <summary>A root with all eight children, each pointing at a childless node.</summary>
    private static TerrainOctree RootWithAllChildren() => BuildTree(childMask: 0xFF, childIndex: 2);

    private static TerrainOctree BuildTree(byte childMask, ushort childIndex)
    {
        // Records are 32 bytes: eight u16 child indices, eight orientation bytes, then the mask.
        // Index 0 is unused and the root is index 1, matching TerrainOctree.RootIndex.
        byte[] data = new byte[TerrainNode.Stride * 3];

        int root = TerrainNode.Stride * TerrainOctree.RootIndex;
        for (int slot = 0; slot < 8; slot++)
        {
            data[root + (slot * 2)] = (byte)(childIndex & 0xFF);
            data[root + (slot * 2) + 1] = (byte)(childIndex >> 8);
            data[root + 16 + slot] = 0;
        }

        data[root + 24] = childMask;
        return TerrainOctree.Read(data);
    }
}
