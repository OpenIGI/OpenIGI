namespace OpenIGI.Core.Titles;

/// <summary>
/// A <see cref="GameTitle"/> bound to a concrete installation on disk: its data root, whether it is
/// actually available there, its asset roots, and — once scanned — its missions.
/// </summary>
/// <remarks>
/// <para>
/// This is what game-path resolution now flows through. Rather than the composition root passing a bare
/// path string around, it resolves a profile (see <see cref="GameProfileResolver"/>) and everything that
/// needs the data root asks the profile. That is what makes a second title tractable later: the shell
/// holds a list of profiles, one per title, and the active one decides where data is read from.
/// </para>
/// <para>
/// It lives in <c>OpenIGI.Core</c> so that everything from the engine upwards can ask which title is
/// running without depending on gameplay. It carried a scanned mission registry until this was
/// written, which made a Core-shaped identity type depend on <c>OpenIGI.Game</c> for a property
/// nothing read; the session that scans the missions owns them instead.
/// </para>
/// <para>
/// <see cref="Availability"/> combines two things: whether the port supports the title at all
/// (<see cref="GameTitle.Support"/>) and whether its data is actually present at <see cref="DataRoot"/>.
/// I.G.I. 2 is unavailable on the first count regardless of disk; a mistyped I.G.I. 1 path is unavailable
/// on the second.
/// </para>
/// </remarks>
public sealed class GameProfile
{
    /// <summary>Initializes a new instance of the <see cref="GameProfile"/> class.</summary>
    /// <param name="title">The title this profile is an installation of.</param>
    /// <param name="dataRoot">The directory the game's data lives in; may be empty when unresolved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is <see langword="null"/>.</exception>
    public GameProfile(GameTitle title, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
        DataRoot = dataRoot ?? string.Empty;
    }

    /// <summary>Gets the title this profile is an installation of.</summary>
    public GameTitle Title { get; }

    /// <summary>Gets the directory the game's data lives in.</summary>
    public string DataRoot { get; }

    /// <summary>Gets the title's stable identifier.</summary>
    public string Id => Title.Id;

    /// <summary>Gets the name shown to the player.</summary>
    public string DisplayName => Title.DisplayName;

    /// <summary>Gets a value indicating whether the data is present at <see cref="DataRoot"/>.</summary>
    /// <remarks>
    /// True when the data root exists and holds the title's signature file. This touches the disk, so it
    /// reflects the installation as it is now — a drive unplugged between launches shows as not installed.
    /// </remarks>
    public bool IsInstalled =>
        !string.IsNullOrWhiteSpace(DataRoot)
        && Directory.Exists(DataRoot)
        && File.Exists(Path.Combine(DataRoot, Title.SignatureFile));

    /// <summary>Gets whether the title can be played from this profile.</summary>
    public GameAvailability Availability =>
        Title.Support == GameAvailability.Available && IsInstalled
            ? GameAvailability.Available
            : GameAvailability.Unavailable;

    /// <summary>Gets a value indicating whether the title can be played from this profile.</summary>
    public bool IsPlayable => Availability == GameAvailability.Available;

    /// <summary>Enumerates the absolute paths of the title's asset roots under <see cref="DataRoot"/>.</summary>
    /// <returns>One path per <see cref="GameTitle.AssetRoots"/> entry.</returns>
    public IEnumerable<string> AssetRootPaths()
    {
        foreach (string root in Title.AssetRoots)
        {
            yield return Path.Combine(DataRoot, root);
        }
    }
}
