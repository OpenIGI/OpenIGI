using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>Mountain areas shaped live: the octree under the square is rebuilt as the samples move.</summary>
public sealed class Igi1MountainAreaTests(ITestOutputHelper output)
{
    private const double Metre = 4096.0;

    /// <summary>
    /// A mountain area laid over level 1's trainyard starts as the ground there, rises 100 m where its samples are raised
    /// (the ground the game stands on, at once), saves the octree, and leaves the ground as it was when taken away.
    /// </summary>
    [RequiresGameDataFact]
    public void A_raised_mountain_area_is_the_ground_at_once()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        TerrainSurface surface = TerrainSurface.Build(terrain, root);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(surface);
        (double x, double y) = (5376.0, -14080.0);
        double Ground() => terrain.TryDrop(x * Metre, y * Metre, 44000 * Metre, 4000 * Metre, upward: false, out TerrainGround g) ? g.Height / Metre : double.NaN;
        double before = Ground();

        ITerrainHeightGrid? area = editable.AddMountain(new Vector3d(x, y, before));
        Assert.NotNull(area);
        Assert.Contains(area, editable.Grids);
        Assert.Null(editable.AddMountain(new Vector3d(x + 10, y, before)));
        Assert.InRange(Ground() - before, -0.3, 0.3);

        // A dome 100 m high and 150 m across at the middle of the square.
        (double cx, double cy) = area.ToGrid(x, y);
        for (int j = 0; j < area.Height; j++)
        {
            for (int i = 0; i < area.Width; i++)
            {
                double r = Math.Sqrt(((i - cx) * (i - cx)) + ((j - cy) * (j - cy))) * 4.0;
                if (r < 150.0)
                {
                    area.SetHeight(i, j, area.GetHeight(i, j) + (100.0 * (1.0 - (r / 150.0))));
                }
            }
        }

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        editable.Refresh(area, 0, 0, area.Width - 1, area.Height - 1);
        editable.Settle();
        double raised = Ground();
        output.WriteLine($"ground {before:0.##} m, raised {raised:0.##} m, rebuilt in {clock.ElapsedMilliseconds} ms");
        Assert.InRange(raised - before, 99.0, 101.0);
        Assert.True(editable.TryGetHeight(x, y, out double brushed));
        Assert.InRange(brushed - raised, -0.5, 0.5);

        IReadOnlyList<(string Path, byte[] Bytes)> files = editable.ChangedFiles(_ => null);
        Assert.Contains(files, static f => f.Path == Igi1EditableTerrain.OctreeMissionPath);
        Assert.Contains(files, static f => f.Path == Igi1EditableTerrain.GeometryMissionPath);
        Assert.True(TerrainOctree.Read(files.Single(static f => f.Path == Igi1EditableTerrain.OctreeMissionPath).Bytes).IsUsable);

        editable.RemoveMountain(area);
        Assert.DoesNotContain(area, editable.Grids);
        Assert.InRange(Ground() - before, -0.3, 0.3);
    }
}
