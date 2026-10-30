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

    // The most packets one snapshot is split over.
    private const int MaxSnapshotParts = 16;
    private readonly List<UserCommand> _commands = [];

    /// <summary>Initializes a new instance of the <see cref="ServerSession"/> class.</summary>
    /// <param name="net">The server's network end; the session reads its events, so nothing else should.</param>
    /// <param name="world">The title's world.</param>
    /// <param name="tickRate">The world's ticks per second.</param>
    /// <param name="map">The map, told to each client as it joins.</param>
    /// <param name="rules">The server's rules, or the original's defaults.</param>
    /// <param name="hostName">The listen server's own player's name, or <see langword="null"/> on a dedicated server.</param>
    public ServerSession(NetServer net, IReplicatedWorld world, int tickRate, string map, MultiplayerRules? rules = null, string? hostName = null)
    {
        _net = net ?? throw new ArgumentNullException(nameof(net));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        TickRate = tickRate;
        Map = map ?? throw new ArgumentNullException(nameof(map));
        _codec = new UserCommandCodec(world.ChannelCount);
        world.LagCompensation = new LagCompensation(LagCompensationHistory, tickRate);
        Match = new Match(rules ?? new MultiplayerRules());
        if (hostName is not null)
        {
            Match.Join(HostSlot, hostName, TeamRoster.Good);
        }
    }

    /// <summary>The match slot of a listen server's own player, past every client's.</summary>
    public const int HostSlot = 1000;

    /// <summary>The entity id of a listen server's own player.</summary>
    public const int HostEntity = 0;

    /// <summary>Gets the match: money, kills, scores and rounds.</summary>
    public Match Match { get; }

    /// <summary>Gets the round of objectives, once a map that has them is played, or <see langword="null"/>.</summary>
    public ObjectiveRound? Round { get; private set; }

    /// <summary>Gets or sets how long a dead player waits before the server respawns it, in seconds, once it can pay.</summary>
    public double RespawnDelay { get; set; } = 3;

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
                        Match.Leave(e.Peer);
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
        double seconds = (double)tick / TickRate;
        RunRound(tick);
        foreach (WorldDeath death in _world.TakeDeaths())
        {
            Match.Kill(SlotOf(death.Victim), death.Killer < 0 ? -1 : SlotOf(death.Killer), seconds);
        }

        if (_world.HoldsTheDead)
        {
            // Down until someone comes for them: the world says who was revived, and the match counts them alive.
            foreach (int slot in _world.TakeRevivals())
            {
                if (Match.Players.TryGetValue(slot, out MatchPlayer? revived))
                {
                    revived.IsAlive = true;
                    revived.DiedAt = null;
                }
            }
        }
        else
        {
            foreach (int slot in _clients.Keys.Append(HostSlot))
            {
                if (Match.Players.TryGetValue(slot, out MatchPlayer? player) && player.DiedAt is { } died
                    && seconds - died >= RespawnDelay && Match.TrySpawn(slot, seconds))
                {
                    _world.Respawn(slot);
                }
            }
        }

        if (tick % TickRate == 0)
        {
            _writer.Reset();
            _writer.WriteBits((uint)SessionMessage.Scoreboard, 8);
            CurrentScoreboard().Write(_writer);
            _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
        }

        Snapshot snapshot = _world.Capture(tick);
        LagCompensationHistory.Add(snapshot);
        if (tick % Math.Max(1, SnapshotInterval) == 0)
        {
            foreach ((int slot, ClientState client) in _clients)
            {
                client.History.Add(snapshot);
                SendSnapshot(slot, snapshot, client.History.Baseline, tick);
            }
        }

        _net.Flush(now);
    }

    // A snapshot to one client, in as few parts as fit a packet each: a level's soldiers fill more than one. Every part is
    // written against the same baseline and names its place, and the client takes the tick once it holds them all.
    private void SendSnapshot(int slot, Snapshot snapshot, Snapshot? baseline, long tick)
    {
        UserCommandBuffer? commands = _world.CommandsFor(slot);
        long ran = commands?.LastTakenTick ?? -1;

        // How many of the client's commands wait to be run: the delay its queue adds, for its net graph (0x8000: none yet).
        int lead = commands is not { LastTakenTick: not long.MinValue } ? short.MinValue : Math.Min(commands.Buffered, short.MaxValue);

        for (int parts = 1; parts <= MaxSnapshotParts; parts++)
        {
            int per = (snapshot.Humans.Length + parts - 1) / Math.Max(1, parts);
            List<byte[]> written = [];
            for (int part = 0; part < parts; part++)
            {
                ReplicatedHuman[] slice = snapshot.Humans.Skip(part * per).Take(per).ToArray();
                _writer.Reset();
                _writer.WriteBits((uint)SessionMessage.Snapshot, 8);
                _writer.WriteBits((uint)part, 8);
                _writer.WriteBits((uint)parts, 8);
                SnapshotCodec.Write(_writer, snapshot with { Humans = slice }, baseline);
                _writer.WriteBits((uint)ran, 32);
                _writer.WriteBits((uint)(ran >> 32), 32);
                _writer.WriteBits((ushort)(short)lead, 16);
                if (_writer.ByteCount > NetConstants.MaxUnreliableMessageSize)
                {
                    break;
                }

                written.Add(_writer.AsSpan().ToArray());
            }

            if (written.Count == parts)
            {
                // One part is sequenced as ever; several go unordered, since a part overtaken by its sibling is still wanted
                // (the client drops a tick older than one it holds by itself).
                foreach (byte[] message in written)
                {
                    _net.Send(slot, parts == 1 ? NetChannel.Sequenced : NetChannel.Unreliable, message);
                }

                return;
            }
        }
    }

    /// <summary>Gets the scoreboard as it stands: every player's standing, entity and round trip.</summary>
    /// <returns>The scoreboard.</returns>
    public Scoreboard CurrentScoreboard() => Scoreboard.Of(Match, EntityOfSlot, PingOfSlot);

    // A player's round trip in milliseconds, by its slot: the listen server's own player has none.
    private int PingOfSlot(int slot) =>
        slot != HostSlot && _net.GetConnection(slot) is { } connection
            ? (int)Math.Round(connection.Statistics.RoundTripTime * 1000)
            : 0;

    // A player's entity in the snapshots, by its slot: the listen server's own is the host entity.
    private int EntityOfSlot(int slot) =>
        slot == HostSlot ? HostEntity : _clients.TryGetValue(slot, out ClientState? client) ? client.Entity : -1;

    private void Join(int slot, string name)
    {
        int? entity = _world.AddPlayer(slot, name);
        if (entity is null)
        {
            _net.Disconnect(slot, "no room in the level");
            return;
        }

        _clients[slot] = new ClientState(name, entity.Value);
        Match.Join(slot, name, _world.TeamOf(slot));
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Welcome, 8);
        _writer.WriteVarUInt((uint)entity.Value);
        _writer.WriteVarUInt((uint)TickRate);
        _writer.WriteString(Map, 64);
        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }

    // The map's objectives, taken in order; a new round puts every player back at its team's spawn.
    private void RunRound(long tick)
    {
        if (Round is null)
        {
            if (_world.RoundObjectives.Count == 0)
            {
                return;
            }

            Round = new ObjectiveRound(_world.RoundObjectives, Match, TickRate);
            Round.Start(tick);
            _world.CurrentObjective = Round.Current;
            return;
        }

        RoundEvent happened = Round.Tick(tick);
        _world.CurrentObjective = Round.Current;
        if (happened == RoundEvent.Restart)
        {
            foreach (int slot in _clients.Keys)
            {
                _world.Respawn(slot);
                if (Match.Players.TryGetValue(slot, out MatchPlayer? player))
                {
                    player.IsAlive = true;
                    player.DiedAt = null;
                }
            }
        }
    }

    // A client's player's entity is the session's own record; the listen server's player is entity 0.
    private int SlotOf(int entity)
    {
        foreach ((int slot, ClientState client) in _clients)
        {
            if (client.Entity == entity)
            {
                return slot;
            }
        }

        return entity == HostEntity ? HostSlot : -1;
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
                buffer.Push(command);
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
