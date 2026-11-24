using System.Net;
using System.Text;
using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// Single-player runs as the game and the launcher reach them (runs.js): a run submitted signed, its claims in the query and its
/// replay the body; a refusal told in the master's own words; a run's status; a board with the asking player's row; a replay not
/// kept read as none.
/// </summary>
public sealed class RunsServiceTests
{
    private static readonly Uri Master = new("https://master.example");

    [Fact]
    public async Task A_run_is_submitted_signed_with_its_claims_and_replay()
    {
        HttpRequestMessage? sent = null;
        byte[]? body = null;
        using HttpClient http = new(new AnsweringHandler(async request =>
        {
            sent = request;
            body = await request.Content!.ReadAsByteArrayAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"id":"abc123","status":"pending"}""", Encoding.UTF8, "application/json") };
        }));
        using PointSigner player = new();

        string id = await new RunsService(http, Master).SubmitAsync(player, new RunClaims("igi1", 3, 2, "classic", 3711, 2), [1, 2, 3]);

        Assert.Equal("abc123", id);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("/v1/runs", sent.RequestUri!.AbsolutePath);
        Assert.Equal("?title=igi1&mission=3&difficulty=2&ruleset=classic&ticks=3711&rank=2", sent.RequestUri.Query);
        Assert.Equal([1, 2, 3], body);
        Assert.True(sent.Headers.Contains(SignedRequest.SignatureHeader));
        Assert.Equal(player.PublicKey, sent.Headers.GetValues(SignedRequest.KeyHeader).Single());
    }

    [Fact]
    public async Task A_refused_run_says_why_in_the_masters_words()
    {
        using HttpClient http = new(new AnsweringHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""{"error":"It would not place in the top 20; it stays on your machine."}""", Encoding.UTF8, "application/json"),
        })));
        using PointSigner player = new();

        AccountRefusedException refused = await Assert.ThrowsAsync<AccountRefusedException>(
            () => new RunsService(http, Master).SubmitAsync(player, new RunClaims("igi1", 3, 2, "classic", 9999, -1), [1]));

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Contains("top 20", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runs_status_is_read_as_the_checker_left_it()
    {
        using HttpClient http = Answering("""{"id":"abc","status":"rejected","reason":"the score it claims is not the one its run comes to","name":"Ana","title":"igi1","mission":3,"difficulty":2,"ruleset":"classic","ticks":null,"rank":null,"replay":false}""");

        RunStatus run = await new RunsService(http, Master).RunAsync("abc");

        Assert.False(run.IsPending);
        Assert.False(run.IsVerified);
        Assert.Contains("score", run.Reason, StringComparison.Ordinal);
        Assert.Null(run.Ticks);
    }

    [Fact]
    public async Task A_board_is_read_with_the_askers_own_row()
    {
        Uri? asked = null;
        using HttpClient http = Answering(
            """
            {"title":"igi1","mission":"3","difficulty":2,"ruleset":"modern","metric":"time","players":41,
             "rows":[{"place":1,"account":"111","name":"Ana","ticks":3500,"rank":2,"run":"r1","replay":true}],
             "you":{"place":17,"account":"222","name":"Bo","ticks":4100,"rank":1,"run":"r9","replay":true}}
            """,
            uri => asked = uri);

        RunBoard board = await new RunsService(http, Master).BoardAsync("igi1", 3, 2, "modern", "time", "222");

        Assert.Equal("?title=igi1&mission=3&difficulty=2&ruleset=modern&metric=time&you=222", asked!.Query);
        Assert.Equal(41, board.Players);
        Assert.Equal("r1", board.Rows[0].Run);
        Assert.Equal(17, board.You!.Place);
    }

    [Fact]
    public async Task The_overall_board_is_asked_for_every_mission()
    {
        Uri? asked = null;
        using HttpClient http = Answering("""{"title":"igi2","mission":"all","difficulty":1,"ruleset":"classic","metric":"rank","players":0,"rows":[],"you":null}""", uri => asked = uri);
        RunBoard board = await new RunsService(http, Master).BoardAsync("igi2", null, 1, "classic", "rank", null);
        Assert.Contains("mission=all", asked!.Query, StringComparison.Ordinal);
        Assert.Empty(board.Rows);
    }

    [Fact]
    public async Task A_replay_not_kept_is_none()
    {
        using HttpClient http = new(new AnsweringHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        Assert.Null(await new RunsService(http, Master).ReplayAsync("gone"));
    }

    private static HttpClient Answering(string json, Action<Uri>? asked = null) => new(new AnsweringHandler(request =>
    {
        asked?.Invoke(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }));
}
