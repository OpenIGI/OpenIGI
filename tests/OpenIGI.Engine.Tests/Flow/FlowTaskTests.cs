using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Tests.Tasks;
using OpenIGI.Engine.Time;
using Xunit;

namespace OpenIGI.Engine.Tests.Flow;

/// <summary>
/// The frame arbiter and the flow state machine: what each frame does, and what the pending event does
/// to the state afterwards.
/// </summary>
public sealed class FlowTaskTests
{
    [Fact]
    public void TheOpeningFramesAlternateTickAndRender()
    {
        Fixture fixture = new();

        // Nothing on the first frame: the deadline for tick 0 is the moment the clock was rebased, and
        // the render latch that a tick-rate reset sets keeps the render branch shut until a tick runs.
        Assert.Equal(FrameOutcome.Idle, fixture.Frame());

        fixture.Time.Advance(1);
        Assert.Equal(FrameOutcome.Tick, fixture.Frame());
        Assert.Equal(FrameOutcome.Render, fixture.Frame());
        Assert.Equal(FrameOutcome.Idle, fixture.Frame());

        fixture.Time.Advance(33);
        Assert.Equal(FrameOutcome.Tick, fixture.Frame());
        Assert.Equal(FrameOutcome.Render, fixture.Frame());
    }

    [Fact]
    public void ATickUpdatesBothTreesAndRendersNeither()
    {
        Fixture fixture = new();
        WalkerTask level = fixture.AddToLevelTree("level");
        WalkerTask system = fixture.AddToSystemTree("system");

        fixture.Time.Advance(1);
        Assert.Equal(FrameOutcome.Tick, fixture.Frame());

        Assert.Equal(["level", "system"], fixture.Visited);
        Assert.Empty(fixture.Rendered);
        Assert.NotNull(level);
        Assert.NotNull(system);
    }

    [Fact]
    public void ARenderPassReachesTheLevelTreeButNeverTheSystemTree()
    {
        Fixture fixture = new();
        fixture.AddToLevelTree("level");
        fixture.AddToSystemTree("system");

        fixture.Time.Advance(1);
        fixture.Frame();
        fixture.Visited.Clear();

        Assert.Equal(FrameOutcome.Render, fixture.Frame());

        // The system tree is created unparented in the original precisely so the render walk cannot
        // reach it: the arbiter renders the root's children, and it is not one of them.
        Assert.Equal(["level"], fixture.Rendered);
        Assert.Empty(fixture.Visited);
    }

    [Fact]
    public void TheTickMessageStopsAtATaskThatDoesNotForwardIt()
    {
        Fixture fixture = new();
        WalkerTask gate = fixture.AddToLevelTree("gate");
        fixture.Add(gate, "under");
        gate.ForwardsToChildren = false;

        fixture.Time.Advance(1);
        fixture.Frame();

        // This is how Static and Idle freeze their subtrees, and how the in-game task pauses the world.
        Assert.Equal(["gate"], fixture.Visited);
    }

    [Fact]
    public void ASuspendedFrameUpdatesAndRendersOnceWithoutConsultingTheClock()
    {
        Fixture fixture = new();
        fixture.AddToLevelTree("level");
        fixture.AddToSystemTree("system");
        fixture.Suspension.IsPaused = true;

        // No time passes at all, so the scheduled path would have done nothing.
        Assert.Equal(FrameOutcome.Suspended, fixture.Frame());
        Assert.Equal(FrameOutcome.Suspended, fixture.Frame());

        Assert.Equal(["level", "system", "level", "system"], fixture.Visited);
        Assert.Equal(["level", "level"], fixture.Rendered);
        Assert.Equal(2, fixture.Loop.Clock.TickCount);
        Assert.Equal(2, fixture.Loop.Clock.FrameCount);
    }

    [Fact]
    public void ACutsceneUpdatesButDoesNotRender()
    {
        Fixture fixture = new();
        fixture.AddToLevelTree("level");
        fixture.Suspension.IsCutscenePlaying = true;

        Assert.Equal(FrameOutcome.SuspendedWithoutRender, fixture.Frame());

        Assert.Equal(["level"], fixture.Visited);
        Assert.Empty(fixture.Rendered);

        // The frame counter still advances even though nothing was drawn.
        Assert.Equal(1, fixture.Loop.Clock.FrameCount);
    }

    [Fact]
    public void ASuspendedFrameLeavesTheRenderLatchAndCatchUpBudgetUntouched()
    {
        Fixture fixture = new();
        fixture.Time.Advance(1);
        fixture.Frame();

        bool renderedBefore = fixture.Loop.Clock.HasRendered;
        int catchUpBefore = fixture.Loop.Clock.CatchUpCount;

        fixture.Suspension.IsLoading = true;
        fixture.Frame();
        fixture.Suspension.IsLoading = false;

        Assert.Equal(renderedBefore, fixture.Loop.Clock.HasRendered);
        Assert.Equal(catchUpBefore, fixture.Loop.Clock.CatchUpCount);
    }

    [Fact]
    public void APendingEventIsConsumedAtTheEndOfTheFrameThatSeesIt()
    {
        Fixture fixture = new();
        fixture.Loop.Flow.Request(FlowEvent.MainMenu);

        fixture.Frame();

        Assert.Equal(FlowEvent.MainMenu, fixture.Loop.Flow.CurrentEvent);
        Assert.Equal(FlowEvent.None, fixture.Loop.Flow.PendingEvent);
        Assert.Equal(["mainmenu"], fixture.Host.Calls);

        // Nothing happens a second time: the request is consumed, not latched.
        fixture.Frame();
        Assert.Equal(["mainmenu"], fixture.Host.Calls);
    }

    [Fact]
    public void ASecondRequestInTheSameFrameReplacesTheFirst()
    {
        Fixture fixture = new();
        fixture.Loop.Flow.Request(FlowEvent.MainMenu);
        fixture.Loop.Flow.Request(FlowEvent.Outro);

        fixture.Frame();

        // Game_RequestState is a bare store: there is no queue, so the replaced event simply never runs.
        Assert.Equal(["outro"], fixture.Host.Calls);
        Assert.Equal(FlowEvent.Outro, fixture.Loop.Flow.CurrentEvent);
    }

    [Fact]
    public void EnteringTheGameCreatesTheInGameTaskAndRestartsTheSchedule()
    {
        Fixture fixture = new();
        fixture.Time.Advance(500);
        fixture.Frame();
        fixture.Loop.Flow.Request(FlowEvent.Game);

        fixture.Frame();

        Assert.Equal(["begingame"], fixture.Host.Calls);
        Assert.Equal(FlowEvent.Game, fixture.Loop.Flow.CurrentEvent);
        Assert.Equal(0, fixture.Loop.Clock.TickCount);
        Assert.Equal(fixture.Time.Milliseconds, fixture.Loop.Clock.TimeBaseMilliseconds);
        Assert.Equal(30, fixture.Renderer.TickRate);
    }

    [Fact]
    public void LoadingALevelLandsInTheGameStateWithoutRecreatingTheInGameTask()
    {
        Fixture fixture = new();
        fixture.Loop.Flow.Request(FlowEvent.RestartGame);

        fixture.Frame();

        // The in-game task outlives a reload, so RestartGame loads and then simply declares itself to
        // be in the Game state — the Game event is never requested and BeginGame never runs.
        Assert.Equal(["loadlevel"], fixture.Host.Calls);
        Assert.Equal(FlowEvent.Game, fixture.Loop.Flow.CurrentEvent);
        Assert.Equal(FlowEvent.None, fixture.Loop.Flow.PendingEvent);
    }

    [Fact]
    public void EndingTheGameFallsStraightIntoTheMainMenu()
    {
        Fixture fixture = new();
        fixture.Loop.Flow.Request(FlowEvent.EndGame);

        fixture.Frame();

        // One frame, both effects: the original falls through the switch rather than requesting the
        // menu event, so the menu is up on the same frame the in-game task dies.
        Assert.Equal(["endgame", "mainmenu"], fixture.Host.Calls);
        Assert.Equal(FlowEvent.MainMenu, fixture.Loop.Flow.CurrentEvent);
    }

    [Fact]
    public void QuittingAbandonsTheFrameBeforeItsTail()
    {
        Fixture fixture = new();
        fixture.Time.Advance(1);
        fixture.Frame();

        int publishedBefore = fixture.Loop.Flow.PublishedTickCount;
        fixture.Loop.Clock.TimeAdjustMilliseconds = 5;
        int timeBaseBefore = fixture.Loop.Clock.TimeBaseMilliseconds;

        fixture.Loop.Flow.Request(FlowEvent.Quit);
        Assert.Equal(FrameOutcome.Aborted, fixture.Frame());

        // The abort skips exactly the tail of the arbiter — publishing the tick count and advancing the
        // time base — and nothing else, because every tree walk has already returned by then.
        Assert.Equal(publishedBefore, fixture.Loop.Flow.PublishedTickCount);
        Assert.Equal(timeBaseBefore, fixture.Loop.Clock.TimeBaseMilliseconds);
        Assert.Equal(FlowEvent.Quit, fixture.Loop.Flow.CurrentEvent);
    }

    [Fact]
    public void QuittingDoesNotStopTheHostLoopOnItsOwn()
    {
        Fixture fixture = new();
        fixture.Loop.Flow.Request(FlowEvent.Quit);

        fixture.Frame();

        // In the original the quit menu item clears the run flag first and the abort only cuts the
        // frame short. A script that requests a quit without doing so loses one frame and nothing else.
        Assert.True(fixture.Loop.IsRunning);

        fixture.Loop.RequestShutdown();
        Assert.False(fixture.Loop.IsRunning);
    }

    [Fact]
    public void ThePublishedTickCountLagsTheClockByOneFrame()
    {
        Fixture fixture = new();
        fixture.Time.Advance(1);

        // The mirror is written at the very end of the frame, after the tick has already landed.
        Assert.Equal(0, fixture.Loop.Flow.PublishedTickCount);
        fixture.Frame();
        Assert.Equal(1, fixture.Loop.Clock.TickCount);
        Assert.Equal(1, fixture.Loop.Flow.PublishedTickCount);
    }

    [Fact]
    public void TheOpeningTickIsMeasuredOutsideTheSchedule()
    {
        Fixture fixture = new();
        fixture.Time.Advance(1);

        // A task that burns 200 ms during the first tick, as loading work does.
        WalkerTask slow = fixture.AddToLevelTree("slow");
        slow.OnUpdated = () => fixture.Time.Advance(200);

        fixture.Frame();

        Assert.Equal(200, fixture.Loop.Clock.ExcludedMilliseconds);
        Assert.Equal(true, fixture.Renderer.LastFirstFrame);
    }

    [Fact]
    public void PastTheOpeningTicksTheCostOfATickCountsAgainstTheSchedule()
    {
        Fixture fixture = new();

        while (fixture.Loop.Clock.TickCount < GameClock.ExcludedStartupCount)
        {
            fixture.Time.Advance(34);
            fixture.Frame();
            fixture.Frame();
        }

        fixture.Time.Advance(34);
        int excludedBefore = fixture.Loop.Clock.ExcludedMilliseconds;
        WalkerTask slow = fixture.AddToLevelTree("slow");
        slow.OnUpdated = () => fixture.Time.Advance(200);

        Assert.Equal(FrameOutcome.Tick, fixture.Frame());

        Assert.Equal(excludedBefore, fixture.Loop.Clock.ExcludedMilliseconds);
        Assert.Equal(false, fixture.Renderer.LastFirstFrame);
    }

    [Fact]
    public void AnUnknownEventClearsThePendingSlotAndDoesNothing()
    {
        Fixture fixture = new();
        fixture.Loop.Flow.Request((FlowEvent)42);

        fixture.Frame();

        Assert.Empty(fixture.Host.Calls);
        Assert.Equal((FlowEvent)42, fixture.Loop.Flow.CurrentEvent);
        Assert.Equal(FlowEvent.None, fixture.Loop.Flow.PendingEvent);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Time = new ManualTimeSource(1_000);
            Host = new RecordingFlowHost();
            Suspension = new FrameSuspensionState();
            Renderer = new RecordingRenderScheduler();
            Loop = new GameLoop(Time, Host, Suspension, Renderer);
            WalkerType = Loop.Types.Register("Walker", () => new WalkerTask());
        }

        public ManualTimeSource Time { get; }

        public RecordingFlowHost Host { get; }

        public FrameSuspensionState Suspension { get; }

        public RecordingRenderScheduler Renderer { get; }

        public GameLoop Loop { get; }

        public TaskType WalkerType { get; }

        public List<string> Visited { get; } = [];

        public List<string> Rendered { get; } = [];

        public FrameOutcome Frame() => Loop.RunFrame();

        public WalkerTask AddToLevelTree(string tag) => Add(Loop.Tasks.Root, tag);

        public WalkerTask AddToSystemTree(string tag)
        {
            var task = (WalkerTask)Loop.SystemTasks.New(Loop.SystemTasks.Root, WalkerType);
            return Tag(task, tag);
        }

        public WalkerTask Add(GameTask parent, string tag)
        {
            var task = (WalkerTask)Loop.Tasks.New(parent, WalkerType);
            return Tag(task, tag);
        }

        private WalkerTask Tag(WalkerTask task, string tag)
        {
            task.Tag = tag;
            task.Visited = Visited;
            task.Rendered = Rendered;
            return task;
        }
    }
}
