using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class TerrainDiscardRegionTests
{
    [Fact]
    public void MissionThreeRampTaskSnapsToItsRetailOctreeCube()
    {
        Assert.True(TerrainDiscardRegion.TryCreate(
            new Vector3d(23756986, -55333860, 174413140),
            15,
            out TerrainDiscardRegion? region));
        Assert.NotNull(region);

        Assert.Equal(23724032, region.MinX);
        Assert.Equal(-55377920, region.MinY);
        Assert.Equal(174391296, region.MinZ);
        Assert.Equal(23789568, region.MaxX);
        Assert.Equal(-55312384, region.MaxY);
        Assert.Equal(174456832, region.MaxZ);
        Assert.Equal(32768, region.Extent);
    }

    [Fact]
    public void PointQueryUsesTheStrictBoundsFromTheRetailHandler()
    {
        Assert.True(TerrainDiscardRegion.TryCreate(
            new Vector3d(23756986, -55333860, 174413140),
            15,
            out TerrainDiscardRegion? region));
        Assert.NotNull(region);

        Assert.True(region.Contains(23749052, -55320144, 174413136));
        Assert.False(region.Contains(region.MinX, -55320144, 174413136));
        Assert.False(region.Contains(region.MaxX, -55320144, 174413136));
    }

    [Fact]
    public void MatchingCubeIsRemovedButFaceAdjacentCubeDoesNotOverlap()
    {
        Assert.True(TerrainDiscardRegion.TryCreate(
            new Vector3d(23756986, -55333860, 174413140),
            15,
            out TerrainDiscardRegion? region));
        Assert.NotNull(region);

        Vector3d centre = new(23756800, -55345152, 174424064);
        Assert.True(region.ContainsCube(centre, region.Extent));

        Vector3d adjacent = new(centre.X + (2.0 * region.Extent), centre.Y, centre.Z);
        Assert.False(region.ContainsCube(adjacent, region.Extent));
        Assert.False(region.IntersectsCube(adjacent, region.Extent));

        Assert.True(region.IntersectsCube(centre, 2 * region.Extent));
        Assert.False(region.ContainsCube(centre, 2 * region.Extent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(17)]
    public void LevelsOutsideTheRetailTerrainDescentAreRejected(int level)
    {
        Assert.False(TerrainDiscardRegion.TryCreate(default, level, out TerrainDiscardRegion? region));
        Assert.Null(region);
    }
}
