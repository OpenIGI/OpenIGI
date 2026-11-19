using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Host.Multiplayer;

/// <content>
/// The server's files, made this game's before it plays: a connection of their own before the join (<see cref="ServerFileFetch"/>),
/// then each list that comes with a new map while it plays.
/// </content>
public sealed partial class SessionClient
{
    // How long the files may take before the player is told they are coming: most joins find them in place, or nearly.
    private const double QuietFilesSeconds = 1;

    // The files before the join, while they come, and when they began.
    private ServerFileFetch? _fetch;
    private double? _fetchSince;
    private bool _synced;

    // A list that came while playing for a map whose files are not yet where the game reads them, kept until they are.
    private HeldFileList? _held;

    /// <summary>Gets or sets the client's side of the server's files, or <see langword="null"/> to take the game's own as they are.</summary>
    /// <remarks>Set by the host as the session begins, before anything is loaded: until the files are in, the join waits.</remarks>
    public SessionFileSync? Files { get; set; }

    /// <summary>Gets or sets whether a map's own files are where the game reads them, or <see langword="null"/> when they always are.</summary>
    /// <remarks>
    /// A browser holds a level's large files only once that level has been asked for, and asking is what fetches them. A list
    /// is checked against this game's files, so one that came for a map not yet fetched waits rather than being refused for a
    /// file the game is still to be given.
    /// </remarks>
    public Func<string, bool>? MapFilesInPlace { get; set; }

    /// <summary>Gets whether the server's files are in -- or none are compared -- so the level may be loaded and the join made.</summary>
    public bool FilesReady => Files is null || _synced || _ended is not null;

    /// <summary>Gets a line on how the files are coming, once they have taken long enough to be worth a word, or <see langword="null"/>.</summary>
    public string? FilesStatus
    {
        get
        {
            if (Files is not { } files || FilesReady || _fetchSince is not { } since || _clock.Elapsed.TotalSeconds - since < QuietFilesSeconds)
            {
                return null;
            }

            return files.Progress is { Total: > 0 } progress
                ? $"Getting the server's files: {progress.Received * 100 / progress.Total}% of {progress.Total / 1024} KiB"
                : "Checking the server's files";
        }
    }

    // Before the join: the files on a connection of their own over the same line, let go once they are in, so the join that
    // follows loads the level -- and the tables a game reads once -- from the server's.
    private void SyncBeforeJoin(double now)
    {
        if (Files is not { } files || _synced || _ended is not null || _session is not null)
        {
            return;
        }

        _fetchSince ??= now;
        _fetch ??= new ServerFileFetch(_transport, ownsTransport: false, _server, _request, _titleId, files, MapFilesInPlace, _logger, _identity);
        _fetch.Poll(now, Where);
        if (_fetch.Ended is { } ended)
        {
            _ended = ended;
            _logger.Warning(LogCategory, $"The join to {Where} stopped: {ended}.");
            EndFetch();
        }
        else if (_fetch.Done)
        {
            _synced = true;
            EndFetch();
        }
    }

    // The files' own connection let go; the line it ran on stays for the join.
    private void EndFetch()
    {
        _fetch?.Dispose();
        _fetch = null;
    }

    // While playing: a list come with a new map checked, what differs fetched, and a level loaded from files no longer the
    // server's loaded again.
    private void SyncWhilePlaying(ClientSession session)
    {
        if (Files is not { } files)
        {
            return;
        }

        _held ??= new HeldFileList(MapFilesInPlace);
        _held.Check(files, session.TakeFileList(), session.WantFiles);

        files.TakeTables(session.TakeFileTables(), session.WantChunks);
        files.Take(session.TakeFilePieces());
        if (files.Failure is { } failure && _ended is null)
        {
            // A list sent ahead of the next map is only a head start: the map's own list, as the server goes there, decides.
            if (files.Map is { } ahead && ahead != session.Map && ahead != session.GoingTo)
            {
                _logger.Information(LogCategory, $"The files of map {ahead}, sent ahead, are left for when the server goes there: {failure}.");
                files.Abandon();
                return;
            }

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

    // Whether the level to go to waits for its files: those of the map the server is going on to, not a list sent ahead of a
    // later one, which only ever gives a head start.
    private bool FilesBusy => Files is { Settled: false, Failure: null, Map: { } map } && (map == _session?.GoingTo || map == _session?.Map);
}
