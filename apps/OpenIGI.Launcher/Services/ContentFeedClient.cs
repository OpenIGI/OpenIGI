using System.Net;
using System.Net.Http.Headers;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>Asks the update service which optional content packs there are, and fetches their manifests.</summary>
/// <remarks>
/// The index is asked with the game's feed, once a minute, so it is a conditional GET like the feed's. A
/// manifest is only fetched when a pack is installed or updated: it lists every chunk of every file, which is
/// more than the index ever needs to carry.
/// </remarks>
internal sealed class ContentFeedClient(HttpClient http)
{
    private string? _tag;
    private ContentIndex? _last;

    /// <summary>Fetches the content index.</summary>
    /// <param name="service">The service's address.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The index, which is the previous answer again when the service says nothing changed.</returns>
    /// <exception cref="HttpRequestException">The service could not be reached or answered with an error.</exception>
    public async Task<ContentIndex> FetchIndexAsync(Uri service, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(service, "v1/content"));
        if (_tag is not null && _last is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(_tag));
        }

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && _last is not null)
        {
            return _last;
        }

        response.EnsureSuccessStatusCode();
        _last = ReleaseJson.ReadContentIndex(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        _tag = response.Headers.ETag?.ToString();
        return _last;
    }

    /// <summary>Fetches one version's manifest.</summary>
    /// <param name="service">The service's address.</param>
    /// <param name="pack">The pack.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="HttpRequestException">The service could not be reached or answered with an error.</exception>
    /// <exception cref="InvalidDataException">The service answered with a different pack or version.</exception>
    public async Task<ContentManifest> FetchManifestAsync(Uri service, ContentPack pack, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pack);
        Uri address = new(service, $"v1/content/{Uri.EscapeDataString(pack.Id)}/{Uri.EscapeDataString(pack.Version)}");
        string json = await http.GetStringAsync(address, cancellationToken).ConfigureAwait(false);
        ContentManifest manifest = ReleaseJson.ReadContent(json);
        return manifest.Id == pack.Id && manifest.Version == pack.Version
            ? manifest
            : throw new InvalidDataException($"The service sent {manifest.Id} {manifest.Version} for {pack.Id} {pack.Version}.");
    }
}
