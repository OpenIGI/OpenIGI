using OpenIGI.Core.Titles;

namespace OpenIGI.TestSupport;

/// <summary>One title's installation, located from its own environment variable.</summary>
/// <remarks>
/// <para>
/// Golden tests read real game data, and there are two games now. Each has its own variable so a
/// checkout can hold one, both or neither: <c>IGI_GAME_PATH</c> for Project I.G.I. and
/// <c>IGI2_GAME_PATH</c> for I.G.I. 2.
/// </para>
/// <para>
/// <see cref="GameData"/> stays as the unqualified name for I.G.I. 1 and delegates here, so the
/// hundreds of existing call sites keep meaning exactly what they always did. A second title must not
/// cost a rewrite of every test that reads the first.
/// </para>
/// </remarks>
public sealed class GameDataSource
{
    private GameDataSource(string environmentVariable, GameTitle title)
    {
        EnvironmentVariable = environmentVariable;
        Title = title;
    }

    /// <summary>Gets the retail Project I.G.I. installation, from <c>IGI_GAME_PATH</c>.</summary>
    /// <remarks>The folder holding <c>IGI.exe</c>, <c>common/</c> and <c>missions/</c>.</remarks>
    public static GameDataSource Igi1 { get; } = new("IGI_GAME_PATH", GameTitle.Igi1);

    /// <summary>Gets the retail I.G.I. 2 installation, from <c>IGI2_GAME_PATH</c>.</summary>
    /// <remarks>
    /// The <c>pc</c> directory, not the folder above it: that is where <c>igi2.exe</c>, <c>config.qvm</c>
    /// and the content directories live.
    /// </remarks>
    public static GameDataSource Igi2 { get; } = new("IGI2_GAME_PATH", GameTitle.Igi2);

    /// <summary>Gets every source, so a test can be written once and run per title.</summary>
    public static IReadOnlyList<GameDataSource> All { get; } = [Igi1, Igi2];

    /// <summary>Gets the environment variable holding the path.</summary>
    public string EnvironmentVariable { get; }

    /// <summary>Gets the title this source is an installation of.</summary>
    public GameTitle Title { get; }

    /// <summary>Gets the installation root, or <see langword="null"/> when unset or absent.</summary>
    public string? Root
    {
        get
        {
            string? path = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? null : path;
        }
    }

    /// <summary>Gets a value indicating whether this title's data is available.</summary>
    public bool IsAvailable => Root is not null;

    /// <summary>Finds the source for a title id.</summary>
    /// <param name="titleId">The title's stable id, for example <c>igi2</c>.</param>
    /// <returns>The matching source.</returns>
    /// <exception cref="ArgumentException">No source is registered for that id.</exception>
    public static GameDataSource For(string titleId)
    {
        foreach (GameDataSource source in All)
        {
            if (string.Equals(source.Title.Id, titleId, StringComparison.Ordinal))
            {
                return source;
            }
        }

        throw new ArgumentException($"No game data source is registered for the title '{titleId}'.", nameof(titleId));
    }

    /// <summary>Gets the installation root, failing when it is unavailable.</summary>
    /// <returns>The installation root.</returns>
    /// <exception cref="InvalidOperationException">No installation is configured.</exception>
    public string RequireRoot() =>
        Root ?? throw new InvalidOperationException($"{EnvironmentVariable} is not set to an existing directory.");

    /// <summary>Resolves a path relative to the installation root.</summary>
    /// <param name="relativePath">The path relative to the root.</param>
    /// <returns>The absolute path, which is not checked for existence.</returns>
    public string Resolve(string relativePath) => Path.Combine(RequireRoot(), relativePath);

    /// <summary>Enumerates every file under the installation root matching a search pattern.</summary>
    /// <param name="searchPattern">A pattern such as <c>*.res</c>.</param>
    /// <returns>The matching absolute paths, in a stable sorted order.</returns>
    public IReadOnlyList<string> EnumerateFiles(string searchPattern)
    {
        string[] files = Directory.GetFiles(RequireRoot(), searchPattern, SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
