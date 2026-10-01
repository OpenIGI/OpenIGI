using System.Security.Cryptography;
using System.Text;
using OpenIGI.Releases;

namespace OpenIGI.Services;

/// <summary>Every published release, newest first, and the answers the endpoints give from them.</summary>
/// <remarks>
/// Immutable: the scanner builds a new one when the store changes and swaps it in, so a request never sees
/// half a scan. The tag is a hash over what every answer is built from, which is what a client's
/// <c>If-None-Match</c> is compared against.
/// </remarks>
internal sealed class ReleaseIndex
{
    private readonly IReadOnlyList<ReleaseManifest> _releases;

    /// <summary>Initializes a new instance of the <see cref="ReleaseIndex"/> class.</summary>
    /// <param name="releases">The releases found, in any order.</param>
    /// <param name="downloadUrl">The download address template (see <see cref="ReleaseOptions.DownloadUrl"/>).</param>
    public ReleaseIndex(IEnumerable<ReleaseManifest> releases, string downloadUrl)
    {
        _releases = [.. releases
            .Select(release => WithUrls(release, downloadUrl))
            .OrderByDescending(static release => release.ParsedVersion)];

        StringBuilder identity = new(downloadUrl);
        foreach (ReleaseManifest release in _releases)
        {
            identity.Append('|').Append(release.Version);
            foreach (ReleaseAsset asset in release.Assets)
            {
                identity.Append(':').Append(asset.Sha256);
            }
        }

        Tag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))[..16]}\"";
    }

    /// <summary>Gets an index with no releases in it.</summary>
    public static ReleaseIndex Empty { get; } = new([], string.Empty);

    /// <summary>Gets the entity tag of this index, quoted as HTTP wants it.</summary>
    public string Tag { get; }

    /// <summary>Gets how many releases the index holds.</summary>
    public int Count => _releases.Count;

    /// <summary>Answers what one channel offers.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="since">The version the caller has; releases newer than it are listed.</param>
    /// <returns>The channel's newest release and the releases the caller has not got.</returns>
    /// <remarks>
    /// The beta channel offers stable releases too, so a beta player is never left behind a stable release
    /// that came after their last beta.
    /// </remarks>
    public ChannelFeed Feed(ReleaseChannel channel, string? since)
    {
        List<ReleaseManifest> offered = [.. _releases.Where(release => Offers(channel, release))];
        ReleaseManifest? latest = offered.FirstOrDefault();

        IReadOnlyList<ReleaseManifest> releases = ReleaseVersion.TryParse(since, out ReleaseVersion installed)
            ? [.. offered.Where(release => release.ParsedVersion > installed)]
            : latest is null ? [] : [latest];

        return new ChannelFeed(channel, latest, releases);
    }

    private static bool Offers(ReleaseChannel channel, ReleaseManifest release) =>
        channel == ReleaseChannel.Beta || release.Channel == ReleaseChannel.Stable;

    private static ReleaseManifest WithUrls(ReleaseManifest release, string downloadUrl) =>
        release with
        {
            Assets = [.. release.Assets.Select(asset => asset with
            {
                Url = downloadUrl
                    .Replace("{version}", Uri.EscapeDataString(release.Version), StringComparison.Ordinal)
                    .Replace("{name}", Uri.EscapeDataString(asset.Name), StringComparison.Ordinal),
            })],
        };
}
