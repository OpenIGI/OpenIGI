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
public sealed class SessionClient : IHostTickObserver, ISessionScoreboard, ISessionFollower, IDisposable
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

        // Through a room, the handshake waits for the channel to the host: until it opens there is nowhere to send it.
        if (_session is null && _transport is RoomTransport { Status: not RoomStatus.Open } room)
        {
            room.Poll();
            if (room.Status == RoomStatus.Closed && _ended is null)
            {
                _ended = room.Error ?? "the room closed";
                _logger.Warning(LogCategory, $"Could not join {room}: {_ended}.");
            }

            return;
        }

        if (_session is null)
        {
            if (title is not IReplicatedTitle replicated)
            {
                return;
            }

            _world = replicated.CreateClientWorld();
            _level = replicated.Level;
            _netClient = new NetClient(_transport, MultiplayerProtocol.IdFor(_titleId));
            _session = new ClientSession(_netClient, _world.LocalFrame.Length);
            _session.Connect(_server, _request, now);
            _logger.Information(LogCategory, $"Joining {Where} as {_request.PlayerName}.");
        }

        else if (title is IReplicatedTitle next && !ReferenceEquals(next.Level, _level))
        {
            // The level the server travelled to is up: this one's players and state are shown on it from now.
            _world = next.CreateClientWorld();
            _level = next.Level;
            _logger.Information(LogCategory, $"Following {Where} to {_session.Map}.");
        }

        ClientReplication.Tick(_session, _world!, now);
        if (!_welcomed && _session.EntityId is { } id)
        {
            _welcomed = true;
            _logger.Information(LogCategory, $"Joined {Where}: map {_session.Map}, {_session.TickRate} ticks a second, playing as entity {id}.");
        }

        if (_session.EndReason is { } reason && reason != _ended)
        {
            _ended = reason;
            _logger.Warning(LogCategory, $"The session with {Where} ended: {reason}.");
        }
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public string? TakeTravel() => _session?.TakeTravel();

    /// <inheritdoc/>
    public string? Ended => _ended;

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
