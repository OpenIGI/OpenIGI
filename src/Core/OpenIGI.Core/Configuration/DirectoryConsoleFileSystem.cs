namespace OpenIGI.Core.Configuration;

/// <summary>
/// Reads console scripts from a directory on disk.
/// </summary>
/// <remarks>
/// Paths are resolved inside <see cref="Root"/> and anything that escapes it is refused, so a config
/// file cannot reach outside the game's own configuration directory.
/// </remarks>
/// <param name="root">The directory to read from.</param>
public sealed class DirectoryConsoleFileSystem(string root) : IConsoleFileSystem
{
    /// <summary>Gets the directory scripts are read from.</summary>
    public string Root { get; } = Path.GetFullPath(root);

    /// <inheritdoc/>
    public bool TryReadText(string name, out string text)
    {
        text = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(Root, name));
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!candidate.StartsWith(Root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            return false;
        }

        try
        {
            text = File.ReadAllText(candidate);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
