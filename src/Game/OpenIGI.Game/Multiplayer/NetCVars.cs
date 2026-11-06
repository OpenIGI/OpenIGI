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

    /// <summary>Whether a hosted session keeps a recording of itself, to be saved as a demo.</summary>
    public static readonly BoolCVar HostRecord = CVar.Bool(
        "sv_recorddemo",
        true,
        CVarOptions.Archive,
        "Keeps a recording of every session this game hosts, from its start (up to 128 MB), so demo_save can save it. Off for a server short of memory.");

    /// <summary>Whether a session joined is saved as a demo when it is left.</summary>
    public static readonly BoolCVar AutoRecord = CVar.Bool(
        "cl_autorecord",
        false,
        CVarOptions.Archive,
        "Saves every session joined or hosted as a demo when it is left, in the demos folder beside the settings. demo_save saves one at any time.");
}
