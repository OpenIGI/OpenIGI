using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
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
    private sealed class ToyWorld : IReplicatedWorld
    {
        public const int Channels = 8;

        private readonly Dictionary<int, Player> _bySlot = [];
        private int _nextId = 1;

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

        public Snapshot Capture(long tick) => new(tick,
        [
            .. _bySlot.Values.OrderBy(static p => p.Id).Select(static p => new ReplicatedHuman(
                p.Id, p.Position, new Vector3f(p.Speed, 0, 0), 0, 0, 0, 0, 0, 255, 0, ReplicatedHumanCondition.Alive)),
        ]);

        public Player PlayerOf(int id) => _bySlot.Values.Single(p => p.Id == id);

        public void Step(long tick)
        {
            float[] frame = new float[Channels];
            foreach (Player player in _bySlot.Values)
            {
                player.Commands.Take(tick, frame);
                player.Speed = frame[0] * 100;
                player.Position = player.Position with { X = player.Position.X + (int)player.Speed };
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
