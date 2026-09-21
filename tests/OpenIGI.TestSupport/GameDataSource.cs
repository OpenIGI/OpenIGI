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

    /// <summary>The folder a replacement pack is dropped into, which is not part of the installation.</summary>
    /// <remarks>
    /// It lives INSIDE the game directory by design -- a pack is the game folder with the files somebody
    /// changed and nothing else -- so a recursive scan finds it unless it is told not to. See
    /// <c>ResourceOverrides.DirectoryName</c>, whose value this mirrors; the test support cannot reference
    /// the engine.
    /// </remarks>
    private const string OverridesDirectory = "overrides";

    /// <summary>Enumerates the installation's own files matching a search pattern.</summary>
    /// <param name="searchPattern">A pattern such as <c>*.res</c>.</param>
    /// <returns>The matching absolute paths, in a stable sorted order.</returns>
    /// <remarks>
    /// <b>The overrides folder is excluded, and that is the point of this method.</b> Every caller is
    /// asserting something about what the game SHIPPED -- that its textures round-trip, that its archives
    /// rebuild byte for byte -- and a replacement somebody dropped in is not that. It is not even the same
    /// format: a pack picture is version 12, which the retail readers rightly refuse.
    /// <para>
    /// Left in, the whole suite failed for anyone who had installed a pack, which the port itself writes
    /// and encourages. The failure looked like a texture bug and was a scanning one.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> EnumerateFiles(string searchPattern)
    {
        string root = RequireRoot();
        string overrides = Path.Combine(root, OverridesDirectory) + Path.DirectorySeparatorChar;

        string[] files = [.. Directory
            .GetFiles(root, searchPattern, SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(overrides, StringComparison.OrdinalIgnoreCase))];
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }
}
