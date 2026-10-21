namespace OpenIGI.Core;

/// <summary>Normalization rules shared by everything that names a game resource.</summary>
/// <remarks>
/// The original engine normalizes backslashes to forward slashes when it creates a resource entry
/// (<c>Res_CreateEntry</c>, <c>0x4B59F0</c>), so <c>LOCAL:menusystem\mainmenu.qsc</c> and
/// <c>LOCAL:menusystem/mainmenu.qsc</c> name the same resource. Archives, the virtual file system and
/// mod overrides must all apply the same rule or lookups silently miss.
/// </remarks>
public static class ResourcePath
{
    /// <summary>The separator resource names use once normalized.</summary>
    public const char DirectorySeparator = '/';

    /// <summary>The separator that appears in the original's scripts and archives and is normalized away.</summary>
    public const char AlternateDirectorySeparator = '\\';

    /// <summary>The character that terminates a virtual file system namespace prefix, as in <c>LOCAL:</c>.</summary>
    public const char NamespaceSeparator = ':';

    /// <summary>Converts a resource name to its canonical form.</summary>
    /// <param name="name">The name to normalize.</param>
    /// <returns>The name with every backslash replaced by a forward slash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Replace(AlternateDirectorySeparator, DirectorySeparator);
    }

    /// <summary>Normalises a directory name: forward slashes, and no separator at the end.</summary>
    /// <param name="directory">The directory, in either separator.</param>
    /// <returns>The directory, which the mission catalogues of both titles key their paths by.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/>.</exception>
    public static string NormalizeDirectory(string directory) => Normalize(directory).TrimEnd(DirectorySeparator);
}
