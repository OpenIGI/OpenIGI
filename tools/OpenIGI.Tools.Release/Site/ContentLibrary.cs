using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Every published version of every optional content pack, as the site serves them.</summary>
/// <remarks>
/// The manifests are kept as the JSON they were read from, which is what the site hands out: the store's own
/// bytes, never a reserialization that could drift from what was published.
/// </remarks>
internal sealed class ContentLibrary
{
    private readonly Dictionary<string, Dictionary<string, string>> _manifests;
    private readonly Dictionary<string, string> _newest;

    /// <summary>Initializes a new instance of the <see cref="ContentLibrary"/> class.</summary>
    /// <param name="versions">Every manifest found, with the JSON it was read from.</param>
    /// <param name="chunkUrl">Where a chunk is downloaded from (see <see cref="ContentIndex.ChunkUrl"/>).</param>
    /// <param name="packs">Each pack's own description (its name, summary and picture), by pack.</param>
    /// <param name="thumbnailUrl">Where a pack's picture is served from, with <c>{pack}</c> and <c>{name}</c>.</param>
    public ContentLibrary(
        IEnumerable<(ContentManifest Manifest, string Json)> versions,
        string chunkUrl,
        IReadOnlyDictionary<string, ContentPackInfo>? packs = null,
        string thumbnailUrl = "")
    {
        ArgumentNullException.ThrowIfNull(versions);

        _manifests = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, ContentManifest> newest = new(StringComparer.Ordinal);

        foreach ((ContentManifest manifest, string json) in versions)
        {
            if (!_manifests.TryGetValue(manifest.Id, out Dictionary<string, string>? byVersion))
            {
                _manifests[manifest.Id] = byVersion = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            byVersion[manifest.Version] = json;

            if (!newest.TryGetValue(manifest.Id, out ContentManifest? current)
                || manifest.ParsedVersion > current.ParsedVersion)
            {
                newest[manifest.Id] = manifest;
            }
        }

        _newest = newest.ToDictionary(static pair => pair.Key, static pair => pair.Value.Version, StringComparer.Ordinal);

        // A pack's own description goes over its newest version's: renamed, described again or pictured
        // without a version being published. One for a pack with no version yet lists nothing.
        string Picture(string pack, string name) =>
            thumbnailUrl.Replace("{pack}", pack, StringComparison.Ordinal).Replace("{name}", name, StringComparison.Ordinal);
        List<ContentPack> listed = [];
        foreach (ContentManifest manifest in newest.Values.OrderBy(static manifest => manifest.Id, StringComparer.Ordinal))
        {
            ContentPack listing = manifest.Listing;
            if (packs?.GetValueOrDefault(manifest.Id) is { } info)
            {
                listing = info.Apply(listing, Picture);
            }

            listed.Add(listing);
        }

        // Mods in an index of their own: a launcher that reads the content index knows the kinds it was built with.
        Index = new ContentIndex(chunkUrl, [.. listed.Where(static pack => pack.Kind != ContentKind.Mod)]);
        Mods = new ContentIndex(chunkUrl, [.. listed.Where(static pack => pack.Kind == ContentKind.Mod)]);
        Count = _manifests.Values.Sum(static versions => versions.Count);
    }

    /// <summary>Gets a library with nothing in it, for before the first scan.</summary>
    public static ContentLibrary Empty { get; } = new([], string.Empty);

    /// <summary>Gets what the content index lists: every pack's newest version, mods aside.</summary>
    public ContentIndex Index { get; }

    /// <summary>Gets what the mods index lists: every mod's newest version.</summary>
    public ContentIndex Mods { get; }

    /// <summary>Gets how many pack versions there are.</summary>
    public int Count { get; }

    /// <summary>Gets every version of every pack, with its manifest's JSON.</summary>
    public IEnumerable<(string Id, string Version, string Json)> Versions =>
        _manifests.SelectMany(static pack => pack.Value.Select(version => (pack.Key, version.Key, version.Value)));

    /// <summary>Finds a manifest's JSON.</summary>
    /// <param name="id">The pack.</param>
    /// <param name="version">The version, or <see langword="null"/> for the newest.</param>
    /// <returns>The manifest's JSON, or <see langword="null"/> when there is no such version.</returns>
    public string? Manifest(string id, string? version)
    {
        if (!_manifests.TryGetValue(id, out Dictionary<string, string>? byVersion))
        {
            return null;
        }

        return byVersion.GetValueOrDefault(version ?? _newest[id]);
    }
}
