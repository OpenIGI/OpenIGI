using System.Diagnostics;
using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Frontend;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using OpenIGI.Net.Browsing;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// Co-op through rooms from the menu, on any build: opens a room and starts its mission, or finds a room by its code,
/// starts the mission its host plays and joins it. The session runs as the host's tick observer, as one started from the
/// command line does.
/// </summary>
/// <remarks>
/// Lookups go to the master server in the background; <see cref="Update"/>, once a frame on the game's thread, acts on
/// what they found, so nothing here blocks a frame.
/// </remarks>
internal sealed class CoopRooms : ICoopRooms, IDisposable
{
    private const string LogCategory = "Room";

    private readonly IRtcPeerFactory _peers;
    private readonly Func<string> _titleId;
    private readonly Func<string> _playerName;
    private readonly Func<int> _difficulty;
    private readonly Func<string> _content;
    private readonly Action<IHostTickObserver?> _observe;
    private readonly Action<string?> _preview;
    private readonly Action<float> _turn;
    private readonly IOpenIgiLogger _logger;
    private readonly string _version;
    private IDisposable? _session;
    private Task<RoomListing?>? _finding;
    private string? _findingCode;
    private ServerBrowser? _browser;
    private string? _browsing;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private IReadOnlyList<CoopRoomInfo>? _rooms = [];
    private string? _listingError;
    private string? _failure;

    public CoopRooms(
        IRtcPeerFactory peers,
        Func<string> titleId,
        Func<string> playerName,
        Func<int> difficulty,
        Func<string> content,
        Action<IHostTickObserver?> observe,
        Action<string?> preview,
        Action<float> turn,
        string version,
        IOpenIgiLogger logger)
    {
        _peers = peers;
        _titleId = titleId;
        _playerName = playerName;
        _difficulty = difficulty;
        _content = content;
        _observe = observe;
        _preview = preview;
        _turn = turn;
        _version = version;
        _logger = logger;
    }

    /// <summary>Gets which missions' maps can be played as arenas; every one when unset.</summary>
    public Func<int, bool>? Arenas { get; init; }

    /// <inheritdoc/>
    public bool HasArena(int mission) => Arenas?.Invoke(mission) ?? true;

    /// <summary>Gets or sets what reads the game's tables again, for a server's shared files put in place before the join.</summary>
    public Action? ReloadTables { get; set; }

    // A joined server's mission, started once the server's files are in: the mission and difficulty, and whether the tables
    // have been asked to be read again for it.
    private (int Mission, int Difficulty, bool Reloaded)? _startWhenReady;

    // A client's mission started once its files are in, the game's tables read again first when the server's shared ones
    // went in: a frame for the reload to happen at the frame boundary, then the mission.
    private void StartWhenFilesReady()
    {
        if (_startWhenReady is not { } start || _session is not SessionClient client)
        {
            _startWhenReady = null;
            return;
        }

        if (client.Ended is not null || !client.FilesReady)
        {
            if (client.Ended is not null)
            {
                _startWhenReady = null;
            }

            return;
        }

        if (!start.Reloaded && client.Files?.TakeChanges() is { Shared: true } && ReloadTables is { } reload)
        {
            _logger.Information(LogCategory, "The server's shared files are in; the game's tables are read again before the mission.");
            _startWhenReady = start with { Reloaded = true };
            reload();
            return;
        }

        _startWhenReady = null;
        Shell?.StartMission(start.Mission, start.Difficulty);
    }

    /// <summary>Gets or sets the shell the missions are started through.</summary>
    public IShellHost? Shell { get; set; }

    /// <inheritdoc/>
    public CoopStatus Status => _session switch
    {
        HostedSession hosted when hosted.RoomCode is { } code => new CoopStatus(CoopPhase.Playing, code, Hosting: true),
        HostedSession => new CoopStatus(CoopPhase.Connecting, Hosting: true),
        SessionClient { Ended: { } ended } client => new CoopStatus(CoopPhase.Failed, client.RoomCode, Error: ended),
        SessionClient { FilesReady: false } client => new CoopStatus(CoopPhase.Connecting, client.RoomCode),
        SessionClient { IsRejoining: true } client => new CoopStatus(CoopPhase.Connecting, client.RoomCode),
        SessionClient { Room: null } => new CoopStatus(CoopPhase.Playing),
        SessionClient client when client.Room is { } room => room.Status switch
        {
            RoomStatus.Open => new CoopStatus(CoopPhase.Playing, room.Code),
            RoomStatus.Closed => new CoopStatus(CoopPhase.Failed, room.Code, Error: room.Error),
            _ => new CoopStatus(CoopPhase.Connecting, room.Code),
        },
        _ when _finding is not null => new CoopStatus(CoopPhase.Connecting, _findingCode),
        _ when _waitingFor is not null => new CoopStatus(CoopPhase.Connecting),
        _ when _failure is not null => new CoopStatus(CoopPhase.Failed, Error: _failure),
        _ => new CoopStatus(CoopPhase.None),
    };

    /// <inheritdoc/>
    public IReadOnlyList<CoopRoomInfo>? Listing => _rooms;

    /// <inheritdoc/>
    public string? ListingError => _listingError;

    private string Protocol => MultiplayerProtocol.IdFor(_titleId()).ToString("x16", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public void Host(int missionId, int difficulty, bool listed, string mode = Game.Multiplayer.Modes.GameModes.Coop)
    {
        Leave();

        // Co-op is a squad of four; a versus game hosted in a player's own game takes up to sixteen.
        bool versus = Game.Multiplayer.Modes.ArenaLevel.IsVersus(mode);
        SessionArguments arguments = new(
            Connect: null, Listen: false, Port: 0, PlayerName: _playerName(), ServerName: $"{_playerName()}'s game", MaxPlayers: versus ? 15 : 3,
            Password: string.Empty, Mode: versus ? mode : Game.Multiplayer.Modes.GameModes.Coop, Host: true, Public: listed);
        Begin(RoomSessions.Start(arguments, _titleId(), missionId, _version, _peers, _logger, _content()));
        Shell?.StartMission(missionId, difficulty);
    }

    /// <inheritdoc/>
    public void Join(string code)
    {
        Leave();
        if (string.IsNullOrWhiteSpace(code))
        {
            _failure = "enter the room's code";
            return;
        }

        // A line of the list: a server this build reaches by address is joined so; any other through its room.
        if (_browser?.Servers.FirstOrDefault(s => s.Key == code) is { } line)
        {
            _browser.Played(line);
            if (line.Address is { } address && OpenUdp is { } open)
            {
                // One not yet heard from (a saved or recent server) is joined once it answers with what it plays.
                if (!line.Answered)
                {
                    _waitingFor = (line.Key, _clock.Elapsed.TotalSeconds, WaitSeconds);
                    _browser.Refresh(_clock.Elapsed.TotalSeconds);
                    return;
                }

                JoinServer(line, address, open);
                return;
            }

            if (line.Room is not { } room)
            {
                _failure = "that server can't be reached from this build";
                return;
            }

            code = room;
        }

        _findingCode = code.Trim().ToUpperInvariant();
        _finding = RoomDirectory.FindAsync(RoomTransport.DefaultMaster, _findingCode);
    }

    // A server by its address, its level brought up as it plays it: its map, an arena or a mission.
    private void JoinServer(BrowserServer line, NetAddress address, Func<int, INetTransport> open)
    {
        if (line.Info.Passworded)
        {
            _failure = "that server asks a password; join it with -connect and -password";
            return;
        }

        if (!line.Info.Compatible)
        {
            _failure = $"that server runs another version ({line.Info.Version})";
            return;
        }

        if (!int.TryParse(line.Info.Map, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mission) || mission <= 0)
        {
            _failure = "that server's map isn't one this game has";
            return;
        }

        JoinRequest request = new(_playerName(), _version, string.Empty, _content(), PlayerSkins.Of(PlayerSkins.Chosen.Value).Id);
        Begin(new SessionClient(open(0), address, request, _titleId(), _logger, reopen: () => open(0)) { Mode = line.Info.Mode });
        _logger.Information(LogCategory, $"Joining {line.Info.Name} at {address} on mission {mission} ({line.Info.Mode}).");
        _startWhenReady = (mission, _difficulty(), false);
    }

    /// <summary>
    /// Joins where a party's leader plays: a room by its code, or a server by its browser key, asked after first when this
    /// game's browser has not heard of it.
    /// </summary>
    /// <param name="target">The room's code or the server's key (<c>udp:host:port</c>).</param>
    public void Follow(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (ServerBrowser.TryAddressOf(target, out NetAddress address) && OpenUdp is not null)
        {
            Browser().Watch(address, target, _clock.Elapsed.TotalSeconds);
        }

        Join(target);
    }

    /// <inheritdoc/>
    public void Leave()
    {
        _waitingFor = null;
        _finding = null;
        _findingCode = null;
        _failure = null;
        _startWhenReady = null;
        if (_session is not null)
        {
            _observe(null);
            _session.Dispose();
            _session = null;
        }
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public IReadOnlyList<string> Players => (_session as HostedSession)?.Players ?? [];

    /// <inheritdoc/>
    public void Kick(string name) => (_session as HostedSession)?.Kick(name);

    /// <inheritdoc/>
    public bool CanRetryFromCheckpoint => (_session as HostedSession)?.CanRetryFromCheckpoint == true;

    /// <inheritdoc/>
    public void RetryFromCheckpoint() => (_session as HostedSession)?.RetryFromCheckpoint();

    public void RefreshListing() => Browser().Refresh(_clock.Elapsed.TotalSeconds);

    /// <summary>Gets or sets what opens a UDP socket on a port (0 for any), where this build has UDP: servers on this network and
    /// dedicated servers are asked after directly and joined by address. Without it the list is the master's alone.</summary>
    public Func<int, INetTransport>? OpenUdp { get; set; }

    /// <summary>Gets or sets the file the player's favourite and recent servers are kept in, or <see langword="null"/> for none.</summary>
    public string? BookPath { get; set; }

    /// <inheritdoc/>
    public void SetFavourite(string code, bool favourite)
    {
        if (Browser().Servers.FirstOrDefault(s => s.Key == code) is { } server)
        {
            _browser!.SetFavourite(server, favourite);
            _rooms = Lines();
        }
    }

    /// <inheritdoc/>
    public string? LastPlayed => Browser().Book.History is [var last, ..] ? last.Name : null;

    /// <inheritdoc/>
    public DateTimeOffset? LastPlayedAt => Browser().Book.History is [var last, ..] ? last.At : null;

    /// <inheritdoc/>
    public void Reconnect()
    {
        if (Browser().Book.History is not [var last, ..])
        {
            return;
        }

        // The booked line is in the list once it has been asked after; a room's code joins as typed in.
        _browser!.Refresh(_clock.Elapsed.TotalSeconds);
        Join(last.Key.StartsWith("room:", StringComparison.Ordinal) && _browser.Servers.All(s => s.Key != last.Key) ? last.Key["room:".Length..] : last.Key);
    }

    /// <inheritdoc/>
    public void QuickPlay()
    {
        if (BrowserFilter.QuickPlay(Browser().Servers) is { } best)
        {
            Join(best.Key);
            return;
        }

        Leave();
        _failure = "no open game to join right now; host one, or refresh the list";
        RefreshListing();
    }

    // The browser of the game played now: made again when the player switches game, as each lists its own servers.
    private ServerBrowser Browser()
    {
        string title = _titleId();
        if (_browser is null || _browsing != title)
        {
            _browser?.Dispose();
            _browsing = title;
            _book ??= new ServerBook(BookPath);
            NetQueryClient? query = OpenUdp is { } open ? new NetQueryClient(open(0), MultiplayerProtocol.IdFor(title)) : null;
            _browser = new ServerBrowser(new ServerBrowserOptions(RoomTransport.DefaultMaster, title, Decode) { Version = _version, Protocol = Protocol }, query, _book);
        }

        return _browser;
    }

    private ServerBook? _book;

    // A server waited on to answer before it is joined, since when, and for how long before it is given up on.
    private (string Key, double Since, double Seconds)? _waitingFor;
    private const double WaitSeconds = 4;

    // A dedicated server brings its level up before it answers: a minute, on a slow disk.
    private const double ServerStartSeconds = 60;
    private Process? _server;
    private DedicatedServerLaunch? _serverLaunch;
    private double _askedServerAt;

    /// <summary>Gets or sets where the game played now reads its data from, for a dedicated server started here to read the same.</summary>
    public Func<string?>? GamePath { get; set; }

    /// <inheritdoc/>
    public bool CanHostServer => OpenUdp is not null && DedicatedServerLaunch.FindServer(Core.Configuration.SettingsPaths.ExecutableDirectory) is not null;

    /// <inheritdoc/>
    public string? ServerStatus => _server is null || _serverLaunch is not { } launch
        ? null
        : _server.HasExited
            ? $"The server stopped (exit code {_server.ExitCode}); its log is in {ServerFolder(launch.Title)}."
            : string.Create(CultureInfo.InvariantCulture, $"Your server plays {launch.Mode.ToUpperInvariant()} on port {launch.Port}{(launch.Public ? ", listed" : string.Empty)}.");

    /// <inheritdoc/>
    public void HostServer(int missionId, string mode, bool listed)
    {
        StopServer();
        Leave();
        if (DedicatedServerLaunch.FindServer(Core.Configuration.SettingsPaths.ExecutableDirectory) is not { } executable)
        {
            _failure = "this build has no dedicated server beside it";
            return;
        }

        DedicatedServerLaunch launch = new()
        {
            Title = _titleId(),
            Name = $"{_playerName()}'s server",
            Mission = missionId,
            Mode = mode,
            Public = listed,
            GamePath = GamePath?.Invoke(),
        };
        try
        {
            _server = launch.Start(executable, ServerFolder(launch.Title));
            _serverLaunch = launch;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _failure = $"the server didn't start: {ex.Message}";
            return;
        }

        _logger.Information(LogCategory, $"Started a dedicated server ({executable}) for mission {missionId}, {mode}, on port {launch.Port}.");
        NetAddress.TryParseIp("127.0.0.1", launch.Port, out NetAddress here);
        string key = Browser().Watch(here, launch.Name, _clock.Elapsed.TotalSeconds);
        _waitingFor = (key, _clock.Elapsed.TotalSeconds, ServerStartSeconds);
    }

    /// <inheritdoc/>
    public void StopServer()
    {
        if (_server is { } server)
        {
            DedicatedServerLaunch.Stop(server);
            server.Dispose();
            _server = null;
            _serverLaunch = null;
        }
    }

    private static string ServerFolder(string title) => DedicatedServerLaunch.FolderFor(title);

    private static BrowserInfo? Decode(byte[] info) =>
        ServerInfo.Decode(info) is { } said
            ? new BrowserInfo(said.TitleId, said.Name, said.Map, said.Mode, said.Players, said.MaxPlayers, said.HasPassword, said.Version, said.IsCompatibleWith(MultiplayerProtocol.Version))
            : null;

    // The browser's lines as the menu shows them.
    private CoopRoomInfo[] Lines() =>
        [.. new BrowserFilter().Apply(_browser?.Servers ?? []).Select(static s => new CoopRoomInfo(
            s.Key,
            s.Info.Name,
            int.TryParse(s.Info.Map, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mission) ? mission : 0,
            s.Info.Players,
            s.Info.MaxPlayers,
            s.Info.Passworded)
        {
            Mode = s.Info.Mode,
            PingMs = s.Ping is { } ping ? (int)Math.Round(ping * 1000) : null,
            Region = s.Region,
            Dedicated = s.Address is not null && s.Sources.HasFlag(BrowserSource.Internet),
            Lan = s.Sources.HasFlag(BrowserSource.Lan),
            Favourite = s.Sources.HasFlag(BrowserSource.Favourite),
            Recent = s.Sources.HasFlag(BrowserSource.History),
            Compatible = s.Info.Compatible,
        })];

    /// <summary>Acts on what the master server answered since the last frame.</summary>
    public void Update()
    {
        StartWhenFilesReady();

        if (_waitingFor is { } waiting && _browser is { } asked)
        {
            if (asked.Servers.FirstOrDefault(s => s.Key == waiting.Key) is { Answered: true, Address: { } at } heard && OpenUdp is { } udp)
            {
                _waitingFor = null;
                JoinServer(heard, at, udp);
            }
            else if (_server is { HasExited: true } && waiting.Seconds == ServerStartSeconds)
            {
                _waitingFor = null;
                _failure = ServerStatus;
            }
            else if (_clock.Elapsed.TotalSeconds - waiting.Since > waiting.Seconds)
            {
                _waitingFor = null;
                _failure = "that server didn't answer";
            }
            else if (_clock.Elapsed.TotalSeconds - _askedServerAt >= 1 && ServerBrowser.TryAddressOf(waiting.Key, out NetAddress again))
            {
                // Asked again each second: a server bringing its level up answers only once it is.
                _askedServerAt = _clock.Elapsed.TotalSeconds;
                asked.Watch(again, asked.Servers.FirstOrDefault(s => s.Key == waiting.Key)?.Info.Name ?? waiting.Key, _askedServerAt);
            }
        }

        if (_browser is { } browser && browser.Update(_clock.Elapsed.TotalSeconds))
        {
            CoopRoomInfo[] rooms = Lines();
            if (_rooms is null || !_rooms.SequenceEqual(rooms))
            {
                _rooms = rooms;
            }

            _listingError = browser.MasterError is not null && rooms.Length == 0 ? "couldn't reach the server list" : null;
        }

        if (_finding is { IsCompleted: true } finding)
        {
            string code = _findingCode!;
            _finding = null;
            _findingCode = null;
            RoomListing? room = finding.IsCompletedSuccessfully ? finding.Result : null;
            if (!finding.IsCompletedSuccessfully)
            {
                _failure = "couldn't reach the room list";
            }
            else if (room is null)
            {
                _failure = $"no room with the code {code}";
            }
            else if (room.Protocol != Protocol || room.Title != _titleId())
            {
                _failure = $"that room plays another game or version ({room.Title} {room.Version})";
            }
            else if (room.Players >= room.MaxPlayers)
            {
                _failure = "that room is full";
            }
            else if (room.Mission() is not { } mission)
            {
                _failure = "that room's mission isn't one this game has";
            }
            else
            {
                SessionArguments arguments = new(
                    Connect: null, Listen: false, Port: 0, PlayerName: _playerName(), ServerName: string.Empty, MaxPlayers: 0,
                    Password: string.Empty, Mode: room.Mode, Join: code);
                Begin(RoomSessions.Start(arguments, _titleId(), mission, _version, _peers, _logger, _content()));
                _logger.Information(LogCategory, $"Joining room {code} on mission {mission}.");
                _startWhenReady = (mission, _difficulty(), false);
            }
        }
    }

    /// <inheritdoc/>
    public void PreviewLook(string? skinId) => _preview(skinId);

    /// <inheritdoc/>
    public void TurnLook(float radians) => _turn(radians);

    /// <inheritdoc/>
    public void Dispose()
    {
        Leave();
        StopServer();
        _browser?.Dispose();
    }

    private void Begin(IDisposable? session)
    {
        _session = session;
        _observe(session as IHostTickObserver);
    }
}
