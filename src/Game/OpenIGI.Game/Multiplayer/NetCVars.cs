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

    /// <summary>Whether a session joined is saved as a demo when it is left.</summary>
    public static readonly BoolCVar AutoRecord = CVar.Bool(
        "cl_autorecord",
        false,
        CVarOptions.Archive,
        "Saves every session joined as a demo when it is left, in the demos folder beside the settings. demo_save saves one at any time.");
}
