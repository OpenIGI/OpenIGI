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

    /// <summary>
    /// Gets the settings a server's operator may set by name: in a dedicated server's <c>server.cfg</c>, or at its console.
    /// </summary>
    public static IReadOnlyList<ICVar> ServerSettings { get; } = [TimeLimit, BuyTime, ShotLog];

    /// <summary>Whether a session joined is saved as a demo when it is left.</summary>
    public static readonly BoolCVar AutoRecord = CVar.Bool(
        "cl_autorecord",
        false,
        CVarOptions.Archive,
        "Saves every session joined or hosted as a demo when it is left, in the demos folder beside the settings. demo_save saves one at any time.");
}
