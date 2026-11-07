namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>A team match's sides as the team menu shows them: who plays on each, and which this player is on.</summary>
/// <param name="OwnTeam">This player's side, or -1 before it has one.</param>
/// <param name="Igi">The names on the I.G.I. side.</param>
/// <param name="Conspiracy">The names on the Conspiracy side.</param>
public sealed record TeamPick(int OwnTeam, IReadOnlyList<string> Igi, IReadOnlyList<string> Conspiracy)
{
    /// <summary>The menu's choices, by their number on it less one: I.G.I., the Conspiracy, and the fairer side.</summary>
    public static IReadOnlyList<string> Choices { get; } = ["igi", "conspiracy", "auto"];

    /// <summary>Gets the names on a side.</summary>
    /// <param name="team">The side.</param>
    /// <returns>Its players' names.</returns>
    public IReadOnlyList<string> On(int team) => team == TeamRoster.Bad ? Conspiracy : Igi;

    /// <summary>Gets whether this player may go over to a side: the teams no more than one apart once it has.</summary>
    /// <param name="team">The side.</param>
    /// <returns><see langword="true"/> when it is open to this player (its own side always is).</returns>
    /// <remarks>The server rules on it (<c>ServerSession.ChooseTeam</c>); this is what the menu greys out ahead of asking.</remarks>
    public bool Open(int team)
    {
        if (team == OwnTeam)
        {
            return true;
        }

        int other = team == TeamRoster.Bad ? TeamRoster.Good : TeamRoster.Bad;
        int joining = On(team).Count;
        int leaving = On(other).Count - (OwnTeam == other ? 1 : 0);
        return joining <= leaving;
    }

    /// <summary>Gets the sides of a scoreboard, as this player sees them.</summary>
    /// <param name="lines">The scoreboard's players.</param>
    /// <param name="ownTeam">This player's side, or -1.</param>
    /// <returns>The pick.</returns>
    public static TeamPick Of(IReadOnlyList<ScoreboardLine> lines, int ownTeam)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new TeamPick(
            ownTeam,
            [.. lines.Where(l => l.Team == TeamRoster.Good).Select(l => l.Name)],
            [.. lines.Where(l => l.Team == TeamRoster.Bad).Select(l => l.Name)]);
    }
}
