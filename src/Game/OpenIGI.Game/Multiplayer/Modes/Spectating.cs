using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>Who a dead player watches: a living player, and where it stands.</summary>
/// <param name="Entity">The watched player's entity.</param>
/// <param name="Name">Its name.</param>
/// <param name="Position">Where it stands, in world units.</param>
public readonly record struct SpectateTarget(int Entity, string Name, Vector3d Position);

/// <summary>
/// The dead player's view while it waits to play again: the death camera's orbit moved from its own body to a living player of
/// its side -- or, with no sides, anyone still standing -- the next of them each time it fires.
/// </summary>
/// <remarks>The scoreboard says who is alive and on which side; the caller says where each stands (the snapshots, or the
/// server's own world). Listen host and clients alike use it, in both games.</remarks>
public static class Spectating
{
    /// <summary>Picks the player a dead one watches.</summary>
    /// <param name="board">The scoreboard.</param>
    /// <param name="own">The dead player's entity.</param>
    /// <param name="teams">Whether the match is played in teams: only one's own side is watched then.</param>
    /// <param name="positionOf">Where an entity stands, or <see langword="null"/> when it is not known.</param>
    /// <param name="pick">How many times the player has asked for the next one; the choice wraps round.</param>
    /// <returns>The watched player, or <see langword="null"/> when nobody can be.</returns>
    public static SpectateTarget? Pick(Scoreboard? board, int own, bool teams, Func<int, Vector3d?> positionOf, int pick)
    {
        ArgumentNullException.ThrowIfNull(positionOf);
        if (board is null)
        {
            return null;
        }

        int ownTeam = board.Lines.Where(l => l.Entity == own).Select(static l => l.Team).DefaultIfEmpty(-1).First();
        List<SpectateTarget> standing = [];
        foreach (ScoreboardLine line in board.Lines.OrderBy(static l => l.Slot))
        {
            if (line.Entity >= 0 && line.Entity != own && line.IsAlive && (!teams || line.Team == ownTeam) && positionOf(line.Entity) is { } at)
            {
                standing.Add(new SpectateTarget(line.Entity, line.Name, at));
            }
        }

        return standing.Count == 0 ? null : standing[((pick % standing.Count) + standing.Count) % standing.Count];
    }
}
