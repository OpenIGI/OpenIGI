using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1 twice in one process, a listen server and a client: each shows the other's player.</summary>
public sealed class RetailIgi2ClientWorldTests
{
    [RequiresTitleDataFact("igi2")]
    public void The_client_shows_the_hosts_player_where_the_host_stands()
    {
        Igi2TitlePlayer serverLevel = RetailIgi2NetworkHumanTests.StandMission1();
        Igi2TitlePlayer clientLevel = RetailIgi2NetworkHumanTests.StandMission1();
        for (int i = 0; i < 300; i++)
        {
            serverLevel.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            clientLevel.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
        }

        double now = 0;
        LoopbackNetwork network = new();
        ulong protocol = MultiplayerProtocol.IdFor("igi2");
        using NetServer serverNet = new(network.Open(26001), new NetServerOptions { ProtocolId = protocol });
        IReplicatedWorld serverWorld = serverLevel.CreateReplicatedWorld(includesHost: true);
        ServerSession server = new(serverNet, serverWorld, 30, "location1/level1");
        Igi2ClientWorld clientWorld = (Igi2ClientWorld)clientLevel.CreateClientWorld();
        ClientSession client = new(new NetClient(network.Open(), protocol), serverWorld.ChannelCount);
        client.Connect(serverNet.LocalAddress, new JoinRequest("Jones", "test", string.Empty), now);

        for (int i = 0; i < 120; i++)
        {
            now += 1.0 / 30;
            long tick = serverWorld.CurrentTick;
            server.Receive(now, tick);
            serverLevel.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            server.Send(now, tick);

            clientLevel.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            ClientReplication.Tick(client, clientWorld, now);
        }

        Igi2NetworkHuman puppet = clientWorld.PuppetOf(Igi2ReplicatedWorld.HostEntity)
            ?? throw new Xunit.Sdk.XunitException($"no puppet of the host; shown: {string.Join(",", clientWorld.Shown)}");
        Core.Numerics.Vector3d host = serverLevel.Human.Position;
        double off = Math.Abs(puppet.Human.Position.X - host.X) + Math.Abs(puppet.Human.Position.Y - host.Y);
        Assert.True(off < 4096, $"the host's puppet stands {off} units from the host");
        Assert.Single(serverLevel.NetworkHumans);
        Assert.Single(clientLevel.NetworkHumans);
    }
}
