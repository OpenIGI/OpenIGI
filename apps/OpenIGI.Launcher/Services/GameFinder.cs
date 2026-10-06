using System.Text.RegularExpressions;
using Microsoft.Win32;
using OpenIGI.Core.Titles;

namespace OpenIGI.Launcher.Services;

/// <summary>
/// Looks for a player's own copy of a game everywhere it is likely to be: the folders <see cref="GameStarter"/>
/// knows, every Steam library, and GOG's installs.
/// </summary>
/// <remarks>
/// <para>
/// Slower than <see cref="GameStarter.Locate"/>, which the launcher asks on every refresh: this one lists folders,
/// so it runs once, off the frame, when the first-run guide asks where the game is.
/// </para>
/// <para>
/// A store's folder is not taken on trust. Every folder found is a candidate only, and counts when the game's
/// signature file is in it or in its <c>pc</c> folder, the same proof the game itself asks for.
/// </para>
/// </remarks>
internal static partial class GameFinder
{
    /// <summary>Finds every installation of a title on this machine.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The installation folders, each once, in the order found.</returns>
    public static IReadOnlyList<string> Find(GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        string? variable = Environment.GetEnvironmentVariable(title == GameTitle.Igi2 ? "IGI2_GAME_PATH" : "IGI_GAME_PATH");

        IEnumerable<string?> candidates = GameStarter.Candidates(title)
            .Prepend(variable)
            .Concat(StoreFolders().SelectMany(ChildrenOf));

        List<string> found = [];
        foreach (string? candidate in candidates)
        {
            if (Installation(title, candidate) is { } folder
                && !found.Contains(folder, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                found.Add(folder);
            }
        }

        return found;
    }

    /// <summary>Reads the library folders out of Steam's <c>libraryfolders.vdf</c>.</summary>
    /// <param name="vdf">The file's text.</param>
    /// <returns>Each library's folder.</returns>
    public static IReadOnlyList<string> SteamLibraries(string vdf)
    {
        ArgumentNullException.ThrowIfNull(vdf);
        return [.. LibraryPath().Matches(vdf).Select(static match => match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal))];
    }

    // The folder itself, or its pc folder, whichever holds the signature file.
    private static string? Installation(GameTitle title, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        string trimmed = folder.Trim().Trim('"');
        return GameStarter.IsInstallation(title, trimmed) ? trimmed
            : GameStarter.IsInstallation(title, Path.Combine(trimmed, "pc")) ? Path.Combine(trimmed, "pc")
            : null;
    }

    // Folders whose children are each one game: Steam's common folders, GOG's install folders.
    private static IEnumerable<string> StoreFolders()
    {
        foreach (string steam in SteamRoots())
        {
            string file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            IReadOnlyList<string> libraries = [steam];
            try
            {
                if (File.Exists(file))
                {
                    libraries = [steam, .. SteamLibraries(File.ReadAllText(file))];
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The default library is still worth a look.
            }

            foreach (string library in libraries)
            {
                yield return Path.Combine(library, "steamapps", "common");
            }
        }

        foreach (string gog in GogRoots())
        {
            yield return gog;
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            if (RegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath") is { } path)
            {
                yield return path.Replace('/', '\\');
            }

            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
        else
        {
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        }
    }

    // GOG names each game's folder in the registry; GOG Galaxy and the offline installers also default to these.
    private static IEnumerable<string> GogRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        yield return @"C:\GOG Games";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games");

        foreach (string folder in GogRegistryFolders())
        {
            // The game's own folder; its parent is given so that ChildrenOf finds it among its siblings.
            if (Path.GetDirectoryName(folder.TrimEnd('\\')) is { } parent)
            {
                yield return parent;
            }
        }
    }

    private static List<string> GogRegistryFolders()
    {
        List<string> folders = [];
        if (!OperatingSystem.IsWindows())
        {
            return folders;
        }

        try
        {
            using RegistryKey? games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            foreach (string id in games?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? game = games!.OpenSubKey(id);
                if (game?.GetValue("path") is string path && path.Length > 0)
                {
                    folders.Add(path);
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // No GOG, or no right to read what it wrote: there is nothing to find there.
        }

        return folders;
    }

    private static string? RegistryValue(RegistryKey hive, string key, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using RegistryKey? opened = hive.OpenSubKey(key);
            return opened?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ChildrenOf(string folder)
    {
        string[] children;
        try
        {
            children = Directory.Exists(folder) ? Directory.GetDirectories(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            children = [];
        }

        return children;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPath();
}
