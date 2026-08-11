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

    [Fact]
    public void NegativeFractionalCoordinatesSnapToPrecedingCube()
    {
        // At level 15, span is 65536; negative fractional positions must snap down to the preceding cube.
        // Position -1.5 should floor to -2, then snap to -65536 (not 0).
        Assert.True(TerrainDiscardRegion.TryCreate(
            new Vector3d(-1.5, -1.5, -1.5),
            15,
            out TerrainDiscardRegion? region));
        Assert.NotNull(region);

        Assert.Equal(-65536, region.MinX);
        Assert.Equal(-65536, region.MinY);
        Assert.Equal(-65536, region.MinZ);
        Assert.Equal(0, region.MaxX);
        Assert.Equal(0, region.MaxY);
        Assert.Equal(0, region.MaxZ);

        // Position exactly at a negative boundary should snap to that boundary.
        Assert.True(TerrainDiscardRegion.TryCreate(
            new Vector3d(-65536.0, -65536.0, -65536.0),
            15,
            out TerrainDiscardRegion? boundary));
        Assert.NotNull(boundary);

        Assert.Equal(-65536, boundary.MinX);
        Assert.Equal(-65536, boundary.MinY);
        Assert.Equal(-65536, boundary.MinZ);
    }
}
