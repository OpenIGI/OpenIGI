using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Server;

/// <summary>The session side of the dedicated server: who is connected, who may join, and what the server list is told.</summary>
/// <remarks>
/// The level itself is the host's, run headless beside this. The server pumps this once per step of the host, so what it
/// reads and sends keeps the host's time.
/// </remarks>
internal sealed class DedicatedServer : IDisposable
{
    private const string LogCategory = "Server";

    private readonly NetServer _net;
    private readonly ServerOptions _options;
    private readonly string _titleId;
    private readonly string _map;
    private readonly string _version;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<int, string> _names = [];

    public DedicatedServer(INetTransport transport, ServerOptions options, string titleId, string map, string version, IOpenIgiLogger logger)
    {
        _options = options;
        _titleId = titleId;
        _map = map;
        _version = version;
        _logger = logger;
        _net = new NetServer(transport, new NetServerOptions
        {
            ProtocolId = MultiplayerProtocol.IdFor(titleId),
            MaxClients = options.MaxPlayers,
            Admit = Admit,
            Info = _ => Info().Encode(),
        });
    }

    /// <summary>Gets the address the server listens on.</summary>
    public NetAddress LocalAddress => _net.LocalAddress;

    /// <summary>Gets the names of the connected players, by slot.</summary>
    public IReadOnlyDictionary<int, string> Players => _names;

    /// <summary>Reads what arrived, admits and drops players, and sends what is due.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Pump(double now)
    {
        _net.Update(now);
        while (_net.TryReceive(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    string name = JoinRequest.Decode(e.Payload)?.PlayerName ?? "player";
                    _names[e.Peer] = name;
                    _logger.Information(LogCategory, $"{name} joined from {_net.GetConnection(e.Peer)?.RemoteAddress} (slot {e.Peer}, {_names.Count}/{_options.MaxPlayers}).");
                    break;
                case NetEventKind.Disconnected:
                    _names.Remove(e.Peer, out string? left);
                    _logger.Information(LogCategory, $"{left ?? "a player"} left: {e.Reason}.");
                    break;
            }
        }

        _net.Flush(now);
    }

    /// <summary>Gets the server list's line for this server now.</summary>
    /// <returns>The line.</returns>
    public ServerInfo Info() =>
        new(_titleId, _options.Name, _map, _options.Mode, _names.Count, _options.MaxPlayers, _options.Password.Length > 0, _version);

    /// <inheritdoc/>
    public void Dispose() => _net.Dispose();

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

        if (string.IsNullOrWhiteSpace(join.PlayerName))
        {
            return NetAdmission.Reject("a player needs a name");
        }

        return NetAdmission.Accept(Encoding.UTF8.GetBytes(_map));
    }
}
