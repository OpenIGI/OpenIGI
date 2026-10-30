using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Frontend;
using OpenIGI.Game.Multiplayer;
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
    private readonly IOpenIgiLogger _logger;
    private readonly string _version;
    private IDisposable? _session;
    private Task<RoomListing?>? _finding;
    private string? _findingCode;
    private Task<IReadOnlyList<RoomListing>>? _listing;
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
        _version = version;
        _logger = logger;
    }

    /// <summary>Gets or sets the shell the missions are started through.</summary>
    public IShellHost? Shell { get; set; }

    /// <inheritdoc/>
    public CoopStatus Status => _session switch
    {
        HostedSession hosted when hosted.RoomCode is { } code => new CoopStatus(CoopPhase.Playing, code, Hosting: true),
        HostedSession => new CoopStatus(CoopPhase.Connecting, Hosting: true),
        SessionClient client when client.Room is { } room => room.Status switch
        {
            RoomStatus.Open => new CoopStatus(CoopPhase.Playing, room.Code),
            RoomStatus.Closed => new CoopStatus(CoopPhase.Failed, room.Code, Error: room.Error),
            _ => new CoopStatus(CoopPhase.Connecting, room.Code),
        },
        _ when _finding is not null => new CoopStatus(CoopPhase.Connecting, _findingCode),
        _ when _failure is not null => new CoopStatus(CoopPhase.Failed, Error: _failure),
        _ => new CoopStatus(CoopPhase.None),
    };

    /// <inheritdoc/>
    public IReadOnlyList<CoopRoomInfo>? Listing => _rooms;

    /// <inheritdoc/>
    public string? ListingError => _listingError;

    private string Protocol => MultiplayerProtocol.IdFor(_titleId()).ToString("x16", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public void Host(int missionId, int difficulty, bool listed)
    {
        Leave();
        SessionArguments arguments = new(
            Connect: null, Listen: false, Port: 0, PlayerName: _playerName(), ServerName: $"{_playerName()}'s game", MaxPlayers: 3,
            Password: string.Empty, Mode: "coop", Host: true, Public: listed);
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

        _findingCode = code.Trim().ToUpperInvariant();
        _finding = RoomDirectory.FindAsync(RoomTransport.DefaultMaster, _findingCode);
    }

    /// <inheritdoc/>
    public void Leave()
    {
        _finding = null;
        _findingCode = null;
        _failure = null;
        if (_session is not null)
        {
            _observe(null);
            _session.Dispose();
            _session = null;
        }
    }

    /// <inheritdoc/>
    public void RefreshListing()
    {
        if (_listing is { IsCompleted: false })
        {
            return;
        }

        _rooms = null;
        _listingError = null;
        _listing = RoomDirectory.ListAsync(RoomTransport.DefaultMaster, _titleId(), Protocol);
    }

    /// <summary>Acts on what the master server answered since the last frame.</summary>
    public void Update()
    {
        if (_listing is { IsCompleted: true } listing)
        {
            _listing = null;
            if (listing.IsCompletedSuccessfully)
            {
                _rooms = [.. listing.Result.Select(r => new CoopRoomInfo(r.Code, r.Name, r.Mission() ?? 0, r.Players, r.MaxPlayers, r.Passworded))];
            }
            else
            {
                _rooms = [];
                _listingError = "couldn't reach the room list";
            }
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
                Shell?.StartMission(mission, _difficulty());
            }
        }
    }

    /// <inheritdoc/>
    public void PreviewLook(string? skinId) => _preview(skinId);

    /// <inheritdoc/>
    public void Dispose() => Leave();

    private void Begin(IDisposable? session)
    {
        _session = session;
        _observe(session as IHostTickObserver);
    }
}
