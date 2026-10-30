using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A shipped multiplayer map started, though it places no player of its own.</summary>
public sealed class RetailIgi2MultiplayerLevelTests
{
    [RequiresTitleDataFact("igi2")]
    public void Sandstorm_starts_with_its_player_at_a_spawn_point_of_the_first_team()
    {
        Igi2TitlePlayer level = RetailIgi2NetworkHumanTests.StandLevel("multiplayer", "sandstorm", 5)
            ?? throw new Xunit.Sdk.XunitException("the multiplayer map's level did not start");
        Igi2SpawnAreaTask[] areas = [.. level.LevelTasks().OfType<Igi2SpawnAreaTask>()];
        Assert.NotEmpty(areas);
        Assert.Contains(areas, a => a.Team == TeamRoster.Good
            && a.Points().Any(p => Math.Abs(p.Position.X - level.Human.Position.X) + Math.Abs(p.Position.Y - level.Human.Position.Y) < 4096));
        Assert.NotNull(level.Level);
        Assert.NotEmpty(level.CreateReplicatedWorld(includesHost: true).RoundObjectives);
    }

    [RequiresTitleDataFact("igi2")]
    public void On_sandstorm_a_client_joins_a_team_at_its_spawn_and_the_round_runs()
    {
        Igi2TitlePlayer level = RetailIgi2NetworkHumanTests.StandLevel("multiplayer", "sandstorm", 5)!;
        Igi2SpawnAreaTask[] areas = [.. level.LevelTasks().OfType<Igi2SpawnAreaTask>()];
        double now = 0;
        Net.LoopbackNetwork network = new();
        ulong protocol = MultiplayerProtocol.IdFor("igi2");
        using Net.NetServer serverNet = new(network.Open(26001), new Net.NetServerOptions { ProtocolId = protocol });
        IReplicatedWorld world = level.CreateReplicatedWorld(includesHost: true);
        ServerSession server = new(serverNet, world, 30, "sandstorm", hostName: "host");
        ClientSession client = new(new Net.NetClient(network.Open(), protocol), world.ChannelCount);
        client.Connect(serverNet.LocalAddress, new JoinRequest("Anya", "test", string.Empty), now);
        for (int i = 0; i < 90; i++)
        {
            now += 1.0 / 30;
            long tick = world.CurrentTick;
            server.Receive(now, tick);
            level.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            server.Send(now, tick);
            client.Receive(now);
            client.SendCommand(new float[Humans.Igi2HumanChannels.Count], now);
            client.Flush(now);
        }

        Assert.NotNull(server.Round);
        Assert.Equal(1, server.Round.Current);
        Igi2NetworkHuman joined = Assert.Single(level.NetworkHumans);
        int team = world.TeamOf(0);
        Assert.Contains(areas, a => a.Team == team
            && a.Points().Any(p => Math.Abs(p.Position.X - joined.Human.Position.X) + Math.Abs(p.Position.Y - joined.Human.Position.Y) < 4096));
        Assert.Contains(client.Scoreboard!.Lines, l => l.Name == "Anya");
    }
}