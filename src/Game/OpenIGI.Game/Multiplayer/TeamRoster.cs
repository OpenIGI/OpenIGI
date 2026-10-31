namespace OpenIGI.Game.Multiplayer;

/// <summary>Which team each player is on, and which team a newcomer is put on.</summary>
/// <remarks>
/// <para>
/// I.G.I. 2's <c>TeamManager</c> (teammanager.c), with its two teams, <see cref="Good"/> and <see cref="Bad"/>.
/// <c>TeamManager_GetFairTeam</c> counts each team's players and puts a newcomer on the smaller one; with the teams level, it
/// gives the team the player asked for, or, asked for none, one of the two from its own random stream.
/// </para>
/// <para>
/// The stream is <c>TeamManager_tRandomState</c>'s three words, stepped as the original steps them: the first shifted right
/// seven and mixed with the third makes the step, which the first gains mixed with the second, the third takes, and the
/// second is mixed with; the team is the first's low bit.
/// </para>
/// </remarks>
public sealed class TeamRoster
{
    /// <summary>The first team, <c>TEAM_GOOD</c>.</summary>
    public const int Good = 0;

    /// <summary>The second team, <c>TEAM_BAD</c>.</summary>
    public const int Bad = 1;

    private readonly Dictionary<int, int> _teams = [];
    private uint _random0;
    private uint _random1;
    private uint _random2;

    /// <summary>Initializes a new instance of the <see cref="TeamRoster"/> class.</summary>
    /// <param name="seed">Seeds the stream a balanced choice is drawn from.</param>
    public TeamRoster(uint seed = 0x2545F491u)
    {
        _random0 = seed | 1u;
        _random1 = seed * 0x9E3779B1u;
        _random2 = ~seed;
    }

    /// <summary>Gets each player's team, by its slot.</summary>
    public IReadOnlyDictionary<int, int> Teams => _teams;

    /// <summary>Gets how many players a team has.</summary>
    /// <param name="team">The team.</param>
    /// <returns>Its players.</returns>
    public int CountOf(int team) => _teams.Values.Count(t => t == team);

    /// <summary>Gets the team a newcomer should join, <c>TeamManager_GetFairTeam</c>.</summary>
    /// <param name="requested">The team it asked for, or -1 for none.</param>
    /// <returns>The team.</returns>
    public int FairTeam(int requested = -1)
    {
        int good = CountOf(Good);
        int bad = CountOf(Bad);
        if (bad < good)
        {
            return Bad;
        }

        if (bad > good)
        {
            return Good;
        }

        if (requested != -1)
        {
            return requested;
        }

        uint step = (_random0 >> 7) ^ _random2;
        _random0 += step ^ _random1;
        _random2 = step;
        _random1 ^= step;
        return (int)(_random0 & 1);
    }

    /// <summary>Puts a player on a team.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="team">The team.</param>
    public void Assign(int slot, int team) => _teams[slot] = team;

    /// <summary>Takes a player off its team, as it leaves.</summary>
    /// <param name="slot">The player's slot.</param>
    public void Remove(int slot) => _teams.Remove(slot);

    /// <summary>Gets a player's team.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns>The team, or -1 when it has none.</returns>
    public int TeamOf(int slot) => _teams.TryGetValue(slot, out int team) ? team : -1;
}
