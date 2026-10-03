using System.Globalization;
using OpenIGI.Releases;
using OpenIGI.Tools;
using OpenIGI.Tools.Release;

// Writes the manifest of one release, beside its archives.
//
//   dotnet run --project tools/OpenIGI.Tools.Release -- manifest
//       --version 0.2.0-beta.3 --channel beta --commit <sha>
//       --notes <git-cliff --context output> --assets <folder of downloads> [--tag <git tag>]
//
// The manifest (release.json) is what the update service serves and the launcher installs from; the same
// run also writes SHA256SUMS, so an archive downloaded by hand can be checked with sha256sum -c.

//   dotnet run --project tools/OpenIGI.Tools.Release -- content publish <pack folder>
//       --pack hd-textures --version 1.0.0 --title <name> --summary <text> --game igi1
//       [--min-game 0.2.0-beta.1] [--ssh deploy@sh-de | --to-dir <store folder> | --upload-file <tar.gz>] [--remote <command>]
//       [--cache <folder>] [--dry-run]
//
// Publishes a version of an optional content pack: cuts its files into chunks, asks the store which of them
// it lacks, and uploads only those with the version's manifest (see ContentPublish). The same command lists
// the packs (content list), renames one, describes it again or changes its picture without publishing a
// version (content edit), and deletes a pack or one of its versions (content delete --yes).

return ToolHost.Run("Release", args, Run);

static int Run(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count >= 1 && args[0] == "content")
    {
        return ContentCommand.Run(ctx, [.. args.Skip(1)]);
    }

    if (args.Count == 0 || args[0] != "manifest")
    {
        return ctx.Usage(
            ContentCommand.Usage + "\n"
            + "usage: manifest --version <v> --channel <stable|beta> --commit <sha> --notes <file> --assets <dir> [--tag <tag>] [--published <iso8601>]");
    }

    Dictionary<string, string> options = new(StringComparer.Ordinal);
    for (int i = 1; i + 1 < args.Count; i += 2)
    {
        options[args[i]] = args[i + 1];
    }

    string Require(string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");

    try
    {
        ReleaseVersion version = ReleaseVersion.Parse(Require("--version"));
        if (!ReleaseJson.TryParseChannel(Require("--channel"), out ReleaseChannel channel))
        {
            throw new ArgumentException("--channel must be stable or beta.");
        }

        // A stable release that looks like a beta, or the reverse, would put a pre-release in front of every
        // player or hide a release from them, so the two must agree before anything is written.
        if (version.IsPrerelease != (channel == ReleaseChannel.Beta))
        {
            throw new ArgumentException($"{version} cannot be published to the {channel.Name()} channel.");
        }

        string assets = Require("--assets");
        DateTimeOffset published = options.TryGetValue("--published", out string? when)
            ? DateTimeOffset.Parse(when, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.UtcNow;

        ReleaseManifest manifest = new(
            ReleaseManifest.CurrentSchema,
            version.ToString(),
            channel,
            published,
            Require("--commit"),
            ReleaseNotesReader.Read(File.ReadAllText(Require("--notes"))),
            AssetCatalog.Scan(assets, version.ToString()),
            options.GetValueOrDefault("--tag"));

        if (manifest.Assets.Count == 0)
        {
            throw new InvalidDataException($"No archives of {version} were found in {assets}.");
        }

        File.WriteAllText(Path.Combine(assets, ReleaseManifest.FileName), ReleaseJson.Write(manifest) + "\n");
        File.WriteAllLines(
            Path.Combine(assets, "SHA256SUMS"),
            manifest.Assets.Select(static asset => $"{asset.Sha256}  {asset.Name}"));

        ctx.Out.WriteLine($"{manifest.Version} ({channel.Name()}): {manifest.Assets.Count} archive(s), "
            + $"{manifest.Notes.Sum(static s => s.Entries.Count)} note(s)");
        foreach (ReleaseAsset asset in manifest.Assets)
        {
            ctx.Out.WriteLine($"  {asset.Name,-60} {asset.Size,12:N0}  {asset.Sha256[..12]}");
        }

        return ToolExitCode.Ok;
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidDataException
                                   or System.Text.Json.JsonException)
    {
        return ctx.Fail(ex.Message, ex);
    }
}
