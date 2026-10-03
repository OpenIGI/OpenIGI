using System.Globalization;
using OpenIGI.Core;
using OpenIGI.Core.Audio;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Legacy;
using OpenIGI.Formats.Sound;
using OpenIGI.Formats.Textures;
using OpenIGI.Tools;
using OpenIGI.Tools.ResExtract;

return ToolHost.Run("ResExtract", args, Run);

static int Run(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
    {
        if (args.Count == 0)
        {
            return ctx.Usage(UsageText);
        }

        ctx.Out.WriteLine(UsageText);
        return ToolExitCode.Ok;
    }

    try
    {
        return args[0].ToUpperInvariant() switch
        {
            "LIST" => List(ctx),
            "EXTRACT" => Extract(ctx),
            "IMAGES" => Images(ctx),
            "PACK" => Pack(ctx),
            "COMPRESS" => Compress(ctx),
            "MAPS" => Maps(ctx),
            "SOUNDS" => Sounds(ctx),
            _ => ctx.Usage(UsageText, $"Unknown command '{args[0]}'."),
        };
    }
    catch (Exception error) when (error is IlffFormatException or FlatResourceFormatException
        or IlsfFormatException or IOException or UnauthorizedAccessException)
    {
        return ctx.Fail(error.Message, error);
    }
}

static int List(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count != 2)
    {
        return ctx.Usage(UsageText, "usage: ResExtract list <archive>");
    }

    IReadOnlyList<ArchiveItem> items = ReadArchive(args[1]);
    long total = 0;

    foreach (ArchiveItem item in items)
    {
        ctx.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{item.Kind,-13} {item.Data.Length,10}  {item.Name}"));
        total += item.Data.Length;
    }

    ctx.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{items.Count} entries, {total} bytes."));
    return ToolExitCode.Ok;
}

static int Extract(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count != 3)
    {
        return ctx.Usage(UsageText, "usage: ResExtract extract <archive> <outdir>");
    }

    string outputRoot = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(outputRoot);

    int written = 0;
    int skipped = 0;

    foreach (ArchiveItem item in ReadArchive(args[1]))
    {
        string? relativePath = ToRelativePath(item);
        string? path = relativePath is null ? null : Path.GetFullPath(Path.Combine(outputRoot, relativePath));

        if (path is null || !path.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Warn($"skipping '{item.Name}': its name does not map to a file below the output directory.");
            skipped++;
            continue;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, item.Data.ToArray());
        written++;
    }

    ctx.Info(string.Create(
        CultureInfo.InvariantCulture,
        $"Wrote {written} file(s) to {outputRoot}{(skipped > 0 ? $", skipped {skipped}" : string.Empty)}."));
    return ToolExitCode.Ok;
}

// Writes every picture in an installation out as a PNG, with a manifest saying where each came from.
//
// This exists to get the game's art OUT so it can be worked on -- upscaled, remastered, or given the
// surface maps a physically-based renderer needs and this game never had -- and then put back through
// `pack`. The pair is the whole point: what comes out has to carry enough about where it came from that
// what goes back is the game's own files, in the game's own containers, at the game's own paths.
//
// EVERY picture, established by scanning a retail installation for the "LOOP" magic rather than by
// trusting a list of extensions: 8,232 of them in 7,388 containers, all of them either inside a .res
// archive or in a loose .tex. The version of this command that this replaced found the 7,199 single-image
// entries inside archives and silently passed over the rest -- which is why an upscale pack visibly
// improved the world and left the menu, the loading screens, the HUD, the computer terminals, the sprites
// and every square metre of ground exactly as they were.
static int Images(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count != 3)
    {
        return ctx.Usage(UsageText, "usage: ResExtract images <archive|dir> <outdir>");
    }

    ImageExport.Result result = ImageExport.Run(args[1], Path.GetFullPath(args[2]));

    foreach (string failure in result.Failures.Take(20))
    {
        ctx.Warn(failure);
    }

    if (result.Failures.Count > 20)
    {
        ctx.Warn($"and {result.Failures.Count - 20} more.");
    }

    ctx.Info(string.Create(
        CultureInfo.InvariantCulture,
        $"Wrote {result.Assets} picture(s) as {result.Frames} PNG(s) to {Path.GetFullPath(args[2])}, with " +
        $"{PackManifest.FileName}; {result.Repeats} repeat(s), {result.Failures.Count} failure(s)."));

    return result.Assets == 0 ? ToolExitCode.Failure : ToolExitCode.Ok;
}

// Turns a worked-on PNG tree back into an overrides folder of the game's own files.
//
// The output goes straight into <game>/overrides: archived pictures back into an archive at the archive's
// own path, loose ones at their own path, and the game finds every one of them by the path it already had.
//
// An override archive holds only what the pack replaces. It is an overlay rather than a copy of the
// game's archive with some entries swapped, so nothing falls out of the installation by being left out of
// a pack, and a half-finished upscale packs what exists and leaves the rest to the game's own copy.
static int Pack(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    PackBuild.Options options = new();
    List<string> positional = [];
    for (int i = 1; i < args.Count; i++)
    {
        switch (args[i])
        {
            case "--png":
                // The old pack: PNG frames, decoded and mipped by the game at load.
                options = options with { BlockCompress = false };
                break;

            case "--quality" when i + 1 < args.Count && Enum.TryParse(args[i + 1], true, out BlockQuality quality):
                options = options with { Quality = quality };
                i++;
                break;

            default:
                positional.Add(args[i]);
                break;
        }
    }

    if (positional.Count != 2)
    {
        return ctx.Usage(UsageText, "usage: ResExtract pack <png-dir> <overrides-dir> [--png] [--quality fast|balanced|best]");
    }

    args = ["pack", .. positional];

    string source = Path.GetFullPath(args[1]);
    string manifest = Path.Combine(source, PackManifest.FileName);

    if (!File.Exists(manifest))
    {
        return ctx.Usage(
            UsageText,
            $"'{source}' holds no {PackManifest.FileName}. A pack is built from what `ResExtract images` " +
            "wrote, so run that first and work on the PNGs in place.");
    }

    string destination = Path.GetFullPath(args[2]);
    PackBuild.Result result = PackBuild.Run(source, destination, options);

    foreach (string failure in result.Failures.Take(20))
    {
        ctx.Warn(failure);
    }

    if (result.Failures.Count > 20)
    {
        ctx.Warn($"and {result.Failures.Count - 20} more.");
    }

    ctx.Info(string.Create(
        CultureInfo.InvariantCulture,
        $"Packed {result.Assets} picture(s) into {result.Archives} archive(s) and {result.Loose} loose " +
        $"file(s), {result.Bytes / (1024.0 * 1024.0):F1} MiB in all; {result.Missing} not yet upscaled, " +
        $"{result.Failures.Count} failure(s)."));

    if (result.Assets == 0)
    {
        return ctx.Fail("Nothing was packed.");
    }

    ctx.Info(string.Empty);
    ctx.Info("Move or copy the contents of");
    ctx.Info($"  {destination}");
    ctx.Info("into <game>/overrides, beside missions and common. Nothing else to configure --");
    ctx.Info("the assets are found by the paths they already had, and `overrides` in the console");
    ctx.Info("re-reads the folder without a restart.");

    return ToolExitCode.Ok;
}

// Converts an installed overrides folder's world textures from PNG to GPU block compression, into a new
// folder. The pack keeps working either way; compressed, the game uploads its textures as they are
// stored, at a quarter to an eighth of the memory, with no decoding and no mip building at load.
static int Compress(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    BlockQuality quality = BlockQuality.Balanced;
    List<string> positional = [];
    for (int i = 1; i < args.Count; i++)
    {
        if (args[i] == "--quality" && i + 1 < args.Count && Enum.TryParse(args[i + 1], true, out BlockQuality chosen))
        {
            quality = chosen;
            i++;
            continue;
        }

        positional.Add(args[i]);
    }

    if (positional.Count != 2)
    {
        return ctx.Usage(UsageText, "usage: ResExtract compress <overrides-dir> <new-overrides-dir> [--quality fast|balanced|best]");
    }

    string source = Path.GetFullPath(positional[0]);
    string destination = Path.GetFullPath(positional[1]);
    if (!Directory.Exists(source))
    {
        return ctx.Usage(UsageText, $"'{source}' does not exist.");
    }

    if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), destination.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
    {
        return ctx.Usage(UsageText, "Write the converted pack to a new folder, then swap it in: the source is read as it is written.");
    }

    PackRecompress.Result result = PackRecompress.Run(source, destination, quality, ctx);

    foreach (string failure in result.Failures.Take(20))
    {
        ctx.Warn(failure);
    }

    ctx.Info(string.Create(
        CultureInfo.InvariantCulture,
        $"Compressed {result.Compressed} picture(s), copied {result.Copied}; "
        + $"{result.BytesBefore / 1048576.0:F0} MiB -> {result.BytesAfter / 1048576.0:F0} MiB; "
        + $"{result.Failures.Count} left as PNG after a failure."));
    return ToolExitCode.Ok;
}

// Decodes every ILSF sound found below a path and writes each one out as an ordinary WAV.
//
// The game's ".wav" files are not WAVs -- they are ILSF, and about half of them are ADPCM. This exists
// so the decoder can be LISTENED to: a byte count can be right while the samples are noise, and the
// ADPCM tables were read out of the binary rather than taken from a spec, so hearing them is the check
// that actually counts.
//
// Walks loose files and archives alike, because the streamed forms only ever exist loose on disk and
// the resident forms only ever inside sounds.res -- so covering one and not the other tests half the
// decoder.
static int Sounds(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count != 3)
    {
        return ctx.Usage(UsageText, "usage: ResExtract sounds <archive|file|dir> <outdir>");
    }

    string outputRoot = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(outputRoot);

    HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    int written = 0;
    int duplicates = 0;
    int failed = 0;
    var census = new SortedDictionary<string, int>(StringComparer.Ordinal);

    foreach ((string origin, ReadOnlyMemory<byte> data) in EnumerateSoundSources(ctx, args[1]))
    {
        string stem = Path.GetFileNameWithoutExtension(ToRelativePath(new ArchiveItem(origin, string.Empty, data)) ?? origin);
        if (stem.Length == 0 || !seen.Add(stem))
        {
            duplicates++;
            continue;
        }

        try
        {
            IlsfFile sound = IlsfFile.Read(data);
            WaveWriter.Write(
                Path.Combine(outputRoot, stem + ".wav"),
                sound.DecodePcm().Span,
                sound.SampleRate,
                sound.Channels,
                sound.BitsPerSample);

            string key = string.Create(CultureInfo.InvariantCulture, $"{sound.Format} {sound.SampleRate}Hz {sound.Channels}ch");
            census[key] = census.TryGetValue(key, out int count) ? count + 1 : 1;
            written++;
        }
        catch (Exception error) when (error is IlsfFormatException or IOException)
        {
            ctx.Warn($"'{stem}' did not decode: {error.Message}");
            failed++;
        }
    }

    foreach ((string key, int count) in census)
    {
        ctx.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{count,6}  {key}"));
    }

    ctx.Info(string.Create(
        CultureInfo.InvariantCulture,
        $"Wrote {written} sound(s) to {outputRoot}; {duplicates} repeat(s), {failed} failure(s)."));

    return failed > 0 && written == 0 ? ToolExitCode.Failure : ToolExitCode.Ok;
}

// Yields every ILSF sound below a path: loose files first, then the bodies inside every archive.
static IEnumerable<(string Origin, ReadOnlyMemory<byte> Data)> EnumerateSoundSources(ToolContext ctx, string path)
{
    if (!Directory.Exists(path))
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (IlsfFile.IsIlsf(bytes))
        {
            yield return (path, bytes);
            yield break;
        }

        foreach (ArchiveItem item in ReadArchive(path))
        {
            if (IlsfFile.IsIlsf(item.Data.Span))
            {
                yield return (item.Name, item.Data);
            }
        }

        yield break;
    }

    foreach (string file in Directory.EnumerateFiles(path, "*.wav", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
    {
        byte[] bytes = File.ReadAllBytes(file);
        if (IlsfFile.IsIlsf(bytes))
        {
            yield return (file, bytes);
        }
    }

    foreach (string archive in Directory.EnumerateFiles(path, "*.res", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
    {
        IReadOnlyList<ArchiveItem> items;
        try
        {
            items = ReadArchive(archive);
        }
        catch (Exception error) when (error is IlffFormatException or FlatResourceFormatException)
        {
            ctx.Warn($"skipping '{archive}': {error.Message}");
            continue;
        }

        foreach (ArchiveItem item in items)
        {
            if (IlsfFile.IsIlsf(item.Data.Span))
            {
                yield return (item.Name, item.Data);
            }
        }
    }
}

// Derives the surface maps this game's textures never had, for every PNG in a directory.
//
// Writes them beside the diffuse under one directory per map, so the whole set can be looked at as a
// contact sheet rather than one texture at a time -- which is how a bad derivation is actually caught.
// The ORM arrangement is the one every other tool expects, so a texture somebody wants to correct by
// hand can be taken out, edited, and put back.
static int Maps(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count is not (3 or 4))
    {
        return ctx.Usage(UsageText, "usage: ResExtract maps <diffuse-dir> <outdir> [normal-strength]");
    }

    string sourceRoot = Path.GetFullPath(args[1]);
    string outputRoot = Path.GetFullPath(args[2]);

    if (!Directory.Exists(sourceRoot))
    {
        return ctx.Usage(UsageText, $"'{sourceRoot}' is not a directory. Run 'images' first.");
    }

    float strength = 1f;

    if (args.Count == 4
        && !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out strength))
    {
        return ctx.Usage(UsageText, $"'{args[3]}' is not a number.");
    }

    MaterialMapGenerator.Options options = new(NormalStrength: strength);

    // "detail" is the one the renderer actually loads; the rest are written so a human can look at
    // them, because a packed texture is unreadable at a glance and these need judging by eye.
    string[] kinds = ["detail", "normal", "orm", "roughness", "occlusion", "height"];

    foreach (string kind in kinds)
    {
        Directory.CreateDirectory(Path.Combine(outputRoot, kind));
    }

    int written = 0;
    int failed = 0;

    foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.png").Order(StringComparer.OrdinalIgnoreCase))
    {
        string stem = Path.GetFileNameWithoutExtension(file);

        try
        {
            (Bgra32[] pixels, int width, int height) = PngReader.Read(file);
            MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, width, height, options);

            Bgra32[] packed = new Bgra32[width * height];

            MaterialMapGenerator.PackDetail(maps, packed);
            PngWriter.Write(Path.Combine(outputRoot, "detail", stem + ".png"), packed, width, height);

            MaterialMapGenerator.PackNormal(maps, packed);
            PngWriter.Write(Path.Combine(outputRoot, "normal", stem + ".png"), packed, width, height);

            MaterialMapGenerator.PackOcclusionRoughnessMetallic(maps, packed);
            PngWriter.Write(Path.Combine(outputRoot, "orm", stem + ".png"), packed, width, height);

            // The three that ORM already carries, written out again on their own. Redundant by
            // construction and worth it: a packed channel is unreadable at a glance, and these exist
            // to be looked at.
            // Halved, as the packed channel is: roughness here is a multiplier that runs past one, and
            // an image cannot show that. Mid-grey means "leave the material alone".
            float[] displayRoughness = [.. maps.Roughness.Select(value => value * 0.5f)];
            MaterialMapGenerator.PackGreyscale(displayRoughness, packed);
            PngWriter.Write(Path.Combine(outputRoot, "roughness", stem + ".png"), packed, width, height);

            MaterialMapGenerator.PackGreyscale(maps.Occlusion, packed);
            PngWriter.Write(Path.Combine(outputRoot, "occlusion", stem + ".png"), packed, width, height);

            MaterialMapGenerator.PackGreyscale(maps.Elevation, packed);
            PngWriter.Write(Path.Combine(outputRoot, "height", stem + ".png"), packed, width, height);

            written++;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException)
        {
            ctx.Warn($"'{stem}' did not derive: {error.Message}");
            failed++;
        }
    }

    ctx.Info(string.Create(
        CultureInfo.InvariantCulture,
        $"Derived maps for {written} texture(s) into {outputRoot} ({failed} failure(s))."));

    return failed > 0 && written == 0 ? ToolExitCode.Failure : ToolExitCode.Ok;
}

static IReadOnlyList<ArchiveItem> ReadArchive(string path)
{
    byte[] bytes = File.ReadAllBytes(path);

    if (IlffContainer.IsIlff(bytes))
    {
        IlffContainer container = IlffContainer.Read(bytes);
        if (container.FormType != IlffTags.Ires)
        {
            // A model or font container holds chunks rather than named resources; list those instead.
            return [.. container.Chunks.Select(chunk =>
                new ArchiveItem(chunk.Id.ToString(), chunk.Id.ToString(), chunk.Payload))];
        }

        return [.. IresArchive.FromContainer(container).Entries.Select(entry =>
            new ArchiveItem(entry.Name, entry.Kind.ToString(), entry.Data))];
    }

    if (FlatResourceArchive.IsFlatArchive(bytes))
    {
        return [.. FlatResourceArchive.Read(bytes).Entries.Select(entry =>
            new ArchiveItem(entry.Name, nameof(IresEntryKind.Body), entry.Data))];
    }

    throw new IlffFormatException($"'{path}' is neither an ILFF container nor a RESOURCE archive.");
}

static string? ToRelativePath(ArchiveItem item)
{
    string name = ResourcePath.Normalize(item.Name);

    // Drop the virtual file system namespace: "LOCAL:common/textures/a.tex" extracts to
    // "common/textures/a.tex".
    int namespaceSeparator = name.IndexOf(ResourcePath.NamespaceSeparator, StringComparison.Ordinal);
    if (namespaceSeparator >= 0)
    {
        name = name[(namespaceSeparator + 1)..];
    }

    name = name.Trim().TrimStart(ResourcePath.DirectorySeparator);
    if (name.Length == 0
        || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0
        || Array.Exists(name.Split(ResourcePath.DirectorySeparator), part => part is "." or ".."))
    {
        return null;
    }

    // Context strings are text, not files, so give them an extension a text editor will open.
    return item.Kind == nameof(IresEntryKind.ContextString) ? name + ".txt" : name;
}

/// <summary>The tool's usage text.</summary>
internal static partial class Program
{
    private const string UsageText =
        """
        OpenIGI ResExtract - inspect and unpack Project I.G.I. resource archives.

        usage:
          ResExtract list    <archive>
          ResExtract extract <archive> <outdir>
          ResExtract images  <archive|dir> <outdir>
          ResExtract pack    <png-dir> <overrides-dir> [--png] [--quality fast|balanced|best]
          ResExtract compress <overrides-dir> <new-overrides-dir> [--quality fast|balanced|best]
          ResExtract sounds  <archive|file|dir> <outdir>

        Reads ILFF archives (.res), model and font containers (.mef, .fnt) and the legacy
        "RESOURCE" flat archive. Extracted entries keep their layout below <outdir>, with the
        virtual file system namespace ("LOCAL:") stripped.

        "images" decodes every picture it finds to an 8-bit RGBA PNG, laid out the way the game's
        overrides folder is: each asset under the directory its archive sits in. That means all of
        them -- the textures inside .res archives, the loose terrain, sky, flare and shadow files,
        and the multi-frame sprites and pictures. A picture stored as a tile grid, like the 150
        tiles of the main menu, is written whole so that working on it leaves no seams.

        It also writes pack.json, which says where each picture came from and what size the game
        lays it out at. "pack" reads that back and builds an overrides folder of the game's own
        files. Work on the PNGs in place between the two.

        NOT FLATTENED, and that matters. A texture name is not unique here -- the art is layered,
        and 79 of the retail game's 1363 names are shared by files that differ -- so a flat
        directory can hold only one of them, and it is usually not the one the level draws.


        "sounds" decodes every ILSF sound it finds -- expanding the ADPCM forms -- to an ordinary
        PCM WAV. The game's own ".wav" files are ILSF, not WAV, so nothing else will open them.
        Given a directory it walks the loose files and every .res below it.
        """;
}

internal sealed record ArchiveItem(string Name, string Kind, ReadOnlyMemory<byte> Data);
