using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <summary>A client's session run inside the host: its own player played as usual, the others shown as the server has them.</summary>
/// <remarks>
/// The host plays the same map locally. Once its title's player is ticking, the session connects; after each tick it sends
/// that tick's command and shows every other player where the snapshots put it.
/// </remarks>
public sealed class SessionClient : IHostTickObserver, ISessionScoreboard, ISessionFollower, ISessionTalk, ISessionLook, ISessionHold, ISessionMode, IDisposable
{
    private const string LogCategory = "Session";

    private INetTransport _transport;

    // Opens a new line to the same server, for joining again after this one went quiet; null when it can't be.
    private readonly Func<INetTransport>? _reopen;

    // When the line went quiet under a joined session, and when the next try at joining again is due.
    private double? _lostAt;
    private double _retryAt;
    private readonly NetAddress _server;
    private readonly JoinRequest _request;
    private readonly string _titleId;
    private readonly IOpenIgiLogger _logger;
    private readonly OpenIGI.Net.Identity.IPlayerIdentity? _identity;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IReplicatedClientWorld? _world;
    private NetClient? _netClient;
    private ClientSession? _session;
    private bool _welcomed;
    // The level the session was last bound to.
    private object? _level;
    private string? _ended;

    // The readout's last write: the counts it was taken at, and the ticks and pumps since (a client that ticks slowly reads
    // the server's packets late, and acknowledges them late).
    private double _diagnosedAt;
    private int _ticksSinceDiagnosed;
    private int _pumpsSinceDiagnosed;
    private long _partsAt;
    private long _snapshotsAt;
    private long _snapshotBytesAt;
    private long _correctionsAt;
    private long _droppedSnapshotsAt;
    private long _heldAt;

    /// <summary>Initializes a new instance of the <see cref="SessionClient"/> class.</summary>
    /// <param name="transport">What it sends on; the client disposes it.</param>
    /// <param name="server">The server to join.</param>
    /// <param name="request">The join request.</param>
    /// <param name="titleId">The game's id, which keys the protocol.</param>
    /// <param name="logger">Where the session's progress is told.</param>
    /// <param name="identity">Who the player is; <see langword="null"/> takes the game's own (<see cref="OpenIGI.Net.Identity.PlayerIdentities.Default"/>).</param>
    /// <param name="reopen">
    /// Opens a new line to the same server -- the room again by its code, or a new socket -- so a session whose line went
    /// quiet joins again by itself and gets its own player back; <see langword="null"/> ends such a session instead.
    /// </param>
    public SessionClient(
        INetTransport transport,
        NetAddress server,
        JoinRequest request,
        string titleId,
        IOpenIgiLogger logger,
        OpenIGI.Net.Identity.IPlayerIdentity? identity = null,
        Func<INetTransport>? reopen = null)
    {
        _identity = identity;
        _reopen = reopen;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _server = server;
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _titleId = titleId ?? throw new ArgumentNullException(nameof(titleId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string? RoomCode => Room?.Code;

    /// <inheritdoc/>
    public string Place => _session?.Map is { Length: > 0 } map ? HostedSession.MapName(map) : string.Empty;

    /// <inheritdoc/>
    /// <remarks>A fallen player's health byte carries its revival, in the snapshots the server sends.</remarks>
    public float? OwnRevive =>
        Game.Multiplayer.Modes.PlayerSides.Revivable(Mode) && _session is { EntityId: { } self, Latest: { } latest } && latest.TryGet(self, out ReplicatedHuman own) && (own.Flags & ReplicatedHumanCondition.Alive) == 0
            ? own.Health / 255f
            : null;

    /// <summary>Gets the room joined through, when the session is one.</summary>
    public RoomTransport? Room => _transport as RoomTransport;

    /// <inheritdoc/>
    public bool Relayed => Room?.Relayed == true;

    /// <inheritdoc/>
    /// <remarks>Poor: a round trip over a quarter of a second, or one packet in twenty lost.</remarks>
    public bool PoorConnection => _netClient?.Connection?.Statistics is { } stats && (stats.RoundTripTime > 0.25 || stats.PacketLoss > 0.05);

    /// <summary>Gets the session once connecting has started, or <see langword="null"/>.</summary>
    public ClientSession? Session => _session;

    /// <summary>Gets this client's copy of the level as the session drives it, once there is one.</summary>
    public IReplicatedClientWorld? World => _world;

    /// <inheritdoc/>
    public Scoreboard? CurrentScoreboard => _session?.Scoreboard;

    /// <inheritdoc/>
    public int? LocalEntity => _session?.EntityId;

    /// <inheritdoc/>
    public string Title => _session?.Map is { Length: > 0 } map ? $"{Where}  ·  {HostedSession.MapName(map)}" : Where;

    // The server as a player knows it: a room by its code, otherwise by its address.
    private string Where => _transport is RoomTransport { Code: { } code } ? $"Room {code}" : _server.ToString();

    /// <inheritdoc/>
    public void BeforeTitleTick(object title)
    {
    }

    /// <inheritdoc/>
    public void AfterTitleTick(object title)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (!Attach(title, now))
        {
            return;
        }

        ClientReplication.Tick(_session!, _world!, now);
        if (!_welcomed && _session!.EntityId is { } id)
        {
            _welcomed = true;

            // Brought up as a co-op level for a versus server, or the other way: loaded again, the server's way.
            if (_levelMode is { } loadedAs
                && Game.Multiplayer.Modes.ArenaLevel.IsVersus(loadedAs) != Game.Multiplayer.Modes.ArenaLevel.IsVersus(Mode))
            {
                _logger.Information(LogCategory, $"The level was brought up for {loadedAs}; {Where} plays {Mode}, so it is loaded again.");
                _session.ReloadMap();
            }

            _lostAt = null;
            _logger.Information(LogCategory, $"Joined {Where}: map {_session.Map}, {_session.TickRate} ticks a second, playing as entity {id}.");
        }

        if (_session!.EndReason is { } reason && reason != _ended && !Rejoin(reason, now))
        {
            _ended = reason;
            _logger.Warning(LogCategory, $"The session with {Where} ended: {reason}.");
        }

        _ticksSinceDiagnosed++;
        Diagnose(now);
    }

    // Every few seconds, this client's connection as it sees it: what came in, how late, and how its own commands fare.
    private void Diagnose(double now)
    {
        double seconds = now - _diagnosedAt;
        if (_session is not { } session || seconds < NetDiagnostics.IntervalSeconds)
        {
            return;
        }

        NetConnection? connection = _netClient?.Connection;
        NetStatistics? stats = connection?.Statistics;
        long sent = session.SentCommands.Count > 0 ? session.SentCommands[^1].Tick : long.MinValue;
        string age = session.Clock is { HasSample: true } clock && session.Latest is { } latest
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{clock.ServerTick(now) - latest.Tick:F1}")
            : "-";
        string ahead = sent != long.MinValue && session.LastRunCommandTick != long.MinValue
            ? (sent - session.LastRunCommandTick).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "-";
        _logger.Information(NetDiagnostics.LogCategory, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"client ({_request.PlayerName}): {_ticksSinceDiagnosed / seconds:F1} ticks/s {_pumpsSinceDiagnosed / seconds:F1} pumps/s | " +
            $"rtt {stats?.RoundTripTime * 1000:F0} ms jitter {stats?.RoundTripJitter * 1000:F0} ms loss {stats?.PacketLoss * 100:F1}% " +
            $"resends {stats?.ReliableResends} unacked {connection?.PendingReliableCount} backlog {connection?.Backlog} B | " +
            $"out {stats?.BytesSentPerSecond / 1024:F1} KiB/s in {stats?.BytesReceivedPerSecond / 1024:F1} KiB/s | " +
            $"snapshots {(session.SnapshotsAssembled - _snapshotsAt) / seconds:F1}/s parts {(session.SnapshotPartsReceived - _partsAt) / seconds:F1}/s " +
            $"{(session.SnapshotBytesReceived - _snapshotBytesAt) / seconds / 1024:F1} KiB/s unreadable {session.DroppedSnapshots - _droppedSnapshotsAt} | " +
            $"newest snapshot {age} ticks old, commands ahead {ahead}, server queue {session.CommandLead}, packets held {(session.CommandPacketsHeld - _heldAt) / seconds:F1}/s | " +
            $"corrections {session.Reconciler.Corrections - _correctionsAt} last error {session.Reconciler.LastError:F0} | " +
            $"{(connection is null ? "-" : NetDiagnostics.Route(_transport, connection.RemoteAddress))}"));

        _diagnosedAt = now;
        _ticksSinceDiagnosed = 0;
        _pumpsSinceDiagnosed = 0;
        _partsAt = session.SnapshotPartsReceived;
        _snapshotsAt = session.SnapshotsAssembled;
        _snapshotBytesAt = session.SnapshotBytesReceived;
        _correctionsAt = session.Reconciler.Corrections;
        _droppedSnapshotsAt = session.DroppedSnapshots;
        _heldAt = session.CommandPacketsHeld;
    }

    // The session, as far as it can be brought up this call: the room's channel opened, the session made on the first level
    // up here, and each later level followed. False while there is no session to run yet.
    private bool Attach(object? title, double now)
    {
        // Through a room, the handshake waits for the channel to the host: until it opens there is nowhere to send it.
        if (_session is null && _transport is RoomTransport { Status: not RoomStatus.Open } room)
        {
            room.Poll();
            if (room.Status == RoomStatus.Closed && _ended is null && !Rejoin(NetClient.TimedOutReason, now))
            {
                _ended = room.Error ?? "the room closed";
                _logger.Warning(LogCategory, $"Could not join {room}: {_ended}.");
            }

            return false;
        }

        if (_session is null && now < _retryAt)
        {
            return false;
        }

        if (_session is null)
        {
            if (title is not IReplicatedTitle { Level: not null } replicated)
            {
                return false;
            }

            _world = replicated.CreateClientWorld();
            _world.MarkLevelStart();
            _level = replicated.Level;
            _levelMode = Mode;
            _netClient = new NetClient(_transport, MultiplayerProtocol.IdFor(_titleId), MultiplayerProtocol.Version);
            _session = new ClientSession(_netClient, _world.LocalFrame.Length, _identity, MultiplayerProtocol.IdFor(_titleId));
            _session.Connect(_server, _request, now);
            _logger.Information(LogCategory, $"Joining {Where} as {_request.PlayerName}.");
        }
        else if (title is not null)
        {
            FollowLevel(title);
        }

        return true;
    }

    /// <inheritdoc/>
    public string? TakeTravel() => _session?.TakeTravel();

    /// <inheritdoc/>
    public string OwnSkin => _request.Skin;

    // The longest a loaded level waits for the server's word: past this the server is taken to have gone on without it.
    private const double LongestWaitSeconds = 45.0;

    // When the level up here began waiting for the server -- to be joined, told its state, or told to begin -- or null when
    // it isn't waiting.
    private double? _heldSince;

    /// <inheritdoc/>
    /// <remarks>
    /// A level is held behind the loading card until it can be played as the server's: the session joined and the server's
    /// level state taken, so its scenes run as the server's run -- a late joiner never sees the intro the host has passed
    /// -- and, going on to a level the server announced, the server's word to begin it. Past <see cref="LongestWaitSeconds"/>,
    /// or once the session is over, it is let go.
    /// </remarks>
    public bool HoldsLevel =>
        _ended is null && Waiting && (_heldSince is not { } since || _clock.Elapsed.TotalSeconds - since < LongestWaitSeconds);

    // Whether the level up here still wants something of the server before it can be played as the server's.
    private bool Waiting =>
        _session is not { EntityId: not null, EndReason: null } session || !session.LevelTaken || session.WaitingForServer;

    /// <inheritdoc/>
    public void Pump(object? title)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (title is IReplicatedTitle { Level: not null })
        {
            _heldSince ??= now;
        }

        if (!Attach(title, now) || _session is not { } session)
        {
            return;
        }

        session.Receive(now);
        _pumpsSinceDiagnosed++;

        // The server's level state, taken while the level is held, so its first tick is already the server's: nothing of a
        // level the server has left is taken for the one here.
        if (!session.Behind && session.TakeLevel() is { } level)
        {
            _world!.ApplyLevel(level);
        }

        if (!Waiting)
        {
            _heldSince = null;
        }

        if (session.EndReason is { } reason && reason != _ended && !Rejoin(reason, now))
        {
            _ended = reason;
            _logger.Warning(LogCategory, $"The session with {Where} ended: {reason}.");
            return;
        }

        _session?.Flush(now);
    }

    // A new level up -- gone to, or loaded on the server's announcement and held: this one's players and state are shown on
    // it from now, and the server is told it is here.
    private void FollowLevel(object title)
    {
        if (_session is null || title is not IReplicatedTitle { Level: { } level } next || ReferenceEquals(level, _level))
        {
            return;
        }

        _world = next.CreateClientWorld();
        _world.MarkLevelStart();
        _level = level;
        _levelMode = Mode;
        _session.Arrived();
        _heldSince = _clock.Elapsed.TotalSeconds;
        _logger.Information(LogCategory, $"Following {Where} to {_session.Map}.");
        if (_session.WaitingForServer)
        {
            _session.ReportReady();
        }
    }

    /// <inheritdoc/>
    public string? Ended => _ended;

    // Whether the session was ended.
    private bool _disposed;

    /// <inheritdoc/>
    /// <remarks>The server's own once welcomed; until then the mode of the room or server joined as it was listed.</remarks>
    public string Mode
    {
        get => _session?.ServerMode is { Length: > 0 } told ? told : _listedMode;
        init => _listedMode = value ?? string.Empty;
    }

    private readonly string _listedMode = string.Empty;

    // The mode the level here was brought up under, which says whether the level's soldiers were taken out of it.
    private string? _levelMode;

    /// <inheritdoc/>
    public IReadOnlyList<Game.Multiplayer.Modes.KillNote> KillFeed => _session?.Feed.Shown(_clock.Elapsed.TotalSeconds) ?? [];

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.MatchBanner? Banner =>
        _session?.MatchState is { } state ? state.Banner(_clock.Elapsed.TotalSeconds - _session.MatchStateAt) : null;

    /// <inheritdoc/>
    public void Poll()
    {
        _session?.Poll(_clock.Elapsed.TotalSeconds);
        foreach (string line in _session?.TakeRconAnswers() ?? [])
        {
            _logger.Information("Rcon", line);
        }
    }

    /// <summary>Sends a command to the server's remote console; its answer is told in the log as it comes.</summary>
    /// <param name="password">The server's remote console password.</param>
    /// <param name="command">The command.</param>
    /// <returns><see langword="true"/> when there is a server to send it to.</returns>
    public bool SendRcon(string password, string command)
    {
        if (_session is null)
        {
            return false;
        }

        _session.SendRcon(password, command);
        return true;
    }

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.SpectateTarget? Spectate(int pick) =>
        _session is { EntityId: { } own, Latest: { } latest } session && Game.Multiplayer.Modes.ArenaLevel.IsVersus(Mode)
            ? Game.Multiplayer.Modes.Spectating.Pick(
                session.Scoreboard,
                own,
                Game.Multiplayer.Modes.GameModes.HasTeams(Mode),
                entity => latest.TryGet(entity, out Game.Multiplayer.ReplicatedHuman human)
                    ? new Core.Numerics.Vector3d(human.Position.X, human.Position.Y, human.Position.Z)
                    : null,
                pick)
            : null;

    /// <inheritdoc/>
    public IReadOnlyList<string> NetGraph
    {
        get
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (_session is not { } session || _netClient?.Connection is not { } connection)
            {
                return [];
            }

            // The rates over the last second or so, sampled as they are asked for.
            if (now - _graphAt >= 1)
            {
                double seconds = now - _graphAt;
                _graphSnapshots = (session.SnapshotsAssembled - _graphSnapshotsAt) / seconds;
                _graphCorrections = (session.Reconciler.Corrections - _graphCorrectionsAt) / seconds;
                _graphAt = now;
                _graphSnapshotsAt = session.SnapshotsAssembled;
                _graphCorrectionsAt = session.Reconciler.Corrections;
            }

            NetStatistics stats = connection.Statistics;
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            return
            [
                string.Create(invariant, $"PING {stats.RoundTripTime * 1000:0} ms   JITTER {stats.RoundTripJitter * 1000:0} ms   LOSS {stats.PacketLoss * 100:0.0}%"),
                string.Create(invariant, $"IN {stats.BytesReceivedPerSecond / 1024:0.0} KiB/s   OUT {stats.BytesSentPerSecond / 1024:0.0} KiB/s   CHOKE {stats.Choked}"),
                string.Create(invariant, $"SNAPSHOTS {_graphSnapshots:0}/s   INTERP {(session.Clock?.AppliedInterpolationDelay ?? 0) * 1000:0} ms"),
                string.Create(invariant, $"CORRECTIONS {_graphCorrections:0.0}/s   LAST {session.Reconciler.LastError:0}   {(Relayed ? "RELAYED" : "DIRECT")}"),
            ];
        }
    }

    private double _graphAt;
    private double _graphSnapshots;
    private double _graphCorrections;
    private long _graphSnapshotsAt;
    private long _graphCorrectionsAt;

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.CombatHud? Combat(Core.Numerics.Vector3d eye, Core.Numerics.Matrix3f view)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_session is not { } session)
        {
            return null;
        }

        _combat.Take(session.TakeCombat(), session.EntityId ?? -1, now);
        return _combat.Shown(now, eye, view, session.MatchState);
    }

    private readonly Game.Multiplayer.Modes.CombatFeedback _combat = new();

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.MatchHud? MatchHud(Core.Numerics.Vector3d? ownPosition) =>
        _session?.MatchState is { } state
            ? Game.Multiplayer.Modes.MatchHud.Of(
                state,
                _clock.Elapsed.TotalSeconds - _session.MatchStateAt,
                CurrentScoreboard,
                LocalEntity,
                ownPosition,
                Layout,
                _session.UseHeldSince is { } since
                    ? new Game.Multiplayer.Modes.LocalUse(true, _clock.Elapsed.TotalSeconds - since)
                    : new Game.Multiplayer.Modes.LocalUse(false, 0))
            : null;

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.ModeReport? ModeReport => _session?.MatchState?.Report;

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.MpLayout? Layout { get; set; }

    /// <summary>Gets a value indicating whether the line went quiet and the session is joining again.</summary>
    public bool IsRejoining => _lostAt is not null && _ended is null;

    // How long a session whose line went quiet keeps trying to join again: the server keeps its player about that long.
    private const double RejoinSeconds = 60.0;

    // A session that was joined and whose line went quiet -- the host's room still there, a socket that died with the Wi-Fi
    // -- joins again on a new line, backing off, until RejoinSeconds have passed: the server knows it by its key and gives it
    // its own player back where it was. Anything said on purpose (kicked, full, the room closed, a version) is the end.
    private bool Rejoin(string reason, double now)
    {
        if (_reopen is null || reason != NetClient.TimedOutReason || (!_welcomed && _lostAt is null))
        {
            return false;
        }

        _lostAt ??= now;
        if (now - _lostAt.Value > RejoinSeconds)
        {
            return false;
        }

        double tried = now - _lostAt.Value;
        _logger.Warning(LogCategory, $"Lost {Where}; joining again ({tried:F0} s since the line went quiet).");
        if (_netClient is { } old)
        {
            old.Dispose();
        }
        else
        {
            _transport.Dispose();
        }

        _netClient = null;
        _session = null;
        _welcomed = false;
        _level = null;
        _transport = _reopen();
        _retryAt = now + Math.Min(8, 1 + tried / 4);
        return true;
    }

    /// <inheritdoc/>
    public void Say(string text) => _session?.Say(text);

    /// <inheritdoc/>
    public void Mark(Int3 position) => _session?.Mark(position);

    /// <inheritdoc/>
    public IReadOnlyList<ChatLine> TakeChat() => _session?.TakeChat() ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<WorldMark> TakeMarks() => _session?.TakeMarks() ?? [];

    /// <inheritdoc/>
    public void Speak(ReadOnlySpan<byte> packet) => _session?.Speak(packet);

    /// <inheritdoc/>
    public IReadOnlyList<VoicePacket> TakeVoices() => _session?.TakeVoices() ?? [];

    /// <inheritdoc/>
    public Int3? WhereIs(int entity) => _session?.Latest is { } last && last.TryGet(entity, out ReplicatedHuman human) ? human.Position : null;

    /// <inheritdoc/>
    public void Dispose()
    {
        // Ended once: by leaving the mission, and again by the program that made it as it closes.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_netClient is { } client)
        {
            client.Dispose();
        }
        else
        {
            _transport.Dispose();
        }
    }
}
