using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Replays;

/// <summary>Whether single-player runs are kept as replays, and how many.</summary>
[CVarContainer]
public static class ReplayCVars
{
    /// <summary>Whether every single-player mission is recorded from its start.</summary>
    public static readonly BoolCVar Record = CVar.Bool(
        "g_replay_record",
        true,
        CVarOptions.Archive,
        "Record every single-player mission from its start as a replay, to watch again or race. Costs nothing a player can see.");

    /// <summary>How many of a mission's latest replays are kept, beside its best.</summary>
    public static readonly NumericCVar<int> Keep = CVar.Int(
        "g_replay_keep",
        20,
        1,
        200,
        CVarOptions.Archive,
        "How many of each mission's latest replays are kept. Its best runs are kept besides, whatever this is.");
}
