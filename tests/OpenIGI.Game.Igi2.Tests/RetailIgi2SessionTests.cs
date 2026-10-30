using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1 as a listen server: a client joins over loopback, walks its player by its commands and sees it in the snapshots.</summary>
public sealed class RetailIgi2SessionTests
{
    [RequiresTitleDataFact("igi2")]
    public void A_client_walks_its_player_in_the_servers_level_and_sees_both_players()
    {
        Igi2TitlePlayer level = RetailIgi2NetworkHumanTests.StandMission1();
        for (int i = 0; i < 300; i++)
        {
            level.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
        }

        double now = 0;
        LoopbackNetwork network = new();
        ulong protocol = MultiplayerProtocol.IdFor("igi2");
        using NetServer serverNet = new(network.Open(26001), new NetServerOptions { ProtocolId = protocol });
        Igi2ReplicatedWorld world = new(level);
        ServerSession server = new(serverNet, world, 30, "location1/level1");
        ClientSession client = new(new NetClient(network.Open(), protocol), world.ChannelCount);
        client.Connect(serverNet.LocalAddress, new JoinRequest("Jones", "test", string.Empty), now);

        float[] forwards = new float[Igi2HumanChannels.Count];
        forwards[(int)Igi2HumanChannel.MoveForwards] = 1f;
        Core.Numerics.Vector3d? start = null;
        for (int i = 0; i < 150; i++)
        {
            now += 1.0 / 30;
            long tick = level.TickCount;
            server.Receive(now, tick);
            level.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            server.Send(now, tick);

            client.Receive(now);
            client.SendCommand(forwards, now);
            client.Flush(now);
            if (start is null && world.PlayerOf(0) is { } joined)
            {
                start = joined.Human.Position;
            }
        }

        Assert.Equal(1, client.EntityId);
        Igi2NetworkHuman player = world.PlayerOf(0)!;
        double walked = Math.Abs(player.Human.Position.X - start!.Value.X) + Math.Abs(player.Human.Position.Y - start.Value.Y);
        Assert.True(walked > 4096, $"walked {walked}; missed {player.Commands.Missed}, late {player.Commands.Late}");

        Snapshot latest = client.Latest!;
        Assert.True(latest.TryGet(Igi2ReplicatedWorld.HostEntity, out _));
        Assert.True(latest.TryGet(1, out ReplicatedHuman mine));
        Assert.Equal(Int3.From(player.Human.Position).X, mine.Position.X, tolerance: 2048);
        Assert.True((mine.Flags & ReplicatedHumanCondition.Alive) != 0);

        // Lag compensation: the host jumps a metre; as the client's shots are traced the host is put back where the
        // client last drew it, and stands where it jumped to again afterwards.
        Core.Numerics.Vector3d hostWas = level.Human.Position;
        level.Human.Position = hostWas + new Core.Numerics.Vector3d(4096, 0, 0);
        using (IDisposable? rewound = level.NetworkRewind!(player))
        {
            Assert.NotNull(rewound);
            Assert.True(Math.Abs(level.Human.Position.X - hostWas.X) < 512, $"rewound to {level.Human.Position.X}, was {hostWas.X}");
        }

        Assert.Equal(hostWas.X + 4096, level.Human.Position.X);

        // A death: the client's player falls; the match counts it, the scoreboard tells the client, and once the respawn
        // delay is over and the client can pay, a new body stands in the level.
        player.Human.Damage.MaxDamage = 1f;
        player.Human.HandleDamage(Humans.Igi2DamagePart.Head, 1000f, default);
        for (int i = 0; i < 130; i++)
        {
            now += 1.0 / 30;
            long tick = level.TickCount;
            server.Receive(now, tick);
            level.Tick(RetailIgi2Mission1.Idle with { BreakCutscene = true });
            server.Send(now, tick);
            client.Receive(now);
            client.SendCommand(new float[Igi2HumanChannels.Count], now);
            client.Flush(now);
        }

        Assert.Equal(1, server.Match.Players[0].Deaths);
        Assert.Contains(client.Scoreboard!.Lines, l => l.Slot == 0 && l.Deaths == 1);
        Igi2NetworkHuman reborn = world.PlayerOf(0)!;
        Assert.NotSame(player, reborn);
        Assert.False(reborn.Human.IsDead);
        Assert.True(server.Match.Players[0].IsAlive);
    }
}
