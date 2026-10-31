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
    private IReplicatedWorld _world;

    // Which of the server's levels a message belongs to, counted on at each travel: a snapshot or a command from the level
    // before is dropped, its ticks being another level's.
    private byte _epoch;

    // The players who dropped, by name, kept under a parked key of their own until they come back or their time is up.
    private readonly Dictionary<string, (int Parked, double Since)> _away = new(StringComparer.Ordinal);
    private int _nextParked = -1;

    // The names the host has put out of the room: they are not let back in while it stands.
    private readonly HashSet<string> _barred = new(StringComparer.Ordinal);

    // What was said and marked since the host last took it, its own included.
    private readonly List<ChatLine> _heard = [];
    private readonly List<WorldMark> _marked = [];
    private readonly List<VoicePacket> _voices = [];

    // The levels announced as going on to, counted; a client's Ready names the one it loaded.
    private byte _going;

    /// <summary>Gets the newest snapshot the world gave, where every human stood at the end of the last tick.</summary>
    public Snapshot? LastSnapshot { get; private set; }
    private double _now;
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
    /// <param name="hostSkin">The listen server's own player's look, a <see cref="PlayerSkins"/> id.</param>
    public ServerSession(
        NetServer net, IReplicatedWorld world, int tickRate, string map, MultiplayerRules? rules = null, string? hostName = null, string hostSkin = "")
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
            Match.Join(HostSlot, hostName, TeamRoster.Good).Skin = hostSkin;
            world.SetSkin(HostSlot, hostSkin);
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
    public string Map { get; private set; }

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
        _now = now;
        _net.Update(now);
        while (_net.TryReceive(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    JoinRequest? request = JoinRequest.Decode(e.Payload ?? []);
                    Join(e.Peer, request?.PlayerName ?? "player", request?.Skin ?? string.Empty);
                    break;
                case NetEventKind.Disconnected:
                    if (_clients.Remove(e.Peer, out ClientState? gone))
                    {
                        // Kept aside a while, in case the line dropped rather than the player leaving.
                        int parked = _nextParked--;
                        if (_world.ParkPlayer(e.Peer, parked))
                        {
                            Match.Move(e.Peer, parked);
                            _away[gone.Name] = (parked, now);
                        }
                        else
                        {
                            Match.Leave(e.Peer);
                        }
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
        foreach ((string name, (int parked, double since)) in _away.Where(a => now - a.Value.Since > Match.Rules.RejoinSeconds).ToArray())
        {
            _world.ForgetPlayer(parked);
            Match.Leave(parked);
            _away.Remove(name);
        }

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

        // The level's running state, to each client that hasn't the newest -- a client joining has none -- and to every client
        // once a second besides, which puts right whatever its own level wrote over it.
        if (_world.CaptureLevel() is { } level)
        {
            foreach (ClientState client in _clients.Values.Where(c => tick % TickRate == 0 || c.Level is null || !c.Level.AsSpan().SequenceEqual(level)))
            {
                client.Level = level;
            }

            foreach ((int slot, ClientState client) in _clients)
            {
                if (ReferenceEquals(client.Level, level))
                {
                    _writer.Reset();
                    _writer.WriteBits((uint)SessionMessage.Level, 8);
                    _writer.WriteVarUInt((uint)level.Length);
                    foreach (byte b in level)
                    {
                        _writer.WriteBits(b, 8);
                    }

                    _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
                }
            }
        }

        Snapshot snapshot = _world.Capture(tick);
        LastSnapshot = snapshot;
        LagCompensationHistory.Add(snapshot);
        if (tick % Math.Max(1, SnapshotInterval) == 0)
        {
            foreach ((int slot, ClientState client) in _clients)
            {
                // A line still carrying the last ones gets none this tick: a snapshot queued behind them would only
                // arrive late, and push the round trip out with it.
                if (_net.GetConnection(slot) is { IsChoked: true })
                {
                    continue;
                }

                client.History.Add(snapshot);
                SendSnapshot(slot, snapshot, client.History.Baseline, tick);
            }
        }

        _net.Flush(now);
    }

    // A snapshot to one client, in as few parts as fit a packet each: a level's soldiers fill more than one. Every part is
    // written against the same baseline and names its place, and the client takes the tick once it holds them all. The
    // doors, the rounds and the senses ride the first part alone: the others carry the baseline's doors (a bit) and no
    // events, so nothing lands twice.
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
                Snapshot piece = part == 0
                    ? snapshot with { Humans = slice }
                    : snapshot with { Humans = slice, Doors = baseline?.Doors, Lifts = baseline?.Lifts, Impacts = null, Senses = null };
                _writer.Reset();
                _writer.WriteBits((uint)SessionMessage.Snapshot, 8);
                _writer.WriteBits((uint)part, 8);
                _writer.WriteBits((uint)parts, 8);
                _writer.WriteBits(_epoch, 8);
                SnapshotCodec.Write(_writer, piece, baseline);
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

    private void Join(int slot, string name, string skin)
    {
        if (_barred.Contains(name))
        {
            _net.Disconnect(slot, "the host removed you from this room");
            return;
        }

        // One who dropped and comes back under its name has its player back, where and as it was, and its standing.
        int? entity = null;
        if (_away.Remove(name, out (int Parked, double Since) away) && _world.UnparkPlayer(away.Parked, slot) is { } back)
        {
            entity = back;
            Match.Move(away.Parked, slot);
        }

        entity ??= _world.AddPlayer(slot, name);
        if (entity is null)
        {
            _net.Disconnect(slot, "no room in the level");
            return;
        }

        _clients[slot] = new ClientState(name, entity.Value);
        if (!Match.Players.ContainsKey(slot))
        {
            Match.Join(slot, name, _world.TeamOf(slot));
        }

        Match.Players[slot].Skin = skin;
        _world.SetSkin(slot, skin);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Welcome, 8);
        _writer.WriteVarUInt((uint)entity.Value);
        _writer.WriteVarUInt((uint)TickRate);
        _writer.WriteString(Map, 64);
        _writer.WriteBits(_epoch, 8);
        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }

    /// <summary>Gets whether a name has been put out of the room, and is to be refused at the door.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    public bool IsBarred(string name) => _barred.Contains(name);

    /// <summary>Puts a client's player out of the room, and keeps its name out while the room stands.</summary>
    /// <param name="slot">The client's slot.</param>
    public void Kick(int slot)
    {
        if (!_clients.Remove(slot, out ClientState? client))
        {
            return;
        }

        _barred.Add(client.Name);
        _world.RemovePlayer(slot);
        Match.Leave(slot);
        _net.Disconnect(slot, "the host removed you from this room");
    }

    /// <summary>Says a line to every player, as the listen server's own player.</summary>
    /// <param name="text">The line.</param>
    public void Say(string text) => Say(new ChatLine(HostEntity, text));

    /// <summary>Marks a place for every player, as the listen server's own player.</summary>
    /// <param name="position">Where, in world units.</param>
    public void Mark(Int3 position) => Mark(new WorldMark(HostEntity, position));

    /// <summary>Sends a moment of the listen server's own player's voice to every player.</summary>
    /// <param name="packet">The encoded voice.</param>
    public void Speak(ReadOnlySpan<byte> packet) => Speak(new VoicePacket(HostEntity, packet.ToArray()), except: null);

    /// <summary>Takes the clients' voices heard since the last call, for the listen server's own player to hear.</summary>
    /// <returns>The moments, oldest first.</returns>
    public IReadOnlyList<VoicePacket> TakeVoices()
    {
        VoicePacket[] heard = [.. _voices];
        _voices.Clear();
        return heard;
    }

    // A voice to everyone but the one it is: the speaker hears itself already.
    private void Speak(VoicePacket voice, int? except)
    {
        if (except is not null)
        {
            _voices.Add(voice);
        }

        _writer.Reset();
        SessionTalk.WriteVoice(_writer, voice.Entity, voice.Packet);
        foreach (int slot in _clients.Keys)
        {
            if (slot != except)
            {
                _net.Send(slot, NetChannel.Unreliable, _writer.AsSpan());
            }
        }
    }

    /// <summary>Takes what has been said since the last call, the host's own lines included.</summary>
    /// <returns>The lines, oldest first.</returns>
    public IReadOnlyList<ChatLine> TakeChat()
    {
        ChatLine[] heard = [.. _heard];
        _heard.Clear();
        return heard;
    }

    /// <summary>Takes what has been marked since the last call, the host's own marks included.</summary>
    /// <returns>The marks, oldest first.</returns>
    public IReadOnlyList<WorldMark> TakeMarks()
    {
        WorldMark[] marked = [.. _marked];
        _marked.Clear();
        return marked;
    }

    private void Say(ChatLine line)
    {
        _heard.Add(line);
        _writer.Reset();
        SessionTalk.WriteChat(_writer, line.Entity, line.Text);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
    }

    private void Mark(WorldMark mark)
    {
        _marked.Add(mark);
        _writer.Reset();
        SessionTalk.WriteMark(_writer, mark.Entity, mark.Position);
        _net.Broadcast(NetChannel.Reliable, _writer.AsSpan());
    }

    /// <summary>Tells every player the level the server has begun loading, for each to load alongside it.</summary>
    /// <param name="map">Its map.</param>
    /// <remarks>
    /// Said before the server's own load blocks, so all of them load at once; each says when it has (<see cref="EveryoneReady"/>),
    /// and <see cref="Travel"/>, once the server's level is up and begun, is every player's word to begin theirs.
    /// </remarks>
    public void AnnounceGoing(string map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _going++;
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Going, 8);
        _writer.WriteBits(_going, 8);
        _writer.WriteString(map, 64);
        foreach ((int slot, ClientState client) in _clients)
        {
            client.ReadyFor = null;
            _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
        }
    }

    /// <summary>Gets whether every player has loaded the level last announced: the server's to begin when it is.</summary>
    public bool EveryoneReady => _clients.Values.All(c => c.ReadyFor is null ? _going == 0 : c.ReadyFor == _going);

    /// <summary>Sends what is waiting to go without capturing the level: for while the level is held, not run.</summary>
    /// <param name="now">The clock, in seconds.</param>
    public void Flush(double now) => _net.Flush(now);

    /// <summary>Goes on to another level with every player: the next mission, or the same one again.</summary>
    /// <param name="world">The new level's world.</param>
    /// <param name="map">Its map.</param>
    /// <remarks>
    /// Each client keeps its slot, so its player is made again in the new level under the same entity, and is told to load
    /// the map; until its new level is up, what it sends belongs to the old one and is dropped.
    /// </remarks>
    public void Travel(IReplicatedWorld world, string map)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        _world = world;
        Map = map;
        _epoch++;

        // A player away when the level changed is not kept into the next: if it comes back, it joins as anyone does.
        foreach ((int parked, double _) in _away.Values)
        {
            Match.Leave(parked);
        }

        _away.Clear();
        LagCompensationHistory.Clear();
        world.LagCompensation = new LagCompensation(LagCompensationHistory, TickRate);
        if (Match.Players.TryGetValue(HostSlot, out MatchPlayer? host))
        {
            world.SetSkin(HostSlot, host.Skin);
        }

        foreach ((int slot, ClientState client) in _clients.ToArray())
        {
            if (world.AddPlayer(slot, client.Name) is not { } entity)
            {
                _clients.Remove(slot);
                Match.Leave(slot);
                _net.Disconnect(slot, "no room in the level");
                continue;
            }

            world.SetSkin(slot, Match.Players.GetValueOrDefault(slot)?.Skin ?? string.Empty);
            _clients[slot] = new ClientState(client.Name, entity);
            _writer.Reset();
            _writer.WriteBits((uint)SessionMessage.Travel, 8);
            _writer.WriteBits(_epoch, 8);
            _writer.WriteVarUInt((uint)entity);
            _writer.WriteString(map, 64);
            _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
        }

        foreach (MatchPlayer player in Match.Players.Values)
        {
            player.IsAlive = true;
            player.DiedAt = null;
        }
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
        switch ((SessionMessage)reader.ReadBits(8))
        {
            // Said or marked by a client: told to everyone, under its own entity whatever it claimed.
            case SessionMessage.Chat when SessionTalk.ReadChat(ref reader) is { } line:
                Say(line with { Entity = client.Entity });
                return;
            case SessionMessage.Mark when SessionTalk.ReadMark(ref reader) is { } mark:
                Mark(mark with { Entity = client.Entity });
                return;
            case SessionMessage.Voice when SessionTalk.ReadVoice(ref reader) is { } voice:
                Speak(voice with { Entity = client.Entity }, except: slot);
                return;
            case SessionMessage.Ready:
                byte ready = (byte)reader.ReadBits(8);
                if (!reader.IsOverflowed)
                {
                    client.ReadyFor = ready;
                }

                return;
            case SessionMessage.Commands when reader.ReadBits(8) == _epoch:
                break;
            default:
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

        // The level state this client was last sent.
        public byte[]? Level { get; set; }

        // The announced level it has loaded, or null before it has loaded the one announced.
        public byte? ReadyFor { get; set; }
    }
}
