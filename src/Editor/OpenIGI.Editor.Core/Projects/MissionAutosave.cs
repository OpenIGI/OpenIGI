using System.Globalization;

namespace OpenIGI.Editor.Core.Projects;

/// <summary>
/// Copies of a mission's objects taken while it has changes not yet saved, kept apart from the project so the game never
/// plays them, the last few a mission, to recover what was done before a crash or a close without saving.
/// </summary>
/// <param name="directory">The folder the games' folders are in.</param>
/// <param name="game">The game (<c>igi1</c>, <c>igi2</c>).</param>
public sealed class MissionAutosave(string directory, string game)
{
    /// <summary>How many copies a mission keeps; the oldest goes when another is taken.</summary>
    public const int Kept = 5;

    private const string Prefix = "objects-";
    private const string Extension = ".qvm";
    private const string Stamp = "yyyyMMdd-HHmmss-fff";

    /// <summary>Takes a copy of a mission's objects.</summary>
    /// <param name="mission">The mission, by the game path of its objects.</param>
    /// <param name="image">The objects as the game reads them.</param>
    /// <param name="now">When, in UTC: the copy's name, so copies sort by it.</param>
    /// <returns>The file written.</returns>
    public string Write(string mission, ReadOnlySpan<byte> image, DateTime now)
    {
        string folder = Folder(mission);
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, Prefix + now.ToString(Stamp, CultureInfo.InvariantCulture) + Extension);
        File.WriteAllBytes(file, image.ToArray());
        foreach (string old in Copies(mission).Skip(Kept))
        {
            File.Delete(old);
        }

        return file;
    }

    /// <summary>A mission's copies, the newest first.</summary>
    /// <param name="mission">The mission, by the game path of its objects.</param>
    /// <returns>The files.</returns>
    public IReadOnlyList<string> Copies(string mission)
    {
        string folder = Folder(mission);
        return Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, Prefix + "*" + Extension).OrderDescending(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The newest copy taken after the mission was last saved, if there is one: work that was never saved.</summary>
    /// <param name="mission">The mission, by the game path of its objects.</param>
    /// <param name="savedUtc">When the mission was last saved, in UTC; <see cref="DateTime.MinValue"/> when never.</param>
    /// <returns>The file and when it was taken; null when every copy is older than the save.</returns>
    public (string File, DateTime TakenUtc)? Unsaved(string mission, DateTime savedUtc)
    {
        foreach (string file in Copies(mission))
        {
            string name = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateTime.TryParseExact(name, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime taken))
            {
                return taken > savedUtc ? (file, taken) : null;
            }
        }

        return null;
    }

    /// <summary>Throws a mission's copies away, as saving it makes them old.</summary>
    /// <param name="mission">The mission, by the game path of its objects.</param>
    public void Clear(string mission)
    {
        foreach (string file in Copies(mission))
        {
            File.Delete(file);
        }
    }

    // A folder a mission, named for its objects' game path with what a file name cannot hold made a dash.
    private string Folder(string mission)
    {
        char[] bad = Path.GetInvalidFileNameChars();
        string safe = new([.. mission.Select(c => bad.Contains(c) || c is '/' or '\\' ? '-' : c)]);
        return Path.Combine(directory, game, safe);
    }
}
