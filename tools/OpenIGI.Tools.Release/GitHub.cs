using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Tools.Release;

/// <summary>One GitHub release, as the release and content tooling reads it.</summary>
/// <param name="Id">The release's id.</param>
/// <param name="Tag">Its tag.</param>
/// <param name="Draft">Whether it is an unpublished draft.</param>
/// <param name="Assets">Its files.</param>
internal sealed record GitHubRelease(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

/// <summary>One file of a GitHub release.</summary>
/// <param name="Id">The asset's id.</param>
/// <param name="Name">Its file name.</param>
internal sealed record GitHubAsset(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>GitHub, through the <c>gh</c> command line, signed in as whoever runs it (<c>GH_TOKEN</c> in a workflow).</summary>
internal static class GitHub
{
    /// <summary>Lists a repository's releases, drafts included.</summary>
    /// <param name="repository">The repository, <c>owner/name</c>.</param>
    /// <returns>The releases, newest first, each with its files as the release listing names them.</returns>
    public static List<GitHubRelease> Releases(string repository) =>
        Pages<GitHubRelease>(Run("api", "--paginate", $"repos/{repository}/releases?per_page=100"));

    /// <summary>Lists every file of one release, however many there are.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="release">The release's id.</param>
    /// <returns>The files.</returns>
    public static List<GitHubAsset> Assets(string repository, long release) =>
        Pages<GitHubAsset>(Run("api", "--paginate", $"repos/{repository}/releases/{release}/assets?per_page=100"));

    /// <summary>Downloads files of a release into a folder.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="tag">The release's tag.</param>
    /// <param name="folder">Where they are written.</param>
    /// <param name="names">The files, or none for every file.</param>
    public static void Download(string repository, string tag, string folder, params string[] names)
    {
        List<string> args = ["release", "download", tag, "--repo", repository, "--dir", folder, "--clobber"];
        foreach (string name in names)
        {
            args.Add("--pattern");
            args.Add(name);
        }

        Run([.. args]);
    }

    /// <summary>Creates a release holding store files, unless it is there already.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="tag">The release's tag.</param>
    /// <param name="notes">What the release is for.</param>
    /// <param name="existing">The releases the repository has, which a new one is added to.</param>
    /// <returns>The release.</returns>
    public static GitHubRelease Ensure(string repository, string tag, string notes, List<GitHubRelease> existing)
    {
        if (existing.FirstOrDefault(release => release.Tag == tag) is { } found)
        {
            return found;
        }

        Run("release", "create", tag, "--repo", repository, "--title", tag, "--notes", notes, "--latest=false");
        GitHubRelease created = JsonSerializer.Deserialize<GitHubRelease>(Run("api", $"repos/{repository}/releases/tags/{tag}"))
            ?? throw new IOException($"GitHub did not describe the release {tag} it just created.");
        existing.Add(created);
        return created;
    }

    /// <summary>Uploads files to a release, replacing ones of the same name.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="tag">The release's tag.</param>
    /// <param name="files">The files.</param>
    public static void Upload(string repository, string tag, IReadOnlyList<string> files)
    {
        // A few dozen at a time: every file is an argument, and a command line has a length limit.
        foreach (string[] batch in files.Chunk(32))
        {
            Run(["release", "upload", tag, "--repo", repository, "--clobber", .. batch]);
        }
    }

    /// <summary>Deletes one file of a release.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="asset">The file.</param>
    public static void Delete(string repository, GitHubAsset asset) =>
        Run("api", "--method", "DELETE", $"repos/{repository}/releases/assets/{asset.Id}");

    /// <summary>Starts a workflow on a branch.</summary>
    /// <param name="repository">The repository.</param>
    /// <param name="workflow">The workflow's file name.</param>
    /// <param name="branch">The branch it runs on.</param>
    public static void Dispatch(string repository, string workflow, string branch) =>
        Run("workflow", "run", workflow, "--repo", repository, "--ref", branch);

    /// <summary>Runs <c>gh</c>.</summary>
    /// <param name="args">Its arguments.</param>
    /// <returns>What it wrote.</returns>
    /// <exception cref="IOException">It could not be started or failed.</exception>
    public static string Run(params string[] args)
    {
        ProcessStartInfo start = new("gh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process gh = Process.Start(start) ?? throw new IOException("gh could not be started; is the GitHub CLI installed?");
        Task<string> error = gh.StandardError.ReadToEndAsync();
        string output = gh.StandardOutput.ReadToEnd();
        gh.WaitForExit();
        return gh.ExitCode == 0
            ? output
            : throw new IOException($"gh {string.Join(' ', args.Take(3))} failed: {error.GetAwaiter().GetResult().Trim()}");
    }

    // `gh api --paginate` writes each page's array after the last, so the answer is several JSON documents in a row.
    private static List<T> Pages<T>(string json)
    {
        List<T> items = [];
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            items.AddRange(JsonSerializer.Deserialize<List<T>>(ref reader) ?? []);
        }

        return items;
    }
}
