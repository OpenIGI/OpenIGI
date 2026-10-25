using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A graph's cover felt for in the world, held against the cover mission 1 ships (<c>AIGraph_UpdateNodeCover</c>).</summary>
public sealed class RetailIgi2GraphCoverTests(ITestOutputHelper output)
{
    /// <summary>
    /// Mission 1's first graph, felt for in the level's static world, is the cover the level ships, node for node.
    /// </summary>
    /// <remarks>
    /// Its third graph runs indoors among the level's switches, doors, cameras and lifts, which the editor felt the cover against
    /// and a world built from the static scene does not hold: there the shipped cover has the same walls and more besides.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Cover_felt_for_in_mission_ones_world_is_the_cover_it_ships()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(level.Directory, "objects.qvm"), loadTables: true);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        ResourceRegistry resources = new(files);
        Igi2AiGraphs graphs = Igi2AiGraphs.Build(
            harness.LevelRoot, resources, harness.Loop.Natives, harness.Session.Services.Require<Igi2ScriptFunctions>());
        Igi2Forest forest = Igi2Forest.Build(level.Root, resources);
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain", forest);

        Igi2AiGraph graph = graphs.Graph(1)!;
        Dictionary<int, int[]> shipped = graph.Cover.ToDictionary(static c => c.NodeId, static c => c.CoverCircle);
        graph.UpdateCover(world);
        Dictionary<int, int[]> felt = graph.Cover.ToDictionary(static c => c.NodeId, static c => c.CoverCircle);

        int sameCircles = shipped.Count(p => felt.TryGetValue(p.Key, out int[]? mine) && mine.SequenceEqual(p.Value));
        int sameWords = shipped.Sum(p => felt.TryGetValue(p.Key, out int[]? mine) ? p.Value.Zip(mine).Count(static w => w.First == w.Second) : 0);
        output.WriteLine($"shipped {shipped.Count}, felt {felt.Count}, same circles {sameCircles}, same words {sameWords}/{shipped.Count * 32}");

        Assert.Equal(shipped.Keys.Order(), felt.Keys.Order());
        Assert.True(sameCircles >= shipped.Count - 4, $"only {sameCircles} of {shipped.Count} circles match");
        Assert.True(sameWords >= shipped.Count * 32 * 0.995, $"only {sameWords} of {shipped.Count * 32} directions match");
    }
}
