using System.Globalization;

namespace OpenIGI.Releases.Accounts;

/// <summary>What a <see cref="MissionBoardFeed"/> reads its boards through.</summary>
public interface IRunBoardReader
{
    /// <summary>A board.</summary>
    /// <param name="title">The game.</param>
    /// <param name="mission">The mission, or null for every mission together.</param>
    /// <param name="difficulty">The difficulty.</param>
    /// <param name="ruleset">The rules.</param>
    /// <param name="metric">time or rank.</param>
    /// <param name="you">The asking player's account, or null.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The board.</returns>
    Task<RunBoard> BoardAsync(string title, int? mission, int difficulty, string ruleset, string metric, string? you, CancellationToken cancel);
}

/// <summary>
/// The single-player boards a page shows, as the launcher and the game's menu read them: which game, which mission (or all of
/// them), which difficulty, which rules and which metric, and the board for that, kept while fresh and asked for again when it
/// is old (<see cref="PolledCache{T}"/>). The page calls <see cref="Update"/> each frame and draws again when
/// <see cref="Revision"/> moves.
/// </summary>
/// <param name="reader">What the boards are read through.</param>
/// <param name="title">The game shown first.</param>
/// <param name="now">The clock; the system's when omitted.</param>
public sealed class MissionBoardFeed(IRunBoardReader reader, string title, Func<DateTimeOffset>? now = null)
{
    /// <summary>The rules the boards rank, by id, as they are offered.</summary>
    public static readonly IReadOnlyList<string> Rulesets = ["classic", "modern"];

    /// <summary>The metrics the boards rank by, by id, as they are offered: the fastest, then the best rank.</summary>
    public static readonly IReadOnlyList<string> Metrics = ["time", "rank"];

    private readonly PolledCache<RunBoard> _boards = new(TimeSpan.FromSeconds(30), now ?? (() => DateTimeOffset.UtcNow));
    private string? _you;

    /// <summary>Gets the game shown.</summary>
    public string Title { get; private set; } = title;

    /// <summary>Gets the mission shown, or null for every mission together.</summary>
    public int? Mission { get; private set; }

    /// <summary>Gets the difficulty shown, nought to two.</summary>
    public int Difficulty { get; private set; } = 2;

    /// <summary>Gets the rules shown, by id.</summary>
    public string Ruleset { get; private set; } = Rulesets[0];

    /// <summary>Gets the metric shown, by id.</summary>
    public string Metric { get; private set; } = Metrics[0];

    /// <summary>Gets or sets the account whose own row the board pins, or null for none.</summary>
    public string? You
    {
        get => _you;
        set
        {
            if (_you != value)
            {
                _you = value;
                _boards.Clear();
                Revision++;
            }
        }
    }

    /// <summary>Gets a number that moves whenever anything shown changes.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the board shown, or null while it has not been read.</summary>
    public RunBoard? Board => _boards.Get(Key);

    /// <summary>Gets how the board shown stands.</summary>
    public FeedState BoardState => _boards.State(Key);

    private string Key => string.Create(CultureInfo.InvariantCulture, $"{Title}/{Mission?.ToString(CultureInfo.InvariantCulture) ?? "all"}/{Difficulty}/{Ruleset}/{Metric}");

    /// <summary>Shows another game's boards, from every mission together.</summary>
    /// <param name="title">The game.</param>
    public void ShowTitle(string title)
    {
        if (Title != title)
        {
            Title = title;
            Mission = null;
            Revision++;
        }
    }

    /// <summary>Shows one mission's board, or every mission's together.</summary>
    /// <param name="mission">The mission, or null.</param>
    public void ShowMission(int? mission) => Set(() => Mission = mission, Mission != mission);

    /// <summary>Shows another difficulty's board.</summary>
    /// <param name="difficulty">The difficulty, nought to two.</param>
    public void ShowDifficulty(int difficulty) => Set(() => Difficulty = Math.Clamp(difficulty, 0, 2), Difficulty != difficulty);

    /// <summary>Shows another rules' board.</summary>
    /// <param name="ruleset">The rules, by id.</param>
    public void ShowRuleset(string ruleset) => Set(() => Ruleset = Rulesets.Contains(ruleset) ? ruleset : Rulesets[0], Ruleset != ruleset);

    /// <summary>Ranks by another metric.</summary>
    /// <param name="metric">The metric, by id.</param>
    public void ShowMetric(string metric) => Set(() => Metric = Metrics.Contains(metric) ? metric : Metrics[0], Metric != metric);

    /// <summary>Forgets the board shown, for it to be read again now.</summary>
    public void Refresh()
    {
        _boards.Forget(Key);
        Revision++;
    }

    /// <summary>Takes in what has come back, and asks for the board shown where it is missing or old.</summary>
    /// <param name="watching">Whether the page is up: nothing is asked for while it isn't.</param>
    public void Update(bool watching)
    {
        if (_boards.Collect())
        {
            Revision++;
        }

        if (watching && _boards.Ask(Key, Read(Title, Mission, Difficulty, Ruleset, Metric, You)))
        {
            Revision++;
        }
    }

    private Func<Task<RunBoard>> Read(string title, int? mission, int difficulty, string ruleset, string metric, string? you) =>
        () => reader.BoardAsync(title, mission, difficulty, ruleset, metric, you, CancellationToken.None);

    private void Set(Action change, bool changes)
    {
        if (changes)
        {
            change();
            Revision++;
        }
    }
}

/// <summary>The runs service as the boards' reader.</summary>
/// <param name="runs">The runs.</param>
public sealed class MasterRunBoardReader(RunsService runs) : IRunBoardReader
{
    /// <inheritdoc/>
    public Task<RunBoard> BoardAsync(string title, int? mission, int difficulty, string ruleset, string metric, string? you, CancellationToken cancel) =>
        runs.BoardAsync(title, mission, difficulty, ruleset, metric, you, cancel);
}
