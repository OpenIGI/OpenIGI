using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// A server's files fetched on a connection of their own that asks only for them (<see cref="JoinRequest.Sync"/>): the list of
/// the map it plays checked against this game's, the pieces that differ fetched, and the connection let go.
/// </summary>
/// <remarks>
/// <para>
/// What a client does before it joins, so the level -- and the tables a game reads once -- load from the server's files. Also
/// what the menu does for a line the player has picked and not yet joined: the files come into the kept cache meanwhile, and
/// the join that follows finds them there and fetches nothing.
/// </para>
/// </remarks>
internal sealed class ServerFileFetch : IDisposable
{
    private const string LogCategory = "Files";

    private readonly INetTransport _transport;
    private readonly bool _ownsTransport;
    private readonly NetAddress _server;
    private readonly JoinRequest _request;
    private readonly string _titleId;
    private readonly Net.Identity.IPlayerIdentity? _identity;
    private readonly IOpenIgiLogger _logger;
    private readonly HeldFileList _held;
    private NetClient? _net;
    private ClientSession? _session;
    private double _since;

    /// <summary>Initializes a new instance of the <see cref="ServerFileFetch"/> class.</summary>
    /// <param name="transport">What the connection runs on.</param>
    /// <param name="ownsTransport">Whether the fetch disposes it when done: a line opened for the fetch alone.</param>
    /// <param name="server">The server.</param>
    /// <param name="request">The join request the connection is made with, asking only for the files.</param>
    /// <param name="titleId">The game's id, which keys the protocol.</param>
    /// <param name="files">This game's side of the files.</param>
    /// <param name="mapFilesInPlace">Whether a map's own files are where the game reads them, or <see langword="null"/> when they always are.</param>
    /// <param name="logger">Where its progress is told.</param>
    /// <param name="identity">Who the player is, or <see langword="null"/> for the game's own.</param>
    public ServerFileFetch(
        INetTransport transport,
        bool ownsTransport,
        NetAddress server,
        JoinRequest request,
        string titleId,
        SessionFileSync files,
        Func<string, bool>? mapFilesInPlace,
        IOpenIgiLogger logger,
        Net.Identity.IPlayerIdentity? identity = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _ownsTransport = ownsTransport;
        _server = server;
        _request = (request ?? throw new ArgumentNullException(nameof(request))) with { Sync = true };
        _titleId = titleId ?? throw new ArgumentNullException(nameof(titleId));
        Files = files ?? throw new ArgumentNullException(nameof(files));
        _held = new HeldFileList(mapFilesInPlace);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _identity = identity;
    }

    /// <summary>Gets this game's side of the files.</summary>
    public SessionFileSync Files { get; }

    /// <summary>Gets whether every file is this game's or in place, the connection let go.</summary>
    public bool Done { get; private set; }

    /// <summary>Gets why the files could not be had, or <see langword="null"/>.</summary>
    public string? Ended { get; private set; }

    /// <summary>Reads what came, asks for what differs, and lets the connection go once the files are in or cannot be.</summary>
    /// <param name="now">The time, in seconds.</param>
    /// <param name="where">The server as the player knows it, for what is told.</param>
    public void Poll(double now, string where)
    {
        if (Done || Ended is not null)
        {
            return;
        }

        if (_transport is RoomTransport { Status: not RoomStatus.Open } room)
        {
            room.Poll();
            if (room.Status == RoomStatus.Closed)
            {
                End(now, room.Error ?? "the room closed");
                _logger.Warning(LogCategory, $"Could not reach {where} for its files: {Ended}.");
            }

            return;
        }

        if (_session is null)
        {
            _net = new NetClient(_transport, MultiplayerProtocol.IdFor(_titleId), MultiplayerProtocol.Version);
            _session = new ClientSession(_net, 1, _identity, MultiplayerProtocol.IdFor(_titleId));
            _session.Connect(_server, _request, now);
            _since = now;
            _logger.Information(LogCategory, $"Checking {where}'s files.");
        }

        _session.Poll(now);
        _session.Receive(now);
        _held.Check(Files, _session.TakeFileList(), _session.WantFiles);
        Files.TakeTables(_session.TakeFileTables(), _session.WantChunks);
        Files.Take(_session.TakeFilePieces());
        _session.Flush(now);

        if (Files.Failure is { } failure)
        {
            End(now, $"couldn't get the server's files: {failure}");
        }
        else if (Files.Settled)
        {
            Close(now, "files in");
            Done = true;
        }
        else if (_session.EndReason is { } reason)
        {
            End(now, reason);
            _logger.Warning(LogCategory, $"{where} would not give its files: {reason}.");
        }
        else if (now - _since > ServerSession.SyncSeconds)
        {
            End(now, "the server's files took too long to come");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Close(0, "closed");
        if (_ownsTransport)
        {
            (_transport as IDisposable)?.Dispose();
        }
    }

    private void End(double now, string reason)
    {
        Close(now, reason);
        Ended = reason;
    }

    // The connection let go; the line it ran on stays for whoever owns it.
    private void Close(double now, string reason)
    {
        _net?.Disconnect(reason, now);
        _net = null;
        _session = null;
    }
}

/// <summary>
/// A list of a server's files held until its map's own files are where the game reads them: a browser holds a level's large
/// files only once it is asked for, and a list checked before then would be refused for files the game is still to be given.
/// </summary>
/// <param name="mapFilesInPlace">Whether a map's own files are in place, or <see langword="null"/> when they always are.</param>
internal sealed class HeldFileList(Func<string, bool>? mapFilesInPlace)
{
    private SessionFileList? _held;

    /// <summary>Checks the newest list that came, or the one held, once its map's files are in place.</summary>
    /// <param name="files">This game's side of the files.</param>
    /// <param name="came">A list that came now, or <see langword="null"/>.</param>
    /// <param name="want">Sends the request for files of the list.</param>
    public void Check(SessionFileSync files, SessionFileList? came, Action<string, IReadOnlyCollection<int>> want)
    {
        _held = came ?? _held;
        if (_held is { } list && mapFilesInPlace?.Invoke(list.Map) != false)
        {
            _held = null;
            files.Accept(list, want);
        }
    }
}
