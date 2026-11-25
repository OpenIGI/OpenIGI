namespace OpenIGI.Core.Titles;

/// <summary>Whether a game title can be played by this port.</summary>
public enum GameAvailability
{
    /// <summary>The title is supported and its data is present.</summary>
    Available,

    /// <summary>The title is known to the shell but cannot be played — not yet supported, or not installed.</summary>
    Unavailable,
}

/// <summary>
/// The static identity of a game this shell can present: its name, the file that proves an installation,
/// and the data directories it reads from.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="GameTitle"/> is the game in the abstract; a <see cref="GameProfile"/> binds it to a
/// concrete installation on disk. The shell's game selector lists the titles and greys out the ones the
/// player cannot take — a title this build does not support, or one it supports and cannot find on
/// disk. Both games are supported now, so what greys an entry out is the installation being absent.
/// </para>
/// <para>
/// <see cref="SignatureFile"/> is a file whose presence in a directory is taken as proof that the
/// directory is that game's data root. For I.G.I. it is <c>config.qvm</c>, which every retail install
/// ships and which the port reads but never overwrites.
/// </para>
/// </remarks>
/// <param name="Id">A stable machine identifier, e.g. <c>igi1</c>.</param>
/// <param name="DisplayName">The name shown to the player.</param>
/// <param name="Support">Whether the port supports the title at all, before checking any disk.</param>
/// <param name="SignatureFile">A file, relative to the data root, whose presence proves an installation.</param>
/// <param name="AssetRoots">The data subdirectories the title reads from, relative to the data root.</param>
/// <param name="GamePathVariable">The environment variable that names the installation, ahead of any search.</param>
public sealed record GameTitle(
    string Id,
    string DisplayName,
    GameAvailability Support,
    string SignatureFile,
    IReadOnlyList<string> AssetRoots,
    string GamePathVariable)
{
    /// <summary>Gets the name without its subtitle: <c>I.G.I. 2: Covert Strike</c> is <c>I.G.I. 2</c>.</summary>
    public string ShortName => ShortNameOf(DisplayName);

    /// <summary>A title's name without its subtitle: <c>I.G.I. 2: Covert Strike</c> is <c>I.G.I. 2</c>.</summary>
    /// <param name="displayName">The name, as shown.</param>
    /// <returns>The name before its colon, or all of it.</returns>
    public static string ShortNameOf(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        int colon = displayName.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? displayName : displayName[..colon].Trim();
    }

    /// <summary>Gets Project I.G.I. (2000) — the active, supported title.</summary>
    public static GameTitle Igi1 { get; } = new(
        "igi1",
        "Project I.G.I.",
        GameAvailability.Available,
        "config.qvm",
        ["missions", "common", "language", "menusystem"],
        "IGI_GAME_PATH")
    {
        MissionCount = 14,
        MissionNames =
        [
            "Trainyard", "SAM Base", "Military Airbase", "GOD", "Radar Base", "Get Priboi", "Border Crossing",
            "Re-Supply", "Missile Trainyard", "Defend Priboi", "Eagle's Nest I", "Eagle's Nest II", "Nuclear Infiltration", "Finding the Bomb",
        ],
    };

    /// <summary>Gets I.G.I. 2: Covert Strike (2003).</summary>
    /// <remarks>
    /// <para>
    /// <b>Supported means "this build can load and render it", not "it is finished".</b> The sequel's
    /// levels load, its world, ground, sky, forest and water draw, and the shell can switch to it while
    /// the game is running; it has no player, weapons or AI yet, so a level opens in the free camera.
    /// Listing it as unavailable once the port could draw it would have been the shell telling the
    /// player something untrue about their own installation.
    /// </para>
    /// <para>
    /// Support is still a claim about the port rather than about the disk. Whether it can actually be
    /// played from here is <see cref="GameProfile.Availability"/>, which needs this AND the signature
    /// file to be present.
    /// </para>
    /// <para>
    /// The data root is the installation's <c>pc</c> directory, not the directory above it: a retail
    /// install puts <c>igi2.exe</c> in <c>pc</c> alongside <c>config.qvm</c>, <c>missions</c>,
    /// <c>common</c>, <c>language</c> and <c>menusystem</c>. So the asset roots are those content
    /// directories, exactly as they are for <see cref="Igi1"/>, and <c>pc</c> is not one of them — it is
    /// the root they hang off. Checked against a retail 1.1 installation.
    /// </para>
    /// </remarks>
    public static GameTitle Igi2 { get; } = new(
        "igi2",
        "I.G.I. 2: Covert Strike",
        GameAvailability.Available,
        "igi2.exe",
        ["missions", "common", "language", "menusystem"],
        "IGI2_GAME_PATH")
    {
        MissionCount = 19,

        // Its missions' lighting under the remastered presets is still being tuned; the menu shows them as coming soon.
        GraphicsPresetHeld = Configuration.Presets.GraphicsPreset.Vanilla,

        // Its runs are not yet proven as Project I.G.I.'s are, played again to the end by the boards' checker: none is recorded
        // or ranked, and the boards show it as coming soon.
        RunsHeld = true,
    };

    /// <summary>Gets the one graphics preset the title offers for now, or <see langword="null"/> when it offers every one.</summary>
    /// <remarks>
    /// Held while the title is played (<see cref="Configuration.Presets.GraphicsPresetRegistry.Hold"/>): a title whose look
    /// under the remastered presets is not ready is drawn as the original, and the player's own preset comes back with a title
    /// that offers it.
    /// </remarks>
    public Configuration.Presets.GraphicsPreset? GraphicsPresetHeld { get; init; }

    /// <summary>
    /// Gets a value indicating whether the title's single-player runs are held back for now: not recorded, watched, raced or
    /// ranked, and its mission boards shown as coming soon.
    /// </summary>
    public bool RunsHeld { get; init; }

    /// <summary>Gets whether a title's single-player runs are offered: recorded, watched, raced and ranked.</summary>
    /// <param name="titleId">The title.</param>
    /// <returns>Whether they are; a title not known holds none back.</returns>
    public static bool RunsOffered(string titleId) => All.FirstOrDefault(t => t.Id == titleId) is not { RunsHeld: true };

    /// <summary>Gets the first title whose runs are offered: what a board asked for a held one shows instead.</summary>
    /// <param name="titleId">The title asked for.</param>
    /// <returns>Its id, or the first offered title's where its runs are held.</returns>
    public static string RunsOfferedFor(string titleId) =>
        RunsOffered(titleId) ? titleId : All.First(static t => !t.RunsHeld).Id;

    /// <summary>Gets how many missions the title's campaign has, each a map a server can play.</summary>
    public int MissionCount { get; init; }

    /// <summary>Gets the campaign's missions' names as the menus show them, by mission id from 1; empty where not known.</summary>
    public IReadOnlyList<string> MissionNames { get; init; } = [];

    /// <summary>Gets a mission's name (Trainyard) by its id, which a server lists its map by.</summary>
    /// <param name="mission">The mission's id, from 1.</param>
    /// <returns>Its name, or <see langword="null"/> where not known.</returns>
    public string? MissionName(int mission) => mission >= 1 && mission <= MissionNames.Count ? MissionNames[mission - 1] : null;

    /// <summary>Gets how many ranks a mission's score may earn at each difficulty.</summary>
    public const int RanksPerDifficulty = 3;

    /// <summary>
    /// Gets the ranks a mission's score earns, as the menus name them in English (I.G.I. 2's <c>language/english/menusystem.res</c>,
    /// <c>Rank1</c> to <c>Rank9</c>): <see cref="RanksPerDifficulty"/> a difficulty from Easy, lowest first. Both games' rank screens
    /// are I.G.I. 2's, and name them so.
    /// </summary>
    public static IReadOnlyList<string> RankNames { get; } =
    [
        "Desk Officer", "Operative", "Agent",
        "Field Officer", "Covert Operative", "Secret Agent",
        "IGI Recruit", "IGI Operative", "David Jones",
    ];

    /// <summary>Gets a rank's English name.</summary>
    /// <param name="rank">The rank at its difficulty, from nought, lowest first.</param>
    /// <param name="difficulty">The difficulty, nought (Easy) to two (Hard).</param>
    /// <returns>The name, or <see langword="null"/> for no rank.</returns>
    public static string? RankName(int rank, int difficulty) =>
        rank is >= 0 and < RanksPerDifficulty && difficulty is >= 0 and <= 2 ? RankNames[(difficulty * RanksPerDifficulty) + rank] : null;

    /// <summary>Gets every title the shell knows about, active first.</summary>
    public static IReadOnlyList<GameTitle> All { get; } = [Igi1, Igi2];
}
