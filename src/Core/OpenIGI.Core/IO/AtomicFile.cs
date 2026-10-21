namespace OpenIGI.Core.IO;

/// <summary>Files written whole or not at all, for documents another process may read at any moment.</summary>
public static class AtomicFile
{
    /// <summary>Writes a text file beside its target and moves it over the target, creating the target's folder.</summary>
    /// <param name="path">The file.</param>
    /// <param name="text">Its text.</param>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <remarks>A reader starting at the same moment sees the old file or the new one, never half of either.</remarks>
    public static void WriteAllText(string path, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(text);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }
}
