using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the multi-ray cast's broad-phase box and terrain depth against <c>0x4CDB20</c>.
/// </summary>
public sealed class RayCastBoundsTests
{
    [Fact]
    public void OneBoxCoversTheWholeBatch()
    {
        // Three rays are broad-phased with a single box, which is why casting three probes costs one
        // traversal rather than three.
        Vector3d[] points =
        [
            new Vector3d(0, 0, 0),
            new Vector3d(1000, -500, 200),
            new Vector3d(-300, 900, -100),
        ];

        RayCastBounds.Build(points, out Vector3d low, out Vector3d high);

        Assert.Equal(-300 - RayCastBounds.HorizontalPadding, low.X, 6);
        Assert.Equal(-500 - RayCastBounds.HorizontalPadding, low.Y, 6);
        Assert.Equal(-100 - RayCastBounds.VerticalPadding, low.Z, 6);
        Assert.Equal(1000 + RayCastBounds.HorizontalPadding, high.X, 6);
        Assert.Equal(900 + RayCastBounds.HorizontalPadding, high.Y, 6);
        Assert.Equal(200 + RayCastBounds.VerticalPadding, high.Z, 6);
    }

    [Fact]
    public void TheVerticalPaddingIsFarLargerThanTheHorizontal()
    {
        // Two units sideways against just over four metres up and down: a near-horizontal cast still
        // has to catch the ground and ceiling around it.
        Assert.Equal(2.0, RayCastBounds.HorizontalPadding);
        Assert.Equal(16387.0, RayCastBounds.VerticalPadding);
        Assert.True(RayCastBounds.VerticalPadding / 4096.0 > 4.0);
    }

    [Fact]
    public void ASinglePointStillMakesAPaddedBox()
    {
        RayCastBounds.Build([new Vector3d(5, 5, 5)], out Vector3d low, out Vector3d high);

        Assert.True(high.X > low.X);
        Assert.True(high.Z > low.Z);
    }

    [Fact]
    public void AnEmptyBatchIsRejected() =>
        Assert.Throws<ArgumentException>(() => RayCastBounds.Build([], out _, out _));

    [Fact]
    public void AStraightDownBatchIsRecognisedAsVertical()
    {
        // The ground query always casts straight down, so this is the common case and it gets the
        // cheaper broad-phase routine.
        Vector3d[] origins = [new Vector3d(100, 200, 5000), new Vector3d(100, 200, 4000)];
        Vector3d[] ends = [new Vector3d(100, 200, 0), new Vector3d(100, 200, -100)];

        Assert.True(RayCastBounds.IsVertical(origins, ends));
    }

    [Fact]
    public void AnyHorizontalMovementDisqualifiesTheWholeBatch()
    {
        // One ray leaning is enough — the flag is cleared for the batch, not per ray.
        Vector3d[] origins = [new Vector3d(100, 200, 5000), new Vector3d(100, 200, 4000)];
        Vector3d[] ends = [new Vector3d(100, 200, 0), new Vector3d(101, 200, 0)];

        Assert.False(RayCastBounds.IsVertical(origins, ends));
    }

    [Fact]
    public void MismatchedBatchesAreRejected() =>
        Assert.Throws<ArgumentException>(
            () => RayCastBounds.IsVertical([new Vector3d(0, 0, 0)], []));

    [Fact]
    public void TheTerrainDepthIsTheRootMinusTheHighestDifferingBit()
    {
        // A box spanning the whole world sits at the root.
        Assert.Equal(0, RayCastBounds.TerrainDepth(0, 0, 0, 1 << 30, 0, 0));

        // Halve it and you descend one level.
        Assert.Equal(1, RayCastBounds.TerrainDepth(0, 0, 0, 1 << 29, 0, 0));
        Assert.Equal(2, RayCastBounds.TerrainDepth(0, 0, 0, 1 << 28, 0, 0));
    }

    [Fact]
    public void TheDepthIsCappedAtSixteen()
    {
        // A player-sized box would otherwise descend far past the octree's finest level.
        Assert.Equal(RayCastBounds.MaximumTerrainDepth, RayCastBounds.TerrainDepth(0, 0, 0, 1, 0, 0));
        Assert.Equal(RayCastBounds.MaximumTerrainDepth, RayCastBounds.TerrainDepth(0, 0, 0, 0, 0, 0));
        Assert.Equal(16, RayCastBounds.MaximumTerrainDepth);
    }

    [Fact]
    public void TheDepthAgreesWithTheRenderersCellSize()
    {
        // Two independent readings of the same octree: the renderer's traversal works in cell sizes
        // and stops at e < 16, this works in depths below a 2^30 root and caps at 16. A box at
        // depth d has cell size 2^(30-d), so the two must line up.
        for (int depth = 1; depth <= 15; depth++)
        {
            int span = 1 << (RayCastBounds.RootDepth - depth);

            Assert.Equal(depth, RayCastBounds.TerrainDepth(0, 0, 0, span, 0, 0));
            Assert.Equal(span, WorldCell.SizeOf(0, 0, 0, span, 0, 0));
        }
    }
}
