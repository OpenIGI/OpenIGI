using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <summary>How a <see cref="HostedSession"/> runs: what it shows the server list and who may join.</summary>
/// <param name="Name">The name it shows in the server list.</param>
/// <param name="MaxPlayers">How many clients may be connected at once.</param>
/// <param name="Password">The password joining takes, or empty for none.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Map">The map.</param>
/// <param name="Version">The build it runs.</param>
/// <param name="IncludesHost">Whether the host's own player plays too: a listen server rather than a dedicated one.</param>
/// <param name="HostName">The name the host's own player plays under, when it plays.</param>
/// <param name="Content">The host's <see cref="JoinRequest.Content"/> fingerprint, or empty to take any.</param>
public sealed record HostedSessionOptions(
    string Name, int MaxPlayers, string Password, string Mode, string Map, string Version, bool IncludesHost, string HostName = "Jones", string Content = "");

/// <summary>A server's session run inside the host: a dedicated server's, or a listen server's beside the host's own player.</summary>
/// <remarks>
/// The level is the host's. Once its title's player is ticking, the session is made over it; from then on it reads before
/// each tick and sends after it. A client trying to join while the level still loads keeps retrying its handshake until the
/// session starts reading.
/// </remarks>
public sealed class HostedSession : IHostTickObserver, ISessionScoreboard, IDisposable
{
    private const string LogCategory = "Session";

    private readonly NetServer _net;
    private readonly RoomTransport? _room;
    private bool _announced;
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
        _room = transport as RoomTransport;
        _net = new NetServer(transport, new NetServerOptions
        {
            ProtocolId = MultiplayerProtocol.IdFor(titleId),
            MaxClients = options.MaxPlayers,
            Admit = Admit,
            Info = _ => Info().Encode(),
        });
    }

    /// <summary>Gets the room's code, when the session is hosted through one and the room is open.</summary>
    public string? RoomCode => _room?.Code;

    /// <summary>Gets the address the session listens on.</summary>
    public NetAddress LocalAddress => _net.LocalAddress;

    /// <summary>Gets the session once the level is up, or <see langword="null"/>.</summary>
    public ServerSession? Session => _session;

    /// <inheritdoc/>
    public Scoreboard? CurrentScoreboard => _session?.CurrentScoreboard();

    /// <inheritdoc/>
    public int? LocalEntity => _options.IncludesHost ? ServerSession.HostEntity : null;

    /// <inheritdoc/>
    public string Title => _room?.Code is { } code ? $"Room {code}  ·  {MapName(_options.Map)}" : $"{_options.Name}  ·  {MapName(_options.Map)}";

    /// <inheritdoc/>
    public string Place => MapName(_options.Map);

    /// <inheritdoc/>
    public float? OwnRevive => _options.IncludesHost ? _world?.ReviveProgressOf(ServerSession.HostEntity) : null;

    /// <summary>A map as a player reads it: a mission by its number, anything else as it is.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The name.</returns>
    internal static string MapName(string map) =>
        int.TryParse(map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mission)
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Mission {mission}")
            : map;

    /// <inheritdoc/>
    public void BeforeTitleTick(object title)
    {
        if (_session is null)
        {
            if (title is not IReplicatedTitle replicated)
            {
                return;
            }

            _world = replicated.CreateReplicatedWorld(_options.IncludesHost);
            _session = new ServerSession(_net, _world, _tickRate, _options.Map, hostName: _options.IncludesHost ? _options.HostName : null);
            _logger.Information(
                LogCategory,
                _room is null ? $"The level is up; players may join on port {_net.LocalAddress.Port}." : "The level is up; players may join through the room.");
        }

        if (_room is { Code: { } code } && !_announced)
        {
            _announced = true;
            _logger.Information(LogCategory, $"Room code {code}: friends join with it, on the desktop or in the browser.");
        }

        _session.Receive(_clock.Elapsed.TotalSeconds, _world!.CurrentTick);
        ReportJoinsAndDepartures();
    }

    /// <inheritdoc/>
    public void AfterTitleTick(object title) => _session?.Send(_clock.Elapsed.TotalSeconds, _world!.CurrentTick - 1);

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

        if (!_known.SetEquals(now))
        {
            _room?.ReportState(now.Count + (_options.IncludesHost ? 1 : 0), map: null);
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

        if (_options.Content.Length > 0 && join.Content.Length > 0 && !string.Equals(join.Content, _options.Content, StringComparison.Ordinal))
        {
            return NetAdmission.Reject("your game plays with different mods from the host's");
        }

        if (_options.Password.Length > 0 && !string.Equals(join.Password, _options.Password, StringComparison.Ordinal))
        {
            return NetAdmission.Reject("wrong password");
        }

        return string.IsNullOrWhiteSpace(join.PlayerName) ? NetAdmission.Reject("a player needs a name") : NetAdmission.Accept();
    }
}
