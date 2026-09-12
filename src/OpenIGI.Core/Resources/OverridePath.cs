namespace OpenIGI.Core.Resources;

/// <summary>
/// Where a replacement for one of the game's assets has to be put.
/// </summary>
/// <remarks>
/// <para>
/// The overrides folder mirrors the installation, so the path an asset lives at is the path that
/// replaces it. This builds that path — and it lives down here, rather than beside the loader that uses
/// it, because two programs have to agree on it: the game asks for a replacement, and the extractor lays
/// the replacements out for a tool to work on.
/// </para>
/// <para>
/// Two implementations of a naming rule agree until the day one of them is edited, and the failure then
/// is silent: a folder full of files that replaces nothing.
/// </para>
/// </remarks>
public static class OverridePath
{
    /// <summary>Builds the path an archived entry's replacement would live at.</summary>
    /// <param name="archiveName">The archive's resource name, as the registry keys it.</param>
    /// <param name="entryName">The entry's resource name.</param>
    /// <returns>The relative path, or an empty string when one cannot be formed.</returns>
    /// <remarks>
    /// <b>The archive's directory, and the entry's file name.</b> An entry's own path is relative to
    /// whatever base the archive was authored against — a level's entries are named <c>textures/x.tex</c>
    /// while the archive sits in <c>missions/.../level10/textures/</c>, and the shared archive's entries
    /// are named <c>common/textures/x.tex</c> and sit in <c>common/textures/</c>. Neither base is
    /// recoverable in general, and neither needs to be: names are unique within one archive, so the
    /// archive's own directory plus the file name is both unambiguous and exactly where a person would
    /// expect to put the replacement.
    /// </remarks>
    public static string For(string? archiveName, string entryName)
    {
        if (string.IsNullOrEmpty(archiveName) || string.IsNullOrEmpty(entryName))
        {
            return string.Empty;
        }

        string archive = StripNamespace(archiveName);
        string entry = StripNamespace(entryName);

        int slash = archive.LastIndexOf(ResourcePath.DirectorySeparator);
        string directory = slash < 0 ? string.Empty : archive[..slash];

        int entrySlash = entry.LastIndexOf(ResourcePath.DirectorySeparator);
        string file = entrySlash < 0 ? entry : entry[(entrySlash + 1)..];

        if (file.Length == 0)
        {
            return string.Empty;
        }

        return directory.Length == 0 ? file : $"{directory}{ResourcePath.DirectorySeparator}{file}";
    }

    /// <summary>Builds the path a loose file's replacement would live at.</summary>
    /// <param name="resolvedPath">The device path the virtual file system resolved to.</param>
    /// <returns>The relative path, or an empty string when one cannot be formed.</returns>
    /// <remarks>
    /// A file that was never in an archive — a terrain atlas, a menu script, a language table — is
    /// already addressed by where it is, so replacing it needs nothing but its own path.
    /// </remarks>
    public static string For(string resolvedPath) =>
        string.IsNullOrEmpty(resolvedPath) ? string.Empty : StripNamespace(resolvedPath);

    private static string StripNamespace(string name)
    {
        string path = ResourcePath.Normalize(name);

        int separator = path.IndexOf(ResourcePath.NamespaceSeparator, StringComparison.Ordinal);
        if (separator >= 0)
        {
            path = path[(separator + 1)..];
        }

        return path.TrimStart(ResourcePath.DirectorySeparator);
    }
}
