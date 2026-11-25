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

    public string YourName => "Joe";

    public bool SignedIn => SignInHint is null;

    public bool CanFindMatch => true;

    public bool CanOpenProfile => true;

    public bool CanPlay => true;

    public bool CanReport => true;

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

    public bool Unranked { get; set; }

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
        // The week's movement: some up, some down, one new; none for a season gone by.
        bool weekly = period is "season" or "all";
        int?[] moved = [2, -1, 1, 3, -1, 0, 5, -2, null, 1, 1, -3];
        rows = [.. rows.Select((row, i) => row with { Rank = i + 1, Was = weekly && moved[i % moved.Length] is { } m ? i + 1 + m : null })];
        BoardRow? own = you is null || Unranked ? null : new BoardRow(212, you, null, "Joe", 9, 3, 41, 37, 6, 0, 5400, 715, 1.11, Was: 230);
        BoardRow? ahead = own is null ? null : new BoardRow(211, "bbbbbbbbbbbbbbbb", null, "Sparrow", 10, 4, 44, 36, 7, 0, 5600, 755, 1.22);
        long? week = weekly && rows.Count > 0 ? new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
        return Task.FromResult(new Leaderboard(title, period == "all" ? "all" : "season", period == "season" ? "2026-11" : period, metric, rows, own, rows.Count == 0 ? 0 : 1480, ["2026-11", "2026-10", "2026-09"], ahead, week));
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
        string[] servers = ["OpenIGI Official · Frankfurt #1", "OpenIGI Official · Frankfurt #2", "Covert Ops 24/7", "Mumbai Regulars"];
        string[] modes = ["tdm", "sd", "tdm", "ffa"];
        bool[] won = [true, true, false, true, true, false, true, true, true, false];
        RecordMatch[] latest =
        [
            .. Enumerable.Range(0, LatestCount).Select(i => new RecordMatch(
                (i + 1).ToString(CultureInfo.InvariantCulture),
                at - (i * 14_400_000L),
                servers[i % servers.Length],
                modes[i % modes.Length],
                ((i % 6) + 1).ToString(CultureInfo.InvariantCulture),
                900,
                won[i % won.Length] ? 0 : 1,
                [new RecordLine(player, "Jones", 0, 24 - i, 6 + (i % 4), 3 - (i % 3), 0, won[i % won.Length] ? 305 - (i * 15) : 140, won[i % won.Length])])),
        ];
        return Task.FromResult(new PlayerRecord(player, "2026-11", season, all, medals, title, latest));
    }

    public Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel) =>
        Task.FromResult<PlayerAccount?>(new PlayerAccount(player, "Jones", string.Empty, 0));

    /// <summary>Gets or sets how many latest matches a record lists.</summary>
    public int LatestCount { get; set; } = 3;

    public Task<MissionStanding?> StandingAsync(string player, string title, CancellationToken cancel)
    {
        static PlayerBest Best(int mission, int ticks, int rank, int place) =>
            new(mission, 2, "classic", new RunBest($"run{mission}", ticks, -1), new RunBest($"run{mission}", ticks, rank), place, place, 312);
        PlayerBest[] bests = [Best(1, 9411, 2, 4), Best(2, 14526, 1, 9), Best(3, 20604, 2, 2), Best(5, 17730, 1, 15), Best(6, 12009, 2, 6), Best(8, 12987, 1, 21)];
        return Task.FromResult<MissionStanding?>(new MissionStanding(2, "classic", bests, new RunBoardRow(7, player, "Jones", 160000)));
    }
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
                Replay: mission is not null && i < 20 && i % 7 != 6,
                Splits: mission is null ? null : [1446 + (i * 10), 4194 + (i * 30), 7581 + (i * 70)],
                Missions: mission is null ? 6 : 0)),
        ];
        RunBoardRow? own = you is null ? null : rows.FirstOrDefault(r => r.Account == you);
        return Task.FromResult(new RunBoard(title, mission?.ToString(CultureInfo.InvariantCulture) ?? "all", difficulty, ruleset, metric, count * 7, rows, own));
    }
}
