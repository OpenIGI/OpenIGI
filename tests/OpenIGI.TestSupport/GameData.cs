namespace OpenIGI.TestSupport;

/// <summary>
/// Locates an original Project I.G.I. installation for tests that verify behaviour against real data.
/// </summary>
/// <remarks>
/// Set <c>IGI_GAME_PATH</c> to the folder containing <c>IGI.exe</c>, <c>common/</c> and
/// <c>missions/</c>. Tests that need it are marked with <see cref="RequiresGameDataFactAttribute"/> and
/// are skipped when it is unset — no original game file is ever committed to this repository.
/// </remarks>
public static class GameData
{
    /// <summary>The environment variable holding the path to an original installation.</summary>
    public const string EnvironmentVariable = "IGI_GAME_PATH";

    /// <summary>Gets the installation root, or <see langword="null"/> when it is unset or does not exist.</summary>
    public static string? Root
    {
        get
        {
            string? path = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? null : path;
        }
    }

    /// <summary>Gets a value indicating whether real game data is available.</summary>
    public static bool IsAvailable => Root is not null;

    /// <summary>Gets the installation root, failing when it is unavailable.</summary>
    /// <returns>The installation root.</returns>
    /// <exception cref="InvalidOperationException">No installation is configured.</exception>
    public static string RequireRoot() =>
        Root ?? throw new InvalidOperationException($"{EnvironmentVariable} is not set to an existing directory.");

    /// <summary>Resolves a path relative to the installation root.</summary>
    /// <param name="relativePath">The path relative to the root, for example <c>config.qvm</c>.</param>
    /// <returns>The absolute path, which is not checked for existence.</returns>
    public static string Resolve(string relativePath) => Path.Combine(RequireRoot(), relativePath);

    /// <summary>Enumerates every file under the installation root matching a search pattern.</summary>
    /// <param name="searchPattern">A pattern such as <c>*.res</c>.</param>
    /// <returns>The matching absolute paths, in a stable sorted order.</returns>
    public static IReadOnlyList<string> EnumerateFiles(string searchPattern)
    {
        string[] files = Directory.GetFiles(RequireRoot(), searchPattern, SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
