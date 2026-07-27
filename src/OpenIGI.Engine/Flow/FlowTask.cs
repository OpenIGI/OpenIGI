using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;

namespace OpenIGI.Engine.Flow;

/// <summary>
/// The root task, and the frame arbiter: it decides on every frame whether to simulate, to render or to
/// do neither, and it processes the pending flow event afterwards.
/// </summary>
/// <remarks>
/// <para>
/// The original registers this as a task type called <c>Flow</c> with an instance size of 84
/// (<c>0x401FC0</c>) whose update method (<c>0x402260</c>) is the whole of the main loop body. Naming it
/// after that type rather than after "the game" is deliberate: it really is a task, it really is the
/// tree root, and its update really is the only thing <c>Game_Frame</c> calls.
/// </para>
/// <para>
/// The timing state lives in <see cref="Clock"/>; this class owns the sequencing around it. See
/// the original.
/// </para>
/// </remarks>
public sealed class FlowTask : GameTask
{
    private GameClock? _clock;
    private TaskTree? _systemTasks;
    private IFlowHost _host = NullFlowHost.Instance;
    private IFrameSuspension _suspension = FrameSuspensionState.None;
    private IRenderScheduler _renderer = NullRenderScheduler.Instance;

    /// <summary>Gets the frame schedule.</summary>
    /// <exception cref="InvalidOperationException">The task has not been initialized.</exception>
    public GameClock Clock =>
        _clock ?? throw new InvalidOperationException($"This {nameof(FlowTask)} has not been initialized.");

    /// <summary>Gets the second task tree, which updates every frame but never renders.</summary>
    /// <exception cref="InvalidOperationException">The task has not been initialized.</exception>
    /// <remarks>
    /// The original creates it unparented in the flow task's constructor and keeps it at <c>Flow+0x48</c>.
    /// It is a separate tree, not a child, which is exactly why the render pass does not reach it.
    /// </remarks>
    public TaskTree SystemTasks =>
        _systemTasks ?? throw new InvalidOperationException($"This {nameof(FlowTask)} has not been initialized.");

    /// <summary>Gets the flow state currently in effect.</summary>
    public FlowEvent CurrentEvent { get; private set; }

    /// <summary>Gets the flow event waiting to be processed at the end of this frame.</summary>
    public FlowEvent PendingEvent { get; private set; }

    /// <summary>Gets what the last completed frame did.</summary>
    public FrameOutcome LastOutcome { get; private set; }

    /// <summary>Gets the tick count published for anything outside the engine to read.</summary>
    /// <remarks>
    /// The original mirrors the tick counter into a global (<c>dword_C28C5C</c>) at the very end of the
    /// frame — so it lags <see cref="Time.GameClock.TickCount"/> by one frame, and an aborted frame does
    /// not update it at all.
    /// </remarks>
    public int PublishedTickCount { get; private set; }

    /// <summary>Requests a flow event, to be processed at the end of the current frame.</summary>
    /// <param name="request">The event to request.</param>
    /// <remarks>
    /// The original's <c>Game_RequestState</c> (<c>0x4028A0</c>) is a bare store: there is no queue and
    /// no validation, so a second request in the same frame replaces the first and the one it replaced
    /// never happens. That is reproduced here, because mission scripts can and do request twice.
    /// </remarks>
    public void Request(FlowEvent request) => PendingEvent = request;

    /// <summary>Sets the simulation rate on both the clock and the renderer.</summary>
    /// <param name="tickRate">The rate in hertz.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    /// <remarks>The pair the original's <c>Time_SetTickRate</c> (<c>0x402820</c>) performs together.</remarks>
    public void SetTickRate(int tickRate)
    {
        Clock.SetTickRate(tickRate);
        _renderer.SetTickRate(tickRate);
    }

    internal void Initialize(
        GameClock clock,
        TaskTree systemTasks,
        IFlowHost host,
        IFrameSuspension suspension,
        IRenderScheduler renderer)
    {
        _clock = clock;
        _systemTasks = systemTasks;
        _host = host;
        _suspension = suspension;
        _renderer = renderer;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One pass of the original's <c>Flow_Update</c>. The clock is sampled exactly once, at the top, and
    /// every branch shares that reading.
    /// </remarks>
    protected override void OnUpdate()
    {
        int now = Clock.Now;

        if (_suspension.IsPaused || _suspension.IsLoading || _suspension.IsCutscenePlaying)
        {
            RunSuspendedFrame();
        }
        else
        {
            RunScheduledFrame(now);
        }

        ProcessPendingEvent();

        PublishedTickCount = Clock.TickCount;
        Clock.AdvanceTimeBase();
    }

    private void RunSuspendedFrame()
    {
        // No deadline, no latch: one update and one render, every frame, for as long as the suspension
        // lasts. The render latch and the catch-up budget are deliberately left untouched, so the first
        // scheduled frame afterwards resumes from wherever the suspension interrupted.
        ForwardToChildren(TaskMessage.Update);
        SystemTasks.Update();

        bool rendered = !_suspension.IsCutscenePlaying;
        if (rendered)
        {
            ForwardToChildren(TaskMessage.Render);
        }

        Clock.CompleteUnscheduledFrame();

        // The flush comes after the walks here and before them in the scheduled tick branch. Keeping
        // that asymmetry is cheap and removes a question later.
        _renderer.Flush();
        _renderer.Reset();

        LastOutcome = rendered ? FrameOutcome.Suspended : FrameOutcome.SuspendedWithoutRender;
    }

    private void RunScheduledFrame(int now)
    {
        switch (Clock.Decide(now))
        {
            case FrameAction.Tick:
                RunTick();
                break;

            case FrameAction.Render:
                RunRender();
                break;

            default:
                LastOutcome = FrameOutcome.Idle;
                break;
        }
    }

    private void RunTick()
    {
        _renderer.Flush();
        _renderer.Reset();

        bool isStartupTick = Clock.IsStartupTick;
        if (isStartupTick)
        {
            Clock.BeginExcludedTime();
        }

        _renderer.SetFirstFrame(isStartupTick);

        ForwardToChildren(TaskMessage.Update);
        SystemTasks.Update();

        if (isStartupTick)
        {
            Clock.EndExcludedTime();
        }

        Clock.CompleteTick();
        LastOutcome = FrameOutcome.Tick;
    }

    private void RunRender()
    {
        // Evaluated once and reused, as the original does: it tests the frame counter twice but
        // increments it only afterwards, so both tests always agree.
        bool isStartupFrame = Clock.IsStartupFrame;
        if (isStartupFrame)
        {
            Clock.BeginExcludedTime();
        }

        ForwardToChildren(TaskMessage.Render);

        if (isStartupFrame)
        {
            Clock.EndExcludedTime();
        }

        Clock.CompleteRender();
        LastOutcome = FrameOutcome.Render;
    }

    private void ProcessPendingEvent()
    {
        if (PendingEvent == FlowEvent.None)
        {
            return;
        }

        // The state is assigned before the handler runs, and two of the handlers then overwrite it — so
        // the state a frame ends in is not always the event it processed.
        CurrentEvent = PendingEvent;
        PendingEvent = FlowEvent.None;

        switch (CurrentEvent)
        {
            case FlowEvent.Quit:
                throw new GameAbortException(FlowEvent.Quit);

            case FlowEvent.Intro:
                _host.ShowIntroScreens();
                break;

            case FlowEvent.MainMenu:
                _host.ShowMainMenu();
                break;

            case FlowEvent.Game:
                _host.BeginGame(this);
                SetTickRate(GameClock.DefaultTickRate);
                break;

            case FlowEvent.RestartGame:
                _host.LoadLevel();
                SetTickRate(GameClock.DefaultTickRate);
                CurrentEvent = FlowEvent.Game;
                break;

            case FlowEvent.EndGame:
                _host.EndGame();
                CurrentEvent = FlowEvent.MainMenu;
                _host.ShowMainMenu();
                break;

            case FlowEvent.Outro:
                _host.ShowOutroScreens();
                break;

            case FlowEvent.AttractScreens:
                _host.ShowAttractScreens();
                break;

            default:
                // The original switches on a raw integer and silently ignores anything outside 1..8.
                break;
        }
    }
}
