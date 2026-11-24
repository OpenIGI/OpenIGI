using System.Globalization;
using OpenIGI.Game.Player;
using OpenIGI.Game.Replays;
using OpenIGI.Game.Scoring;
using OpenIGI.Host;
using OpenIGI.Host.Replays;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>
/// A ranked run checked as the server checks one: played again from its file in a level of the game's own, headless and only
/// simulated. A clean completed run is verified, with the score the level came to here and the ghost the runner made here. A file
/// that claims more than its inputs did -- a better score, a cheat it hides, a setting changed, another build, other files, a
/// mission it did not complete -- is refused, and says why.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class ReplayVerificationTests : IDisposable
{
    // Half an hour of play, past every mission the solver completes.
    private const int MaxTicks = 30 * 60 * 30;

    private readonly AiScenarioSupport _support = new();
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-verify-{Guid.NewGuid():N}");

    public ReplayVerificationTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        _support.Dispose();
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // Nothing to do: the failure is expected here and leaves nothing to undo.
        }
    }

    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_clean_completed_run_is_verified_with_its_own_score_and_ghost(int mission)
    {
        ReplayFile run = Record(mission);
        ReplayVerdict verdict = Verify(run, "clean");

        Assert.True(verdict.Verified, verdict.Reason);
        Assert.Equal(run.Result.Score, verdict.Score);
        Assert.Equal(Ruleset.Modern.Id, verdict.Ruleset);

        // Unseen, unhurt and quick: it earns a rank, as the level's own requirements weigh it.
        Assert.True(verdict.Rank >= 0, $"The run earned no rank: {verdict.Score}.");
        GhostTrack ghost = Assert.IsType<GhostTrack>(verdict.Ghost);
        Assert.InRange(ghost.Length, run.Tape.Length - 2, run.Tape.Length + 2);
        Assert.Equal(run.Ghost!.Splits(), ghost.Splits());
        Assert.Contains(ghost.Events, static e => e is { Kind: GhostEventKind.End, Detail: 1 });
    }

    [RequiresGameDataFact]
    public void A_run_claiming_a_better_score_than_it_made_is_refused()
    {
        ReplayFile run = Record(mission: 1);
        MissionScore made = run.Result.Score!.Value;
        ReplayFile claimed = run with { Result = run.Result with { Score = made with { Ticks = made.Ticks - 90 } } };

        ReplayVerdict verdict = Verify(claimed, "claimed");

        Assert.False(verdict.Verified);
        Assert.Contains("score", verdict.Reason, StringComparison.Ordinal);
        Assert.Equal(made, verdict.Score);
    }

    [RequiresGameDataFact]
    public void A_run_played_with_a_cheat_its_file_hiding_it_goes_another_way_and_is_refused()
    {
        // Played carrying every weapon, so the player's arms were never the mission's; its file then rewritten to say it was played
        // by the rules.
        ReplayFile clean = Record(mission: 3);
        ReplayFile cheated;
        PlayerCheatCVars.CheatsEnabled.Override(true);
        PlayerCheatCVars.AllWeapons.Override(true);
        try
        {
            cheated = Record(mission: 3);
        }
        finally
        {
            PlayerCheatCVars.AllWeapons.ClearOverride();
            PlayerCheatCVars.CheatsEnabled.ClearOverride();
        }

        Assert.NotEqual(clean.Header.Settings, cheated.Header.Settings);
        ReplayFile hidden = cheated with
        {
            Header = cheated.Header with { Settings = clean.Header.Settings, Ruleset = clean.Header.Ruleset },
            Result = cheated.Result with { Unranked = string.Empty },
        };

        ReplayVerdict verdict = Verify(hidden, "hidden");

        Assert.False(verdict.Verified);
        Assert.NotNull(verdict.PartedAt);
    }

    [RequiresGameDataFact]
    public void A_run_with_a_cheat_in_its_settings_is_refused_before_it_is_played()
    {
        ReplayFile run = Record(mission: 1);
        List<(string Name, string Value)> settings = [.. run.Header.Settings];
        int at = settings.FindIndex(static s => s.Name == PlayerCheatCVars.InfiniteHealth.Name);
        settings[at] = (PlayerCheatCVars.InfiniteHealth.Name, "1");

        ReplayVerdict verdict = Verify(run with { Header = run.Header with { Settings = settings } }, "cheat");

        Assert.False(verdict.Verified);
        Assert.Null(verdict.Ghost);
        Assert.Contains(PlayerCheatCVars.InfiniteHealth.Name, verdict.Reason, StringComparison.Ordinal);
    }

    [RequiresGameDataFact]
    public void A_run_of_another_build_other_files_or_no_completed_mission_is_refused()
    {
        ReplayFile run = Record(mission: 1);

        Assert.Contains("recorded by", Verify(run with { Header = run.Header with { GameVersion = "0.0.1" } }, "build").Reason, StringComparison.Ordinal);
        Assert.Contains("other files", Verify(run with { Header = run.Header with { ContentFingerprint = new string('0', 64) } }, "files").Reason, StringComparison.Ordinal);
        Assert.Contains(
            "does not complete",
            Verify(run with { Result = run.Result with { Outcome = ReplayOutcome.Failed } }, "failed").Reason,
            StringComparison.Ordinal);
    }

    // A clean completed run of a mission: no cheat, its cutscenes played, as a player would make one.
    private ReplayFile Record(int mission)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(
            device, new HeadlessGameWindow(), "-mission", mission.ToString(CultureInfo.InvariantCulture), "-lockstep", "-frames", "100000000");
        ReplayFile? made = null;
        host.ReplayMade = replay => made ??= replay;
        host.Start();
        MissionSolver solver = MissionSolver.Over(host, mission);
        solver.Run(MaxTicks);
        host.Stop();
        Assert.True(solver.Outcome == true, $"Mission {mission} was not completed as it was recorded:\n{solver.Log}");
        return made ?? throw new Xunit.Sdk.XunitException($"Mission {mission} was completed and no replay was made of it.");
    }

    // The run through its bytes, checked by another game as the server checks one. The solver's own moves are recorded beside the
    // inputs, and only a test lets them by.
    private ReplayVerdict Verify(ReplayFile run, string name)
    {
        string file = Path.Combine(_scratch, name + ReplayFile.Extension);
        File.WriteAllBytes(file, run.ToBytes());
        ReplayFile read = ReplayFile.Read(File.ReadAllBytes(file));
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(device, new HeadlessGameWindow(), simulationOnly: true, "-playreplay", file, "-lockstep", "-frames", "100000000");
        host.ReplayCommandHandler = words => MissionSolver.ApplyRecorded(host, words);
        return ReplayVerification.Run(host, read, acceptsTestCommands: true);
    }
}
