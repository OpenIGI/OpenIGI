using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The five shipped multiplayer maps' objects: their mission, objectives, spawn areas and spawn points.</summary>
public sealed class RetailIgi2MultiplayerMapTests
{
    [RequiresTitleDataTheory("igi2")]
    [InlineData("chinesetemple")]
    [InlineData("forestraid")]
    [InlineData("redstone")]
    [InlineData("sandstorm")]
    [InlineData("timberland")]
    public void A_multiplayer_map_declares_a_mission_its_objectives_and_both_teams_spawns(string map)
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        ResourceRegistry resources = new(files);
        files.SetAlias(VirtualFileSystem.MissionAlias, $"LOCAL:missions/multiplayer/{map}/");
        Engine.Flow.GameLoop loop = new(new Engine.Time.ManualTimeSource(1000), structuralTypes: new Igi2Runtime().Layout.StructuralTaskTypes);
        Engine.Titles.TitleSession.Start(new Igi2Runtime().Modules, loop, files, resources, loadTables: false);
        Engine.Tasks.LevelTask root = (Engine.Tasks.LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(Engine.Tasks.EngineTaskTypes.Level));
        new Engine.Level.LevelLoader(root, loop.TaskCommands, loop.Natives, resources).LoadScript("MISSION:objects.qvm");

        Engine.Tasks.GameTask[] all = [.. root.Descendants()];
        Assert.Single(all.OfType<Igi2NetworkMissionTask>());
        Igi2NetworkObjectiveTask[] objectives = [.. all.OfType<Igi2NetworkObjectiveTask>()];
        Assert.NotEmpty(objectives);
        Igi2SpawnAreaTask[] areas = [.. all.OfType<Igi2SpawnAreaTask>()];
        Assert.Contains(areas, a => a.Team == 0);
        Assert.Contains(areas, a => a.Team == 1);
        Igi2SpawnPointTask[] points = [.. all.OfType<Igi2SpawnPointTask>()];
        Assert.NotEmpty(points);

        // Every spawn point is a child of a spawn area: that is how the original finds a team's points (SpawnPoint_GetSpawnPoint).
        Assert.All(points, p => Assert.IsType<Igi2SpawnAreaTask>(p.Parent));
        Assert.All(areas.Where(a => a.Points().Any()), a => Assert.NotNull(new Igi2SpawnSelector().Pick(a)));
    }
}
