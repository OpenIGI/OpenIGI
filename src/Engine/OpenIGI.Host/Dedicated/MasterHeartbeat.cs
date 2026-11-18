using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net.Browsing;
using OpenIGI.Releases;

namespace OpenIGI.Host.Dedicated;

/// <summary>What a dedicated server tells the master server of itself, each beat.</summary>
/// <param name="Port">Its UDP port.</param>
/// <param name="Title">The game's id.</param>
/// <param name="Protocol">The protocol's id, as hex.</param>
/// <param name="Name">Its name.</param>
/// <param name="Map">The map played.</param>
/// <param name="Mode">The mode played.</param>
/// <param name="Version">The build.</param>
/// <param name="Players">Its players now.</param>
/// <param name="MaxPlayers">The most it takes.</param>
/// <param name="Passworded">Whether it asks a password.</param>
/// <param name="Region">Its region, or empty.</param>
/// <param name="Room">Its WebRTC room's code, by which browsers join it, or empty.</param>
public sealed record HeartbeatBody(
    int Port, string Title, string Protocol, string Name, string Map, string Mode, string Version, int Players, int MaxPlayers,
    bool Passworded, string Region, string Room);

/// <summary>What a stopping server tells the master: which of its ports leaves the list.</summary>
/// <param name="Port">The port.</param>
public sealed record ShutdownBody(int Port);

/// <summary>
/// Keeps a dedicated server on the master server's list (<c>POST /v1/servers/heartbeat</c>): a beat as it starts and every
/// <see cref="IntervalSeconds"/> after, inside the master's 90 seconds; a <c>shutdown</c> as it stops, so it leaves the list at once.
/// </summary>
/// <remarks>
/// Each beat runs off the loop's thread and is never waited on: a master that is slow or down costs the game nothing, and the
/// next beat simply tries again. What the master answered is told the first time and whenever it changes. Each beat is
/// signed by the server's own key, where it has one: the master lists a server whose key it has registered as official.
/// </remarks>
/// <param name="master">The master server.</param>
/// <param name="logger">Where its answers are told.</param>
/// <param name="key">The server's own key, which its matches are reported under; <see langword="null"/> for unsigned beats.</param>
public sealed class MasterHeartbeat(Uri master, IOpenIgiLogger logger, IRequestSigner? key = null) : IDisposable
{
    /// <summary>The seconds between beats.</summary>
    public const double IntervalSeconds = 30;

    private const string LogCategory = "Master";
    private const string HeartbeatPath = "/v1/servers/heartbeat";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private double _lastBeat = double.NegativeInfinity;
    private Task? _inFlight;
    private string? _lastAnswer;
    private int _port;

    /// <summary>Beats when one is due.</summary>
    /// <param name="now">The clock, in seconds.</param>
    /// <param name="body">What the server is now.</param>
    public void Tick(double now, Func<HeartbeatBody> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (now - _lastBeat < IntervalSeconds || _inFlight is { IsCompleted: false })
        {
            return;
        }

        _lastBeat = now;
        HeartbeatBody beat = body();
        _port = beat.Port;
        _inFlight = Beat(beat);
    }

    /// <summary>Takes the server off the list.</summary>
    public void Shutdown()
    {
        try
        {
            using HttpResponseMessage _ = _http.PostAsJsonAsync(new Uri(master, "/v1/servers/shutdown"), new ShutdownBody(_port), DedicatedJsonContext.Default.ShutdownBody).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _ = ex;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    private async Task Beat(HeartbeatBody beat)
    {
        string answer;
        try
        {
            using HttpRequestMessage request = await RequestOf(beat).ConfigureAwait(false);
            using HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            answer = !response.IsSuccessStatusCode
                ? $"{master.Host} refused the listing: {(int)response.StatusCode} {text}"
                : IsOfficial(text) ? $"listed on {master.Host} as an official server" : $"listed on {master.Host}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            answer = $"{master.Host} could not be reached ({ex.Message}); trying again in {IntervalSeconds:0} s";
        }

        if (answer != _lastAnswer)
        {
            _lastAnswer = answer;
            logger.Information(LogCategory, answer);
        }
    }

    // A beat as it is sent: its body, signed by the server's key where it has one.
    private async Task<HttpRequestMessage> RequestOf(HeartbeatBody beat)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(beat, DedicatedJsonContext.Default.HeartbeatBody);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(master, HeartbeatPath))
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
        };
        if (key is not null)
        {
            foreach ((string name, string value) in await SignedRequest.HeadersAsync(key, "POST", HeartbeatPath, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body).ConfigureAwait(false))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    // Whether the master's answer lists the server as official.
    private static bool IsOfficial(string answer)
    {
        try
        {
            using JsonDocument read = JsonDocument.Parse(answer);
            return read.RootElement.TryGetProperty("official", out JsonElement official) && official.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Makes a beat's body for a session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="config">The server's settings.</param>
    /// <param name="titleId">The game's id.</param>
    /// <param name="version">The build.</param>
    /// <param name="roomCode">The room's code, or <see langword="null"/>.</param>
    /// <returns>The body.</returns>
    public static HeartbeatBody BodyOf(Multiplayer.HostedSession session, ServerConfig config, string titleId, string version, string? roomCode)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(config);
        ServerInfo info = session.Info();
        return new HeartbeatBody(
            config.Port,
            titleId,
            MultiplayerProtocol.IdFor(titleId).ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
            config.Name,
            info.Map,
            info.Mode,
            version,
            info.Players,
            config.MaxPlayers,
            config.Password.Length > 0,
            config.Region,
            roomCode ?? string.Empty);
    }
}
