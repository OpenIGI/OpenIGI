using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>New ground built into a square of a retail level: a mountain the heightmap could never raise.</summary>
public sealed class TerrainRegionBuildTests(ITestOutputHelper output)
{
    private const double Metre = 4096.0;

    /// <summary>
    /// A 150 m cone raised over level 1's trainyard, 31° at its steepest (the format holds about 35° whichever way a slope runs), is where the game's own ground query finds it, to within the format's
    /// height steps, while the ground outside the square is exactly where it was.
    /// </summary>
    [RequiresGameDataFact]
    public void A_mountain_stands_where_it_was_built()
    {
        (TerrainData level, LevelTask _) = TerrainHeightBindingTests.Load(1);
        long side = 2L * (TerrainOctree.RootExtent >> TerrainRegionBuild.DefaultExponent);
        long minX = (long)(5120 * Metre), minY = (long)(-14336 * Metre);
        double centreX = minX + (side / 2.0), centreY = minY + (side / 2.0);
        Assert.True(level.TryDrop(centreX, centreY, 44000 * Metre, 4000 * Metre, upward: false, out TerrainGround middle));

        double Ground(TerrainData terrain, double x, double y, double around) =>
            terrain.TryDrop(x, y, around + (400 * Metre), 800 * Metre, upward: false, out TerrainGround g) ? g.Height : double.NaN;
        double Cone(double x, double y) =>
            Math.Max(0.0, 150.0 * (1.0 - (Math.Sqrt(((x - centreX) * (x - centreX)) + ((y - centreY) * (y - centreY))) / Metre / 250.0))) * Metre;
        Dictionary<(long, long), double> before = [];
        double Before(long x, long y)
        {
            if (!before.TryGetValue((x, y), out double h))
            {
                h = Ground(level, x, y, middle.Height);
                before[(x, y)] = h;
            }

            return h;
        }

        TerrainRegionResult? result = TerrainRegionBuild.Build(level, TerrainRegionBuild.DefaultExponent, minX, minY, (x, y) => Before(x, y) + Cone(x, y));
        Assert.NotNull(result);
        TerrainOctree tree = TerrainOctree.Read(result.Ctr);
        Assert.True(tree.IsUsable, string.Join("; ", tree.Defects));
        Assert.True(tree.Count < TerrainNode.NoChild);
        output.WriteLine($"{result.Nodes} nodes, {tree.Count} records, {result.Clamped} clamped, cmd {result.Cmd.Length} bytes");
        Assert.True(result.Clamped < result.Nodes / 100, $"{result.Clamped} heights held in");

        TerrainData built = result.ToTerrain(level);
        List<double> errors = [];
        for (double fx = 0.05; fx < 1.0; fx += 0.1)
        {
            for (double fy = 0.05; fy < 1.0; fy += 0.1)
            {
                double x = minX + (fx * side), y = minY + (fy * side);
                double want = Ground(level, x, y, middle.Height) + Cone(x, y);
                double found = Ground(built, x, y, want);
                errors.Add(Math.Abs(found - want) / Metre);
            }
        }

        errors.Sort();
        output.WriteLine($"median error {errors[errors.Count / 2]:0.###} m, worst {errors[^1]:0.###} m, peak at {Cone(centreX, centreY) / Metre:0} m");
        Assert.True(errors[errors.Count / 2] < 0.3, $"median {errors[errors.Count / 2]} m");
        Assert.True(errors[^1] < 2.0, $"worst {errors[^1]} m");

        // Outside the square nothing moved.
        foreach ((double x, double y) in new[] { (minX - (50 * Metre), centreY), (minX + side + (50 * Metre), centreY), (centreX, minY - (50 * Metre)) })
        {
            Assert.Equal(Ground(level, x, y, middle.Height), Ground(built, x, y, middle.Height), 3);
        }
    }
}
