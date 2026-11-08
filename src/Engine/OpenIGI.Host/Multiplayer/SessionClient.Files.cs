using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <content>
/// The server's files, made this game's before it plays: a connection of their own before the join, then each list that
/// comes with a new map while it plays.
/// </content>
public sealed partial class SessionClient
{
    // The longest the files before the join are waited for, in seconds; as long as the server keeps such a connection.
    private const double LongestSyncSeconds = ServerSession.SyncSeconds;

    // The connection the files come on before the join, while it lasts, and when it began.
    private NetClient? _syncNet;
    private ClientSession? _syncSession;
    private double _syncSince;
    private bool _synced;

    /// <summary>Gets or sets the client's side of the server's files, or <see langword="null"/> to take the game's own as they are.</summary>
    /// <remarks>Set by the host as the session begins, before anything is loaded: until the files are in, the join waits.</remarks>
    public SessionFileSync? Files { get; set; }

    /// <summary>Gets whether the server's files are in -- or none are compared -- so the level may be loaded and the join made.</summary>
    public bool FilesReady => Files is null || _synced || _ended is not null;

    /// <summary>Gets a line on how the files are coming, while they are, or <see langword="null"/>.</summary>
    public string? FilesStatus => Files is { } files && !FilesReady && files.Progress is { Total: > 0 } progress
        ? $"Getting the server's files: {progress.Received * 100 / progress.Total}% of {progress.Total / 1024} KiB"
        : Files is not null && !FilesReady ? "Checking the server's files" : null;

    // Before the join: the files on a connection of their own, which is let go once they are in, so the join that follows
    // loads the level -- and the tables a game reads once -- from the server's.
    private void SyncBeforeJoin(double now)
    {
        if (Files is not { } files || _synced || _ended is not null || _session is not null)
        {
            return;
        }

        if (_transport is RoomTransport { Status: not RoomStatus.Open } room)
        {
            room.Poll();
            if (room.Status == RoomStatus.Closed)
            {
                _ended = room.Error ?? "the room closed";
                _logger.Warning(LogCategory, $"Could not reach {Where} for its files: {_ended}.");
            }

            return;
        }

        if (_syncSession is null)
        {
            _syncNet = new NetClient(_transport, MultiplayerProtocol.IdFor(_titleId), MultiplayerProtocol.Version);
            _syncSession = new ClientSession(_syncNet, 1, _identity, MultiplayerProtocol.IdFor(_titleId));
            _syncSession.Connect(_server, _request with { Sync = true }, now);
            _syncSince = now;
            _logger.Information(LogCategory, $"Checking {Where}'s files before joining.");
        }

        _syncSession.Poll(now);
        _syncSession.Receive(now);
        if (_syncSession.TakeFileList() is { } list)
        {
            files.Accept(list, _syncSession.WantFiles);
        }

        files.Take(_syncSession.TakeFilePieces());
        _syncSession.Flush(now);

        if (files.Failure is { } failure)
        {
            EndSync(now);
            _ended = $"couldn't get the server's files: {failure}";
            _logger.Warning(LogCategory, $"The join to {Where} stopped: {_ended}.");
        }
        else if (files.Settled)
        {
            EndSync(now);
            _synced = true;
        }
        else if (_syncSession.EndReason is { } reason)
        {
            EndSync(now);
            _ended = reason;
            _logger.Warning(LogCategory, $"{Where} would not give its files: {reason}.");
        }
        else if (now - _syncSince > LongestSyncSeconds)
        {
            EndSync(now);
            _ended = "the server's files took too long to come";
            _logger.Warning(LogCategory, $"The join to {Where} stopped: {_ended}.");
        }
    }

    // The files' own connection let go; the line it ran on stays for the join.
    private void EndSync(double now)
    {
        _syncNet?.Disconnect("files in", now);
        _syncNet = null;
        _syncSession = null;
    }

    // While playing: a list come with a new map checked, what differs fetched, and a level loaded from files no longer the
    // server's loaded again.
    private void SyncWhilePlaying(ClientSession session)
    {
        if (Files is not { } files)
        {
            return;
        }

        if (session.TakeFileList() is { } list)
        {
            files.Accept(list, session.WantFiles);
        }

        files.Take(session.TakeFilePieces());
        if (files.Failure is { } failure && _ended is null)
        {
            _ended = $"couldn't get the server's files: {failure}";
            _logger.Warning(LogCategory, $"Left {Where}: {_ended}.");
            return;
        }

        (bool changed, bool shared) = files.TakeChanges();
        if (!changed)
        {
            return;
        }

        if (shared)
        {
            _logger.Warning(LogCategory, "The server's shared files changed during play; they take effect from the next join.");
        }

        // A level up here already was loaded from what differed: loaded again, from the server's.
        if (_level is not null && files.Map == session.Map)
        {
            _logger.Information(LogCategory, $"Map {session.Map} is loaded again from the server's files.");
            session.ReloadMap();
        }
    }

    // Whether the level to go to waits for its files.
    private bool FilesBusy => Files is { Settled: false, Failure: null, Map: not null };
}
