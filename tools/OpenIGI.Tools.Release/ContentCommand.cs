using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenIGI.Releases;

namespace OpenIGI.Tools.Release;

/// <summary>The <c>content</c> command line: publishing, listing, describing and deleting optional content packs.</summary>
internal static class ContentCommand
{
    /// <summary>The SSH destination when none is given: the release VM's deploy user.</summary>
    private const string DefaultSsh = "deploy@sh-de";

    private const string Store = "[--ssh <host> [--remote <cmd>] [--service <url>] | --to-dir <dir> | --upload-file <tar.gz>]";

    internal const string Usage =
        "usage: content publish <pack folder> --pack <id> --version <v> --title <name> --summary <text> (--game <id> | --kind runtime --platform <rid>[,<rid>...]) [--min-game <v>] [--cache <dir>] [--dry-run] " + Store + "\n"
        + "       content list " + Store + "\n"
        + "       content edit <id> [--title <name>] [--summary <text>] [--thumbnail <png|jpg|webp> | --no-thumbnail] " + Store + "\n"
        + "       content delete <id> [--version <v>] --yes " + Store;

    /// <summary>Runs the command.</summary>
    /// <param name="context">Where the command reports to.</param>
    /// <param name="args">The arguments after <c>content</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(ToolContext context, IReadOnlyList<string> args)
    {
        if (args.Count == 0 || (args[0] != "list" && (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))))
        {
            return context.Usage(Usage);
        }

        string verb = args[0];
        string? subject = verb == "list" ? null : args[1];
        HashSet<string> flags = new(StringComparer.Ordinal);
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = verb == "list" ? 1 : 2; i < args.Count; i++)
        {
            if (args[i] is "--dry-run" or "--yes" or "--no-thumbnail")
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

        try
        {
            IContentStore store = OpenStore(options);
            return verb switch
            {
                "publish" => Publish(context, subject!, options, flags, store),
                "list" => List(context, store),
                "edit" => Edit(context, subject!, options, flags, store),
                "delete" => Delete(context, subject!, options, flags, store),
                _ => Unknown(context, verb),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidDataException
                                       or InvalidOperationException or UnauthorizedAccessException or JsonException
                                       or NotSupportedException or HttpRequestException)
        {
            return context.Fail(ex.Message, ex);
        }
    }

    private static IContentStore OpenStore(Dictionary<string, string> options) =>
        options.TryGetValue("--to-dir", out string? directory) ? new FolderStore(directory)
        : options.TryGetValue("--upload-file", out string? tarball) ? new TarballStore(tarball)
        : new SshStore(
            options.GetValueOrDefault("--ssh") ?? DefaultSsh,
            options.GetValueOrDefault("--remote") ?? string.Empty,
            new Uri((options.GetValueOrDefault("--service") ?? "https://api.openigi.com/").TrimEnd('/') + "/"));

    private static int Publish(ToolContext context, string folder, Dictionary<string, string> options, HashSet<string> flags, IContentStore store)
    {
        ContentKind kind = options.GetValueOrDefault("--kind") switch
        {
            null or "overlay" => ContentKind.Overlay,
            "runtime" => ContentKind.Runtime,
            { } other => throw new ArgumentException($"'{other}' is not a pack kind: overlay or runtime."),
        };

        // A runtime pack is the engine's, not a game's: published for the one id no game has, so that no
        // launcher from before kinds ever offers it.
        string game = kind == ContentKind.Runtime
            ? options.GetValueOrDefault("--game") ?? RuntimePacks.Game
            : Require(options, "--game");

        ContentPublish.Details details = new(
            Require(options, "--pack"),
            Require(options, "--version"),
            Require(options, "--title"),
            Require(options, "--summary"),
            game,
            options.GetValueOrDefault("--min-game"),
            kind,
            options.GetValueOrDefault("--platform")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // The cache keeps every chunk's compressed object between runs, so publishing the next version of a
        // pack compresses only the chunks that changed.
        string cache = options.GetValueOrDefault("--cache")
            ?? Path.Combine(Path.GetTempPath(), "openigi-content-cache", details.Id);

        context.Info($"chunking {Path.GetFullPath(folder)} ...");
        ContentPublish.Chunked chunked = ContentPublish.Chunk(folder, details, cache, DateTimeOffset.UtcNow);
        ContentPublish.Publish(chunked, store, flags.Contains("--dry-run"), context.Out);
        return 0;
    }

    private static int List(ToolContext context, IContentStore store)
    {
        IReadOnlyList<ContentPack> packs = store.List();
        if (packs.Count == 0)
        {
            context.Out.WriteLine("no packs are published");
        }

        foreach (ContentPack pack in packs)
        {
            context.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{pack.Id,-20} {pack.Version,-10} {pack.Size / 1048576.0,8:F1} MiB  {pack.Title}"));
            context.Out.WriteLine($"{string.Empty,-20} {pack.Summary}");
            context.Out.WriteLine($"{string.Empty,-20} picture: {pack.Thumbnail ?? "none"}");
        }

        return 0;
    }

    // A rename, a new summary or a new picture: the pack's own description, which goes over every version's
    // without publishing one. What is not given stays as players see it now.
    private static int Edit(ToolContext context, string id, Dictionary<string, string> options, HashSet<string> flags, IContentStore store)
    {
        string? title = options.GetValueOrDefault("--title");
        string? summary = options.GetValueOrDefault("--summary");
        string? picture = options.GetValueOrDefault("--thumbnail");
        if (title is null && summary is null && picture is null && !flags.Contains("--no-thumbnail"))
        {
            throw new ArgumentException("Nothing to change: give --title, --summary, --thumbnail or --no-thumbnail.");
        }

        if (picture is not null && flags.Contains("--no-thumbnail"))
        {
            throw new ArgumentException("--thumbnail and --no-thumbnail cannot both be given.");
        }

        ContentPack? current = store is TarballStore ? null : store.List().FirstOrDefault(p => p.Id == id);
        if (current is null && store is not TarballStore)
        {
            throw new InvalidOperationException($"There is no pack {id}; publish a version first.");
        }

        string? thumbnail = flags.Contains("--no-thumbnail") ? null : CurrentPicture(current);
        if (picture is not null)
        {
            byte[] data = File.ReadAllBytes(picture);
            string extension = ContentPackInfo.PictureExtension(data)
                ?? throw new InvalidDataException($"{picture} is not a PNG, JPEG or WebP picture.");
            if (data.Length > ContentManifest.MaxThumbnailBytes)
            {
                throw new InvalidDataException($"{picture} is larger than {ContentManifest.MaxThumbnailBytes / 1048576} MiB.");
            }

            thumbnail = $"thumbnail-{Convert.ToHexStringLower(SHA256.HashData(data))[..12]}.{extension}";
        }

        ContentPackInfo info = new(ContentPackInfo.CurrentSchema, id, title ?? current?.Title, summary ?? current?.Summary, thumbnail);
        store.Describe(info, picture);
        context.Info($"{id}: \"{info.Title ?? "(the manifest's title)"}\" -- {info.Summary ?? "(the manifest's summary)"} -- picture {info.Thumbnail ?? "none"}");
        return 0;
    }

    private static int Delete(ToolContext context, string id, Dictionary<string, string> options, HashSet<string> flags, IContentStore store)
    {
        string? version = options.GetValueOrDefault("--version");
        string what = version is null ? $"{id} and every version of it" : $"{id} {version}";
        if (!flags.Contains("--yes"))
        {
            return context.Fail(
                $"This deletes {what} from the store; players who installed it keep their files but get no updates. "
                + "Run again with --yes to do it.");
        }

        store.Delete(id, version);
        context.Info($"deleted {what}");
        return 0;
    }

    // The picture a pack has now, by the file name at the end of its address.
    private static string? CurrentPicture(ContentPack? pack)
    {
        string? name = pack?.Thumbnail is { } address ? Path.GetFileName(address.Replace('\\', '/').Split('/')[^1]) : null;
        return ContentPackInfo.IsValidThumbnail(name) ? name : null;
    }

    private static int Unknown(ToolContext context, string verb) =>
        context.Usage(Usage, $"unknown content command '{verb}'.");

    private static string Require(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");
}
