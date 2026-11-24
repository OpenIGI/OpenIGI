using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Game.Replays;
using OpenIGI.Game.Scoring;

namespace OpenIGI.Host.Replays;

/// <summary>What a replay checker found: the run as it plays here, or why it is no ranked run.</summary>
/// <param name="Verified">Whether the run is a ranked one, as it plays here.</param>
/// <param name="Reason">Why not, or empty.</param>
/// <param name="Ruleset">The rules it was played by, by id.</param>
/// <param name="EndTick">The tick the level completed its mission on here, or -1 where it never did.</param>
/// <param name="Score">The mission's score as it stood here when the level completed it -- the checker's own, never the file's.</param>
/// <param name="PartedAt">The first tick the run went another way here than its recording says it went, or null.</param>
/// <param name="Ghost">The runner as it moved here, tick by tick: the ghost a race against the run shows.</param>
public sealed record ReplayVerdict(
    bool Verified,
    string Reason,
    string Ruleset,
    int EndTick,
    MissionScore? Score,
    int? PartedAt,
    GhostTrack? Ghost)
{
    /// <summary>A run refused before it is played, or after: why.</summary>
    /// <param name="reason">Why it is no ranked run.</param>
    /// <param name="ruleset">The rules it says it was played by.</param>
    /// <returns>The verdict.</returns>
    public static ReplayVerdict Refused(string reason, string ruleset = "") => new(false, reason, ruleset, -1, null, null, null);
}

/// <summary>
/// The checks a ranked run is held to, around its being played again: before, what its file says (the build that recorded it,
/// the files it played from, the rules its settings make, that it claims a completed mission and nothing it saw unranked it,
/// that nothing but its inputs drove it); after, what the level did with its inputs here (every second as recorded, the mission
/// completed where the file says, the score the file claims the one the level came to here).
/// </summary>
/// <remarks>
/// The file is never trusted: the only thing it is taken at its word for is its inputs, and the level's own simulation, run here,
/// says what they come to. Played again, an input another run did not give parts at once from the fingerprints recorded beside
/// them; a claim the inputs do not bear out is refused by the comparison after.
/// </remarks>
public static class ReplayVerifier
{
    /// <summary>What a file must say before it is worth playing again.</summary>
    /// <param name="replay">The run.</param>
    /// <param name="buildVersion">This build's version: a run recorded by another may play differently, and is not ranked here.</param>
    /// <param name="contentFingerprint">The fingerprint of the files the run's mission plays from here.</param>
    /// <param name="acceptsTestCommands">Whether a test's own commands in the tape are let by (tests only).</param>
    /// <returns>Why it is refused, or <see langword="null"/> to play it.</returns>
    public static string? Before(ReplayFile replay, string buildVersion, string contentFingerprint, bool acceptsTestCommands = false)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ReplayHeader header = replay.Header;
        if (!string.Equals(header.GameVersion, buildVersion, StringComparison.Ordinal))
        {
            return $"it was recorded by {header.GameVersion}, not {buildVersion}";
        }

        if (!string.Equals(header.ContentFingerprint, contentFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return "it was played from other files than the game's own";
        }

        (Ruleset rules, string why) = Ruleset.Of(header.Settings, CVarRegistry.Default, ExperiencePresetRegistry.Default);
        if (rules == Ruleset.Unranked)
        {
            return $"it was not played by ranked rules: {why}";
        }

        if (!string.Equals(rules.Id, header.Ruleset, StringComparison.Ordinal))
        {
            return $"its settings are the {rules.Name} rules, and it says {header.Ruleset}";
        }

        if (replay.Result.Outcome != ReplayOutcome.Completed || replay.Result.Score is null)
        {
            return "it does not complete its mission";
        }

        if (replay.Result.Unranked.Length > 0)
        {
            return replay.Result.Unranked;
        }

        if (!header.PlaysCutscenes)
        {
            return "the game was started without its cutscenes";
        }

        if (replay.Result.EndTick != replay.Tape.Length - 1)
        {
            return "it says it ends where its inputs do not";
        }

        foreach ((int tick, string command) in replay.Tape.Commands)
        {
            bool test = acceptsTestCommands && command.StartsWith("solver-", StringComparison.Ordinal);
            if (!test)
            {
                return $"something besides its inputs moved it at {tick / 30.0:0.0} s ({command.Split(' ')[0]})";
            }
        }

        return null;
    }

    /// <summary>What the level did with the run's inputs here, against what the file claims.</summary>
    /// <param name="replay">The run.</param>
    /// <param name="partedAt">The first tick the run parted from its recording here, or null.</param>
    /// <param name="finished">Whether every tick it recorded was played.</param>
    /// <param name="completedAt">The tick the level completed its mission on here, or null where it never did.</param>
    /// <param name="score">The mission's score as it stood here when the level completed it.</param>
    /// <param name="ghost">The runner as it moved here.</param>
    /// <returns>The verdict.</returns>
    public static ReplayVerdict After(ReplayFile replay, int? partedAt, bool finished, int? completedAt, MissionScore? score, GhostTrack? ghost)
    {
        ArgumentNullException.ThrowIfNull(replay);
        string rules = replay.Header.Ruleset;
        if (partedAt is { } parted)
        {
            return new ReplayVerdict(false, $"it went another way here at {parted / 30.0:0.0} s than its recording says", rules, -1, null, parted, null);
        }

        if (completedAt is not { } end || score is not { } computed)
        {
            return ReplayVerdict.Refused(finished ? "its inputs do not complete its mission here" : "it was not played to its end", rules);
        }

        if (computed != replay.Result.Score)
        {
            return new ReplayVerdict(false, "the score it claims is not the one its run comes to", rules, end, computed, null, null);
        }

        return new ReplayVerdict(true, string.Empty, rules, end, computed, null, ghost);
    }
}
