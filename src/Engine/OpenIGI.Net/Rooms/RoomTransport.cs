using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Net.Rooms;

/// <summary>What a host asks the master server for when it opens a room.</summary>
/// <param name="Title">The game's id.</param>
/// <param name="Protocol">The protocol id, as hex: only a game speaking the same one can play.</param>
/// <param name="Name">The room's name in the list.</param>
/// <param name="Map">The map or mission.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Version">The build.</param>
/// <param name="MaxPlayers">How many may play, the host included.</param>
/// <param name="Public">Whether it is listed, or reached by its code alone.</param>
/// <param name="Passworded">Whether joining takes a password.</param>
public sealed record RoomRequest(
    string Title, string Protocol, string Name, string Map, string Mode, string Version, int MaxPlayers, bool Public, bool Passworded);

/// <summary>Where a <see cref="RoomTransport"/> is.</summary>
public enum RoomStatus
{
    /// <summary>Opening the room, or reaching its host.</summary>
    Connecting,

    /// <summary>Hosting: the room is open to joiners. Joining: the channel to the host is open.</summary>
    Open,

    /// <summary>The room is gone, or never could be reached; <see cref="RoomTransport.Error"/> says why.</summary>
    Closed,
}

/// <summary>
/// The game's datagrams to and from other players met through a room: a WebRTC data channel to each, set up by way of
/// the master server, so a player behind a router hosts and joins without opening a port, and a browser plays with a
/// desktop as with another browser.
/// </summary>
/// <remarks>
/// <para>
/// The host is <see cref="HostAddress"/> to its joiners, and each joiner is <see cref="NetAddress.Peer"/> of its number to
/// the host; the connection layer above keys on those as it keys on IP addresses. The room only introduces the players:
/// once a channel is open nothing passes through it, and the game runs directly between them, or through the TURN relay
/// when their routers allow nothing better.
/// </para>
/// <para>
/// Usable at once: the room is opened in the background, and datagrams sent to a peer whose channel is not open yet are
/// dropped, as a network drops them. The handshake above resends until the channel opens.
/// </para>
/// </remarks>
public sealed class RoomTransport : INetTransport
{
    /// <summary>The master server rooms are opened on.</summary>
    public static readonly Uri DefaultMaster = new("https://master.openigi.com");

    /// <summary>The host's address, to a joiner.</summary>
    public static readonly NetAddress HostAddress = NetAddress.Peer(0);

    private const string LogCategory = "Room";

    private readonly Uri _master;
    private readonly IRtcPeerFactory _factory;
    private readonly IOpenIgiLogger _logger;
    private readonly bool _hosting;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _inbox = new();
    private readonly ConcurrentQueue<string> _outbox = new();
    private readonly SemaphoreSlim _outboxReady = new(0);
    private readonly Dictionary<int, IRtcPeer> _peers = [];
    private readonly List<int> _order = [];
    private IReadOnlyList<IceServer> _ice = [];
    private ClientWebSocket? _socket;
    private int _next;
    private volatile string? _code;
    private volatile string? _error;
    private volatile RoomStatus _status = RoomStatus.Connecting;
    private bool _disposed;

    private RoomTransport(Uri master, IRtcPeerFactory factory, IOpenIgiLogger logger, bool hosting)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hosting = hosting;
    }

    /// <summary>Gets the room's code, once it has one.</summary>
    public string? Code => _code;

    /// <summary>Gets where the room is.</summary>
    public RoomStatus Status => _status;

    /// <summary>Gets why the room closed, when it did.</summary>
    public string? Error => _error;

    /// <summary>Gets whether this side hosts the room.</summary>
    public bool IsHost => _hosting;

    /// <inheritdoc/>
    public NetAddress LocalAddress => _hosting ? HostAddress : NetAddress.Peer(-1);

    /// <inheritdoc/>
    public int MaxDatagramSize => NetConstants.MaxPacketSize;

    /// <summary>Opens a room and hosts it.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="request">The room.</param>
    /// <param name="factory">Makes the WebRTC connections.</param>
    /// <param name="logger">Where its progress is told.</param>
    /// <returns>The transport, opening the room.</returns>
    public static RoomTransport Host(Uri master, RoomRequest request, IRtcPeerFactory factory, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        RoomTransport transport = new(master, factory, logger, hosting: true);
        transport.Start(transport.HostAsync(request));
        return transport;
    }

    /// <summary>Joins a room by its code.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="code">The room's code; case does not matter.</param>
    /// <param name="factory">Makes the WebRTC connection.</param>
    /// <param name="logger">Where its progress is told.</param>
    /// <returns>The transport, reaching the host.</returns>
    public static RoomTransport Join(Uri master, string code, IRtcPeerFactory factory, IOpenIgiLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        RoomTransport transport = new(master, factory, logger, hosting: false) { _code = code.Trim().ToUpperInvariant() };
        transport.Start(transport.ConnectAsync(new Uri(master, $"/v1/rooms/{Uri.EscapeDataString(transport._code)}/ws?role=join")));
        return transport;
    }

    /// <summary>Tells the room's list entry how many play and on what map. Hosting only.</summary>
    /// <param name="players">How many play, the host included.</param>
    /// <param name="map">The map, or <see langword="null"/> to leave it.</param>
    public void ReportState(int players, string? map)
    {
        if (!_hosting)
        {
            return;
        }

        JsonObject state = new() { ["type"] = "state", ["players"] = players };
        if (map is not null)
        {
            state["map"] = map;
        }

        Post(state);
    }

    /// <inheritdoc/>
    public void Send(NetAddress destination, ReadOnlySpan<byte> datagram)
    {
        if (destination.Kind == NetAddressKind.Peer && _peers.TryGetValue((int)(uint)destination.Host, out IRtcPeer? peer))
        {
            peer.Send(datagram);
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
    {
        Pump();

        // Round the peers, starting after the last one heard from, so one busy peer can't starve the rest.
        for (int i = 0; i < _order.Count; i++)
        {
            int id = _order[(_next + i) % _order.Count];
            if (_peers[id].TryReceive(buffer, out length))
            {
                _next = (_next + i + 1) % _order.Count;
                source = NetAddress.Peer(id);
                return true;
            }
        }

        source = default;
        length = 0;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        foreach (IRtcPeer peer in _peers.Values)
        {
            peer.Dispose();
        }

        _peers.Clear();
        _order.Clear();
        _socket?.Abort();
        _socket?.Dispose();
        _stop.Dispose();
        _outboxReady.Dispose();
    }

    // What the room said, and what each connection has to say back, on the network's thread.
    private void Pump()
    {
        while (_inbox.TryDequeue(out string? text))
        {
            try
            {
                Handle(JsonNode.Parse(text) as JsonObject);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
            {
                _logger.Warning(LogCategory, $"Ignored a room message: {ex.Message}");
            }
        }

        List<int>? gone = null;
        foreach ((int id, IRtcPeer peer) in _peers)
        {
            while (peer.TryTakeSignal(out RtcSignal signal))
            {
                JsonObject data = signal.Kind == "candidate"
                    ? new JsonObject { ["candidate"] = signal.Value, ["mid"] = signal.Mid }
                    : new JsonObject { ["type"] = signal.Kind, ["sdp"] = signal.Value };
                JsonObject message = new() { ["type"] = signal.Kind, ["data"] = data };
                if (_hosting)
                {
                    message["peer"] = id;
                }

                Post(message);
            }

            if (!_hosting && _status == RoomStatus.Connecting && peer.State == RtcPeerState.Open)
            {
                _status = RoomStatus.Open;
                _logger.Information(LogCategory, $"Connected to room {_code}'s host.");
            }

            if (peer.State == RtcPeerState.Closed)
            {
                (gone ??= []).Add(id);
            }
        }

        foreach (int id in gone ?? [])
        {
            string? why = _peers[id].Failure;
            Drop(id);
            if (_hosting)
            {
                _logger.Information(LogCategory, $"Lost player {id} of room {_code}{(why is null ? "." : $": {why}")}");
            }
            else
            {
                Close($"lost the host{(why is null ? string.Empty : $": {why}")}");
            }
        }
    }

    private void Handle(JsonObject? message)
    {
        if (message is null)
        {
            return;
        }

        string type = (string?)message["type"] ?? string.Empty;
        int peer = message["peer"] is JsonValue p ? (int)p : 0;
        JsonObject? data = message["data"] as JsonObject;
        switch (type)
        {
            case "hosting":
                _status = RoomStatus.Open;
                _logger.Information(LogCategory, $"Hosting room {_code}.");
                break;

            case "welcome" when !_hosting:
                _ice = ReadIce(message["ice"] as JsonArray);
                Add(0, _factory.Create(_ice, offerer: true));
                _logger.Information(LogCategory, $"In room {_code} as player {(int?)message["peer"]}; reaching its host.");
                break;

            case "join" when _hosting:
                Add(peer, _factory.Create(_ice, offerer: false));
                break;

            case "leave" when _hosting:
                Drop(peer);
                break;

            case "offer" or "answer" when data is not null:
                if (_peers.TryGetValue(_hosting ? peer : 0, out IRtcPeer? to))
                {
                    to.SetRemoteDescription((string?)data["type"] ?? type, (string?)data["sdp"] ?? string.Empty);
                }

                break;

            case "candidate" when data is not null:
                if (_peers.TryGetValue(_hosting ? peer : 0, out IRtcPeer? at) && (string?)data["candidate"] is { Length: > 0 } line)
                {
                    at.AddRemoteCandidate(line, (string?)data["mid"]);
                }

                break;

            case "closed":
                Close((string?)message["reason"] ?? "the room closed");
                break;
        }
    }

    private void Add(int id, IRtcPeer peer)
    {
        Drop(id);
        _peers[id] = peer;
        _order.Add(id);
    }

    private void Drop(int id)
    {
        if (_peers.Remove(id, out IRtcPeer? peer))
        {
            peer.Dispose();
            _order.Remove(id);
            _next = 0;
        }
    }

    private void Close(string reason)
    {
        if (_status != RoomStatus.Closed)
        {
            _error = reason;
            _status = RoomStatus.Closed;
            _logger.Warning(LogCategory, $"Room {_code ?? "(none)"} closed: {reason}");
        }
    }

    private void Post(JsonObject message)
    {
        _outbox.Enqueue(message.ToJsonString());
        _outboxReady.Release();
    }

    private void Start(Task run) =>
        run.ContinueWith(
            t => _inbox.Enqueue(new JsonObject { ["type"] = "closed", ["reason"] = Reason(t.Exception?.GetBaseException()) }.ToJsonString()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private async Task HostAsync(RoomRequest request)
    {
        JsonObject body = new()
        {
            ["title"] = request.Title,
            ["protocol"] = request.Protocol,
            ["name"] = request.Name,
            ["map"] = request.Map,
            ["mode"] = request.Mode,
            ["version"] = request.Version,
            ["maxPlayers"] = request.MaxPlayers,
            ["public"] = request.Public,
            ["passworded"] = request.Passworded,
        };

        using HttpClient http = new();
        using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(new Uri(_master, "/v1/rooms"), content, _stop.Token).ConfigureAwait(false);
        JsonObject made = JsonNode.Parse(await response.Content.ReadAsStringAsync(_stop.Token).ConfigureAwait(false)) as JsonObject
            ?? throw new InvalidOperationException("the master server answered nothing");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException((string?)made["error"] ?? $"the master server answered {(int)response.StatusCode}");
        }

        string code = (string?)made["code"] ?? throw new InvalidOperationException("no room code");
        _ice = ReadIce(made["ice"] as JsonArray);
        _code = code;
        string token = (string?)made["hostToken"] ?? string.Empty;
        await ConnectAsync(new Uri(_master, $"/v1/rooms/{code}/ws?role=host&token={Uri.EscapeDataString(token)}")).ConfigureAwait(false);
    }

    private async Task ConnectAsync(Uri http)
    {
        UriBuilder ws = new(http) { Scheme = http.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" };
        ClientWebSocket socket = new();
        _socket = socket;
        try
        {
            await socket.ConnectAsync(ws.Uri, _stop.Token).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // The room answers a refusal (no such room, full, not its host) before the upgrade; ask for it in words.
            throw new InvalidOperationException(await RefusalAsync(http).ConfigureAwait(false));
        }

        Task sending = SendLoopAsync(socket);
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream message = new();
        while (!_stop.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                string reason = socket.CloseStatusDescription is { Length: > 0 } said ? said : "the room closed";
                _inbox.Enqueue(new JsonObject { ["type"] = "closed", ["reason"] = reason }.ToJsonString());
                break;
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                _inbox.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
            }
        }

        _outboxReady.Release();
        await sending.ConfigureAwait(false);
    }

    private async Task SendLoopAsync(ClientWebSocket socket)
    {
        while (!_stop.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await _outboxReady.WaitAsync(_stop.Token).ConfigureAwait(false);
            while (_outbox.TryDequeue(out string? text) && socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> RefusalAsync(Uri http)
    {
        try
        {
            using HttpClient client = new();
            string info = await client.GetStringAsync(new Uri(_master, $"/v1/rooms/{Uri.EscapeDataString(_code ?? string.Empty)}"), _stop.Token)
                .ConfigureAwait(false);
            return JsonNode.Parse(info) is JsonObject room && (int?)room["players"] >= (int?)room["maxPlayers"]
                ? "the room is full"
                : "the room refused the connection";
        }
        catch (HttpRequestException)
        {
            return http.Query.Contains("role=join", StringComparison.Ordinal) ? "no room with that code" : "could not reach the master server";
        }
    }

    private static List<IceServer> ReadIce(JsonArray? servers)
    {
        List<IceServer> ice = [];
        foreach (JsonNode? server in servers ?? [])
        {
            if (server is JsonObject s && s["urls"] is JsonArray urls)
            {
                ice.Add(new IceServer([.. urls.Select(u => (string?)u).OfType<string>()], (string?)s["username"], (string?)s["credential"]));
            }
        }

        return ice;
    }

    private static string Reason(Exception? ex) => ex switch
    {
        null => "the room closed",
        OperationCanceledException => "stopped",
        HttpRequestException => "could not reach the master server",
        _ => ex.Message,
    };

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"room {_code ?? "(opening)"}");
}
