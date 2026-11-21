using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Releases.Accounts;

/// <summary>One player's line in a match's result, as a server reports it.</summary>
/// <param name="Player">The key the player played under (PlayerIdentities.IdOf), or <see langword="null"/> for one without.</param>
/// <param name="Name">The name they played under.</param>
/// <param name="Team">Their team, or their slot in a free-for-all.</param>
/// <param name="Kills">The enemies they killed.</param>
/// <param name="Deaths">The times they died.</param>
/// <param name="Objectives">The objectives they won: bombs planted or defused, rounds decided.</param>
/// <param name="Won">Whether they were on the winning side.</param>
/// <param name="Assists">The enemies they helped kill: badly hurt in a life another player ended.</param>
public sealed record MatchReportLine(string? Player, string Name, int Team, int Kills, int Deaths, int Objectives, bool Won, int Assists = 0);

/// <summary>A decided match's result, as a server reports it.</summary>
/// <param name="Title">The game, by title id.</param>
/// <param name="Mode">The mode, such as <c>tdm</c>.</param>
/// <param name="Map">The map.</param>
/// <param name="Seconds">How long it lasted.</param>
/// <param name="Winner">The winning team (or slot, in a free-for-all), or -1 for a draw.</param>
/// <param name="Players">Each player's line.</param>
public sealed record MatchReport(string Title, string Mode, string Map, int Seconds, int Winner, IReadOnlyList<MatchReportLine> Players);

/// <summary>A player's totals over a period.</summary>
/// <param name="Matches">The matches played.</param>
/// <param name="Wins">The matches won.</param>
/// <param name="Kills">The kills.</param>
/// <param name="Deaths">The deaths.</param>
/// <param name="Objectives">The objectives won.</param>
/// <param name="Seconds">The time played.</param>
/// <param name="Score">The points: a kill 10, an assist 5, an objective 25, a win 50.</param>
/// <param name="Kd">Kills a death, to two places.</param>
/// <param name="Assists">The assists; nought from a master too old to count them.</param>
public sealed record StatTotals(int Matches, int Wins, int Kills, int Deaths, int Objectives, int Seconds, int Score, double Kd, int Assists = 0);

/// <summary>A medal: something a player has done on the official servers, read from their all-time totals.</summary>
/// <param name="Id">Its id, such as <c>first-win</c>.</param>
/// <param name="Name">Its name, such as <c>First win</c>.</param>
/// <param name="About">What earns it.</param>
public sealed record Medal(string Id, string Name, string About);

/// <summary>A player's record on the official servers.</summary>
/// <param name="Player">Who: a key's id or an account's.</param>
/// <param name="Season">The season now, as <c>yyyy-MM</c>.</param>
/// <param name="SeasonTotals">This season's totals.</param>
/// <param name="AllTime">All time's.</param>
/// <param name="Medals">The medals earned, or <see langword="null"/> from a master too old to say.</param>
/// <param name="Title">The game the record is of, by title id; <see langword="null"/> from a master that kept both games as one.</param>
public sealed record PlayerRecord(string Player, string Season, StatTotals SeasonTotals, StatTotals AllTime, IReadOnlyList<Medal>? Medals = null, string? Title = null);

/// <summary>
/// The master server's stats (tools/master-server/src/stats.js): a listed server reports its decided matches, signed by its
/// own key, and a player's record is read by their key or account, one game at a time: each game's stats are its own.
/// </summary>
/// <param name="http">The client to ask through.</param>
/// <param name="master">The master server.</param>
public sealed class StatsService(HttpClient http, Uri master)
{
    /// <summary>Reports a decided match.</summary>
    /// <param name="server">The server's own key, which the master lists.</param>
    /// <param name="report">The result.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>A task.</returns>
    /// <exception cref="AccountRefusedException">The master would not count it: the server isn't listed, or the result is malformed.</exception>
    public async Task ReportAsync(IRequestSigner server, MatchReport report, CancellationToken cancel = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(report, StatsJsonContext.Default.MatchReport);
        using HttpRequestMessage request = await SignedRequest.CreateAsync(server, HttpMethod.Post, master, "/v1/matches", body).ConfigureAwait(false);
        using HttpResponseMessage response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        await Refused(response, cancel).ConfigureAwait(false);
    }

    /// <summary>Reads a player's record in one game.</summary>
    /// <param name="player">A key's id or an account's.</param>
    /// <param name="title">The game, by title id: <c>igi1</c> or <c>igi2</c>.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The record.</returns>
    public async Task<PlayerRecord> RecordAsync(string player, string title, CancellationToken cancel = default)
    {
        Uri address = new(master, $"/v1/players/{Uri.EscapeDataString(player)}/stats?title={Uri.EscapeDataString(title)}");
        using HttpResponseMessage response = await http.GetAsync(address, cancel).ConfigureAwait(false);
        await Refused(response, cancel).ConfigureAwait(false);
        Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, StatsJsonContext.Default.PlayerRecord, cancel).ConfigureAwait(false)
            ?? throw new AccountRefusedException("The stats came back empty.", response.StatusCode);
    }

    private static async Task Refused(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = $"The stats server said {(int)response.StatusCode}.";
        try
        {
            using JsonDocument said = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
            if (said.RootElement.TryGetProperty("error", out JsonElement error) && error.GetString() is { Length: > 0 } reason)
            {
                message = reason;
            }
        }
        catch (JsonException)
        {
            // Its status says enough.
        }

        throw new AccountRefusedException(message, response.StatusCode);
    }
}

/// <summary>The JSON the stats read and write, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MatchReport))]
[JsonSerializable(typeof(PlayerRecord))]
internal sealed partial class StatsJsonContext : JsonSerializerContext;
