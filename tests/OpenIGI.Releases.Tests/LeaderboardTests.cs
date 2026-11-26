using System.Net;
using System.Text;
using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// The leaderboard as the launcher and the game read it (stats.js): a board with its own row and seasons, a record with its
/// latest matches, the feed that keeps them, and the avatars a board draws.
/// </summary>
public sealed class LeaderboardTests
{
    private const string Row = """
        {"rank":1,"player":"900000000000000001","account":{"id":"900000000000000001","name":"Ana","avatar":"https://cdn.discordapp.com/avatars/9/a.png"},
         "name":"Ana","matches":4,"wins":3,"kills":40,"deaths":10,"assists":6,"objectives":1,"seconds":2400,"score":1705,"kd":4}
        """;

    private const string Totals = """{"matches":3,"wins":2,"kills":17,"deaths":4,"objectives":1,"seconds":1800,"score":295,"kd":4.25}""";

    [Fact]
    public async Task A_board_is_read_with_its_own_row_and_seasons()
    {
        Uri? asked = null;
        string json = $$$"""
            {"title":"igi2","period":"season","season":"2026-11","metric":"kd","rows":[{{{Row}}}],"players":212,"seasons":["2026-11","2026-10"],
             "you":{"rank":140,"player":"bbbbbbbbbbbbbbbb","account":null,"name":"Bo","matches":5,"wins":1,"kills":3,"deaths":9,"score":80,"kd":0.33}}
            """;
        using var http = Answering(json, uri => asked = uri);

        Leaderboard board = await new StatsService(http, new Uri("https://master.example")).BoardAsync("igi2", "season", "kd", "bbbbbbbbbbbbbbbb");

        Assert.Equal("?title=igi2&period=season&metric=kd&you=bbbbbbbbbbbbbbbb", asked!.Query);
        Assert.Equal(212, board.Players);
        Assert.Equal(["2026-11", "2026-10"], board.Seasons);
        Assert.Equal("900000000000000001", board.Rows[0].RecordId);
        Assert.Equal(0.75, board.Rows[0].WinRate);
        Assert.Equal(140, board.You!.Rank);
        Assert.Equal("bbbbbbbbbbbbbbbb", board.You.RecordId);
        Assert.Equal("0.33", LeaderboardMetric.Of("kd").Figure(board.You));
        Assert.Equal("1,705", LeaderboardMetric.Of("nonsense").Figure(board.Rows[0]));
    }

    [Fact]
    public async Task A_board_from_an_older_master_has_no_own_row_or_seasons()
    {
        using var http = Answering($$"""{"title":"igi1","period":"all","season":"all","metric":"score","rows":[{{Row}}]}""");
        Leaderboard board = await new StatsService(http, new Uri("https://master.example")).BoardAsync("igi1", "all", "score");
        Assert.Null(board.You);
        Assert.Null(board.Seasons);
        Assert.Equal(0, board.Players);
    }

    [Fact]
    public async Task A_record_is_read_with_its_latest_matches()
    {
        string json = $$"""
            {"player":"111","season":"2026-11","seasonTotals":{{Totals}},"allTime":{{Totals}},"latest":[
              {"id":"1-a","at":1763600000000,"server":"Official EU","title":"igi1","mode":"tdm","map":"3","seconds":600,"winner":0,
               "players":[],"you":[{"player":"aaaaaaaaaaaaaaaa","name":"Ana","team":0,"kills":9,"deaths":2,"assists":1,"objectives":0,"seconds":600,"won":true,"score":145}]}]}
            """;
        using var http = Answering(json);

        PlayerRecord record = await new StatsService(http, new Uri("https://master.example")).RecordAsync("111", "igi1");

        RecordMatch match = Assert.Single(record.Latest!);
        Assert.Equal("Official EU", match.Server);
        Assert.Equal("3", match.Map);
        Assert.True(match.Line!.Won);
        Assert.Equal(145, match.Line.Score);
    }

    [Fact]
    public async Task A_missing_player_has_no_account()
    {
        using var http = new HttpClient(new AnsweringHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"No such player.\"}") })));
        Assert.Null(await new AccountService(http, new Uri("https://master.example")).PlayerAsync("123456"));
    }

    [Theory]
    [InlineData("2026-11", "November 2026")]
    [InlineData("all", "all")]
    public void A_season_is_said_by_its_month(string season, string said) => Assert.Equal(said, LeaderboardSeasons.Name(season));

    [Fact]
    public void A_season_ends_with_its_month()
    {
        Assert.Equal(11, LeaderboardSeasons.DaysLeft(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(1, LeaderboardSeasons.DaysLeft(new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero)));
        Assert.True(LeaderboardSeasons.IsMonth("2026-10"));
        Assert.False(LeaderboardSeasons.IsMonth(LeaderboardSeasons.Current));
    }

    [Fact]
    public void The_feed_asks_only_while_watched_and_keeps_what_it_read()
    {
        var reader = new FakeReader();
        DateTimeOffset now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var feed = new LeaderboardFeed(reader, "igi1", () => now);

        feed.Update(watching: false);
        Assert.Equal(0, reader.Boards);

        feed.Update(watching: true);
        feed.Update(watching: true);
        Assert.Equal(1, reader.Boards);
        Assert.Equal(FeedState.Ready, feed.BoardState);
        Assert.Equal("igi1/season/score", feed.Board!.Metric);

        // Another metric is its own board; back again is the one kept.
        feed.ShowMetric(LeaderboardMetric.Of("kills"));
        feed.Update(watching: true);
        feed.ShowMetric(LeaderboardMetric.Default);
        feed.Update(watching: true);
        Assert.Equal(2, reader.Boards);

        // Old, it is read again.
        now += LeaderboardFeed.Fresh + TimeSpan.FromSeconds(1);
        feed.Update(watching: true);
        Assert.Equal(3, reader.Boards);
    }

    [Fact]
    public void A_season_gone_by_is_left_for_this_one_when_the_game_changes()
    {
        var feed = new LeaderboardFeed(new FakeReader(), "igi1");
        feed.ShowPeriod("2026-10");
        feed.ShowTitle("igi2");
        Assert.Equal(LeaderboardSeasons.Current, feed.Period);
    }

    [Fact]
    public void A_record_opened_is_read_with_its_account_and_place()
    {
        var reader = new FakeReader();
        var feed = new LeaderboardFeed(reader, "igi1");
        feed.OpenRecord("bbbbbbbbbbbbbbbb");
        feed.Update(watching: true);
        feed.Update(watching: true);

        Assert.Equal(FeedState.Ready, feed.RecordState);
        Assert.Equal("Bo", feed.Record!.Name);
        Assert.Equal(7, feed.Record.Placed!.Rank);

        feed.CloseRecord();
        Assert.Equal(FeedState.Idle, feed.RecordState);
    }

    [Fact]
    public void A_board_that_fails_is_shown_failed_and_tried_again_later()
    {
        DateTimeOffset now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var reader = new FakeReader { Fail = true };
        var feed = new LeaderboardFeed(reader, "igi1", () => now);
        feed.Update(watching: true);
        feed.Update(watching: true);
        Assert.Equal(FeedState.Failed, feed.BoardState);
        Assert.Equal(1, reader.Boards);

        reader.Fail = false;
        feed.Update(watching: true);
        Assert.Equal(1, reader.Boards);
        now += LeaderboardFeed.Fresh + TimeSpan.FromSeconds(1);
        feed.Update(watching: true);
        feed.Update(watching: true);
        Assert.Equal(FeedState.Ready, feed.BoardState);
    }

    [Fact]
    public void Avatars_come_from_discord_only_and_a_few_at_a_time()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        int asked = 0;
        using var http = new HttpClient(new AnsweringHandler(_ =>
        {
            asked++;
            return gate.Task;
        }));
        var files = new AvatarFiles(http, Path.Combine(Path.GetTempPath(), "openigi-avatar-test-" + Guid.NewGuid().ToString("N")));

        Assert.Null(files.PathOf("https://example.com/a.png"));
        for (int i = 0; i < 6; i++)
        {
            Assert.Null(files.PathOf($"https://cdn.discordapp.com/avatars/1/{i}.png"));
        }

        Assert.Equal(AvatarFiles.MostAtOnce, files.Fetching);
        Assert.Equal(AvatarFiles.MostAtOnce, asked);
        gate.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpClient Answering(string json, Action<Uri>? asked = null) => new(new AnsweringHandler(request =>
    {
        asked?.Invoke(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }));

    private sealed class FakeReader : ILeaderboardReader
    {
        public int Boards { get; private set; }

        public bool Fail { get; set; }

        public Task<Leaderboard> BoardAsync(string title, string period, string metric, string? you, CancellationToken cancel)
        {
            Boards++;
            if (Fail)
            {
                return Task.FromException<Leaderboard>(new HttpRequestException("down"));
            }

            BoardRow? own = you is null ? null : new BoardRow(7, you, null, "Bo", 1, 0, 1, 1);
            return Task.FromResult(new Leaderboard(title, period, period, $"{title}/{period}/{metric}", [], own));
        }

        public Task<PlayerRecord> RecordAsync(string player, string title, CancellationToken cancel)
        {
            StatTotals none = new(0, 0, 0, 0, 0, 0, 0, 0);
            return Task.FromResult(new PlayerRecord(player, "2026-11", none, none, Title: title));
        }

        public Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel) => Task.FromResult<PlayerAccount?>(null);
    }
}
