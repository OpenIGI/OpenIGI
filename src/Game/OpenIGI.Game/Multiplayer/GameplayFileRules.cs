using System.Security.Cryptography;

namespace OpenIGI.Game.Multiplayer;

/// <summary>A game's <see cref="GameplayFileLayout"/> put to use: which files a map plays from, for a host to list and a player to check.</summary>
/// <remarks>
/// <para>
/// A map's own folders come from this machine's mission catalogue, never from the host: a list naming a file outside the
/// map's folders and the shared ones is no list of that map at all, whoever sent it.
/// </para>
/// </remarks>
public sealed class GameplayFileRules
{
    private readonly Func<string, IReadOnlyList<string>?> _mapFolders;

    /// <summary>Initializes a new instance of the <see cref="GameplayFileRules"/> class.</summary>
    /// <param name="layout">The game's layout.</param>
    /// <param name="mapFolders">
    /// The folders a map plays from, as a list writes them and without a closing slash, or <see langword="null"/> for a map
    /// this game does not have.
    /// </param>
    public GameplayFileRules(GameplayFileLayout layout, Func<string, IReadOnlyList<string>?> mapFolders)
    {
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _mapFolders = mapFolders ?? throw new ArgumentNullException(nameof(mapFolders));
    }

    /// <summary>Gets rules that compare nothing.</summary>
    public static GameplayFileRules None { get; } = new(GameplayFileLayout.None, static _ => null);

    /// <summary>Gets the game's layout.</summary>
    public GameplayFileLayout Layout { get; }

    /// <summary>Gets whether a path is one some level of the game plays from.</summary>
    /// <param name="path">The path under the installation, any case, either slash.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsGameplay(string path)
    {
        if (string.IsNullOrEmpty(path) || !Layout.ComparesFiles)
        {
            return false;
        }

        string p = GameplayFiles.Normalize(path);
        if (Layout.NeverExtensions.Any(e => p.EndsWith(e, StringComparison.Ordinal)))
        {
            return false;
        }

        if (Layout.SharedFiles.Contains(p, StringComparer.Ordinal))
        {
            return true;
        }

        bool placed = IsLevelFile(p) || Layout.SharedFolders.Any(f => p.StartsWith(f + "/", StringComparison.Ordinal));
        return placed && !OnlyLooks(p);
    }

    /// <summary>Gets whether a path lies among the maps' own folders, as against those every map plays from.</summary>
    /// <param name="path">The path, as a list writes it.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <remarks>A shared file is read once a game; a map's are read as the map loads.</remarks>
    public bool IsLevelFile(string path) =>
        Layout.LevelRoot.Length > 0
        && path.StartsWith(Layout.LevelRoot, StringComparison.Ordinal)
        && !Layout.SharedFiles.Contains(path, StringComparer.Ordinal)
        && !Layout.SharedFolders.Any(f => path.StartsWith(f + "/", StringComparison.Ordinal));

    /// <summary>Gets whether a list of a map may name a path: a file that map plays from, or one every map does.</summary>
    /// <param name="map">The map, as the session names it.</param>
    /// <param name="path">The path, as a list writes it.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    public bool IsListable(string map, string path) =>
        GameplayFiles.IsSafePath(path) && IsGameplay(path) && _mapFolders(map) is { } folders && BelongsTo(path, folders);

    /// <summary>Gets the list a host plays a map from.</summary>
    /// <param name="map">The map, as the session names it.</param>
    /// <param name="source">The installation as the host plays it.</param>
    /// <returns>The list; empty for a game or map whose files are not compared.</returns>
    public SessionFileList Build(string map, ISessionFileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<SessionFile> files = [];
        if (Layout.ComparesFiles && _mapFolders(map) is { } folders)
        {
            SortedSet<string> paths = new(StringComparer.Ordinal);
            foreach (string folder in Layout.SharedFolders.Concat(folders))
            {
                paths.UnionWith(source.ListFiles(folder).Select(GameplayFiles.Normalize));
            }

            paths.UnionWith(Layout.SharedFiles);
            foreach (string path in paths.Where(p => IsGameplay(p) && GameplayFiles.IsSafePath(p) && BelongsTo(p, folders)))
            {
                if (source.Read(path) is { } data && data.Length <= SessionFileList.MaxFileSize)
                {
                    files.Add(new SessionFile(path, data.Length, SHA256.HashData(data)));
                }
            }
        }

        return new SessionFileList(map, files);
    }

    // Below a folder of looks or sounds, at any depth.
    private bool OnlyLooks(string path)
    {
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (Layout.LooksFolders.Contains(parts[i], StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Only the map's own folders' files, besides the shared ones.
    private bool BelongsTo(string path, IReadOnlyList<string> folders) =>
        !IsLevelFile(path) || folders.Any(f => path.StartsWith(f + "/", StringComparison.Ordinal));
}
