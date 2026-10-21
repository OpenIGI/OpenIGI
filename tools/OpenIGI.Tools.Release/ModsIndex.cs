using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Content;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>A mod as the mods index lists it: who may publish it, and where it lives.</summary>
/// <param name="Owners">The GitHub accounts that may publish its versions and change this entry.</param>
/// <param name="Repository">Its public source repository, or <see langword="null"/> for a mod of content alone.</param>
/// <param name="Tags">Words a player finds it by, or <see langword="null"/>.</param>
internal sealed record ModIndexEntry(IReadOnlyList<string> Owners, string? Repository = null, IReadOnlyList<string>? Tags = null);

/// <summary>One version of a mod as the mods index lists it: the source it is built from, or the files it is.</summary>
/// <param name="Source">For a mod with code, the commit and project it is built from.</param>
/// <param name="Files">For a mod of content alone, the archive it is.</param>
internal sealed record ModIndexVersion(ModIndexSource? Source = null, ModIndexFiles? Files = null);

/// <summary>Where a version's code is: the entry's repository at a commit, and the project there that builds it.</summary>
/// <param name="Commit">The full commit hash.</param>
/// <param name="Project">The mod's project, relative to the repository's root.</param>
internal sealed record ModIndexSource(string Commit, string Project);

/// <summary>A mod of content alone: a zip of its folder, <c>mod.json</c> at its root.</summary>
/// <param name="Url">Where the archive is, over HTTPS.</param>
/// <param name="Sha256">The archive's SHA-256, lowercase hex.</param>
internal sealed record ModIndexFiles(string Url, string Sha256);

/// <summary>One file a pull request changes in the mods index, as <c>git diff --name-status</c> reports it.</summary>
/// <param name="Status">A for added, M for modified, D for deleted; anything else as git says it.</param>
/// <param name="Path">The file, relative to the index's root, with forward slashes.</param>
internal sealed record ModIndexChange(string Status, string Path);

/// <summary>
/// The community mods index: a git repository of small JSON files, one entry per mod and one per published version,
/// added by pull request, checked by CI, and published once a maintainer merges it.
/// </summary>
/// <remarks>
/// <para>
/// <c>mods/&lt;id&gt;/index.json</c> is a <see cref="ModIndexEntry"/>; <c>mods/&lt;id&gt;/&lt;version&gt;.json</c> a
/// <see cref="ModIndexVersion"/>. A version is never changed once merged: a fix is a new version. Only an entry's owners
/// may add its versions or change it, as the entry stands before the pull request; whoever adds a mod is among its owners.
/// </para>
/// <para>
/// A mod with code is built here from its source, at the commit its version names, never taken as its author built it:
/// what players run is what anyone can read. A mod of content alone is taken as an archive, checked against its hash, and
/// refused if it carries code.
/// </para>
/// </remarks>
internal static class ModsIndex
{
    /// <summary>The folder of the index that holds the mods.</summary>
    internal const string ModsFolder = "mods";

    /// <summary>An entry's file name, in its mod's folder.</summary>
    internal const string EntryFile = "index.json";

    /// <summary>Checks the whole index, and a pull request's changes to it when given.</summary>
    /// <param name="root">The index's checkout.</param>
    /// <param name="changes">The pull request's changes, or <see langword="null"/> to check the index alone.</param>
    /// <param name="author">The pull request's author, whose right to make <paramref name="changes"/> is checked.</param>
    /// <param name="before">An entry as it stood before the pull request, by mod id; <see langword="null"/> for a new mod.</param>
    /// <returns>What is wrong, one line each; empty when nothing is.</returns>
    internal static IReadOnlyList<string> Check(
        string root,
        IReadOnlyList<ModIndexChange>? changes = null,
        string? author = null,
        Func<string, ModIndexEntry?>? before = null)
    {
        List<string> problems = [];
        string mods = Path.Combine(root, ModsFolder);
        if (Directory.Exists(mods))
        {
            foreach (string folder in Directory.EnumerateDirectories(mods).Order(StringComparer.Ordinal))
            {
                CheckMod(folder, problems);
            }
        }

        foreach (ModIndexChange change in changes ?? [])
        {
            string[] parts = change.Path.Split('/');
            if (parts.Length != 3 || parts[0] != ModsFolder)
            {
                problems.Add($"{change.Path}: a pull request to the index changes mods/<id>/ alone; anything else is a maintainer's.");
                continue;
            }

            string id = parts[1];
            bool entry = parts[2] == EntryFile;
            if (!entry && change.Status != "A")
            {
                problems.Add($"{change.Path}: a published version is never changed or removed; publish a new version instead.");
            }

            if (author is null)
            {
                continue;
            }

            // The owners as the entry stood before: a pull request cannot make its own author an owner of someone else's mod.
            IReadOnlyList<string>? owners = before?.Invoke(id)?.Owners ?? (Read<ModIndexEntry>(Path.Combine(mods, id, EntryFile)) is { } added ? added.Owners : null);
            if (owners is null || !owners.Contains(author, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"{change.Path}: {author} is not an owner of '{id}'.");
            }
        }

        return [.. problems.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Gets the versions a pull request adds, to build and, once merged, publish.</summary>
    /// <param name="changes">The pull request's changes.</param>
    /// <returns>Each added version's mod id and version.</returns>
    internal static IReadOnlyList<(string Id, string Version)> Added(IReadOnlyList<ModIndexChange> changes) =>
        [.. changes
            .Where(static change => change.Status == "A")
            .Select(static change => change.Path.Split('/'))
            .Where(static parts => parts.Length == 3 && parts[0] == ModsFolder && parts[2] != EntryFile && parts[2].EndsWith(".json", StringComparison.Ordinal))
            .Select(static parts => (parts[1], parts[2][..^".json".Length]))];

    /// <summary>Builds a version into the folder an installed mod is: from its source for a mod with code, from its archive otherwise.</summary>
    /// <param name="root">The index's checkout.</param>
    /// <param name="id">The mod.</param>
    /// <param name="version">The version.</param>
    /// <param name="output">The folder to build into; emptied first.</param>
    /// <param name="work">Where the source is checked out.</param>
    /// <param name="http">Fetches a mod of content alone's archive.</param>
    /// <param name="log">Where the build reports.</param>
    /// <returns>The publish details, from the built <c>mod.json</c>, with the source it was built from.</returns>
    /// <exception cref="InvalidDataException">The version, or what it builds, is not what the index says.</exception>
    internal static ContentPublish.Details Build(string root, string id, string version, string output, string work, HttpClient http, TextWriter log)
    {
        string folder = Path.Combine(root, ModsFolder, id);
        ModIndexEntry entry = Read<ModIndexEntry>(Path.Combine(folder, EntryFile)) ?? throw new InvalidDataException($"There is no mod '{id}' in the index.");
        ModIndexVersion listed = Read<ModIndexVersion>(Path.Combine(folder, version + ".json")) ?? throw new InvalidDataException($"'{id}' has no version {version} in the index.");

        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);
        string? source = null;
        string? commit = null;
        if (listed.Source is { } code)
        {
            source = entry.Repository!;
            commit = code.Commit;
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }

            Run(log, "git", "clone", "--quiet", "--no-tags", source, work);
            Run(log, "git", "-C", work, "checkout", "--quiet", "--detach", commit);
            string project = Path.GetFullPath(Path.Combine(work, code.Project));
            if (!project.StartsWith(Path.GetFullPath(work) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The project '{code.Project}' is outside the repository.");
            }

            Run(log, "dotnet", "build", project, "-c", "Release", $"-p:ModPackageDirectory={Path.GetFullPath(output)}{Path.DirectorySeparatorChar}");
        }
        else if (listed.Files is { } files)
        {
            byte[] archive = http.GetByteArrayAsync(new Uri(files.Url)).GetAwaiter().GetResult();
            string hash = Convert.ToHexStringLower(SHA256.HashData(archive));
            if (!string.Equals(hash, files.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{files.Url} is {hash}, not the {files.Sha256} the index names.");
            }

            using MemoryStream stream = new(archive);
            ZipFile.ExtractToDirectory(stream, output);
        }

        return Describe(root, id, version, output);
    }

    /// <summary>Describes a built version for publishing, from its <c>mod.json</c> and the source the index names.</summary>
    /// <param name="root">The index's checkout.</param>
    /// <param name="id">The mod.</param>
    /// <param name="version">The version.</param>
    /// <param name="package">The built package.</param>
    /// <returns>The publish details.</returns>
    /// <exception cref="InvalidDataException">The package is not the version the index lists.</exception>
    internal static ContentPublish.Details Describe(string root, string id, string version, string package)
    {
        string folder = Path.Combine(root, ModsFolder, id);
        ModIndexEntry entry = Read<ModIndexEntry>(Path.Combine(folder, EntryFile)) ?? throw new InvalidDataException($"There is no mod '{id}' in the index.");
        ModIndexVersion listed = Read<ModIndexVersion>(Path.Combine(folder, version + ".json")) ?? throw new InvalidDataException($"'{id}' has no version {version} in the index.");
        ContentPublish.Details details = listed.Source is { } code
            ? ContentPublish.ForMod(package, entry.Repository, code.Commit)
            : ContentPublish.ForMod(package, null, null);
        if (details.Id != id || details.Version != version)
        {
            throw new InvalidDataException($"The package says it is {details.Id} {details.Version}, not {id} {version}.");
        }

        if (listed.Source is null && details.Mod!.RunsCode)
        {
            throw new InvalidDataException($"{id} {version} is listed as files alone, and its mod.json names an assembly: a mod with code is built from its source.");
        }

        return details;
    }

    /// <summary>Finds the picture beside a mod's entry, named for the store by its content.</summary>
    /// <param name="root">The index's checkout.</param>
    /// <param name="id">The mod.</param>
    /// <returns>Its file and the name the store keeps it under, or <see langword="null"/> when it has none.</returns>
    /// <exception cref="InvalidDataException">The picture is not a PNG, JPEG or WebP, or is too large.</exception>
    internal static (string Path, string Name)? Thumbnail(string root, string id)
    {
        foreach (string extension in (string[])["png", "jpg", "webp"])
        {
            string path = Path.Combine(root, ModsFolder, id, $"thumbnail.{extension}");
            if (!File.Exists(path))
            {
                continue;
            }

            byte[] data = File.ReadAllBytes(path);
            string kind = ContentPackInfo.PictureExtension(data) ?? throw new InvalidDataException($"{path} is not a PNG, JPEG or WebP picture.");
            if (data.Length > ContentManifest.MaxThumbnailBytes)
            {
                throw new InvalidDataException($"{path} is larger than {ContentManifest.MaxThumbnailBytes / 1048576} MiB.");
            }

            return (path, $"thumbnail-{Convert.ToHexStringLower(SHA256.HashData(data))[..12]}.{kind}");
        }

        return null;
    }

    // One mod's folder: an entry that reads, and versions that read and say one way to get them.
    private static void CheckMod(string folder, List<string> problems)
    {
        string id = Path.GetFileName(folder);
        string where = $"{ModsFolder}/{id}";
        if (!ContentManifest.IsValidId(id) || id == ModPacks.LocalFolder)
        {
            problems.Add($"{where}: '{id}' is not a mod id: lowercase letters, digits and inner dashes, at most 64, and not '{ModPacks.LocalFolder}'.");
        }

        ModIndexEntry? entry = TryRead<ModIndexEntry>(Path.Combine(folder, EntryFile), problems);
        if (entry is null)
        {
            problems.Add($"{where}: it has no {EntryFile}.");
            return;
        }

        if (entry.Owners is not { Count: > 0 })
        {
            problems.Add($"{where}/{EntryFile}: it names no owners.");
        }

        if (entry.Repository is { } repository && !IsHttps(repository))
        {
            problems.Add($"{where}/{EntryFile}: the repository '{repository}' is not an https address.");
        }

        foreach (string file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            if (name == EntryFile || name.StartsWith("thumbnail.", StringComparison.Ordinal))
            {
                continue;
            }

            string version = Path.GetFileNameWithoutExtension(name);
            if (!name.EndsWith(".json", StringComparison.Ordinal) || !ReleaseVersion.TryParse(version, out ReleaseVersion parsed) || parsed.IsPrerelease)
            {
                problems.Add($"{where}/{name}: a version's file is <major.minor.patch>.json.");
                continue;
            }

            if (TryRead<ModIndexVersion>(file, problems) is not { } listed)
            {
                continue;
            }

            if ((listed.Source is null) == (listed.Files is null))
            {
                problems.Add($"{where}/{name}: a version names its source or its files, one of the two.");
            }

            if (listed.Source is { } source)
            {
                if (entry.Repository is null)
                {
                    problems.Add($"{where}/{name}: it is built from source, and the mod names no repository.");
                }

                if (source.Commit is not { Length: 40 } commit || !commit.All(Uri.IsHexDigit))
                {
                    problems.Add($"{where}/{name}: the commit is a full 40-digit hash.");
                }

                if (string.IsNullOrWhiteSpace(source.Project) || Path.IsPathRooted(source.Project) || source.Project.Split('/', '\\').Contains(".."))
                {
                    problems.Add($"{where}/{name}: the project is a path inside the repository.");
                }
            }

            if (listed.Files is { } files && (!IsHttps(files.Url) || files.Sha256 is not { Length: 64 } || !files.Sha256.All(Uri.IsHexDigit)))
            {
                problems.Add($"{where}/{name}: the files are an https address and a 64-digit SHA-256.");
            }
        }
    }

    private static bool IsHttps(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static T? Read<T>(string path)
        where T : class =>
        File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)ModsIndexJson.Default.GetTypeInfo(typeof(T))!)
            : null;

    private static T? TryRead<T>(string path, List<string> problems)
        where T : class
    {
        try
        {
            return Read<T>(path);
        }
        catch (JsonException ex)
        {
            problems.Add($"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    // Runs a program to the end, its output to the log; a failure stops the build.
    private static void Run(TextWriter log, string program, params string[] arguments)
    {
        ProcessStartInfo start = new(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        process.OutputDataReceived += (_, line) => { if (line.Data is not null) { log.WriteLine(line.Data); } };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) { log.WriteLine(line.Data); } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"{program} {string.Join(' ', arguments)} failed ({process.ExitCode}).");
        }
    }
}

/// <summary>The index's JSON: camelCase, comments and trailing commas allowed, as a person writes it.</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ModIndexEntry))]
[JsonSerializable(typeof(ModIndexVersion))]
internal sealed partial class ModsIndexJson : JsonSerializerContext;
