using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Host.Multiplayer;

/// <summary>How a <see cref="HostedSession"/> runs: what it shows the server list and who may join.</summary>
/// <param name="Name">The name it shows in the server list.</param>
/// <param name="MaxPlayers">How many clients may be connected at once.</param>
/// <param name="Password">The password joining takes, or empty for none.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Map">The map.</param>
/// <param name="Version">The build it runs.</param>
/// <param name="IncludesHost">Whether the host's own player plays too: a listen server rather than a dedicated one.</param>
public sealed record HostedSessionOptions(
    string Name, int MaxPlayers, string Password, string Mode, string Map, string Version, bool IncludesHost);

/// <summary>A server's session run inside the host: a dedicated server's, or a listen server's beside the host's own player.</summary>
/// <remarks>
/// The level is the host's. Once its title's player is ticking, the session is made over it; from then on it reads before
/// each tick and sends after it. A client trying to join while the level still loads keeps retrying its handshake until the
/// session starts reading.
/// </remarks>
public sealed class HostedSession : IHostTickObserver, IDisposable
{
    private const string LogCategory = "Session";

    private readonly NetServer _net;
    private readonly HostedSessionOptions _options;
    private readonly string _titleId;
    private readonly int _tickRate;
    private readonly IOpenIgiLogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<int> _known = [];
    private IReplicatedWorld? _world;
    private ServerSession? _session;

    /// <summary>Initializes a new instance of the <see cref="HostedSession"/> class.</summary>
    /// <param name="transport">What it listens on; the session disposes it.</param>
    /// <param name="options">How it runs.</param>
    /// <param name="titleId">The game's id, which keys the protocol.</param>
    /// <param name="tickRate">The level's ticks per second.</param>
    /// <param name="logger">Where joins and departures are told.</param>
    public HostedSession(INetTransport transport, HostedSessionOptions options, string titleId, int tickRate, IOpenIgiLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _titleId = titleId ?? throw new ArgumentNullException(nameof(titleId));
        _tickRate = tickRate;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _net = new NetServer(transport, new NetServerOptions
        {
            ProtocolId = MultiplayerProtocol.IdFor(titleId),
            MaxClients = options.MaxPlayers,
            Admit = Admit,
            Info = _ => Info().Encode(),
        });
    }

    /// <summary>Gets the address the session listens on.</summary>
    public NetAddress LocalAddress => _net.LocalAddress;

    /// <summary>Gets the session once the level is up, or <see langword="null"/>.</summary>
    public ServerSession? Session => _session;

    /// <inheritdoc/>
    public void BeforeTitleTick(ITitlePlayer player)
    {
        if (_session is null)
        {
            if (player is not IReplicatedTitle title)
            {
                return;
            }

            _world = title.CreateReplicatedWorld(_options.IncludesHost);
            _session = new ServerSession(_net, _world, _tickRate, _options.Map, hostName: _options.IncludesHost ? "host" : null);
            _logger.Information(LogCategory, $"The level is up; players may join on port {_net.LocalAddress.Port}.");
        }

        _session.Receive(_clock.Elapsed.TotalSeconds, _world!.CurrentTick);
        ReportJoinsAndDepartures();
    }

    /// <inheritdoc/>
    public void AfterTitleTick(ITitlePlayer player) => _session?.Send(_clock.Elapsed.TotalSeconds, _world!.CurrentTick - 1);

    /// <summary>Gets the server list's line for this server now.</summary>
    /// <returns>The line.</returns>
    public ServerInfo Info() =>
        new(_titleId, _options.Name, _options.Map, _options.Mode, _known.Count + (_options.IncludesHost ? 1 : 0),
            _options.MaxPlayers, _options.Password.Length > 0, _options.Version);

    /// <inheritdoc/>
    public void Dispose() => _net.Dispose();

    private void ReportJoinsAndDepartures()
    {
        HashSet<int> now = [.. _session!.Clients];
        foreach (int slot in now.Except(_known))
        {
            _logger.Information(LogCategory, $"{_session.NameOf(slot)} joined from {_net.GetConnection(slot)?.RemoteAddress} (slot {slot}).");
        }

        foreach (int slot in _known.Except(now))
        {
            _logger.Information(LogCategory, $"The player in slot {slot} left.");
        }

        _known.Clear();
        _known.UnionWith(now);
    }

    private NetAdmission Admit(NetConnectRequest request)
    {
        if (JoinRequest.Decode(request.Payload) is not { } join)
        {
            return NetAdmission.Reject("this client speaks another version of the protocol");
        }

        if (_options.Password.Length > 0 && !string.Equals(join.Password, _options.Password, StringComparison.Ordinal))
        {
            return NetAdmission.Reject("wrong password");
        }

        return string.IsNullOrWhiteSpace(join.PlayerName) ? NetAdmission.Reject("a player needs a name") : NetAdmission.Accept();
    }
}
