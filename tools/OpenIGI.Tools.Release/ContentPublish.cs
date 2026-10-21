using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OpenIGI.Core.Content;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>Publishes a version of an optional content pack: chunks it, and uploads only what the store lacks.</summary>
/// <remarks>
/// <para>
/// Every file of the pack folder is cut into content-defined chunks (<see cref="ContentChunker"/>), each
/// compressed the way the store keeps it into a local cache, and listed in the version's manifest. The store
/// is then asked which of the chunks it does not hold, and only those travel, with the manifest, in one
/// upload. A pack rebuilt with a few textures changed uploads a few chunks; a first publish uploads them all.
/// </para>
/// <para>
/// The store is the release VM's, reached through its deploy dispatcher over SSH (<see cref="SshStore"/>),
/// or a folder laid out the same way (<see cref="FolderStore"/>), which is how the whole path is tried
/// without a server.
/// </para>
/// </remarks>
internal static class ContentPublish
{
    /// <summary>What describes the version besides its files.</summary>
    /// <param name="Id">The pack's identifier.</param>
    /// <param name="Version">The version being published.</param>
    /// <param name="Title">The pack's name as players see it.</param>
    /// <param name="Summary">What it changes.</param>
    /// <param name="Game">The game it is for, by title id.</param>
    /// <param name="MinGameVersion">The oldest game build that can read it, if any.</param>
    /// <param name="Kind">What the pack is: an overlay of game files, or a runtime the engine loads.</param>
    /// <param name="Platforms">The platforms it is for, by runtime identifier, or <see langword="null"/> for every one.</param>
    /// <param name="Mod">What a mod says besides a pack's, for <see cref="ContentKind.Mod"/>; see <see cref="ForMod"/>.</param>
    internal sealed record Details(
        string Id,
        string Version,
        string Title,
        string Summary,
        string Game,
        string? MinGameVersion,
        ContentKind Kind = ContentKind.Overlay,
        IReadOnlyList<string>? Platforms = null,
        ContentModInfo? Mod = null);

    /// <summary>Describes a mod's version from the <c>mod.json</c> in its folder, which is the one account of it.</summary>
    /// <param name="folder">The mod's package: its <c>mod.json</c> and files, as an installed mod's folder holds them.</param>
    /// <param name="source">The public repository its code was built from; required for a mod with code.</param>
    /// <param name="commit">The commit of <paramref name="source"/> it was built from; required with it.</param>
    /// <returns>The details: the manifest's id, version, name, summary, games and oldest game build.</returns>
    /// <exception cref="ArgumentException">The manifest is missing what a listing needs, or a mod with code names no source.</exception>
    internal static Details ForMod(string folder, string? source, string? commit)
    {
        string path = Path.Combine(Path.GetFullPath(folder), ModManifest.FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"A mod's folder holds its {ModManifest.FileName}, and {folder} has none.", path);
        }

        ModManifest manifest;
        try
        {
            manifest = ModManifest.Parse(File.ReadAllText(path));
        }
        catch (ModLoadException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }

        if (manifest.Id == ModPacks.LocalFolder)
        {
            throw new ArgumentException($"No mod is named '{ModPacks.LocalFolder}': the launcher keeps the mods being tried in a folder of that name.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Summary))
        {
            throw new ArgumentException($"{path} has no summary, which the launcher lists the mod with.");
        }

        // Code runs on every player's machine, so what is published is built from source anyone can read: the mods
        // index builds it from this commit, and the listing says where that is.
        if (manifest.RunsCode && (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(commit)))
        {
            throw new ArgumentException("A mod with code is published built from its source: give --source <repository> and --commit <sha>.");
        }

        ContentModInfo mod = new(
            [.. manifest.Titles.Select(static title => title.ToLowerInvariant())],
            manifest.Authors,
            manifest.RunsCode,
            manifest.RunsCode ? source : null,
            manifest.RunsCode ? commit : null,
            manifest.Homepage,
            manifest.License,
            manifest.RequiresTitleData.Count > 0 ? manifest.RequiresTitleData : null,
            manifest.Dependencies.Count > 0 ? manifest.Dependencies : null,
            manifest.Conflicts.Count > 0 ? manifest.Conflicts : null,
            manifest.AffectsSimulation);
        return new Details(
            manifest.Id,
            manifest.Version,
            manifest.Name,
            manifest.Summary,
            ModPacks.GameFor(mod.Titles),
            manifest.MinGameVersion,
            ContentKind.Mod,
            Mod: mod);
    }

    /// <summary>A chunked pack folder: the manifest, and where each chunk's stored object was written.</summary>
    /// <param name="Manifest">The version's manifest.</param>
    /// <param name="Cache">The folder holding every chunk's stored object, as <c>chunks/xx/&lt;hash&gt;.br</c>.</param>
    internal sealed record Chunked(ContentManifest Manifest, string Cache)
    {
        /// <summary>Gets every distinct chunk the manifest names, in first-use order.</summary>
        public IReadOnlyList<ContentChunk> Chunks { get; } = [.. Manifest.Files
            .SelectMany(static file => file.Chunks)
            .DistinctBy(static chunk => chunk.Hash)];

        /// <summary>Gets a chunk's stored object, relative to <see cref="Cache"/>.</summary>
        /// <param name="hash">The chunk's hash.</param>
        /// <returns>Its path, with forward slashes, as the store names it.</returns>
        public static string StoredName(string hash) => $"chunks/{hash[..2]}/{hash}.br";
    }

    /// <summary>Cuts a pack folder into its manifest and stored chunks.</summary>
    /// <param name="folder">The pack: the files as the game reads them.</param>
    /// <param name="details">What describes the version.</param>
    /// <param name="cache">Where to write the stored chunks; one already there is not compressed again.</param>
    /// <param name="published">The publish time written into the manifest.</param>
    /// <returns>The chunked pack.</returns>
    internal static Chunked Chunk(string folder, Details details, string cache, DateTimeOffset published)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!ContentManifest.IsValidId(details.Id))
        {
            throw new ArgumentException($"'{details.Id}' is not a pack name: lowercase letters, digits and hyphens.");
        }

        if (!ReleaseVersion.TryParse(details.Version, out ReleaseVersion version) || version.IsPrerelease)
        {
            throw new ArgumentException($"'{details.Version}' is not a content version (major.minor.patch).");
        }

        if (details.MinGameVersion is { } minimum && !ReleaseVersion.TryParse(minimum, out _))
        {
            throw new ArgumentException($"'{minimum}' is not a game version.");
        }

        if (details.Platforms?.FirstOrDefault(static platform => !ContentManifest.IsValidPlatform(platform)) is { } platform)
        {
            throw new ArgumentException($"'{platform}' is not a platform: win, linux or osx, then x64, x86 or arm64 (win-x64).");
        }

        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"{root} does not exist.");
        }

        // Ordinal on the forward-slash path: the same folder lists the same way on every machine.
        List<string> paths = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

        if (paths.Count == 0)
        {
            throw new InvalidDataException($"{root} has no files.");
        }

        if (details.Kind == ContentKind.Runtime)
        {
            RequireRuntime(details, paths);
        }
        else if (details.Kind == ContentKind.Mod)
        {
            RequireMod(root, paths);
        }

        List<ContentFile> files = new(paths.Count);
        foreach (string path in paths)
        {
            byte[] data = File.ReadAllBytes(Path.Combine(root, path));
            files.Add(new ContentFile(path, data.Length, Convert.ToHexStringLower(SHA256.HashData(data)), ChunkFile(data, cache)));
        }

        ContentManifest manifest = new(
            ContentManifest.CurrentSchema,
            details.Id,
            version.ToString(),
            details.Title,
            details.Summary,
            details.Game,
            details.MinGameVersion,
            published,
            files.Sum(static file => file.Size),
            files,
            details.Kind,
            details.Platforms is { Count: > 0 } platforms ? [.. platforms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)] : null,
            details.Mod);

        return new Chunked(manifest, cache);
    }

    /// <summary>Refuses a mod's package that carries what it must not: the game's own assemblies, or code it does not declare.</summary>
    /// <param name="root">The package's folder.</param>
    /// <param name="paths">Its files, relative to it.</param>
    /// <remarks>
    /// A mod gets the port's assemblies from the game it runs in, so a package carrying one would carry dead weight at best
    /// and, at worst, a copy the loader never loads while the mod was tested against it. A mod of content alone runs no code,
    /// so it carries none: what a player is told it runs is what it runs.
    /// </remarks>
    private static void RequireMod(string root, IReadOnlyList<string> paths)
    {
        ModManifest manifest = ModManifest.Parse(File.ReadAllText(Path.Combine(root, ModManifest.FileName)));
        string[] code = [.. paths.Where(static path =>
            path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))];
        if (!manifest.RunsCode)
        {
            if (code.Length > 0)
            {
                throw new InvalidDataException($"'{code[0]}' is code, and the mod's {ModManifest.FileName} names no assembly: a mod of content alone runs none.");
            }

            return;
        }

        string assembly = manifest.Assembly!.Replace('\\', '/');
        if (!paths.Contains(assembly, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"The mod's assembly '{assembly}' is not in its folder.");
        }

        if (code.FirstOrDefault(path =>
                !string.Equals(path, assembly, StringComparison.Ordinal)
                && Path.GetFileName(path).StartsWith("OpenIGI.", StringComparison.OrdinalIgnoreCase)) is { } port)
        {
            throw new InvalidDataException($"'{port}' is one of the game's own assemblies, which every mod gets from the game; build the package with OpenIGI.Modding.Sdk.");
        }
    }

    /// <summary>Refuses a runtime pack that holds anything but what the engine loads and the licences beside it.</summary>
    /// <param name="details">What describes the version.</param>
    /// <param name="paths">The pack's files, relative to its root.</param>
    /// <remarks>
    /// <para>
    /// Streamline reads a JSON file beside each of its libraries where one is present, and lets it change what the
    /// library does: which plugins load, what they log, what they draw on the screen. A pack that carried one would
    /// configure every player's runtime from the store, so none is published; neither are the logs a development
    /// run leaves, nor the development build of the runtime, which draws its own overlays.
    /// </para>
    /// <para>
    /// What is loaded is checked again where it is loaded: the game refuses a folder holding any library not signed
    /// by NVIDIA (or by Microsoft, for the C++ runtime it carries).
    /// </para>
    /// </remarks>
    private static void RequireRuntime(Details details, IReadOnlyList<string> paths)
    {
        if (details.Game != RuntimePacks.Game)
        {
            throw new ArgumentException(
                $"A runtime pack is the engine's and is published for '{RuntimePacks.Game}', not '{details.Game}'.");
        }

        // Native libraries run on the one platform they were built for; a pack that named none would be offered,
        // and downloaded, on every other.
        if (details.Platforms is not { Count: > 0 })
        {
            throw new ArgumentException("A runtime pack holds native libraries: name the platforms they are for with --platform (win-x64).");
        }

        foreach (string path in paths)
        {
            string name = path.ToLowerInvariant();
            if (name.EndsWith(".json", StringComparison.Ordinal)
                || name.EndsWith(".log", StringComparison.Ordinal)
                || name.StartsWith("development/", StringComparison.Ordinal)
                || name.Contains("/development/", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"'{path}' cannot be published in a runtime pack: no configuration, logs or development build go to players.");
            }
        }

        if (!paths.Any(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("A runtime pack holds the libraries the engine loads, and this one has none.");
        }
    }

    /// <summary>Publishes a chunked pack to a store.</summary>
    /// <param name="chunked">The chunked pack.</param>
    /// <param name="store">The store.</param>
    /// <param name="dryRun">Only report what would be uploaded.</param>
    /// <param name="log">Where progress is written.</param>
    /// <returns>The chunks that were (or would be) uploaded.</returns>
    internal static IReadOnlyList<ContentChunk> Publish(Chunked chunked, IContentStore store, bool dryRun, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(chunked);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);

        HashSet<string> missing = store.Missing(chunked.Chunks.Select(static chunk => chunk.Hash));
        List<ContentChunk> upload = [.. chunked.Chunks.Where(chunk => missing.Contains(chunk.Hash))];

        long total = chunked.Chunks.Sum(static chunk => (long)chunk.StoredSize);
        long sent = upload.Sum(static chunk => (long)chunk.StoredSize);
        ContentManifest manifest = chunked.Manifest;
        log.WriteLine(FormattableString.Invariant(
            $"{manifest.Id} {manifest.Version}: {manifest.Files.Count} file(s), {Mib(manifest.Size)} installed, {chunked.Chunks.Count} chunk(s), {Mib(total)} stored"));
        log.WriteLine(FormattableString.Invariant(
            $"  new: {upload.Count} chunk(s), {Mib(sent)} -- what this upload sends, and what a player on the previous version downloads to update"));
        log.WriteLine(FormattableString.Invariant(
            $"  reused: {chunked.Chunks.Count - upload.Count} chunk(s) already in the store"));

        if (!dryRun)
        {
            store.Publish(chunked, upload);
            log.WriteLine($"published {chunked.Manifest.Id} {chunked.Manifest.Version}");
        }

        return upload;
    }

    /// <summary>Writes what one upload carries: the chunks it sends, their checksums, and the version's manifest.</summary>
    /// <param name="chunked">The chunked pack.</param>
    /// <param name="upload">The chunks the store lacks.</param>
    /// <param name="output">Where the gzipped tarball is written.</param>
    internal static void WriteUpload(Chunked chunked, IReadOnlyList<ContentChunk> upload, Stream output)
    {
        using GZipStream gzip = new(output, CompressionLevel.Fastest, leaveOpen: true);
        using TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: true);

        StringBuilder sums = new();
        foreach (ContentChunk chunk in upload)
        {
            string name = Chunked.StoredName(chunk.Hash);
            string source = Path.Combine(chunked.Cache, name);
            tar.WriteEntry(source, name);
            using FileStream stored = File.OpenRead(source);
            sums.Append(Convert.ToHexStringLower(SHA256.HashData(stored))).Append("  ").Append(name).Append('\n');
        }

        AddText(tar, "SHA256SUMS", sums.ToString());
        AddText(tar, ContentManifest.FileName, ReleaseJson.Write(chunked.Manifest) + "\n");
    }

    private static List<ContentChunk> ChunkFile(byte[] data, string cache)
    {
        List<(int Offset, int Length)> cuts = [];
        for (int offset = 0; offset < data.Length;)
        {
            int length = ContentChunker.Cut(data.AsSpan(offset));
            cuts.Add((offset, length));
            offset += length;
        }

        // Hashing and compressing are per chunk and independent; the list keeps the file's order.
        ContentChunk[] chunks = new ContentChunk[cuts.Count];
        Parallel.For(0, cuts.Count, i =>
        {
            ReadOnlySpan<byte> bytes = data.AsSpan(cuts[i].Offset, cuts[i].Length);
            string hash = ContentChunker.Hash(bytes);
            string path = Path.Combine(cache, Chunked.StoredName(hash));

            int stored;
            if (File.Exists(path))
            {
                stored = checked((int)new FileInfo(path).Length);
            }
            else
            {
                byte[] encoded = ContentChunker.Encode(bytes);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = $"{path}.{Environment.CurrentManagedThreadId}.tmp";
                File.WriteAllBytes(temporary, encoded);
                File.Move(temporary, path, overwrite: true);
                stored = encoded.Length;
            }

            chunks[i] = new ContentChunk(hash, bytes.Length, stored);
        });

        return [.. chunks];
    }

    private static void AddText(TarWriter tar, string name, string text)
    {
        PaxTarEntry entry = new(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
        };
        tar.WriteEntry(entry);
    }

    private static string Mib(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:F1} MiB");
}
