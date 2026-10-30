namespace OpenIGI.Game.Multiplayer;

/// <summary>One player's standing in a match.</summary>
public sealed class MatchPlayer(int slot, string name, int team, int money)
{
    /// <summary>Gets the player's slot.</summary>
    public int Slot { get; } = slot;

    /// <summary>Gets the player's name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets or sets the look the player wears, a <see cref="PlayerSkins"/> id.</summary>
    public string Skin { get; set; } = string.Empty;

    /// <summary>Gets or sets the player's team.</summary>
    public int Team { get; set; } = team;

    /// <summary>Gets or sets the player's money.</summary>
    public int Money { get; set; } = money;

    /// <summary>Gets or sets the enemies the player has killed.</summary>
    public int Kills { get; set; }

    /// <summary>Gets or sets the times the player has died.</summary>
    public int Deaths { get; set; }

    /// <summary>Gets or sets the objectives the player has resolved.</summary>
    public int Objectives { get; set; }

    /// <summary>Gets or sets a value indicating whether the player is alive and in the level.</summary>
    public bool IsAlive { get; set; }

    /// <summary>Gets or sets the match second the player last died, or null while alive.</summary>
    public double? DiedAt { get; set; }
}

/// <summary>A multiplayer match's rules applied: money, kills, objectives, rounds, and when the map is over.</summary>
/// <remarks>
/// <para>
/// I.G.I. 2's session bookkeeping, apart from any level: <c>Netshop</c>'s accounts (start money, the cap, the awards for a kill,
/// a team kill, an objective and a mission), <c>TeamManager</c>'s scores, and <c>NetManager</c>'s end of a map by rounds, time
/// or team score. A title tells it what happened in its level (a kill, an objective resolved) and asks it what follows (whether
/// a player can afford to spawn, whether the map is over); both games' sessions keep score through it.
/// </para>
/// <para>
/// Money is clamped to <see cref="MultiplayerRules.MoneyCap"/> on the way up and allowed to go negative, as a team kill's fine
/// can take it, on the way down.
/// </para>
/// </remarks>
public sealed class Match
{
    private readonly Dictionary<int, MatchPlayer> _players = [];
    private readonly int[] _teamScores = new int[2];

    /// <summary>Initializes a new instance of the <see cref="Match"/> class.</summary>
    /// <param name="rules">The server's rules.</param>
    public Match(MultiplayerRules rules) => Rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>Gets the server's rules.</summary>
    public MultiplayerRules Rules { get; }

    /// <summary>Gets the players, by slot.</summary>
    public IReadOnlyDictionary<int, MatchPlayer> Players => _players;

    /// <summary>Gets the rounds played on this map.</summary>
    public int Round { get; private set; }

    /// <summary>Gets the team that defends the current objective, which pays less to respawn, or -1.</summary>
    public int DefendingTeam { get; set; } = -1;

    /// <summary>Gets a team's score.</summary>
    /// <param name="team">The team.</param>
    /// <returns>Its score.</returns>
    public int ScoreOf(int team) => (uint)team < 2 ? _teamScores[team] : 0;

    /// <summary>Takes in a player who has joined, with the start money.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="name">The player's name.</param>
    /// <param name="team">The player's team.</param>
    /// <returns>The player's standing.</returns>
    public MatchPlayer Join(int slot, string name, int team)
    {
        MatchPlayer player = new(slot, name, team, Math.Min(Rules.MoneyStart, Rules.MoneyCap)) { IsAlive = true };
        _players[slot] = player;
        return player;
    }

    /// <summary>Takes out a player who has left.</summary>
    /// <param name="slot">The player's slot.</param>
    public void Leave(int slot) => _players.Remove(slot);

    /// <summary>Records a death and pays its killer: the kill award for an enemy, the team-kill award (a fine) for a team mate.</summary>
    /// <param name="victim">The slot of the player killed.</param>
    /// <param name="killer">The slot of the killer, or -1 when nobody killed it (a fall, the world).</param>
    /// <param name="now">The match second it happened.</param>
    public void Kill(int victim, int killer, double now)
    {
        if (!_players.TryGetValue(victim, out MatchPlayer? dead))
        {
            return;
        }

        dead.Deaths++;
        dead.IsAlive = false;
        dead.DiedAt = now;
        if (killer == victim || !_players.TryGetValue(killer, out MatchPlayer? shooter))
        {
            return;
        }

        if (shooter.Team == dead.Team)
        {
            Pay(shooter, Rules.MoneyTeamKill);
            return;
        }

        shooter.Kills++;
        Pay(shooter, Rules.MoneyKill);
    }

    /// <summary>Gets what a respawn costs a dead player now, the cost falling to nothing with time.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="now">The match second.</param>
    /// <returns>The cost, or zero for a player who is not dead.</returns>
    public int SpawnCost(int slot, double now) =>
        _players.TryGetValue(slot, out MatchPlayer? player) && player.DiedAt is { } died
            ? Rules.SpawnCost(now - died, player.Team == DefendingTeam)
            : 0;

    /// <summary>Respawns a dead player who can pay for it, taking the cost.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="now">The match second.</param>
    /// <returns><see langword="true"/> when the player was dead, could pay, and is now alive.</returns>
    public bool TrySpawn(int slot, double now)
    {
        if (!_players.TryGetValue(slot, out MatchPlayer? player) || player.IsAlive)
        {
            return false;
        }

        int cost = SpawnCost(slot, now);
        if (player.Money < cost)
        {
            return false;
        }

        player.Money -= cost;
        player.IsAlive = true;
        player.DiedAt = null;
        return true;
    }

    /// <summary>Records an objective resolved: its resolver and team paid as winners, the other team as losers.</summary>
    /// <remarks>The team's point comes with the mission, not the objective (<c>TeamManager_AddScore</c> in <c>NetMission_RunHandler</c>).</remarks>
    /// <param name="winningTeam">The team that won it.</param>
    /// <param name="resolver">The slot of the player who resolved it, or -1.</param>
    public void ObjectiveWon(int winningTeam, int resolver)
    {
        foreach (MatchPlayer player in _players.Values)
        {
            Pay(player, player.Team == winningTeam ? Rules.MoneyObjectiveTeamWin : Rules.MoneyObjectiveTeamLost);
        }

        if (_players.TryGetValue(resolver, out MatchPlayer? hero))
        {
            hero.Objectives++;
            Pay(hero, Rules.MoneyObjectivePlayerWin);
        }
    }

    /// <summary>Records the end of a round: every player paid as a winner or a loser of the mission, and the winners' point.</summary>
    /// <param name="winningTeam">The team that won the round's mission.</param>
    public void RoundOver(int winningTeam)
    {
        Round++;
        if ((uint)winningTeam < 2)
        {
            _teamScores[winningTeam]++;
        }

        foreach (MatchPlayer player in _players.Values)
        {
            Pay(player, player.Team == winningTeam ? Rules.MoneyMissionWin : Rules.MoneyMissionLost);
        }
    }

    /// <summary>Gets whether the map is over: its rounds played, its time up, or a team at the score limit.</summary>
    /// <param name="minutes">The minutes the map has been played.</param>
    /// <returns><see langword="true"/> when any limit set has been reached.</returns>
    public bool IsMapOver(double minutes) =>
        (Rules.MapMaxRounds > 0 && Round >= Rules.MapMaxRounds)
        || (Rules.MapMaxMinutes > 0 && minutes >= Rules.MapMaxMinutes)
        || (Rules.MapMaxTeamScore > 0 && (_teamScores[0] >= Rules.MapMaxTeamScore || _teamScores[1] >= Rules.MapMaxTeamScore));

    /// <summary>Starts a new map: scores and rounds cleared, every player back to the start money and alive.</summary>
    public void NewMap()
    {
        Round = 0;
        Array.Clear(_teamScores);
        foreach (MatchPlayer player in _players.Values)
        {
            player.Money = Math.Min(Rules.MoneyStart, Rules.MoneyCap);
            player.Kills = 0;
            player.Deaths = 0;
            player.Objectives = 0;
            player.IsAlive = true;
            player.DiedAt = null;
        }
    }

    private void Pay(MatchPlayer player, int amount) =>
        player.Money = amount >= 0 ? Math.Min(player.Money + amount, Math.Max(Rules.MoneyCap, player.Money)) : player.Money + amount;
}
