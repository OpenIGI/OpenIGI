using System.Net;
using System.Text;
using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>A player's record as the master gives it (stats.js), read with its medals, and from an older master without them.</summary>
public sealed class StatsServiceTests
{
    private const string Totals = """{"matches":3,"wins":2,"kills":17,"deaths":4,"objectives":1,"seconds":1800,"score":295,"kd":4.25}""";

    [Fact]
    public async Task A_record_is_read_with_its_medals()
    {
        PlayerRecord record = await Read($$"""
            {"player":"111","season":"2026-11","seasonTotals":{{Totals}},"allTime":{{Totals}},
             "medals":[{"id":"first-kill","name":"First blood","about":"A kill."},{"id":"ghost","name":"Ghost","about":"No deaths."}],"latest":[]}
            """);

        Assert.Equal(17, record.AllTime.Kills);
        Assert.Equal(["First blood", "Ghost"], record.Medals!.Select(static medal => medal.Name));
    }

    [Fact]
    public async Task A_record_from_an_older_master_has_no_medals()
    {
        PlayerRecord record = await Read($$"""{"player":"111","season":"2026-11","seasonTotals":{{Totals}},"allTime":{{Totals}}}""");
        Assert.Null(record.Medals);
    }

    [Fact]
    public async Task A_record_reads_assists_and_none_from_an_older_master()
    {
        string counted = Totals.Replace("\"kd\":4.25", "\"kd\":4.25,\"assists\":9", StringComparison.Ordinal);
        PlayerRecord record = await Read($$"""{"player":"111","season":"2026-11","seasonTotals":{{counted}},"allTime":{{Totals}}}""");
        Assert.Equal(9, record.SeasonTotals.Assists);
        Assert.Equal(0, record.AllTime.Assists);
    }

    [Fact]
    public async Task A_report_sends_each_players_assists()
    {
        string? sent = null;
        using var http = new HttpClient(new AnsweringHandler(async request =>
        {
            sent = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") };
        }));
        using var key = new PointSigner();
        MatchReport report = new("igi1", "tdm", "1", 600, 0, [new MatchReportLine("aaaaaaaaaaaaaaaa", "Ana", 0, 4, 2, 0, true, Assists: 7), new MatchReportLine(null, "Bo", 1, 2, 4, 0, false)]);

        await new StatsService(http, new Uri("https://master.example")).ReportAsync(key, report);

        Assert.Contains("\"assists\":7", sent, StringComparison.Ordinal);
        Assert.Contains("\"assists\":0", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_record_is_asked_for_one_game()
    {
        Uri? asked = null;
        using var http = new HttpClient(new AnsweringHandler(request =>
        {
            asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"player":"111","title":"igi2","season":"2026-11","seasonTotals":{{Totals}},"allTime":{{Totals}}}""", Encoding.UTF8, "application/json") });
        }));

        PlayerRecord record = await new StatsService(http, new Uri("https://master.example")).RecordAsync("111", "igi2");

        Assert.Equal("/v1/players/111/stats", asked!.AbsolutePath);
        Assert.Equal("?title=igi2", asked.Query);
        Assert.Equal("igi2", record.Title);
    }

    private static async Task<PlayerRecord> Read(string json)
    {
        using var http = new HttpClient(new AnsweringHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
        return await new StatsService(http, new Uri("https://master.example")).RecordAsync("111", "igi1");
    }
}
