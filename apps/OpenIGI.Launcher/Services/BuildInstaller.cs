using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Releases;

namespace OpenIGI.Launcher.Services;

/// <summary>An installed build: which version, where, and the files it was installed with.</summary>
/// <param name="Version">The release version.</param>
/// <param name="Folder">The version folder the build was installed into.</param>
/// <param name="Files">Every file of the build, relative to <paramref name="Folder"/>, with its size and hash.</param>
/// <param name="SelfContained">
/// Whether the build carries its own .NET runtime; the other flavour needs the runtime installed to start.
/// </param>
internal sealed record InstalledBuild(string Version, string Folder, IReadOnlyList<InstalledFile> Files, bool SelfContained = true);

/// <summary>One installed file, as it was when it was extracted.</summary>
/// <param name="Path">Relative to the version folder, with forward slashes.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Sha256">Its lowercase hex SHA-256.</param>
internal sealed record InstalledFile(string Path, long Size, string Sha256);

/// <summary>How far an install has got, for the progress bar and the footer.</summary>
/// <param name="Done">Bytes downloaded so far, including any resumed from an earlier attempt.</param>
/// <param name="Total">The archive's size.</param>
/// <param name="Stage">What it is doing.</param>
internal readonly record struct InstallProgress(long Done, long Total, InstallStage Stage);

/// <summary>The steps of an install.</summary>
internal enum InstallStage
{
    /// <summary>Fetching the archive.</summary>
    Downloading,

    /// <summary>Checking the archive's hash.</summary>
    Verifying,

    /// <summary>Unpacking it into its version folder.</summary>
    Installing,
}

/// <summary>Installs one component's builds into a folder of the library, one version folder per release.</summary>
/// <remarks>
/// <para>
/// <c>&lt;root&gt;/versions/&lt;version&gt;/</c> holds each build and <c>&lt;root&gt;/current.json</c> names the
/// one in use. A new build is downloaded (resuming a partial download), checked against the SHA-256 the
/// release's manifest gives, extracted into a staging folder and renamed into place, and only then does
/// <c>current.json</c> move to it — so a failure at any point leaves the previous build exactly as it was.
/// The previous build is kept for one release, as the fallback a player can be put back on.
/// </para>
/// <para>
/// Every file's hash is recorded as it is extracted, which is what <see cref="Verify"/> checks the install
/// against later: a file antivirus removed or a disk corrupted is found without downloading anything.
/// </para>
/// </remarks>
internal sealed class BuildInstaller(HttpClient http, string root)
{
    private const string PointerFile = "current.json";
    private const string VersionsFolder = "versions";

    /// <summary>Gets the folder this installer manages.</summary>
    public string Root { get; } = root;

    /// <summary>Reads which build is installed.</summary>
    /// <returns>The build in use, or <see langword="null"/> when none is installed (or its folder is gone).</returns>
    public InstalledBuild? Current()
    {
        string pointer = Path.Combine(Root, PointerFile);
        try
        {
            if (!File.Exists(pointer))
            {
                return null;
            }

            InstalledBuild? build = JsonSerializer.Deserialize(File.ReadAllText(pointer), InstallJson.Default.InstalledBuild);
            return build is not null && Directory.Exists(build.Folder) ? build : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Gets whether a version other than the current one is still on disk, kept from before.</summary>
    /// <param name="version">The release version.</param>
    /// <returns>Whether its folder is there; <see cref="TryReuse"/> still checks it is intact.</returns>
    /// <remarks>Cheap enough to ask whenever the pages are refreshed: it hashes nothing.</remarks>
    public bool IsKept(string version) =>
        Current()?.Version != version && Directory.Exists(Path.Combine(Root, VersionsFolder, version));

    /// <summary>Makes a build that is still on disk the current one again, without downloading it.</summary>
    /// <param name="version">The release version.</param>
    /// <param name="asset">The release's download, whose hash identifies a single-file build kept from before.</param>
    /// <param name="executableName">The name a single-file build is installed under.</param>
    /// <returns>The build, or <see langword="null"/> when it is not on disk or no longer intact.</returns>
    /// <remarks>
    /// The build an install replaces is kept, so switching back (leaving a beta, say) finds it here. Its files
    /// are checked against the hashes recorded when it was installed. A build kept from before those records
    /// existed is recognised when it is a single file whose hash is the release's own.
    /// </remarks>
    public InstalledBuild? TryReuse(string version, ReleaseAsset asset, string executableName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string folder = Path.Combine(Root, VersionsFolder, version);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        InstalledBuild? build = ReadRecord(version) ?? SingleFileBuild(version, folder, asset, executableName);
        if (build is null
            || !string.Equals(Path.GetFullPath(build.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
            || Verify(build).Count > 0)
        {
            return null;
        }

        Activate(build);
        return build;
    }

    /// <summary>Downloads, checks and installs one build, then makes it the current one.</summary>
    /// <param name="version">The release version.</param>
    /// <param name="asset">The download: an executable, or an archive of a macOS bundle.</param>
    /// <param name="executableName">
    /// The name a download that is the executable itself is installed under (it is published as
    /// <c>openigi-game-0.2.0-win-x64.exe</c> and started as <c>OpenIGI.Desktop.exe</c>). Archives keep their
    /// own names.
    /// </param>
    /// <param name="progress">Told how far it has got.</param>
    /// <param name="cancellationToken">Cancels the install; a partial download is kept and resumed next time.</param>
    /// <returns>The installed build.</returns>
    /// <exception cref="InvalidDataException">The download does not match its hash.</exception>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    public async Task<InstalledBuild> InstallAsync(
        string version,
        ReleaseAsset asset,
        string executableName,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(progress);
        if (asset.Url is null)
        {
            throw new InvalidDataException($"{asset.Name} has no download address.");
        }

        string downloads = Directory.CreateDirectory(Path.Combine(Root, "downloads")).FullName;
        string archive = Path.Combine(downloads, asset.Name);
        string partial = archive + ".part";

        if (!File.Exists(archive) || !await HashMatchesAsync(archive, asset.Sha256, cancellationToken).ConfigureAwait(false))
        {
            await DownloadAsync(new Uri(asset.Url), partial, asset.Size, progress, cancellationToken).ConfigureAwait(false);
            progress.Report(new InstallProgress(asset.Size, asset.Size, InstallStage.Verifying));
            if (!await HashMatchesAsync(partial, asset.Sha256, cancellationToken).ConfigureAwait(false))
            {
                File.Delete(partial);
                throw new InvalidDataException($"{asset.Name} did not download intact (its SHA-256 does not match the release).");
            }

            File.Move(partial, archive, overwrite: true);
        }

        progress.Report(new InstallProgress(asset.Size, asset.Size, InstallStage.Installing));
        InstalledBuild build = await Task.Run(() => Extract(version, archive, executableName, asset.SelfContained), cancellationToken).ConfigureAwait(false);

        File.Delete(archive);
        return build;
    }

    /// <summary>Checks every installed file against the hash it was installed with.</summary>
    /// <param name="build">The build to check.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The files that are missing or changed; empty when the install is intact.</returns>
    public static IReadOnlyList<string> Verify(InstalledBuild build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        List<string> damaged = [];
        foreach (InstalledFile file in build.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(build.Folder, file.Path);
            FileInfo info = new(path);
            if (!info.Exists || info.Length != file.Size || HashFile(path) != file.Sha256)
            {
                damaged.Add(file.Path);
            }
        }

        return damaged;
    }

    /// <summary>Hashes a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its lowercase hex SHA-256.</returns>
    public static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private async Task DownloadAsync(Uri url, string partial, long expected, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        long resumeFrom = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (resumeFrom >= expected && expected > 0)
        {
            // A whole file left over: let the hash decide whether it is the right one.
            return;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (resumeFrom > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        }

        using HttpResponseMessage response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // A server that ignores the range sends the whole file again; start over rather than append it.
        bool resumed = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
        {
            resumeFrom = 0;
        }

        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream file = new(partial, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        byte[] buffer = new byte[1 << 16];
        long done = resumeFrom;
        long reported = 0;
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            if (done - reported >= 1 << 18)
            {
                progress.Report(new InstallProgress(done, expected, InstallStage.Downloading));
                reported = done;
            }
        }

        progress.Report(new InstallProgress(done, expected, InstallStage.Downloading));
    }

    private static async Task<bool> HashMatchesAsync(string path, string sha256, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexStringLower(hash), sha256, StringComparison.OrdinalIgnoreCase);
    }

    private InstalledBuild Extract(string version, string archive, string executableName, bool selfContained)
    {
        string versions = Directory.CreateDirectory(Path.Combine(Root, VersionsFolder)).FullName;
        string folder = Path.Combine(versions, version);
        string staging = Path.Combine(versions, $".{version}.{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, staging, overwriteFiles: true);
            }
            else if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream compressed = File.OpenRead(archive);
                using GZipStream tar = new(compressed, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(tar, staging, overwriteFiles: true);
            }
            else
            {
                // A single-file build: the download is the program.
                string executable = Path.Combine(staging, executableName);
                File.Copy(archive, executable);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }

            List<InstalledFile> files = [.. Directory
                .EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(path => new InstalledFile(
                    Path.GetRelativePath(staging, path).Replace('\\', '/'),
                    new FileInfo(path).Length,
                    HashFile(path)))
                .OrderBy(static f => f.Path, StringComparer.Ordinal)];

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            Directory.Move(staging, folder);

            InstalledBuild build = new(version, folder, files, selfContained);
            WriteJson(RecordPath(version), build);
            Activate(build);
            return build;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void WriteJson(string path, InstalledBuild build)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(build, InstallJson.Default.InstalledBuild));
        File.Move(temporary, path, overwrite: true);
    }

    // A download that is the program itself, kept from before builds were recorded: its one file is the
    // release's asset, so the asset's hash says whether it is intact.
    private static InstalledBuild? SingleFileBuild(string version, string folder, ReleaseAsset asset, string executableName)
    {
        string executable = Path.Combine(folder, executableName);
        if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || asset.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(executable)
            || !string.Equals(HashFile(executable), asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        InstalledFile file = new(executableName, new FileInfo(executable).Length, asset.Sha256.ToLowerInvariant());
        return new InstalledBuild(version, folder, [file], asset.SelfContained);
    }

    // Each build's record lives beside its folder rather than in it, so it is never one of the build's files.
    private string RecordPath(string version) => Path.Combine(Root, VersionsFolder, version + ".json");

    private InstalledBuild? ReadRecord(string version)
    {
        try
        {
            string path = RecordPath(version);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), InstallJson.Default.InstalledBuild) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Points current.json at a build and keeps the one it replaces, as the fallback a player can go back to.
    private void Activate(InstalledBuild build)
    {
        InstalledBuild? previous = Current();
        WriteJson(Path.Combine(Root, PointerFile), build);
        if (previous is not null && !File.Exists(RecordPath(previous.Version)))
        {
            WriteJson(RecordPath(previous.Version), previous);
        }

        RemoveOldVersions(keep: [build.Folder, previous?.Folder]);
    }

    private void RemoveOldVersions(string?[] keep)
    {
        foreach (string dir in Directory.EnumerateDirectories(Path.Combine(Root, VersionsFolder)))
        {
            if (keep.Any(k => k is not null && Path.GetFullPath(k) == Path.GetFullPath(dir)))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
                File.Delete(dir + ".json");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A version still running (Windows keeps its files locked) goes the next time round.
            }
        }
    }
}

/// <summary>The install pointer's JSON shape.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(InstalledBuild))]
internal sealed partial class InstallJson : JsonSerializerContext;
