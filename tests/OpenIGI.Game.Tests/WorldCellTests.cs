using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the broad-phase cell sizing against <c>0x4C67E0</c>.
/// </summary>
public sealed class WorldCellTests
{
    [Fact]
    public void APointLandsInTheSmallestCell() =>
        Assert.Equal(WorldCell.MinimumSize, WorldCell.SizeOf(1000, 2000, 3000, 1000, 2000, 3000));

    [Fact]
    public void AnythingSmallerThanTwoMetresIsStillTwoMetres()
    {
        // The floor is 8192 units, so a person-sized box does not get its own finer cell.
        Assert.Equal(WorldCell.MinimumSize, WorldCell.SizeOf(0, 0, 0, 100, 100, 100));
        Assert.Equal(8192, WorldCell.MinimumSize);
        Assert.Equal(2.0, WorldCell.MinimumSize / 4096.0, 6);
    }

    [Fact]
    public void TheCellIsTheHighestDifferingBit()
    {
        // Corners agreeing above bit 16 but differing at it: the cell is 1 << 16.
        Assert.Equal(1 << 16, WorldCell.SizeOf(0, 0, 0, 1 << 16, 0, 0));

        // The *position* decides the cell as much as the size does. Two boxes both two units wide:
        // the one sitting inside a cell gets the 8192 floor, while the one straddling a 2^16 boundary
        // is lifted all the way to a 65536 cell, because its corners first disagree at bit 16.
        Assert.Equal(WorldCell.MinimumSize, WorldCell.SizeOf(1 << 16, 0, 0, (1 << 16) + 2, 0, 0));
        Assert.Equal(1 << 16, WorldCell.SizeOf((1 << 16) - 1, 0, 0, (1 << 16) + 1, 0, 0));
    }

    [Fact]
    public void TheWidestAxisWins()
    {
        // The three axes are OR-ed before the bit scan, so one wide axis lifts the whole box.
        Assert.Equal(1 << 20, WorldCell.SizeOf(0, 0, 0, 1, 1 << 20, 1));
    }

    [Fact]
    public void ABoxStraddlingTheOriginTakesTheWholeWorld()
    {
        // Negative and positive differ in the sign bit, and the XOR is taken on the raw integers, so
        // the highest differing bit is 31 - clamped to the world cube.
        Assert.Equal(WorldCell.MaximumSize, WorldCell.SizeOf(-1, 0, 0, 1, 0, 0));
    }

    [Fact]
    public void TheLargestCellIsTheTerrainOctreesHalfExtent()
    {
        // 0x40000000 is the same +/-2^30 cube the terrain octree spans, so the collision index and the
        // terrain agree on the world's bounds.
        Assert.Equal(0x40000000, WorldCell.MaximumSize);
        Assert.Equal(WorldCell.MaximumSize, WorldCell.SizeOf(0, 0, 0, int.MaxValue, 0, 0));
    }

    [Fact]
    public void EveryResultIsAPowerOfTwoInRange()
    {
        int[] samples = [0, 1, 7, 8191, 8192, 100_000, 1 << 22, (1 << 30) - 1];
        foreach (int a in samples)
        {
            foreach (int b in samples)
            {
                int size = WorldCell.SizeOf(a, a, a, b, b, b);

                Assert.InRange(size, WorldCell.MinimumSize, WorldCell.MaximumSize);
                Assert.Equal(0, size & (size - 1));
            }
        }
    }
}
