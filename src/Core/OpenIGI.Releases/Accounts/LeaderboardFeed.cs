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

/// <summary>
/// A player's record as a page shows it: the record, the account it is under, their place this season, and their standing on
/// the single-player boards.
/// </summary>
/// <param name="Id">Who was asked about: an account's id or a key's.</param>
/// <param name="Record">The record.</param>
/// <param name="Account">Their Discord account, or <see langword="null"/> for a key on its own (or when it couldn't be read).</param>
/// <param name="Placed">Their row on this season's board by score, wherever they stand; <see langword="null"/> unranked.</param>
/// <param name="Standing">Their standing on the mission boards; <see langword="null"/> where they have no runs (or it couldn't be read).</param>
/// <param name="Ahead">The row just above theirs this season, by score; <see langword="null"/> where they lead or are unranked.</param>
public sealed record RecordView(string Id, PlayerRecord Record, PlayerAccount? Account, BoardRow? Placed, MissionStanding? Standing = null, BoardRow? Ahead = null)
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

    /// <summary>Reads a player's standing on a game's mission boards; none where the build reads no runs.</summary>
    /// <param name="player">The player: their account's id (a key alone has no runs).</param>
    /// <param name="title">The game.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The standing, or <see langword="null"/> where they have none.</returns>
    Task<MissionStanding?> StandingAsync(string player, string title, CancellationToken cancel) => Task.FromResult<MissionStanding?>(null);
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

    // The boards and records read, each asked for once it is old, a failure tried again once it is as old as a board.
    private readonly PolledCache<Leaderboard> _boards;
    private readonly PolledCache<RecordView> _records;

    /// <summary>Initializes a new instance of the <see cref="LeaderboardFeed"/> class.</summary>
    /// <param name="reader">What reads from the master.</param>
    /// <param name="title">The game shown first, by title id.</param>
    /// <param name="now">The clock; the system's by default.</param>
    public LeaderboardFeed(ILeaderboardReader reader, string title, Func<DateTimeOffset>? now = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        ArgumentException.ThrowIfNullOrEmpty(title);
        Title = title;
        Func<DateTimeOffset> clock = now ?? (static () => DateTimeOffset.UtcNow);
        _boards = new PolledCache<Leaderboard>(Fresh, clock);
        _records = new PolledCache<RecordView>(Fresh, clock);
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
    public Leaderboard? Board => _boards.Get(BoardKey);

    /// <summary>Gets where the board stands.</summary>
    public FeedState BoardState => _boards.State(BoardKey);

    /// <summary>Gets the open record, once read.</summary>
    public RecordView? Record => RecordKey is { } key ? _records.Get(key) : null;

    /// <summary>Gets where the open record stands.</summary>
    public FeedState RecordState => RecordKey is { } key ? _records.State(key) : FeedState.Idle;

    private string BoardKey => $"{Title}/{Period}/{Metric.Id}/{You}";

    private string? RecordKey => RecordId is { } id ? $"{Title}/{id}" : null;

    /// <summary>Gets the player whose record is read for a glance without being opened (the board's selected row), or null.</summary>
    public string? PeekId { get; private set; }

    /// <summary>Gets the record read for a glance, or null while it is not read.</summary>
    public RecordView? Peeked => PeekKey is { } key ? _records.Get(key) : null;

    /// <summary>Gets how the record read for a glance stands.</summary>
    public FeedState PeekState => PeekKey is { } key ? _records.State(key) : FeedState.Idle;

    private string? PeekKey => PeekId is { } id ? $"{Title}/{id}" : null;

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

    /// <summary>Reads a player's record for a glance, without opening it: the board's selected row's; null for none.</summary>
    /// <param name="player">Their account's id or key's, or null.</param>
    public void Peek(string? player)
    {
        if (PeekId != player)
        {
            PeekId = player;
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
        _boards.Forget(BoardKey);
        if (RecordKey is { } key)
        {
            _records.Forget(key);
        }

        Changed();
    }

    /// <summary>Takes in what has come back, and asks for what is shown when it is missing or old.</summary>
    /// <param name="watching">Whether a page shows the feed: nothing is asked for while none does.</param>
    public void Update(bool watching)
    {
        if (_boards.Collect() | _records.Collect())
        {
            Changed();
        }

        if (!watching)
        {
            return;
        }

        string title = Title;
        if (_boards.Ask(BoardKey, () => _reader.BoardAsync(title, Period, Metric.Id, You, CancellationToken.None)))
        {
            Changed();
        }

        if (RecordKey is { } recordKey && RecordId is { } id && _records.Ask(recordKey, () => ReadRecord(id, title)))
        {
            Changed();
        }

        if (PeekKey is { } peekKey && PeekId is { } peeked && _records.Ask(peekKey, () => ReadRecord(peeked, title)))
        {
            Changed();
        }
    }

    // The record, the account it is under and its place this season, together; an account or a place that can't be read is
    // left out rather than failing the record.
    private async Task<RecordView> ReadRecord(string id, string title)
    {
        Task<PlayerRecord> record = _reader.RecordAsync(id, title, CancellationToken.None);
        Task<PlayerAccount?> account = _reader.PlayerAsync(id, CancellationToken.None);
        Task<Leaderboard> placed = _reader.BoardAsync(title, LeaderboardSeasons.Current, LeaderboardMetric.Default.Id, id, CancellationToken.None);
        Task<MissionStanding?> standing = _reader.StandingAsync(id, title, CancellationToken.None);
        PlayerRecord read = await record.ConfigureAwait(false);
        Leaderboard? board = await Quietly(placed).ConfigureAwait(false);
        return new RecordView(id, read, await Quietly(account).ConfigureAwait(false), board?.You, await Quietly(standing).ConfigureAwait(false), board?.Ahead);
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

    private void Changed() => Revision++;
}

/// <summary>The master's own stats, accounts and runs, read for a <see cref="LeaderboardFeed"/>.</summary>
/// <param name="stats">The stats.</param>
/// <param name="accounts">The accounts.</param>
/// <param name="runs">The runs, for a record's standing on the mission boards; null for none.</param>
public sealed class MasterLeaderboardReader(StatsService stats, AccountService accounts, RunsService? runs = null) : ILeaderboardReader
{
    /// <summary>Initializes a new instance of the <see cref="MasterLeaderboardReader"/> class on one client and master.</summary>
    /// <param name="http">The client to ask through.</param>
    /// <param name="master">The master server.</param>
    public MasterLeaderboardReader(HttpClient http, Uri master)
        : this(new StatsService(http, master), new AccountService(http, master), new RunsService(http, master))
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Their bests at the difficulty and rules they have run the most missions on (<see cref="MissionStanding.Pick"/>), and their
    /// row on that overall board by time.
    /// </remarks>
    public async Task<MissionStanding?> StandingAsync(string player, string title, CancellationToken cancel)
    {
        // Runs are an account's: a key on its own has none.
        if (runs is null || !player.All(char.IsAsciiDigit))
        {
            return null;
        }

        IReadOnlyList<PlayerBest> bests = await runs.BestsAsync(player, title, cancel).ConfigureAwait(false);
        if (MissionStanding.Pick(bests) is not var (difficulty, ruleset))
        {
            return null;
        }

        RunBoard overall = await runs.BoardAsync(title, null, difficulty, ruleset, MissionBoardFeed.Metrics[0], player, cancel).ConfigureAwait(false);
        return new MissionStanding(
            difficulty,
            ruleset,
            [.. bests.Where(best => best.Difficulty == difficulty && best.Ruleset == ruleset).OrderBy(static best => best.Mission)],
            overall.You);
    }

    /// <inheritdoc/>
    public Task<Leaderboard> BoardAsync(string title, string period, string metric, string? you, CancellationToken cancel) =>
        stats.BoardAsync(title, period, metric, you, cancel);

    /// <inheritdoc/>
    public Task<PlayerRecord> RecordAsync(string player, string title, CancellationToken cancel) => stats.RecordAsync(player, title, cancel);

    /// <inheritdoc/>
    public Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel) => accounts.PlayerAsync(player, cancel);
}
