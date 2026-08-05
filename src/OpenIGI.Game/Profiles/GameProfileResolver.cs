namespace OpenIGI.Game.Profiles;

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
    /// <summary>Finds the first candidate directory that actually holds an installation.</summary>
    /// <param name="candidates">Directories to test, in order of preference.</param>
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
    public static string? DiscoverDataRoot(IEnumerable<string?> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (new GameProfile(GameTitle.Igi1, candidate).IsInstalled)
            {
                return candidate;
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

    /// <summary>Resolves the active title (I.G.I. 1) as a profile bound to the chosen data root.</summary>
    /// <param name="explicitPath">The path from a command-line flag, or <see langword="null"/>.</param>
    /// <param name="environmentPath">The path from the environment, or <see langword="null"/>.</param>
    /// <param name="fallbackPath">The default path, or <see langword="null"/>.</param>
    /// <returns>The active game profile.</returns>
    public static GameProfile ResolveActive(string? explicitPath, string? environmentPath, string? fallbackPath) =>
        new(GameTitle.Igi1, ResolvePath(explicitPath, environmentPath, fallbackPath));

    /// <summary>Builds the full catalogue of titles as profiles, the active one resolved to a data root.</summary>
    /// <param name="explicitPath">The path from a command-line flag, or <see langword="null"/>.</param>
    /// <param name="environmentPath">The path from the environment, or <see langword="null"/>.</param>
    /// <param name="fallbackPath">The default path, or <see langword="null"/>.</param>
    /// <returns>One profile per <see cref="GameTitle.All"/> entry; the game selector lists these.</returns>
    /// <remarks>
    /// The active title is bound to the resolved path; the others are bound to an empty data root, so they
    /// report <see cref="GameAvailability.Unavailable"/> and appear greyed out until the port supports
    /// them and they are pointed at their own installation.
    /// </remarks>
    public static IReadOnlyList<GameProfile> BuildCatalogue(string? explicitPath, string? environmentPath, string? fallbackPath)
    {
        string activePath = ResolvePath(explicitPath, environmentPath, fallbackPath);
        var catalogue = new List<GameProfile>(GameTitle.All.Count);
        foreach (GameTitle title in GameTitle.All)
        {
            catalogue.Add(new GameProfile(title, title == GameTitle.Igi1 ? activePath : string.Empty));
        }

        return catalogue;
    }
}
