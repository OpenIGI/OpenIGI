using OpenIGI.Game.Replays;

namespace OpenIGI.Host.Replays;

/// <summary>
/// A ranked run checked: its file held to what it must say, then its inputs played again in a level of this game's own, headless
/// and only simulated, as fast as it runs, and what the level came to held against what the file claims (<see cref="ReplayVerifier"/>).
/// </summary>
public static class ReplayVerification
{
    /// <summary>The most host steps a check is given per tick of the run, before it is taken to have hung.</summary>
    public const int StepsPerTick = 8;

    /// <summary>Checks a run on a host made to play it back (<c>-playreplay</c>, lockstep, only simulated).</summary>
    /// <param name="host">The host, not yet started, its options naming the run's file.</param>
    /// <param name="replay">The run, as read from that file.</param>
    /// <param name="acceptsTestCommands">Whether a test's own commands in the tape are let by (tests only).</param>
    /// <returns>The verdict.</returns>
    public static ReplayVerdict Run(GameHost host, ReplayFile replay, bool acceptsTestCommands = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(replay);
        // Started, for the game's own files to be known, and checked before a tick of the run is played.
        host.Start();
        try
        {
            if (ReplayVerifier.Before(replay, GameHost.BuildVersion, host.ContentFingerprintOf(replay.Header.Mission), acceptsTestCommands) is { } refused)
            {
                return ReplayVerdict.Refused(refused, replay.Header.Ruleset);
            }

            // Past the run's end by a little, for its last tick's decision to be settled.
            long limit = ((long)replay.Tape.Length + 300) * StepsPerTick;
            for (long step = 0; step < limit && host.Step(); step++)
            {
                // Played on past a fingerprint that differs: what the run comes to is what decides it.
                if (host.ReplayFinished && host.ReplayCompletedAt is not null)
                {
                    break;
                }
            }

            return ReplayVerifier.After(
                replay, host.ReplayDivergedAt, host.ReplayFinished, host.ReplayCompletedAt, host.ReplayScore, host.ReplayGhost, host.ReplayRank);
        }
        finally
        {
            // The run's settings, borrowed as its playback began, given back: refused or not, they never outlive its check.
            host.StopPlayback();
            host.Stop();
        }
    }
}
