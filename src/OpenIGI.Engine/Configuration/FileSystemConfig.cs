using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Engine.Configuration;

/// <summary>
/// The alias bindings the virtual file system starts with, optionally overridden by a
/// <c>filesys.cfg</c> file next to the game data.
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>WinMain</c> calls <c>FileSys_SetConfig("filesys.cfg")</c>, but that setter only
/// stores the name in a global (<c>0x48F0F0</c> → <c>0x5C8BCC</c>) that nothing else in the retail
/// binary reads, and no <c>filesys.cfg</c> ships with the game — the shipping build hard-codes its
/// aliases in the modules that own them. The file is therefore an <b>OpenIGI extension</b>: the syntax
/// below is ours, it is optional, and <see cref="CreateDefault"/> reproduces the retail bindings when
/// no file is present.
/// </para>
/// <para>Syntax — one binding per line, <c>#</c> or <c>;</c> starts a comment:</para>
/// <code>
/// # alias           target (semicolon-separated search list)
/// MENUSYS:        = LOCAL:menusystem/
/// MISSION:        = LOCAL:mods/mymod/missions/location0/level1/;LOCAL:missions/location0/level1/
/// </code>
/// <para>
/// A search list is exactly what the original's alias resolver supports: it tries each entry in order
/// and falls through to the next when a file is missing, which is what makes data-file mods work
/// without touching the base install.
/// </para>
/// </remarks>
public sealed class FileSystemConfig
{
    /// <summary>The file name the original passes to <c>FileSys_SetConfig</c>.</summary>
    public const string DefaultFileName = "filesys.cfg";

    private FileSystemConfig(ReadOnlyCollection<KeyValuePair<string, string>> aliases) => Aliases = aliases;

    /// <summary>Gets the alias bindings, in the order they were declared.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Aliases { get; }

    /// <summary>Creates the alias set a title binds at start-up.</summary>
    /// <param name="layout">The title whose aliases these are.</param>
    /// <returns>The default configuration for that title.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Every binding was read out of the title's own registration sites. <c>LANGUAGE:</c> and
    /// <c>MISSION:</c> are deliberately absent from both games' sets: both bind them at run time, when
    /// the language is chosen and when a mission loads.
    /// </remarks>
    public static FileSystemConfig CreateDefault(ITitleDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new(new ReadOnlyCollection<KeyValuePair<string, string>>([.. layout.StartupAliases]));
    }

    /// <summary>Parses configuration text.</summary>
    /// <param name="text">The file contents.</param>
    /// <returns>The parsed configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileSystemConfigException">A line is neither blank, a comment, nor a binding.</exception>
    public static FileSystemConfig Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<KeyValuePair<string, string>> aliases = [];
        int lineNumber = 0;

        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                throw new FileSystemConfigException($"Line {lineNumber}: expected '<ALIAS:> = <target>', got '{line}'.");
            }

            string alias = line[..separator].Trim();
            string target = line[(separator + 1)..].Trim();

            if (alias.Length < 2 || alias[^1] != ResourcePath.NamespaceSeparator)
            {
                throw new FileSystemConfigException(
                    $"Line {lineNumber}: '{alias}' is not an alias name; alias names end with '{ResourcePath.NamespaceSeparator}'.");
            }

            if (target.Length == 0)
            {
                throw new FileSystemConfigException($"Line {lineNumber}: the alias '{alias}' has no target.");
            }

            aliases.Add(new KeyValuePair<string, string>(alias, target));
        }

        return new FileSystemConfig(new ReadOnlyCollection<KeyValuePair<string, string>>(aliases));
    }

    /// <summary>Reads a configuration file, falling back to a title's defaults when it does not exist.</summary>
    /// <param name="path">The path to the configuration file.</param>
    /// <param name="layout">The title whose defaults to fall back to.</param>
    /// <returns>The parsed configuration, or the title's defaults when the file is absent.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="FileSystemConfigException">The file exists but does not parse.</exception>
    public static FileSystemConfig LoadOrDefault(string path, ITitleDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(layout);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : CreateDefault(layout);
    }

    /// <summary>Binds every alias of this configuration on a virtual file system.</summary>
    /// <param name="fileSystem">The file system to configure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    /// <exception cref="Core.Diagnostics.EngineFatalException">A target names a device that is not registered.</exception>
    public void ApplyTo(VirtualFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        foreach ((string alias, string target) in Aliases)
        {
            fileSystem.SetAlias(alias, target);
        }
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            return line[..hash];
        }

        // A semicolon separates search-list entries, so it only starts a comment at the start of a line.
        int semicolon = line.IndexOf(';', StringComparison.Ordinal);
        return semicolon >= 0 && line.AsSpan(0, semicolon).IsWhiteSpace() ? string.Empty : line;
    }
}
