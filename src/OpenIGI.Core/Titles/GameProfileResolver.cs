namespace OpenIGI.Core.Titles;

/// <summary>
/// Resolves which installation the port should run against, in one place, so game-path resolution flows
/// through a <see cref="GameProfile"/> rather than a bare string threaded through the composition root.
/// </summary>
/// <remarks>
/// The precedence is the established one: an explicit path (the <c>-game</c> flag) wins, then the
/// <c>IGI_GAME_PATH</c> environment variable, then a fallback the caller supplies — which
/// <see cref="DiscoverDataRoot"/> produces by looking for an installation around the executable. The
/// precedence logic is pure and testable; whether the resolved path actually holds the data is
/// <see cref="GameProfile.IsInstalled"/>, kept separate so a caller can resolve a profile and then
/// report a clear "not installed" rather than the resolver silently failing.
/// </remarks>
public static class GameProfileResolver
{
    /// <summary>Finds the first candidate directory that actually holds an installation of a title.</summary>
    /// <param name="candidates">Directories to test, in order of preference.</param>
    /// <param name="title">The title to test for.</param>
    /// <returns>The first candidate that is an installation, or <see langword="null"/> when none is.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// So that dropping the port into the game's own folder is enough to run it, with nothing to
    /// configure — the way an add-on for a game of this age is expected to be installed. The caller
    /// supplies the places worth looking, which for the desktop app is the directory the executable is
    /// in and the working directory.
    /// </para>
    /// <para>
    /// A candidate counts only when <see cref="GameTitle.SignatureFile"/> is in it, not merely when the
    /// directory exists. Testing for the file rather than the directory is what stops the working
    /// directory being accepted just because it is somewhere.
    /// </para>
    /// </remarks>
    public static string? DiscoverDataRoot(IEnumerable<string?> candidates, GameTitle title)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(title);

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (new GameProfile(title, candidate).IsInstalled)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Finds the first candidate directory holding an installation of any known title.</summary>
    /// <param name="candidates">Directories to test, in order of preference.</param>
    /// <param name="titles">The titles to test for, in order of preference.</param>
    /// <returns>A profile bound to the first match, or <see langword="null"/> when none matches.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Candidate-major rather than title-major: a directory is tested against every title before the
    /// next directory is tried, so the folder the port was dropped into wins over a better-matching
    /// title somewhere further down the list. That is the behaviour the drop-it-in-and-run install
    /// depends on, and it is the reason this is not just <see cref="DiscoverDataRoot"/> in a loop.
    /// </remarks>
    public static GameProfile? Discover(IEnumerable<string?> candidates, IReadOnlyList<GameTitle> titles)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(titles);

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            foreach (GameTitle title in titles)
            {
                GameProfile profile = new(title, candidate);
                if (profile.IsInstalled)
                {
                    return profile;
                }
            }
        }

        return null;
    }

    /// <summary>Chooses a data-root path by precedence: explicit, then environment, then fallback.</summary>
    /// <param name="explicitPath">The path from a command-line flag, or <see langword="null"/>.</param>
    /// <param name="environmentPath">The path from the environment, or <see langword="null"/>.</param>
    /// <param name="fallbackPath">The default path, or <see langword="null"/>.</param>
    /// <returns>The first non-blank path, or the empty string when all are blank.</returns>
    public static string ResolvePath(string? explicitPath, string? environmentPath, string? fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return environmentPath;
        }

        return string.IsNullOrWhiteSpace(fallbackPath) ? string.Empty : fallbackPath;
    }

    /// <summary>Resolves a title as a profile bound to the chosen data root.</summary>
    /// <param name="title">The title to run.</param>
    /// <param name="explicitPath">The path from a command-line flag, or <see langword="null"/>.</param>
    /// <param name="environmentPath">The path from the environment, or <see langword="null"/>.</param>
    /// <param name="fallbackPath">The default path, or <see langword="null"/>.</param>
    /// <returns>The active game profile.</returns>
    public static GameProfile ResolveActive(
        GameTitle title,
        string? explicitPath,
        string? environmentPath,
        string? fallbackPath) =>
        new(title, ResolvePath(explicitPath, environmentPath, fallbackPath));

    /// <summary>Builds the full catalogue of titles as profiles, each resolved to its own data root.</summary>
    /// <param name="active">The title the resolved path belongs to.</param>
    /// <param name="explicitPath">The path from a command-line flag, or <see langword="null"/>.</param>
    /// <param name="environmentPath">The path from the environment, or <see langword="null"/>.</param>
    /// <param name="fallbackPath">The default path, or <see langword="null"/>.</param>
    /// <param name="pathFor">
    /// Where a title other than the active one is installed, or <see langword="null"/> to bind them to
    /// nothing. A machine may well have both games, and only the caller knows where each is.
    /// </param>
    /// <returns>One profile per <see cref="GameTitle.All"/> entry; the game selector lists these.</returns>
    /// <remarks>
    /// <para>
    /// The active title takes the resolved path, because that is the one the command line and the
    /// working directory can speak about. Every other title is asked for by <paramref name="pathFor"/>,
    /// so a selector can show which of them is actually installed instead of showing them all as
    /// missing — which is what it did when the others were bound to an empty root.
    /// </para>
    /// <para>
    /// A title with no path still appears, greyed out. Listing a game the player owns as missing is a
    /// worse answer than listing one they do not own as unavailable.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<GameProfile> BuildCatalogue(
        GameTitle active,
        string? explicitPath,
        string? environmentPath,
        string? fallbackPath,
        Func<GameTitle, string?>? pathFor = null)
    {
        ArgumentNullException.ThrowIfNull(active);

        string activePath = ResolvePath(explicitPath, environmentPath, fallbackPath);
        var catalogue = new List<GameProfile>(GameTitle.All.Count);
        foreach (GameTitle title in GameTitle.All)
        {
            string path = title == active
                ? activePath
                : pathFor?.Invoke(title) ?? string.Empty;

            catalogue.Add(new GameProfile(title, path));
        }

        return catalogue;
    }
}
