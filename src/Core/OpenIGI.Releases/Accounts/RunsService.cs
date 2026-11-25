using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Releases.Accounts;

/// <summary>What a run is claimed to be as it is submitted: which board, and the numbers it says it came to.</summary>
/// <param name="Title">The game, by title id.</param>
/// <param name="Mission">The mission.</param>
/// <param name="Difficulty">The difficulty, nought to two.</param>
/// <param name="Ruleset">The rules, by id: classic or modern.</param>
/// <param name="Ticks">The mission's time, in ticks.</param>
/// <param name="Rank">The rank it earned, -1 for none.</param>
/// <param name="Version">The build that recorded it, whose checker alone can play it again; empty where unknown.</param>
/// <param name="Fingerprint">The fingerprint of the game files its mission was played from; empty where unknown.</param>
/// <remarks>The claims only decide whether the run is worth keeping and checking: the boards take the checker's numbers.</remarks>
public sealed record RunClaims(string Title, int Mission, int Difficulty, string Ruleset, int Ticks, int Rank, string Version = "", string Fingerprint = "");

/// <summary>
/// A build's official game files, as its checker holds them: every mission's fingerprint in each edition it plays runs in. A
/// run is taken only from files one of them holds.
/// </summary>
/// <param name="titles">By game, then by mission, the fingerprints.</param>
public sealed class OfficialFiles(IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlySet<string>>> titles)
{
    /// <summary>Whether the checker plays a game's runs at all.</summary>
    /// <param name="title">The game.</param>
    /// <returns>Whether it does.</returns>
    public bool Knows(string title) => titles.ContainsKey(title);

    /// <summary>Whether files of a mission are an official edition's.</summary>
    /// <param name="title">The game.</param>
    /// <param name="mission">The mission.</param>
    /// <param name="fingerprint">The fingerprint of the files it was played from.</param>
    /// <returns>Whether they are.</returns>
    public bool Holds(string title, int mission, string fingerprint) =>
        titles.TryGetValue(title, out IReadOnlyDictionary<int, IReadOnlySet<string>>? missions)
        && missions.TryGetValue(mission, out IReadOnlySet<string>? fingerprints)
        && fingerprints.Contains(fingerprint);

    /// <summary>Reads the master's answer: <c>{ "titles": { "igi1": { "1": [ ... ] } } }</c>.</summary>
    /// <param name="json">The answer.</param>
    /// <returns>The files.</returns>
    public static OfficialFiles Read(JsonElement json)
    {
        Dictionary<string, IReadOnlyDictionary<int, IReadOnlySet<string>>> titles = new(StringComparer.Ordinal);
        if (json.TryGetProperty("titles", out JsonElement all) && all.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty title in all.EnumerateObject().Where(static t => t.Value.ValueKind == JsonValueKind.Object))
            {
                Dictionary<int, IReadOnlySet<string>> missions = [];
                foreach (JsonProperty mission in title.Value.EnumerateObject().Where(static m => m.Value.ValueKind == JsonValueKind.Array))
                {
                    if (int.TryParse(mission.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                    {
                        missions[id] = mission.Value.EnumerateArray().Where(static f => f.ValueKind == JsonValueKind.String).Select(static f => f.GetString()!).ToHashSet(StringComparer.Ordinal);
                    }
                }

                titles[title.Name] = missions;
            }
        }

        return new OfficialFiles(titles);
    }
}

/// <summary>A run as the master tells it.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Status">pending, verified or rejected.</param>
/// <param name="Reason">Why it was rejected, or empty.</param>
/// <param name="Name">Whose it is, by the name they play under.</param>
/// <param name="Title">The game.</param>
/// <param name="Mission">The mission.</param>
/// <param name="Difficulty">The difficulty.</param>
/// <param name="Ruleset">The rules.</param>
/// <param name="Ticks">Its time, once checked.</param>
/// <param name="Rank">Its rank, once checked.</param>
/// <param name="Replay">Whether its replay is kept to watch and race.</param>
public sealed record RunStatus(
    string Id,
    string Status,
    string Reason = "",
    string Name = "",
    string Title = "",
    int Mission = 0,
    int Difficulty = 0,
    string Ruleset = "",
    int? Ticks = null,
    int? Rank = null,
    bool Replay = false)
{
    /// <summary>Gets a value indicating whether the checker has yet to rule on it.</summary>
    public bool IsPending => Status == "pending";

    /// <summary>Gets a value indicating whether it is on its board.</summary>
    public bool IsVerified => Status == "verified";
}

/// <summary>A row of a mission board.</summary>
/// <param name="Place">Its place, from one.</param>
/// <param name="Account">The player's account.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Ticks">Their time, or their total over every mission on the overall board.</param>
/// <param name="Rank">Their rank on a mission's board, -1 for none.</param>
/// <param name="Points">Their ranks' points over every mission, on the overall board by rank.</param>
/// <param name="Run">The run, on a mission's board.</param>
/// <param name="Replay">Whether its replay is kept to watch and race.</param>
public sealed record RunBoardRow(int Place, string Account, string Name, int Ticks, int Rank = -1, int Points = 0, string? Run = null, bool Replay = false);

/// <summary>One mission board, or the overall one.</summary>
/// <param name="Title">The game.</param>
/// <param name="Mission">The mission, or all.</param>
/// <param name="Difficulty">The difficulty.</param>
/// <param name="Ruleset">The rules.</param>
/// <param name="Metric">time or rank.</param>
/// <param name="Players">How many are ranked.</param>
/// <param name="Rows">The best of them.</param>
/// <param name="You">The asking player's own row, wherever it stands, or null.</param>
public sealed record RunBoard(string Title, string Mission, int Difficulty, string Ruleset, string Metric, int Players, IReadOnlyList<RunBoardRow> Rows, RunBoardRow? You);

/// <summary>
/// The master's single-player runs: a completed run submitted for the boards (signed by the player's installation key, which must
/// be linked to a Discord account), a run's status as the checker rules on it, the boards, and a board's replay to watch or race.
/// </summary>
/// <param name="http">The HTTP client.</param>
/// <param name="master">The master's address.</param>
public sealed class RunsService(HttpClient http, Uri master)
{
    /// <summary>The largest replay the master takes: its inputs alone, its ghost left out.</summary>
    public const int MaxReplayBytes = 1024 * 1024;

    /// <summary>Submits a run.</summary>
    /// <param name="player">The player's key.</param>
    /// <param name="claims">What it is claimed to be.</param>
    /// <param name="replay">The replay's bytes, its ghost left out.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>Its id, waiting to be checked.</returns>
    /// <exception cref="AccountRefusedException">The master did not take it, saying why.</exception>
    public async Task<string> SubmitAsync(IRequestSigner player, RunClaims claims, byte[] replay, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(replay);
        string path = string.Create(
            CultureInfo.InvariantCulture,
            $"/v1/runs?title={claims.Title}&mission={claims.Mission}&difficulty={claims.Difficulty}&ruleset={claims.Ruleset}&ticks={claims.Ticks}&rank={claims.Rank}")
            + (claims.Version.Length > 0 ? "&version=" + Uri.EscapeDataString(claims.Version) : string.Empty)
            + (claims.Fingerprint.Length > 0 ? "&fingerprint=" + Uri.EscapeDataString(claims.Fingerprint) : string.Empty);
        using HttpRequestMessage request = await SignedRequest.CreateAsync(player, HttpMethod.Post, master, path, replay, "application/octet-stream").ConfigureAwait(false);
        using HttpResponseMessage response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        await MasterAnswers.ThrowIfRefusedAsync(response, "runs", cancel).ConfigureAwait(false);
        RunStatus taken = await ReadAsync(response, RunsJsonContext.Default.RunStatus, cancel).ConfigureAwait(false);
        return taken.Id;
    }

    /// <summary>A build's official game files, as its checker told the master; null where no checker plays its runs.</summary>
    /// <param name="version">The build.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The files, or null.</returns>
    public async Task<OfficialFiles?> OfficialFilesAsync(string version, CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/runs/official?version={Uri.EscapeDataString(version)}"), cancel).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await MasterAnswers.ThrowIfRefusedAsync(response, "runs", cancel).ConfigureAwait(false);
        using JsonDocument answer = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false), cancellationToken: cancel).ConfigureAwait(false);
        return OfficialFiles.Read(answer.RootElement);
    }

    /// <summary>Reports a run on the boards for a person to look at, signed by the reporting player's key.</summary>
    /// <param name="player">The reporting player's key, which must belong to a linked account.</param>
    /// <param name="id">The run.</param>
    /// <param name="reason">What is wrong with it, in a sentence; may be empty.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>A task that completes once the master has taken the report.</returns>
    /// <exception cref="AccountRefusedException">The master did not take it, saying why.</exception>
    public async Task ReportAsync(IRequestSigner player, string id, string reason, CancellationToken cancel = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using MemoryStream body = new();
        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteString("reason", reason ?? string.Empty);
            json.WriteEndObject();
        }

        using HttpRequestMessage request = await SignedRequest.CreateAsync(
            player, HttpMethod.Post, master, $"/v1/runs/{Uri.EscapeDataString(id)}/report", body.ToArray(), "application/json").ConfigureAwait(false);
        using HttpResponseMessage response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        await MasterAnswers.ThrowIfRefusedAsync(response, "runs", cancel).ConfigureAwait(false);
    }

    /// <summary>A run as the master tells it now.</summary>
    /// <param name="id">The run.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The run.</returns>
    public async Task<RunStatus> RunAsync(string id, CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/runs/{Uri.EscapeDataString(id)}"), cancel).ConfigureAwait(false);
        await MasterAnswers.ThrowIfRefusedAsync(response, "runs", cancel).ConfigureAwait(false);
        return await ReadAsync(response, RunsJsonContext.Default.RunStatus, cancel).ConfigureAwait(false);
    }

    /// <summary>A board.</summary>
    /// <param name="title">The game.</param>
    /// <param name="mission">The mission, or null for every mission together.</param>
    /// <param name="difficulty">The difficulty.</param>
    /// <param name="ruleset">The rules.</param>
    /// <param name="metric">time or rank.</param>
    /// <param name="you">The asking player's account, for their own row; or null.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The board.</returns>
    public async Task<RunBoard> BoardAsync(string title, int? mission, int difficulty, string ruleset, string metric, string? you, CancellationToken cancel = default)
    {
        string path = string.Create(
            CultureInfo.InvariantCulture,
            $"/v1/runs/board?title={title}&mission={(mission is { } m ? m.ToString(CultureInfo.InvariantCulture) : "all")}&difficulty={difficulty}&ruleset={ruleset}&metric={metric}");
        if (you is { Length: > 0 })
        {
            path += "&you=" + Uri.EscapeDataString(you);
        }

        using HttpResponseMessage response = await http.GetAsync(new Uri(master, path), cancel).ConfigureAwait(false);
        await MasterAnswers.ThrowIfRefusedAsync(response, "runs", cancel).ConfigureAwait(false);
        return await ReadAsync(response, RunsJsonContext.Default.RunBoard, cancel).ConfigureAwait(false);
    }

    /// <summary>Whether some text is a run's id as the master gives them: 32 lowercase hexadecimal digits.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsRunId(string? text) => text is { Length: 32 } && text.All(static c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>A board's run's replay, with the ghost the checker saw, to watch or race; null where it is not kept.</summary>
    /// <param name="id">The run.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>Its bytes, or null.</returns>
    public async Task<byte[]?> ReplayAsync(string id, CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/runs/{Uri.EscapeDataString(id)}/replay"), cancel).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await MasterAnswers.ThrowIfRefusedAsync(response, "runs", cancel).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancel)
    {
        Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return (await JsonSerializer.DeserializeAsync(stream, type, cancel).ConfigureAwait(false))!;
    }
}

/// <summary>The JSON the runs read, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RunStatus))]
[JsonSerializable(typeof(RunBoard))]
internal sealed partial class RunsJsonContext : JsonSerializerContext;
