using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>The <c>site</c> command line: builds openigi.com as one folder for GitHub Pages to serve.</summary>
/// <remarks>
/// <para>
/// The site is the browser build of the newest stable release at its root and of the newest release of all at
/// <c>/beta/</c> (each release carries its build as <c>openigi-web-&lt;version&gt;.tar.gz</c>), and beside them what
/// <see cref="SiteWriter"/> writes from the releases' <c>release.json</c> files and the content store.
/// </para>
/// <para>
/// The browser build's precompressed <c>.br</c> and <c>.gz</c> siblings are left out: GitHub Pages compresses as it
/// serves and would only hand them out as files of their own.
/// </para>
/// </remarks>
internal static class SiteCommand
{
    internal const string Usage =
        "usage: site build --out <dir> [--repo <owner/repo>] [--content-repo <owner/repo>] [--releases <dir>] [--content <dir>] [--no-web]";

    /// <summary>Where the site's pictures are served from; <see cref="SiteWriter"/> puts them there.</summary>
    private const string ThumbnailUrl = "https://openigi.com/content/{pack}/{name}";

    /// <summary>Runs the command.</summary>
    /// <param name="context">Where the command reports to.</param>
    /// <param name="args">The arguments after <c>site</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(ToolContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] != "build")
        {
            return context.Usage(Usage);
        }

        HashSet<string> flags = new(StringComparer.Ordinal);
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is "--no-web")
            {
                flags.Add(args[i]);
            }
            else if (i + 1 < args.Count)
            {
                options[args[i]] = args[++i];
            }
            else
            {
                return context.Usage(Usage, $"{args[i]} needs a value.");
            }
        }

        if (!options.TryGetValue("--out", out string? output))
        {
            return context.Usage(Usage, "--out is required.");
        }

        string repository = options.GetValueOrDefault("--repo") ?? ContentCommand.SiteRepository;
        string contentRepository = options.GetValueOrDefault("--content-repo") ?? ContentCommand.DefaultRepository;
        string work = Directory.CreateTempSubdirectory("openigi-site-").FullName;
        try
        {
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                throw new IOException($"{output} is not empty; the site is built into an empty folder.");
            }

            Directory.CreateDirectory(output);

            // Read from GitHub unless folders are given, which is how the build is tried offline.
            List<GitHubRelease>? published = null;
            if (!options.TryGetValue("--releases", out string? releases))
            {
                releases = Path.Combine(work, "releases");
                published = [.. GitHub.Releases(repository).Where(static r => !r.Draft)];
                foreach (GitHubRelease release in published.Where(static r => r.Assets.Any(static a => a.Name == ReleaseManifest.FileName)))
                {
                    GitHub.Download(repository, release.Tag, Path.Combine(releases, release.Tag), ReleaseManifest.FileName);
                }
            }

            if (!options.TryGetValue("--content", out string? content))
            {
                content = Path.Combine(work, "content");
                GitHubStore.Unpack(contentRepository, content);
            }

            void Skipped(string path, string reason) => context.Warn($"skipped {path}: {reason}");
            ReleaseIndex index = StoreScan.Releases(releases, $"https://github.com/{repository}/releases/download/{{tag}}/{{name}}", Skipped);
            ContentLibrary library = StoreScan.Content(content, GitHubStore.ChunkUrl(contentRepository), ThumbnailUrl, Skipped);

            if (!flags.Contains("--no-web") && published is not null)
            {
                Web(context, repository, published, index, ReleaseChannel.Stable, output, work);
                Web(context, repository, published, index, ReleaseChannel.Beta, Path.Combine(output, "beta"), work);
                File.WriteAllText(Path.Combine(output, "robots.txt"), "User-agent: *\nDisallow: /beta/\n");
            }

            SiteWriter.Write(output, index, library, content);
            context.Info($"site built in {Path.GetFullPath(output)}: {index.Count} release(s), {library.Count} content version(s)");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or JsonException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return context.Fail(ex.Message, ex);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // The newest release the channel offers that carries a browser build, unpacked into the folder.
    private static void Web(
        ToolContext context, string repository, List<GitHubRelease> published, ReleaseIndex index, ReleaseChannel channel, string folder, string work)
    {
        foreach (ReleaseManifest release in index.Published(channel, int.MaxValue).Releases)
        {
            string name = $"openigi-web-{release.Version}.tar.gz";
            if (published.FirstOrDefault(r => r.Tag == release.ReleaseTag) is not { } github || !github.Assets.Any(a => a.Name == name))
            {
                continue;
            }

            string download = Path.Combine(work, "web", release.Version);
            if (!File.Exists(Path.Combine(download, name)))
            {
                GitHub.Download(repository, release.ReleaseTag, download, name);
            }

            Unpack(Path.Combine(download, name), folder);
            context.Info($"{channel.Name()} site: the browser build of {release.Version}");
            return;
        }

        context.Warn($"no {channel.Name()} release carries a browser build; the {channel.Name()} site is left out");
    }

    private static void Unpack(string archive, string folder)
    {
        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        using FileStream file = File.OpenRead(archive);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using TarReader tar = new(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                || entry.Name.EndsWith(".br", StringComparison.Ordinal)
                || entry.Name.EndsWith(".gz", StringComparison.Ordinal))
            {
                continue;
            }

            string target = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{archive} holds {entry.Name}, which is outside the site.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
