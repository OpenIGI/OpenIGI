using OpenIGI.Core.Collections;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>The open-addressed map the terrain tessellation looks its vertices and edges up in.</summary>
public sealed class PackedKeyIntMapTests
{
    /// <summary>It agrees with a dictionary across growth, overwrites and clears, on keys that pack pairs.</summary>
    [Fact]
    public void Matches_a_dictionary_through_growth_and_clears()
    {
        PackedKeyIntMap map = new(capacity: 4);
        Dictionary<long, int> expected = [];
        Random random = new(1234);

        for (int round = 0; round < 5; round++)
        {
            for (int i = 0; i < 3000; i++)
            {
                // Packed pairs of small numbers: the shape whose plain hash collides.
                long key = ((long)random.Next(64) << 32) | (uint)random.Next(64);
                int value = random.Next();
                map.Set(key, value);
                expected[key] = value;
            }

            Assert.Equal(expected.Count, map.Count);
            foreach ((long key, int value) in expected)
            {
                Assert.True(map.TryGetValue(key, out int found));
                Assert.Equal(value, found);
            }

            Assert.False(map.TryGetValue(-1L, out int missing));
            Assert.Equal(0, missing);

            map.Clear();
            expected.Clear();
            Assert.Equal(0, map.Count);
            Assert.False(map.TryGetValue(((long)3 << 32) | 5u, out _));
        }
    }
}
