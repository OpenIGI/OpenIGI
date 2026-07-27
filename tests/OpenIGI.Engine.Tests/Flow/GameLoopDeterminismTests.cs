using System.Globalization;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using Xunit;

namespace OpenIGI.Engine.Tests.Flow;

/// <summary>
/// The determinism contract: simulation state is a function of the tick index and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The frame schedule reacts to wall clock — how often it renders, when it gives up and loses time —
/// but the simulation must not. Two machines whose clocks tick at 1 ms and at 16 ms have to compute the
/// same thing; only the number of frames they draw along the way may differ.
/// </para>
/// <para>
/// That is what makes the absolute-deadline formula worth defending. A <c>dt</c> accumulator would pass
/// a "run it twice" test and fail this one.
/// </para>
/// </remarks>
public sealed class GameLoopDeterminismTests
{
    private const int TargetTicks = 200;

    [Fact]
    public void TheSameClockScriptProducesTheSameFrameSequenceEveryRun()
    {
        Run first = RunFrames(500, granularity: 7);
        Run second = RunFrames(500, granularity: 7);

        Assert.Equal(first.Outcomes, second.Outcomes);
        Assert.Equal(first.StateHash, second.StateHash);
        Assert.Equal(first.TickCount, second.TickCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(100)]
    public void ClockGranularityChangesTheFramesButNeverTheSimulation(int granularity)
    {
        Run reference = RunToTicks(TargetTicks, granularity: 1);
        Run coarse = RunToTicks(TargetTicks, granularity);

        Assert.Equal(TargetTicks, coarse.TickCount);
        Assert.Equal(reference.StateHash, coarse.StateHash);
    }

    [Fact]
    public void ACoarseClockDrawsFewerFramesForTheSameSimulation()
    {
        Run fine = RunToTicks(TargetTicks, granularity: 1);
        Run coarse = RunToTicks(TargetTicks, granularity: 100);

        // The point of the previous test: the frame counts genuinely differ, so the state matching is
        // not an accident of the two runs behaving identically.
        Assert.True(
            coarse.FrameCount < fine.FrameCount,
            $"expected fewer frames at 100 ms granularity, got {coarse.FrameCount} against {fine.FrameCount}");
    }

    [Fact]
    public void AStallDoesNotLetTheSimulationRunAheadOfItsTickIndex()
    {
        Run steady = RunToTicks(TargetTicks, granularity: 1);
        Run stalled = RunToTicks(TargetTicks, granularity: 1, stallEveryFrames: 20, stallMilliseconds: 750);

        // The catch-up cap means a stalled run reaches the same tick in less simulated time than a
        // steady one — but the state at that tick is identical, because it never depends on the clock.
        Assert.Equal(steady.StateHash, stalled.StateHash);
    }

    private static Run RunFrames(int frames, int granularity)
    {
        Harness harness = new();
        List<FrameOutcome> outcomes = new(frames);

        for (int i = 0; i < frames; i++)
        {
            outcomes.Add(harness.Loop.RunFrame());
            harness.Time.Advance(granularity);
        }

        return harness.Snapshot(outcomes);
    }

    private static Run RunToTicks(int targetTicks, int granularity, int stallEveryFrames = 0, int stallMilliseconds = 0)
    {
        Harness harness = new();
        int frames = 0;

        while (harness.Loop.Clock.TickCount < targetTicks)
        {
            harness.Loop.RunFrame();
            frames++;

            if (stallEveryFrames > 0 && frames % stallEveryFrames == 0)
            {
                harness.Time.Advance(stallMilliseconds);
            }
            else
            {
                harness.Time.Advance(granularity);
            }

            Assert.True(frames < 1_000_000, "the loop never reached the target tick count");
        }

        return harness.Snapshot([]);
    }

    private sealed record Run(IReadOnlyList<FrameOutcome> Outcomes, string StateHash, int TickCount, int FrameCount);

    private sealed class Harness
    {
        public Harness()
        {
            Time = new ManualTimeSource(12_345);
            Loop = new GameLoop(Time);

            TaskType type = Loop.Types.Register("Sim", () => new SimulationTask());
            GameTask branch = Loop.Tasks.New(Loop.Tasks.Root, type);
            Loop.Tasks.New(branch, type);
            Loop.Tasks.New(branch, type);
            Loop.Tasks.New(Loop.Tasks.Root, type);
            Loop.SystemTasks.New(Loop.SystemTasks.Root, type);
        }

        public ManualTimeSource Time { get; }

        public GameLoop Loop { get; }

        public Run Snapshot(IReadOnlyList<FrameOutcome> outcomes) =>
            new(outcomes, Hash(), Loop.Clock.TickCount, Loop.Clock.FrameCount);

        private string Hash()
        {
            long hash = 17;
            Collect(Loop.Tasks.Root, ref hash);
            Collect(Loop.SystemTasks.Root, ref hash);
            return hash.ToString(CultureInfo.InvariantCulture);
        }

        private static void Collect(GameTask task, ref long hash)
        {
            foreach (GameTask child in task.Children)
            {
                if (child is SimulationTask sim)
                {
                    hash = unchecked((hash * 1_000_003) + sim.State);
                }

                Collect(child, ref hash);
            }
        }
    }

    /// <summary>
    /// Advances a value with a mixing step, so any divergence in tick count or ordering shows up in the
    /// final state rather than cancelling out.
    /// </summary>
    private sealed class SimulationTask : GameTask
    {
        public long State { get; private set; } = 1;

        protected override void OnUpdate()
        {
            State = unchecked((State * 6_364_136_223_846_793_005L) + 1_442_695_040_888_963_407L);
            ForwardToChildren(TaskMessage.Update);
        }
    }
}
