using System.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>
/// A full server's worth of clients over a poor line, for what it costs: the server's time to capture and send a tick, the
/// bytes each client is sent, and that everyone stays connected and sees everyone. The figures are printed for the record and
/// held under bounds generous enough to pass on a slow machine but tight enough to catch a cost that has grown by a multiple.
/// </summary>
public sealed class SessionSoakTests(ITestOutputHelper output)
{
    private const int TickRate = 30;
    private const ulong Protocol = 0x5445535453455353UL;

    [Theory]
    [InlineData(32, 0, 0.15, 0.03, 0.05)]
    [InlineData(4, 100, 0.15, 0.03, 0.05)]
    public void A_full_server_on_a_poor_line_keeps_everyone_and_stays_within_its_costs(int players, int crowd, double latency, double jitter, double loss)
    {
        double now = 0;
        LoopbackNetwork network = new();
        SessionLoopbackTests.ToyWorld world = new() { Crowd = crowd };
        using NetServer serverNet = new(Line(network.Open(26001), () => now, latency, jitter, loss, 1), new NetServerOptions { ProtocolId = Protocol, MaxClients = 32 });
        ServerSession server = new(serverNet, world, TickRate, "toy");
        ClientSession[] clients = [.. Enumerable.Range(0, players).Select(i =>
            new ClientSession(new NetClient(Line(network.Open(), () => now, latency, jitter, loss, 10 + i), Protocol), SessionLoopbackTests.ToyWorld.Channels))];
        for (int i = 0; i < clients.Length; i++)
        {
            clients[i].Connect(serverNet.LocalAddress, new JoinRequest($"p{i}", "test", string.Empty), now);
        }

        // Every other player walks, on and off, so the snapshots carry movement as a match's do.
        float[] walk = new float[SessionLoopbackTests.ToyWorld.Channels];
        float[] idle = new float[SessionLoopbackTests.ToyWorld.Channels];
        walk[0] = 1f;
        const int Seconds = 20;
        long[] received = new long[players];
        Stopwatch sending = new();
        for (long tick = 0; tick < Seconds * TickRate; tick++)
        {
            now = tick / (double)TickRate;
            server.Receive(now, tick);
            world.Step(tick);
            sending.Start();
            server.Send(now, tick);
            sending.Stop();
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i].Receive(now);
                clients[i].SendCommand(i % 2 == 0 && tick % 90 < 60 ? walk : idle, now);
                clients[i].Flush(now);
            }
        }

        for (int i = 0; i < players; i++)
        {
            received[i] = clients[i].SnapshotBytesReceived;
        }

        // Everyone in, and each sees all the players and the crowd.
        Assert.All(clients, c => Assert.NotNull(c.EntityId));
        Assert.All(clients, c => Assert.Equal(players + crowd, c.Latest!.Humans.Length));

        double sendMs = sending.Elapsed.TotalMilliseconds / (Seconds * TickRate);
        double perClient = received.Average() / Seconds / 1024;
        double busiest = received.Max() / (double)Seconds / 1024;
        output.WriteLine($"{players} players, {crowd} crowd, {latency * 1000:0} ms, {loss * 100:0}% loss: " +
            $"send {sendMs:0.000} ms/tick, snapshots {perClient:0.0} KiB/s per client (busiest {busiest:0.0})");

        // A tick's send fits well inside a 33 ms tick; a client's snapshots inside a broadband line's share many times over.
        Assert.True(sendMs < 8, $"the server took {sendMs:0.00} ms a tick to send");
        Assert.True(busiest < 64, $"a client was sent {busiest:0.0} KiB/s of snapshots");
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
}
