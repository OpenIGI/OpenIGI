using System.Collections.Concurrent;
using System.Globalization;
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

    /// <summary>Gets the link a friend opens to join a room: in the browser, or the game where it handles the link.</summary>
    /// <param name="code">The room's code.</param>
    /// <returns>The link.</returns>
    public static string InviteLink(string code) => $"https://openigi.com/join/{Uri.EscapeDataString(code)}";

    /// <summary>The host's address, to a joiner.</summary>
    public static readonly NetAddress HostAddress = NetAddress.Peer(0);

    private const string LogCategory = "Room";

    // How long a host keeps trying to get back to its room after its socket to the master dropped: the room waits a minute.
    private static readonly TimeSpan HostReturn = TimeSpan.FromSeconds(55);

    // The close code the room's own closing carries: the room is gone, not the line.
    private const int RoomClosedCode = 4001;

    // How long a host that reopens its room waits before opening another, at first and at most; doubled each time one fails
    // straight away, so a master that is down is not asked without end.
    private static readonly TimeSpan ReopenFirst = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReopenMost = TimeSpan.FromSeconds(60);

    // What the host's own task tells the network's thread when it gives a room up for another: see Handle.
    private const string ReopeningMessage = "reopening";

    private readonly Uri _master;
    private readonly IRtcPeerFactory _factory;
    private readonly IOpenIgiLogger _logger;
    private readonly bool _hosting;

    // Whether a host whose room is gone opens another, rather than ending: a dedicated server, which outlives any one room.
    private readonly bool _reopens;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentQueue<string> _inbox = new();
    private readonly ConcurrentQueue<string> _outbox = new();
    private readonly SemaphoreSlim _outboxReady = new(0);
    private readonly Dictionary<int, IRtcPeer> _peers = [];

    /// <summary>Gets whether any of the room's connections goes through the TURN relay.</summary>
    public bool Relayed => _peers.Values.Any(static p => p.State == RtcPeerState.Open && p.Relayed);
    private readonly List<int> _order = [];
    private IReadOnlyList<IceServer> _ice = [];
    private ClientWebSocket? _socket;
    private int _next;
    private volatile string? _code;
    private volatile string? _error;

    // Why the room last closed itself, for a host that reopens to say; and what the host last told the list of itself, to
    // tell the next room: how many play (-1 before it has said) and on what map.
    private volatile string? _closedBecause;
    private volatile int _reportedPlayers = -1;
    private volatile string? _reportedMap;

    // The room numbers its joiners from the start again; a host on its second room still has the first room's players. So
    // each room's numbers are moved past every number used before it. The network thread's alone.
    private int _peerOffset;
    private int _highestPeer;
    private volatile RoomStatus _status = RoomStatus.Connecting;
    private bool _disposed;

    private RoomTransport(Uri master, IRtcPeerFactory factory, IOpenIgiLogger logger, bool hosting, bool reopens = false)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hosting = hosting;
        _reopens = hosting && reopens;
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

    /// <inheritdoc/>
    /// <remarks>Every data channel runs over DTLS.</remarks>
    public bool IsEncrypted => true;

    /// <summary>Opens a room and hosts it.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="request">The room.</param>
    /// <param name="factory">Makes the WebRTC connections.</param>
    /// <param name="logger">Where its progress is told.</param>
    /// <param name="reopens">
    /// Whether the host opens another room when this one is gone -- the master restarted, or out of reach for longer than
    /// a room waits -- instead of ending. For a dedicated server, which runs for weeks: without it the server went on
    /// listing a room that no longer was, and no browser could join it until it was restarted. The players already in
    /// are untouched; <see cref="Code"/> is <see langword="null"/> between rooms and then the new room's.
    /// </param>
    /// <returns>The transport, opening the room.</returns>
    public static RoomTransport Host(Uri master, RoomRequest request, IRtcPeerFactory factory, IOpenIgiLogger logger, bool reopens = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        RoomTransport transport = new(master, factory, logger, hosting: true, reopens);
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

        _reportedPlayers = players;
        if (map is not null)
        {
            _reportedMap = map;
        }

        Post(StateMessage(players, map));
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
    public int Backlog(NetAddress destination) =>
        destination.Kind == NetAddressKind.Peer && _peers.TryGetValue((int)(uint)destination.Host, out IRtcPeer? peer) ? peer.Backlog : 0;

    /// <summary>Gets how the connection to a peer runs, for diagnostics.</summary>
    /// <param name="destination">The peer.</param>
    /// <returns>Its route, or "none" when there is no such peer.</returns>
    public string Route(NetAddress destination) =>
        destination.Kind == NetAddressKind.Peer && _peers.TryGetValue((int)(uint)destination.Host, out IRtcPeer? peer) ? peer.Route : "none";

    /// <summary>Gets how many datagrams to a peer were dropped because its channel was full.</summary>
    /// <param name="destination">The peer.</param>
    /// <returns>The count.</returns>
    public long Dropped(NetAddress destination) =>
        destination.Kind == NetAddressKind.Peer && _peers.TryGetValue((int)(uint)destination.Host, out IRtcPeer? peer) ? peer.Dropped : 0;

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

        // A host leaving says goodbye, so its room closes now rather than waiting a minute for it to come back.
        if (_hosting && _socket is { State: WebSocketState.Open } open)
        {
            try
            {
                open.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "the host left", CancellationToken.None).Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) when (ex is WebSocketException or AggregateException or ObjectDisposedException)
            {
                // Gone already: the room finds out by itself.
            }
        }

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

    /// <summary>Handles what the room said and what each connection has to say back, without taking a datagram.</summary>
    /// <remarks>
    /// <see cref="TryReceive"/> does this itself; a joiner calls it while it waits for <see cref="Status"/> to open, before
    /// anything reads datagrams.
    /// </remarks>
    public void Poll() => Pump();

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
                    // A player of a room since given up has nothing to say to the room there is now.
                    if (id < _peerOffset)
                    {
                        continue;
                    }

                    message["peer"] = id - _peerOffset;
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
        int peer = message["peer"] is JsonValue p ? (int)p + (_hosting ? _peerOffset : 0) : 0;
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

            // The master picks a relay for each host and joiner pair and sends this host's logins on it with the join; a
            // master from before that sends none, and the relay the room was made with serves.
            case "join" when _hosting:
                Add(peer, _factory.Create(message["ice"] is JsonArray pairIce ? ReadIce(pairIce) : _ice, offerer: false));
                break;

            // A joiner's socket to the room closed. One still on its way in is gone; one whose channel is open is playing, and
            // has only lost the room -- its line blinked, the master was redeployed -- which it needs no longer. Its channel
            // says so itself when it really goes. Dropped here, every redeploy of the master put every joiner of every room
            // out of its game.
            case "leave" when _hosting:
                if (!_peers.TryGetValue(peer, out IRtcPeer? leaving) || leaving.State != RtcPeerState.Open)
                {
                    Drop(peer);
                }

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

            // This host gave its room up and is opening another, whose joiners are numbered from the start again: past
            // every number a player of the old room holds, so none of them is taken for a newcomer and dropped.
            case ReopeningMessage when _hosting:
                _peerOffset = _highestPeer + 1;
                break;
        }
    }

    private void Add(int id, IRtcPeer peer)
    {
        Drop(id);
        _peers[id] = peer;
        _order.Add(id);
        _highestPeer = Math.Max(_highestPeer, id);
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

    private static JsonObject StateMessage(int players, string? map)
    {
        JsonObject state = new() { ["type"] = "state", ["players"] = players };
        if (map is not null)
        {
            state["map"] = map;
        }

        return state;
    }

    // Hosts a room until it ends; and, for a host that reopens, another after it, for as long as the host runs.
    private async Task HostAsync(RoomRequest request)
    {
        TimeSpan wait = ReopenFirst;
        while (true)
        {
            DateTime opened = DateTime.UtcNow;
            string? lost;
            try
            {
                lost = await HostRoomAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex) when (_reopens && !_stop.IsCancellationRequested
                && ex is InvalidOperationException or WebSocketException or HttpRequestException or OperationCanceledException)
            {
                // The master out of reach, refusing, or never answering (the request timing out): not the end for a host
                // that will ask again.
                lost = Reason(ex);
            }

            if (_stop.IsCancellationRequested)
            {
                return;
            }

            if (!_reopens)
            {
                if (lost is not null)
                {
                    _inbox.Enqueue(new JsonObject { ["type"] = "closed", ["reason"] = lost }.ToJsonString());
                }

                return;
            }

            // The room is gone, the game is not: its players play on over their own connections. Nothing of the old room
            // is said to the new one, and the list is told of no room until there is one.
            string was = _code ?? "(none)";
            _code = null;
            _status = RoomStatus.Connecting;
            while (_outbox.TryDequeue(out _))
            {
            }

            _inbox.Enqueue(new JsonObject { ["type"] = ReopeningMessage }.ToJsonString());

            // A room that lasted was lost to something passing; one that never opened, to something still wrong.
            wait = DateTime.UtcNow - opened > ReopenMost ? ReopenFirst : wait;
            _logger.Warning(LogCategory, $"Room {was} is gone ({lost ?? _closedBecause ?? "the room closed"}); opening another in {wait.TotalSeconds:0} s.");
            await Task.Delay(wait, _stop.Token).ConfigureAwait(false);
            wait = TimeSpan.FromSeconds(Math.Min(ReopenMost.TotalSeconds, wait.TotalSeconds * 2));
        }
    }

    // One room, made and hosted until it ends. Why the host lost it, when it did; null when the room closed itself (which
    // it has said) or the host stopped.
    private async Task<string?> HostRoomAsync(RoomRequest request)
    {
        JsonObject body = new()
        {
            ["title"] = request.Title,
            ["protocol"] = request.Protocol,
            ["name"] = request.Name,
            ["map"] = _reportedMap ?? request.Map,
            ["mode"] = request.Mode,
            ["version"] = request.Version,
            ["maxPlayers"] = request.MaxPlayers,
            ["public"] = request.Public,
            ["passworded"] = request.Passworded,
        };

        using HttpClient http = new();
        using StringContent content = new(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.PostAsync(new Uri(_master, "/v1/rooms"), content, _stop.Token).ConfigureAwait(false);
        JsonObject? answered;
        try
        {
            answered = JsonNode.Parse(await response.Content.ReadAsStringAsync(_stop.Token).ConfigureAwait(false)) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            // Not the master's own answer: an error page from whatever stands in front of it.
            throw new InvalidOperationException($"the master server answered {(int)response.StatusCode}");
        }

        JsonObject made = answered ?? throw new InvalidOperationException("the master server answered nothing");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException((string?)made["error"] ?? $"the master server answered {(int)response.StatusCode}");
        }

        string code = (string?)made["code"] ?? throw new InvalidOperationException("no room code");
        _ice = ReadIce(made["ice"] as JsonArray);
        _code = code;
        string token = (string?)made["hostToken"] ?? string.Empty;
        Uri signalling = new(_master, $"/v1/rooms/{code}/ws?role=host&token={Uri.EscapeDataString(token)}&returns=1");

        // A room opened in place of one lost starts from what the list was last told, not from an empty game.
        if (_reportedPlayers >= 0)
        {
            Post(StateMessage(_reportedPlayers, _reportedMap));
        }

        // The socket to the room drops -- the line blinks, the master is redeployed -- while the game runs on between the
        // players: the host goes back to its room, which waits for it, so new joiners can still arrive. Only the first
        // connection failing is the end at once.
        DateTime? lostAt = null;
        TimeSpan wait = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                bool roomClosed = await ConnectAsync(signalling).ConfigureAwait(false);
                if (roomClosed || _stop.IsCancellationRequested)
                {
                    return null;
                }

                lostAt = DateTime.UtcNow;
                wait = TimeSpan.FromSeconds(1);
                _logger.Warning(LogCategory, $"Lost room {code}'s master server; getting back to it.");
            }
            catch (Exception ex) when (lostAt is not null && ex is InvalidOperationException or WebSocketException or HttpRequestException)
            {
                // Not back yet: the line still down, or the room not yet aware the last socket went.
            }

            if (DateTime.UtcNow - lostAt > HostReturn)
            {
                return "lost the master server";
            }

            await Task.Delay(wait, _stop.Token).ConfigureAwait(false);
            wait = TimeSpan.FromSeconds(Math.Min(8, wait.TotalSeconds * 2));
        }
    }

    // One socket to the room, read until it closes: true when the room itself closed it, false when only the line dropped.
    private async Task<bool> ConnectAsync(Uri http)
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
        bool roomClosed = false;
        try
        {
            while (!_stop.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // The room closing, or refusing this side, is the end; a host's socket closed any other way is only the
                    // line, and the host goes back. A joiner whose channel to the host is open needs the room no more.
                    roomClosed = !_hosting || (int?)socket.CloseStatus is RoomClosedCode or 4000;
                    if (roomClosed && (_hosting || _status != RoomStatus.Open))
                    {
                        string reason = socket.CloseStatusDescription is { Length: > 0 } said ? said : "the room closed";
                        _closedBecause = reason;

                        // A host that reopens is not closed by its room closing: it opens another.
                        if (!_reopens)
                        {
                            _inbox.Enqueue(new JsonObject { ["type"] = "closed", ["reason"] = reason }.ToJsonString());
                        }
                    }

                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    _inbox.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                }
            }
        }
        catch (WebSocketException) when (_hosting || _status == RoomStatus.Open)
        {
            // The line to the master dropped: a host goes back to its room, a joiner already playing doesn't need it.
            roomClosed = !_hosting;
        }

        _outboxReady.Release();
        try
        {
            await sending.ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // The same drop, seen sending.
        }

        socket.Dispose();
        return roomClosed;
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
