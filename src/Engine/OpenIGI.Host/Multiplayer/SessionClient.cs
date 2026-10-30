using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Host.Multiplayer;

/// <summary>A client's session run inside the host: its own player played as usual, the others shown as the server has them.</summary>
/// <remarks>
/// The host plays the same map locally. Once its title's player is ticking, the session connects; after each tick it sends
/// that tick's command and shows every other player where the snapshots put it.
/// </remarks>
public sealed class SessionClient : IHostTickObserver, IDisposable
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

    /// <summary>Gets the session once connecting has started, or <see langword="null"/>.</summary>
    public ClientSession? Session => _session;

    /// <inheritdoc/>
    public void BeforeTitleTick(ITitlePlayer player)
    {
    }

    /// <inheritdoc/>
    public void AfterTitleTick(ITitlePlayer player)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_session is null)
        {
            if (player is not IReplicatedTitle title)
            {
                return;
            }

            _world = title.CreateClientWorld();
            _netClient = new NetClient(_transport, MultiplayerProtocol.IdFor(_titleId));
            _session = new ClientSession(_netClient, _world.LocalFrame.Length);
            _session.Connect(_server, _request, now);
            _logger.Information(LogCategory, $"Joining {_server} as {_request.PlayerName}.");
        }

        ClientReplication.Tick(_session, _world!, now);
        if (!_welcomed && _session.EntityId is { } id)
        {
            _welcomed = true;
            _logger.Information(LogCategory, $"Joined {_server}: map {_session.Map}, {_session.TickRate} ticks a second, playing as entity {id}.");
        }

        if (_session.EndReason is { } reason && reason != _ended)
        {
            _ended = reason;
            _logger.Warning(LogCategory, $"The session with {_server} ended: {reason}.");
        }
    }

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
