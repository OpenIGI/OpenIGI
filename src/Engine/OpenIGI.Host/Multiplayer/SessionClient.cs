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
public sealed class SessionClient : IHostTickObserver, ISessionScoreboard, ISessionFollower, ISessionTalk, ISessionLook, ISessionHold, IDisposable
{
    private const string LogCategory = "Session";

    private readonly INetTransport _transport;
    private readonly NetAddress _server;
    private readonly JoinRequest _request;
    private readonly string _titleId;
    private readonly IOpenIgiLogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IReplicatedClientWorld? _world;
    private NetClient? _netClient;
    private ClientSession? _session;
    private bool _welcomed;
    // The level the session was last bound to.
    private object? _level;
    private string? _ended;

    /// <summary>Initializes a new instance of the <see cref="SessionClient"/> class.</summary>
    /// <param name="transport">What it sends on; the client disposes it.</param>
    /// <param name="server">The server to join.</param>
    /// <param name="request">The join request.</param>
    /// <param name="titleId">The game's id, which keys the protocol.</param>
    /// <param name="logger">Where the session's progress is told.</param>
    public SessionClient(INetTransport transport, NetAddress server, JoinRequest request, string titleId, IOpenIgiLogger logger)
    {
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
        _session is { EntityId: { } self, Latest: { } latest } && latest.TryGet(self, out ReplicatedHuman own) && (own.Flags & ReplicatedHumanCondition.Alive) == 0
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
            _logger.Information(LogCategory, $"Joined {Where}: map {_session.Map}, {_session.TickRate} ticks a second, playing as entity {id}.");
        }

        if (_session!.EndReason is { } reason && reason != _ended)
        {
            _ended = reason;
            _logger.Warning(LogCategory, $"The session with {Where} ended: {reason}.");
        }
    }

    // The session, as far as it can be brought up this call: the room's channel opened, the session made on the first level
    // up here, and each later level followed. False while there is no session to run yet.
    private bool Attach(object? title, double now)
    {
        // Through a room, the handshake waits for the channel to the host: until it opens there is nowhere to send it.
        if (_session is null && _transport is RoomTransport { Status: not RoomStatus.Open } room)
        {
            room.Poll();
            if (room.Status == RoomStatus.Closed && _ended is null)
            {
                _ended = room.Error ?? "the room closed";
                _logger.Warning(LogCategory, $"Could not join {room}: {_ended}.");
            }

            return false;
        }

        if (_session is null)
        {
            if (title is not IReplicatedTitle { Level: not null } replicated)
            {
                return false;
            }

            _world = replicated.CreateClientWorld();
            _level = replicated.Level;
            _netClient = new NetClient(_transport, MultiplayerProtocol.IdFor(_titleId));
            _session = new ClientSession(_netClient, _world.LocalFrame.Length);
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

        if (session.EndReason is { } reason && reason != _ended)
        {
            _ended = reason;
            _logger.Warning(LogCategory, $"The session with {Where} ended: {reason}.");
        }

        session.Flush(now);
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
        _level = level;
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
