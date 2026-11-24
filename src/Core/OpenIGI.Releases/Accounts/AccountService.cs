using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Releases.Accounts;

/// <summary>A player's account as anyone may see it: their Discord name and avatar.</summary>
/// <param name="Id">The account's id, its Discord user id.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Avatar">The avatar's address.</param>
/// <param name="Since">When it was made, Unix milliseconds.</param>
public sealed record PlayerAccount(string Id, string Name, string Avatar, long Since);

/// <summary>A link begun: the code and the page that finishes it.</summary>
/// <param name="Code">The code the game asks about.</param>
/// <param name="Url">The page the player opens to approve it with Discord.</param>
/// <param name="ExpiresIn">Seconds until it lapses.</param>
public sealed record LinkStart(string Code, string Url, int ExpiresIn);

/// <summary>How a link is doing.</summary>
/// <param name="State"><c>pending</c>, <c>linked</c>, <c>expired</c> or <c>unknown</c>.</param>
/// <param name="Account">The account, once linked.</param>
public sealed record LinkState(string State, PlayerAccount? Account)
{
    /// <summary>Gets whether it is linked.</summary>
    [JsonIgnore]
    public bool IsLinked => State == "linked" && Account is not null;

    /// <summary>Gets whether it is still waiting on the player.</summary>
    [JsonIgnore]
    public bool IsPending => State == "pending";
}

/// <summary>One install's key on an account.</summary>
/// <param name="Id">The key's id (PlayerIdentities.IdOf).</param>
/// <param name="Label">What the install called itself when it linked.</param>
/// <param name="Added">When it was linked, Unix milliseconds.</param>
/// <param name="Current">Whether it is the key that asked.</param>
public sealed record AccountKey(string Id, string Label, long Added, bool Current);

/// <summary>The account the asking key is linked to, if any.</summary>
/// <param name="Linked">Whether the key is linked.</param>
/// <param name="Player">The key's id.</param>
/// <param name="Account">The account.</param>
/// <param name="Keys">Every key on the account.</param>
public sealed record OwnAccount(bool Linked, string Player, PlayerAccount? Account, IReadOnlyList<AccountKey>? Keys);

/// <summary>The profile an account's games keep in step, as the master holds it.</summary>
/// <param name="Data">The profile's document, or <see langword="null"/> before any game has written one.</param>
/// <param name="Updated">When it was written, Unix milliseconds; 0 before.</param>
public sealed record RemoteProfile(JsonElement? Data, long Updated);

/// <summary>An account request the master server refused.</summary>
public sealed class AccountRefusedException(string message, HttpStatusCode status) : Exception(message)
{
    /// <summary>Gets the status it answered.</summary>
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// The master server's accounts (tools/master-server/src/accounts.js): an install's player key linked to a Discord account,
/// so the player's name, avatar and stats follow them. Every request about one's own account is signed by the key the
/// player plays under; looking players up is open.
/// </summary>
/// <param name="http">The client to ask through.</param>
/// <param name="master">The master server.</param>
public sealed class AccountService(HttpClient http, Uri master)
{
    /// <summary>Begins linking the key to a Discord account.</summary>
    /// <param name="key">The key the player plays under.</param>
    /// <param name="label">What this install is called on the account, such as "Desktop".</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The code to ask about, and the page the player opens.</returns>
    public async Task<LinkStart> StartLinkAsync(IRequestSigner key, string label, CancellationToken cancel = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["label"] = label }, AccountJsonContext.Default.DictionaryStringString);
        return await SendAsync(key, HttpMethod.Post, "/v1/link", body, AccountJsonContext.Default.LinkStart, cancel).ConfigureAwait(false);
    }

    /// <summary>Asks how a link is doing.</summary>
    /// <param name="key">The key that began it.</param>
    /// <param name="code">Its code.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>Its state.</returns>
    public async Task<LinkState> LinkStateAsync(IRequestSigner key, string code, CancellationToken cancel = default)
    {
        try
        {
            return await SendAsync(key, HttpMethod.Get, $"/v1/link/{Uri.EscapeDataString(code)}", [], AccountJsonContext.Default.LinkState, cancel).ConfigureAwait(false);
        }
        catch (AccountRefusedException refused) when (refused.Status == HttpStatusCode.NotFound)
        {
            return new LinkState("unknown", null);
        }
    }

    /// <summary>Gets the account the key is linked to.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The account, or <see cref="OwnAccount.Linked"/> false.</returns>
    public Task<OwnAccount> OwnAsync(IRequestSigner key, CancellationToken cancel = default) =>
        SendAsync(key, HttpMethod.Get, "/v1/account", [], AccountJsonContext.Default.OwnAccount, cancel);

    /// <summary>Takes this key off its account; the account keeps its other keys.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>A task.</returns>
    public Task UnlinkAsync(IRequestSigner key, CancellationToken cancel = default) =>
        SendAsync(key, HttpMethod.Delete, "/v1/link", [], AccountJsonContext.Default.JsonElement, cancel);

    /// <summary>Deletes the whole account the key is linked to, and everything kept of it.</summary>
    /// <param name="key">One of its keys.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>A task.</returns>
    public Task DeleteAccountAsync(IRequestSigner key, CancellationToken cancel = default) =>
        SendAsync(key, HttpMethod.Delete, "/v1/account", [], AccountJsonContext.Default.JsonElement, cancel);

    /// <summary>Reads the profile the account's games keep in step.</summary>
    /// <param name="key">A key linked to the account.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The profile, or one with no data before any game has written it.</returns>
    public Task<RemoteProfile> GetProfileAsync(IRequestSigner key, CancellationToken cancel = default) =>
        SendAsync(key, HttpMethod.Get, "/v1/account/profile", [], AccountJsonContext.Default.RemoteProfile, cancel);

    /// <summary>Writes the account's profile: the newest write wins, so a game merges what it read before it writes.</summary>
    /// <param name="key">A key linked to the account.</param>
    /// <param name="data">The profile's document.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>When the master kept it, Unix milliseconds.</returns>
    public async Task<long> PutProfileAsync(IRequestSigner key, JsonElement data, CancellationToken cancel = default)
    {
        using MemoryStream body = new();
        using (Utf8JsonWriter writer = new(body))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("data");
            data.WriteTo(writer);
            writer.WriteEndObject();
        }

        JsonElement kept = await SendAsync(key, HttpMethod.Put, "/v1/account/profile", body.ToArray(), AccountJsonContext.Default.JsonElement, cancel).ConfigureAwait(false);
        return kept.TryGetProperty("updated", out JsonElement updated) && updated.TryGetInt64(out long at) ? at : 0;
    }

    /// <summary>Looks players up by their keys' ids, as a game server does for its scoreboard.</summary>
    /// <param name="playerIds">The ids (at most 64 are looked up).</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The accounts of those that are linked, by id.</returns>
    public async Task<IReadOnlyDictionary<string, PlayerAccount>> LookupAsync(IEnumerable<string> playerIds, CancellationToken cancel = default)
    {
        string ids = string.Join(',', playerIds.Distinct(StringComparer.Ordinal).Take(64).Select(Uri.EscapeDataString));
        if (ids.Length == 0)
        {
            return new Dictionary<string, PlayerAccount>();
        }

        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/players?ids={ids}"), cancel).ConfigureAwait(false);
        await ThrowIfRefused(response, cancel).ConfigureAwait(false);
        Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, AccountJsonContext.Default.DictionaryStringPlayerAccount, cancel).ConfigureAwait(false) ?? [];
    }

    /// <summary>Looks one player up by their account's id or a key's, as their record on the leaderboard names them.</summary>
    /// <param name="player">The id.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The account, or <see langword="null"/> for a player with none.</returns>
    public async Task<PlayerAccount?> PlayerAsync(string player, CancellationToken cancel = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(player);
        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/players/{Uri.EscapeDataString(player)}"), cancel).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await ThrowIfRefused(response, cancel).ConfigureAwait(false);
        Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, AccountJsonContext.Default.PlayerAccount, cancel).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(IRequestSigner key, HttpMethod method, string path, byte[] body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancel)
    {
        using HttpRequestMessage request = await SignedRequest.CreateAsync(key, method, master, path, body).ConfigureAwait(false);
        using HttpResponseMessage response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        await ThrowIfRefused(response, cancel).ConfigureAwait(false);
        Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return (await JsonSerializer.DeserializeAsync(stream, type, cancel).ConfigureAwait(false))!;
    }

    private static async Task ThrowIfRefused(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = $"The account server said {(int)response.StatusCode}.";
        try
        {
            string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            using JsonDocument said = JsonDocument.Parse(text);
            if (said.RootElement.TryGetProperty("error", out JsonElement error) && error.GetString() is { Length: > 0 } reason)
            {
                message = reason;
            }
        }
        catch (JsonException)
        {
            // Not the server's JSON: its status says enough.
        }

        throw new AccountRefusedException(message, response.StatusCode);
    }
}

/// <summary>The JSON the accounts read and write, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LinkStart))]
[JsonSerializable(typeof(LinkState))]
[JsonSerializable(typeof(OwnAccount))]
[JsonSerializable(typeof(PlayerAccount))]
[JsonSerializable(typeof(RemoteProfile))]
[JsonSerializable(typeof(Dictionary<string, PlayerAccount>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class AccountJsonContext : JsonSerializerContext;
