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
    /// <param name="releases">The releases found, in any order, each with the store folder it is in.</param>
    /// <param name="downloadUrl">The download address template (see <see cref="ReleaseOptions.DownloadUrl"/>).</param>
    public ReleaseIndex(IEnumerable<(ReleaseManifest Release, string Folder)> releases, string downloadUrl)
    {
        _releases = [.. releases
            .Select(found => WithUrls(found.Release, found.Folder, downloadUrl))
            .OrderByDescending(static release => release.ParsedVersion)];

        StringBuilder identity = new(downloadUrl);
        foreach (ReleaseManifest release in _releases)
        {
            identity.Append('|').Append(release.ReleaseTag);
            foreach (ReleaseAsset asset in release.Assets)
            {
                identity.Append(':').Append(asset.Sha256);
            }
        }

        Tag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))[..16]}\"";
    }

    /// <summary>Initializes a new instance of the <see cref="ReleaseIndex"/> class, each release in the folder named for its version.</summary>
    /// <param name="releases">The releases, in any order.</param>
    /// <param name="downloadUrl">The download address template.</param>
    public ReleaseIndex(IEnumerable<ReleaseManifest> releases, string downloadUrl)
        : this(releases.Select(static release => (release, release.Version)), downloadUrl)
    {
    }

    /// <summary>Gets an index with no releases in it.</summary>
    public static ReleaseIndex Empty { get; } = new(Array.Empty<ReleaseManifest>(), string.Empty);

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
    /// that came after their last beta. A release of the launcher alone is never a game update.
    /// </remarks>
    public ChannelFeed Feed(ReleaseChannel channel, string? since)
    {
        List<ReleaseManifest> offered = [.. _releases.Where(release => Offers(channel, release))];
        ReleaseManifest? latest = offered.FirstOrDefault();

        IReadOnlyList<ReleaseManifest> releases = ReleaseVersion.TryParse(since, out ReleaseVersion installed)
            ? [.. offered.Where(release => release.ParsedVersion > installed)]
            : latest is null ? [] : [latest];

        return new ChannelFeed(channel, latest, releases, NewestLauncher());
    }

    /// <summary>The newest stable release with a launcher; for one runtime when one is named.</summary>
    /// <param name="runtime">The runtime the launcher must exist for, or <see langword="null"/> for any.</param>
    /// <returns>The release, or <see langword="null"/> before the first stable launcher is published.</returns>
    /// <remarks>
    /// The launcher has one channel, stable, and its own release line (<c>launcher-v</c> tags), whichever
    /// channel a player's games follow. v0.1.0 shipped the first one inside a game release, which still
    /// counts; a launcher found in a beta release (the first betas shipped one) is never offered.
    /// </remarks>
    public ReleaseManifest? NewestLauncher(string? runtime = null) =>
        _releases.FirstOrDefault(release => release.Channel == ReleaseChannel.Stable && release.Assets.Any(asset =>
            asset.Component == ReleaseComponent.Launcher
            && asset.SelfContained
            && (runtime is null || string.Equals(asset.Runtime, runtime, StringComparison.OrdinalIgnoreCase))));

    private static bool Offers(ReleaseChannel channel, ReleaseManifest release) =>
        release.HasGame && (channel == ReleaseChannel.Beta || release.Channel == ReleaseChannel.Stable);

    // {folder} is the release's folder on the download store, {tag} its git tag (for GitHub's release
    // downloads), {version} its version and {name} the file.
    private static ReleaseManifest WithUrls(ReleaseManifest release, string folder, string downloadUrl) =>
        release with
        {
            Assets = [.. release.Assets.Select(asset => asset with
            {
                Url = downloadUrl
                    .Replace("{folder}", Uri.EscapeDataString(folder), StringComparison.Ordinal)
                    .Replace("{tag}", Uri.EscapeDataString(release.ReleaseTag), StringComparison.Ordinal)
                    .Replace("{version}", Uri.EscapeDataString(release.Version), StringComparison.Ordinal)
                    .Replace("{name}", Uri.EscapeDataString(asset.Name), StringComparison.Ordinal),
            })],
        };
}
