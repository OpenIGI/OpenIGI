using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Multiplayer;

/// <summary>The settings of a multiplayer session's own read-outs.</summary>
[CVarContainer]
public static class NetCVars
{
    /// <summary>Whether the net graph is shown: the connection as this game sees it, beside the HUD.</summary>
    public static readonly BoolCVar NetGraph = CVar.Bool(
        "net_graph",
        false,
        CVarOptions.Archive,
        "Shows the connection in a multiplayer session: round trip, jitter, loss, rates, snapshots, interpolation, corrections.");

    /// <summary>Whether a hosted session logs every pass of every round a player fires at the others.</summary>
    public static readonly BoolCVar ShotLog = CVar.Bool(
        "sv_shotlog",
        true,
        CVarOptions.None,
        "Logs every round a player fires: what it struck, and for each other player whether its body was cast, met or skipped, and how near the round passed its chest.");

    /// <summary>Whether a hosted session keeps a recording of itself, to be saved as a demo.</summary>
    public static readonly BoolCVar HostRecord = CVar.Bool(
        "sv_recorddemo",
        true,
        CVarOptions.Archive,
        "Keeps a recording of every session this game hosts, from its start (up to 128 MB), so demo_save can save it. Off for a server short of memory.");

    /// <summary>How long a deathmatch lasts, in minutes, on a server this game hosts; 0 for the mode's own (10).</summary>
    public static readonly NumericCVar<float> TimeLimit = CVar.Float(
        "mp_timelimit",
        0f,
        0f,
        120f,
        CVarOptions.None,
        "How long a team deathmatch or free-for-all lasts on a server this game hosts, in minutes; 0 for the mode's own ten. Read when a match starts on a new map.");

    /// <summary>How long a round of a mode with a buy phase sells for, in seconds, from the end of its freeze time.</summary>
    public static readonly NumericCVar<float> BuyTime = CVar.Float(
        "mp_buytime",
        20f,
        0f,
        300f,
        CVarOptions.None,
        "How long players may buy in a search and destroy round on a server this game hosts, in seconds after the freeze time; 0 for no buying. Read as each round begins.");

    /// <summary>How long a decided round's result is shown before the next round begins, in seconds.</summary>
    public static readonly NumericCVar<float> RoundEndTime = CVar.Float(
        "mp_roundendtime",
        5f,
        1f,
        60f,
        CVarOptions.None,
        "How long a round's result is shown before the next round begins in search and destroy and elimination on a server this game hosts, in seconds. Read as each round begins.");

    /// <summary>How long a round the bomb ended is shown before the next, in seconds: a while to watch what it brought down.</summary>
    public static readonly NumericCVar<float> BombEndTime = CVar.Float(
        "mp_bombendtime",
        10f,
        1f,
        60f,
        CVarOptions.None,
        "How long a search and destroy round the bomb ended is shown before the next round begins, in seconds, to watch the blast and the collapse; never shorter than mp_roundendtime. Read as each round begins.");

    /// <summary>The money each player starts a map, and each half, with (I.G.I. 2's <c>moneystart</c>).</summary>
    public static readonly NumericCVar<int> MoneyStart = CVar.Int(
        "mp_moneystart",
        1000,
        0,
        100000,
        CVarOptions.None,
        "The money each player starts a map with on a server this game hosts, and starts again with at the half. Taken by players joining from then on, and at the next map.");

    /// <summary>The most money a player may hold (<c>moneycap</c>).</summary>
    public static readonly NumericCVar<int> MoneyCap = CVar.Int(
        "mp_moneycap",
        15000,
        0,
        100000,
        CVarOptions.None,
        "The most money a player may hold on a server this game hosts; pay that would take it past this is cut to it. Takes hold at once.");

    /// <summary>The money for killing an enemy (<c>moneykill</c>).</summary>
    public static readonly NumericCVar<int> MoneyKill = CVar.Int(
        "mp_moneykill",
        800,
        0,
        20000,
        CVarOptions.None,
        "The money a player is paid for killing an enemy on a server this game hosts. Takes hold at once.");

    /// <summary>The fine for killing a team mate, taken from the killer (<c>moneyteamkill</c>, there a negative pay).</summary>
    public static readonly NumericCVar<int> MoneyTeamKill = CVar.Int(
        "mp_moneyteamkill",
        4000,
        0,
        20000,
        CVarOptions.None,
        "The money taken from a player for killing a team mate on a server this game hosts; 0 for no fine. Takes hold at once.");

    /// <summary>The money each player of the team that wins a round is paid (<c>moneymissionwin</c>).</summary>
    public static readonly NumericCVar<int> MoneyWin = CVar.Int(
        "mp_moneywin",
        1250,
        0,
        20000,
        CVarOptions.None,
        "The money each player of the team that wins a round is paid on a server this game hosts. Takes hold at once.");

    /// <summary>The money each player of the team that loses a round is paid (<c>moneymissionlost</c>).</summary>
    public static readonly NumericCVar<int> MoneyLose = CVar.Int(
        "mp_moneylose",
        750,
        0,
        20000,
        CVarOptions.None,
        "The money each player of the team that loses a round (or of either team, in a draw) is paid on a server this game hosts. Takes hold at once.");

    /// <summary>The money the attacker who plants the bomb is paid.</summary>
    public static readonly NumericCVar<int> MoneyPlant = CVar.Int(
        "mp_moneyplant",
        300,
        0,
        20000,
        CVarOptions.None,
        "The money the attacker who plants the bomb is paid on a server this game hosts. Takes hold at once.");

    /// <summary>Gets a server's rules with the money the settings above give.</summary>
    /// <param name="rules">The rules as they stand.</param>
    /// <returns>The same rules when the money is already the settings', else the rules with the settings' money.</returns>
    public static MultiplayerRules WithMoney(MultiplayerRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.MoneyStart == MoneyStart.Value && rules.MoneyCap == MoneyCap.Value && rules.MoneyKill == MoneyKill.Value
            && rules.MoneyTeamKill == -MoneyTeamKill.Value && rules.MoneyMissionWin == MoneyWin.Value
            && rules.MoneyMissionLost == MoneyLose.Value && rules.MoneyPlant == MoneyPlant.Value
                ? rules
                : rules with
                {
                    MoneyStart = MoneyStart.Value,
                    MoneyCap = MoneyCap.Value,
                    MoneyKill = MoneyKill.Value,
                    MoneyTeamKill = -MoneyTeamKill.Value,
                    MoneyMissionWin = MoneyWin.Value,
                    MoneyMissionLost = MoneyLose.Value,
                    MoneyPlant = MoneyPlant.Value,
                };
    }

    /// <summary>
    /// Gets the settings a server's operator may set by name: in a dedicated server's <c>server.cfg</c>, or at its console.
    /// </summary>
    public static IReadOnlyList<ICVar> ServerSettings { get; } =
        [TimeLimit, BuyTime, RoundEndTime, BombEndTime, MoneyStart, MoneyCap, MoneyKill, MoneyTeamKill, MoneyWin, MoneyLose, MoneyPlant, ShotLog];

    /// <summary>Whether a session joined is saved as a demo when it is left.</summary>
    public static readonly BoolCVar AutoRecord = CVar.Bool(
        "cl_autorecord",
        false,
        CVarOptions.Archive,
        "Saves every session joined or hosted as a demo when it is left, in the demos folder beside the settings. demo_save saves one at any time.");
}
