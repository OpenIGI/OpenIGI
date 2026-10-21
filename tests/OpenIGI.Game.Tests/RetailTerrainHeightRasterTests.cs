using System.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>The terrain's height raster holds, at every cell, what a descent from above the cell's centre would find.</summary>
public sealed class RetailTerrainHeightRasterTests(ITestOutputHelper output)
{
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(8)]
    public void Every_cell_holds_the_height_a_descent_finds(int missionId)
    {
        (TerrainData terrain, WorldScene scene) = Load(missionId);
        double[] xs = [.. scene.Placements.Select(static p => p.Position.X).Order()];
        double[] ys = [.. scene.Placements.Select(static p => p.Position.Y).Order()];
        double minX = xs[xs.Length / 10], maxX = xs[xs.Length * 9 / 10];
        double minY = ys[ys.Length / 10], maxY = ys[ys.Length * 9 / 10];
        double lowZ = scene.GroundHeight - 100.0, highZ = scene.GroundHeight + 100.0;

        Stopwatch clock = Stopwatch.StartNew();
        ShelterHeightField raster = TerrainHeightRaster.Build(InlineWorkScheduler.Instance, terrain, minX, minY, maxX, maxY, lowZ, highZ);
        output.WriteLine($"m{missionId}: {raster.Width}x{raster.Depth} in {clock.ElapsedMilliseconds} ms");
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.0, 30.0);

        double top = (highZ + 200.0) * WorldScene.UnitsPerMetre;
        double bottom = (lowZ - 200.0) * WorldScene.UnitsPerMetre;
        int ground = 0, mismatched = 0;
        for (int row = 3; row < raster.Depth; row += 13)
        {
            for (int column = 5; column < raster.Width; column += 17)
            {
                double x = (minX + ((column + 0.5) * raster.CellMetres)) * WorldScene.UnitsPerMetre;
                double y = (minY + ((row + 0.5) * raster.CellMetres)) * WorldScene.UnitsPerMetre;
                float expected = terrain.TryTraceDescent(new Vector3d(x, y, top), new Vector3d(x, y, bottom), out TerrainGround found)
                    ? (float)(found.Height / WorldScene.UnitsPerMetre)
                    : ShelterHeightField.OpenSky;
                float actual = raster.SurfaceOver(x / WorldScene.UnitsPerMetre, y / WorldScene.UnitsPerMetre);
                if (float.IsNegativeInfinity(expected))
                {
                    mismatched += float.IsNegativeInfinity(actual) ? 0 : 1;
                    continue;
                }

                ground++;
                mismatched += Math.Abs(expected - actual) <= 0.01f ? 0 : 1;
            }
        }

        // A centre on the very edge between two triangles may fall to either side's rounding; nothing more.
        output.WriteLine($"m{missionId}: {ground} cells over ground, {mismatched} differ");
        Assert.True(ground > 100, $"only {ground} sampled cells had ground");
        Assert.InRange(mismatched, 0, Math.Max(1, ground / 500));
    }

    private static (TerrainData, WorldScene) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader, null, terrainLoader).Load(missionId);
        return (mission.Terrain!, WorldScene.Build(mission, levelRoot, magic));
    }
}
