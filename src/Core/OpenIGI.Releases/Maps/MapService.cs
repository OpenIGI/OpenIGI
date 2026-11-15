using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenIGI.Releases.Maps;

/// <summary>
/// The master server's community maps (<c>/v1/maps</c>): what is published, a map's package, and publishing, rating, reporting
/// and taking down, each signed by the installation's key (<see cref="MapIdentity"/>).
/// </summary>
/// <param name="http">The client to ask through.</param>
/// <param name="master">The master server; <see cref="DefaultMaster"/> unless a test or <c>OPENIGI_MAPS_URL</c> says otherwise.</param>
public sealed class MapService(HttpClient http, Uri master)
{
    /// <summary>The master server the game knows.</summary>
    public static readonly Uri DefaultMaster = new("https://master.openigi.com");

    /// <summary>Gets the master server to use: <c>OPENIGI_MAPS_URL</c> when set (a local server, for a test), else <see cref="DefaultMaster"/>.</summary>
    public static Uri Master =>
        Environment.GetEnvironmentVariable("OPENIGI_MAPS_URL") is { Length: > 0 } local && Uri.TryCreate(local, UriKind.Absolute, out Uri? uri)
            ? uri
            : DefaultMaster;

    /// <summary>Lists what is published.</summary>
    /// <param name="game">The game, by title id, or <see langword="null"/> for every game.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The maps, the most downloaded first.</returns>
    public async Task<IReadOnlyList<MapListing>> ListAsync(string? game, CancellationToken cancel = default)
    {
        string query = game is null ? string.Empty : $"?game={Uri.EscapeDataString(game)}";
        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/maps{query}"), cancel).ConfigureAwait(false);
        await ThrowIfRefused(response, cancel).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false), MapJsonContext.Default.IReadOnlyListMapListing, cancel).ConfigureAwait(false) ?? [];
    }

    /// <summary>Downloads a map's package, checked against its listing's hash.</summary>
    /// <param name="map">The map.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The package.</returns>
    /// <exception cref="InvalidDataException">What came is not the package listed.</exception>
    public async Task<byte[]> DownloadAsync(MapListing map, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        using HttpResponseMessage response = await http.GetAsync(new Uri(master, $"/v1/maps/{Uri.EscapeDataString(map.Id)}/package"), cancel).ConfigureAwait(false);
        await ThrowIfRefused(response, cancel).ConfigureAwait(false);
        byte[] package = await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
        string expected = response.Headers.TryGetValues("x-openigi-map-sha256", out IEnumerable<string>? sent) ? sent.First() : map.Sha256;
        if (MapPackage.Hash(package) != expected)
        {
            throw new InvalidDataException($"{map.Title} did not download whole; try again.");
        }

        return package;
    }

    /// <summary>Publishes a map, or a new version of one of the installation's own.</summary>
    /// <param name="package">The package.</param>
    /// <param name="identity">The installation's key.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The map as it is now listed.</returns>
    /// <exception cref="MapRefusedException">The server would not take it, and why.</exception>
    public async Task<MapListing> PublishAsync(byte[] package, MapIdentity identity, CancellationToken cancel = default) =>
        await SendAsync<MapListing>(HttpMethod.Post, "/v1/maps", package, identity, "application/octet-stream", cancel).ConfigureAwait(false);

    /// <summary>Rates a map, one to five stars; a second rating replaces the first.</summary>
    /// <param name="id">The map.</param>
    /// <param name="stars">The stars.</param>
    /// <param name="identity">The installation's key.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>The map as it is now listed.</returns>
    public async Task<MapListing> RateAsync(string id, int stars, MapIdentity identity, CancellationToken cancel = default) =>
        await SendAsync<MapListing>(HttpMethod.Post, $"/v1/maps/{Uri.EscapeDataString(id)}/rate", Encoding.UTF8.GetBytes($$"""{"stars":{{stars}}}"""), identity, "application/json", cancel).ConfigureAwait(false);

    /// <summary>Reports a map as broken or not right to have.</summary>
    /// <param name="id">The map.</param>
    /// <param name="reason">Why, in a few words.</param>
    /// <param name="identity">The installation's key.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>A task.</returns>
    public async Task ReportAsync(string id, string reason, MapIdentity identity, CancellationToken cancel = default) =>
        await SendAsync<JsonElement>(HttpMethod.Post, $"/v1/maps/{Uri.EscapeDataString(id)}/report", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, string> { ["reason"] = reason }, MapJsonContext.Default.DictionaryStringString)), identity, "application/json", cancel).ConfigureAwait(false);

    /// <summary>Takes down one of the installation's own maps.</summary>
    /// <param name="id">The map.</param>
    /// <param name="identity">The installation's key.</param>
    /// <param name="cancel">Stops it.</param>
    /// <returns>A task.</returns>
    public async Task RemoveAsync(string id, MapIdentity identity, CancellationToken cancel = default) =>
        await SendAsync<JsonElement>(HttpMethod.Delete, $"/v1/maps/{Uri.EscapeDataString(id)}", [], identity, "application/octet-stream", cancel).ConfigureAwait(false);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, byte[] body, MapIdentity identity, string type, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(identity);
        using HttpRequestMessage request = new(method, new Uri(master, path)) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        foreach ((string name, string value) in identity.Sign(method.Method, path, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancel).ConfigureAwait(false);
        await ThrowIfRefused(response, cancel).ConfigureAwait(false);
        Stream stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        return typeof(T) == typeof(MapListing)
            ? (T)(object)(await JsonSerializer.DeserializeAsync(stream, MapJsonContext.Default.MapListing, cancel).ConfigureAwait(false))!
            : (T)(object)(await JsonSerializer.DeserializeAsync(stream, MapJsonContext.Default.JsonElement, cancel).ConfigureAwait(false));
    }

    // A refusal read as what the server says of it.
    private static async Task ThrowIfRefused(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = $"The map server said {(int)response.StatusCode}.";
        IReadOnlyList<string> problems = [];
        try
        {
            using JsonDocument said = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false), cancellationToken: cancel).ConfigureAwait(false);
            if (said.RootElement.TryGetProperty("error", out JsonElement error) && error.GetString() is { Length: > 0 } text)
            {
                message = text;
            }

            if (said.RootElement.TryGetProperty("problems", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                problems = [.. list.EnumerateArray().Select(static p => p.GetString() ?? string.Empty)];
            }
        }
        catch (JsonException)
        {
            // Not the server's JSON: its status says enough.
        }

        throw new MapRefusedException(message, response.StatusCode, problems);
    }
}

/// <summary>The map server would not do what was asked, and why.</summary>
public sealed class MapRefusedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MapRefusedException"/> class.</summary>
    /// <param name="message">What the server said.</param>
    /// <param name="status">Its status.</param>
    /// <param name="problems">What it found wrong with a map, a line each.</param>
    public MapRefusedException(string message, HttpStatusCode status, IReadOnlyList<string> problems)
        : base(message)
    {
        Status = status;
        Problems = problems;
    }

    /// <summary>Initializes a new instance of the <see cref="MapRefusedException"/> class.</summary>
    public MapRefusedException()
        : this("The map server refused.", HttpStatusCode.BadRequest, [])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MapRefusedException"/> class.</summary>
    /// <param name="message">The message.</param>
    public MapRefusedException(string message)
        : this(message, HttpStatusCode.BadRequest, [])
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MapRefusedException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">What it came of.</param>
    public MapRefusedException(string message, Exception inner)
        : base(message, inner)
    {
        Problems = [];
    }

    /// <summary>Gets the server's status.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>Gets what the server found wrong with a map, a line each.</summary>
    public IReadOnlyList<string> Problems { get; } = [];
}
