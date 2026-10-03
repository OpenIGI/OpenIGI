using System.Net;
using System.Net.Http.Headers;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>Asks the update service what is newest on a channel.</summary>
/// <remarks>
/// Asked once a minute, so it is a conditional GET: the service tags every answer, and an unchanged one
/// comes back as a bodiless 304. Each channel's last answer is kept, so changing channel shows what that
/// channel offered at once while the fresh answer is fetched. The address can be pointed elsewhere with
/// <c>OPENIGI_UPDATE_URL</c>, which is how a local copy of the service is tried against a real launcher.
/// </remarks>
internal sealed class ReleaseFeedClient(HttpClient http)
{
    /// <summary>The update service's address.</summary>
    public static Uri DefaultService { get; } = new(
        Environment.GetEnvironmentVariable("OPENIGI_UPDATE_URL") is { Length: > 0 } custom ? custom.TrimEnd('/') + "/" : "https://api.openigi.com/");

    private readonly Dictionary<string, (string? Tag, ChannelFeed Feed)> _answers = new(StringComparer.Ordinal);

    /// <summary>Fetches a channel's feed.</summary>
    /// <param name="service">The service's address.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="installed">The version installed, so the feed lists every release since it; null for none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The feed, which is the previous answer again when the service says nothing changed.</returns>
    /// <exception cref="HttpRequestException">The service could not be reached or answered with an error.</exception>
    public async Task<ChannelFeed> FetchAsync(Uri service, ReleaseChannel channel, string? installed, CancellationToken cancellationToken)
    {
        string query = Query(channel, installed);
        (string? Tag, ChannelFeed Feed)? previous = Answer(query);
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(service, query));
        if (previous is { Tag: { } tag })
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(tag));
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && previous is { } unchanged)
        {
            return unchanged.Feed;
        }

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ChannelFeed feed = ReleaseJson.ReadFeed(json);
        lock (_answers)
        {
            _answers[query] = (response.Headers.ETag?.ToString(), feed);
        }

        return feed;
    }

    /// <summary>Gets the last answer for a channel, without asking the service.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="installed">The version installed, as it would be asked with.</param>
    /// <returns>The feed, or <see langword="null"/> when that channel has not been asked about yet.</returns>
    public ChannelFeed? Cached(ReleaseChannel channel, string? installed) => Answer(Query(channel, installed))?.Feed;

    private static string Query(ReleaseChannel channel, string? installed) =>
        $"v1/channels/{channel.Name()}" + (installed is null ? string.Empty : $"?since={Uri.EscapeDataString(installed)}");

    private (string? Tag, ChannelFeed Feed)? Answer(string query)
    {
        lock (_answers)
        {
            return _answers.TryGetValue(query, out (string? Tag, ChannelFeed Feed) answer) ? answer : null;
        }
    }
}
