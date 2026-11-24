using System.Globalization;
using OpenIGI.Game.Player;
using OpenIGI.Game.Replays;
using OpenIGI.Host;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>
/// A whole mission played -- the solver's run, fought through to its end -- recorded as a replay, written to bytes and read back
/// as a server takes one, then played back in another game from nothing but the file: it agrees with its recording at every
/// second it kept a fingerprint of, and comes to the same end.
/// </summary>
/// <remarks>
/// The proof a replay is the run it recorded, with all the level's combat, deaths, doors and scripts in it; and the first second
/// it parts names what the level read that the replay did not carry. One mission a process, as the completion runs are.
/// </remarks>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class ReplayRoundTripTests : IDisposable
{
    private const int MaxTicks = 30 * 60 * 20;

    private readonly AiScenarioSupport _support = new();
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-replay-{Guid.NewGuid():N}");

    public ReplayRoundTripTests() => Directory.CreateDirectory(_scratch);

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
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void A_completed_mission_plays_the_same_from_its_replay(int mission)
    {
        PlayerCheatCVars.CheatsEnabled.Override(true);
        PlayerCheatCVars.InfiniteHealth.Override(true);
        try
        {
            ReplayFile recorded = Record(mission);
            Assert.Equal(ReplayOutcome.Completed, recorded.Result.Outcome);
            Assert.NotEmpty(recorded.Tape.Digests);

            // Through the bytes, as the file goes to the server.
            string file = Path.Combine(_scratch, $"m{mission}{ReplayFile.Extension}");
            File.WriteAllBytes(file, recorded.ToBytes());
            (int? partedAt, int matched, bool finished) = Play(file);

            Assert.True(
                partedAt is null,
                $"Mission {mission}'s replay parted from its recording at tick {partedAt} ({partedAt / 30.0:0.0} s), after {matched} of {recorded.Tape.Digests.Count} seconds agreed.");
            Assert.True(finished, $"Mission {mission}'s replay never reached its end ({recorded.Tape.Length} ticks).");
            Assert.Equal(recorded.Tape.Digests.Count, matched);
        }
        finally
        {
            PlayerCheatCVars.InfiniteHealth.ClearOverride();
            PlayerCheatCVars.CheatsEnabled.ClearOverride();
        }
    }

    /// <summary>The same replay, only simulated as the server's checker plays it -- nothing drawn, no picture to read.</summary>
    /// <param name="mission">The mission.</param>
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void A_completed_mission_plays_the_same_from_its_replay_only_simulated(int mission)
    {
        PlayerCheatCVars.CheatsEnabled.Override(true);
        PlayerCheatCVars.InfiniteHealth.Override(true);
        try
        {
            ReplayFile recorded = Record(mission);
            string file = Path.Combine(_scratch, $"m{mission}{ReplayFile.Extension}");
            File.WriteAllBytes(file, recorded.ToBytes());
            (int? partedAt, int matched, bool finished) = Play(file, simulationOnly: true);

            Assert.True(
                partedAt is null,
                $"Mission {mission}'s replay, only simulated, parted from its drawn recording at tick {partedAt} ({partedAt / 30.0:0.0} s), after {matched} of {recorded.Tape.Digests.Count} seconds agreed.");
            Assert.True(finished, $"Mission {mission}'s replay never reached its end ({recorded.Tape.Length} ticks).");
            Assert.Equal(recorded.Tape.Digests.Count, matched);
        }
        finally
        {
            PlayerCheatCVars.InfiniteHealth.ClearOverride();
            PlayerCheatCVars.CheatsEnabled.ClearOverride();
        }
    }

    /// <summary>
    /// A replay with one of the solver's blasts set off elsewhere is not the run it recorded: it parts from it, on or after that
    /// blast. The solver gives its player no input -- it stands it where it wants it and blows up what it must -- so these
    /// replays are proved by its moves, and a blast moved a hundred metres spares what it was set off at.
    /// </summary>
    [RequiresGameDataFact]
    public void A_replay_with_one_blast_moved_parts_from_its_recording()
    {
        PlayerCheatCVars.CheatsEnabled.Override(true);
        PlayerCheatCVars.InfiniteHealth.Override(true);
        try
        {
            // The first of a few missions whose run sets a blast off.
            ReplayFile? recorded = null;
            (int Tick, string Command)[] moves = [];
            foreach (int mission in new[] { 3, 5, 2, 8 })
            {
                recorded = ReplayFile.Read(Record(mission).ToBytes());
                moves = [.. recorded.Tape.Commands.Where(static c => c.Command.StartsWith("solver-blast ", StringComparison.Ordinal))];
                if (moves.Length > 0)
                {
                    break;
                }
            }

            Assert.True(recorded is not null && moves.Length > 0, "None of the missions tried sets a blast off.");
            (int changedTick, string changedMove) = moves[0];
            string[] words = changedMove.Split(' ');
            double x = double.Parse(words[1], CultureInfo.InvariantCulture) + (100 * 4096.0);
            string moved = string.Join(' ', words[0], x.ToString("R", CultureInfo.InvariantCulture), words[2], words[3]);

            string file = Path.Combine(_scratch, $"moved{ReplayFile.Extension}");
            File.WriteAllBytes(file, (recorded! with { Tape = WithCommand(recorded.Tape, changedTick, changedMove, moved) }).ToBytes());
            (int? partedAt, _, _) = Play(file);

            Assert.NotNull(partedAt);
            Assert.True(partedAt >= changedTick, $"It parted at tick {partedAt}, before the moved blast at {changedTick}.");
        }
        finally
        {
            PlayerCheatCVars.InfiniteHealth.ClearOverride();
            PlayerCheatCVars.CheatsEnabled.ClearOverride();
        }
    }

    // A tape as another, one of its commands said otherwise.
    private static InputTape WithCommand(InputTape tape, int tick, string was, string becomes)
    {
        InputTape copy = new();
        for (int t = 0; t < tape.Length; t++)
        {
            copy.Ran(t);
            copy.SetBreak(t, tape.BreaksAt(t));
            if (tape.InputAt(t) is { } input)
            {
                copy.SetInput(t, input);
            }
        }

        bool swapped = false;
        foreach ((int at, string command) in tape.Commands)
        {
            bool swap = !swapped && at == tick && command == was;
            swapped |= swap;
            copy.Command(at, swap ? becomes : command);
        }

        foreach ((int at, ulong digest) in tape.Digests)
        {
            copy.SetDigest(at, digest);
        }

        return copy;
    }

    // The solver's run of a mission, as its replay.
    private ReplayFile Record(int mission)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(
            device,
            new HeadlessGameWindow(),
            "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "100000000");
        ReplayFile? made = null;
        host.ReplayMade = replay => made ??= replay;
        host.Start();
        MissionSolver solver = MissionSolver.Over(host, mission);
        solver.Run(MaxTicks);

        // The rank screen holds the level once the mission is decided; the replay is made as it is decided.
        host.Stop();
        Assert.True(solver.Outcome == true, $"Mission {mission} {(solver.Outcome == false ? "failed" : "never ended")} as it was recorded:\n{solver.Log}");
        return made ?? throw new Xunit.Sdk.XunitException($"Mission {mission} was completed and no replay was made of it.");
    }

    // The replay in a file, played back by another game: the first tick it parted on, how many seconds agreed, and whether it
    // reached its end.
    private (int? PartedAt, int Matched, bool Finished) Play(string file, bool simulationOnly = false)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(device, new HeadlessGameWindow(), simulationOnly, "-playreplay", file, "-lockstep", "-frames", "100000000");
        host.ReplayCommandHandler = words => MissionSolver.ApplyRecorded(host, words);
        host.Start();
        for (int step = 0; step < 20 * MaxTicks && host.Step(); step++)
        {
            if (host.ReplayFinished || host.ReplayDivergedAt is not null)
            {
                break;
            }
        }

        (int? PartedAt, int Matched, bool Finished) outcome = (host.ReplayDivergedAt, host.ReplayFingerprintsMatched, host.ReplayFinished);
        host.Stop();
        return outcome;
    }
}
