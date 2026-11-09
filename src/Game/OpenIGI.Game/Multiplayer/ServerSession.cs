using System.Security.Cryptography;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;
using OpenIGI.Net.Identity;

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
public sealed partial class ServerSession : IModeHost
{
    private readonly NetServer _net;
    private IReplicatedWorld _world;

    // Which of the server's levels a message belongs to, counted on at each travel: a snapshot or a command from the level
    // before is dropped, its ticks being another level's.
    private byte _epoch;

    // The players who dropped, by identity, kept under a parked key of their own until they come back or their time is up.
    private readonly Dictionary<string, (int Parked, double Since)> _away = new(StringComparer.Ordinal);
    private int _nextParked = -1;

    // The identities the host has put out of the room, with the name each played under: not let back in while it stands.
    private readonly Dictionary<string, string> _barred = new(StringComparer.Ordinal);

    // Clients connected but not yet let in: each has been sent a challenge, and is let in once its identity has signed it.
    private readonly Dictionary<int, PendingJoin> _pending = [];
    private readonly IPlayerVerifier? _verifier;
    private readonly ulong _protocolId;

    /// <summary>The longest a connected client may take to prove who it is before it is dropped, in seconds.</summary>
    public const double ProofTimeout = 15;

    // What was said and marked since the host last took it, its own included.
    private readonly List<ChatLine> _heard = [];
    private readonly List<WorldMark> _marked = [];
    private readonly List<VoicePacket> _voices = [];

    // The levels announced as going on to, counted; a client's Ready names the one it loaded.
    private byte _going;

    /// <summary>Gets the newest snapshot the world gave, where every human stood at the end of the last tick.</summary>
    public Snapshot? LastSnapshot { get; private set; }
    private double _now;

    // The tick the mode last ran on: what a command between ticks is dated by.
    private long _tick;

    /// <summary>Gets or sets the title played, as <c>igi1</c>.</summary>
    public string Title { get; set; } = string.Empty;
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
    /// <param name="verifier">
    /// Checks the clients' signatures; <see langword="null"/> takes the platform's own ECDSA where it has one and lets in
    /// every client as someone new where it has none.
    /// </param>
    /// <param name="protocolId">The protocol the session plays, which every identity proof names.</param>
    public ServerSession(
        NetServer net, IReplicatedWorld world, int tickRate, string map, MultiplayerRules? rules = null, string? hostName = null, string hostSkin = "",
        IPlayerVerifier? verifier = null, ulong protocolId = 0)
    {
        _verifier = verifier ?? (PlayerIdentities.Default as IPlayerVerifier) ?? (EcdsaPlayerIdentity.IsSupported ? EcdsaPlayerVerifier.Instance : null);
        _protocolId = protocolId;
        _net = net ?? throw new ArgumentNullException(nameof(net));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        TickRate = tickRate;
        Map = map ?? throw new ArgumentNullException(nameof(map));
        _codec = new UserCommandCodec(world.ChannelCount);
        world.LagCompensation = new LagCompensation(LagCompensationHistory, tickRate);
        Match = new Match(rules ?? new MultiplayerRules());
        Mode = GameModes.For(null, world, tickRate);
        if (hostName is not null)
        {
            Match.Join(HostSlot, hostName, TeamRoster.Good).Skin = hostSkin;
            world.SetSkin(HostSlot, hostSkin);
        }
    }

    /// <summary>The match slot of a listen server's own player, past every client's.</summary>
    public const int HostSlot = 1000;

    /// <summary>
    /// Gets or sets whether the match pays by the money settings (<see cref="NetCVars.MoneyKill"/> and the rest) rather than the
    /// rules it was made with: a game's hosted session does, a test's own rules do not.
    /// </summary>
    public bool UsesMoneySettings { get; set; }

    /// <summary>The entity id of a listen server's own player.</summary>
    public const int HostEntity = 0;

    /// <summary>Gets the match: money, kills, scores and rounds.</summary>
    public Match Match { get; }

    /// <summary>Gets or sets the mode the match is played by; set before the first tick (the world's own by default).</summary>
    /// <remarks>A new mode's rounds start the level afresh: it is marked again as the first of them begins.</remarks>
    public IGameMode Mode
    {
        get => _mode;
        set
        {
            _mode = value ?? throw new ArgumentNullException(nameof(value));
            _levelMarked = false;
        }
    }

    private IGameMode _mode = null!;

    // Whether the level's start has been marked for this mode's rounds to go back to (IModeHost.ResetLevel).
    private bool _levelMarked;


    /// <summary>Gets the world's ticks per second.</summary>
    public int TickRate { get; }

    /// <summary>Gets the map.</summary>
    public string Map { get; private set; }

    /// <summary>Gets or sets how many ticks apart snapshots are sent; one sends every tick.</summary>
    public int SnapshotInterval { get; set; } = 1;

    /// <summary>Gets the server's own recent snapshots, where shots are traced against the world as each shooter saw it.</summary>
    public SnapshotInterpolator LagCompensationHistory { get; } = new(64);

    /// <summary>Gets the connected clients' slots.</summary>
    public IEnumerable<int> Clients => _clients.Where(static c => !c.Value.Watching).Select(static c => c.Key);

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
        foreach (int slot in _clients.Keys)
        {
            if (_world.CommandsFor(slot) is { } commands)
            {
                commands.ServerTick = serverTick;
            }
        }

        while (_net.TryReceive(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    JoinRequest? request = JoinRequest.Decode(e.Payload ?? []);
                    if (TryBeginSync(e.Peer, request))
                    {
                        break;
                    }

                    BeginJoin(e.Peer, request?.PlayerName ?? "player", request?.Skin ?? string.Empty);
                    NoteWatch(e.Peer, request);
                    ApplyRates(e.Peer, request);
                    break;
                case NetEventKind.Disconnected when _syncing.ContainsKey(e.Peer):
                    ForgetFiles(e.Peer);
                    break;
                case NetEventKind.Disconnected when _pending.Remove(e.Peer):
                    _watching.Remove(e.Peer);
                    ForgetFiles(e.Peer);
                    break;
                case NetEventKind.Disconnected:
                    ForgetFiles(e.Peer);
                    if (_clients.Remove(e.Peer, out ClientState? gone) && !gone.Watching)
                    {
                        // Kept aside a while, in case the line dropped rather than the player leaving.
                        Park(e.Peer, gone.PlayerId, now);
                    }

                    break;
                case NetEventKind.Message when e.Message.Data.Length > 0 && _syncing.ContainsKey(e.Peer):
                    BitReader syncReader = new(e.Message.Data);
                    if ((SessionMessage)syncReader.ReadBits(8) == SessionMessage.FileWant)
                    {
                        ReadFileWant(e.Peer, ref syncReader);
                    }

                    break;
                case NetEventKind.Message when e.Message.Data.Length > 0 && _pending.TryGetValue(e.Peer, out PendingJoin? pending):
                    ReadProof(pending, e.Message.Data);
                    break;
                case NetEventKind.Message when e.Message.Data.Length > 0 && _clients.TryGetValue(e.Peer, out ClientState? client):
                    ReadMessage(e.Peer, client, e.Message.Data, serverTick);
                    break;
            }
        }

        ResolvePending(now);
    }

    // A client connected: challenged to prove who it is, and let in once it has.
    /// <summary>Gets or sets the fewest snapshots a second a client may ask for (<c>sv_minupdaterate</c>).</summary>
    public int MinUpdateRate { get; set; } = 10;

    /// <summary>Gets or sets the least and most bytes a second a client's line may be given (<c>sv_minrate</c>, <c>sv_maxrate</c>).</summary>
    public (int Min, int Max) RateLimits { get; set; } = (16 * 1024, 1024 * 1024);

    // Each client's ticks between snapshots, from what it asked.
    private readonly Dictionary<int, int> _intervals = [];

    // What a client asked of its line, within what this server allows: snapshots every so many ticks, so many bytes a second.
    private void ApplyRates(int slot, JoinRequest? request)
    {
        int wanted = Math.Clamp(request?.UpdateRate is > 0 and int asked ? asked : TickRate, Math.Min(MinUpdateRate, TickRate), TickRate);
        _intervals[slot] = (int)Math.Ceiling(TickRate / (double)wanted);
        if (request?.Rate is > 0 and int bytes && _net.GetConnection(slot) is { } line)
        {
            line.Rate = Math.Clamp(bytes, RateLimits.Min, RateLimits.Max);
        }
    }

    private void BeginJoin(int slot, string name, string skin)
    {
        byte[] challenge = RandomNumberGenerator.GetBytes(PlayerIdentities.ChallengeSize);
        _pending[slot] = new PendingJoin(name, skin, challenge, _now);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Prove, 8);
        foreach (byte b in challenge)
        {
            _writer.WriteBits(b, 8);
        }

        _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
    }

    // A client's answer to its challenge: its key, and its signature, checked as the verifier can (a browser's asynchronously).
    // A client with no identity to give (an empty key) is let in as someone new, who can't rejoin as itself.
    private void ReadProof(PendingJoin pending, byte[] data)
    {
        BitReader reader = new(data);
        if ((SessionMessage)reader.ReadBits(8) != SessionMessage.Proof || pending.Check is not null || pending.Anonymous)
        {
            return;
        }

        byte[] key = ReadBytes(ref reader, PlayerIdentities.PublicKeySize);
        byte[] signature = ReadBytes(ref reader, PlayerIdentities.SignatureSize);
        if (reader.IsOverflowed)
        {
            return;
        }

        if (key.Length == 0 || _verifier is null)
        {
            pending.Anonymous = true;
            return;
        }

        pending.Key = key;
        pending.Check = _verifier.VerifyAsync(key, PlayerIdentities.ProofOf(_protocolId, pending.Challenge), signature);
    }

    // The clients whose proofs are in let in, those that failed or took too long dropped.
    private void ResolvePending(double now)
    {
        foreach ((int slot, PendingJoin pending) in _pending.ToArray())
        {
            if (pending.Anonymous)
            {
                _pending.Remove(slot);
                Join(slot, pending.Name, pending.Skin, "anon-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
            }
            else if (pending.Check is { IsCompleted: true } check)
            {
                _pending.Remove(slot);
                if (check.IsCompletedSuccessfully && check.Result)
                {
                    Join(slot, pending.Name, pending.Skin, PlayerIdentities.IdOf(pending.Key));
                }
                else
                {
                    _net.Disconnect(slot, "your identity could not be checked");
                }
            }
            else if (now - pending.Since > ProofTimeout)
            {
                _pending.Remove(slot);
                _net.Disconnect(slot, "your game never said who it is");
            }
        }
    }

    private static byte[] ReadBytes(ref BitReader reader, int most)
    {
        uint length = reader.ReadVarUInt();
        if (length > most)
        {
            return [];
        }

        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)reader.ReadBits(8);
        }

        return bytes;
    }

    /// <summary>Captures the world after its tick and sends each client what is due.</summary>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="tick">The tick just run.</param>
    public void Send(double now, long tick)
    {
        TickSay(now);
        foreach ((string name, (int parked, double since)) in _away.Where(a => now - a.Value.Since > Match.Rules.RejoinSeconds).ToArray())
        {
            _world.ForgetPlayer(parked);
            Match.Leave(parked);
            _away.Remove(name);
        }

        if (!_modeStarted)
        {
            _modeStarted = true;
            Mode.Start(this, tick);
        }

        _tick = tick;
        PumpFiles(now);

        // The money the operator set (mp_moneykill and the rest), taken from this tick's pay on.
        if (UsesMoneySettings)
        {
            Match.Rules = NetCVars.WithMoney(Match.Rules);
        }

        Mode.Tick(this, tick, _world.TakeDeaths());
        TellKits();
        TellCombat();
        TellMatch(tick);

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
                    client.LevelMessages++;
                    client.LevelBytes += _writer.ByteCount;
                }
            }
        }

        Snapshot snapshot = _world.Capture(tick);
        LastSnapshot = snapshot;
        LagCompensationHistory.Add(snapshot);
        foreach ((int slot, ClientState client) in _clients)
        {
            if (tick % Math.Max(Math.Max(1, SnapshotInterval), _intervals.GetValueOrDefault(slot, 1)) != 0)
            {
                continue;
            }

            // A line still carrying the last ones gets none this tick: a snapshot queued behind them would only
            // arrive late, and push the round trip out with it.
            if (_net.GetConnection(slot) is { } line && line.IsChokedAt(now))
            {
                client.SnapshotsChoked++;
                continue;
            }

            client.History.Add(snapshot);
            SendSnapshot(slot, snapshot, client.History.Baseline, tick);
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

        // Started at as many parts as this client's last snapshot took -- the level changes little from one tick to the next
        // -- and once a second at one fewer, so a level that quietened is sent in fewer again: most ticks write it once.
        _clients.TryGetValue(slot, out ClientState? state);
        int first = state is null ? 1 : Math.Max(1, state.Parts - (tick % TickRate == 0 ? 1 : 0));
        for (int parts = first; parts <= MaxSnapshotParts; parts++)
        {
            int per = (snapshot.Humans.Length + parts - 1) / Math.Max(1, parts);
            List<byte[]> written = [];
            for (int part = 0; part < parts; part++)
            {
                int from = Math.Min(part * per, snapshot.Humans.Length);
                ReplicatedHuman[] slice = snapshot.Humans.AsSpan(from, Math.Min(per, snapshot.Humans.Length - from)).ToArray();
                Snapshot piece = part == 0
                    ? snapshot with { Humans = slice }
                    : snapshot with { Humans = slice, Doors = baseline?.Doors, Lifts = baseline?.Lifts, Cameras = baseline?.Cameras, Wrecks = baseline?.Wrecks, Projectiles = baseline?.Projectiles, Tasks = baseline?.Tasks, Impacts = null, Senses = null };
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
                    if (_clients.TryGetValue(slot, out ClientState? sent))
                    {
                        sent.SnapshotBytes += message.Length;
                    }
                }

                if (_clients.TryGetValue(slot, out ClientState? counted))
                {
                    counted.SnapshotsSent++;
                    counted.SnapshotParts += parts;
                    counted.Parts = parts;
                }

                return;
            }
        }
    }

    /// <summary>Describes each client's connection since the last call, for finding where its round trip goes, and starts afresh.</summary>
    /// <param name="seconds">How long since the last call.</param>
    /// <param name="route">Describes a client's address: its route and what its transport dropped; <see langword="null"/> for none.</param>
    /// <returns>One line per client.</returns>
    public IEnumerable<string> Diagnose(double seconds, Func<NetAddress, string>? route = null)
    {
        double per = Math.Max(0.001, seconds);
        List<string> lines = [];
        foreach ((int slot, ClientState client) in _clients)
        {
            NetConnection? connection = _net.GetConnection(slot);
            NetStatistics? stats = connection?.Statistics;
            UserCommandBuffer? commands = _world.CommandsFor(slot);
            lines.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"to {client.Name} (slot {slot}): rtt {stats?.RoundTripTime * 1000:F0} ms jitter {stats?.RoundTripJitter * 1000:F0} ms loss {stats?.PacketLoss * 100:F1}% " +
                $"resends {stats?.ReliableResends} unacked {connection?.PendingReliableCount} backlog {connection?.Backlog} B choked {connection?.IsChoked} | " +
                $"out {stats?.BytesSentPerSecond / 1024:F1} KiB/s in {stats?.BytesReceivedPerSecond / 1024:F1} KiB/s | " +
                $"snapshots {client.SnapshotsSent / per:F1}/s skipped {client.SnapshotsChoked / per:F1}/s parts {(client.SnapshotsSent == 0 ? 0 : (double)client.SnapshotParts / client.SnapshotsSent):F1} " +
                $"avg {(client.SnapshotsSent == 0 ? 0 : client.SnapshotBytes / client.SnapshotsSent)} B | level {client.LevelMessages / per:F1}/s {client.LevelBytes / per / 1024:F1} KiB/s | " +
                $"commands waiting {commands?.Buffered} | {(connection is null || route is null ? "-" : route(connection.RemoteAddress))}"));
            client.SnapshotsSent = client.SnapshotsChoked = client.SnapshotParts = 0;
            client.SnapshotBytes = client.LevelBytes = 0;
            client.LevelMessages = 0;
        }

        lines.AddRange(_world.TakeCombatReport());
        return lines;
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

    // A player whose line went, kept aside under its key to be given back if it comes back. One key keeps one: a player
    // already kept under it (two copies of the game on one machine, both gone) is let go, rather than left in the match
    // under a slot nothing names any more -- a player with no line, standing on the scoreboard for good.
    private void Park(int slot, string playerId, double now)
    {
        if (_away.Remove(playerId, out (int Parked, double Since) earlier))
        {
            _world.ForgetPlayer(earlier.Parked);
            Match.Leave(earlier.Parked);
        }

        int parked = _nextParked--;
        if (_world.ParkPlayer(slot, parked))
        {
            Match.Move(slot, parked);
            _away[playerId] = (parked, now);
        }
        else
        {
            Match.Leave(slot);
        }
    }

    // How long a line must have been silent for a new line under its key to be taken for its player coming back.
    private const double QuietLineSeconds = 1.0;

    // Ends the quiet line of a player that just joined again on a new one, parking its player as a drop does.
    private void ReplaceQuietLine(string playerId)
    {
        foreach ((int old, ClientState client) in _clients)
        {
            if (client.PlayerId != playerId
                || _net.GetConnection(old) is not { } line
                || _now - line.LastReceiveTime < QuietLineSeconds)
            {
                continue;
            }

            _clients.Remove(old);
            Park(old, playerId, _now);
            _net.Disconnect(old, "you joined again on another line");
            return;
        }
    }

    private void Join(int slot, string name, string skin, string playerId)
    {
        if (TurnedAway(playerId) is { } refusal)
        {
            _net.Disconnect(slot, refusal);
            return;
        }

        if (_watching.Remove(slot))
        {
            Watch(slot, name, playerId);
            return;
        }

        // Back on a new line before this one's old line timed out: the old one gone quiet is that same player's, and goes,
        // its player kept for this one. Two lines both alive under one key are two copies of the game on one machine.
        ReplaceQuietLine(playerId);

        // Two players may share a name; the room tells them apart.
        name = UniqueName(name, playerId);

        // One who dropped and comes back as itself has its player back, where and as it was, and its standing.
        int? entity = null;
        if (_away.Remove(playerId, out (int Parked, double Since) away) && _world.UnparkPlayer(away.Parked, slot) is { } back)
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

        _clients[slot] = new ClientState(name, entity.Value, playerId);
        if (!Match.Players.ContainsKey(slot))
        {
            int team = Mode.TeamFor(this, slot);
            Match.Join(slot, name, team);
            _world.SetTeam(slot, team);
        }

        Match.Players[slot].Skin = skin;
        _world.SetSkin(slot, skin);
        Welcome(slot, entity.Value);
    }

    /// <summary>Gets whether a player of this name has been put out of the room.</summary>
    /// <param name="name">The name it played under.</param>
    /// <returns><see langword="true"/> when one was.</returns>
    /// <remarks>What is refused at the door is the player's identity, not its name: another of the same name still comes in.</remarks>
    public bool IsBarred(string name) => _barred.ContainsValue(name);

    /// <summary>Gets a client's identity, or <see langword="null"/>.</summary>
    /// <param name="slot">The client's slot.</param>
    /// <returns>Its <see cref="PlayerIdentities.IdOf">id</see>, or <c>anon-</c> and a number for one that gave none.</returns>
    public string? IdOf(int slot) => _clients.TryGetValue(slot, out ClientState? client) ? client.PlayerId : null;

    // A name no other player in the room has: the one asked for, or it with a number after.
    private string UniqueName(string name, string playerId)
    {
        // Two games of one install (the same key) may play side by side, a host testing its own room: told apart by name too.
        _ = playerId;
        bool Taken(string candidate) =>
            _clients.Values.Any(c => c.Name == candidate)
            || (Match.Players.TryGetValue(HostSlot, out MatchPlayer? host) && host.Name == candidate);

        if (!Taken(name))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string numbered = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} ({n})");
            if (!Taken(numbered))
            {
                return numbered;
            }
        }
    }

    /// <summary>Puts a client's player out of the room, and keeps its name out while the room stands.</summary>
    /// <param name="slot">The client's slot.</param>
    public void Kick(int slot)
    {
        if (!_clients.Remove(slot, out ClientState? client))
        {
            return;
        }

        _barred[client.PlayerId] = client.Name;
        _world.RemovePlayer(slot);
        Match.Leave(slot);
        _net.Disconnect(slot, "the host removed you from this room");
    }

    /// <summary>Says a line to every player, or its team only, as the listen server's own player.</summary>
    /// <param name="text">The line.</param>
    /// <param name="team">Whether only the host's team hears it.</param>
    public void Say(string text, bool team = false) => Say(new ChatLine(HostEntity, text, team ? ChatKind.Team : ChatKind.All), HostSlot);

    /// <summary>Tells every player something in the server's own word: nobody's line, shown as the server's.</summary>
    /// <param name="text">What to tell.</param>
    public void Announce(string text) => Say(new ChatLine(-1, text, ChatKind.Server), null);

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

    // A line to those it is for: a team line to the speaker's team only (everyone, where the mode plays no teams), any other
    // to all. The listen host's own player hears what its team would.
    private void Say(ChatLine line, int? speaker)
    {
        int? team = line.Kind == ChatKind.Team && Mode.IsTeamMode && speaker is { } from && Match.Players.TryGetValue(from, out MatchPlayer? who)
            ? who.Team
            : null;
        bool Hears(int slot) => team is not { } side || (Match.Players.TryGetValue(slot, out MatchPlayer? player) && player.Team == side);

        if (Hears(HostSlot) || speaker == HostSlot)
        {
            _heard.Add(line);
        }

        _writer.Reset();
        SessionTalk.WriteChat(_writer, line.Entity, line.Text, line.Kind);
        foreach (int slot in _clients.Keys)
        {
            if (Hears(slot) || slot == speaker)
            {
                _net.Send(slot, NetChannel.Reliable, _writer.AsSpan());
            }
        }
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

        // With the files the new map plays from, which a client whose own differ fetches before it says it is ready.
        foreach (int slot in _clients.Keys)
        {
            SendFileList(slot, map);
        }
    }

    /// <summary>Gets whether every player has loaded the level last announced: the server's to begin when it is.</summary>
    public bool EveryoneReady => _clients.Values.All(c => c.ReadyFor is null ? _going == 0 : c.ReadyFor == _going);

    /// <summary>Sends what is waiting to go without capturing the level: for while the level is held, not run.</summary>
    /// <param name="now">The clock, in seconds.</param>
    public void Flush(double now)
    {
        PumpFiles(now);
        _net.Flush(now);
    }

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
        _levelMarked = false;

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
            if ((client.Watching ? WatcherEntity : world.AddPlayer(slot, client.Name)) is not { } entity)
            {
                _clients.Remove(slot);
                Match.Leave(slot);
                _net.Disconnect(slot, "no room in the level");
                continue;
            }

            if (!client.Watching)
            {
                world.SetSkin(slot, Match.Players.GetValueOrDefault(slot)?.Skin ?? string.Empty);
            }

            _clients[slot] = new ClientState(client.Name, entity, client.PlayerId) { Watching = client.Watching };
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

    // How far behind the server's present a client can honestly have been drawing the others when its command arrives: the
    // snapshot it drew was half a round trip old, its command took the other half to come, it draws its interpolation behind
    // that, and the line jitters -- Source's latency plus lerp, within sv_maxunlag. Some ticks' slack for its command buffer and
    // a hitch.
    private int MaxRewindTicks(int slot)
    {
        NetStatistics? stats = _net.GetConnection(slot)?.Statistics;
        double interpolation = _world.LagCompensation?.InterpolationDelay ?? 0.1;
        double unlag = _world.LagCompensation?.MaxUnlag ?? 1.0;
        double seconds = Math.Min(unlag, (stats?.RoundTripTime ?? 0) + ((stats?.RoundTripJitter ?? 0) * 3) + interpolation);
        return (int)Math.Ceiling(seconds * TickRate) + RewindSlackTicks;
    }

    // The ticks beyond what a client's line explains that its view may lag the server: its command buffer and a hitch.
    private const int RewindSlackTicks = 8;

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
            // Said or marked by a client: told to everyone, under its own entity whatever it claimed, and only as fast as its
            // budget allows -- what one client sends, every other is sent.
            case SessionMessage.Chat when SessionTalk.ReadChat(ref reader) is { } line:
                if (client.Chat.TrySpend(_now) && !TryCommand(slot, line.Text))
                {
                    Say(line with { Entity = client.Entity, Kind = line.Kind == ChatKind.Team ? ChatKind.Team : ChatKind.All }, slot);
                }

                return;
            case SessionMessage.Mark when SessionTalk.ReadMark(ref reader) is { } mark:
                if (client.Marks.TrySpend(_now))
                {
                    Mark(mark with { Entity = client.Entity });
                }

                return;
            case SessionMessage.Voice when SessionTalk.ReadVoice(ref reader) is { } voice:
                if (client.Voice.TrySpend(_now, voice.Packet.Length))
                {
                    Speak(voice with { Entity = client.Entity }, except: slot);
                }

                return;
            case SessionMessage.Rcon:
                ReadRcon(slot, client, ref reader);
                return;
            case SessionMessage.FileWant:
                ReadFileWant(slot, ref reader);
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
            buffer.MaxRewindTicks = MaxRewindTicks(slot);
            foreach (UserCommand command in _commands)
            {
                buffer.Push(command);
                if (Differs(command.Channels, client.LastChannels))
                {
                    client.ActiveAt = _now;
                    client.LastChannels = (float[])command.Channels.Clone();
                }
            }
        }
    }

    // A client connected but not let in: its join, its challenge, and the check of its answer once it came.
    private sealed class PendingJoin(string name, string skin, byte[] challenge, double since)
    {
        public string Name { get; } = name;

        public string Skin { get; } = skin;

        public byte[] Challenge { get; } = challenge;

        public double Since { get; } = since;

        public byte[] Key { get; set; } = [];

        public Task<bool>? Check { get; set; }

        public bool Anonymous { get; set; }
    }

    private sealed class ClientState(string name, int entity, string playerId)
    {
        public bool Watching { get; init; }

        public string Name { get; } = name;

        public string PlayerId { get; } = playerId;

        public int Entity { get; } = entity;

        public SnapshotHistory History { get; } = new();

        // What it may send everyone: a line a second (five at once), two marks a second, and voice at twice what speech takes.
        public TokenBucket Chat { get; } = new(1, 5);

        public TokenBucket Marks { get; } = new(2, 6);

        public TokenBucket Voice { get; } = new(8 * 1024, 16 * 1024);

        // The level state this client was last sent.
        public byte[]? Level { get; set; }

        // The announced level it has loaded, or null before it has loaded the one announced.
        public byte? ReadyFor { get; set; }

        // Counted for Diagnose since its last call.
        public int SnapshotsSent { get; set; }

        public int SnapshotsChoked { get; set; }

        public int SnapshotParts { get; set; }

        // The parts its last snapshot took, where the next starts trying.
        public int Parts { get; set; } = 1;

        public long SnapshotBytes { get; set; }

        public int LevelMessages { get; set; }

        // When its player last touched the keys, and what it sent then (the idle kick's).
        public double ActiveAt { get; set; } = double.NaN;

        public float[]? LastChannels { get; set; }

        public long LevelBytes { get; set; }
    }
}
