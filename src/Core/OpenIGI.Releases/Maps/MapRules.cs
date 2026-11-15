namespace OpenIGI.Releases.Maps;

/// <summary>
/// What a community map may be, checked by the editor before it publishes and by the master server again as it takes one
/// (<c>tools/master-server/src/maps.js</c> holds the same rules): data only, inside its mission's folder, within size, and
/// none of the game's own files carried -- a file of the game it uses is named and the player's game supplies it.
/// </summary>
public static class MapRules
{
    /// <summary>The games a map may be for.</summary>
    public static readonly IReadOnlyList<string> Games = ["igi1", "igi2"];

    /// <summary>The kinds of file a map may carry: the game's own data, never anything that runs.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.Ordinal)
    {
        ".qvm", ".qsc", ".dat", ".res", ".tex", ".bit", ".cmd", ".ctr", ".hmp", ".lmp", ".mtp", ".olm", ".wav", ".mef", ".iff",
        ".spr", ".pic", ".tga", ".png", ".jpg", ".dds", ".json", ".txt", ".ogg", ".fnt", ".mtl", ".ilff", ".grp",
    };

    /// <summary>The most a map may carry itself, in bytes.</summary>
    public const long MaxEmbeddedBytes = 96L * 1024 * 1024;

    /// <summary>The most files a map may have.</summary>
    public const int MaxFiles = 6000;

    /// <summary>The longest manifest a package may have, in bytes.</summary>
    public const int MaxManifestBytes = 4 * 1024 * 1024;

    /// <summary>The longest a map's name may be.</summary>
    public const int MaxTitle = 64;

    /// <summary>The longest a map's summary may be.</summary>
    public const int MaxSummary = 500;

    /// <summary>The longest an author's name may be.</summary>
    public const int MaxAuthor = 40;

    /// <summary>Checks a map's manifest.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="isGameFile">Whether a hash is of one of the game's own files, which a map may name but not carry; null to not ask.</param>
    /// <returns>What is wrong with it, a line each; empty when nothing is.</returns>
    public static IReadOnlyList<string> Check(MapManifest manifest, Func<string, bool>? isGameFile = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        List<string> problems = [];
        if (manifest.Schema != MapManifest.CurrentSchema)
        {
            problems.Add($"Format {manifest.Schema} is not one this reads ({MapManifest.CurrentSchema}).");
        }

        if (!ContentManifest.IsValidId(manifest.Id))
        {
            problems.Add("The map's id is lowercase letters, digits and inner hyphens, at most 64.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Title) || manifest.Title.Length > MaxTitle)
        {
            problems.Add($"The map needs a name of at most {MaxTitle} characters.");
        }

        if ((manifest.Summary?.Length ?? 0) > MaxSummary)
        {
            problems.Add($"The summary is at most {MaxSummary} characters.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Author) || manifest.Author.Length > MaxAuthor)
        {
            problems.Add($"The map needs an author's name of at most {MaxAuthor} characters.");
        }

        if (!Games.Contains(manifest.Game, StringComparer.Ordinal))
        {
            problems.Add($"The map is for a game this does not know: {manifest.Game}.");
        }

        if (manifest.RemixOf is { } remix && !ContentManifest.IsValidId(remix))
        {
            problems.Add("The map it remixes is named by an id.");
        }

        string folder = manifest.MissionDirectory?.TrimEnd('/') ?? string.Empty;
        if (!IsSafePath(folder) || !folder.StartsWith("missions/", StringComparison.Ordinal))
        {
            problems.Add("The mission's folder is a folder under missions/.");
        }

        IReadOnlyList<MapFile> files = manifest.Files ?? [];
        if (files.Count == 0 || files.Count > MaxFiles)
        {
            problems.Add($"A map has between one and {MaxFiles} files.");
        }

        if (manifest.EmbeddedSize > MaxEmbeddedBytes)
        {
            problems.Add($"A map carries at most {MaxEmbeddedBytes / (1024 * 1024)} MB of its own.");
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (MapFile file in files)
        {
            if (!IsSafePath(file.Path) || !file.Path.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                problems.Add($"{file.Path}: every file is inside the mission's folder.");
            }
            else if (!seen.Add(file.Path))
            {
                problems.Add($"{file.Path}: named twice.");
            }

            if (file.Size < 0 || file.Sha256 is not { Length: 64 } || !file.Sha256.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                problems.Add($"{file.Path}: its size or hash is not one.");
            }

            if (file.Origin is { } origin)
            {
                if (!IsSafePath(origin))
                {
                    problems.Add($"{file.Path}: the game file it is, {origin}, is not a path in the game.");
                }

                if (isGameFile is not null && !isGameFile(file.Sha256))
                {
                    problems.Add($"{file.Path}: named as the game's own {origin}, which it is not; the map has to carry it.");
                }
            }
            else
            {
                if (!Extensions.Contains(Path.GetExtension(file.Path).ToLowerInvariant()))
                {
                    problems.Add($"{file.Path}: a map carries the game's kinds of data only.");
                }

                if (isGameFile is not null && isGameFile(file.Sha256))
                {
                    problems.Add($"{file.Path}: one of the game's own files, which a map names rather than carries.");
                }
            }
        }

        if (!files.Any(f => f.Path.Equals($"{folder}/mission.qvm", StringComparison.Ordinal) && f.Origin is null))
        {
            problems.Add("The map carries its own mission.qvm, which defines it.");
        }

        return problems;
    }

    /// <summary>Whether a path is relative, forward-slashed and stays where it is put.</summary>
    /// <param name="path">The path.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsSafePath(string? path) =>
        path is { Length: > 0 and <= 240 }
        && !path.StartsWith('/')
        && !path.Contains('\\', StringComparison.Ordinal)
        && !path.Contains(':', StringComparison.Ordinal)
        && path.Split('/').All(static part => part.Length > 0 && part != "." && part != "..")
        && path.All(static c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '/' or '.' or '_' or '-' or ' ');
}

/// <summary>A published map as the master server lists it.</summary>
/// <param name="Id">The map's id.</param>
/// <param name="Version">Its newest version, counted by the server from one.</param>
/// <param name="Title">Its name.</param>
/// <param name="Summary">What it is.</param>
/// <param name="Game">The game it is for.</param>
/// <param name="Author">Who made it.</param>
/// <param name="Published">When its newest version was published.</param>
/// <param name="Size">Its package's size, what downloading it costs.</param>
/// <param name="Sha256">Its package's SHA-256, lowercase hex.</param>
/// <param name="Downloads">How many times it has been downloaded.</param>
/// <param name="Rating">Its average rating, one to five stars, or nought before anyone has rated it.</param>
/// <param name="Ratings">How many have rated it.</param>
/// <param name="RemixOf">The map it was made from, or <see langword="null"/>.</param>
public sealed record MapListing(
    string Id,
    int Version,
    string Title,
    string Summary,
    string Game,
    string Author,
    DateTimeOffset Published,
    long Size,
    string Sha256,
    int Downloads,
    double Rating,
    int Ratings,
    string? RemixOf = null);
