using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Where content is published: which chunks it holds, taking a version, and managing its packs.</summary>
internal interface IContentStore
{
    /// <summary>Finds the chunks the store does not hold.</summary>
    /// <param name="hashes">The chunks to ask about.</param>
    /// <returns>The ones it lacks.</returns>
    HashSet<string> Missing(IEnumerable<string> hashes);

    /// <summary>Publishes a version, sending the chunks the store lacks.</summary>
    /// <param name="chunked">The chunked pack.</param>
    /// <param name="upload">The chunks to send.</param>
    void Publish(ContentPublish.Chunked chunked, IReadOnlyList<ContentChunk> upload);

    /// <summary>Lists the packs, each at its newest version, as players see them.</summary>
    /// <returns>The packs.</returns>
    IReadOnlyList<ContentPack> List();

    /// <summary>Replaces a pack's own description: its name, summary and picture.</summary>
    /// <param name="info">The description.</param>
    /// <param name="picture">The picture's file, named as <see cref="ContentPackInfo.Thumbnail"/> says, or <see langword="null"/> to keep the stored one.</param>
    void Describe(ContentPackInfo info, string? picture);

    /// <summary>Deletes a pack, or one version of it.</summary>
    /// <param name="pack">The pack.</param>
    /// <param name="version">The version, or <see langword="null"/> for the whole pack.</param>
    void Delete(string pack, string? version);
}

/// <summary>A content store in a local folder, laid out as the site's build unpacks the published one (<see cref="GitHubStore.Unpack"/>).</summary>
/// <param name="root">The store: <c>chunks/</c> and <c>&lt;pack&gt;/&lt;version&gt;/</c>.</param>
internal sealed class FolderStore(string root) : IContentStore
{
    /// <inheritdoc/>
    public HashSet<string> Missing(IEnumerable<string> hashes) =>
        [.. hashes.Where(hash => !File.Exists(Path.Combine(root, ContentPublish.Chunked.StoredName(hash))))];

    /// <inheritdoc/>
    public void Publish(ContentPublish.Chunked chunked, IReadOnlyList<ContentChunk> upload)
    {
        string version = Path.Combine(root, chunked.Manifest.Id, chunked.Manifest.Version);
        if (File.Exists(Path.Combine(version, ContentManifest.FileName)))
        {
            throw new InvalidOperationException($"{chunked.Manifest.Id} {chunked.Manifest.Version} is already published.");
        }

        // Chunks first and the manifest last, as the published store takes them.
        foreach (ContentChunk chunk in upload)
        {
            string name = ContentPublish.Chunked.StoredName(chunk.Hash);
            string target = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(chunked.Cache, name), target, overwrite: true);
        }

        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, ContentManifest.FileName), ReleaseJson.Write(chunked.Manifest) + "\n");
    }

    /// <inheritdoc/>
    public IReadOnlyList<ContentPack> List()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        List<ContentPack> packs = [];
        foreach (string pack in Directory.EnumerateDirectories(root).Where(static d => ContentManifest.IsValidId(Path.GetFileName(d))).Order(StringComparer.Ordinal))
        {
            ContentManifest? newest = Directory.EnumerateDirectories(pack)
                .Select(static v => Path.Combine(v, ContentManifest.FileName))
                .Where(File.Exists)
                .Select(static path => ReleaseJson.ReadContent(File.ReadAllText(path)))
                .MaxBy(static m => m.ParsedVersion);
            if (newest is null)
            {
                continue;
            }

            string info = Path.Combine(pack, ContentPackInfo.FileName);
            packs.Add(File.Exists(info)
                ? ReleaseJson.ReadPackInfo(File.ReadAllText(info)).Apply(newest.Listing, (id, name) => Path.Combine(root, id, name))
                : newest.Listing);
        }

        return packs;
    }

    /// <inheritdoc/>
    public void Describe(ContentPackInfo info, string? picture)
    {
        ArgumentNullException.ThrowIfNull(info);
        string pack = Path.Combine(root, info.Id);
        if (!Directory.Exists(pack))
        {
            throw new InvalidOperationException($"There is no pack {info.Id}; publish a version first.");
        }

        if (picture is not null)
        {
            File.Copy(picture, Path.Combine(pack, info.Thumbnail!), overwrite: true);
        }

        File.WriteAllText(Path.Combine(pack, ContentPackInfo.FileName), ReleaseJson.Write(info) + "\n");
        foreach (string old in Directory.EnumerateFiles(pack, "thumbnail-*").Where(path => Path.GetFileName(path) != info.Thumbnail))
        {
            File.Delete(old);
        }
    }

    /// <inheritdoc/>
    public void Delete(string pack, string? version)
    {
        string target = version is null ? Path.Combine(root, pack) : Path.Combine(root, pack, version);
        if (!Directory.Exists(target))
        {
            throw new InvalidOperationException(version is null ? $"There is no pack {pack}." : $"{pack} {version} is not published.");
        }

        Directory.Delete(target, recursive: true);

        // Chunks no remaining version's manifest names go, as the published store's collection does. A manifest that
        // cannot be read stops it: every chunk would otherwise look unused.
        HashSet<string> named = [.. Directory.EnumerateDirectories(root)
            .Where(static d => ContentManifest.IsValidId(Path.GetFileName(d)))
            .SelectMany(static pack => Directory.EnumerateDirectories(pack))
            .Select(static v => Path.Combine(v, ContentManifest.FileName))
            .Where(File.Exists)
            .SelectMany(static path => ReleaseJson.ReadContent(File.ReadAllText(path)).Files)
            .SelectMany(static file => file.Chunks)
            .Select(static chunk => chunk.Hash)];
        string chunks = Path.Combine(root, "chunks");
        if (Directory.Exists(chunks))
        {
            foreach (string stored in Directory.EnumerateFiles(chunks, "*.br", SearchOption.AllDirectories)
                         .Where(path => !named.Contains(Path.GetFileNameWithoutExtension(path))))
            {
                File.Delete(stored);
            }
        }
    }
}
