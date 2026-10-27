using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Every published release, newest first, and the feeds and download links the site serves from them.</summary>
internal sealed class ReleaseIndex
{
    private readonly IReadOnlyList<ReleaseManifest> _releases;

    /// <summary>Initializes a new instance of the <see cref="ReleaseIndex"/> class.</summary>
    /// <param name="releases">The releases found, in any order, each with the store folder it is in.</param>
    /// <param name="downloadUrl">
    /// Where an archive is downloaded from: <c>{folder}</c> (the release's folder), <c>{tag}</c> (its git tag),
    /// <c>{version}</c> and <c>{name}</c> are replaced per asset.
    /// </param>
    public ReleaseIndex(IEnumerable<(ReleaseManifest Release, string Folder)> releases, string downloadUrl)
    {
        _releases = [.. releases
            .Select(found => WithUrls(found.Release, found.Folder, downloadUrl))
            .OrderByDescending(static release => release.ParsedVersion)];
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

    /// <summary>Gets how many releases the index holds.</summary>
    public int Count => _releases.Count;

    /// <summary>Answers what one channel offers to every caller at once, as a file the site serves.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="keep">How many of the channel's newest releases are listed.</param>
    /// <returns>The channel's newest release and its <paramref name="keep"/> newest releases.</returns>
    /// <remarks>
    /// A static file cannot answer <c>?since=</c>, so it lists the recent releases and the launcher keeps the ones
    /// newer than its own; a launcher further behind than <paramref name="keep"/> releases sees the newest notes only.
    /// </remarks>
    public ChannelFeed Published(ReleaseChannel channel, int keep)
    {
        List<ReleaseManifest> offered = [.. _releases.Where(release => Offers(channel, release)).Take(keep)];
        return new ChannelFeed(channel, offered.FirstOrDefault(), offered, NewestLauncher());
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
