using System.Text.Json;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Reads the releases and the content packs out of folders, as the site is built from them.</summary>
/// <remarks>
/// A manifest that does not parse, or that disagrees with the folder it sits in, is skipped and reported rather than
/// taking the whole site down with it: the rest is still published.
/// </remarks>
internal static class StoreScan
{
    /// <summary>Indexes a folder holding one sub-folder per release, each with its <c>release.json</c>.</summary>
    /// <param name="root">The folder.</param>
    /// <param name="downloadUrl">The download address template (see <see cref="ReleaseIndex"/>).</param>
    /// <param name="skipped">Told of each manifest skipped, and why.</param>
    /// <returns>The index; empty when the folder does not exist.</returns>
    public static ReleaseIndex Releases(string root, string downloadUrl, Action<string, string> skipped)
    {
        if (!Directory.Exists(root))
        {
            return new ReleaseIndex(Array.Empty<ReleaseManifest>(), downloadUrl);
        }

        List<(ReleaseManifest Release, string Folder)> releases = [];
        foreach (string path in Directory.EnumerateDirectories(root)
                     .Select(static dir => Path.Combine(dir, ReleaseManifest.FileName))
                     .Where(File.Exists)
                     .Order(StringComparer.Ordinal))
        {
            try
            {
                ReleaseManifest manifest = ReleaseJson.ReadManifest(File.ReadAllText(path));
                _ = manifest.ParsedVersion;
                releases.Add((manifest, Path.GetFileName(Path.GetDirectoryName(path)!)));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                skipped(path, ex.Message);
            }
        }

        return new ReleaseIndex(releases, downloadUrl);
    }

    /// <summary>Indexes a content store: <c>&lt;pack&gt;/&lt;version&gt;/content.json</c> and <c>&lt;pack&gt;/pack.json</c>.</summary>
    /// <param name="root">The store.</param>
    /// <param name="chunkUrl">Where a chunk is downloaded from; see <see cref="ContentIndex.ChunkUrl"/>.</param>
    /// <param name="thumbnailUrl">Where a pack's picture is downloaded from, with <c>{pack}</c> and <c>{name}</c>.</param>
    /// <param name="skipped">Told of each manifest skipped, and why.</param>
    /// <returns>The library; empty, but still naming the chunk address, when the store does not exist.</returns>
    public static ContentLibrary Content(string root, string chunkUrl, string thumbnailUrl, Action<string, string> skipped)
    {
        if (!Directory.Exists(root))
        {
            return new ContentLibrary([], chunkUrl, null, thumbnailUrl);
        }

        List<string> packs = [.. Directory
            .EnumerateDirectories(root)
            .Where(static dir => ContentManifest.IsValidId(Path.GetFileName(dir)))
            .Order(StringComparer.Ordinal)];

        List<(ContentManifest, string)> versions = [];
        foreach (string path in packs
                     .SelectMany(static pack => Directory.EnumerateDirectories(pack))
                     .Select(static version => Path.Combine(version, ContentManifest.FileName))
                     .Where(File.Exists)
                     .Order(StringComparer.Ordinal))
        {
            try
            {
                string json = File.ReadAllText(path);
                ContentManifest manifest = ReleaseJson.ReadContent(json);
                _ = manifest.ParsedVersion;

                // The folders are the truth the store was written as; a manifest that disagrees with where it
                // sits would be served under one name and describe another.
                string version = Path.GetFileName(Path.GetDirectoryName(path)!);
                string id = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))!);
                if (manifest.Id != id || manifest.Version != version)
                {
                    throw new FormatException($"it names {manifest.Id} {manifest.Version} but sits in {id}/{version}");
                }

                versions.Add((manifest, json));
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                skipped(path, ex.Message);
            }
        }

        Dictionary<string, ContentPackInfo> infos = new(StringComparer.Ordinal);
        foreach (string path in packs.Select(static pack => Path.Combine(pack, ContentPackInfo.FileName)).Where(File.Exists))
        {
            try
            {
                ContentPackInfo info = ReleaseJson.ReadPackInfo(File.ReadAllText(path));
                string id = Path.GetFileName(Path.GetDirectoryName(path)!);
                if (info.Id != id)
                {
                    throw new FormatException($"it describes {info.Id} but sits in {id}");
                }

                infos[id] = info;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                skipped(path, ex.Message);
            }
        }

        return new ContentLibrary(versions, chunkUrl, infos, thumbnailUrl);
    }
}
