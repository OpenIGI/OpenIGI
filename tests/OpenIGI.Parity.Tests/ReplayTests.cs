using OpenIGI.Core.Titles;
using OpenIGI.Game.Replays;
using OpenIGI.Host;
using OpenIGI.Input;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// I.G.I. 2 at the controls for a minute -- walking, the mouse, bursts, ducking -- recorded as the game draws it, then played back
/// from the file only simulated, as the server checks a run: every second agrees, and one turn of the mouse changed parts it.
/// </summary>
[Collection(ParityGroup.Name)]
public sealed class ReplayTests : IDisposable
{
    private const int Ticks = 60 * 30;

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-replays-{Guid.NewGuid():N}");

    public ReplayTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // Nothing to do: the failure is expected here and leaves nothing to undo.
        }
    }

    [RequiresTitleDataTheory("igi2")]
    [InlineData(21)]
    [InlineData(31)]
    public void Igi2_minute_at_the_controls_plays_the_same_from_its_replay(int mission)
    {
        ReplayFile recorded = ReplayFile.Read(Record(mission).ToBytes());
        int withInput = Enumerable.Range(0, recorded.Tape.Length).Count(tick => recorded.Tape.InputAt(tick) is not null);

        // Its ghost runs the whole run, and goes where the player went.
        GhostTrack ghost = Assert.IsType<GhostTrack>(recorded.Ghost);
        Assert.Equal(recorded.Tape.Length, ghost.Length);
        int moved = Enumerable.Range(1, ghost.Length - 1).Count(tick => ghost.At(tick)!.Value.Position != ghost.At(tick - 1)!.Value.Position);
        Assert.True(moved > ghost.Length / 4, $"The ghost moved on only {moved} of {ghost.Length} ticks.");
        Assert.True(withInput > recorded.Tape.Length / 2, $"Only {withInput} of {recorded.Tape.Length} ticks took the player's input.");

        (int? partedAt, int matched, bool finished) = Play(Write(recorded, $"igi2-m{mission}"), mission);
        Assert.True(partedAt is null, $"I.G.I. 2 mission {mission}'s replay parted at tick {partedAt} ({partedAt / 30.0:0.0} s), after {matched} of {recorded.Tape.Digests.Count} seconds agreed.");
        Assert.True(finished, "The replay never reached its end.");
        Assert.Equal(recorded.Tape.Digests.Count, matched);
    }

    [RequiresTitleDataFact("igi2")]
    public void Igi2_replay_with_one_turn_of_the_mouse_changed_parts_from_its_recording()
    {
        ReplayFile recorded = ReplayFile.Read(Record(21).ToBytes());
        int changed = Enumerable.Range(recorded.Tape.Length / 2, recorded.Tape.Length / 2).First(tick => recorded.Tape.InputAt(tick) is not null);
        Game.Player.TitlePlayerInput input = recorded.Tape.InputAt(changed)!.Value;
        recorded.Tape.SetInput(changed, input with { MouseX = input.MouseX + 40f });

        (int? partedAt, _, _) = Play(Write(recorded, "igi2-changed"), 21);
        Assert.NotNull(partedAt);
        Assert.True(partedAt >= changed, $"It parted at tick {partedAt}, before the changed input at {changed}.");
    }

    private static ReplayFile Record(int mission)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = ParityHost.Create(GameDataSource.Igi2, GameTitle.Igi2, mission, device, frames: 400 * Ticks);
        InputState input = new();
        host.ScriptedViewInput = input;
        ReplayFile? made = null;
        host.ReplayMade = replay => made ??= replay;
        int ticks = 0;
        host.TickDigested = (tick, _) => ticks = tick;
        host.Start();
        for (int step = 0; step < 400 * Ticks && ticks < Ticks && host.Step(); step++)
        {
            Steer(input, ticks);
        }

        host.Stop();
        return made ?? throw new Xunit.Sdk.XunitException($"A minute of I.G.I. 2 mission {mission} was played and no replay was made of it.");
    }

    // The controls as a player holds them: on, looking about, firing in bursts, ducking.
    private static void Steer(InputState input, int tick)
    {
        input.BeginFrame();
        int phase = tick % 300;
        input.Keyboard.SetKey(DefaultControls.KeyFor("MoveUp"), phase < 200);
        input.Keyboard.SetKey(DefaultControls.KeyFor("Crouch"), phase is >= 200 and < 240);
        input.Mouse.SetButton(MouseButton.Left, phase % 60 is >= 40 and < 48);
        input.Mouse.AddMotion(phase < 100 ? 3f : phase < 150 ? -5f : 0.5f, phase % 90 < 10 ? 1f : 0f);
    }

    private string Write(ReplayFile replay, string name)
    {
        string file = Path.Combine(_scratch, name + ReplayFile.Extension);
        File.WriteAllBytes(file, replay.ToBytes());
        return file;
    }

    private static (int? PartedAt, int Matched, bool Finished) Play(string file, int mission)
    {
        using RecordingGraphicsDevice device = new() { IsRecording = false };
        GameHost host = ParityHost.Create(GameDataSource.Igi2, GameTitle.Igi2, mission, device, frames: 400 * Ticks, simulationOnly: true, "-playreplay", file);
        host.Start();
        for (int step = 0; step < 400 * Ticks && host.Step(); step++)
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
