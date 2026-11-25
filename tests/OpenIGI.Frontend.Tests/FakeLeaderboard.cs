using System.Globalization;
using OpenIGI.Releases.Accounts;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Leaderboards;

namespace OpenIGI.Frontend.Tests;

/// <summary>Leaderboards that answer at once from made-up players, for the Leaderboard page's tests and pictures.</summary>
internal sealed class FakeLeaderboard : ILeaderboardSource
{
    private static readonly string[] Names =
    [
        "Jones", "Anya", "Priboi", "Ekk", "Night Owl", "Ghost of Kursk", "Marlin", "Tavish", "Sasha", "Ivo", "Kestrel", "Rook",
        "Dmitri", "Lena", "Hawk", "Viper", "Oksana", "Brody", "Mako", "Yuri", "Quill", "Taras", "Wren", "Zeno",
    ];

    public FakeLeaderboard(FakeBoardReader reader, string title = "igi1")
    {
        Feed = new LeaderboardFeed(reader, title) { You = "aaaaaaaaaaaaaaaa" };
        Missions = new MissionBoardFeed(new FakeRunBoards(reader.RowCount), title) { You = "900000000000000004" };
    }

    public LeaderboardFeed Feed { get; }

    public MissionBoardFeed Missions { get; }

    public List<(string Run, bool Race)> Played { get; } = [];

    public void Watch(string run) => Played.Add((run, false));

    public void Race(string run) => Played.Add((run, true));

    public IReadOnlyList<LeaderboardGame> Games => LeaderboardGame.Official;

    public string? SignInHint { get; set; } = "Sign in with Discord on the Profile page to be on the board under your name.";

    public bool CanOpenLinks => true;

    public List<string> Opened { get; } = [];

    public UiPicture? Avatar(string? url) => null;

    public string MapName(string title, string map) => LeaderboardGame.MapName(title, map);

    public void OpenLink(string url) => Opened.Add(url);

    /// <summary>A board of so many rows, best first by score.</summary>
    public static IReadOnlyList<BoardRow> Rows(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i =>
        {
            int kills = 420 - (i * 15);
            int deaths = 90 + (i * 4);
            int assists = 60 - i;
            int wins = 30 - i;
            int matches = 48 - (i / 2);
            string id = (900000000000000000L + i).ToString(CultureInfo.InvariantCulture);
            BoardAccount? account = i % 3 == 2 ? null : new BoardAccount(id, Names[i % Names.Length]);
            return new BoardRow(i + 1, account?.Id ?? $"{i:x16}", account, Names[i % Names.Length], matches, wins, kills, deaths, assists, i % 5, matches * 600, (kills * 10) + (assists * 5) + (wins * 50), Math.Round(kills / (double)deaths, 2));
        }),
    ];
}

/// <summary>What the master would answer, made up; or nothing, or a failure.</summary>
internal sealed class FakeBoardReader : ILeaderboardReader
{
    public int RowCount { get; set; } = 24;

    public bool Fail { get; set; }

    public bool Hold { get; set; }

    public Task<Leaderboard> BoardAsync(string title, string period, string metric, string? you, CancellationToken cancel)
    {
        if (Hold)
        {
            return new TaskCompletionSource<Leaderboard>().Task;
        }

        if (Fail)
        {
            return Task.FromException<Leaderboard>(new HttpRequestException("down"));
        }

        LeaderboardMetric by = LeaderboardMetric.Of(metric);
        List<BoardRow> rows = [.. FakeLeaderboard.Rows(RowCount).OrderByDescending(row => by.Id == "kd" ? row.Kd : double.Parse(by.Figure(row), NumberStyles.AllowThousands, CultureInfo.InvariantCulture))];
        rows = [.. rows.Select((row, i) => row with { Rank = i + 1 })];
        BoardRow? own = you is null ? null : new BoardRow(212, you, null, "Operative", 9, 3, 41, 37, 6, 0, 5400, 715, 1.11);
        return Task.FromResult(new Leaderboard(title, period == "all" ? "all" : "season", period == "season" ? "2026-11" : period, metric, rows, own, rows.Count == 0 ? 0 : 1480, ["2026-11", "2026-10", "2026-09"]));
    }

    public Task<PlayerRecord> RecordAsync(string player, string title, CancellationToken cancel)
    {
        if (Fail)
        {
            return Task.FromException<PlayerRecord>(new HttpRequestException("down"));
        }

        StatTotals season = new(31, 19, 402, 98, 4, 19800, 6215, 4.1, 57);
        StatTotals all = new(140, 77, 1840, 520, 22, 88200, 27400, 3.54, 260);
        Medal[] medals =
        [
            new("first-kill", "First blood", "A kill on an official server."),
            new("first-win", "First win", "A match won."),
            new("veteran", "Veteran", "100 matches played."),
            new("hunter", "Hunter", "1,000 kills."),
            new("ghost", "Ghost", "A match with five kills or more and no deaths."),
        ];
        long at = new DateTimeOffset(2026, 11, 22, 20, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        RecordMatch[] latest =
        [
            new("1", at, "OpenIGI Official · Frankfurt #1", "tdm", "1", 900, 0, [new RecordLine(player, "Jones", 0, 24, 6, 3, 0, 305, true)]),
            new("2", at - 3600000, "OpenIGI Official · Frankfurt #2", "sd", "3", 1200, 1, [new RecordLine(player, "Jones", 0, 11, 9, 2, 1, 145, false)]),
            new("3", at - 86400000, "OpenIGI Official · Mumbai", "ffa", "5", 600, 2, [new RecordLine(player, "Jones", 2, 17, 4, 0, 0, 220, true)]),
        ];
        return Task.FromResult(new PlayerRecord(player, "2026-11", season, all, medals, title, latest));
    }

    public Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel) =>
        Task.FromResult<PlayerAccount?>(new PlayerAccount(player, "Jones", string.Empty, 0));
}

/// <summary>A mission board of made-up runs: times climbing a few seconds a place, ranks falling, every fourth replay let go.</summary>
internal sealed class FakeRunBoards(int count) : IRunBoardReader
{
    private static readonly string[] Names = ["Kestrel", "Anya", "Night Owl", "Jones", "Marlin", "Sasha", "Rook", "Lena", "Hawk", "Viper", "Ivo", "Wren"];

    public Task<RunBoard> BoardAsync(string title, int? mission, int difficulty, string ruleset, string metric, string? you, CancellationToken cancel)
    {
        List<RunBoardRow> rows =
        [
            .. Enumerable.Range(0, count).Select(i => new RunBoardRow(
                i + 1,
                (900000000000000000L + i).ToString(CultureInfo.InvariantCulture),
                Names[i % Names.Length],
                mission is null ? 145000 + (i * 2400) : 9120 + (i * 97),
                Rank: Math.Max(-1, 2 - (i / 6)),
                Points: mission is null ? 42 - i : 0,
                Run: mission is null ? null : $"run{i}",
                Replay: mission is not null && i % 4 != 3)),
        ];
        RunBoardRow? own = you is null ? null : rows.FirstOrDefault(r => r.Account == you);
        return Task.FromResult(new RunBoard(title, mission?.ToString(CultureInfo.InvariantCulture) ?? "all", difficulty, ruleset, metric, count * 7, rows, own));
    }
}
