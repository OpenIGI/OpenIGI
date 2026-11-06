using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>Who a dead player watches: a living player, and where it stands.</summary>
/// <param name="Entity">The watched player's entity.</param>
/// <param name="Name">Its name.</param>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Killer">Whether it is the player's killer, framed for a moment after the death (the kill cam).</param>
/// <param name="Metres">How far it stood from the player who died, for the kill cam's card.</param>
public readonly record struct SpectateTarget(int Entity, string Name, Vector3d Position, bool Killer = false, float Metres = 0f);

/// <summary>
/// The dead player's view while it waits to play again: the death camera's orbit moved from its own body to a living player of
/// its side -- or, with no sides, anyone still standing -- the next of them each time it fires.
/// </summary>
/// <remarks>The scoreboard says who is alive and on which side; the caller says where each stands (the snapshots, or the
/// server's own world). Listen host and clients alike use it, in both games.</remarks>
public static class Spectating
{
    /// <summary>How long the kill cam frames the killer before the dead player watches its side, in seconds.</summary>
    public const double KillCamSeconds = 3.5;

    /// <summary>Picks the player a dead one watches: its killer for a moment after the death, then the living of its side.</summary>
    /// <param name="board">The scoreboard.</param>
    /// <param name="own">The dead player's entity.</param>
    /// <param name="teams">Whether the match is played in teams.</param>
    /// <param name="positionOf">Where an entity stands, or <see langword="null"/> when it is not known.</param>
    /// <param name="pick">How many times the player has asked for the next one: any skips the kill cam.</param>
    /// <param name="killedBy">Who killed it and how long ago, in seconds, or <see langword="null"/> when nobody did.</param>
    /// <param name="fellAt">Where the dead player fell, for the killer's distance.</param>
    /// <returns>The watched player, or <see langword="null"/> when nobody can be.</returns>
    public static SpectateTarget? Pick(
        Scoreboard? board, int own, bool teams, Func<int, Vector3d?> positionOf, int pick, (int Entity, double Age)? killedBy, Vector3d? fellAt)
    {
        ArgumentNullException.ThrowIfNull(positionOf);
        if (pick == 0 && killedBy is { } killer && killer.Entity != own && killer.Age < KillCamSeconds
            && board is not null && positionOf(killer.Entity) is { } at)
        {
            string name = board.Lines.Where(l => l.Entity == killer.Entity).Select(static l => l.Name).FirstOrDefault() ?? "?";
            float metres = fellAt is { } fell ? (float)(Vector3d.Distance(fell, at) / WorldUnits.PerMetre) : 0f;
            return new SpectateTarget(killer.Entity, name, at, Killer: true, metres);
        }

        return Pick(board, own, teams, positionOf, Math.Max(0, pick - 1));
    }

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
