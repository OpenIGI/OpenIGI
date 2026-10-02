using System.Security.Cryptography;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>What bringing a pack to a version takes, worked out before anything is downloaded.</summary>
/// <param name="Build">The files to write: new, changed, damaged, or missing from disk.</param>
/// <param name="Download">The chunks to fetch: what <see cref="Build"/> needs that is nowhere on disk.</param>
/// <param name="DownloadBytes">What fetching them costs, as the server stores them.</param>
/// <param name="Remove">The installed files the version no longer has.</param>
internal sealed record ContentPlan(
    IReadOnlyList<ContentFile> Build,
    IReadOnlyList<ContentChunk> Download,
    long DownloadBytes,
    IReadOnlyList<string> Remove)
{
    /// <summary>Gets whether the pack is already at the version, every file intact.</summary>
    public bool IsEmpty => Build.Count == 0 && Remove.Count == 0;
}

/// <summary>Installs, updates, verifies and removes one optional content pack.</summary>
/// <remarks>
/// <para>
/// <c>&lt;root&gt;/files/</c> holds the pack as the game reads it (the launcher points the game at it), and
/// <c>&lt;root&gt;/installed.json</c> the manifest of the version those files are. There is one copy of the
/// pack and it is updated in place: a texture pack is a gigabyte, and a second copy per version is not a cost
/// a player should carry.
/// </para>
/// <para>
/// An update fetches only the chunks it cannot find on disk. A file's chunks are read back out of the
/// installed files where the installed manifest says they are -- each one checked against its hash on the
/// way, so a damaged file never seeds a new one -- and the rest are downloaded into
/// <c>&lt;root&gt;/chunks.part/</c>, checked and kept there until the update finishes, which is what lets a
/// paused or interrupted update carry on without fetching anything twice. Each new file is written beside the
/// old one and swapped in once its whole hash matches; <c>installed.json</c> is written last.
/// </para>
/// <para>
/// A repair is the same thing with the damaged files named: they are rebuilt from their chunks, and only the
/// chunks that cannot be found intact anywhere else are downloaded.
/// </para>
/// </remarks>
internal sealed class ContentInstaller(HttpClient http, string root)
{
    private const string InstalledFile = "installed.json";
    private const string FilesFolder = "files";
    private const string PartFolder = "chunks.part";
    private const int ParallelDownloads = 4;

    /// <summary>Gets the folder this installer manages.</summary>
    public string Root { get; } = root;

    /// <summary>Gets the folder the pack's files are in: what the game is pointed at.</summary>
    public string Files => Path.Combine(Root, FilesFolder);

    private string Part => Path.Combine(Root, PartFolder);

    /// <summary>Reads which version is installed.</summary>
    /// <returns>Its manifest, or <see langword="null"/> when the pack is not installed.</returns>
    public ContentManifest? Installed()
    {
        string path = Path.Combine(Root, InstalledFile);
        try
        {
            return File.Exists(path) && Directory.Exists(Files) ? ReleaseJson.ReadContent(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // An unreadable record is a pack that has to be installed again, not a launcher that cannot start.
            return null;
        }
    }

    /// <summary>Works out what bringing the pack to a version takes.</summary>
    /// <param name="target">The version wanted.</param>
    /// <param name="rebuild">Files to write again whatever the record says, such as the ones a verify found damaged.</param>
    /// <returns>The plan.</returns>
    public ContentPlan Plan(ContentManifest target, IReadOnlySet<string>? rebuild = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ContentManifest? installed = Installed();
        Dictionary<string, ContentFile> had = installed?.Files.ToDictionary(static f => f.Path, StringComparer.Ordinal)
            ?? new Dictionary<string, ContentFile>(StringComparer.Ordinal);

        List<ContentFile> build = [];
        foreach (ContentFile file in target.Files)
        {
            bool same = had.TryGetValue(file.Path, out ContentFile? old)
                && old.Sha256 == file.Sha256
                && rebuild?.Contains(file.Path) != true
                && new FileInfo(Target(file.Path)) is { Exists: true } info
                && info.Length == file.Size;
            if (!same)
            {
                build.Add(file);
            }
        }

        Dictionary<string, ChunkSource> local = LocalChunks(installed, rebuild);
        HashSet<string> part = Directory.Exists(Part)
            ? [.. Directory.EnumerateFiles(Part).Select(Path.GetFileName).OfType<string>()]
            : [];

        List<ContentChunk> download = [.. build
            .SelectMany(static file => file.Chunks)
            .DistinctBy(static chunk => chunk.Hash)
            .Where(chunk => !local.ContainsKey(chunk.Hash) && !part.Contains(chunk.Hash))];

        HashSet<string> wanted = [.. target.Files.Select(static f => f.Path)];
        List<string> remove = [.. had.Keys.Where(path => !wanted.Contains(path)).Order(StringComparer.Ordinal)];

        return new ContentPlan(build, download, download.Sum(static chunk => (long)chunk.StoredSize), remove);
    }

    /// <summary>Brings the pack to a version: installs it, updates it, or repairs it.</summary>
    /// <param name="index">The content index, which says where chunks are downloaded from.</param>
    /// <param name="target">The version wanted.</param>
    /// <param name="rebuild">Files to write again whatever the record says.</param>
    /// <param name="progress">Told how far the work has got.</param>
    /// <param name="cancellationToken">Pauses it: everything downloaded so far is kept for the next attempt.</param>
    /// <returns>The installed version's manifest.</returns>
    /// <exception cref="HttpRequestException">A chunk could not be downloaded.</exception>
    /// <exception cref="InvalidDataException">A chunk or a finished file did not match its hash.</exception>
    public async Task<ContentManifest> InstallAsync(
        ContentIndex index,
        ContentManifest target,
        IReadOnlySet<string>? rebuild,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(target);

        foreach (ContentFile file in target.Files)
        {
            _ = Target(file.Path);
        }

        ContentPlan plan = Plan(target, rebuild);
        long total = plan.DownloadBytes;
        long done = 0;
        progress?.Report(new InstallProgress(0, total, InstallStage.Downloading));

        Directory.CreateDirectory(Part);
        using (SemaphoreSlim slots = new(ParallelDownloads))
        {
            await Task.WhenAll(plan.Download.Select(async chunk =>
            {
                await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await FetchAsync(index, chunk, cancellationToken).ConfigureAwait(false);
                    long now = Interlocked.Add(ref done, chunk.StoredSize);
                    progress?.Report(new InstallProgress(now, total, InstallStage.Downloading));
                }
                finally
                {
                    slots.Release();
                }
            })).ConfigureAwait(false);
        }

        // Writing the files reads the old ones, so it is all done before anything is removed.
        ContentManifest? installed = Installed();
        Dictionary<string, ChunkSource> local = LocalChunks(installed, rebuild);
        long building = Math.Max(1, plan.Build.Sum(static f => f.Size));
        long built = 0;
        foreach (ContentFile file in plan.Build)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await BuildFileAsync(index, file, local, cancellationToken).ConfigureAwait(false);
            built += file.Size;
            progress?.Report(new InstallProgress(total == 0 ? 0 : total * built / building, total, InstallStage.Installing));
        }

        foreach (string path in plan.Remove)
        {
            File.Delete(Target(path));
        }

        Directory.CreateDirectory(Root);
        string record = Path.Combine(Root, InstalledFile);
        File.WriteAllText(record + ".tmp", ReleaseJson.Write(target));
        File.Move(record + ".tmp", record, overwrite: true);

        Directory.Delete(Part, recursive: true);
        progress?.Report(new InstallProgress(total, total, InstallStage.Installing));
        return target;
    }

    /// <summary>Checks the installed files against the manifest they were installed from.</summary>
    /// <param name="manifest">The installed version's manifest.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>The paths of the files that are missing or changed.</returns>
    public IReadOnlyList<string> Verify(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        List<string> damaged = [];
        foreach (ContentFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Target(file.Path);
            if (new FileInfo(path) is not { Exists: true } info || info.Length != file.Size)
            {
                damaged.Add(file.Path);
                continue;
            }

            using FileStream stream = File.OpenRead(path);
            if (Convert.ToHexStringLower(SHA256.HashData(stream)) != file.Sha256)
            {
                damaged.Add(file.Path);
            }
        }

        return damaged;
    }

    /// <summary>Removes the pack: its files, its record and anything half downloaded.</summary>
    public void Uninstall()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    // Where each chunk of the installed version sits on disk, for the files that are still there and not
    // about to be rebuilt.
    private Dictionary<string, ChunkSource> LocalChunks(ContentManifest? installed, IReadOnlySet<string>? rebuild)
    {
        Dictionary<string, ChunkSource> local = new(StringComparer.Ordinal);
        if (installed is null)
        {
            return local;
        }

        foreach (ContentFile file in installed.Files)
        {
            string path = Target(file.Path);
            if (rebuild?.Contains(file.Path) == true || new FileInfo(path) is not { Exists: true } info || info.Length != file.Size)
            {
                continue;
            }

            long offset = 0;
            foreach (ContentChunk chunk in file.Chunks)
            {
                local.TryAdd(chunk.Hash, new ChunkSource(path, offset));
                offset += chunk.Size;
            }
        }

        return local;
    }

    private async Task FetchAsync(ContentIndex index, ContentChunk chunk, CancellationToken cancellationToken)
    {
        string path = Path.Combine(Part, chunk.Hash);
        if (File.Exists(path))
        {
            return;
        }

        byte[] stored = await http.GetByteArrayAsync(index.ChunkAddress(chunk.Hash), cancellationToken).ConfigureAwait(false);
        byte[] data = ContentChunker.Decode(stored, chunk);
        string temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, data, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private async Task BuildFileAsync(ContentIndex index, ContentFile file, Dictionary<string, ChunkSource> local, CancellationToken cancellationToken)
    {
        string path = Target(file.Path);
        string next = path + ".new";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (IncrementalHash whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using (FileStream output = new(next, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                foreach (ContentChunk chunk in file.Chunks)
                {
                    byte[] data = await ChunkAsync(index, chunk, local, cancellationToken).ConfigureAwait(false);
                    whole.AppendData(data);
                    await output.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                }
            }

            if (Convert.ToHexStringLower(whole.GetHashAndReset()) != file.Sha256)
            {
                File.Delete(next);
                throw new InvalidDataException($"{file.Path} did not come out as the manifest describes it.");
            }
        }

        File.Move(next, path, overwrite: true);
    }

    // A chunk's bytes: from the download cache, else read back out of an installed file and checked, else
    // fetched now (an installed file that turned out not to hold it any more).
    private async Task<byte[]> ChunkAsync(ContentIndex index, ContentChunk chunk, Dictionary<string, ChunkSource> local, CancellationToken cancellationToken)
    {
        string part = Path.Combine(Part, chunk.Hash);
        if (!File.Exists(part) && local.TryGetValue(chunk.Hash, out ChunkSource? source) && File.Exists(source.Path))
        {
            byte[] data = new byte[chunk.Size];
            await using (FileStream input = new(source.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true))
            {
                input.Position = source.Offset;
                await input.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            }

            if (ContentChunker.Hash(data) == chunk.Hash)
            {
                return data;
            }
        }

        if (!File.Exists(part))
        {
            await FetchAsync(index, chunk, cancellationToken).ConfigureAwait(false);
        }

        return await File.ReadAllBytesAsync(part, cancellationToken).ConfigureAwait(false);
    }

    // A path from a manifest, kept inside the pack's folder whatever it says.
    private string Target(string relative)
    {
        string files = Path.GetFullPath(Files) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(files, relative));
        return full.StartsWith(files, StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(relative)
            ? full
            : throw new InvalidDataException($"'{relative}' is not a path inside the pack.");
    }

    private sealed record ChunkSource(string Path, long Offset);
}
