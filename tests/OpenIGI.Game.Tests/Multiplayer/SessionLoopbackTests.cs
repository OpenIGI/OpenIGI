using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>A whole session over loopback with a toy world: commands up, snapshots down, the other player seen moving.</summary>
public sealed class SessionLoopbackTests
{
    private const int TickRate = 30;
    private const ulong Protocol = 0x5445535453455353UL;

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.05, 0.03, 0.1)]
    public void TwoClientsDriveTheirPlayersAndSeeEachOther(double latency, double jitter, double loss)
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, latency, jitter, loss, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession[] clients =
        [
            new(new NetClient(Line(network.Open(), () => now, latency, jitter, loss, 2), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, latency, jitter, loss, 3), Protocol), ToyWorld.Channels),
        ];
        clients[0].Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);
        clients[1].Connect(serverNet.LocalAddress, new JoinRequest("two", "test", string.Empty), now);

        // Client one walks along X; client two stands still.
        float[] walk = new float[ToyWorld.Channels];
        walk[0] = 1f;
        float[] idle = new float[ToyWorld.Channels];
        for (long tick = 0; tick < 10 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Receive(now);
                clients[i].SendCommand(i == 0 ? walk : idle, now);
                clients[i].Flush(now);
            }
        }

        Assert.All(clients, c => Assert.NotNull(c.EntityId));
        Assert.Equal("toy", clients[1].Map);
        int walker = clients[0].EntityId!.Value;

        // The walker moved by its own commands on the server: nearly every tick ran one it sent.
        ToyWorld.Player moved = world.PlayerOf(walker);
        Assert.True(moved.Position.X > 200 * 100, $"walked only {moved.Position.X}");
        Assert.True(moved.Commands.Missed < 30, $"{moved.Commands.Missed} ticks had no command");

        // The other client sees it, drawn a little behind the server, and still walking.
        TickClockSync clock = clients[1].Clock!;
        Assert.True(clients[1].Remote.Sample(clock.InterpolationTick(now), walker, out ReplicatedHuman seen));
        Assert.InRange(seen.Position.X, moved.Position.X - (TickRate * 100), moved.Position.X);
        Assert.True(clients[1].Remote.Sample(clock.InterpolationTick(now) - 10, walker, out ReplicatedHuman earlier));
        Assert.True(seen.Position.X > earlier.Position.X);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.08, 0.04)]
    [InlineData(0.2, 0.06)]
    public void A_client_predicting_its_player_as_the_server_runs_it_is_never_corrected(double latency, double jitter)
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, latency, jitter, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession client = new(new NetClient(Line(network.Open(), () => now, latency, jitter, 0, 2), Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        // Walking on and off, so a command run twice, out of order or never would show as a position the client never had.
        float[] frame = new float[ToyWorld.Channels];
        long predicted = 0;
        for (long tick = 0; tick < 12 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            client.Receive(now);
            if (client.EntityId is { } self && client.Latest is { } newest && newest.TryGet(self, out ReplicatedHuman own))
            {
                client.Reconciler.Check(client.LastRunCommandTick, own.Position.ToVector());
            }

            frame[0] = tick % 45 < 30 ? 1f : 0f;
            if (client.SendCommand(frame, now) is { } sent)
            {
                predicted += (int)(frame[0] * 100);
                client.Reconciler.Record(sent, new Core.Numerics.Vector3d(predicted, 0, 0));
            }

            client.Flush(now);
        }

        ToyWorld.Player moved = world.PlayerOf(client.EntityId!.Value);
        Assert.True(moved.Position.X > 100 * 100, $"walked only {moved.Position.X}");
        Assert.Equal(0, client.Reconciler.Corrections);
        Assert.Equal(0, moved.Commands.Late);
    }
    [Fact]
    public void A_line_too_slow_for_every_snapshot_keeps_its_round_trip_and_still_gets_the_world()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new() { Crowd = 300 };
        SimulatedLinkTransport serverLine = Line(network.Open(26001), () => now, 0.15, 0, 0, 1);

        // A few snapshots' worth a second, where the world fills several packets a tick: a slow relay.
        serverLine.Send.Bandwidth = 40_000;
        using NetServer serverNet = new(serverLine, new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession client = new(new NetClient(Line(network.Open(), () => now, 0.15, 0, 0, 2), Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        float[] walk = new float[ToyWorld.Channels];
        walk[0] = 1f;
        double worstRoundTrip = 0;
        for (long tick = 0; tick < 20 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            client.Receive(now);
            client.SendCommand(walk, now);
            client.Flush(now);
            if (tick > 5 * TickRate && server.Clients.Any() && serverNet.GetConnection(server.Clients.First()) is { } line)
            {
                worstRoundTrip = Math.Max(worstRoundTrip, line.Statistics.RoundTripTime);
            }
        }

        // The round trip is the line's and the queue's few packets, never seconds of snapshots waiting their turn.
        Assert.InRange(worstRoundTrip, 0.1, 0.6);

        // Fewer snapshots, but whole and recent ones.
        Snapshot newest = Assert.IsType<Snapshot>(client.Latest);
        Assert.Equal(301, newest.Humans.Length);
        Assert.InRange(newest.Tick, (20 * TickRate) - TickRate, 20 * TickRate);
    }

    [Fact]
    public void A_client_whose_line_carries_less_than_its_commands_keeps_its_round_trip_and_still_moves()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.15, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        SimulatedLinkTransport clientLine = Line(network.Open(), () => now, 0.15, 0, 0, 2);

        // Less up than thirty command packets a second need: a lossy relay WebRTC's congestion control has slowed to a crawl.
        clientLine.Send.Bandwidth = 1200;
        ClientSession client = new(new NetClient(clientLine, Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        float[] walk = new float[ToyWorld.Channels];
        walk[0] = 1f;
        double worstRoundTrip = 0;
        int worstBacklog = 0;
        for (long tick = 0; tick < 30 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            client.Receive(now);
            client.SendCommand(walk, now);
            client.Flush(now);
            if (tick > 5 * TickRate && server.Clients.Any() && serverNet.GetConnection(server.Clients.First()) is { } line)
            {
                worstRoundTrip = Math.Max(worstRoundTrip, line.Statistics.RoundTripTime);
                worstBacklog = Math.Max(worstBacklog, clientLine.Backlog(serverNet.LocalAddress));
            }
        }

        // The client's packets wait behind no more than a few of their own: the round trip stays the line's, not seconds
        // of commands queued -- before the hold it climbed past ten seconds and on.
        Assert.InRange(worstRoundTrip, 0.1, 1.0);
        Assert.InRange(worstBacklog, 0, NetConstants.CommandChokeBacklog + NetConstants.MaxPacketSize);
        Assert.True(client.CommandPacketsHeld > 0, "the line never filled");

        // Its commands still get there, several to a packet, and its player walks on the server.
        Assert.True(world.PlayerOf(client.EntityId!.Value).Position.X > 600 * 100, $"walked only {world.PlayerOf(client.EntityId!.Value).Position.X}");
    }

    [Fact]
    public void Each_client_gets_the_snapshots_a_second_it_asked_for_within_the_servers_bounds()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(network.Open(26001), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy") { MinUpdateRate = 10 };
        ClientSession every = new(new NetClient(network.Open(), Protocol), ToyWorld.Channels);
        ClientSession third = new(new NetClient(network.Open(), Protocol), ToyWorld.Channels);
        ClientSession greedy = new(new NetClient(network.Open(), Protocol), ToyWorld.Channels);
        every.Connect(serverNet.LocalAddress, new JoinRequest("every", "test", string.Empty), now);
        third.Connect(serverNet.LocalAddress, new JoinRequest("third", "test", string.Empty, UpdateRate: TickRate / 3), now);
        greedy.Connect(serverNet.LocalAddress, new JoinRequest("greedy", "test", string.Empty, UpdateRate: 1), now);
        ClientSession[] clients = [every, third, greedy];
        float[] idle = new float[ToyWorld.Channels];
        for (long tick = 0; tick < 10 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            foreach (ClientSession client in clients)
            {
                client.Receive(now);
                client.SendCommand(idle, now);
                client.Flush(now);
            }
        }

        // A third as many as every tick; and one asking for less than the server allows gets what it allows.
        double ratio = third.SnapshotsAssembled / (double)every.SnapshotsAssembled;
        Assert.InRange(ratio, 0.28, 0.4);
        double floor = greedy.SnapshotsAssembled / (double)every.SnapshotsAssembled;
        Assert.InRange(floor, 10.0 / TickRate * 0.8, 10.0 / TickRate * 1.3);
    }

    [Fact]
    public void A_world_too_big_for_one_packet_arrives_in_parts_and_whole()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new() { Crowd = 300 };
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.05, 0.02, 0.05, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession client = new(new NetClient(Line(network.Open(), () => now, 0.05, 0.02, 0.05, 2), Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        float[] walk = new float[ToyWorld.Channels];
        walk[0] = 1f;
        for (long tick = 0; tick < 5 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            client.Receive(now);
            client.SendCommand(walk, now);
            client.Flush(now);
        }

        // Every bystander is in the newest snapshot, as the server had it, and the client's commands ran.
        Snapshot newest = Assert.IsType<Snapshot>(client.Latest);
        Assert.Equal(301, newest.Humans.Length);
        Assert.True(newest.TryGet(1299, out ReplicatedHuman last));
        Assert.Equal(299 * 300, last.Position.Y);
        Assert.Equal((int)newest.Tick * 7, last.Position.X);
        Assert.True(world.PlayerOf(client.EntityId!.Value).Position.X > 100 * 100);
    }

    [Fact]
    public void A_snapshot_in_parts_keeps_its_doors_and_lands_each_round_once()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new() { Crowd = 300, Doors = [0b1010_0101, 0b0000_0001] };
        using NetServer serverNet = new(network.Open(26002), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession client = new(new NetClient(network.Open(), Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        float[] idle = new float[ToyWorld.Channels];
        int landed = 0;
        for (long tick = 0; tick < 3 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);

            // One round lands every tick, on a world whose every snapshot takes several packets.
            world.Landed = [new ReplicatedImpact(ImpactKind.Surface, 1, new Int3((int)tick, 0, 0), new Vector3f(0, 0, 1))];
            server.Send(now, tick);
            client.Receive(now);
            if (client.Latest is { } latest && latest.Tick > 0)
            {
                landed += latest.Impacts?.Length ?? 0;
                Assert.Equal(world.Doors, latest.Doors);
                Assert.True(latest.Humans.Length > 1);
            }

            client.SendCommand(idle, now);
            client.Flush(now);
        }

        // Every snapshot read carried its doors and its one round, however many parts it came in.
        Assert.True(landed > 0, "no round reached the client");
        Assert.Equal(0, client.DroppedSnapshots);
        Snapshot newest = Assert.IsType<Snapshot>(client.Latest);
        Assert.Single(newest.Impacts!);
        Assert.Equal(world.Doors, newest.Doors);
    }

    [Fact]
    public void A_remote_console_command_reaches_the_server_and_its_answer_comes_back()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        List<(int Slot, string Password, string Command)> asked = [];
        server.Rcon = (slot, password, command) =>
        {
            asked.Add((slot, password, command));
            return password == "pw" ? ["Test  ·  mission toy", "  #0 one"] : ["Wrong remote console password."];
        };
        ClientSession client = new(new NetClient(Line(network.Open(), () => now, 0, 0, 0, 2), Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        List<string> answers = [];
        for (long tick = 0; tick < 4 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            client.Receive(now);
            if (tick == 2 * TickRate)
            {
                client.SendRcon("nope", "status");
            }
            else if (tick == (2 * TickRate) + 15)
            {
                client.SendRcon("pw", "status");
            }

            answers.AddRange(client.TakeRconAnswers());
            client.Flush(now);
        }

        Assert.Equal([("nope", "status"), ("pw", "status")], asked.Select(a => (a.Password, a.Command)));
        Assert.Equal(["Wrong remote console password.", "Test  ·  mission toy", "  #0 one"], answers);
    }

    [Fact]
    public void A_level_reset_after_the_first_rounds_mark_reaches_each_client_once_on_the_reliable_line_even_over_a_lossy_one()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.05, 0.02, 0.2, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession client = new(new NetClient(Line(network.Open(), () => now, 0.05, 0.02, 0.2, 2), Protocol), ToyWorld.Channels);
        client.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);

        int resets = 0;
        for (long tick = 0; tick < 6 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            // The first round's start marks the level; the next puts it back, and only that is told.
            if (tick == 2 * TickRate || tick == 3 * TickRate)
            {
                ((OpenIGI.Game.Multiplayer.Modes.IModeHost)server).ResetLevel();
            }

            world.Step(tick);
            server.Send(now, tick);
            client.Receive(now);
            resets += client.TakeLevelReset() ? 1 : 0;
            client.Flush(now);
        }

        Assert.Equal(1, resets);
    }

    [Fact]
    public void A_vote_is_called_in_chat_without_the_command_being_said_and_a_player_standing_idle_is_put_off()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.02, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy") { IdleKickSeconds = 3 };
        ClientSession[] clients =
        [
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 2), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 3), Protocol), ToyWorld.Channels),
        ];
        clients[0].Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);
        clients[1].Connect(serverNet.LocalAddress, new JoinRequest("two", "test", string.Empty), now);

        // One keeps moving its look; two stands still from the moment it is in.
        float[] idle = new float[ToyWorld.Channels];
        List<string> heardByTwo = [];
        for (long tick = 0; tick < 8 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            if (tick == 1 * TickRate)
            {
                clients[0].Say("/vote restart");
            }

            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Receive(now);
                float[] look = new float[ToyWorld.Channels];
                look[0] = (tick % 2) * 0.5f;
                clients[i].SendCommand(i == 0 ? look : idle, now);
                clients[i].Flush(now);
            }

            heardByTwo.AddRange(clients[1].TakeChat().Select(static l => l.Text));
        }

        Assert.DoesNotContain("/vote restart", heardByTwo);
        Assert.Contains(heardByTwo, l => l.Contains("calls a vote to start the match over", StringComparison.Ordinal));
        Assert.Contains("idle", clients[1].EndReason ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(clients[0].EndReason);
    }

    [Fact]
    public void A_team_line_is_heard_by_the_team_only_and_the_servers_word_by_all_as_nobodys()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.02, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        server.Mode = GameModes.For("tdm", world, TickRate);
        ClientSession[] clients =
        [
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 2), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 3), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 4), Protocol), ToyWorld.Channels),
        ];
        for (int i = 0; i < clients.Length; i++)
        {
            clients[i].Connect(serverNet.LocalAddress, new JoinRequest($"p{i}", "test", string.Empty), now);
        }

        List<ChatLine>[] heard = [[], [], []];
        float[] idle = new float[ToyWorld.Channels];
        int speaker = -1;
        for (long tick = 0; tick < 4 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            if (tick == 2 * TickRate)
            {
                speaker = clients[0].EntityId!.Value;
                clients[0].Say("hold B", team: true);
                server.Announce("Round two.");
            }

            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Receive(now);
                clients[i].SendCommand(idle, now);
                clients[i].Flush(now);
                heard[i].AddRange(clients[i].TakeChat());
            }
        }

        // Which of the other two shares the speaker's team, by the server's own count.
        int[] teams = [.. Enumerable.Range(0, clients.Length).Select(i => server.Match.Players.Values.First(p => p.Name == $"p{i}").Team)];
        int mate = teams[1] == teams[0] ? 1 : 2;
        int other = 3 - mate;
        Assert.NotEqual(teams[0], teams[other]);

        ChatLine team = new(speaker, "hold B", ChatKind.Team);
        ChatLine word = new(-1, "Round two.", ChatKind.Server);
        Assert.Contains(team, heard[0]);
        Assert.Contains(team, heard[mate]);
        Assert.DoesNotContain(heard[other], l => l.Kind == ChatKind.Team);
        Assert.All(heard, h => Assert.Contains(word, h));
    }

    [Theory]
    [InlineData(ChatKind.All)]
    [InlineData(ChatKind.Team)]
    [InlineData(ChatKind.Server)]
    public void A_line_of_chat_keeps_its_kind_on_the_wire(ChatKind kind)
    {
        BitWriter writer = new();
        SessionTalk.WriteChat(writer, 7, "  on me  ", kind);
        BitReader reader = new(writer.AsSpan().ToArray());
        Assert.Equal((uint)SessionMessage.Chat, reader.ReadBits(8));
        Assert.Equal(new ChatLine(7, "on me", kind), SessionTalk.ReadChat(ref reader));
    }

    [Fact]
    public void A_hit_is_told_to_the_shooter_and_the_one_hit_and_to_no_one_else()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.02, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession[] clients =
        [
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 2), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 3), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 4), Protocol), ToyWorld.Channels),
        ];
        for (int i = 0; i < clients.Length; i++)
        {
            clients[i].Connect(serverNet.LocalAddress, new JoinRequest($"p{i}", "test", string.Empty), now);
        }

        List<CombatNote>[] told = [[], [], []];
        float[] idle = new float[ToyWorld.Channels];
        for (long tick = 0; tick < 4 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            if (tick == 2 * TickRate)
            {
                world.Combat.Add(new CombatNote(clients[0].EntityId!.Value, clients[1].EntityId!.Value, 0.3f, Killed: false, Head: true, new(100, 200, 300)));
            }

            server.Send(now, tick);
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Receive(now);
                clients[i].SendCommand(idle, now);
                clients[i].Flush(now);
                told[i].AddRange(clients[i].TakeCombat());
            }
        }

        CombatNote hit = Assert.Single(told[0]);
        Assert.Equal(hit, Assert.Single(told[1]));
        Assert.Equal((0.3f, true, 200d), (MathF.Round(hit.Damage, 2), hit.Head, hit.From.Y));
        Assert.Empty(told[2]);
    }

    [Fact]
    public void A_decided_match_offers_the_next_maps_each_player_sees_the_count_and_its_pick_and_the_most_picked_one_is_taken()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.02, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession[] clients =
        [
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 2), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 3), Protocol), ToyWorld.Channels),
            new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 4), Protocol), ToyWorld.Channels),
        ];
        for (int i = 0; i < clients.Length; i++)
        {
            clients[i].Connect(serverNet.LocalAddress, new JoinRequest($"p{i}", "test", string.Empty), now);
        }

        float[] idle = new float[ToyWorld.Channels];
        NextMapVote? seenByFirst = null;
        NextMapVote? seenByLast = null;
        MapChoice? taken = null;
        MapChoice? takenAgain = null;
        for (long tick = 0; tick < 5 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            if (tick == 1 * TickRate)
            {
                server.OfferMaps([new MapChoice(3, "sd", "Trainyard", Again: true), new MapChoice(5, "tdm", "Missile Trainyard")]);
            }

            if (tick == 3 * TickRate)
            {
                seenByFirst = clients[0].MapVote;
                seenByLast = clients[2].MapVote;
                taken = server.TakeChosenMap();
                takenAgain = server.TakeChosenMap();
            }

            if (tick == 2 * TickRate)
            {
                clients[0].Say("/2");
                clients[1].Say("/2");
                clients[2].Say("/1");
            }

            server.Send(now, tick);
            foreach (ClientSession client in clients)
            {
                client.Receive(now);
                client.SendCommand(idle, now);
                client.Flush(now);
            }
        }

        Assert.NotNull(seenByFirst);
        Assert.Equal([1, 2], seenByFirst.Counts);
        Assert.Equal(3, seenByFirst.Voters);
        Assert.Equal(1, seenByFirst.OwnPick);
        Assert.Equal(1, seenByFirst.Leading);
        Assert.Equal("Trainyard", seenByFirst.Choices[0].Name);
        Assert.True(seenByFirst.Choices[0].Again);
        Assert.Equal(0, seenByLast!.OwnPick);
        Assert.Equal(new MapChoice(5, "tdm", "Missile Trainyard"), taken);
        Assert.Null(takenAgain);
        Assert.All(clients, client => Assert.Null(client.MapVote));
    }

    [Fact]
    public void A_vote_reads_back_as_it_was_written_and_none_reads_as_none()
    {
        NextMapVote vote = new([new MapChoice(1, "tdm", "Trainyard", Again: true), new MapChoice(7, "sd", "Military Airbase")], [0, 2], 5, 1);
        BitWriter writer = new();
        NextMapVote.Write(writer, vote);
        NextMapVote.Write(writer, null);
        BitReader reader = new(writer.AsSpan());
        NextMapVote? back = NextMapVote.Read(ref reader);
        NextMapVote? none = NextMapVote.Read(ref reader);

        Assert.NotNull(back);
        Assert.Equal(vote.Choices, back.Choices);
        Assert.Equal(vote.Counts, back.Counts);
        Assert.Equal(5, back.Voters);
        Assert.Equal(1, back.OwnPick);
        Assert.Null(none);
        Assert.False(reader.IsOverflowed);
    }

    [Fact]
    public void A_watcher_is_sent_the_world_but_is_no_player_and_is_never_put_off_for_standing_idle()
    {
        double now = 0;
        LoopbackNetwork network = new();
        ToyWorld world = new();
        using NetServer serverNet = new(Line(network.Open(26001), () => now, 0.02, 0, 0, 1), new NetServerOptions { ProtocolId = Protocol });
        ServerSession server = new(serverNet, world, TickRate, "toy") { IdleKickSeconds = 3 };
        ClientSession player = new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 2), Protocol), ToyWorld.Channels);
        ClientSession watcher = new(new NetClient(Line(network.Open(), () => now, 0.02, 0, 0, 3), Protocol), ToyWorld.Channels);
        player.Connect(serverNet.LocalAddress, new JoinRequest("one", "test", string.Empty), now);
        watcher.Connect(serverNet.LocalAddress, new JoinRequest("demo", "test", string.Empty, Watch: true), now);

        float[] still = new float[ToyWorld.Channels];
        for (long tick = 0; tick < 8 * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            server.Send(now, tick);
            float[] look = new float[ToyWorld.Channels];
            look[0] = (tick % 2) * 0.5f;
            foreach ((ClientSession client, float[] frame) in new[] { (player, look), (watcher, still) })
            {
                client.Receive(now);
                client.SendCommand(frame, now);
                client.Flush(now);
            }
        }

        // Welcomed as nobody, sent the player it does not have, and no player of the match or the server's.
        Assert.Equal(ServerSession.WatcherEntity, watcher.EntityId);
        Assert.NotNull(player.EntityId);
        Assert.True(watcher.Latest is { } seen && seen.TryGet(player.EntityId!.Value, out _), "the watcher was never sent the player");
        Assert.Single(server.Clients);
        Assert.Single(server.Match.Players.Keys, k => k != ServerSession.HostSlot);
        Assert.Null(watcher.EndReason);
        Assert.Null(player.EndReason);
    }

    private static SimulatedLinkTransport Line(INetTransport inner, Func<double> clock, double latency, double jitter, double loss, int seed)
    {
        SimulatedLinkTransport line = new(inner, clock, seed);
        foreach (SimulatedLinkSettings s in new[] { line.Send, line.Receive })
        {
            s.Latency = latency / 2;
            s.Jitter = jitter / 2;
            s.Loss = loss / 2;
        }

        return line;
    }

    /// <summary>A world where each player moves 100 units along X a tick its forward channel is held.</summary>
    internal sealed class ToyWorld : IReplicatedWorld
    {
        public const int Channels = 8;

        private readonly Dictionary<int, Player> _bySlot = [];
        private int _nextId = 1;

        // Whether a player whose line dropped is kept for its return; off, it is gone at once.
        public bool KeepsTheAway { get; set; }

        public bool ParkPlayer(int slot, int parked)
        {
            if (!KeepsTheAway || !_bySlot.Remove(slot, out Player? player))
            {
                return false;
            }

            _bySlot[parked] = player;
            return true;
        }

        public int? UnparkPlayer(int parked, int slot)
        {
            if (!_bySlot.Remove(parked, out Player? player))
            {
                return null;
            }

            _bySlot[slot] = player;
            return player.Id;
        }

        public void ForgetPlayer(int parked) => _bySlot.Remove(parked);

        public int ChannelCount => Channels;

        public long CurrentTick { get; set; }

        public LagCompensation? LagCompensation { get; set; }

        public int? AddPlayer(int slot, string name)
        {
            Player player = new(_nextId++);
            _bySlot[slot] = player;
            return player.Id;
        }

        public void RemovePlayer(int slot) => _bySlot.Remove(slot);

        public UserCommandBuffer? CommandsFor(int slot) => _bySlot.TryGetValue(slot, out Player? p) ? p.Commands : null;

        // How many bystanders besides the players, each moving every tick, as a level's soldiers do.
        public int Crowd { get; set; }

        // The level's doors as the snapshot carries them, and the rounds that landed this tick.
        public byte[]? Doors { get; set; }

        public ReplicatedImpact[]? Landed { get; set; }

        public Snapshot Capture(long tick) => new(tick,
        [
            .. _bySlot.Values.OrderBy(static p => p.Id).Select(static p => new ReplicatedHuman(
                p.Id, p.Position, new Vector3f(p.Speed, 0, 0), 0, 0, 0, 0, 0, 255, 0, ReplicatedHumanCondition.Alive)),
            .. Enumerable.Range(0, Crowd).Select(i => new ReplicatedHuman(
                1000 + i, new Int3((int)tick * 7, i * 300, 0), new Vector3f(i, tick, 0), (ushort)(tick * 11), 0, 3, 4, 5, 200, 1,
                ReplicatedHumanCondition.Alive)),
        ], Doors, Landed);

        public Player PlayerOf(int id) => _bySlot.Values.Single(p => p.Id == id);

        public int TeamOf(int slot) => _bySlot.ContainsKey(slot) ? slot % 2 : -1;

        public IReadOnlyList<WorldDeath> TakeDeaths() => [];

        // Hits a test makes happen, taken by the server on its next tick.
        public List<CombatNote> Combat { get; } = [];

        public IReadOnlyList<CombatNote> TakeCombat()
        {
            CombatNote[] taken = [.. Combat];
            Combat.Clear();
            return taken;
        }

        public IReadOnlyList<IRoundObjective> RoundObjectives => [];

        public int CurrentObjective { get; set; }

        public void Respawn(int slot)
        {
        }

        public void Step(long tick)
        {
            float[] frame = new float[Channels];
            foreach (Player player in _bySlot.Values)
            {
                for (int due = player.Commands.Due(); due > 0 && player.Commands.TryTake(frame); due--)
                {
                    player.Speed = frame[0] * 100;
                    player.Position = player.Position with { X = player.Position.X + (int)player.Speed };
                }
            }
        }

        public sealed class Player(int id)
        {
            public int Id { get; } = id;

            public Int3 Position { get; set; }

            public float Speed { get; set; }

            public UserCommandBuffer Commands { get; } = new(Channels);
        }
    }
}
