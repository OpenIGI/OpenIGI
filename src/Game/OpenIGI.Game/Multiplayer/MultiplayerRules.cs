namespace OpenIGI.Game.Multiplayer;

/// <summary>A server's rules: money, rounds, the bomb, spawning and the limits it holds clients to.</summary>
/// <remarks>
/// <para>
/// I.G.I. 2's own server settings (<c>ServerConfig_s</c>), with its defaults (<c>NetworkConfig_DefaultOptions</c>) and its
/// console names, which <c>mp-commands.txt</c> documents as <c>lo &lt;name&gt; &lt;value&gt;</c>. Both games' sessions play
/// by them; a mode that has no use for one (deathmatch has no bomb) leaves it be.
/// </para>
/// <para>
/// The defaults are <c>NetworkConfig_DefaultOptions</c>'s (<c>igi2 0x42F6F0</c>). Each was matched to its setting through the
/// retail console's own getter, which reads the one global it reports: <c>ServerConfig_GetMaxPlayers</c> reads
/// <c>0x84B730C</c> (16), <c>GetMoneyStart</c> <c>0x84B7314</c> (1000), <c>GetMoneyCap</c> <c>0x84B7318</c> (15000),
/// <c>GetMoneyMissionLost</c> <c>0x84B7334</c> (750), <c>GetBombTime</c> <c>0x84B7348</c> (90) and <c>GetTeamDamage</c>
/// <c>0x84B7378</c> (0); the rest follow the structure's order between them.
/// </para>
/// </remarks>
public sealed record MultiplayerRules
{
    /// <summary>Gets the most players at once (<c>maxplayers</c>).</summary>
    public int MaxPlayers { get; init; } = 16;

    /// <summary>Gets the money each player starts a map with (<c>moneystart</c>).</summary>
    public int MoneyStart { get; init; } = 1000;

    /// <summary>Gets the most money a player may hold (<c>moneycap</c>).</summary>
    public int MoneyCap { get; init; } = 15000;

    /// <summary>Gets the money for killing an enemy (<c>moneykill</c>).</summary>
    public int MoneyKill { get; init; } = 800;

    /// <summary>Gets the money for killing a team mate, a fine when negative (<c>moneyteamkill</c>).</summary>
    public int MoneyTeamKill { get; init; } = -4000;

    /// <summary>Gets the money for the player who resolves an objective (<c>moneyplayerobjwin</c>).</summary>
    public int MoneyObjectivePlayerWin { get; init; } = 1000;

    /// <summary>Gets the money for each player of the team that wins an objective (<c>moneyteamobjwin</c>).</summary>
    public int MoneyObjectiveTeamWin { get; init; } = 2250;

    /// <summary>Gets the money for each player of the team that loses an objective (<c>moneyteamobjlost</c>).</summary>
    public int MoneyObjectiveTeamLost { get; init; } = 1250;

    /// <summary>Gets the money for each player of the team that wins the mission (<c>moneymissionwin</c>).</summary>
    public int MoneyMissionWin { get; init; } = 1250;

    /// <summary>Gets the money for each player of the team that loses the mission (<c>moneymissionlost</c>).</summary>
    public int MoneyMissionLost { get; init; } = 750;

    /// <summary>Gets the rounds after which the map ends, or zero for no limit (<c>maprounds</c>).</summary>
    public int MapMaxRounds { get; init; }

    /// <summary>Gets the minutes after which the map ends, or zero for no limit (<c>maptime</c>).</summary>
    public int MapMaxMinutes { get; init; }

    /// <summary>Gets the team score at which the map ends, or zero for no limit (<c>mapteamscore</c>).</summary>
    public int MapMaxTeamScore { get; init; } = 3;

    /// <summary>Gets the minutes an objective may take (<c>objectivetime</c>).</summary>
    public int ObjectiveMaxMinutes { get; init; } = 15;

    /// <summary>Gets the seconds an armed bomb takes to go off (<c>bombtime</c>).</summary>
    public int BombSeconds { get; init; } = 90;

    /// <summary>Gets the seconds a bomb nobody carries lies before it goes back to the first spawn, or zero never (<c>bombrepostime</c>).</summary>
    public int BombRepositionSeconds { get; init; }

    /// <summary>Gets the money a respawn costs right after dying, for the attackers; the defenders pay 70% (<c>spawncost</c>).</summary>
    public int SpawnMaxCost { get; init; } = 1000;

    /// <summary>Gets the seconds over which the respawn cost falls to nothing (<c>spawntimer</c>).</summary>
    public int SpawnSecondsToFree { get; init; } = 20;

    /// <summary>Gets the seconds a respawned player cannot be hurt (<c>spawnsafetime</c>).</summary>
    public int SpawnSafeSeconds { get; init; } = 10;

    /// <summary>Gets the seconds before a silent client is dropped (<c>timeout</c>).</summary>
    public int TimeoutSeconds { get; init; } = 90;

    /// <summary>Gets the seconds a player who dropped is kept, to be given back as it was if it comes back under its name.</summary>
    public int RejoinSeconds { get; init; } = 180;

    /// <summary>Gets the seconds an idle player may stand before being kicked, or zero never (<c>autokick</c>).</summary>
    public int AutoKickSeconds { get; init; } = 300;

    /// <summary>Gets a value indicating whether team mates hurt each other (<c>teamdamage</c>).</summary>
    public bool TeamDamage { get; init; }

    /// <summary>Gets a value indicating whether players are put on the smaller team rather than choosing (<c>autobalance</c>).</summary>
    public bool AutoBalance { get; init; } = true;

    /// <summary>Gets a value indicating whether a map starts in warmup (<c>warmup</c>).</summary>
    public bool Warmup { get; init; }

    /// <summary>Gets a value indicating whether sniper rifles may be bought (<c>allowsniperrifles</c>).</summary>
    public bool AllowSniperRifles { get; init; } = true;

    /// <summary>Gets the highest ping a client may keep, in milliseconds (<c>pingmax</c>).</summary>
    public int PingMax { get; init; } = 400;

    /// <summary>Gets the money a respawn costs a player now, the cost falling to nothing over <see cref="SpawnSecondsToFree"/>.</summary>
    /// <param name="secondsDead">How long the player has been dead.</param>
    /// <param name="defending">Whether the player's team defends the objective, paying 70%.</param>
    /// <returns>The cost, whole money.</returns>
    public int SpawnCost(double secondsDead, bool defending)
    {
        if (SpawnSecondsToFree <= 0 || secondsDead >= SpawnSecondsToFree)
        {
            return 0;
        }

        double cost = SpawnMaxCost * (1.0 - (Math.Max(0, secondsDead) / SpawnSecondsToFree));
        return (int)(defending ? cost * 0.7 : cost);
    }
}
