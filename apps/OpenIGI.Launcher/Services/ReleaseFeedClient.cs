using System.Net;
using System.Net.Http.Headers;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>Asks the update service what is newest on a channel.</summary>
/// <remarks>
/// Asked once a minute, so it is a conditional GET: the service tags every answer, and an unchanged one
/// comes back as a bodiless 304. The address can be pointed elsewhere with <c>OPENIGI_UPDATE_URL</c>, which
/// is how a local copy of the service is tried against a real launcher.
/// </remarks>
internal sealed class ReleaseFeedClient(HttpClient http)
{
    /// <summary>The update service's address.</summary>
    public static Uri DefaultService { get; } = new(
        Environment.GetEnvironmentVariable("OPENIGI_UPDATE_URL") is { Length: > 0 } custom ? custom.TrimEnd('/') + "/" : "https://api.openigi.com/");

    private string? _tag;
    private string? _tagQuery;
    private ChannelFeed? _last;

    /// <summary>Fetches a channel's feed.</summary>
    /// <param name="service">The service's address.</param>
    /// <param name="channel">The channel.</param>
    /// <param name="installed">The version installed, so the feed lists every release since it; null for none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The feed, which is the previous answer again when the service says nothing changed.</returns>
    /// <exception cref="HttpRequestException">The service could not be reached or answered with an error.</exception>
    public async Task<ChannelFeed> FetchAsync(Uri service, ReleaseChannel channel, string? installed, CancellationToken cancellationToken)
    {
        string query = $"v1/channels/{channel.Name()}" + (installed is null ? string.Empty : $"?since={Uri.EscapeDataString(installed)}");
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(service, query));
        if (_tag is not null && _tagQuery == query && _last is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(_tag));
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && _last is not null)
        {
            return _last;
        }

        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _last = ReleaseJson.ReadFeed(json);
        _tag = response.Headers.ETag?.ToString();
        _tagQuery = query;
        return _last;
    }
}
