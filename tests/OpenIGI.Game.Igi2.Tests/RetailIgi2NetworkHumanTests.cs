using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A second player in mission 1, as a server runs one for a client: made, listed, driven by commands, taken out.</summary>
public sealed class RetailIgi2NetworkHumanTests
{
    [RequiresTitleDataFact("igi2")]
    public void A_networked_player_walks_on_its_clients_commands_and_is_seen_like_the_levels_own()
    {
        Igi2TitlePlayer level = StandMission1();
        for (int tick = 0; tick < 300; tick++)
        {
            level.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
        }

        Vector3d start = level.Human.Position + new Vector3d(8192, 0, 0);
        Igi2NetworkHuman? other = level.AddNetworkHuman(level.Human.Model!.Name, start, level.Human.Gamma, level.Human.Team);
        Assert.NotNull(other);
        Assert.Contains(other.Human, level.Ai!.Detection!.Detectables);
        Assert.Contains(other.Human, level.Ai.Players);
        Assert.Equal(level.Human, level.Ai.Players.Primary);
        Assert.Contains(other.Human, level.MapHumans);

        // The client stamps a command for each tick just ahead of the server; forwards held throughout. The server's tick
        // here is the level player's count, 300 after the intro.
        Vector3d before = other.Human.Position;
        for (int i = 0; i < 90; i++)
        {
            float[] channels = new float[Igi2HumanChannels.Count];
            channels[(int)Igi2HumanChannel.MoveForwards] = 1f;
            other.Commands.Push(new UserCommand(300 + i, channels), serverTick: 300 + i);
            level.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
        }
        double moved = Math.Sqrt(Math.Pow(other.Human.Position.X - before.X, 2) + Math.Pow(other.Human.Position.Y - before.Y, 2));
        Assert.True(moved > 4096, $"moved {moved} units; missed {other.Commands.Missed}, state {other.Human.Locomotion.CurrentState}");

        Assert.True(level.RemoveNetworkHuman(other));
        Assert.DoesNotContain(other.Human, level.Ai.Detection.Detectables);
        Assert.DoesNotContain(other.Human, level.Ai.Players);
        Assert.Empty(level.NetworkHumans);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_single_player_level_has_no_networked_players_and_one_rostered_player()
    {
        Igi2TitlePlayer level = StandMission1();
        Assert.Empty(level.NetworkHumans);
        Assert.Equal(level.Human, Assert.Single(level.Ai!.Players));
    }

    internal static Igi2TitlePlayer StandMission1()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(m.Level);
        resources.FileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        Engine.Flow.GameLoop loop = new(new Engine.Time.ManualTimeSource(1000), structuralTypes: new Igi2Runtime().Layout.StructuralTaskTypes);
        Engine.Titles.TitleSession.Start(new Igi2Runtime().Modules, loop, resources.FileSystem, resources, loadTables: false, inherited: m.Services);
        Engine.Tasks.LevelTask root = (Engine.Tasks.LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(Engine.Tasks.EngineTaskTypes.Level));
        new Engine.Level.LevelLoader(root, loop.TaskCommands, loop.Natives, resources).LoadScript("MISSION:objects.qvm");
        MissionDescriptor descriptor = new(11, "igi2", string.Empty, string.Empty,
            MissionDescriptor.NextMissionUndefined, "LOCAL:missions/location1/level1/", packs[0].Directory,
            packs[^1].Directory, packs[0].Name, packs[^1].Name, string.Empty);
        LoadedMission mission = new(descriptor, packs[0], packs[^1], [], Packs: packs);
        WorldBuildRequest world = new(mission, root, resources, null, Core.Diagnostics.NullLogger.Instance, Services: m.Services);
        WorldBuildResult built = new Igi2WorldBuilder().Build(world);
        _ = new OpenIGI.Game.Igi1.Ai.AiScriptHost(loop.Natives);
        Igi2TitlePlayer level = Assert.IsType<Igi2TitlePlayer>(new Igi2Gameplay().CreatePlayer(
            new TitlePlayerRequest(world, built, m.Services!, Game.Ai.AiDifficulty.Normal, Natives: loop.Natives)));
        level.Human.Damage.MaxDamage = 1000000f;
        return level;
    }
}
