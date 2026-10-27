using System.Diagnostics;
using System.Text.Json;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>The <c>mods-index</c> command line: what the mods index's workflows run on a pull request and on a merge.</summary>
internal static class ModsIndexCommand
{
    internal const string Usage =
        "usage: mods-index check <index> [--base <git ref> --author <login>]\n"
        + "       mods-index added <index> --base <git ref>\n"
        + "       mods-index build <index> <id> <version> --out <dir> [--work <dir>]\n"
        + "       mods-index publish <index> <id> <version> --package <dir> [--cache <dir>] [--dry-run true] " + ContentCommand.Store;

    /// <summary>Runs the command.</summary>
    /// <param name="context">Where the command reports to.</param>
    /// <param name="args">The arguments after <c>mods-index</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(ToolContext context, IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return context.Usage(Usage);
        }

        string verb = args[0];
        string index = args[1];
        List<string> positional = [];
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Count)
                {
                    return context.Usage(Usage, $"{args[i]} needs a value.");
                }

                options[args[i]] = args[++i];
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        try
        {
            switch (verb)
            {
                case "check":
                {
                    string? baseRef = options.GetValueOrDefault("--base");
                    IReadOnlyList<ModIndexChange>? changes = baseRef is null ? null : Changes(index, baseRef);
                    IReadOnlyList<string> problems = ModsIndex.Check(
                        index,
                        changes,
                        options.GetValueOrDefault("--author"),
                        baseRef is null ? null : id => Before(index, baseRef, id));
                    foreach (string problem in problems)
                    {
                        context.Error.WriteLine(problem);
                    }

                    context.Info(problems.Count == 0 ? "the index is in order" : $"{problems.Count} problem(s)");
                    return problems.Count == 0 ? 0 : 1;
                }

                case "added":
                    foreach ((string id, string version) in ModsIndex.Added(Changes(index, Require(options, "--base"))))
                    {
                        context.Out.WriteLine($"{id} {version}");
                    }

                    return 0;

                case "build" when positional.Count == 2:
                {
                    string output = Require(options, "--out");
                    using HttpClient http = new();
                    ContentPublish.Details details = ModsIndex.Build(
                        index,
                        positional[0],
                        positional[1],
                        output,
                        options.GetValueOrDefault("--work") ?? Path.Combine(Path.GetTempPath(), "openigi-mod-source", positional[0]),
                        http,
                        context.Out);
                    context.Info($"built {details.Id} {details.Version} into {Path.GetFullPath(output)}");
                    return 0;
                }

                case "publish" when positional.Count == 2:
                {
                    // What a build left, published as the index names it: the source and commit come from the index,
                    // never from the package, and the package must be the version the index lists.
                    string package = Require(options, "--package");
                    ContentPublish.Details details = ModsIndex.Describe(index, positional[0], positional[1], package);
                    string cache = options.GetValueOrDefault("--cache") ?? Path.Combine(Path.GetTempPath(), "openigi-content-cache", details.Id);
                    ContentPublish.Chunked chunked = ContentPublish.Chunk(package, details, cache, DateTimeOffset.UtcNow);
                    bool dryRun = options.GetValueOrDefault("--dry-run") == "true";
                    IContentStore store = ContentCommand.OpenStore(options, []);
                    ContentPublish.Publish(chunked, store, dryRun, context.Out);

                    // The picture beside the mod's entry, when it has one, as the mod's own description.
                    if (!dryRun && ModsIndex.Thumbnail(index, details.Id) is { } picture)
                    {
                        store.Describe(new ContentPackInfo(ContentPackInfo.CurrentSchema, details.Id, null, null, picture.Name), picture.Path);
                        context.Info($"{details.Id}: picture {picture.Name}");
                    }

                    return 0;
                }

                default:
                    return context.Usage(Usage, $"unknown mods-index command '{verb}'.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException
                                       or UnauthorizedAccessException or JsonException or HttpRequestException
                                       or System.ComponentModel.Win32Exception)
        {
            return context.Fail(ex.Message, ex);
        }
    }

    // What a pull request changes, against the commit it is merged into.
    private static List<ModIndexChange> Changes(string index, string baseRef)
    {
        List<ModIndexChange> changes = [];
        foreach (string line in Git(index, "diff", "--name-status", "--no-renames", $"{baseRef}...HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.TrimEnd('\r').Split('\t');
            if (parts.Length >= 2)
            {
                changes.Add(new ModIndexChange(parts[0][..1], parts[^1]));
            }
        }

        return changes;
    }

    // A mod's entry as it stood at the base, or nothing for a mod the pull request adds.
    private static ModIndexEntry? Before(string index, string baseRef, string id)
    {
        try
        {
            string json = Git(index, "show", $"{baseRef}:{ModsIndex.ModsFolder}/{id}/{ModsIndex.EntryFile}");
            return JsonSerializer.Deserialize(json, ModsIndexJson.Default.ModIndexEntry);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string Git(string index, params string[] arguments)
    {
        ProcessStartInfo start = new("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(index);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error.Result.Trim()}");
    }

    private static string Require(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");
}
