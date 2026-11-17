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

    private static async Task<PlayerRecord> Read(string json)
    {
        using var http = new HttpClient(new AnsweringHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") })));
        return await new StatsService(http, new Uri("https://master.example")).RecordAsync("111");
    }
}
