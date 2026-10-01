using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Flow;

/// <summary>
/// The engine kernel wired together: two task trees, the script namespace, the clock and the flow task,
/// plus the frame boundary that catches an abort.
/// </summary>
/// <remarks>
/// <para>
/// This is the original's <c>Game_Init</c> (<c>0x401000</c>) and <c>Game_Frame</c> (<c>0x401050</c>).
/// Everything above it — the window, the message pump, the renderer — belongs to the platform layer and
/// only has to call <see cref="RunFrame"/>; everything below is reachable through the properties here.
/// </para>
/// <para>
/// Nothing in it touches process-wide state, so a test, a tool and a game can each have their own.
/// </para>
/// </remarks>
public sealed class GameLoop
{
    private const string LogCategory = "Flow";

    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="GameLoop"/> class.</summary>
    /// <param name="time">The wall-clock source; defaults to the real clock.</param>
    /// <param name="host">The handler for flow state transitions; defaults to one that does nothing.</param>
    /// <param name="suspension">The pause/loading/cutscene flags; defaults to none set.</param>
    /// <param name="renderer">The render scheduler; defaults to one that draws nothing.</param>
    /// <param name="logger">The logger; defaults to a sink that discards.</param>
    /// <param name="structuralTypes">
    /// The task types this title's level data spawns without declaring; defaults to
    /// <see cref="EngineTaskTypes.StructuralNames"/>, which is Project I.G.I.'s set. I.G.I. 2 declares
    /// <c>Container</c> itself and must leave it out, or the arity check rejects every level.
    /// </param>
    public GameLoop(
        ITimeSource? time = null,
        IFlowHost? host = null,
        IFrameSuspension? suspension = null,
        IRenderScheduler? renderer = null,
        IIgiLogger? logger = null,
        IReadOnlyList<string>? structuralTypes = null)
    {
        _logger = logger ?? NullLogger.Instance;
        TimeSource = time ?? new SystemTimeSource();

        Types = new TaskTypeRegistry(_logger);
        TaskType root = EngineTaskTypes.Register(Types, structuralTypes ?? EngineTaskTypes.StructuralNames);
        FlowType = Types.Register(FlowTypeName, static () => new FlowTask(), root);

        // The system tree is created first because the flow task holds it, and it is unparented in the
        // original for the same reason it is a separate tree here: the render pass must not reach it.
        SystemTasks = new TaskTree(Types, logger: _logger);

        Flow = (FlowTask)FlowType.CreateInstance();
        Tasks = new TaskTree(Types, Flow, _logger);

        Clock = new GameClock(TimeSource);
        Flow.Initialize(
            Clock,
            SystemTasks,
            host ?? NullFlowHost.Instance,
            suspension ?? FrameSuspensionState.None,
            renderer ?? NullRenderScheduler.Instance);

        // Game_CreateRoot sets the rate and then clears the three drift counters separately, which
        // Time_SetTickRate on its own does not do.
        Flow.SetTickRate(GameClock.DefaultTickRate);
        Clock.ResetDrift();

        Natives = new NativeRegistry();
        Variables = new ScriptVarRegistry(Natives);
        TaskCommands = new TaskScriptCommands(Tasks, Variables, _logger);
        TaskCommands.Register(Natives, Flow);
        FlowCommands = new FlowScriptCommands(Flow);
        FlowCommands.Register(Natives);

        // The script system registers these itself during its own init (0x4B8BC0), before any script
        // runs, so they are part of the language rather than of any one subsystem. Retail level data
        // leans on them heavily: objects.qvm faults on the very first TRUE without them.
        Natives.RegisterConstant("TRUE", 1);
        Natives.RegisterConstant("FALSE", 0);

        // The Game module's own registrar (sub_415530) adds these three beside its natives:
        // GAME_FREQUENCY = 30 through the integer registrar (sub_4B8810), WORLD_METER = 4096.0 and
        // MATH_PI = pi through the REAL registrar (sub_4B8870). WORLD_METER going unregistered meant
        // every distance test in the shipped AI scripts faulted — guard.qvm's whole COMBAT branch
        // (the combat install AND the run-to-the-alarm) died on `10 * WORLD_METER` without a sound.
        Natives.RegisterConstant("GAME_FREQUENCY", 30);
        Natives.RegisterRealConstant("WORLD_METER", 4096.0);
        Natives.RegisterRealConstant("MATH_PI", Math.PI);
    }

    /// <summary>The name the original registers the root task type under.</summary>
    public const string FlowTypeName = "Flow";

    /// <summary>Gets the wall-clock source every part of the schedule reads.</summary>
    public ITimeSource TimeSource { get; }

    /// <summary>Gets the frame schedule.</summary>
    public GameClock Clock { get; }

    /// <summary>Gets the task type registry.</summary>
    public TaskTypeRegistry Types { get; }

    /// <summary>Gets the registered <c>Flow</c> task type.</summary>
    public TaskType FlowType { get; }

    /// <summary>Gets the root task, which is also the frame arbiter.</summary>
    public FlowTask Flow { get; }

    /// <summary>Gets the main task tree, rooted at <see cref="Flow"/>.</summary>
    public TaskTree Tasks { get; }

    /// <summary>Gets the second tree, which updates every frame but never renders.</summary>
    public TaskTree SystemTasks { get; }

    /// <summary>Gets the script namespace.</summary>
    public NativeRegistry Natives { get; }

    /// <summary>Gets the registry exported task variables are published into.</summary>
    public ScriptVarRegistry Variables { get; }

    /// <summary>Gets the <c>Task_New</c> / <c>Task_DeclareParameters</c> commands.</summary>
    public TaskScriptCommands TaskCommands { get; }

    /// <summary>Gets the <c>Flow_RequestEvent</c> command.</summary>
    public FlowScriptCommands FlowCommands { get; }

    /// <summary>Gets a value indicating whether the host loop should keep running.</summary>
    /// <remarks>
    /// The original's <c>g_running</c>, which the window procedure and the quit menu item clear. A
    /// <see cref="FlowEvent.Quit"/> request deliberately does <em>not</em> clear it: in the original the
    /// menu clears the flag first and the abort only cuts the frame short, so a script that requests a
    /// quit on its own loses the rest of one frame and nothing else.
    /// </remarks>
    public bool IsRunning { get; private set; } = true;

    /// <summary>Asks the host loop to stop after the current frame.</summary>
    public void RequestShutdown()
    {
        IsRunning = false;
        _logger.Information(LogCategory, "Shutdown requested.");
    }

    /// <summary>Requests the first flow event, as the end of <c>Game_Init</c> does.</summary>
    /// <param name="showIntro">
    /// Whether to boot into the intro screens rather than straight into the game.
    /// </param>
    /// <remarks>
    /// The original picks between <see cref="FlowEvent.Intro"/> and <see cref="FlowEvent.Game"/> from
    /// <c>Win_GetStartupMode</c> (<c>0x48F220</c>), which reads a global nothing in the binary ever
    /// writes — so the retail path always boots to <see cref="FlowEvent.Game"/>. Exposing it as a
    /// parameter keeps that default while making the other branch reachable.
    /// </remarks>
    public void Boot(bool showIntro = false)
    {
        Flow.Request(showIntro ? FlowEvent.Intro : FlowEvent.Game);
        _logger.Information(LogCategory, $"Booting into {Flow.PendingEvent}.");
    }

    /// <summary>Runs one frame.</summary>
    /// <returns>What the frame did.</returns>
    /// <remarks>
    /// The frame boundary: exactly the <c>setjmp</c>/<c>longjmp</c> pair of <c>Game_Frame</c>. An abort
    /// leaves the frame's tail undone — the published tick count and the time-base advance both stay as
    /// they were — which is the whole observable effect of the original's non-local exit.
    /// </remarks>
    public FrameOutcome RunFrame()
    {
        try
        {
            Flow.Receive(TaskMessage.Update);
            return Flow.LastOutcome;
        }
        catch (GameAbortException abort)
        {
            _logger.Debug(LogCategory, $"Frame aborted by {abort.Cause}.");
            return FrameOutcome.Aborted;
        }
    }
}
