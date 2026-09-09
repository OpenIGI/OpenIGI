namespace OpenIGI.Game.Profiles;

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
/// concrete installation on disk. The shell's game selector lists the titles and greys out the ones that
/// are <see cref="GameAvailability.Unavailable"/> — <see cref="Igi2"/> is registered so the selector can
/// show it as a known but not-yet-playable entry, exactly as the R11 specification asks.
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
public sealed record GameTitle(
    string Id,
    string DisplayName,
    GameAvailability Support,
    string SignatureFile,
    IReadOnlyList<string> AssetRoots)
{
    /// <summary>Gets Project I.G.I. (2000) — the active, supported title.</summary>
    public static GameTitle Igi1 { get; } = new(
        "igi1",
        "Project I.G.I.",
        GameAvailability.Available,
        "config.qvm",
        ["missions", "common", "language", "menusystem"]);

    /// <summary>Gets I.G.I. 2: Covert Strike (2003) — known to the shell but not yet supported.</summary>
    /// <remarks>
    /// The data root is the installation's <c>pc</c> directory, not the directory above it: a retail
    /// install puts <c>igi2.exe</c> in <c>pc</c> alongside <c>config.qvm</c>, <c>missions</c>,
    /// <c>common</c>, <c>language</c> and <c>menusystem</c>. So the asset roots are those content
    /// directories, exactly as they are for <see cref="Igi1"/>, and <c>pc</c> is not one of them — it is
    /// the root they hang off. Checked against a retail 1.1 installation.
    /// </remarks>
    public static GameTitle Igi2 { get; } = new(
        "igi2",
        "I.G.I. 2: Covert Strike",
        GameAvailability.Unavailable,
        "igi2.exe",
        ["missions", "common", "language", "menusystem"]);

    /// <summary>Gets every title the shell knows about, active first.</summary>
    public static IReadOnlyList<GameTitle> All { get; } = [Igi1, Igi2];
}
