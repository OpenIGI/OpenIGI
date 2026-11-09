using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenIGI.Net.Browsing;

/// <summary>What a browser asks the master server, and how it reads servers' own answers.</summary>
/// <param name="Master">The master server, or <see langword="null"/> for none (a LAN-only browser).</param>
/// <param name="Title">The game whose servers are listed, as <c>igi1</c>.</param>
/// <param name="Decode">Reads a server's answer to an info query into its line; <see langword="null"/> for one not understood.</param>
public sealed record ServerBrowserOptions(Uri? Master, string Title, Func<byte[], BrowserInfo?> Decode)
{
    /// <summary>Gets the UDP port servers answer broadcasts on.</summary>
    public int LanPort { get; init; } = 26001;

    /// <summary>Gets the seconds between asks of every listed server, for its ping and its players.</summary>
    public double RequerySeconds { get; init; } = 10;

    /// <summary>Gets this build's own version, so a line of the same build sorts as compatible before it has answered.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the protocol id this build plays (as the master lists it, 16 hex digits), so a room of another version is shown as
    /// one this build cannot join; empty to take every listed line as compatible until it answers.
    /// </summary>
    public string Protocol { get; init; } = string.Empty;
}

/// <summary>
/// The server browser, shared by the game's menu and the launcher: the master server's list (dedicated servers and public
/// rooms), what answers a broadcast on this network, and the player's favourites and history, merged into one line a server
/// and kept current -- every server with an address asked after directly, for a true ping and its live players.
/// </summary>
/// <remarks>
/// <para>
/// The master cannot reach a server itself (a Worker sends no UDP), so a listed server is shown as unanswered until it answers
/// this game's own question; one that never does was listed but cannot be reached from here -- or was never a server.
/// </para>
/// <para>
/// Not thread-safe: <see cref="Refresh"/> and <see cref="Update"/> run on the caller's frame. The master's list is fetched off
/// it and taken in by the next <see cref="Update"/> after it lands.
/// </para>
/// </remarks>
public sealed class ServerBrowser : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly ServerBrowserOptions _options;
    private readonly NetQueryClient? _query;
    private readonly ServerBook _book;
    private readonly Dictionary<string, BrowserServer> _servers = new(StringComparer.Ordinal);
    private Task<List<BrowserServer>>? _fetch;
    private CancellationTokenSource? _cancel;
    private double _lastQuery = double.NegativeInfinity;

    /// <summary>Initializes a new instance of the <see cref="ServerBrowser"/> class.</summary>
    /// <param name="options">What it asks and how it reads answers.</param>
    /// <param name="query">The UDP queries, or <see langword="null"/> where there is no UDP (the web): the master's list alone.</param>
    /// <param name="book">The player's favourites and history.</param>
    public ServerBrowser(ServerBrowserOptions options, NetQueryClient? query, ServerBook book)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _query = query;
        _book = book ?? throw new ArgumentNullException(nameof(book));
    }

    /// <summary>Gets every server heard of, unordered; <see cref="BrowserFilter.Apply"/> picks and orders them.</summary>
    public IEnumerable<BrowserServer> Servers => _servers.Values;

    /// <summary>Gets a value indicating whether the master's list is being fetched.</summary>
    public bool IsFetching => _fetch is { IsCompleted: false };

    /// <summary>Gets why the master's list could not be had the last time, or <see langword="null"/>.</summary>
    public string? MasterError { get; private set; }

    /// <summary>Gets the player's favourites and history.</summary>
    public ServerBook Book => _book;

    /// <summary>Asks again: the master's list, every server on this network, and every favourite and recent server.</summary>
    /// <param name="now">The caller's clock, in seconds.</param>
    public void Refresh(double now)
    {
        if (_options.Master is { } master && !IsFetching)
        {
            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            _fetch = FetchAsync(master, _options.Title, _options.Protocol, _cancel.Token);
        }

        foreach ((BookedServer booked, BrowserSource source) in _book.Favourites.Select(static f => (f, BrowserSource.Favourite))
            .Concat(_book.History.Select(static h => (h, BrowserSource.History))))
        {
            // A server at an address is a line until it answers; a room has no address to ask and is there only while the
            // master lists it -- booked, it only marks the master's line (see TakeMasterList), or it would stand for good.
            if (!IsRoom(booked.Key))
            {
                Merge(Booked(booked, source));
            }
        }

        _query?.QueryLocalNetwork(_options.LanPort, now);
        QueryAll(now);
    }

    /// <summary>Takes in what has come: the master's list once it lands, and every server's answer.</summary>
    /// <param name="now">The caller's clock, in seconds.</param>
    /// <returns><see langword="true"/> when any line changed.</returns>
    public bool Update(double now)
    {
        bool changed = false;
        if (_fetch is { IsCompleted: true } fetched)
        {
            _fetch = null;
            if (fetched.IsCompletedSuccessfully)
            {
                MasterError = null;
                TakeMasterList(fetched.Result);
                QueryAll(now);
            }
            else
            {
                MasterError = fetched.Exception?.GetBaseException().Message ?? "the master server did not answer";
            }

            changed = true;
        }

        if (_query is { } query)
        {
            query.Update(now);
            while (query.TryReceive(out NetServerInfo answer))
            {
                if (_options.Decode(answer.Info) is not { } info || !string.Equals(info.Title, _options.Title, StringComparison.Ordinal))
                {
                    continue;
                }

                string key = BrowserServer.KeyOf(answer.Address);
                BrowserServer line = _servers.TryGetValue(key, out BrowserServer? known)
                    ? known with { Info = info, Ping = answer.Ping, Sources = known.Sources | (IsLocal(answer.Address) ? BrowserSource.Lan : BrowserSource.None) }
                    : new BrowserServer(key, answer.Address, null, info, string.Empty, BrowserSource.Lan, answer.Ping);
                _servers[key] = line;
                changed = true;
            }

            if (now - _lastQuery >= _options.RequerySeconds)
            {
                QueryAll(now);
            }
        }

        return changed;
    }

    /// <summary>Asks after a server by its address from now on, as one the player named: a server just started here, or typed in.</summary>
    /// <param name="address">Its address.</param>
    /// <param name="name">What to call it until it answers.</param>
    /// <param name="now">The caller's clock, in seconds.</param>
    /// <returns>Its line's key.</returns>
    public string Watch(NetAddress address, string name, double now)
    {
        string key = BrowserServer.KeyOf(address);
        Merge(new BrowserServer(key, address, null, new BrowserInfo(_options.Title, name, string.Empty, string.Empty, 0, 0, false, string.Empty, true), string.Empty, BrowserSource.None, null));
        _query?.Query(address, now);
        return key;
    }

    /// <summary>Notes the server joined, as recent.</summary>
    /// <param name="server">The server.</param>
    public void Played(BrowserServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        _book.Played(server.Key, server.Info.Name);
        _servers[server.Key] = server with { Sources = server.Sources | BrowserSource.History };
    }

    /// <summary>Makes a server a favourite, or takes it off the favourites.</summary>
    /// <param name="server">The server.</param>
    /// <param name="favourite">Whether it is one.</param>
    public void SetFavourite(BrowserServer server, bool favourite)
    {
        ArgumentNullException.ThrowIfNull(server);
        _book.SetFavourite(server.Key, server.Info.Name, favourite);
        _servers[server.Key] = server with { Sources = favourite ? server.Sources | BrowserSource.Favourite : server.Sources & ~BrowserSource.Favourite };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
    }

    /// <summary>Reads the master server's list: its dedicated servers and its public rooms, one line each.</summary>
    /// <param name="list">The master's <c>/v1/servers</c> answer.</param>
    /// <param name="title">The game listed.</param>
    /// <param name="protocol">This build's protocol id, which marks lines of another version incompatible; empty for none.</param>
    /// <returns>The lines, unanswered.</returns>
    public static List<BrowserServer> ReadMasterList(JsonNode? list, string title, string protocol = "")
    {
        List<BrowserServer> servers = [];
        foreach (JsonNode? node in (list as JsonObject)?["servers"] as JsonArray ?? [])
        {
            if (node is not JsonObject entry || (string?)entry["title"] != title)
            {
                continue;
            }

            string? room = (string?)entry["room"] is { Length: > 0 } code ? code.ToUpperInvariant() : null;
            NetAddress? address = NetAddress.TryParseIp((string?)entry["address"], (int?)entry["port"] ?? 0, out NetAddress ip) ? ip : null;
            if (address is null && room is null)
            {
                continue;
            }

            BrowserInfo info = new(
                title,
                (string?)entry["name"] ?? room ?? string.Empty,
                (string?)entry["map"] ?? string.Empty,
                (string?)entry["mode"] ?? string.Empty,
                (int?)entry["players"] ?? 0,
                (int?)entry["maxPlayers"] ?? 0,
                (bool?)entry["passworded"] ?? false,
                (string?)entry["version"] ?? string.Empty,
                Compatible: protocol.Length == 0 || string.Equals((string?)entry["protocol"], protocol, StringComparison.OrdinalIgnoreCase));
            string key = address is { } at ? BrowserServer.KeyOf(at) : BrowserServer.KeyOfRoom(room!);
            servers.Add(new BrowserServer(key, address, room, info, (string?)entry["region"] ?? string.Empty, BrowserSource.Internet, null));
        }

        return servers;
    }

    /// <summary>Reads a UDP key's address back (<see cref="BrowserServer.KeyOf"/>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> for a UDP key.</returns>
    public static bool TryAddressOf(string key, out NetAddress address)
    {
        address = default;
        if (key is null || !key.StartsWith("udp:", StringComparison.Ordinal))
        {
            return false;
        }

        string text = key["udp:".Length..];
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int port))
        {
            return false;
        }

        string host = text[..colon];
        if (host == "loopback")
        {
            address = NetAddress.Loopback(port);
            return true;
        }

        if (host is ['[', .. string hex, ']'] && hex.Length == 32 && UInt128.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt128 bits))
        {
            address = new NetAddress(NetAddressKind.Ip, bits, port);
            return true;
        }

        return NetAddress.TryParseIp(host, port, out address);
    }

    private static async Task<List<BrowserServer>> FetchAsync(Uri master, string title, string protocol, CancellationToken cancel)
    {
        string text = await Http.GetStringAsync(new Uri(master, $"/v1/servers?title={Uri.EscapeDataString(title)}"), cancel).ConfigureAwait(false);
        return ReadMasterList(JsonNode.Parse(text), title, protocol);
    }

    /// <summary>
    /// Takes in the master's list as it now stands: each line merged, and a line the master listed before but no longer does
    /// (a room closed, a server gone quiet) dropped -- or, for a server at an address the player also knows otherwise (saved,
    /// played on, on this network), kept for that alone. A room is never kept: it lives only as long as the master lists it,
    /// and its code is not given out again, so a closed room saved or played on is a line nobody can join.
    /// </summary>
    /// <param name="listed">The master's lines.</param>
    internal void TakeMasterList(IReadOnlyList<BrowserServer> listed)
    {
        HashSet<string> keys = new(listed.Select(static s => s.Key), StringComparer.Ordinal);
        foreach ((string key, BrowserServer line) in _servers.ToArray())
        {
            if (!keys.Contains(key) && (IsRoom(key) || line.Sources.HasFlag(BrowserSource.Internet)))
            {
                BrowserSource rest = line.Sources & ~BrowserSource.Internet;
                if (rest == BrowserSource.None || IsRoom(key))
                {
                    _servers.Remove(key);
                }
                else
                {
                    _servers[key] = line with { Sources = rest };
                }
            }
        }

        foreach (BrowserServer server in listed)
        {
            BrowserSource booked = (_book.IsFavourite(server.Key) ? BrowserSource.Favourite : BrowserSource.None)
                | (_book.History.Any(h => h.Key == server.Key) ? BrowserSource.History : BrowserSource.None);
            Merge(IsRoom(server.Key) ? server with { Sources = server.Sources | booked } : server);
        }
    }

    // Whether a line is a room, reached through the master's signalling rather than at an address.
    private static bool IsRoom(string key) => key.StartsWith(BrowserServer.KeyOfRoom(string.Empty), StringComparison.Ordinal);

    // A booked server as a line of its own until it answers or the master lists it.
    private BrowserServer Booked(BookedServer booked, BrowserSource source)
    {
        NetAddress? address = TryAddressOf(booked.Key, out NetAddress at) ? at : null;
        string? room = booked.Key.StartsWith("room:", StringComparison.Ordinal) ? booked.Key["room:".Length..] : null;
        BrowserInfo info = new(_options.Title, booked.Name, string.Empty, string.Empty, 0, 0, false, string.Empty, Compatible: true);
        return new BrowserServer(booked.Key, address, room, info, string.Empty, source, null);
    }

    // One line a server: what is already known of it (its ping, its live answer) kept, what is new added.
    private void Merge(BrowserServer heard)
    {
        if (!_servers.TryGetValue(heard.Key, out BrowserServer? known))
        {
            _servers[heard.Key] = heard;
            return;
        }

        // A server's own answer outranks the master's word on its players and map; the master adds its room and region.
        _servers[heard.Key] = known with
        {
            Room = known.Room ?? heard.Room,
            Region = known.Region.Length > 0 ? known.Region : heard.Region,
            Info = known.Answered || heard.Info.Map.Length == 0 ? known.Info : heard.Info,
            Sources = known.Sources | heard.Sources,
        };
    }

    private void QueryAll(double now)
    {
        if (_query is not { } query)
        {
            return;
        }

        _lastQuery = now;
        foreach (BrowserServer server in _servers.Values)
        {
            if (server.Address is { } address)
            {
                query.Query(address, now);
            }
        }
    }

    // A private or link-local address: a server on this network.
    private static bool IsLocal(NetAddress address)
    {
        if (address.Kind == NetAddressKind.Loopback)
        {
            return true;
        }

        if (!address.IsIPv4)
        {
            return false;
        }

        uint v4 = (uint)address.Host;
        byte a = (byte)(v4 >> 24);
        byte b = (byte)(v4 >> 16);
        return a == 10 || a == 127 || (a == 172 && b is >= 16 and <= 31) || (a == 192 && b == 168) || (a == 169 && b == 254);
    }
}
