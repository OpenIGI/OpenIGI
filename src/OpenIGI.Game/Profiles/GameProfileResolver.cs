namespace OpenIGI.Game.Profiles;

/// <summary>
/// Resolves which installation the port should run against, in one place, so game-path resolution flows
/// through a <see cref="GameProfile"/> rather than a bare string threaded through the composition root.
/// </summary>
/// <remarks>
/// The precedence is the established one: an explicit path (the <c>-game</c> flag) wins, then the
/// <c>IGI_GAME_PATH</c> environment variable, then a fallback the caller supplies (the historical
/// <c>S:\I'm Going In\pc</c> default). The precedence logic is pure and testable; whether the resolved
/// path actually holds the data is <see cref="GameProfile.IsInstalled"/>, kept separate so a caller can
/// resolve a profile and then report a clear "not installed" rather than the resolver silently failing.
/// </remarks>
public static class GameProfileResolver
{
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
