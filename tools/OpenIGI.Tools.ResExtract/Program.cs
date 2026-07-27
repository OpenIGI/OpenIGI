using System.Globalization;
using OpenIGI.Core;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Legacy;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintUsage();
        return args.Length == 0 ? ExitCode.Usage : ExitCode.Ok;
    }

    try
    {
        return args[0].ToUpperInvariant() switch
        {
            "LIST" => List(args),
            "EXTRACT" => Extract(args),
            _ => Fail($"Unknown command '{args[0]}'."),
        };
    }
    catch (Exception error) when (error is IlffFormatException or FlatResourceFormatException
        or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"error: {error.Message}");
        return ExitCode.Failure;
    }
}

static int List(string[] args)
{
    if (args.Length != 2)
    {
        return Fail("usage: ResExtract list <archive>");
    }

    IReadOnlyList<ArchiveItem> items = ReadArchive(args[1]);
    long total = 0;

    foreach (ArchiveItem item in items)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{item.Kind,-13} {item.Data.Length,10}  {item.Name}"));
        total += item.Data.Length;
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{items.Count} entries, {total} bytes."));
    return ExitCode.Ok;
}

static int Extract(string[] args)
{
    if (args.Length != 3)
    {
        return Fail("usage: ResExtract extract <archive> <outdir>");
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
            Console.Error.WriteLine($"warning: skipping '{item.Name}': its name does not map to a file below the output directory.");
            skipped++;
            continue;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, item.Data.ToArray());
        written++;
    }

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"Wrote {written} file(s) to {outputRoot}{(skipped > 0 ? $", skipped {skipped}" : string.Empty)}."));
    return ExitCode.Ok;
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

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    PrintUsage();
    return ExitCode.Usage;
}

static void PrintUsage() =>
    Console.WriteLine(
        """
        OpenIGI ResExtract - inspect and unpack Project I.G.I. resource archives.

        usage:
          ResExtract list    <archive>
          ResExtract extract <archive> <outdir>

        Reads ILFF archives (.res), model and font containers (.mef, .fnt) and the legacy
        "RESOURCE" flat archive. Extracted entries keep their layout below <outdir>, with the
        virtual file system namespace ("LOCAL:") stripped.
        """);

internal sealed record ArchiveItem(string Name, string Kind, ReadOnlyMemory<byte> Data);

internal static class ExitCode
{
    public const int Ok = 0;
    public const int Usage = 1;
    public const int Failure = 2;
}
