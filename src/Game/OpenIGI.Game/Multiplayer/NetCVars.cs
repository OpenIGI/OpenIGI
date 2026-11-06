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
}
