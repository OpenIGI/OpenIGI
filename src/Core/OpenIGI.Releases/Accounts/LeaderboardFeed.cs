namespace OpenIGI.Releases.Accounts;

/// <summary>Where a board or a record being read stands.</summary>
public enum FeedState
{
    /// <summary>Nothing asked for yet.</summary>
    Idle,

    /// <summary>Asked for; nothing to show yet.</summary>
    Loading,

    /// <summary>Read; refreshed in the background as it grows old.</summary>
    Ready,

    /// <summary>The master couldn't be reached or refused, and nothing was read before.</summary>
    Failed,
}

/// <summary>A player's record as a page shows it: the record, the account it is under, and their place this season.</summary>
/// <param name="Id">Who was asked about: an account's id or a key's.</param>
/// <param name="Record">The record.</param>
/// <param name="Account">Their Discord account, or <see langword="null"/> for a key on its own (or when it couldn't be read).</param>
/// <param name="Placed">Their row on this season's board by score, wherever they stand; <see langword="null"/> unranked.</param>
public sealed record RecordView(string Id, PlayerRecord Record, PlayerAccount? Account, BoardRow? Placed)
{
    /// <summary>Gets the name shown: the account's, the board's, the one they last played under, or <c>Operative</c>.</summary>
    public string Name => Account?.Name
        ?? Placed?.Name
        ?? Record.Latest?.Select(static match => match.Line?.Name).FirstOrDefault(static name => !string.IsNullOrEmpty(name))
        ?? "Operative";

    /// <summary>Gets the avatar's address, or <see langword="null"/>.</summary>
    public string? Avatar => Account?.Avatar is { Length: > 0 } avatar ? avatar : Placed?.Account?.Avatar;
}

/// <summary>What a <see cref="LeaderboardFeed"/> reads from the master.</summary>
public interface ILeaderboardReader
{
    /// <summary>Reads a game's board (<see cref="StatsService.BoardAsync"/>).</summary>
    /// <param name="title">The game.</param>
    /// <param name="period">The period.</param>
    /// <param name="metric">The metric.</param>
    /// <param name="you">The player whose own place is wanted, or <see langword="null"/>.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The board.</returns>
    Task<Leaderboard> BoardAsync(string title, string period, string metric, string? you, CancellationToken cancel);

    /// <summary>Reads a player's record in a game (<see cref="StatsService.RecordAsync"/>).</summary>
    /// <param name="player">The player.</param>
    /// <param name="title">The game.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The record.</returns>
    Task<PlayerRecord> RecordAsync(string player, string title, CancellationToken cancel);

    /// <summary>Reads a player's account (<see cref="AccountService.PlayerAsync"/>).</summary>
    /// <param name="player">The player.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The account, or <see langword="null"/>.</returns>
    Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel);
}

/// <summary>
/// The official servers' leaderboards and players' records as the launcher and the game's menu show them: which game, period
/// and metric are picked, the board for them and the record opened, each read from the master when first wanted and again once
/// thirty seconds old (as long as the master keeps a board), and kept meanwhile so going back and forth asks nothing. Polled a
/// frame at a time, as the account is: nothing blocks, and nothing is asked while no page looks.
/// </summary>
public sealed class LeaderboardFeed
{
    /// <summary>How long a board or record read is shown before it is read again.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    private readonly ILeaderboardReader _reader;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (Leaderboard Value, DateTimeOffset At)> _boards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (RecordView Value, DateTimeOffset At)> _records = new(StringComparer.Ordinal);

    // What couldn't be read, and when: shown as failed, and tried again once that is as old as a board.
    private readonly Dictionary<string, DateTimeOffset> _failedBoards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _failedRecords = new(StringComparer.Ordinal);

    private (string Key, Task<Leaderboard> Task)? _boardAsked;
    private (string Key, Task<RecordView> Task)? _recordAsked;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardFeed"/> class.</summary>
    /// <param name="reader">What reads from the master.</param>
    /// <param name="title">The game shown first, by title id.</param>
    /// <param name="now">The clock; the system's by default.</param>
    public LeaderboardFeed(ILeaderboardReader reader, string title, Func<DateTimeOffset>? now = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        ArgumentException.ThrowIfNullOrEmpty(title);
        Title = title;
        _now = now ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>Gets the game shown, by title id.</summary>
    public string Title { get; private set; }

    /// <summary>Gets the period shown: <see cref="LeaderboardSeasons.Current"/>, <see cref="LeaderboardSeasons.AllTime"/> or a month.</summary>
    public string Period { get; private set; } = LeaderboardSeasons.Current;

    /// <summary>Gets the metric ranked by.</summary>
    public LeaderboardMetric Metric { get; private set; } = LeaderboardMetric.Default;

    /// <summary>Gets or sets who "you" are -- the signed-in account's id, else this computer's key's -- for the board's own row.</summary>
    public string? You
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Changed();
            }
        }
    }

    /// <summary>Gets the player whose record is open, or <see langword="null"/> while the board is shown.</summary>
    public string? RecordId { get; private set; }

    /// <summary>Gets a number that changes whenever anything shown changes, for a page to rebuild on.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the board for the game, period and metric picked, once read (the last one read for them while it is read again).</summary>
    public Leaderboard? Board => _boards.TryGetValue(BoardKey, out (Leaderboard Value, DateTimeOffset At) kept) ? kept.Value : null;

    /// <summary>Gets where the board stands.</summary>
    public FeedState BoardState => State(Board is not null, _boardAsked?.Key == BoardKey, _failedBoards.ContainsKey(BoardKey));

    /// <summary>Gets the open record, once read.</summary>
    public RecordView? Record => RecordKey is { } key && _records.TryGetValue(key, out (RecordView Value, DateTimeOffset At) kept) ? kept.Value : null;

    /// <summary>Gets where the open record stands.</summary>
    public FeedState RecordState => RecordKey is not { } key
        ? FeedState.Idle
        : State(Record is not null, _recordAsked?.Key == key, _failedRecords.ContainsKey(key));

    private string BoardKey => $"{Title}/{Period}/{Metric.Id}/{You}";

    private string? RecordKey => RecordId is { } id ? $"{Title}/{id}" : null;

    /// <summary>Shows another game's board (and record, when one is open).</summary>
    /// <param name="title">The game, by title id.</param>
    public void ShowTitle(string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        if (Title != title)
        {
            Title = title;

            // A season gone by is one game's: the other may never have had it.
            if (LeaderboardSeasons.IsMonth(Period))
            {
                Period = LeaderboardSeasons.Current;
            }

            Changed();
        }
    }

    /// <summary>Shows another period.</summary>
    /// <param name="period"><see cref="LeaderboardSeasons.Current"/>, <see cref="LeaderboardSeasons.AllTime"/> or a month.</param>
    public void ShowPeriod(string period)
    {
        ArgumentException.ThrowIfNullOrEmpty(period);
        if (Period != period)
        {
            Period = period;
            Changed();
        }
    }

    /// <summary>Ranks by another metric.</summary>
    /// <param name="metric">The metric.</param>
    public void ShowMetric(LeaderboardMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (Metric != metric)
        {
            Metric = metric;
            Changed();
        }
    }

    /// <summary>Opens a player's record.</summary>
    /// <param name="player">Their account's id or key's.</param>
    public void OpenRecord(string player)
    {
        ArgumentException.ThrowIfNullOrEmpty(player);
        if (RecordId != player)
        {
            RecordId = player;
            Changed();
        }
    }

    /// <summary>Closes the open record, back to the board.</summary>
    public void CloseRecord()
    {
        if (RecordId is not null)
        {
            RecordId = null;
            Changed();
        }
    }

    /// <summary>Reads again what is shown, now, whatever its age.</summary>
    public void Refresh()
    {
        _boards.Remove(BoardKey);
        _failedBoards.Remove(BoardKey);
        if (RecordKey is { } key)
        {
            _records.Remove(key);
            _failedRecords.Remove(key);
        }

        Changed();
    }

    /// <summary>Takes in what has come back, and asks for what is shown when it is missing or old.</summary>
    /// <param name="watching">Whether a page shows the feed: nothing is asked for while none does.</param>
    public void Update(bool watching)
    {
        CollectBoard();
        CollectRecord();
        if (!watching)
        {
            return;
        }

        DateTimeOffset now = _now();
        string boardKey = BoardKey;
        bool boardOld = !_boards.TryGetValue(boardKey, out (Leaderboard Value, DateTimeOffset At) board) || now - board.At > Fresh;
        if (boardOld && _boardAsked is null && !Failing(_failedBoards, boardKey, now))
        {
            _boardAsked = (boardKey, _reader.BoardAsync(Title, Period, Metric.Id, You, CancellationToken.None));
            Changed();
        }

        if (RecordKey is { } recordKey && RecordId is { } id && _recordAsked is null && !Failing(_failedRecords, recordKey, now)
            && (!_records.TryGetValue(recordKey, out (RecordView Value, DateTimeOffset At) record) || now - record.At > Fresh))
        {
            _recordAsked = (recordKey, ReadRecord(id, Title));
            Changed();
        }
    }

    private static bool Failing(Dictionary<string, DateTimeOffset> failed, string key, DateTimeOffset now) =>
        failed.TryGetValue(key, out DateTimeOffset at) && now - at <= Fresh;

    private static FeedState State(bool have, bool asking, bool failed) =>
        have ? FeedState.Ready : asking ? FeedState.Loading : failed ? FeedState.Failed : FeedState.Loading;

    // The record, the account it is under and its place this season, together; an account or a place that can't be read is
    // left out rather than failing the record.
    private async Task<RecordView> ReadRecord(string id, string title)
    {
        Task<PlayerRecord> record = _reader.RecordAsync(id, title, CancellationToken.None);
        Task<PlayerAccount?> account = _reader.PlayerAsync(id, CancellationToken.None);
        Task<Leaderboard> placed = _reader.BoardAsync(title, LeaderboardSeasons.Current, LeaderboardMetric.Default.Id, id, CancellationToken.None);
        PlayerRecord read = await record.ConfigureAwait(false);
        return new RecordView(id, read, await Quietly(account).ConfigureAwait(false), (await Quietly(placed).ConfigureAwait(false))?.You);
    }

    private static async Task<T?> Quietly<T>(Task<T> task)
        where T : class?
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AccountRefusedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void CollectBoard()
    {
        if (Collect(_boardAsked, _boards, _failedBoards))
        {
            _boardAsked = null;
        }
    }

    private void CollectRecord()
    {
        if (Collect(_recordAsked, _records, _failedRecords))
        {
            _recordAsked = null;
        }
    }

    // A finished request taken in: kept when read; marked failed when it wasn't and nothing was read before; else what was read
    // shown on, and tried again once it is old again. True when the request was finished.
    private bool Collect<T>((string Key, Task<T> Task)? asked, Dictionary<string, (T Value, DateTimeOffset At)> kept, Dictionary<string, DateTimeOffset> failed)
    {
        if (asked is not { Task.IsCompleted: true } done)
        {
            return false;
        }

        if (done.Task.IsCompletedSuccessfully)
        {
            kept[done.Key] = (done.Task.Result, _now());
            failed.Remove(done.Key);
        }
        else if (kept.TryGetValue(done.Key, out (T Value, DateTimeOffset At) before))
        {
            kept[done.Key] = (before.Value, _now());
        }
        else
        {
            failed[done.Key] = _now();
        }

        Changed();
        return true;
    }

    private void Changed() => Revision++;
}

/// <summary>The master's own stats and accounts, read for a <see cref="LeaderboardFeed"/>.</summary>
/// <param name="stats">The stats.</param>
/// <param name="accounts">The accounts.</param>
public sealed class MasterLeaderboardReader(StatsService stats, AccountService accounts) : ILeaderboardReader
{
    /// <summary>Initializes a new instance of the <see cref="MasterLeaderboardReader"/> class on one client and master.</summary>
    /// <param name="http">The client to ask through.</param>
    /// <param name="master">The master server.</param>
    public MasterLeaderboardReader(HttpClient http, Uri master)
        : this(new StatsService(http, master), new AccountService(http, master))
    {
    }

    /// <inheritdoc/>
    public Task<Leaderboard> BoardAsync(string title, string period, string metric, string? you, CancellationToken cancel) =>
        stats.BoardAsync(title, period, metric, you, cancel);

    /// <inheritdoc/>
    public Task<PlayerRecord> RecordAsync(string player, string title, CancellationToken cancel) => stats.RecordAsync(player, title, cancel);

    /// <inheritdoc/>
    public Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel) => accounts.PlayerAsync(player, cancel);
}
