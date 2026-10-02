using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
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

/// <summary>Writes the uploads the VM's dispatcher takes.</summary>
internal static class ContentUploads
{
    /// <summary>Writes a pack description's upload: <c>pack.json</c> and, when there is one, the picture.</summary>
    /// <param name="info">The description.</param>
    /// <param name="picture">The picture's file, or <see langword="null"/>.</param>
    /// <param name="output">Where the gzipped tarball is written.</param>
    internal static void WriteDescription(ContentPackInfo info, string? picture, Stream output)
    {
        using GZipStream gzip = new(output, CompressionLevel.Fastest, leaveOpen: true);
        using TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: true);
        if (picture is not null)
        {
            tar.WriteEntry(picture, info.Thumbnail!);
        }

        PaxTarEntry entry = new(TarEntryType.RegularFile, ContentPackInfo.FileName)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(ReleaseJson.Write(info) + "\n")),
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
        };
        tar.WriteEntry(entry);
    }
}

/// <summary>A content store in a local folder, laid out as the release VM's.</summary>
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

        // Chunks first and the manifest last, as the VM's dispatcher does.
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

        // Chunks no remaining version's manifest names go, as the dispatcher's collection does. A manifest that
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

/// <summary>The release VM's content store, through the deploy dispatcher over SSH, and its update service.</summary>
/// <param name="target">The SSH destination, such as <c>deploy@sh-de</c>.</param>
/// <param name="remote">
/// What runs the dispatcher on the far side, or empty when the key is pinned to it (the deploy user's keys
/// are, so the verb alone is sent and the forced command reads it).
/// </param>
/// <param name="service">The update service, which lists the packs as players see them.</param>
internal sealed class SshStore(string target, string remote, Uri service) : IContentStore
{
    /// <inheritdoc/>
    public HashSet<string> Missing(IEnumerable<string> hashes)
    {
        string list = string.Concat(hashes.Select(static hash => hash + "\n"));
        string output = Run("content-have", input => input.Write(Encoding.ASCII.GetBytes(list)));
        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <inheritdoc/>
    public void Publish(ContentPublish.Chunked chunked, IReadOnlyList<ContentChunk> upload)
    {
        // Written to a file first: the upload is checked whole on the far side, and a stream cut by a
        // failure here would only be thrown away there.
        string tarball = Path.Combine(chunked.Cache, $"upload-{chunked.Manifest.Id}-{chunked.Manifest.Version}.tar.gz");
        try
        {
            using (FileStream file = File.Create(tarball))
            {
                ContentPublish.WriteUpload(chunked, upload, file);
            }

            Run($"content {chunked.Manifest.Id} {chunked.Manifest.Version}", input =>
            {
                using FileStream file = File.OpenRead(tarball);
                file.CopyTo(input);
            });
        }
        finally
        {
            File.Delete(tarball);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ContentPack> List()
    {
        using HttpClient http = new();
        string json = http.GetStringAsync(new Uri(service, "v1/content")).GetAwaiter().GetResult();
        return ReleaseJson.ReadContentIndex(json).Packs;
    }

    /// <inheritdoc/>
    public void Describe(ContentPackInfo info, string? picture)
    {
        using MemoryStream upload = new();
        ContentUploads.WriteDescription(info, picture, upload);
        Run($"content-info {info.Id}", input => input.Write(upload.GetBuffer(), 0, (int)upload.Length));
    }

    /// <inheritdoc/>
    public void Delete(string pack, string? version) =>
        Run(version is null ? $"content-delete {pack}" : $"content-delete {pack} {version}", static _ => { });

    private string Run(string verb, Action<Stream> write)
    {
        ProcessStartInfo start = new("ssh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("BatchMode=yes");
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(string.IsNullOrWhiteSpace(remote) ? verb : $"{remote} {verb}");

        using Process ssh = Process.Start(start) ?? throw new IOException("ssh could not be started.");
        Task<string> output = ssh.StandardOutput.ReadToEndAsync();
        write(ssh.StandardInput.BaseStream);
        ssh.StandardInput.Close();
        ssh.WaitForExit();

        return ssh.ExitCode == 0
            ? output.GetAwaiter().GetResult()
            : throw new IOException($"'{verb}' on {target} failed with exit code {ssh.ExitCode}.");
    }
}

/// <summary>
/// Writes the upload a publish (or a description) would send to a file instead, every chunk in it: for carrying
/// it to the VM by hand (<c>openigi-deploy content &lt;pack&gt; &lt;version&gt; &lt; file</c>, or
/// <c>content-info &lt;pack&gt;</c>), or for trying the dispatcher.
/// </summary>
/// <param name="path">The tarball to write.</param>
internal sealed class TarballStore(string path) : IContentStore
{
    /// <inheritdoc/>
    public HashSet<string> Missing(IEnumerable<string> hashes) => [.. hashes];

    /// <inheritdoc/>
    public void Publish(ContentPublish.Chunked chunked, IReadOnlyList<ContentChunk> upload)
    {
        using FileStream file = File.Create(path);
        ContentPublish.WriteUpload(chunked, upload, file);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ContentPack> List() => throw new NotSupportedException("An upload file has no packs to list.");

    /// <inheritdoc/>
    public void Describe(ContentPackInfo info, string? picture)
    {
        using FileStream file = File.Create(path);
        ContentUploads.WriteDescription(info, picture, file);
    }

    /// <inheritdoc/>
    public void Delete(string pack, string? version) =>
        throw new NotSupportedException("Deleting runs on the store itself: use --ssh or --to-dir.");
}
