using OpenIGI.Formats.World;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Formats.Tests;

/// <summary>New terrain blocks held to everything the original asks of a block.</summary>
public sealed class TerrainRegionGeneratorTests(ITestOutputHelper output)
{
    private const double Metre = 4096.0;

    /// <summary>
    /// Whatever the slope, up to a near wall, every block keeps its vertices in its cube, slides each interpolated vertex
    /// onto a base vertex it has, stays within the 63 vertices the original reserves, and nearly all fit the mesh cache's
    /// ordinary 1600-byte element (<c>0x510010</c>) with one ground material on them; nothing is held in. With two
    /// materials meeting, a cliff's busiest blocks need the cache's larger element, of which the original keeps fifty.
    /// </summary>
    [Theory]
    [InlineData("gentle", 0.3)]
    [InlineData("cliff", 2.7)]
    [InlineData("wall", 12.0)]
    public void Every_block_is_one_the_original_can_read(string name, double steepness)
    {
        int exponent = 12;
        long side = 2L * (TerrainOctree.RootExtent >> exponent);
        long parent = 41L * (TerrainOctree.RootExtent >> (exponent - 1));
        double ground = parent - (20 * Metre);

        // A plateau 40 m high, its edge a ring at a radius of 40 m, rising at the given steepness.
        TerrainRegionGenerator generator = new(exponent, 0, 0, (x, y) =>
        {
            double r = Math.Sqrt(((x - (side / 2.0)) * (x - (side / 2.0))) + ((y - (side / 2.0)) * (y - (side / 2.0)))) / Metre;
            return ground + (Math.Clamp((40.0 - r) * steepness, 0.0, 40.0) * Metre);
        });
        IReadOnlyDictionary<long, TerrainGeneratedNode[]> roots = generator.Build([parent]);
        Assert.Equal(0, generator.Clamped);

        HashSet<TerrainGeneratedNode> seen = new(ReferenceEqualityComparer.Instance);
        Stack<TerrainGeneratedNode> open = new(roots.Values.SelectMany(static r => r));
        int blocks = 0, ordinary = 0, ordinaryTwo = 0, largest = 0;
        while (open.TryPop(out TerrainGeneratedNode? node))
        {
            if (!seen.Add(node))
            {
                continue;
            }

            foreach (TerrainGeneratedNode? child in node.Children)
            {
                if (child is not null)
                {
                    open.Push(child);
                }
            }

            Assert.True(TerrainCommands.Read(node.Block).TryGetBlock(0, out TerrainCommandBlock block));
            if (block.IsEmpty)
            {
                continue;
            }

            blocks++;
            Assert.InRange(block.VertexCount, 3, TerrainRegionGenerator.MaximumVertices);
            largest = Math.Max(largest, block.VertexCount);
            for (int i = 0; i < block.VertexCount; i++)
            {
                TerrainPackedVertex v = block.Vertex(i);
                Assert.InRange(v.Z, -24, 24);
                if (i >= block.BaseVertexCount)
                {
                    Assert.InRange(v.MorphSource, 0, block.BaseVertexCount - 1);
                }
            }

            for (int t = 0; t < block.TriangleCount; t++)
            {
                TerrainTriangle triangle = block.Triangle(t);
                Assert.True(triangle.I0 < block.VertexCount && triangle.I1 < block.VertexCount && triangle.I2 < block.VertexCount);
            }

            // The cache element a chunk's mesh takes, for one and for two materials (0x510010's arithmetic).
            int size = (block.VertexCount * 20) - (block.BaseVertexCount * 8);
            ordinary += size + (((size + 4) + (block.VertexCount * 8)) * 2) + 0x3C < 0x640 ? 1 : 0;
            ordinaryTwo += size + ((((size + 4) * 2) + (block.VertexCount * 8)) * 2) + 0x3C < 0x640 ? 1 : 0;
        }

        output.WriteLine($"{name}: {seen.Count} nodes, {blocks} with ground, largest {largest} vertices, {ordinary * 100.0 / blocks:0.#}% fit the ordinary element with one material, {ordinaryTwo * 100.0 / blocks:0.#}% with two");
        Assert.True(ordinary >= blocks * 0.97, $"only {ordinary} of {blocks} blocks fit the ordinary element");
    }
}
