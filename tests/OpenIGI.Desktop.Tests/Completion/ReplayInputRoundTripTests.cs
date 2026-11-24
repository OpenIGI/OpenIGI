using System.Globalization;
using OpenIGI.Game.Player;
using OpenIGI.Game.Replays;
using OpenIGI.Host;
using OpenIGI.Input;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>
/// A player at the controls -- walking, turning with the mouse, firing, crouching, reloading -- recorded as the game draws it,
/// then played back from the file only simulated, as the server checks a run: every second agrees. The replays of the solver's
/// runs prove the level; these prove the inputs, which the solver, standing its player where it wants it, never gives.
/// </summary>
[Collection(AiScenarioSupport.CollectionName)]
public sealed class ReplayInputRoundTripTests : IDisposable
{
    // A minute of play.
    private const int Ticks = 60 * 30;

    private readonly AiScenarioSupport _support = new();

    // Where the player stood after each tick of the last run recorded.
    private readonly List<Core.Numerics.Vector3d> _positions = [];
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-replay-input-{Guid.NewGuid():N}");

    public ReplayInputRoundTripTests() => Directory.CreateDirectory(_scratch);

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
    [InlineData(8)]
    public void A_minute_at_the_controls_plays_the_same_from_its_replay(int mission)
    {
        ReplayFile recorded = ReplayFile.Read(Record(mission).ToBytes());
        int withInput = Enumerable.Range(0, recorded.Tape.Length).Count(tick => recorded.Tape.InputAt(tick) is not null);

        // Its ghost runs the whole run, and moves as the player moved, tick for tick.
        GhostTrack ghost = Assert.IsType<GhostTrack>(recorded.Ghost);
        Assert.Equal(recorded.Tape.Length, ghost.Length);
        int ghostMoved = Enumerable.Range(1, ghost.Length - 1).Count(tick => ghost.At(tick)!.Value.Position != ghost.At(tick - 1)!.Value.Position);
        int playerMoved = Enumerable.Range(1, _positions.Count - 1).Count(i => Int3Of(_positions[i]) != Int3Of(_positions[i - 1]));
        Assert.True(playerMoved > 0, "The player never moved.");
        Assert.InRange(ghostMoved, playerMoved * 9 / 10, (playerMoved * 11 / 10) + 5);
        Assert.True(withInput > recorded.Tape.Length * 9 / 10, $"Only {withInput} of {recorded.Tape.Length} ticks took the player's input.");
        Assert.True(
            Enumerable.Range(0, recorded.Tape.Length).Any(tick => recorded.Tape.InputAt(tick) is { MouseX: not 0f }),
            "No tick took the mouse.");

        (int? partedAt, int matched, bool finished) = Play(Write(recorded, $"m{mission}"));
        Assert.True(partedAt is null, $"Mission {mission}'s replay parted at tick {partedAt} ({partedAt / 30.0:0.0} s), after {matched} of {recorded.Tape.Digests.Count} seconds agreed.");
        Assert.True(finished, "The replay never reached its end.");
        Assert.Equal(recorded.Tape.Digests.Count, matched);
    }

    [RequiresGameDataFact]
    public void A_replay_with_one_turn_of_the_mouse_changed_parts_from_its_recording()
    {
        ReplayFile recorded = ReplayFile.Read(Record(mission: 1).ToBytes());
        int changed = Enumerable.Range(recorded.Tape.Length / 2, recorded.Tape.Length / 2).First(tick => recorded.Tape.InputAt(tick) is not null);
        TitlePlayerInput input = recorded.Tape.InputAt(changed)!.Value;
        recorded.Tape.SetInput(changed, input with { MouseX = input.MouseX + 40f });

        (int? partedAt, _, _) = Play(Write(recorded, "changed"));
        Assert.NotNull(partedAt);
        Assert.True(partedAt >= changed, $"It parted at tick {partedAt}, before the changed input at {changed}.");
    }

    [RequiresGameDataTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_replay_sought_back_and_on_agrees_with_its_recording_to_the_end(bool afresh)
    {
        // Back from half a minute to ten seconds -- to where the level was held, or, with nothing held, loaded afresh and run up
        // to it -- then on from forty seconds to fifty-five, and played out: every second the recording kept agrees.
        ReplayFile recorded = ReplayFile.Read(Record(mission: 1).ToBytes());
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(
            device, new HeadlessGameWindow(), simulationOnly: true, "-playreplay", Write(recorded, afresh ? "afresh" : "held"), "-lockstep", "-frames", "100000000");
        host.Start();
        int seeks = 0;
        int lowest = int.MaxValue;
        for (int step = 0; step < 80 * Ticks && host.Step(); step++)
        {
            if (host.ReplayFinished || host.ReplayDivergedAt is not null)
            {
                break;
            }

            if (seeks > 0)
            {
                lowest = Math.Min(lowest, host.TickOfLevel);
            }

            if (seeks == 0 && host.TickOfLevel >= 30 * 30)
            {
                if (afresh)
                {
                    host.ForgetReplayCheckpointsForTesting();
                }

                host.ReplayTransportForTesting("seek 10");
                seeks++;
            }
            else if (seeks == 1 && host.TickOfLevel >= 40 * 30)
            {
                host.ReplayTransportForTesting("seek 55");
                seeks++;
            }
            else if (host.ReplayPausedForTesting)
            {
                host.ReplayTransportForTesting("run");
            }
        }

        (int? partedAt, int matched, bool finished) = (host.ReplayDivergedAt, host.ReplayFingerprintsMatched, host.ReplayFinished);
        host.Stop();
        Assert.Equal(2, seeks);
        // Held, the level is put straight back at ten seconds; loaded afresh, it runs up to them from its start.
        Assert.Equal(afresh ? 1 : 10 * 30, lowest);
        Assert.True(partedAt is null, $"The replay parted at tick {partedAt} ({partedAt / 30.0:0.0} s) after its seeks.");
        Assert.True(finished, "The replay never reached its end.");
        Assert.Equal(recorded.Tape.Digests.Count, matched);
    }

    private static Game.Multiplayer.Int3 Int3Of(Core.Numerics.Vector3d at) => Game.Multiplayer.Int3.From(at);

    // A minute of a scripted player in a mission, drawn, as its replay.
    private ReplayFile Record(int mission)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(
            device,
            new HeadlessGameWindow(),
            "-mission", mission.ToString(CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", "100000000");
        InputState input = new();
        host.ScriptedViewInput = input;
        ReplayFile? made = null;
        host.ReplayMade = replay => made ??= replay;
        host.Start();
        int recordedTicks = 0;
        _positions.Clear();
        host.TickDigested = (tick, _) =>
        {
            recordedTicks = tick;
            if (host.TitlePlayerForTesting is { } player)
            {
                _positions.Add(player.Position);
            }
        };
        for (int step = 0; step < 40 * Ticks && recordedTicks < Ticks && host.Step(); step++)
        {
            Steer(input, recordedTicks);
        }

        host.Stop();
        return made ?? throw new Xunit.Sdk.XunitException($"A minute of mission {mission} was played and no replay was made of it.");
    }

    // The controls as a player holds them at a tick: on through the level, looking about, firing in bursts, ducking, reloading.
    private static void Steer(InputState input, int tick)
    {
        input.BeginFrame();
        int phase = tick % 300;
        input.Keyboard.SetKey(DefaultControls.KeyFor("MoveUp"), phase < 200);
        input.Keyboard.SetKey(DefaultControls.KeyFor("Crouch"), phase is >= 200 and < 240);
        input.Keyboard.SetKey(DefaultControls.KeyFor("Reload"), phase is >= 280 and < 284);
        input.Mouse.SetButton(MouseButton.Left, phase % 60 is >= 40 and < 48);
        input.Mouse.AddMotion(phase < 100 ? 3f : phase < 150 ? -5f : 0.5f, phase % 90 < 10 ? 1f : 0f);
    }

    private string Write(ReplayFile replay, string name)
    {
        string file = Path.Combine(_scratch, name + ReplayFile.Extension);
        File.WriteAllBytes(file, replay.ToBytes());
        return file;
    }

    // The replay in a file played back only simulated: the first tick it parted on, how many seconds agreed, whether it ended.
    private (int? PartedAt, int Matched, bool Finished) Play(string file)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = _support.Host(device, new HeadlessGameWindow(), simulationOnly: true, "-playreplay", file, "-lockstep", "-frames", "100000000");
        host.Start();
        for (int step = 0; step < 40 * Ticks && host.Step(); step++)
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
