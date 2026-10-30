using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>The server's side of a session: clients' players made and fed their commands, and a snapshot sent each tick.</summary>
/// <remarks>
/// <para>
/// The host runs the world; this runs beside it, once per tick, after the world's tick: it reads what came in (each
/// client's commands go into its player's buffer for the world to take as it runs), captures the world, and writes every
/// client its snapshot against the newest one that client acknowledged. The same snapshot goes into
/// <see cref="LagCompensation"/>, where a shot is traced against the world as its shooter saw it.
/// </para>
/// <para>
/// Sending every tick is Source's <c>cl_updaterate</c> at the tick rate; <see cref="SnapshotInterval"/> thins it for a
/// slow line.
/// </para>
/// </remarks>
public sealed class ServerSession
{
    private readonly NetServer _net;
    private readonly IReplicatedWorld _world;
    private readonly Dictionary<int, ClientState> _clients = [];
    private readonly UserCommandCodec _codec;
    private readonly BitWriter _writer = new(2048);
    private readonly List<UserCommand> _commands = [];

    /// <summary>Initializes a new instance of the <see cref="ServerSession"/> class.</summary>
    /// <param name="net">The server's network end; the session reads its events, so nothing else should.</param>
    /// <param name="world">The title's world.</param>
    /// <param name="tickRate">The world's ticks per second.</param>
    /// <param name="map">The map, told to each client as it joins.</param>
    public ServerSession(NetServer net, IReplicatedWorld world, int tickRate, string map)
    {
        _net = net ?? throw new ArgumentNullException(nameof(net));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        TickRate = tickRate;
        Map = map ?? throw new ArgumentNullException(nameof(map));
        _codec = new UserCommandCodec(world.ChannelCount);
        world.LagCompensation = new LagCompensation(LagCompensationHistory, tickRate);
    }

    /// <summary>Gets the world's ticks per second.</summary>
    public int TickRate { get; }

    /// <summary>Gets the map.</summary>
    public string Map { get; }

    /// <summary>Gets or sets how many ticks apart snapshots are sent; one sends every tick.</summary>
    public int SnapshotInterval { get; set; } = 1;

    /// <summary>Gets the server's own recent snapshots, where shots are traced against the world as each shooter saw it.</summary>
    public SnapshotInterpolator LagCompensationHistory { get; } = new(64);

    /// <summary>Gets the connected clients' slots.</summary>
    public IEnumerable<int> Clients => _clients.Keys;

    /// <summary>Gets a client's player name, or <see langword="null"/>.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>The name.</returns>
    public string? NameOf(int slot) => _clients.TryGetValue(slot, out ClientState? client) ? client.Name : null;

    /// <summary>Reads what arrived: joins, departures and commands. Called before the world's tick.</summary>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="serverTick">The tick the world is about to run.</param>
    public void Receive(double now, long serverTick)
    {
        _net.Update(now);
        while (_net.TryReceive(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    Join(e.Peer, JoinRequest.Decode(e.Payload ?? [])?.PlayerName ?? "player");
                    break;
                case NetEventKind.Disconnected:
                    if (_clients.Remove(e.Peer))
                    {
                        _world.RemovePlayer(e.Peer);
                    }

                    break;
                case NetEventKind.Message when e.Message.Data.Length > 0 && _clients.TryGetValue(e.Peer, out ClientState? client):
                    ReadMessage(e.Peer, client, e.Message.Data, serverTick);
                    break;
            }
        }
    }

    /// <summary>Captures the world after its tick and sends each client what is due.</summary>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="tick">The tick just run.</param>
    public void Send(double now, long tick)
    {
        Snapshot snapshot = _world.Capture(tick);
        LagCompensationHistory.Add(snapshot);
        if (tick % Math.Max(1, SnapshotInterval) == 0)
        {
            foreach ((int slot, ClientState client) in _clients)
            {
                client.History.Add(snapshot);
                _writer.Reset();
                _writer.WriteBits((uint)SessionMessage.Snapshot, 8);
                SnapshotCodec.Write(_writer, snapshot, client.History.Baseline);
                long ran = _world.CommandsFor(slot)?.LastTakenTick ?? -1;
                _writer.WriteBits((uint)ran, 32);
                _writer.WriteBits((uint)(ran >> 32), 32);
                if (_writer.ByteCount <= NetConstants.MaxUnreliableMessageSize)
                {
                    _net.Send(slot, NetChannel.Sequenced, _writer.AsSpan());
                }
            }
        }

        _net.Flush(now);
    }

    private void Join(int slot, string name)
    {
        int? entity = _world.AddPlayer(slot, name);
        if (entity is null)
        {
            _net.Disconnect(slot, "no room in the level");
            return;
        }

        _clients[slot] = new ClientState(name, entity.Value);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Welcome, 8);
        _writer.WriteVarUInt((uint)entity.Value);
        _writer.WriteVarUInt((uint)TickRate);
        _writer.WriteString(Map, 64);
        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }

    private void ReadMessage(int slot, ClientState client, byte[] data, long serverTick)
    {
        BitReader reader = new(data);
        if ((SessionMessage)reader.ReadBits(8) != SessionMessage.Commands)
        {
            return;
        }

        long acknowledged = reader.ReadBits(32) | ((long)reader.ReadBits(32) << 32);
        _commands.Clear();
        if (reader.IsOverflowed || !_codec.Read(ref reader, _commands))
        {
            return;
        }

        client.History.Acknowledge(acknowledged);
        if (_world.CommandsFor(slot) is { } buffer)
        {
            foreach (UserCommand command in _commands)
            {
                buffer.Push(command, serverTick);
            }
        }
    }

    private sealed class ClientState(string name, int entity)
    {
        public string Name { get; } = name;

        public int Entity { get; } = entity;

        public SnapshotHistory History { get; } = new();
    }
}
