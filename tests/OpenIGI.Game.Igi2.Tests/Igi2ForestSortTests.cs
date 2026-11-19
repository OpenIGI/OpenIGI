using OpenIGI.Presentation.Igi2.World;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The blended foliage's order: farthest first, exactly as the comparison sort it replaced put it.</summary>
public sealed class Igi2ForestSortTests
{
    /// <summary>
    /// Every permutation step must match, ties included: with many trees at equal depths an unstable sort's tie order is
    /// visible in what blends over what, so the key sort has to land the same trees in the same places.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(17, 2)]
    [InlineData(500, 3)]
    [InlineData(5000, 4)]
    public void Orders_as_the_comparison_sort_did(int count, int seed)
    {
        Random random = new(seed);

        // Depths drawn from a small set, so ties are everywhere, mixed with distinct ones.
        float[] depths = [.. Enumerable.Range(0, count).Select(i => i % 3 == 0 ? random.Next(8) * 10f : (float)(random.NextDouble() * 500.0))];

        List<(float Depth, int Place)> expected = [.. depths.Select(static (d, i) => (d, i))];
        expected.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));

        float[] keys = [.. depths];
        int[] order = new int[count];
        Igi2ForestPresenter.OrderFarthestFirst(keys, order, count);

        Assert.Equal(expected.Select(static e => e.Place), order);
    }
}
