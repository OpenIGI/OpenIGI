using OpenIGI.Engine.Time;
using Xunit;

namespace OpenIGI.Engine.Tests.Time;

/// <summary>
/// The timing contract, rule by rule.
/// </summary>
/// <remarks>
/// Every one of these would still "work" if it were relaxed — the game would run, just not the same
/// way — so each test names the specific relaxation it rules out. The disassembly they are read from is
/// transcribed in the original.
/// </remarks>
public sealed class GameClockTests
{
    [Fact]
    public void DeadlinesComeFromTheTickIndexSoTheyNeverDrift()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 0;
        clock.SetTickRate(30);

        // 1000/30 truncated per tick would give 0, 33, 66, 99, 132 — a millisecond of drift every third
        // tick, compounding. From the index it is 0, 33, 66, 100, 133.
        Assert.Equal([0, 33, 66, 100, 133, 166, 200], DeadlinesForFirstTicks(clock, 7));
    }

    [Fact]
    public void DeadlinesTruncateTowardZeroRatherThanRounding()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 0;
        clock.SetTickRate(30);

        // Tick 2 is at 66.67 and tick 5 at 166.67. Rounding to nearest — what a bare x87 fistp does
        // unless the rounding mode is forced to chop, which the original does every frame — would put
        // them at 67 and 167 and move a third of all deadlines by a millisecond.
        long[] deadlines = DeadlinesForFirstTicks(clock, 6);
        Assert.Equal(66, deadlines[2]);
        Assert.Equal(166, deadlines[5]);
    }

    [Fact]
    public void TheDeadlineIsPassedNotReached()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 1000;
        clock.SetTickRate(30);

        Assert.False(clock.IsTickDue(1000));
        Assert.True(clock.IsTickDue(1001));
    }

    [Fact]
    public void TheTimeBaseAndExcludedTimeAreSummedAsThirtyTwoBitIntegers()
    {
        // The original adds the two fields with a single `add ecx, edx` and only then converts to
        // double, so the sum wraps instead of widening. Reproduced by starting the clock near the top
        // of the range and excluding enough time to carry past it.
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = int.MaxValue - 10;
        clock.SetTickRate(30);

        clock.BeginExcludedTime();
        time.Milliseconds = unchecked(int.MaxValue - 10 + 100);
        clock.EndExcludedTime();

        Assert.Equal(100, clock.ExcludedMilliseconds);
        Assert.Equal(unchecked(int.MaxValue - 10 + 100), unchecked((int)clock.DueMilliseconds));
    }

    [Fact]
    public void ATickRateResetRebasesTheClockAndLatchesARender()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 500;

        clock.SetTickRate(30);

        Assert.Equal(500, clock.TimeBaseMilliseconds);
        Assert.Equal(0, clock.TickCount);
        Assert.Equal(0, clock.FrameCount);
        Assert.Equal(0, clock.ExcludedMilliseconds);

        // The latch is what guarantees the next thing to happen is a tick, never a render: with it
        // clear, the startup guard would send the first frame down the render branch instead.
        Assert.True(clock.HasRendered);
        Assert.Equal(FrameAction.Tick, clock.Decide(501));
    }

    [Fact]
    public void ATickRateResetLeavesTheCatchUpBudgetAndDriftAlone()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.TimeAdjustMilliseconds = 7;
        time.Advance(1);
        clock.Decide(time.Milliseconds);
        clock.CompleteTick();

        clock.SetTickRate(30);

        // Time_SetTickRate writes six fields and these two are not among them; the boot path clears
        // them once, separately.
        Assert.Equal(1, clock.CatchUpCount);
        Assert.Equal(7, clock.TimeAdjustMilliseconds);
    }

    [Fact]
    public void OnlyOneRenderHappensPerTick()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);

        time.Advance(1);
        Assert.Equal(FrameAction.Tick, clock.Decide(time.Milliseconds));
        clock.CompleteTick();

        Assert.Equal(FrameAction.Render, clock.Decide(time.Milliseconds));
        clock.CompleteRender();

        // Without the latch this would render again, and the frame rate would run free of the tick rate.
        Assert.Equal(FrameAction.Idle, clock.Decide(time.Milliseconds));
        Assert.Equal(FrameAction.Idle, clock.Decide(time.Milliseconds));
    }

    [Fact]
    public void TheOpeningTicksMayOnlyFollowARender()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);

        time.Advance(1);
        Assert.Equal(FrameAction.Tick, clock.Decide(time.Milliseconds));
        clock.CompleteTick();

        // A long stall right after the first tick. The deadline is passed and the catch-up budget is
        // untouched, so only the tickCount >= 3 guard stops a second tick here — and it does, forcing
        // the render that the first two ticks and frames are measured around.
        time.Advance(1000);
        Assert.True(clock.TickCount < GameClock.GuardedStartupTicks);
        Assert.False(clock.HasRendered);
        Assert.Equal(FrameAction.Render, clock.Decide(time.Milliseconds));
    }

    [Fact]
    public void PastTheOpeningTicksAStallBurstsElevenTicksAndThenForcesARender()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);

        // Warm up past the startup guard on a clock that never falls behind, so the burst below starts
        // from a full catch-up budget.
        while (clock.TickCount < GameClock.GuardedStartupTicks || !clock.HasRendered)
        {
            switch (clock.Decide(time.Milliseconds))
            {
                case FrameAction.Tick: clock.CompleteTick(); break;
                case FrameAction.Render: clock.CompleteRender(); break;
                default: time.Advance(1); break;
            }
        }

        Assert.Equal(0, clock.CatchUpCount);
        time.Advance(10_000);

        int ticks = 0;
        while (clock.Decide(time.Milliseconds) == FrameAction.Tick)
        {
            clock.CompleteTick();
            ticks++;
        }

        // The budget is tested before the tick and spent after it, so the cap of 10 admits 11 ticks.
        Assert.Equal(GameClock.MaxCatchUpTicks + 1, ticks);
        Assert.Equal(FrameAction.Render, clock.Decide(time.Milliseconds));
    }

    [Fact]
    public void TheSimulationLosesTimeUnderLoadRatherThanTakingBiggerSteps()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);

        // Ten seconds of wall clock in one jump would be 300 ticks if the sim caught up fully.
        int ticks = 0;
        for (int frame = 0; frame < 100; frame++)
        {
            switch (clock.Decide(time.Milliseconds))
            {
                case FrameAction.Tick:
                    clock.CompleteTick();
                    ticks++;
                    break;
                case FrameAction.Render:
                    clock.CompleteRender();
                    break;
                default:
                    time.Advance(10_000);
                    break;
            }
        }

        Assert.True(ticks < 300, $"the simulation caught up {ticks} ticks, so time was not being lost");
    }

    [Fact]
    public void ExcludedTimePushesTheDeadlineOutByTheTimeSpent()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);
        long before = clock.DueMilliseconds;

        clock.BeginExcludedTime();
        time.Advance(5_000);
        clock.EndExcludedTime();

        // Without this a level load would leave the sim 150 ticks behind and the first frame back would
        // burst the whole catch-up budget.
        Assert.Equal(5_000, clock.ExcludedMilliseconds);
        Assert.Equal(before + 5_000, clock.DueMilliseconds);
        Assert.False(clock.IsTickDue(time.Milliseconds));
    }

    [Fact]
    public void OnlyTheOutermostExcludedSpanIsTimed()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);

        clock.BeginExcludedTime();
        time.Advance(100);
        clock.BeginExcludedTime();
        time.Advance(100);
        clock.EndExcludedTime();

        Assert.True(clock.IsExcludingTime);
        Assert.Equal(0, clock.ExcludedMilliseconds);

        time.Advance(100);
        clock.EndExcludedTime();

        Assert.False(clock.IsExcludingTime);
        Assert.Equal(300, clock.ExcludedMilliseconds);
    }

    [Fact]
    public void AnExcludedScopeClosesItselfAndToleratesADoubleDispose()
    {
        GameClock clock = Create(out ManualTimeSource time);
        clock.SetTickRate(30);

        ExcludedTimeScope scope = clock.ExcludeTime();
        time.Advance(40);
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(40, clock.ExcludedMilliseconds);
        Assert.False(clock.IsExcludingTime);
    }

    [Fact]
    public void ClosingAnExcludedSpanThatWasNeverOpenedThrows()
    {
        GameClock clock = Create(out _);

        // The original underflows its refcount here and then adds garbage to the schedule on the next
        // close. There is no behaviour worth matching, so it is a programming error instead.
        Assert.Throws<InvalidOperationException>(clock.EndExcludedTime);
    }

    [Fact]
    public void TheTimeBaseAdvancesByTheDriftEveryFrame()
    {
        GameClock clock = Create(out _);
        clock.SetTickRate(30);
        int start = clock.TimeBaseMilliseconds;

        clock.TimeAdjustMilliseconds = 3;
        clock.AdvanceTimeBase();
        clock.AdvanceTimeBase();

        Assert.Equal(start + 6, clock.TimeBaseMilliseconds);
    }

    [Fact]
    public void AnUnscheduledFrameAdvancesBothCountersAndTouchesNothingElse()
    {
        GameClock clock = Create(out _);
        clock.SetTickRate(30);
        clock.CompleteTick();
        clock.CompleteTick();

        bool renderedBefore = clock.HasRendered;
        int catchUpBefore = clock.CatchUpCount;

        clock.CompleteUnscheduledFrame();

        Assert.Equal(3, clock.TickCount);
        Assert.Equal(1, clock.FrameCount);
        Assert.Equal(renderedBefore, clock.HasRendered);
        Assert.Equal(catchUpBefore, clock.CatchUpCount);
    }

    [Fact]
    public void ARejectedTickRateIsCaughtRatherThanDividingByZero()
    {
        GameClock clock = Create(out _);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetTickRate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetTickRate(-30));
    }

    [Fact]
    public void TickFractionRisesFromTheLastDeadlineToTheNext()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 0;
        clock.SetTickRate(30);
        clock.CompleteTick();

        // Tick 0's deadline was 0 and tick 1's is 33, so the fraction is measured across that span.
        Assert.Equal(0f, clock.TickFraction(0));
        Assert.Equal(11f / 33f, clock.TickFraction(11), 5);
        Assert.Equal(22f / 33f, clock.TickFraction(22), 5);
        Assert.Equal(1f, clock.TickFraction(33));
    }

    [Fact]
    public void TickFractionClampsRatherThanExtrapolatingWhenTheNextTickIsOverdue()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 0;
        clock.SetTickRate(30);
        clock.CompleteTick();

        // Mid catch-up the honest picture is the newest completed tick, not a guess past it — and a
        // guess would also overshoot poses whose next state exists but has not been captured yet.
        Assert.Equal(1f, clock.TickFraction(500));
    }

    [Fact]
    public void TickFractionIsWholeBeforeAnyTickHasRun()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 0;
        clock.SetTickRate(30);

        // With no tick behind it there is no pair of states to draw between.
        Assert.Equal(1f, clock.TickFraction(10));
    }

    [Fact]
    public void UncappedRendersTradeIdleFramesForRendersAndNothingElse()
    {
        GameClock clock = Create(out ManualTimeSource time);
        time.Milliseconds = 0;
        clock.SetTickRate(30);
        clock.CompleteTick();
        clock.CompleteRender();

        // The original's latch: one render per tick, then idle until the next deadline passes.
        Assert.Equal(FrameAction.Idle, clock.Decide(10));

        // Lifting the latch turns exactly those idle frames into renders...
        clock.UncappedRenders = true;
        Assert.Equal(FrameAction.Render, clock.Decide(10));

        // ...while a due tick still wins the frame, exactly as scheduled.
        Assert.Equal(FrameAction.Tick, clock.Decide(34));
    }

    [Fact]
    public void AdoptingASchedulePutsTheIncomingClockExactlyWhereTheOutgoingOneWas()
    {
        // A mission started from the menu's backdrop is promoted rather than reloaded, and the loop that
        // owns it takes the session over mid-run. Its clock starts at tick zero, and a schedule that
        // restarts under a running session is not cosmetic: -lockstep derives its synthetic milliseconds
        // from FrameCount, so a reset makes wall-clock time jump backwards.
        var time = new ManualTimeSource();
        var outgoing = new GameClock(time);
        outgoing.SetTickRate(30);

        time.Milliseconds = 500;
        for (int i = 0; i < 7; i++)
        {
            outgoing.CompleteTick();
        }

        outgoing.CompleteRender();
        outgoing.TimeAdjustMilliseconds = 3;

        var incoming = new GameClock(time);
        incoming.AdoptScheduleFrom(outgoing);

        Assert.Equal(outgoing.TickRate, incoming.TickRate);
        Assert.Equal(outgoing.TickCount, incoming.TickCount);
        Assert.Equal(outgoing.FrameCount, incoming.FrameCount);
        Assert.Equal(outgoing.CatchUpCount, incoming.CatchUpCount);
        Assert.Equal(outgoing.HasRendered, incoming.HasRendered);
        Assert.Equal(outgoing.TimeBaseMilliseconds, incoming.TimeBaseMilliseconds);
        Assert.Equal(outgoing.ExcludedMilliseconds, incoming.ExcludedMilliseconds);
        Assert.Equal(outgoing.TimeAdjustMilliseconds, incoming.TimeAdjustMilliseconds);

        // The whole point: the next deadline is the one the outgoing clock was about to meet, not tick
        // zero's. Sharing the time source is what makes the adopted time base mean the same thing.
        Assert.Equal(outgoing.DueMilliseconds, incoming.DueMilliseconds);
    }

    [Fact]
    public void AdoptingAScheduleLeavesTheClockItWasTakenFromAlone()
    {
        var time = new ManualTimeSource();
        var outgoing = new GameClock(time);
        outgoing.SetTickRate(30);
        outgoing.CompleteTick();
        outgoing.CompleteTick();

        int ticks = outgoing.TickCount;
        int frames = outgoing.FrameCount;
        int timeBase = outgoing.TimeBaseMilliseconds;

        new GameClock(time).AdoptScheduleFrom(outgoing);

        Assert.Equal(ticks, outgoing.TickCount);
        Assert.Equal(frames, outgoing.FrameCount);
        Assert.Equal(timeBase, outgoing.TimeBaseMilliseconds);
    }

    private static GameClock Create(out ManualTimeSource time)
    {
        time = new ManualTimeSource();
        return new GameClock(time);
    }

    private static long[] DeadlinesForFirstTicks(GameClock clock, int count)
    {
        long[] deadlines = new long[count];
        for (int i = 0; i < count; i++)
        {
            deadlines[i] = clock.DueMilliseconds;
            clock.CompleteTick();
        }

        return deadlines;
    }
}
