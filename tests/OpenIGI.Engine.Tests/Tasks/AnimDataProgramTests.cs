using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// Pins the <c>AnimData</c> interpreter against <c>0x4F3F60</c> and <c>0x4F3EF0</c>.
/// </summary>
public sealed class AnimDataProgramTests
{
    [Fact]
    public void ReceivingTaskCanInstallALatchedResetSentinel()
    {
        AnimDataProgram program = new([new AnimDataEntry(AnimDataProgram.WaitChannel, 3f)]);

        program.SeekTo(0);
        program.WriteChannel(3, -1f);
        program.SeekTo(1);

        Assert.Equal(-1f, program.Channels[3]);
    }

    [Fact]
    public void AnEntryJustWritesItsChannel()
    {
        // There is no instruction set: opcode 7 means "channel 7". The programme runs until something
        // writes the delay, so all three of these land in the same tick.
        AnimDataProgram program = new([
            new AnimDataEntry(0, -1f),
            new AnimDataEntry(7, 0.5f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 3f),
        ]);

        program.Step();

        Assert.Equal(-1f, program.Channels[0]);
        Assert.Equal(0.5f, program.Channels[7]);
        Assert.Equal(3, program.RemainingWait);
    }

    [Fact]
    public void TheDelayChannelIsWhatStopsTheProgramme()
    {
        AnimDataProgram program = new([
            new AnimDataEntry(1, 1f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 2f),
            new AnimDataEntry(1, 0f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 1f),
        ]);

        program.Step();
        Assert.Equal(1f, program.Channels[1]);
        Assert.Equal(2, program.RemainingWait);

        // A tick of the delay is spent and nothing else happens.
        program.Step();
        Assert.Equal(1f, program.Channels[1]);
        Assert.Equal(1, program.RemainingWait);

        // The delay runs out and the next entries run.
        program.Step();
        Assert.Equal(0f, program.Channels[1]);
        Assert.Equal(1, program.RemainingWait);
    }

    [Fact]
    public void TheDelayIsTruncatedNotRounded()
    {
        // The original truncates when the script is parsed and again here. A rounding implementation
        // would hold one tick too long.
        AnimDataProgram program = new([new AnimDataEntry(AnimDataProgram.WaitChannel, 3.9f)]);

        program.Step();

        Assert.Equal(3, program.RemainingWait);
    }

    [Fact]
    public void AProgrammeThatNeverDelaysStillTerminates()
    {
        // Every entry writes an ordinary channel, so the loop consumes the whole programme in one tick
        // and stops on the bounds check rather than on a delay.
        AnimDataProgram program = new([
            new AnimDataEntry(0, 1f),
            new AnimDataEntry(1, 1f),
            new AnimDataEntry(2, 1f),
        ]);

        program.Step();

        Assert.True(program.HasFinished);
        Assert.Equal(3, program.ProgramCounter);
    }

    [Fact]
    public void AFinishedProgrammeHoldsItsLastChannelValues()
    {
        AnimDataProgram program = new([
            new AnimDataEntry(0, 0.25f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 1f),
        ]);

        program.Step();
        program.Step();
        program.Step();

        Assert.True(program.HasFinished);
        Assert.Equal(0.25f, program.Channels[0]);
    }

    [Fact]
    public void WritingTheProgramCounterChannelIsAJump()
    {
        // A consequence of the channel design rather than a designed feature — no retail programme
        // does it, but the interpreter cannot tell the difference, so this pins the behaviour.
        AnimDataProgram program = new([
            new AnimDataEntry(AnimDataProgram.ProgramCounterChannel, 1f),
            new AnimDataEntry(0, 99f),
            new AnimDataEntry(1, 5f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 1f),
        ]);

        program.Step();

        // The write is followed by the ordinary "advance unless a delay was set", so a jump to N
        // resumes at N + 1: entry 1 is skipped and entry 2 runs.
        Assert.Equal(0f, program.Channels[0]);
        Assert.Equal(5f, program.Channels[1]);
        Assert.Equal(1, program.RemainingWait);
    }

    [Fact]
    public void SeekingBackwardsReplaysFromTheStart()
    {
        AnimDataProgram program = new([
            new AnimDataEntry(0, 1f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 2f),
            new AnimDataEntry(0, 2f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 5f),
        ]);

        program.SeekTo(4);
        Assert.Equal(2f, program.Channels[0]);

        // The interpreter cannot run in reverse, so a backward seek clears the channels and replays.
        program.SeekTo(0);
        Assert.Equal(1f, program.Channels[0]);
        Assert.Equal(0, program.Tick);
    }

    [Fact]
    public void SeekingForwardsMatchesSteppingTheWholeWay()
    {
        AnimDataEntry[] entries =
        [
            new AnimDataEntry(0, -1f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 3f),
            new AnimDataEntry(1, 0.125f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 4f),
            new AnimDataEntry(0, 1f),
            new AnimDataEntry(AnimDataProgram.WaitChannel, 2f),
        ];

        AnimDataProgram sought = new(entries);
        sought.SeekTo(6);

        // Seven steps, not six: a task's tick counter starts at -1, so reaching tick 6 runs one step
        // for each of -1..5. The original's reset path agrees, stepping `tick + 1` times.
        AnimDataProgram stepped = new(entries);
        for (int i = 0; i < 7; i++)
        {
            stepped.Step();
        }

        Assert.Equal(stepped.Channels[0], sought.Channels[0]);
        Assert.Equal(stepped.Channels[1], sought.Channels[1]);
        Assert.Equal(stepped.RemainingWait, sought.RemainingWait);
    }

    [Fact]
    public void AnOpcodeOutsideTheChannelArrayIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnimDataProgram([new AnimDataEntry(AnimDataProgram.ChannelCount, 0f)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AnimDataProgram([new AnimDataEntry(-1, 0f)]));
    }
}
