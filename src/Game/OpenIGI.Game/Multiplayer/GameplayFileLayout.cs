namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// Which of a game's files decide how it plays -- the scripts, the shapes, the ground, the AI's graphs, the weapons -- and so
/// must be a host's own on every player's machine, as against those that only change how it looks or sounds.
/// </summary>
/// <param name="SharedFolders">Folders every level plays from, whole but for what only looks or sounds.</param>
/// <param name="SharedFiles">Files every level plays from.</param>
/// <param name="LevelRoot">
/// What every map's own folders begin with (<c>missions/location0/level</c>, <c>missions/</c>): a file below it belongs to the
/// maps whose folders hold it, and to no other.
/// </param>
/// <param name="LooksFolders">Folders, at any depth, whose files only change how a level looks or sounds.</param>
/// <param name="NeverExtensions">Files never a level's to play from, wherever they lie: pictures, notes, sounds, programs.</param>
/// <remarks>
/// Each game describes its own installation; the rules that use the description are the same for every game
/// (<see cref="GameplayFileRules"/>). Paths are as a list writes them: lower case, forward slashes, no leading slash.
/// </remarks>
public sealed record GameplayFileLayout(
    IReadOnlyList<string> SharedFolders,
    IReadOnlyList<string> SharedFiles,
    string LevelRoot,
    IReadOnlyList<string> LooksFolders,
    IReadOnlyList<string> NeverExtensions)
{
    /// <summary>Gets the layout of a game whose files are not compared: nothing is a level's to play from.</summary>
    public static GameplayFileLayout None { get; } = new([], [], string.Empty, [], []);

    /// <summary>Gets whether this layout names any file at all.</summary>
    public bool ComparesFiles => LevelRoot.Length > 0 || SharedFolders.Count > 0 || SharedFiles.Count > 0;
}
