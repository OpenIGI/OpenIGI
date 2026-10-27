using System.Text;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>The content store on a GitHub repository's releases, which the site lists and players download from.</summary>
/// <remarks>
/// <para>
/// Chunks are files of the release <c>chunks-&lt;first two hex digits of the hash&gt;</c>, named <c>&lt;hash&gt;.br</c>,
/// so the index's chunk address is <c>https://github.com/&lt;repository&gt;/releases/download/chunks-{h2}/{hash}.br</c>.
/// Sharding by the prefix keeps every release far below the number of files one may hold.
/// </para>
/// <para>
/// What describes the packs is the release <c>manifests</c>: <c>&lt;pack&gt;_&lt;version&gt;.content.json</c> for each
/// version, <c>&lt;pack&gt;.pack.json</c> and <c>&lt;pack&gt;.thumbnail-….png</c> for its own description. A pack's id
/// holds neither <c>_</c> nor <c>.</c>, so a name splits one way only. <see cref="Unpack"/> lays them out as a
/// <see cref="FolderStore"/>, which is what the site is built from.
/// </para>
/// <para>
/// Chunks go up first and a version's manifest last, so a version the site can see has every chunk it names. Each
/// change starts the site's build, which publishes it.
/// </para>
/// </remarks>
/// <param name="repository">The store's repository, <c>owner/name</c>.</param>
/// <param name="site">The repository whose <c>site.yml</c> publishes the store, or <see langword="null"/> to leave that to the caller.</param>
internal sealed class GitHubStore(string repository, string? site) : IContentStore
{
    /// <summary>The release holding the manifests and the packs' descriptions.</summary>
    public const string ManifestsTag = "manifests";

    private const string ChunksTagPrefix = "chunks-";
    private const string ContentSuffix = ".content.json";
    private const string PackSuffix = ".pack.json";

    /// <summary>The chunk address of a store in a repository, for the site's content index.</summary>
    /// <param name="repository">The repository.</param>
    /// <returns>The address template.</returns>
    public static string ChunkUrl(string repository) =>
        $"https://github.com/{repository}/releases/download/{ChunksTagPrefix}{{h2}}/{{hash}}.br";

    /// <summary>Downloads the store's manifests and descriptions and lays them out as a <see cref="FolderStore"/>.</summary>
    /// <param name="repository">The store's repository.</param>
    /// <param name="folder">Where the store is laid out.</param>
    public static void Unpack(string repository, string folder)
    {
        Directory.CreateDirectory(folder);
        if (!GitHub.Releases(repository).Any(static release => release.Tag == ManifestsTag))
        {
            return;
        }

        string download = Directory.CreateTempSubdirectory("openigi-manifests-").FullName;
        try
        {
            GitHub.Download(repository, ManifestsTag, download);
            foreach (string file in Directory.EnumerateFiles(download))
            {
                if (FolderPath(Path.GetFileName(file)) is { } path)
                {
                    string target = Path.Combine(folder, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(file, target, overwrite: true);
                }
            }
        }
        finally
        {
            Directory.Delete(download, recursive: true);
        }
    }

    /// <summary>Where a file of the <c>manifests</c> release sits in a <see cref="FolderStore"/>.</summary>
    /// <param name="name">The file's name on the release.</param>
    /// <returns>The path, relative to the store, or <see langword="null"/> for a name that is none of the store's.</returns>
    internal static string? FolderPath(string name)
    {
        int dot = name.IndexOf('.', StringComparison.Ordinal);
        int underscore = name.IndexOf('_', StringComparison.Ordinal);
        if (underscore > 0 && name.EndsWith(ContentSuffix, StringComparison.Ordinal))
        {
            string pack = name[..underscore];
            string version = name[(underscore + 1)..^ContentSuffix.Length];
            return ContentManifest.IsValidId(pack) && ReleaseVersion.TryParse(version, out _)
                ? Path.Combine(pack, version, ContentManifest.FileName)
                : null;
        }

        if (dot <= 0 || !ContentManifest.IsValidId(name[..dot]))
        {
            return null;
        }

        string rest = name[(dot + 1)..];
        return rest == ContentPackInfo.FileName || ContentPackInfo.IsValidThumbnail(rest) ? Path.Combine(name[..dot], rest) : null;
    }

    /// <inheritdoc/>
    public HashSet<string> Missing(IEnumerable<string> hashes)
    {
        HashSet<string> wanted = [.. hashes];
        HashSet<string> held = StoredChunks(wanted.Select(static hash => hash[..2]).ToHashSet(StringComparer.Ordinal))
            .Select(static asset => asset.Name)
            .ToHashSet(StringComparer.Ordinal);
        return [.. wanted.Where(hash => !held.Contains(hash + ".br"))];
    }

    /// <inheritdoc/>
    public void Publish(ContentPublish.Chunked chunked, IReadOnlyList<ContentChunk> upload)
    {
        ContentManifest manifest = chunked.Manifest;
        List<GitHubRelease> releases = GitHub.Releases(repository);
        string manifestName = $"{manifest.Id}_{manifest.Version}{ContentSuffix}";
        if (releases.FirstOrDefault(static r => r.Tag == ManifestsTag) is { } existing
            && GitHub.Assets(repository, existing.Id).Any(asset => asset.Name == manifestName))
        {
            throw new InvalidOperationException($"{manifest.Id} {manifest.Version} is already published.");
        }

        foreach (IGrouping<string, ContentChunk> shard in upload.GroupBy(static chunk => chunk.Hash[..2]).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            string tag = ChunksTagPrefix + shard.Key;
            GitHub.Ensure(repository, tag, $"Content chunks whose hash begins {shard.Key}.", releases);
            GitHub.Upload(repository, tag, [.. shard.Select(chunk => Path.Combine(chunked.Cache, ContentPublish.Chunked.StoredName(chunk.Hash)))]);
        }

        UploadManifests(releases, [(manifestName, ReleaseJson.Write(manifest) + "\n")], []);
        Rebuild();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ContentPack> List()
    {
        string folder = Directory.CreateTempSubdirectory("openigi-store-").FullName;
        try
        {
            Unpack(repository, folder);
            return new FolderStore(folder).List();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <inheritdoc/>
    public void Describe(ContentPackInfo info, string? picture)
    {
        ArgumentNullException.ThrowIfNull(info);
        List<GitHubRelease> releases = GitHub.Releases(repository);
        List<GitHubAsset> files = ManifestFiles(releases);
        if (!files.Any(asset => asset.Name.StartsWith(info.Id + "_", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"There is no pack {info.Id}; publish a version first.");
        }

        UploadManifests(
            releases,
            [($"{info.Id}{PackSuffix}", ReleaseJson.Write(info) + "\n")],
            picture is null ? [] : [(picture, $"{info.Id}.{info.Thumbnail}")]);

        // The pack's other pictures, which nothing names any more.
        foreach (GitHubAsset old in files.Where(asset =>
                     asset.Name.StartsWith($"{info.Id}.thumbnail-", StringComparison.Ordinal) && asset.Name != $"{info.Id}.{info.Thumbnail}"))
        {
            GitHub.Delete(repository, old);
        }

        Rebuild();
    }

    /// <inheritdoc/>
    public void Delete(string pack, string? version)
    {
        List<GitHubRelease> releases = GitHub.Releases(repository);
        List<GitHubAsset> files = ManifestFiles(releases);
        List<GitHubAsset> doomed = [.. files.Where(asset => version is null
            ? asset.Name.StartsWith(pack + "_", StringComparison.Ordinal) || asset.Name.StartsWith(pack + ".", StringComparison.Ordinal)
            : asset.Name == $"{pack}_{version}{ContentSuffix}")];
        if (!doomed.Any(static asset => asset.Name.EndsWith(ContentSuffix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(version is null ? $"There is no pack {pack}." : $"{pack} {version} is not published.");
        }

        foreach (GitHubAsset asset in doomed)
        {
            GitHub.Delete(repository, asset);
        }

        // Chunks no remaining version's manifest names go. Every manifest is read again from the store, and one that
        // cannot be read, or names no chunk, stops it: every chunk would otherwise look unused.
        string folder = Directory.CreateTempSubdirectory("openigi-store-").FullName;
        try
        {
            Unpack(repository, folder);
            HashSet<string> named = new(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(folder, ContentManifest.FileName, SearchOption.AllDirectories))
            {
                List<string> hashes = [.. ReleaseJson.ReadContent(File.ReadAllText(path)).Files
                    .SelectMany(static file => file.Chunks)
                    .Select(static chunk => chunk.Hash)];
                if (hashes.Count == 0)
                {
                    throw new InvalidDataException($"{path} names no chunk; no chunk was collected.");
                }

                named.UnionWith(hashes);
            }

            foreach (GitHubAsset stored in StoredChunks(null).Where(asset => !named.Contains(Path.GetFileNameWithoutExtension(asset.Name))))
            {
                GitHub.Delete(repository, stored);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        Rebuild();
    }

    private List<GitHubAsset> ManifestFiles(List<GitHubRelease> releases) =>
        releases.FirstOrDefault(static r => r.Tag == ManifestsTag) is { } manifests ? GitHub.Assets(repository, manifests.Id) : [];

    // The chunk files of the shards named, or of every shard.
    private IEnumerable<GitHubAsset> StoredChunks(HashSet<string>? prefixes) =>
        GitHub.Releases(repository)
            .Where(release => release.Tag.StartsWith(ChunksTagPrefix, StringComparison.Ordinal)
                && (prefixes is null || prefixes.Contains(release.Tag[ChunksTagPrefix.Length..])))
            .SelectMany(release => GitHub.Assets(repository, release.Id));

    // Texts written out and files copied under their names on the release, all in one upload.
    private void UploadManifests(List<GitHubRelease> releases, IReadOnlyList<(string Name, string Text)> texts, IReadOnlyList<(string Path, string Name)> files)
    {
        string folder = Directory.CreateTempSubdirectory("openigi-manifest-").FullName;
        try
        {
            List<string> upload = [];
            foreach ((string name, string text) in texts)
            {
                string path = Path.Combine(folder, name);
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                upload.Add(path);
            }

            foreach ((string source, string name) in files)
            {
                string path = Path.Combine(folder, name);
                File.Copy(source, path);
                upload.Add(path);
            }

            GitHub.Ensure(repository, ManifestsTag, "What describes the content packs: their manifests, descriptions and pictures.", releases);
            GitHub.Upload(repository, ManifestsTag, upload);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The site serves the store's listings, so a change is live once the site is built again.
    private void Rebuild()
    {
        if (site is not null)
        {
            GitHub.Dispatch(site, "site.yml", "master");
        }
    }
}
