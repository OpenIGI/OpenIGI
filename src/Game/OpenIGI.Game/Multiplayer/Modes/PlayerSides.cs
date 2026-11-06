namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// What one player is shown of the others by the side each is on: a team mate's name over its head and a fallen team mate's
/// revive mark in co-op; in a team game only the team mates' names, so an enemy is never given away through a wall; in a free
/// for all nobody's.
/// </summary>
public static class PlayerSides
{
    /// <summary>Gets whether a player is shown another's name over its head.</summary>
    /// <param name="mode">The mode's id.</param>
    /// <param name="ownTeam">The looking player's team, or <see langword="null"/> when it isn't known.</param>
    /// <param name="theirTeam">The other's team, or <see langword="null"/> when it isn't known.</param>
    /// <returns><see langword="true"/> for a team mate.</returns>
    public static bool ShowsName(string? mode, int? ownTeam, int? theirTeam) =>
        !ArenaLevel.IsVersus(mode) || (mode == GameModes.TeamDeathmatch && ownTeam is { } own && theirTeam == own);

    /// <summary>Gets whether a fallen player can be brought back by a team mate, and so is marked where it lies.</summary>
    /// <param name="mode">The mode's id.</param>
    /// <returns><see langword="true"/> in co-op.</returns>
    public static bool Revivable(string? mode) => !ArenaLevel.IsVersus(mode);
}
