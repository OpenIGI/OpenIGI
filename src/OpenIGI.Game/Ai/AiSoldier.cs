using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Ai;

/// <summary>What an AI soldier is currently doing.</summary>
public enum AiSoldierActivity
{
    /// <summary>Standing still, waiting for its next patrol leg.</summary>
    Idle,

    /// <summary>Walking a route between graph nodes.</summary>
    Patrolling,

    /// <summary>It can see an enemy and has stopped to face it.</summary>
    Engaging,

    /// <summary>Firing at a script-named task or graph node.</summary>
    FiringAtTask,

    /// <summary>Running away from the event that installed <c>AIAction_RunPanicking</c>.</summary>
    Panicking,

    /// <summary>Holding the prone channel for <c>AIAction_FallFlat</c>.</summary>
    FallenFlat,

    /// <summary>Playing animation 38 for the scripted stun duration.</summary>
    Stunned,

    /// <summary>Dead.</summary>
    Dead,
}

/// <summary>
/// One soldier's AI runtime — the parts of <c>HumanAI</c> the port has read, joined up and driving a body.
/// </summary>
/// <remarks>
/// <para>
/// This is the integration of what the original settled: the nav graph and its precomputed route
/// table (§1), the event pump's arbitration (§3), the two view cones (§4), the detection cascade's three gates
/// (§5) and the patrol step machine and waypoint steering (§7). Every rule and constant here is read; what is
/// <b>not</b> yet read is the QVM behaviour script that decides <em>which</em> action to take, so the choice
/// between patrolling and engaging is made here directly rather than by running <c>guard.qsc</c>.
/// </para>
/// <para>
/// <b>Speed comes from the animation's own root motion</b>, which is how this engine moves a human at all
/// (M8) — so a soldier covers exactly the ground its walk clip covers, and no invented speed constant appears
/// anywhere in this class.
/// </para>
/// <para>
/// <b>What is faithful:</b> the cones and their alert-selected ranges, the detection phase gate and rate
/// limits, the 0.3 m box arrival test, the route table being followed one step at a time with no A*, the
/// walk-versus-run gait, the idle fallback, and the line-of-sight rule including its 0.79 tolerance.
/// Script events/actions, authored patrol routes, combat target sharing, and hearing are handled by the
/// same facade so they consume the same HumanAI state rather than a second coarse activity model.
/// </para>
/// </remarks>
public sealed class AiSoldier
{
    private readonly GraphFile? _graph;
    private readonly AiDetectionSchedule _schedule;
    private readonly AiDetectionSlot _playerSlot = new();
    private readonly AiMovementStep _steering = new();
    private readonly IgiRandom _random;
    private readonly List<int> _route = [];
    private readonly AiPendingActionList _pendingActions = new();
    private AiPendingAction? _activePendingAction;
    private bool _pendingRanDuringDispatch;

    /// <summary>
    /// The fraction of the way to the target at which an obstruction still counts as seen through —
    /// <c>0x489B20</c>'s literal <c>0.79</c>.
    /// </summary>
    /// <remarks>
    /// The line-of-sight test returns visible when the cast is clear <b>or</b> the blocker sits at or past
    /// this fraction of the distance. So a guard still sees you when the ray clips a corner close to you, and
    /// only a blocker in the first 79% of the line actually hides you.
    /// </remarks>
    public const double SightObstructionTolerance = 0.79000002;

    /// <summary>
    /// How far above its feet a standing human reports itself to detection — the Z offset the descriptor
    /// handler adds in <c>sub_465820</c> (0.7 m; the exact stored double, bits <c>0x40A66666E1400014</c>).
    /// </summary>
    /// <remarks>
    /// Vision does <b>not</b> test the target's eye. The candidate describes itself for the sweep
    /// (<c>sub_45FE30</c>) as its <em>position plus this raise</em> — mid-body — and the cone test, the
    /// sight ray and the stored distance all take that one point. Testing the eye let a guard acquire a
    /// player whose head cleared a sill while retail's waist-height mark stayed hidden.
    /// </remarks>
    public static readonly double DetectionRaiseStandingInUnits =
        BitConverter.Int64BitsToDouble(0x40A66666E1400014);

    /// <summary>The crouched raise — <c>sub_465820</c>'s other constant, 1305.6 units (0.319 m).</summary>
    public static readonly double DetectionRaiseCrouchedInUnits =
        BitConverter.Int64BitsToDouble(0x4094666666666666);

    /// <summary>Tests whether the line between two world points is clear enough to see along.</summary>
    /// <param name="from">The eye.</param>
    /// <param name="to">The target.</param>
    /// <returns><see langword="true"/> when the target can be seen.</returns>
    public delegate bool LineOfSight(Vector3d from, Vector3d to);

    /// <summary>Finds the ground height at a world position.</summary>
    /// <param name="x">World X, in engine units.</param>
    /// <param name="y">World Y, in engine units.</param>
    /// <param name="z">The Z to search downwards from, in engine units.</param>
    /// <param name="groundZ">Receives the ground height when found.</param>
    /// <returns><see langword="true"/> when a surface was found.</returns>
    public delegate bool GroundQuery(double x, double y, double z, out double groundZ);

    private LineOfSight? _sight;
    private Func<Vector3d>? _eyePosition;
    private Func<Matrix3f>? _eyeOrientation;
    private GroundQuery? _ground;
    private AiGuardCombat? _combat;
    private AiPatrolCombat? _patrol;
    private AiGunnerCombat? _gunnerCombat;
    private WeaponQuery? _weapon;
    private AiType _type = AiType.GuardAk;
    private AiScriptHost? _host;
    private OpenIGI.Scripting.Qvm.QvmProgram? _script;
    private OpenIGI.Scripting.Qvm.QvmProgram? _archetypeScript;

    // Where the current leg started, so the soldier's height can follow the graph along it.
    private Vector3d _legOrigin;
    private bool _created;

    /// <summary>Gives the soldier its behaviour scripts, so the script decides what it does.</summary>
    /// <param name="host">The host holding the AI natives.</param>
    /// <param name="script">The compiled script — per-task override if one exists, else the archetype.</param>
    /// <param name="archetype">
    /// The archetype script (e.g. <c>guard.qvm</c>), or <see langword="null"/> when <paramref name="script"/>
    /// IS the archetype. When the running script calls <c>AIFunction_DefaultHandler</c>, this is what runs.
    /// </param>
    /// <remarks>
    /// With a script attached the port stops choosing between patrolling and engaging: the script's own
    /// <c>if (AIFunction_GetCurrentEventType() == …)</c> chain is dispatched per event and whatever
    /// <c>AIAction_*</c> calls it makes are applied. Without one the built-in fallback runs.
    /// </remarks>
    public void SetScript(AiScriptHost host, OpenIGI.Scripting.Qvm.QvmProgram script,
        OpenIGI.Scripting.Qvm.QvmProgram? archetype = null)
    {
        _host = host;
        _script = script;
        _archetypeScript = archetype;
    }

    /// <summary>Gets a value indicating whether a behaviour script is driving this soldier.</summary>
    public bool HasScript => _host is not null && _script is not null;

    /// <summary>Gets a value indicating whether a ground query is wired up.</summary>
    public bool HasGroundQuery => _ground is not null;

    /// <summary>Gets the distance to whatever the last dispatched event was about, in engine units.</summary>
    /// <remarks>What <c>AIFunction_GetEventDistance</c> hands the archetype script.</remarks>
    public double LastEventDistance { get; private set; }

    /// <summary>Gets the actions the archetype script last asked for.</summary>
    public IReadOnlyList<AiProducedAction> LastActions { get; private set; } = [];

    /// <summary>Initializes a new instance of the <see cref="AiSoldier"/> class.</summary>
    /// <param name="graph">The nav graph this soldier walks.</param>
    /// <param name="position">Where it starts, in engine units.</param>
    /// <param name="facing">Its starting heading, in radians about Z.</param>
    /// <param name="phase">Which of the four detection phases it sweeps on — staggering the level's guards.</param>
    /// <param name="seed">The wander seed, so a level is reproducible tick for tick.</param>
    /// <param name="random">The mission-wide IGI random stream, or a seed-local stream for isolated tests.</param>
    /// <remarks>
    /// <paramref name="graph"/> may be <see langword="null"/>: a soldier whose level ships no graph for
    /// its <c>HumanAI</c> still detects and fights — nothing in the stand-and-fight arm needs a node.
    /// Only patrolling and the combat repositioning searches are graph work, and they no-op without one.
    /// </remarks>
    public AiSoldier(GraphFile? graph, Vector3d position, float facing, int phase, int seed, IgiRandom? random = null)
    {
        _graph = graph;
        Position = position;
        _legOrigin = position;
        Facing = facing;
        _random = random ?? new IgiRandom(unchecked((uint)seed));

        // The intervals are NOT the ctor's zeroes: the AI's init handler (sub_4505D0, the same pass that
        // binds the graph and loads MISSION:AI/<id>.qsc) calls sub_4896C0(ai+5624, side, 6, mask, 4, 8),
        // which stores a5/4 = 1 into the cone interval and a6/4 = 2 into the ray interval. With 0x4898D0's
        // interval+1 reload that is a cone test every SECOND sweep (8 ticks) and a sight ray every THIRD
        // (12 ticks) -- the retail acquisition latency. The earlier 0/0 reading came from the ctor path
        // alone, which really does leave the fields zeroed; the init handler overwrites them.
        _schedule = new AiDetectionSchedule(phase & 3, coneInterval: 1, rayInterval: 2);
        _playerSlot.Register(0);

        Vision = new AiVision();
        Events = new AiEventQueues { IsInitialised = true };
        foreach (AiEvent type in Enum.GetValues<AiEvent>())
        {
            Events.SetEnabled(type, true);
        }

        CurrentNode = NearestNode(position);
        DestinationNode = -1;
    }

    /// <summary>Gets the soldier's world position, in engine units.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets the soldier's heading, in radians about Z.</summary>
    public float Facing { get; private set; }

    /// <summary>Gets what it is doing.</summary>
    public AiSoldierActivity Activity { get; private set; } = AiSoldierActivity.Idle;

    /// <summary>Holds the soldier at the position the human-vs-human separation resolved to — <c>0x462BA0</c>.</summary>
    /// <param name="resolved">
    /// The end position after separation. The original refuses a move that would bring the body within
    /// <see cref="Player.HumanSeparation.RadiusInUnits"/> horizontally and
    /// <see cref="Player.HumanSeparation.HeightInUnits"/> vertically of another soldier <em>and</em> closer
    /// than where it started, so <paramref name="resolved"/> is the start when the move was refused — which
    /// is what stops two soldiers walking through each other.
    /// </param>
    public void SeparateTo(Vector3d resolved) => Position = resolved;

    /// <summary>Gets its view cones.</summary>
    public AiVision Vision { get; }

    /// <summary>
    /// Whether the human this brain calls <em>the enemy</em> — the player — is actually hostile. The retail
    /// guard never shoots a human on the same side: detection and every player-origin noise event are gated
    /// on the team comparison, so a friendly (team 0) patrol ignores the player (also team 0) instead of
    /// aiming at and firing on it. Defaults to <see langword="true"/> so brains constructed without a team
    /// (and every existing unit test) keep treating the player as an enemy.
    /// </summary>
    public bool PlayerIsEnemy { get; set; } = true;

    /// <summary>
    /// Gets the eye snapshot the last sweep tested against — <c>HumanAI+14896</c>, rebuilt on every sweep.
    /// </summary>
    public AiEyeSnapshot Eye { get; private set; }

    /// <summary>Gets its event queues.</summary>
    public AiEventQueues Events { get; }

    /// <summary>Diagnostics: the last event type this brain dispatched, or <see langword="null"/>.</summary>
    public AiEvent? LastDispatchedEvent { get; set; }

    /// <summary>Gets the live pending-action program used for event interrupt arbitration.</summary>
    public AiPendingActionList PendingActions => _pendingActions;

    /// <summary>Gets the patrol currently being walked, or <see langword="null"/> when there is none.</summary>
    public AiPatrolRoute? Route { get; private set; }

    /// <summary>Gets a value indicating whether the soldier is walking rather than running.</summary>
    /// <remarks>
    /// Three sources name the gait, mirroring the retail per-ticks that call the walk/run setters
    /// (<c>sub_4525E0</c>/<c>sub_452660</c>) every tick: an <c>AIAction_Activate</c>'s run argument
    /// (<c>0x48E7D0</c>), the patrol cursor's gait flag set by <see cref="AiPatrolCommandKind.RunTo"/>
    /// (<c>0x48E920</c>), and <c>AIAction_RunToNode</c> against <c>AIAction_WalkToNode</c>.
    /// </remarks>
    public bool IsWalking =>
        _activate is { } activating ? !activating.Run
        : Route is { } route ? route.IsWalking
        : !_scriptedRunGait;

    private bool _scriptedRunGait;

    /// <summary>Gets a value indicating whether the soldier is crouching.</summary>
    public bool IsCrouching => Route?.IsCrouching ?? false;

    /// <summary>Gets the animation the current patrol command asks for, or <c>-1</c>.</summary>
    /// <remarks>
    /// Set by <see cref="AiPatrolCommandKind.Animation"/>. The driver plays it and calls
    /// <see cref="ReportAnimationFinished"/> when the clip ends, which is what the original's
    /// <c>0x48EFE0</c> poll does for it.
    /// </remarks>
    public int RequestedAnimation { get; private set; } = -1;

    private bool _animationFinished;
    private AiPatrolPathRegistry? _paths;

    /// <summary>Gives the soldier the level's patrol paths, so <c>AIAction_Patrol</c> can resolve an id.</summary>
    /// <param name="paths">The registry, usually built from the level root.</param>
    public void SetPatrolPaths(AiPatrolPathRegistry? paths) => _paths = paths;

    /// <summary>Tells the soldier the animation it asked for has finished.</summary>
    public void ReportAnimationFinished() => _animationFinished = true;

    /// <summary>Starts walking an authored patrol path — what <c>AIAction_Patrol</c>'s first argument names.</summary>
    /// <param name="pathId">The <c>PatrolPath</c> task's script id.</param>
    /// <returns><see langword="true"/> when a path with that id exists and has a command to start on.</returns>
    public bool BeginPatrol(int pathId)
    {
        AiPatrolPath? path = _paths?.Find(pathId);
        if (path is null)
        {
            return false;
        }

        AiPatrolRoute? route = AiPatrolRoute.Start(path);
        if (route is null)
        {
            return false;
        }

        Route = route;
        RequestedAnimation = -1;
        _animationFinished = false;
        _route.Clear();
        DestinationNode = -1;
        return true;
    }

    /// <summary>Gets the graph node it was last at.</summary>
    public int CurrentNode { get; private set; }

    /// <summary>Gets the node it is heading for, or <c>-1</c>.</summary>
    public int DestinationNode { get; private set; }

    /// <summary>Gets the alert latch — set while it can see an enemy or an alarm is on.</summary>
    public bool Alerted { get; private set; }

    /// <summary>Gets a value indicating whether it can currently see its enemy.</summary>
    public bool SeesEnemy => _playerSlot.IsVisible;

    // --- State the behaviour script configures through the AI natives (HumanAI ctor 0x44F060). ---
    // Every default here is the literal the constructor writes; nothing is assumed.

    /// <summary>Gets or sets whether the soldier takes no damage at all — <c>HumanAI+15036</c>.</summary>
    /// <remarks>
    /// <c>AIFunction_SetInvulnerability</c> (<c>0x44E150</c>) stores its argument here unchanged, and the
    /// damage handler (<c>0x45D3C0</c>) skips the whole damage block when it is set. Defaults to
    /// <see langword="false"/>.
    /// </remarks>
    public bool IsInvulnerable { get; set; }

    /// <summary>
    /// Gets or sets whether the head/neck one-shot is <b>disabled</b> — <c>HumanAI+15037</c>,
    /// <c>isInstantDeathDisabled</c>.
    /// </summary>
    /// <remarks>
    /// The polarity is inverted at the source: <c>AIFunction_SetInstantDeath</c> (<c>0x44E1B0</c>) stores
    /// <c>arg == 0</c>, so <c>SetInstantDeath(FALSE)</c> sets this true. When set, a head or neck hit takes
    /// the weapon's plain damage factor instead of the ×100 lethal multiplier (<c>0x45D3C0</c>). Defaults
    /// to <see langword="false"/>, so by default a head shot is a one-shot kill.
    /// </remarks>
    public bool IsInstantDeathDisabled { get; set; }

    /// <summary>Gets or sets the id of the <c>AlarmTrigger</c> task this soldier watches — <c>HumanAI+15040</c>.</summary>
    /// <remarks><c>-1</c> until <c>AIFunction_SetAlarmTriggerID</c> (<c>0x44E270</c>) names one.</remarks>
    public int AlarmTriggerId { get; set; } = -1;

    /// <summary>Gets or sets the id of the <c>AlarmControl</c> task this soldier obeys — <c>HumanAI+15044</c>.</summary>
    /// <remarks><c>-1</c> until <c>AIFunction_SetAlarmControlID</c> (<c>0x44E2D0</c>) names one.</remarks>
    public int AlarmControlId { get; set; } = -1;

    /// <summary>Gets or sets the id of the stationary-gun task this soldier mans — <c>HumanAI+15048</c>.</summary>
    /// <remarks><c>-1</c> until <c>AIFunction_SetGunnerID</c> (<c>0x44E390</c>) names one.</remarks>
    public int GunnerId { get; set; } = -1;

    /// <summary>Gets or sets when the soldier may raise the alarm — <c>HumanAI+15052</c>.</summary>
    /// <remarks>
    /// One of <see cref="AiAlarmAccess"/> (before or after combat), stored as its integer by
    /// <c>AIFunction_SetAlarmAccess</c> (<c>0x44E330</c>). Defaults to <c>0</c>
    /// (<see cref="AiAlarmAccess.BeforeCombat"/>).
    /// </remarks>
    public int AlarmAccess { get; set; }

    /// <summary>Gets or sets the death animation the script forces — <c>HumanAI+15056</c>.</summary>
    /// <remarks>
    /// <c>-1</c> (no override) until <c>AIFunction_SetDeathAnimation</c> (<c>0x44E210</c>) sets one; the
    /// death chooser (<c>0x45C440</c>) prefers it over its shotgun/head/random selection.
    /// </remarks>
    public int DeathAnimationOverride { get; set; } = -1;

    // Which event types the running script has declared a priority for, and how many so far, so
    // AIFunction_SetEventPriority can rebuild the dispatch order in call order (0x44E0C0).
    private readonly HashSet<AiEvent> _prioritised = [];
    private int _priorityDeclared;

    // The current enemy position, captured each tick, so AIFunction_IsEventBehind can answer against it.
    private Vector3d _enemyPosition;
    private bool _enemyKnown;
    private Vector3d _enemyFeet;
    private bool _enemyCrouching;

    // Where combat BELIEVES the target is -- the shared record's position, which sub_48A920 refreshes
    // from the detection slot only while the target is actually seen. Aiming, turning, hunting and the
    // accuracy distance all read this; only the vision tests themselves see the true position. Feeding
    // the true position into combat made guards omniscient: they tracked and shot at the player's real
    // location through walls the moment anything set them fighting.
    private Vector3d _lastSeenFeet;

    // The combat record for the player is shared by every guard. The detection slot is a different
    // object: retail registers a living enemy candidate before it has ever been visible, and combat
    // tests that registration rather than an "ever seen" latch.
    private AiCombatTarget? _defaultTarget;

    // Script scratch values live in HumanAI, not in the process-wide native host. Keeping them here is
    // what prevents one guard's mission script from overwriting another guard's state.
    private readonly Dictionary<int, int> _scriptIntegers = [];
    private readonly Dictionary<int, float> _scriptReals = [];

    // The position of the event the script is being dispatched for -- what sub_44D850 copies out of
    // the queue head (record +16) for AIAction_LookAtEvent / AIAction_MoveToEvent.
    private Vector3d? _currentEventPosition;
    private int _currentEventSource;
    private Vector3d? _playerEventSourcePosition;
    private Action<int>? _responseRouter;

    /// <summary>Gets or sets the renderer-local identity stored in inter-soldier event source fields.</summary>
    public int RuntimeId { get; set; }

    /// <summary>Connects <c>AIFunction_SendResponse</c> to the other mission AIs.</summary>
    public void SetResponseRouter(Action<int>? router) => _responseRouter = router;

    // AIAction_LookAtEvent (0x44D870 -> 0x48E080 -> 0x48E0A0): turn to the event's position with the
    // instant combat turn to 2 degrees and HOLD until `duration` seconds after the behaviour's first
    // tick; complete only when both aimed and expired. Owns the tick while it runs, as a pending
    // action does.
    private Vector3d? _lookAtPoint;
    private int _lookAtDurationTicks;
    private int _lookAtDeadline;

    // AIAction_FireAtTask / FireAtNode (0x48EDE0 / 0x48EE00): aim at the named object's live
    // position for a fixed total duration. After each weapon burst the third argument is the delay
    // before InitFire is entered again; the fourth chooses visible channel reload versus direct reload.
    private ScriptedFire? _scriptedFire;

    // AIAction_RunPanicking (0x48E270 / 0x48E300 / 0x48E430 / 0x48E4A0): the first argument is
    // an escape radius in metres. The start callback chooses one graph node outside it and the run
    // callback owns the AI until that node is reached.
    private int _panicDestination = -1;

    // AIAction_FallFlat (0x48EB40 / 0x48EB60): channel 2 is held until currentTick > start+seconds*30.
    // Some archetype branches append SetCombat behind it, so that action is installed when the hold ends.
    private int _fallFlatDurationTicks;
    private int _fallFlatDeadline = -1;
    private bool _combatAfterFallFlat;

    // AIAction_Stunned (0x44D8F0 -> 0x48E130 -> 0x48E150): the real argument is converted
    // to ticks on the first run, and channel 3 is held at animation id 38 until currentTick > deadline.
    private int _stunnedDurationTicks;
    private int _stunnedDeadline = -1;

    // AIAction_PlaySound carries a 32-byte definition name and an attach flag. The audio system lives
    // above OpenIGI.Game, so the action reports the request through this seam.
    private Action<string, bool>? _scriptSound;

    // sub_48A920's arg-block byte +33: a visible player may produce one detection bark during this
    // combat action. The mission-wide 90-tick gate lives in SoldierRenderer, beside all other AIs.
    private bool _detectionShoutDone;

    /// <summary>Gets whether this tick reached IGI 1's once-per-engagement detection-shout site.</summary>
    public bool DetectionShoutRequestedThisTick { get; private set; }

    // KickGrenade only installs when the current event context owns a grenade handle. IGI1's installed
    // runner at 0x48E250 is otherwise a diagnostic nullsub and deliberately performs no movement.
    private bool _hasKickableGrenade;

    /// <summary>Connects scripted sound actions to the owning human's audio backend.</summary>
    public void SetScriptSound(Action<string, bool>? sink) => _scriptSound = sink;

    /// <summary>Publishes whether the current event carries the grenade handle KickGrenade requires.</summary>
    public void SetKickableGrenade(bool available) => _hasKickableGrenade = available;

    // AIAction_Activate (0x44D420 -> 0x48E620 / 0x48E7D0 / 0x48E870): claim the target task on the
    // level's activation list, walk to the graph node nearest it, press it on arrival (retail writes
    // the use through input channel 4 and the body's interaction machinery does the rest -- the port
    // invokes the activation callback directly, a recorded stand-in), and release the claim when the
    // action ends or the soldier dies.
    private AiActivationClaims? _claims;
    private ActivateTask? _activate;
    private int _activateTaskId = -1;
    private int _activateClaimA = -1;
    private int _activateClaimB = -1;

    /// <summary>Finds a level task for the activate behaviour.</summary>
    /// <param name="taskId">The task the script named.</param>
    /// <param name="position">Its world position.</param>
    /// <returns><see langword="false"/> when the level has no such task.</returns>
    public delegate bool TaskLocator(int taskId, out Vector3d position);

    /// <summary>Presses a level task — the end of the activate walk.</summary>
    /// <param name="taskId">The task to activate.</param>
    public delegate void TaskActivator(int taskId);

    private TaskLocator? _locateTask;
    private TaskActivator? _activateTask;

    /// <summary>Finds the door nearest a position — the walker's broadcast query.</summary>
    /// <param name="near">The current node's position, which the search is anchored at.</param>
    /// <returns>The nearest door, or <see langword="null"/> when the level has none.</returns>
    /// <remarks>
    /// <c>sub_451540</c> broadcasts message <c>sub_450CA0</c> over the task registry with the current
    /// node's position; every <c>Door</c> answers through <c>sub_446470</c>, which keeps the nearest —
    /// the best-distance sentinel starts at −1, so the first answer always wins and <b>there is no
    /// radius cap</b>. The search runs once per door hop (<c>ai+15946</c>) and the handle is dropped
    /// when the waypoint is reached.
    /// </remarks>
    public delegate DoorState? DoorQuery(Vector3d near);

    private DoorQuery? _doors;
    private DoorState? _doorAtHop;
    private bool _doorSearched;

    /// <summary>Gives the walker its door lookup.</summary>
    /// <param name="doors">The query, or <see langword="null"/> for a level without doors.</param>
    public void SetDoorQuery(DoorQuery? doors) => _doors = doors;

    /// <summary>Wires the activate behaviour to the level — the claims list and the task lookups.</summary>
    /// <param name="claims">The level-wide activation claims.</param>
    /// <param name="locate">Resolves a task id to its position.</param>
    /// <param name="activate">Presses the task.</param>
    public void SetActivateSupport(AiActivationClaims claims, TaskLocator locate, TaskActivator activate)
    {
        _claims = claims;
        _locateTask = locate;
        _activateTask = activate;
    }

    private readonly record struct ActivateTask(int TaskId, bool Run);

    private sealed class ScriptedFire
    {
        public required Func<Vector3d?> LocateTarget { get; init; }

        public required int RemainingTicks { get; set; }

        public required int BurstDelayTicks { get; init; }

        public required bool ShowReload { get; init; }

        public int DelayTicks { get; set; }

        public int BurstRoundsLeft { get; set; }

        public int PreviousAmmo { get; set; } = -1;
    }

    // The idle-animation table -- AIFunction_{SetAnimationInterval, AddAnimationEntry,
    // GetAnimationToPlay} (0x44EAF0 / 0x44EBB0 / 0x44ECB0). ai+15112 (ids, stride 16), +15120
    // (weights, double), +15204 (count, cap 5), +15192/+15196 interval min/random in ticks, +15200
    // the next-play deadline.
    private readonly List<(int Id, double Weight)> _animationEntries = [];
    private int _animationMinTicks;
    private int _animationRandomTicks;
    private int _nextAnimationTick = -1;
    private bool _scriptedAnimation;
    private int _lastTick;

    /// <summary>Diagnostics: the last tick this brain's <see cref="Tick"/> was entered.</summary>
    public int LastTick => _lastTick;

    /// <summary>Declares an event type's dispatch priority — <c>AIFunction_SetEventPriority</c> (<c>0x44E0C0</c>).</summary>
    /// <param name="type">The event type the script wants considered.</param>
    /// <remarks>
    /// The original enables the type and <b>appends</b> it to the priority order the first time it is named,
    /// building the whole order in call order. The port keeps every type enabled (it posts only a handful),
    /// so this rewrites the <em>order</em>: the first declared type goes to the front, the next behind it,
    /// and so on — which is what decides, when two events are pending on one tick, the one the script sees.
    /// </remarks>
    public void DeclareEventPriority(AiEvent type)
    {
        if (!_prioritised.Add(type))
        {
            return;
        }

        Events.SetPriority(type, Math.Min(_priorityDeclared, AiEventQueues.TypeCount - 1));
        _priorityDeclared++;
    }

    /// <summary>Whether the enemy is behind the soldier — <c>AIFunction_IsEventBehind</c> (<c>0x44E860</c>).</summary>
    /// <returns><see langword="true"/> when the enemy is behind the facing plane.</returns>
    /// <remarks>
    /// <para>
    /// The test is <c>forward · (subject − self) &lt; 0</c>, forward being the facing's <c>+Y</c> axis. The
    /// original walks the current event queue and answers for the events it holds; the port has one subject
    /// of interest, the enemy captured this tick, and answers against it — a recorded narrowing. With no
    /// enemy known yet the answer is <see langword="false"/> (treated as in front), matching the original's
    /// empty-queue return.
    /// </para>
    /// </remarks>
    public bool IsEnemyBehind()
    {
        Vector3d? point = _currentEventPosition ?? (_enemyKnown ? _enemyPosition : null);
        if (point is not { } eventOrEnemy)
        {
            return false;
        }

        double dx = eventOrEnemy.X - Position.X;
        double dy = eventOrEnemy.Y - Position.Y;
        double forwardX = -Math.Sin(Facing);
        double forwardY = Math.Cos(Facing);
        return ((forwardX * dx) + (forwardY * dy)) < 0.0;
    }

    /// <summary>Delivers a gunshot the soldier might hear — posts <see cref="AiEvent.Gunshot"/> if it is in earshot.</summary>
    /// <param name="shotPosition">Where the shot was fired, in engine units.</param>
    /// <param name="silenced">Whether the weapon is silenced, which cuts the audible range to a thirtieth.</param>
    /// <returns><see langword="true"/> when the shot was within earshot and an event was queued.</returns>
    /// <remarks>
    /// <para>
    /// The ranges are the two constants the AI module registers: <see cref="AiScriptApi.GunshotRange"/> (30 m)
    /// and <see cref="AiScriptApi.SilencedGunshotRange"/> (1 m) — a silenced weapon is worth carrying because
    /// it is heard a thirtieth as far. The event carries the shot's position, so a script can ask
    /// <c>AIFunction_GetEventDistance</c>, and it is pumped on the soldier's next tick like any other event —
    /// so a guard already busy with a higher-priority event this tick never learns of the shot, which is the
    /// original's discard-not-defer rule (<c>ai.md</c> §3.5).
    /// </para>
    /// <para>
    /// <b>Recorded as a delivery, not a reaction.</b> What a guard <em>does</em> about a heard shot is its
    /// archetype script's <c>AIEVENT_GUNSHOT</c> branch; this only puts the event where the script can see
    /// it. A projectile passing close to the head is a separate <see cref="AiEvent.GunshotMiss"/> delivery,
    /// modelled by <see cref="ReceiveGunshotMiss(Vector3d, Vector3d, Vector3d)"/>.
    /// </para>
    /// </remarks>
    public bool HearGunshot(Vector3d shotPosition, bool silenced)
        => HearGunshot(
            shotPosition,
            silenced ? AiScriptApi.SilencedGunshotRange : AiScriptApi.GunshotRange,
            fromPlayer: true);

    /// <summary>Delivers a gunshot using the weapon record's exact AI detection range.</summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    public bool HearGunshot(Vector3d shotPosition, double range)
        => HearGunshot(shotPosition, range, fromPlayer: true);

    /// <summary>
    /// Delivers a gunshot using the weapon record's exact AI detection range, distinguishing who fired it.
    /// </summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <param name="fromPlayer">
    /// Whether the player fired the round. A friendly (non-enemy) soldier is deaf to the player's own shots
    /// — they share the player's side — but must still hear a hostile AI soldier firing, so this gate applies
    /// only to player-origin gunshots.
    /// </param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    public bool HearGunshot(Vector3d shotPosition, double range, bool fromPlayer)
    {
        if (Activity == AiSoldierActivity.Dead || (fromPlayer && !PlayerIsEnemy))
        {
            return false;
        }

        range = Math.Max(0.0, range);
        double distance = Distance(Position, shotPosition);
        if (distance > range)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = shotPosition;

        // Hearing refreshes the guard's belief with the target's true position: sub_48A920's
        // else-branch runs sub_451380 over the shared record (+272) whenever the tick drained hearing
        // or alert events even though the slot is not visible. This must also run for the first shot an
        // idle guard hears; otherwise SetCombat starts it fighting the zero vector.
        if (_enemyKnown)
        {
            _lastSeenFeet = _enemyFeet;
            SetTarget(_defaultTarget);
            Target?.Refresh(_lastSeenFeet);
        }

        Events.Post(
            new AiEventRecord(AiEvent.Gunshot, -1, AiEventFilter.None, (float)range, shotPosition, -1, 0),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    /// <summary>
    /// Delivers the distinct <see cref="AiEvent.GunshotMiss"/> event when a live round passes within two
    /// metres of this human's eye.
    /// </summary>
    /// <param name="segmentStart">The beginning of the round's live segment.</param>
    /// <param name="segmentEnd">The end of the round's live segment.</param>
    /// <param name="shooterPosition">The originator's position, used by actions which inspect its range.</param>
    /// <returns><see langword="true"/> when the strict two-metre test passed and the event was posted.</returns>
    /// <remarks>
    /// IGI2's symbolised <c>GunShot_FindHearableIntersections</c> exposes the engine algorithm shared with
    /// IGI1: test the start, end, then the closest projected point on the segment, and admit only a distance
    /// <em>less than</em> 8192 units. This is not ordinary weapon hearing; that remains one
    /// <see cref="AiEvent.Gunshot"/> per fired round at the weapon record's AI detection range.
    /// </remarks>
    public bool ReceiveGunshotMiss(Vector3d segmentStart, Vector3d segmentEnd, Vector3d shooterPosition)
    {
        if (Activity == AiSoldierActivity.Dead || !PlayerIsEnemy)
        {
            return false;
        }

        Vector3d eye = EyePosition();
        Vector3d closest = ClosestPointOnSegment(segmentStart, segmentEnd, eye);
        double distance = Distance(eye, closest);
        if (distance >= GunshotMissRadiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = shooterPosition;
        if (_enemyKnown)
        {
            _lastSeenFeet = _enemyFeet;
            SetTarget(_defaultTarget);
            Target?.Refresh(_lastSeenFeet);
        }

        // -1 is the renderer's player-originator sentinel. The event position is the actual nearest point
        // on the round, while BeginFallFlat resolves the source separately to the player's live position.
        Events.Post(
            new AiEventRecord(AiEvent.GunshotMiss, -1, AiEventFilter.None, 0.0f, closest, -1, 0),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    /// <summary>The strict near-miss radius: two metres, or 8192 engine units.</summary>
    public const double GunshotMissRadiusInUnits = 2.0 * 4096.0;

    /// <summary>
    /// Delivers the impact event raised when an enemy round strikes the world within five metres.
    /// </summary>
    /// <param name="impactPosition">The actual world contact, in engine units.</param>
    /// <param name="shooterPosition">The player's live position, used by source-distance action gates.</param>
    /// <param name="radiusInUnits">
    /// The event radius. Bullet impacts use five metres; a damaging player landing uses ten.
    /// </param>
    /// <returns><see langword="true"/> when the inclusive radius test admitted the event.</returns>
    /// <remarks>
    /// A Mission 1 capture of IGI1's broadcaster at <c>0x450CE0</c> records type 11 with filter
    /// <c>0x46</c>, radius <c>5.00 m</c>, and the projectile's real contact position. This is separate from
    /// both muzzle audibility and the strict two-metre <see cref="AiEvent.GunshotMiss"/> test, so even a
    /// suppressed round wakes a guard when it hits the wall or ground beside them.
    /// </remarks>
    public bool ReceiveGroundImpact(
        Vector3d impactPosition,
        Vector3d shooterPosition,
        double radiusInUnits = GroundImpactRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || !PlayerIsEnemy || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Distance(Position, impactPosition);
        if (distance > radiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = shooterPosition;
        if (_enemyKnown)
        {
            _lastSeenFeet = _enemyFeet;
            SetTarget(_defaultTarget);
            Target?.Refresh(_lastSeenFeet);
        }

        Events.Post(
            new AiEventRecord(
                AiEvent.GroundImpact,
                -1,
                AiEventFilter.InRange | AiEventFilter.SubjectIsHuman | AiEventFilter.OtherSide,
                (float)radiusInUnits,
                impactPosition,
                -1,
                -1),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    /// <summary>The IGI1 bullet-impact reaction radius: five metres, or 20480 engine units.</summary>
    public const double GroundImpactRadiusInUnits = 5.0 * 4096.0;

    /// <summary>Delivers the footstep-noise event a walking enemy raises — <c>0x463920</c>'s tail.</summary>
    /// <param name="stepPosition">Where the foot landed — the walker's cached ground point, in units.</param>
    /// <param name="walkerPosition">The walker's live position, used by source-distance action gates.</param>
    /// <param name="radiusInUnits">How far the step carries — the ground material's own figure × 4096.</param>
    /// <returns><see langword="true"/> when the step was close enough to queue.</returns>
    /// <remarks>
    /// The retail footstep handler posts <b>type 10 (<see cref="AiEvent.Walk"/>), filter 0x46</b> with the
    /// radius the ground material names, and <b>not at all while the walker crouches</b> — the caller holds
    /// that gate, because the crouch belongs to the walker, not the listener. <c>guard.qvm</c> answers WALK
    /// with <c>AIAction_SetCombat</c>, which is how a guard investigates footsteps it cannot see.
    /// </remarks>
    public bool ReceiveWalk(Vector3d stepPosition, Vector3d walkerPosition, double radiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || !PlayerIsEnemy)
        {
            return false;
        }

        double distance = Distance(Position, stepPosition);
        if (distance > radiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = walkerPosition;
        if (_enemyKnown)
        {
            _lastSeenFeet = _enemyFeet;
            SetTarget(_defaultTarget);
            Target?.Refresh(_lastSeenFeet);
        }

        Events.Post(
            new AiEventRecord(
                AiEvent.Walk,
                -1,
                AiEventFilter.InRange | AiEventFilter.SubjectIsHuman | AiEventFilter.OtherSide,
                (float)radiusInUnits,
                stepPosition,
                -1,
                -1),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    /// <summary>Posts the type-15 event a non-lethal hit delivers to a HumanAI.</summary>
    /// <param name="attackerPosition">The attacking human's position, used by distance/behind natives.</param>
    public void ReceiveDamage(Vector3d attackerPosition)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return;
        }

        LastEventDistance = Distance(Position, attackerPosition);
        _playerEventSourcePosition = attackerPosition;
        Events.Post(
            new AiEventRecord(
                AiEvent.TakingDamage,
                -1,
                AiEventFilter.None,
                0.0f,
                attackerPosition,
                -1,
                0),
            owner: RuntimeId,
            worldTests: null);
    }

    /// <summary>The alert ping's reach — 28160 units, 6.875 m (<c>sub_48AB10</c>'s radius, float bits <c>1189085184</c>).</summary>
    public const double AlertPingRadiusInUnits = 28160.0;

    /// <summary>How often an engaged guard pings — every 20 ticks (<c>sub_48A920</c>'s <c>% 20</c> counter).</summary>
    public const int AlertPingPeriodTicks = 20;

    /// <summary>Receives a fighting neighbour's alert ping — <c>AIEVENT_ALERT</c> out of <c>sub_48AB10</c>.</summary>
    /// <param name="alerterPosition">The alerting guard's own position — the record's position field.</param>
    /// <param name="source">The alerting guard's runtime task identity, used by <c>SendResponse</c>.</param>
    /// <returns><see langword="false"/> when out of the ping's reach or dead.</returns>
    /// <remarks>
    /// The record: type 5 (<c>ALERT</c>), filter mask 38, radius 6.875 m, source = the alerting guard,
    /// subject = the target it is fighting. The distance gate is applied here, the way
    /// <see cref="HearGunshot(Vector3d, bool)"/> applies its own.
    /// </remarks>
    public bool ReceiveAlert(Vector3d alerterPosition, int source = 0)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return false;
        }

        double distance = Distance(Position, alerterPosition);
        if (distance > AlertPingRadiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;

        // The same target refresh arm handles both hearing and squad alert records. A nearby guard that
        // has never seen Jones therefore inherits the fighting guard's target record and turns on the
        // squad's current player position instead of world origin.
        if (_enemyKnown)
        {
            _lastSeenFeet = _enemyFeet;
            SetTarget(_defaultTarget);
            Target?.Refresh(_lastSeenFeet);
        }

        Events.Post(
            new AiEventRecord(
                AiEvent.Alert, -1, AiEventFilter.None, (float)AlertPingRadiusInUnits,
                alerterPosition, source, 0),
            owner: 0,
            worldTests: null);
        return true;
    }

    /// <summary>Posts the type-6 acknowledgement sent back by another guard.</summary>
    public void ReceiveAlertResponse() =>
        Events.Post(AiEventRecord.Simple(AiEvent.AlertResponse), owner: 0, worldTests: null);

    /// <summary>Implements <c>AIFunction_SendResponse</c> against the current event's source task.</summary>
    public void SendResponse()
    {
        if (_currentEventSource != 0)
        {
            _responseRouter?.Invoke(_currentEventSource);
        }
    }

    /// <summary>Cancels the active alarm-panel action when <c>guard.qsc</c> receives <c>ALARMOFF</c>.</summary>
    public void RemoveAlarmActions()
    {
        if (_activate is null || _activateTaskId != AlarmTriggerId)
        {
            return;
        }

        ReleaseActivateClaims();
        _route.Clear();
        DestinationNode = -1;
        if (Activity != AiSoldierActivity.Dead)
        {
            Activity = AiSoldierActivity.Idle;
        }
    }

    /// <summary>Gets the orientation matrix a renderer should draw it with.</summary>
    public Matrix3f Orientation => Matrix3f.FromEngineEulerAngles(0f, 0f, Facing);

    /// <summary>Diagnostic: the steering frame's Forward from the last Advance.</summary>
    public float LastSteeringForward { get; private set; } = -1f;

    /// <summary>Diagnostic: the steering outcome from the last Advance.</summary>
    public AiMovementOutcome LastSteeringOutcome { get; private set; }

    /// <summary>Diagnostic: the root-motion figure the last Advance was given.</summary>
    public double LastAdvanceRm { get; private set; } = -1.0;

    /// <summary>Diagnostic: the destination Advance last computed for the move branch.</summary>
    public Vector3d? LastMovedTo { get; private set; }

    /// <summary>Diagnostic: the facing (degrees) when Advance last moved.</summary>
    public double LastFacingDeg { get; private set; }

    /// <summary>Diagnostic: the last separation resolution (refused, from, to, resolved).</summary>
    public (bool refused, Vector3d from, Vector3d to, Vector3d resolved) LastSeparation { get; set; }

    /// <summary>Diagnostic: the move was allowed after removing same-team bodies from the blocker set.</summary>
    public bool LastSeparationFallback { get; set; }

    /// <summary>Diagnostic: the current route's first waypoint, in engine units.</summary>
    public Vector3d? DebugWaypoint =>
        _route.Count > 0 && _graph?.NodeAt(_route[0]) is { } wp ? wp.Position : null;

    /// <summary>Gives the soldier an occlusion test, so it stops seeing through walls.</summary>
    /// <param name="sight">The test, or <see langword="null"/> to see through everything.</param>
    public void SetLineOfSight(LineOfSight? sight) => _sight = sight;

    /// <summary>Connects detection and gunfire to body-rig bone 5's live world position.</summary>
    public void SetEyeTransform(Func<Vector3d>? eyePosition, Func<Matrix3f>? eyeOrientation)
    {
        _eyePosition = eyePosition;
        _eyeOrientation = eyeOrientation;
    }

    /// <summary>
    /// Reports every detection gate as it stands this instant for a target eye — for the debug overlay, so a
    /// guard that will not detect the player can be asked <em>which</em> gate is failing.
    /// </summary>
    /// <param name="enemyEye">The target eye in engine units, the same point <see cref="Tick"/> is fed.</param>
    /// <returns>The distance in units, and the cone / line-of-sight / alert / currently-visible flags.</returns>
    /// <remarks>
    /// This recomputes the cone against a fresh eye snapshot and runs the stored occlusion test directly, so
    /// it reflects the geometry now rather than whatever the rate-limited sweep last stored. The last flag is
    /// the sweep's own latched answer.
    /// </remarks>
    public (double DistanceUnits, bool InCone, bool LineOfSightClear, bool Alerted, bool IsCurrentlyVisible) ProbeDetection(Vector3d enemyEye)
    {
        AiEyeSnapshot snapshot = Vision.Capture(EyePosition(), EyeOrientation(), Alerted);
        bool inCone = snapshot.IsVisible(enemyEye);
        bool clear = _sight is null || _sight(EyePosition(), enemyEye);
        return (Distance(Position, enemyEye), inCone, clear, Alerted, _playerSlot.IsVisible);
    }

    /// <summary>
    /// Gives the soldier a ground query, so it walks on the terrain instead of interpolating between graph nodes.
    /// </summary>
    /// <param name="query">
    /// The query, or <see langword="null"/> to fall back to linear interpolation along the leg.
    /// </param>
    /// <remarks>
    /// <para>
    /// In the original the AI steers the soldier through the player's own input frame (<c>0x451540</c>),
    /// which makes the <c>Human</c> task run the same movement integrator as the player (<c>0x4614B0</c>),
    /// including its ground query (<c>0x462390</c>). So a soldier's height comes from the terrain (or
    /// whatever placed object it stands on), not from the graph.
    /// </para>
    /// <para>
    /// Probing the retail game confirms soldiers run the integrator with <c>wallEnable=0 objEnable=1
    /// humanEnable=0</c>: object collision and ground query but <b>no wall sweep</b>. The nav graph
    /// itself keeps soldiers on walkable paths; wall collision is not needed.
    /// </para>
    /// </remarks>
    public void SetGroundQuery(GroundQuery? query) => _ground = query;

    /// <summary>Reports what the soldier's held weapon looks like this tick.</summary>
    /// <returns>The clip, the weapon's own burst length and its category.</returns>
    public delegate AiCombatWeaponState WeaponQuery();

    /// <summary>Gives the soldier the combat behaviour its <c>AIType</c> selects.</summary>
    /// <param name="type">The soldier's AI type, which picks the behaviour and the channel rows.</param>
    /// <param name="parameters">Its settings row.</param>
    /// <param name="weapon">Where the held weapon's state is read from each tick.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Without this a soldier that engages only turns to face its target, which is what the port did
    /// before combat was read.
    /// </para>
    /// <para>
    /// <c>AIAction_Combat</c> dispatches by type (§11.1) and this now follows it: a <c>GUARD_*</c> type,
    /// the sniper and Priboi stand and fight, and every <c>PATROL_*</c> type — <b>most soldiers in the
    /// game</b> — gets the move-and-fight arm instead.
    /// </para>
    /// </remarks>
    public void SetCombat(AiType type, AiCombatParameters parameters, WeaponQuery weapon)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(weapon);

        _type = type;
        _weapon = weapon;
        Arm = AiCombatArms.For(type);

        // The patrol arm's own fire and reload sub-actions ARE the guard arm's - the original installs
        // 0x48D8B0 and 0x48DB10 from both - so the shared machinery is kept and only the DECISION
        // differs, which is the split ai.md 13.10 asks for.
        _combat = Arm == AiCombatArm.None
            ? null
            : new AiGuardCombat(parameters, _random.NextDouble);
        _patrol = Arm is AiCombatArm.Patrol or AiCombatArm.NamedCharacter
            ? new AiPatrolCombat(parameters, _random.NextDouble)
            : null;
        _gunnerCombat = Arm == AiCombatArm.Gunner ? new AiGunnerCombat(this) : null;

        CombatParameters = parameters;
    }

    /// <summary>Gets whether the carried inventory contains a grenade weapon.</summary>
    /// <remarks>
    /// This is <c>HumanAI+15869</c>. IGI 1's <c>0x44FBA0</c> scans every carried weapon and sets the
    /// byte when any resolved weapon type has category 4; it is not derived from the weapon in slot 0.
    /// </remarks>
    public bool HasGrenade { get; private set; }

    /// <summary>Publishes the result of the Human inventory scan to the AI.</summary>
    /// <param name="available">Whether any carried weapon is <c>WEAPONTYPE_GRENADE</c>.</param>
    public void SetGrenadeAvailable(bool available) => HasGrenade = available;

    /// <summary>Gets which combat arm this soldier's type selects.</summary>
    public AiCombatArm Arm { get; private set; } = AiCombatArm.Guard;

    /// <summary>Gets the patrol arm's state, when the soldier has one.</summary>
    public AiPatrolCombat? Patrol => _patrol;

    /// <summary>Gets whether the gunner currently owns its configured stationary gun.</summary>
    public bool IsMounted => _gunnerCombat?.IsMounted == true;

    /// <summary>Gets the stationary gun currently owned by this soldier.</summary>
    public AiStationaryGun? MountedGun => _gunnerCombat?.MountedGun;

    /// <summary>Gets what the patrol arm chose on the last tick it ran.</summary>
    public AiPatrolAction PatrolAction { get; private set; } = AiPatrolAction.LookForTarget;

    private bool _movingInCombat;
    private bool _rpgKneeling;
    private AiPatrolAction? _patrolOneShotChild;
    private AiEvasiveAction? _patrolRollChild;
    private bool _patrolProne;

    /// <summary>Gets a value indicating whether the soldier is walking a combat route this tick.</summary>
    /// <remarks>
    /// True whenever the move-and-fight arm stepped the soldier — advancing on a target, repositioning, or
    /// hunting a lost one while it watches. The renderer plays the WALK clip for it, because the walk clip's
    /// own root motion is what the next tick's <see cref="AdvanceInCombat"/> moves by; playing the fire pose
    /// instead (as the raw action would, since a hunting soldier's action is <c>LookForTarget</c> most
    /// ticks) leaves the clip with no motion and the chase never travels.
    /// </remarks>
    public bool IsMovingInCombat => _movingInCombat;

    /// <summary>Gets the soldier's combat row, once one has been given.</summary>
    public AiCombatParameters? CombatParameters { get; private set; }

    /// <summary>Gets the soldier's AI type.</summary>
    public AiType Type => _type;

    /// <summary>Gets what the combat behaviour asked for on the last tick it ran.</summary>
    public AiCombatCommand Combat { get; private set; }

    private readonly AiCombatStateMachine _combatMachine = new();

    /// <summary>Gets the soldier's combat locomotion state — the real machine, not the old inference.</summary>
    public int CombatState => _combatMachine.State;

    /// <summary>Gets whether this tick entered a new body state rather than running its per-tick callback.</summary>
    public bool CombatStateChangedThisTick => _combatMachine.ChangedThisTick;

    /// <summary>Runs one tick of the combat locomotion machine and returns the state to animate.</summary>
    /// <param name="animationDone">Whether the state's clip finished a cycle this tick — the machine's <c>0x1</c> bit.</param>
    /// <returns>The locomotion state, whose clip the renderer plays.</returns>
    /// <remarks>
    /// The fire pose and the trigger come from the combat command the arm produced this tick; the machine
    /// holds each state until its clip ends, which is what the old per-tick inference could not do and why
    /// the firing animation looped. See <see cref="AiCombatStateMachine"/>.
    /// </remarks>
    public int TickCombatState(bool animationDone) =>
        _combatMachine.Tick(new AiCombatStateMachine.Signals(
            OnGround: true,
            Flinch: false,
            Dead: Activity == AiSoldierActivity.Dead,
            FirePose: Combat.FirePose,
            FireHeld: Combat.PullTrigger,
            ThrowGrenade: WroteChannel(AiCombatChannel.ThrowGrenade),
            CloseCombat: WroteChannel(AiCombatChannel.CloseCombat),
            Kneel: WroteChannel(AiCombatChannel.Kneel),
            LieDown: WroteChannel(AiCombatChannel.LieDown),
            RollLeft: WroteRawInputSlot(2),
            RollRight: WroteRawInputSlot(3),
            ReloadPressed: WroteRawInputSlot(8),
            AnimationDone: animationDone,
            MovingForward: IsMovingInCombat));

    /// <summary>Gets which sub-state the combat behaviour is in.</summary>
    public AiCombatPhase CombatPhase => _combat?.Phase ?? AiCombatPhase.Ended;

    /// <summary>Gets how many rounds are left in the current burst — <c>HumanAI+15956</c>.</summary>
    public int RoundsLeftInBurst => _combat?.RoundsLeftInBurst ?? 0;

    /// <summary>Gets the input channels this tick asked the body for, resolved to their frame slots.</summary>
    /// <remarks>
    /// The original writes these through <c>sub_48EF70(ai, channel, value)</c>, which resolves the slot
    /// out of <c>off_54151C[aiType][channel]</c>. The port has no forty-slot input frame on a soldier
    /// yet, so it records the same writes instead — which is what lets a run of the port be compared
    /// line for line against the probe's <c>[combat] channel</c> lines from the retail game.
    /// </remarks>
    public IReadOnlyList<AiChannelWrite> ChannelWrites => _channelWrites;

    private readonly List<AiChannelWrite> _channelWrites = [];

    private void WriteChannel(AiCombatChannel channel, float value) =>
        _channelWrites.Add(new AiChannelWrite(channel, AiCombatChannels.SlotOf(_type, channel), value));

    private bool WroteChannel(AiCombatChannel channel) =>
        _channelWrites.Any(write => write.Channel == channel && write.Value != 0f);

    /// <summary>Gets direct physical input-frame writes made this tick.</summary>
    /// <remarks>Unlike <see cref="ChannelWrites"/>, these bypass the AI-type channel map.</remarks>
    public IReadOnlyList<AiRawInputWrite> RawInputWrites => _rawInputWrites;

    private readonly List<AiRawInputWrite> _rawInputWrites = [];

    private void WriteRawInputSlot(int slot, float value) =>
        _rawInputWrites.Add(new AiRawInputWrite(slot, value));

    private bool WroteRawInputSlot(int slot) =>
        _rawInputWrites.Any(write => write.Slot == slot && write.Value != 0f);

    /// <summary>Gets the hit probability the last burst set — <c>Human+1236</c>.</summary>
    /// <remarks>The weapon reads it with every round; the AI never decides a hit itself.</remarks>
    public float HitProbability { get; private set; } = 1f;

    /// <summary>Gets the shared record of everybody fighting this soldier's target.</summary>
    public AiCombatTarget? Target { get; private set; }

    /// <summary>Puts the soldier on a shared target record — <c>sub_451080</c>.</summary>
    /// <param name="target">The record, or <see langword="null"/> to leave the one it is on.</param>
    public void SetTarget(AiCombatTarget? target)
    {
        if (ReferenceEquals(Target, target))
        {
            return;
        }

        Target?.Remove(this);
        Target = target;
        target?.Add(this);
    }

    /// <summary>Gives this AI the shared record for the living player it can classify as an enemy.</summary>
    /// <param name="target">The mission-wide player target record.</param>
    public void SetDefaultTarget(AiCombatTarget? target) => _defaultTarget = target;

    /// <summary>Stores one per-HumanAI integer script scratch slot.</summary>
    public void SetScriptInteger(int index, int value) => _scriptIntegers[index] = value;

    /// <summary>Reads one per-HumanAI integer script scratch slot.</summary>
    public int GetScriptInteger(int index) => _scriptIntegers.GetValueOrDefault(index);

    /// <summary>Stores one per-HumanAI real script scratch slot.</summary>
    public void SetScriptReal(int index, float value) => _scriptReals[index] = value;

    /// <summary>Reads one per-HumanAI real script scratch slot.</summary>
    public float GetScriptReal(int index) => _scriptReals.GetValueOrDefault(index);

    /// <summary>Returns the referenced AlarmControl's live on-state.</summary>
    public bool GetAlarmControlStatus() =>
        AlarmControlId != -1 && _alarmStatus?.Invoke(AlarmControlId) == true;

    /// <summary>Returns the distance to the referenced alarm trigger, or retail's -1 failure value.</summary>
    public double GetAlarmTriggerDistance()
    {
        if (AlarmTriggerId == -1 || _locateTask is null || !_locateTask(AlarmTriggerId, out Vector3d position))
        {
            return -1.0;
        }

        return Distance(Position, position);
    }

    private Func<int, bool>? _alarmStatus;
    private Func<int, bool?>? _gunnerStatus;
    private Func<int, AiStationaryGun?>? _stationaryGun;

    /// <summary>Connects the AI natives to the level's live AlarmControl registry.</summary>
    public void SetAlarmStatusResolver(Func<int, bool>? resolver) => _alarmStatus = resolver;

    /// <summary>Connects the gunner native to StationaryGun+540's occupied flag.</summary>
    public void SetGunnerStatusResolver(Func<int, bool?>? resolver) => _gunnerStatus = resolver;

    /// <summary>Connects the gunner combat arm to the level's live stationary-gun tasks.</summary>
    public void SetStationaryGunResolver(Func<int, AiStationaryGun?>? resolver) => _stationaryGun = resolver;

    /// <summary>Returns the referenced stationary gun's occupied status, or false on lookup failure.</summary>
    public bool GetGunnerStatus() => GunnerId != -1
        && (_stationaryGun?.Invoke(GunnerId)?.IsOccupied == true || _gunnerStatus?.Invoke(GunnerId) == true);

    /// <summary>Applies whatever the archetype script asked for on the last dispatch.</summary>
    /// <param name="actions">The produced actions, in script order.</param>
    /// <remarks>
    /// The original hands one produced action to <c>0x450E70</c> and several to <c>0x450F10</c>, and in both
    /// cases the action's own start function runs. Only the movement and combat actions have anything to do
    /// here yet; the rest are recorded so a caller can see what the script wanted.
    /// </remarks>
    public void ApplyActions(IReadOnlyList<AiProducedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        LastActions = actions;

        foreach (AiProducedAction action in actions)
        {
            switch (action.Name)
            {
                case "AIAction_Patrol":
                    // The first argument is a patrol PATH id: a PatrolPath task whose child tasks are the
                    // commands (ai.md 7.1). Resolving it is the whole point of the registry.
                    BeginPatrol(action.Argument(0));
                    break;

                case "AIAction_RunToNode":
                    Route = null;
                    _scriptedRunGait = true;
                    GoTo(action.Argument(0));
                    break;

                case "AIAction_WalkToNode":
                    Route = null;
                    _scriptedRunGait = false;
                    GoTo(action.Argument(0));
                    break;

                case "AIAction_Combat":
                    if (_fallFlatDeadline >= 0 || Activity == AiSoldierActivity.FallenFlat)
                    {
                        _combatAfterFallFlat = true;
                    }
                    else
                    {
                        EnterCombat();
                    }
                    break;

                case "AIAction_SetCombat":
                    // 0x48E560 does NOT install a combat arm. It synthesizes an AIEVENT_COMBAT record
                    // carrying the triggering event's position, posts it to this AI's OWN queue
                    // (sub_450D50) and ends itself at once (sub_450FF0). The arm arrives on the next
                    // tick, when that COMBAT event dispatches and the script's own COMBAT branch runs --
                    // which is also where guard.qvm keeps its run-to-the-alarm logic, and what places
                    // AIAction_Combat in the pending list at COMBAT's own priority position so nothing
                    // ranked below it (further gunshots, footsteps) can interrupt the engagement.
                    // Treating SetCombat as a direct combat install skipped all three: every heard
                    // event re-entered combat, wiped the hunt route, and the guard never went anywhere.
                    Events.Post(
                        new AiEventRecord(
                            AiEvent.Combat,
                            -1,
                            AiEventFilter.None,
                            0.0f,
                            _currentEventPosition ?? _enemyPosition,
                            -1,
                            -1),
                        owner: RuntimeId,
                        worldTests: null);
                    break;

                case "AIAction_FallFlat":
                    BeginFallFlat(action.Real(0));
                    break;

                case "AIAction_FireAtTask":
                    BeginFireAtTask(action);
                    break;

                case "AIAction_FireAtNode":
                    BeginFireAtNode(action);
                    break;

                case "AIAction_Dead":
                    Kill();
                    break;

                case "AIAction_Idle":
                    Activity = AiSoldierActivity.Idle;
                    _fallFlatDeadline = -1;
                    _combatAfterFallFlat = false;
                    _route.Clear();
                    break;

                case "AIAction_Activate":
                    // 0x44D420: arg 0 = the task to press, arg 1 = the gait (run when set). The start
                    // (0x48E620) claims the task -- and, when it is this AI's own alarm trigger, the
                    // alarm control too -- or SKIPS the action outright when somebody already has.
                    BeginActivate(action.Argument(0), run: action.Argument(1) != 0);
                    break;

                case "AIAction_LookAtEvent":
                    // 0x44D870: copy the event's position, arg 0 = the hold in SECONDS (a real), then
                    // the behaviour turns to it (0x48E0A0, instant turn to 2 degrees) and holds until
                    // aimed AND expired. This is a guard "noticing" a shot it heard.
                    if (_currentEventPosition is { } noticed)
                    {
                        _lookAtPoint = noticed;
                        _lookAtDurationTicks = (int)(action.Real(0) * 30.0f);
                        _lookAtDeadline = -1;
                    }

                    break;

                case "AIAction_MoveToEvent":
                    // 0x44D7D0 -> 0x48DFE0: the nearest-node query (sub_5096F0) against the EVENT's
                    // position, then walk there -- a guard investigating the noise.
                    if (_currentEventPosition is { } toInvestigate)
                    {
                        int nearest = NearestNode(toInvestigate);
                        if (nearest >= 0)
                        {
                            Route = null;
                            GoTo(nearest);
                            Activity = AiSoldierActivity.Patrolling;
                        }
                    }

                    break;

                case "AIAction_RunPanicking":
                    BeginRunPanicking(action.Real(0));
                    break;

                case "AIAction_PlayAnimation":
                    // The scripted one-shot clip: the id the script chose (usually what
                    // GetAnimationToPlay returned). Cleared when the renderer reports the clip done.
                    RequestedAnimation = action.Argument(0);
                    _animationFinished = false;
                    _scriptedAnimation = true;
                    break;

                case "AIAction_PlaySound":
                    // 0x48DF80 plays once and completes the action in the same run. When the flag is
                    // set the sound is attached to Human+20; otherwise it is created globally and its
                    // position is set to HumanAI+32.
                    string sound = action.Text(0);
                    if (!string.IsNullOrEmpty(sound))
                    {
                        _scriptSound?.Invoke(sound, action.Argument(1) != 0);
                    }

                    break;

                case "AIAction_Stunned":
                    BeginStunned(action.Real(0));
                    break;

                case "AIAction_KickGrenade":
                    // The wrapper refuses to install without a grenade event handle. With one, IGI1
                    // records its nearest node and retains a no-op runner (0x48E250); preserve that
                    // dormant behavior rather than inventing a kick animation or impulse.
                    if (_hasKickableGrenade)
                    {
                        Route = null;
                        _route.Clear();
                        DestinationNode = NearestNode(Position);
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Heads for a graph node, following the route table.</summary>
    /// <param name="node">The destination node id.</param>
    /// <returns><see langword="true"/> when a route exists or the soldier is already there.</returns>
    /// <remarks>
    /// When the soldier is already at the destination (<c>CurrentNode == node</c>), the route is empty and
    /// the call succeeds — the caller should advance to its next command rather than stalling. The original
    /// handles this in <c>0x4518E0</c> where <c>from == to</c> creates a waypoint at the current position
    /// and returns.
    /// </remarks>
    public bool GoTo(int node)
    {
        if (_graph?.RouteTable is null)
        {
            return false;
        }

        if (_graph.NodeAt(node) is null)
        {
            return false;
        }

        if (CurrentNode < 0)
        {
            return false;
        }

        if (CurrentNode == node)
        {
            _route.Clear();
            DestinationNode = node;
            _legOrigin = Position;
            return true;
        }

        IReadOnlyList<int> route = _graph.RouteTable.EnumerateRoute(CurrentNode, node);
        if (route.Count == 0)
        {
            return false;
        }

        // Do not publish a destination until its route has been built. A failed combat Follow used to
        // leave DestinationNode set with an empty route; HeadFor then regarded it as already installed on
        // every later tick, stranding a visible, engaging soldier in place indefinitely.
        _route.Clear();
        _route.AddRange(route);
        DestinationNode = node;
        _legOrigin = Position;
        return true;
    }

    /// <summary>Marks the soldier dead; it stops thinking, but the body keeps being stepped.</summary>
    /// <param name="fallVelocity">
    /// The death-fall solver's corrective velocity (<c>0x45D5B0</c>), in engine units per tick. The death
    /// state's per-tick adds it to the position on every tick, which is what stops a body sliding into
    /// whatever its death clip's root motion was about to travel through.
    /// </param>
    /// <param name="facing">The heading the death turn (<c>0x45DAA0</c>) left the body at.</param>
    public void Kill(Vector3d fallVelocity = default, float? facing = null)
    {
        Activity = AiSoldierActivity.Dead;
        DeathFallVelocity = fallVelocity;
        Route = null;
        _route.Clear();
        DestinationNode = -1;
        _scriptedFire = null;
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _combatAfterFallFlat = false;
        Combat = new AiCombatCommand();
        _channelWrites.Clear();
        _rawInputWrites.Clear();
        _patrolOneShotChild = null;
        _patrolRollChild = null;
        _patrolProne = false;
        _patrolKneeling = false;
        _occupancy?.Release(this);
        _combatFreePosition = null;
        _gunnerCombat?.End();

        // Death leaves the shared target record. The original's combat action ends when its owner dies
        // and the end function (0x48D7D0) runs sub_4512A0, which swap-removes this attacker and frees
        // the record when it was the last. Without the release a dead guard kept holding its AIMING
        // slot, and once four had died mid-fight Count(Aiming) stayed >= 4 for ever -- every guard that
        // engaged afterwards was pushed to Crowded and stood there, seeing the player and doing
        // nothing, until the level was restarted.
        SetTarget(null);

        // And death frees the alarm panel: 0x44F410's cleanup releases the claim handles at
        // ai+15024/+15028, so a guard shot on its way to the alarm leaves it for the next one.
        ReleaseActivateClaims();
        if (facing is { } heading)
        {
            Facing = Wrap(heading);
        }
    }

    /// <summary>Creates the fresh HumanAI instance installed by a GuardGenerator respawn.</summary>
    /// <param name="position">The authored soldier spawn position.</param>
    /// <param name="facing">The authored soldier heading.</param>
    /// <param name="weapon">
    /// Reads the respawned brain's weapon state. The owner is supplied so renderer callbacks cannot retain
    /// the dead brain while querying mounted-gun state.
    /// </param>
    /// <returns>A new brain with the same level wiring and reset AI execution state.</returns>
    /// <remarks>
    /// IGI1's <c>0x488000</c>/<c>0x488040</c> path constructs the whole stored child subtree again. A new
    /// <see cref="AiSoldier"/> is the managed equivalent: CREATE will run again on its first tick, while
    /// immutable level services (graph, scripts, collision queries and combat row) are reattached here.
    /// The caller must assign a unique <see cref="RuntimeId"/> before registering the new brain alongside
    /// retained corpses, and rebind view-tracking callbacks to its new rendered body.
    /// </remarks>
    public AiSoldier Respawned(
        Vector3d position,
        float facing,
        Func<AiSoldier, AiCombatWeaponState>? weapon = null)
    {
        int seed = Math.Max(0, RuntimeId - 1);
        AiSoldier fresh = new(_graph, position, facing, seed & 3, seed, _random)
        {
            PlayerIsEnemy = PlayerIsEnemy,
            RuntimeId = RuntimeId,
        };

        fresh.SetLineOfSight(_sight);
        fresh.SetEyeTransform(_eyePosition, _eyeOrientation);
        fresh.SetGroundQuery(_ground);
        fresh.SetPatrolPaths(_paths);
        fresh.SetResponseRouter(_responseRouter);
        fresh.SetScriptSound(_scriptSound);
        fresh.SetDoorQuery(_doors);
        fresh.SetAlarmStatusResolver(_alarmStatus);
        fresh.SetGunnerStatusResolver(_gunnerStatus);
        fresh.SetStationaryGunResolver(_stationaryGun);
        fresh.SetDefaultTarget(_defaultTarget);
        fresh.SetNodeOccupancy(_occupancy);
        fresh.SetGrenadeAvailable(HasGrenade);

        if (_claims is not null && _locateTask is not null && _activateTask is not null)
        {
            fresh.SetActivateSupport(_claims, _locateTask, _activateTask);
        }

        if (_host is not null && _script is not null)
        {
            fresh.SetScript(_host, _script, _archetypeScript);
        }

        if (CombatParameters is not null && weapon is not null)
        {
            fresh.SetCombat(_type, CombatParameters, () => weapon(fresh));
        }

        return fresh;
    }

    /// <summary>Gets the corrective velocity the death-fall solver returned, in engine units per tick.</summary>
    public Vector3d DeathFallVelocity { get; private set; }

    /// <summary>
    /// Steps a dead body for one tick — the death state's per-tick (<c>0x45C700</c>) joined to the soldier
    /// update's ordinary root-motion move and ground query (<c>0x45D080</c>).
    /// </summary>
    /// <param name="rootMotionDelta">
    /// What the death clip's own root-motion track moved this tick, in the body's model space. The soldier
    /// update rotates exactly this into world space and hands it to the movement integrator.
    /// </param>
    /// <remarks>
    /// <para>
    /// Three things move a corpse, and all three are here: the clip's root motion, the solver's corrective
    /// velocity, and the ground query snapping the body onto whatever is under it. Nothing else does — the
    /// solver's own output is purely horizontal (its vertical component is forced to zero before the
    /// arithmetic starts), so a body <em>falls</em> because its death clip's root track goes down and the
    /// ground catches it.
    /// </para>
    /// <para>
    /// A soldier with no ground query keeps the Z its clip gives it, which is the same fallback the living
    /// path takes. When a ground service exists but has no support for a placed upper floor, the last feet
    /// height is retained and the renderer's root sample supplies the visible fall pose.
    /// </para>
    /// </remarks>
    public void TickDeadBody(Vector3f rootMotionDelta)
    {
        if (Activity != AiSoldierActivity.Dead)
        {
            return;
        }

        Vector3d local = new(rootMotionDelta.X, rootMotionDelta.Y, rootMotionDelta.Z);
        Vector3d world = SoldierDeathFall.RotateAboutZ(local, Facing);

        double x = Position.X + world.X + DeathFallVelocity.X;
        double y = Position.Y + world.Y + DeathFallVelocity.Y;
        double z = Position.Z + world.Z + DeathFallVelocity.Z;

        if (_ground is not null && _ground(x, y, z + GroundProbeAboveUnits, out double groundZ))
        {
            z = groundZ;
        }
        else if (_ground is not null)
        {
            // Some placed upper floors are visual-only in the remaster's collision index. A live guard
            // on one of those floors stays at the authored/nav height, but the death clip's descending
            // root delta used to push its Human position below that plane. The renderer then also applies
            // the clip's current root height, putting the entire corpse under the floor (Mission 4 guards
            // 417-419). When a world ground service exists but cannot find a replacement support, retain
            // the last valid feet height; the death pose still lowers the pelvis through RootSample.
            z = Position.Z;
        }

        Position = new Vector3d(x, y, z);
    }

    /// <summary>Runs one 30 Hz tick.</summary>
    /// <param name="tick">The engine tick counter, which the detection phase gate keys off.</param>
    /// <param name="enemy">The enemy's world position — the player's eye.</param>
    /// <param name="rootMotionThisTick">
    /// How far the soldier's current animation moves it this tick, in engine units along its own forward axis.
    /// The engine moves a human by its clip's root motion and nothing else, so this is the only speed source.
    /// </param>
    /// <param name="alarmOn">Whether an alarm the soldier watches is sounding.</param>
    /// <param name="alarmWasOn">Whether that alarm sounded on the preceding tick, for the falling-edge event.</param>
    /// <param name="enemyAlive">
    /// Whether the enemy is still a valid target. A dead enemy is not: the detection scan (<c>0x489750</c>)
    /// unregisters a human that reports itself invalid, so the soldier loses it and leaves combat instead
    /// of firing at the corpse. <see langword="false"/> reproduces that.
    /// </param>
    /// <param name="enemyFeet">
    /// The enemy task's position — its feet, which is what combat aims at (<c>sub_451380</c> raises it
    /// 0.5 m, 0.25 m crouched). Defaults to <paramref name="enemy"/> when the caller has no body.
    /// </param>
    /// <param name="enemyCrouching">Whether the enemy is crouching, which halves the aim raise.</param>
    /// <param name="enemyIsSoldier">
    /// Whether the fed enemy is another soldier rather than the player. The player is only a target to a
    /// hostile team, so a friendly never registers it; another soldier is a target to any soldier whose
    /// team differs from the enemy's own, regardless of the player relationship.
    /// </param>
    public void Tick(
        int tick,
        Vector3d enemy,
        double rootMotionThisTick,
        bool alarmOn = false,
        bool alarmWasOn = false,
        bool enemyAlive = true,
        Vector3d? enemyFeet = null,
        bool enemyCrouching = false,
        bool enemyIsSoldier = false)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return;
        }

        _lastTick = tick;
        _rawInputWrites.Clear();
        DetectionShoutRequestedThisTick = false;

        // A scripted one-shot clip releases the animation slot the moment the renderer says it ended,
        // so the soldier goes back to whatever its activity plays.
        if (_scriptedAnimation && _animationFinished)
        {
            RequestedAnimation = -1;
            _scriptedAnimation = false;
            _animationFinished = false;
        }

        // The idle-animation deadline (ai+15200): passing it posts AIEVENT_ANIMATION and re-arms.
        if (_nextAnimationTick >= 0 && tick >= _nextAnimationTick)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Animation), owner: 0, worldTests: null);
            _nextAnimationTick = tick + _animationMinTicks
                + NextBounded(_animationRandomTicks);
        }

        _enemyPosition = enemy;
        _enemyKnown = true;

        // Vision tests the enemy's EYE (what the cones and the sight ray are cast at), but combat aims at
        // the enemy's POSITION — its feet — because sub_451380 copies the target TASK's position and adds
        // 0.5 m standing / 0.25 m crouched. Feeding the eye into the aim put the point above the head.
        _enemyFeet = enemyFeet ?? enemy;
        _enemyCrouching = enemyCrouching;

        // An AI's FIRST event is always its creation: before initialisation only type 0 passes admission, and
        // the mask is not consulted at all (ai.md 3.3). Never posting it left every archetype script's
        // CREATE branch unrun -- which is where guard.qsc sets its view length, alarm length, alpha and
        // gamma. Guards were therefore seeing through the ENGINE DEFAULTS from 0x44F060 (+/-57.3 degrees
        // vertical, +/-59.5 horizontal, 26 m) instead of the archetype's own 40 m / 50 m / 50 / 45.
        if (!_created)
        {
            _created = true;
            Events.Post(AiEventRecord.Simple(AiEvent.Create), owner: 0, worldTests: null);
            RunScript();
        }

        bool alert = alarmOn;

        // The detection cascade in order: the phase gate, then the cone, then the line-of-sight cast behind
        // it -- the expensive test is never paid for someone outside the cone (ai.md 5.1).
        //
        // The cone test is the EYE SNAPSHOT's (0x489960), not 0x4502F0's: probing the retail game showed
        // 0x4502F0 is never called once in a whole mission, so modelling detection on it modelled the wrong
        // function (ai.md 8). The snapshot is taken here, before the alert latch is recomputed below, which
        // is where the original takes it -- hence the one-tick lag in which range applies.
        // A dead enemy is not a target. The detection scan (0x489750) asks each human to describe itself
        // and UNREGISTERS one that reports itself invalid -- a corpse -- so the AI loses it. Reproduce the
        // unregister: the slot goes not-visible and the sweep is skipped, so no detection event fires and
        // the guard stops seeing a threat where the body fell.
        // A SAME-TEAM player is equally not a target: the retail guard never engages a human on its own
        // side, so a friendly patrol must not register or fire on the player. The slot is held unregistered
        // exactly as a corpse's is, so no detection, alarm or combat ever starts for it. The one exception
        // is a fed soldier enemy: NPC-vs-NPC combat feeds a soldier of the opposing team into the slot the
        // same way the player's eye is fed, and team rivalry (not the player relationship) decides it.
        if (!enemyAlive || (!PlayerIsEnemy && !enemyIsSoldier))
        {
            _playerSlot.IsVisible = false;
            _playerSlot.IsInCone = false;
            _playerSlot.Unregister();
        }
        else
        {
            // The global detection sweep classifies/registers a living enemy candidate independently
            // of visibility. A player becoming alive again therefore recreates its slot before combat.
            if (!_playerSlot.IsRegistered)
            {
                _playerSlot.Register(tick);
            }

            if (_schedule.SweepsOn(tick))
            {
                Eye = Vision.Capture(EyePosition(), EyeOrientation(), Alerted);
                AiEyeSnapshot snapshot = Eye;

                // The point every vision test takes is the one the candidate DESCRIBES ITSELF with:
                // its position raised 0.7 m standing / 0.319 m crouched (sub_465820) — mid-body, not
                // the eye. Only when the caller supplies no body position (tests driving the brain
                // with a single point) is that point tested as given.
                Vector3d mark = enemyFeet is null
                    ? enemy
                    : new Vector3d(
                        _enemyFeet.X,
                        _enemyFeet.Y,
                        _enemyFeet.Z + (enemyCrouching
                            ? DetectionRaiseCrouchedInUnits
                            : DetectionRaiseStandingInUnits));

                _schedule.Sweep(
                    _playerSlot,
                    () => snapshot.IsVisible(mark),
                    () => _sight is null || _sight(EyePosition(), mark),
                    Distance(EyePosition(), mark));
            }
        }

        if (_playerSlot.IsVisible)
        {
            alert = true;
            LastEventDistance = _playerSlot.Distance;
            _lastSeenFeet = _enemyFeet;
            SetTarget(_defaultTarget);
            Target?.Refresh(_lastSeenFeet);
            Events.Post(AiEventRecord.Simple(AiEvent.EnemyDetection), owner: 0, worldTests: null);
        }

        if (alarmOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOn), owner: 0, worldTests: null);
        }
        else if (alarmWasOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOff), owner: 0, worldTests: null);
        }

        // The alert latch is recomputed from scratch every tick, from those two causes only (ai.md 3.1).
        Alerted = alert;

        if (_playerSlot.IsVisible)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Combat), owner: 0, worldTests: null);
        }
        else if (_route.Count == 0 && Route is null && _lookAtPoint is null && _activate is null
            && _scriptedFire is null && _panicDestination < 0
            && Activity != AiSoldierActivity.FallenFlat
            && !_pendingActions.HasPending
            && !Events.HasAnyPending)
        {
            // A running LookAtEvent is a pending ACTION too, and the pending-action list being empty is
            // the idle post's whole condition -- without this the next Idle dispatch re-ran the script
            // and re-aimed the look at the Idle record's zero position.
            // Idle is the fallback the original posts only when nothing is pending (ai.md 3.1).
            // A running patrol IS a pending action — re-posting IDLE while one is active would make
            // the script call AIAction_Patrol again, restarting the patrol from command 0 every tick.
            // And a pending EVENT suppresses it too: with Idle posted unconditionally, a standing
            // guard's Idle dispatched first every tick and the gunshot it heard was drained unheard.
            Events.Post(AiEventRecord.Simple(AiEvent.Idle), owner: 0, worldTests: null);
        }

        // ONE event type dispatches per tick, in priority order, and the script decides what to do about it.
        RunScript();

        // AIEVENT_COMBAT INSTALLS THE COMBAT ACTION, whatever the script's branch looks like: every
        // archetype answers COMBAT with AIFunction_DefaultHandler, and 0x44E060 hands the event to the
        // engine, which is what reaches AIAction_Combat (0x44D160) and installs the per-type arm.
        //
        // The port used to install it only when a script PRODUCED AIAction_Combat, which the archetype
        // scripts never do -- so only the scriptless fallback below ever armed the behaviour. Once the
        // machine ended (a targeting timeout, or a target it had no slot for), nothing re-armed it and
        // the soldier stayed Engaging with a dead machine: seeing the player, holding its gun, and
        // never firing again for the rest of the level. Re-installing here is what the original's
        // action install does on the next COMBAT event.
        if (enemyAlive && _playerSlot.IsVisible
            && Activity != AiSoldierActivity.FallenFlat
            && (Activity != AiSoldierActivity.Engaging || CombatPhase == AiCombatPhase.Ended))
        {
            EnterCombat();

            // The engagement must OCCUPY the pending-action list, exactly as retail's AIAction_Combat
            // does through 0x450E10: while it runs, the interrupt cap is COMBAT's position in the
            // priority order and only strictly-more-important events can dispatch. Installing the arm
            // outside the list left the cap unbounded, so every heard footstep or shot re-dispatched
            // the script (WALK/GUNSHOT -> SetCombat) and the re-entry tore down the chase route --
            // guards visibly never followed. The script's own COMBAT branch usually installs this
            // pending entry itself (guard.qvm calls AIAction_Combat there); this covers the re-arm
            // path and any script whose branch produced nothing.
            if (!_pendingActions.HasPending)
            {
                InstallEngineCombatAction();
            }
        }

        // The target is dead: leave combat outright rather than keep firing at where it fell. The script
        // drops out on its own once the Combat events stop, but a guard already engaging would empty its
        // magazine into the corpse for the ticks that takes -- the unregister (0x4894E0) is immediate, so
        // this is too.
        if (!enemyAlive && Activity == AiSoldierActivity.Engaging)
        {
            Combat = new AiCombatCommand();
            _combatMachine.Reset();
            _movingInCombat = false;
            LeaveCombatOccupancy();
            Activity = Route is not null || _route.Count > 0
                ? AiSoldierActivity.Patrolling
                : AiSoldierActivity.Idle;
            SetTarget(null);
        }

        if (Activity == AiSoldierActivity.Engaging)
        {
            Fight(tick, _lastSeenFeet, rootMotionThisTick);
            return;
        }

        if (Activity == AiSoldierActivity.FallenFlat)
        {
            RunFallFlat(tick);
            return;
        }

        if (Activity == AiSoldierActivity.Stunned)
        {
            RunStunned(tick);
            return;
        }

        if (_scriptedFire is not null)
        {
            Activity = AiSoldierActivity.FiringAtTask;
            RunScriptedFire();
            return;
        }

        if (_panicDestination >= 0)
        {
            if (_route.Count == 0)
            {
                _panicDestination = -1;
                Activity = AiSoldierActivity.Idle;
            }
            else
            {
                Activity = AiSoldierActivity.Panicking;
                Advance(rootMotionThisTick);
            }

            return;
        }

        // A pending LookAtEvent owns the tick until it completes: the behaviour (0x48E0A0) sets its
        // deadline on its FIRST tick, turns to the event with the instant combat turn, and completes
        // only once it is both aimed inside 2 degrees and past the deadline. Combat pre-empts it (the
        // return above); the patrol resumes when it is done.
        if (_lookAtPoint is { } watched)
        {
            if (_lookAtDeadline < 0)
            {
                _lookAtDeadline = tick + _lookAtDurationTicks;
            }

            TurnTowards(watched, (float)AiGuardCombat.AimToleranceRadians);
            if (IsFacing(watched, (float)AiGuardCombat.AimToleranceRadians) && tick > _lookAtDeadline)
            {
                _lookAtPoint = null;
            }
            else
            {
                return;
            }
        }

        // A pending Activate owns the tick the same way: walk the route to the panel's node, press on
        // arrival, release the claims. Retail's per-tick (0x48E7D0) picks the gait from the action's
        // run flag and presses through input channel 4 -- the port walks its ordinary route and
        // invokes the activation directly; both are recorded stand-ins.
        if (_activate is { } pressing)
        {
            if (_route.Count > 0)
            {
                Activity = AiSoldierActivity.Patrolling;
                Advance(rootMotionThisTick);
                return;
            }

            _activateTask?.Invoke(pressing.TaskId);
            ReleaseActivateClaims();
            Activity = AiSoldierActivity.Idle;
            return;
        }

        // An authored path takes precedence over anything this class would choose for itself.
        if (Route is not null)
        {
            RunPatrolCommand(tick, rootMotionThisTick);
            return;
        }

        if (_route.Count == 0)
        {
            // A soldier with nothing to walk STANDS STILL. The original has no wander behaviour at all:
            // where a guard goes is authored, as a PatrolPath its own MISSION:AI/<taskid>.qsc names
            // (ai.md 7.5a). Choosing a random reachable node here instead sent guards through the level to
            // places no patrol goes, and off raised positions into mid-air.
            _scriptedRunGait = false;
            Activity = AiSoldierActivity.Idle;
            return;
        }

        Activity = AiSoldierActivity.Patrolling;
        Advance(rootMotionThisTick);
    }

    /// <summary>
    /// Runs one tick of the current patrol command — the human walker <c>0x48E920</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the walker's: the crouch flag and the gait are applied first and unconditionally,
    /// because both outlive the command that set them, and only then does the switch run. A command that
    /// reports itself finished advances the cursor and clears the deadline; a kind the walker has no arm
    /// for returns <em>without</em> advancing, which is how a
    /// <see cref="AiPatrolCommandKind.SetSpeed"/> wedges a human patrol (§7.5) — reproduced rather than
    /// smoothed over.
    /// </para>
    /// </remarks>
    private void RunPatrolCommand(int tick, double rootMotionThisTick)
    {
        AiPatrolRoute route = Route!;
        if (route.Current is not { } command)
        {
            Activity = AiSoldierActivity.Idle;
            return;
        }

        bool finished = route.Begin(tick);

        switch (command.Kind)
        {
            case AiPatrolCommandKind.Animation:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = command.Operand;
                finished = _animationFinished;
                break;

            case AiPatrolCommandKind.Delay:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                break;

            case AiPatrolCommandKind.WalkTo:
            case AiPatrolCommandKind.RunTo:
                RequestedAnimation = -1;
                finished = StepTowardsNode(command.Operand, rootMotionThisTick);
                break;

            case AiPatrolCommandKind.LookAtNode:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                finished = FaceNode(command.Operand);
                break;

            case AiPatrolCommandKind.Crouch:
                // Already applied by Begin; it is the one kind that never waits.
                break;

            case AiPatrolCommandKind.Quit:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                Route = null;
                return;

            default:
                // No arm in the human walker: the cursor does not advance. SetSpeed is the only kind that
                // reaches here, and the patrol stays on it.
                Activity = AiSoldierActivity.Idle;
                return;
        }

        if (finished)
        {
            _animationFinished = false;
            route.Advance();
        }
    }

    private bool StepTowardsNode(int node, double rootMotionThisTick)
    {
        if (DestinationNode != node || _route.Count == 0)
        {
            if (!GoTo(node))
            {
                return true;
            }
        }

        if (_route.Count == 0)
        {
            return true;
        }

        Activity = AiSoldierActivity.Patrolling;
        Advance(rootMotionThisTick);
        return _route.Count == 0;
    }

    private bool FaceNode(int node)
    {
        GraphNode? target = _graph?.NodeAt(node);
        if (target is null)
        {
            return true;
        }

        TurnTowards(target.Position, AiPatrolRoute.LookAtToleranceRadians);
        return IsFacing(target.Position, AiPatrolRoute.LookAtToleranceRadians);
    }

    private bool IsFacing(Vector3d target, float tolerance)
    {
        double dx = target.X - Position.X;
        double dy = target.Y - Position.Y;
        if ((dx * dx) + (dy * dy) < 1.0)
        {
            return true;
        }

        return Math.Abs(Wrap((float)Math.Atan2(-dx, dy) - Facing)) <= tolerance;
    }

    private void RunScript()
    {
        if (_host is null || _script is null)
        {
            // No script attached: the built-in fallback decides, which is what shipped before guard.qsc ran.
            // A pending COMBAT event counts as the stimulus too -- it is what a SetCombat synthesizes
            // (0x48E560), and the engine's default answer to a dispatched COMBAT is AIAction_Combat.
            // A soldier thrown flat finishes the fall first, exactly as the scripted path defers it.
            if (_playerSlot.IsVisible || Events.PendingCount(AiEvent.Combat) > 0)
            {
                if (_fallFlatDeadline >= 0 || Activity == AiSoldierActivity.FallenFlat)
                {
                    _combatAfterFallFlat = true;
                }
                else
                {
                    EnterCombat();
                }
            }

            Drain();
            return;
        }

        _pendingRanDuringDispatch = false;
        Events.Pump(_pendingActions.PriorityCap, (type, slot) =>
        {
            // What sub_44D850 reads mid-dispatch: the current event's own position, off the queue head.
            AiEventRecord? current = Events.HeadOf(type);
            _currentEventPosition = current?.Position;
            _currentEventSource = current?.Source ?? 0;

            // AIFunction_GetEventDistance (0x44E930) measures the CURRENT queue record from the AI at
            // dispatch time. Keeping the value written when an event was posted let a later visibility
            // sweep or alert overwrite a queued gunshot's distance before guard.qvm saw it.
            if (_currentEventPosition is { } eventPosition)
            {
                LastEventDistance = Distance(Position, eventPosition);
            }

            bool handled = _host.Run(_script, this, type, slot, _archetypeScript);
            LastDispatchedEvent = type;
            if (handled)
            {
                QueueActions(_host.Produced);
            }

            _currentEventPosition = null;
            _currentEventSource = 0;
            return handled;
        });

        if (!_pendingRanDuringDispatch)
        {
            _pendingActions.Tick();
        }
    }

    private void QueueActions(IReadOnlyList<AiProducedAction> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }

        // Creation is the constructor/configuration dispatch. Retail archetypes use it for immediate
        // setters/idle setup rather than for a long-running body; keeping it out of the interrupt list
        // also preserves the engine's guarantee that the first ordinary sensed event can run after
        // initialisation even for a custom script that emits a body during CREATE.
        if (_host?.CurrentEvent == AiEvent.Create)
        {
            ApplyActions(actions);
            return;
        }

        // Reaching this path means an event ranked above the head action's cap was handled. The retail
        // TakeOne/TakeMany path invokes the current action's interrupt cleanup before installing the new
        // list. Release the resources whose owners are explicit in this facade.
        if (_pendingActions.HasPending)
        {
            InterruptCurrentAction();
            _pendingActions.Clear();
            _activePendingAction = null;
        }

        foreach (AiProducedAction produced in actions)
        {
            AiPendingAction? pending = null;
            // 0x450F10 draws and writes this 6..11 tick value before calling each +20 initializer.
            // A node with a +24 runner does not wait on it in 0x44FEC0, but consuming the draw here is
            // essential because every AI subsystem shares this random stream.
            int countdown = (int)((_random.NextDouble() + 1.0) * 0.2 * 30.0);
            pending = new AiPendingAction(produced.Slot, countdown, run: () =>
            {
                if (IsActionActive(produced))
                {
                    _activePendingAction = pending;
                }
                else
                {
                    _pendingActions.Remove(pending!);
                    if (ReferenceEquals(_activePendingAction, pending))
                    {
                        _activePendingAction = null;
                    }
                }
            });
            _pendingActions.Add(pending);

            // 0x450F10 calls every action's +20 start initializer while it links the produced list.
            // The list controls the later +24 run callbacks; it does not defer construction of action
            // two until action one completes. Retail gunshot branches therefore initialize LookAtEvent
            // and SetCombat together while the look callback owns the pending-list head.
            ApplyActions([produced]);
            if (!IsActionActive(produced))
            {
                _pendingActions.Remove(pending);
            }
        }

        LastActions = actions;

        // Several action wrappers copy the current queue head (event position/source) into their
        // private context when installed. Run the head once before dispatch clears that context.
        _pendingActions.Tick();
        _pendingRanDuringDispatch = true;
    }

    private bool IsActionActive(AiProducedAction action) => action.Name switch
    {
        "AIAction_Patrol" => Route is not null,
        // SetCombat is instant in retail (0x48E560 posts a COMBAT event and ends itself) -- it never
        // occupies the pending list. The long-running engagement is AIAction_Combat's.
        "AIAction_Combat" => Activity == AiSoldierActivity.Engaging,
        "AIAction_Dead" => Activity == AiSoldierActivity.Dead,
        "AIAction_FallFlat" => Activity == AiSoldierActivity.FallenFlat,
        "AIAction_Activate" => _activate is not null,
        "AIAction_WalkToNode" or "AIAction_RunToNode" or "AIAction_MoveToEvent" => _route.Count != 0,
        "AIAction_FireAtNode" or "AIAction_FireAtTask" => _scriptedFire is not null,
        "AIAction_PlayAnimation" => _scriptedAnimation,
        "AIAction_LookAtEvent" => _lookAtPoint is not null,
        "AIAction_Stunned" => Activity == AiSoldierActivity.Stunned,
        "AIAction_KickGrenade" => _hasKickableGrenade,
        "AIAction_RunPanicking" => _panicDestination >= 0,
        _ => false,
    };

    private void InterruptCurrentAction()
    {
        _gunnerCombat?.End();
        _scriptedRunGait = false;
        _patrolOneShotChild = null;
        _patrolRollChild = null;
        _patrolProne = false;
        _patrolKneeling = false;
        _occupancy?.Release(this);
        _combatFreePosition = null;
        _rawInputWrites.Clear();
        ReleaseActivateClaims();
        _lookAtPoint = null;
        _scriptedFire = null;
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _combatAfterFallFlat = false;
        _stunnedDeadline = -1;
        if (_scriptedAnimation || Activity == AiSoldierActivity.Stunned)
        {
            RequestedAnimation = -1;
            _scriptedAnimation = false;
        }
    }

    private void BeginFallFlat(double seconds)
    {
        if (_graph?.NodeAt(CurrentNode) is { } node
            && (node.Criteria & GraphNodeCriteria.Stair) != 0)
        {
            return;
        }

        // The wrapper skips the action when the current event names a task less than 30 m away. Event
        // records without a source task do not run that gate, exactly like the null-handle retail arm.
        if (_currentEventSource != 0
            && ResolveCurrentEventSourcePosition() is { } sourcePosition
            && Distance(Position, sourcePosition) < AiScriptApi.GunshotRange)
        {
            return;
        }

        _fallFlatDurationTicks = Math.Max(0, (int)(seconds * 30.0));
        _fallFlatDeadline = -2;
        _combatAfterFallFlat = false;
        Route = null;
        _route.Clear();
        DestinationNode = -1;
        Activity = AiSoldierActivity.FallenFlat;
    }

    private Vector3d? ResolveCurrentEventSourcePosition()
    {
        // Player-authored sensed events use -1 because the player is not one of the renderer-local soldier
        // ids. By the time an event is dispatched Tick has supplied the player's current feet position.
        if (_currentEventSource == -1 && _enemyKnown)
        {
            return _enemyFeet;
        }

        if (_currentEventSource == -1)
        {
            return _playerEventSourcePosition;
        }

        if (_locateTask is not null && _locateTask(_currentEventSource, out Vector3d position))
        {
            return position;
        }

        // Retain the old record-position fallback for synthetic/headless events with no task registry.
        return _currentEventPosition;
    }

    private void RunFallFlat(int tick)
    {
        if (_fallFlatDeadline < 0)
        {
            _fallFlatDeadline = tick + _fallFlatDurationTicks;
        }

        _channelWrites.Clear();
        WriteChannel(AiCombatChannel.LieDown, 1f);
        Combat = new AiCombatCommand();

        if (tick <= _fallFlatDeadline)
        {
            return;
        }

        _fallFlatDeadline = -1;
        Activity = AiSoldierActivity.Idle;
        if (_combatAfterFallFlat)
        {
            _combatAfterFallFlat = false;
            EnterCombat();
        }
    }

    private void BeginStunned(double seconds)
    {
        _stunnedDurationTicks = Math.Max(0, (int)(seconds * 30.0));
        _stunnedDeadline = -1;
        _scriptedFire = null;
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _combatAfterFallFlat = false;
        SetTarget(null);
        Route = null;
        _route.Clear();
        DestinationNode = -1;
        RequestedAnimation = 38;
        Activity = AiSoldierActivity.Stunned;
    }

    private void RunStunned(int tick)
    {
        if (_stunnedDeadline < 0)
        {
            _stunnedDeadline = tick + _stunnedDurationTicks;
        }

        _channelWrites.Clear();
        Combat = new AiCombatCommand();
        if (tick <= _stunnedDeadline)
        {
            WriteChannel(AiCombatChannel.PlayAnimation, 38.0f);
            return;
        }

        _stunnedDeadline = -1;
        RequestedAnimation = -1;
        Activity = AiSoldierActivity.Idle;
    }

    private void BeginFireAtTask(AiProducedAction action)
    {
        int taskId = action.Argument(0);
        BeginScriptedFire(
            () => _locateTask is not null && _locateTask(taskId, out Vector3d position) ? position : null,
            action);
    }

    private void BeginFireAtNode(AiProducedAction action)
    {
        int nodeId = action.Argument(0);
        BeginScriptedFire(() => _graph?.NodeAt(nodeId)?.Position, action);
    }

    private void BeginScriptedFire(Func<Vector3d?> locateTarget, AiProducedAction action)
    {
        if (locateTarget() is null)
        {
            return;
        }

        _scriptedFire = new ScriptedFire
        {
            LocateTarget = locateTarget,
            RemainingTicks = (int)(action.Real(1) * 30.0f),
            BurstDelayTicks = Math.Max(0, (int)(action.Real(2) * 30.0f)),
            ShowReload = action.Argument(3) != 0,
        };
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _combatAfterFallFlat = false;
        SetTarget(null);
        _combatMachine.Reset();
        Route = null;
        _route.Clear();
        DestinationNode = -1;
        Activity = AiSoldierActivity.FiringAtTask;
    }

    private void RunScriptedFire()
    {
        ScriptedFire firing = _scriptedFire!;
        if (firing.RemainingTicks-- < 0 || firing.LocateTarget() is not { } target || _weapon is null)
        {
            EndScriptedFire();
            return;
        }

        AiCombatWeaponState weapon = _weapon();
        if (firing.PreviousAmmo >= 0 && weapon.AmmoInClip < firing.PreviousAmmo)
        {
            firing.BurstRoundsLeft -= firing.PreviousAmmo - weapon.AmmoInClip;
            if (firing.BurstRoundsLeft <= 0)
            {
                firing.BurstRoundsLeft = 0;
                firing.DelayTicks = firing.BurstDelayTicks;
            }
        }

        firing.PreviousAmmo = weapon.AmmoInClip;
        _channelWrites.Clear();
        Combat = new AiCombatCommand { TurnTo = target };
        TurnTowards(target, (float)AiGuardCombat.AimToleranceRadians);

        if (weapon.AmmoInClip <= 0)
        {
            Combat = firing.ShowReload
                ? new AiCombatCommand { TurnTo = target, PressReload = true }
                : new AiCombatCommand { TurnTo = target, ReloadWeaponDirectly = true };
            if (firing.ShowReload)
            {
                WriteRawInputSlot(8, 1f);
            }

            return;
        }

        if (firing.DelayTicks > 0)
        {
            firing.DelayTicks--;
            return;
        }

        if (firing.BurstRoundsLeft <= 0)
        {
            firing.BurstRoundsLeft = Math.Max(1, weapon.BurstSize);
        }

        bool aimed = IsFacing(target, (float)AiGuardCombat.AimToleranceRadians);
        Combat = new AiCombatCommand
        {
            TurnTo = target,
            Aimed = aimed,
            FirePose = true,
            PullTrigger = aimed,
        };
        WriteChannel(AiCombatChannel.Fire, 1f);
    }

    private void EndScriptedFire()
    {
        _scriptedFire = null;
        Combat = new AiCombatCommand();
        _channelWrites.Clear();
        if (Activity == AiSoldierActivity.FiringAtTask)
        {
            Activity = AiSoldierActivity.Idle;
        }
    }

    private void BeginRunPanicking(double distanceMetres)
    {
        if (_graph is null || _currentEventPosition is not { } eventPosition)
        {
            return;
        }

        int? destination = AiRunPanicking.FindDestination(
            _graph, Position, eventPosition, distanceMetres);
        if (destination is null || !GoTo(destination.Value))
        {
            return;
        }

        EndScriptedFire();
        SetTarget(null);
        _combatMachine.Reset();
        _fallFlatDeadline = -1;
        _combatAfterFallFlat = false;
        _panicDestination = destination.Value;
        Route = null;
        Activity = AiSoldierActivity.Panicking;
    }

    private void BeginActivate(int taskId, bool run)
    {
        if (_claims is null || _locateTask is null)
        {
            return;
        }

        // Already claimed by somebody -- or, for this AI's own alarm trigger, the CONTROL claimed --
        // means the action never starts (0x48E620's early exits).
        if (_claims.Contains(taskId))
        {
            return;
        }

        bool isMyAlarmTrigger = taskId == AlarmTriggerId && AlarmTriggerId != -1;
        if (!_locateTask(taskId, out Vector3d position))
        {
            // "Level QTask ID #%d not defined (AI Activate)" -- the original warns and gives up.
            return;
        }

        int node = NearestNode(position);
        if (node < 0)
        {
            return;
        }

        _claims.Claim(taskId);
        _activateClaimA = taskId;
        if (isMyAlarmTrigger && AlarmControlId != -1)
        {
            _claims.Claim(AlarmControlId);
            _activateClaimB = AlarmControlId;
        }

        Route = null;
        GoTo(node);
        _activate = new ActivateTask(taskId, run);
        _activateTaskId = taskId;
        Activity = AiSoldierActivity.Patrolling;
    }

    private void ReleaseActivateClaims()
    {
        if (_activateClaimA != -1)
        {
            _claims?.Release(_activateClaimA);
            _activateClaimA = -1;
        }

        if (_activateClaimB != -1)
        {
            _claims?.Release(_activateClaimB);
            _activateClaimB = -1;
        }

        _activate = null;
        _activateTaskId = -1;
    }

    /// <summary>Arms the idle-animation cadence — <c>AIFunction_SetAnimationInterval</c> (<c>0x44EAF0</c>).</summary>
    /// <param name="minimumSeconds">The floor of the gap between idle animations.</param>
    /// <param name="randomSeconds">The random span on top of it.</param>
    public void SetAnimationInterval(float minimumSeconds, float randomSeconds)
    {
        _animationMinTicks = (int)(minimumSeconds * 30.0);
        _animationRandomTicks = (int)(randomSeconds * 30.0);
        _nextAnimationTick = _lastTick + _animationMinTicks
            + NextBounded(_animationRandomTicks);
    }

    /// <summary>Adds an idle animation — <c>AIFunction_AddAnimationEntry</c> (<c>0x44EBB0</c>), capped at five.</summary>
    /// <param name="animationId">The clip to add.</param>
    /// <param name="weight">Its share of the weighted pick.</param>
    public void AddAnimationEntry(int animationId, double weight)
    {
        if (_animationEntries.Count < 5)
        {
            _animationEntries.Add((animationId, weight));
        }
    }

    /// <summary>Picks an idle animation — <c>AIFunction_GetAnimationToPlay</c> (<c>0x44ECB0</c>).</summary>
    /// <returns>An entry's id, or −1 with no entries.</returns>
    /// <remarks>
    /// Weights summing under 0.01 take a uniformly random entry; otherwise the walk subtracts weights
    /// from <c>rand01 × total</c> until the remainder crosses zero, and walking off the end takes the
    /// last entry — exactly the original's arithmetic, edge for edge.
    /// </remarks>
    public int PickAnimationToPlay()
    {
        if (_animationEntries.Count == 0)
        {
            return -1;
        }

        double total = 0.0;
        foreach ((_, double weight) in _animationEntries)
        {
            total += weight;
        }

        if (total < 0.01)
        {
            return _animationEntries[NextBounded(_animationEntries.Count)].Id;
        }

        double remaining = _random.NextDouble() * total;
        foreach ((int id, double weight) in _animationEntries)
        {
            remaining -= weight;
            if (remaining <= 0.0)
            {
                return id;
            }
        }

        return _animationEntries[^1].Id;
    }

    /// <summary>The eye the cones are measured from — body-rig bone 5 when a live rig is attached.</summary>
    /// <returns>The eye position.</returns>
    /// <remarks>
    /// IGI1 snapshots palette offsets 684/688/692, which resolve to bone 5's position. Headless tests and
    /// callers without a rig fall back to the separation cylinder's read half-height.
    /// </remarks>
    public Vector3d EyePosition() => _eyePosition?.Invoke()
        ?? new Vector3d(Position.X, Position.Y, Position.Z + HumanSeparationEyeHeight);

    private Matrix3f EyeOrientation() => _eyeOrientation?.Invoke() ?? Orientation;

    /// <summary>The eye height fallback for a soldier with no attached body rig.</summary>
    public const double HumanSeparationEyeHeight = 1.8 * 4096.0;

    /// <summary>How far above the soldier's feet the ground probe starts — 0.5 m.</summary>
    private const double GroundProbeAboveUnits = 2048.0;

    /// <summary>How far below the soldier's feet the ground probe reaches — 2 m (the sliding step-down budget).</summary>
    private const double GroundProbeBelowUnits = 8192.0;

    private void Drain()
    {
        // One event type dispatches per tick and every queue is then emptied, so an event nothing acted on is
        // discarded rather than deferred (ai.md 3.5).
        Events.Pump(AiEventQueues.UnboundedPriorityCap, (_, _) => true);
    }

    private bool ChooseNextLeg()
    {
        if (_graph?.RouteTable is null || _graph.Nodes.Count < 2)
        {
            return false;
        }

        if (CurrentNode < 0)
        {
            CurrentNode = NearestNode(Position);
            if (CurrentNode < 0)
            {
                return false;
            }
        }

        // Wander: pick a reachable node that is not where we already are, then follow the table to it.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            GraphNode candidate = _graph.Nodes[NextBounded(_graph.Nodes.Count)];
            if (candidate.Id == CurrentNode || candidate.IsExcludedFromNearestQuery)
            {
                continue;
            }

            IReadOnlyList<int> route = _graph.RouteTable.EnumerateRoute(CurrentNode, candidate.Id);
            if (route.Count == 0)
            {
                continue;
            }

            _route.Clear();
            _route.AddRange(route);
            DestinationNode = candidate.Id;
            return true;
        }

        return false;
    }

    private void Advance(double rootMotionThisTick)
    {
        GraphNode? next = _graph?.NodeAt(_route[0]);
        if (next is null)
        {
            _route.RemoveAt(0);
            return;
        }

        Vector3d waypoint = next.Position;

        // A DOOR hop: the planner (sub_4518E0) flags a queue item when BOTH ends of the hop carry the
        // door criteria bit, and the walker (sub_451540) then finds the nearest door once per hop and
        // acts on its state: 1 (closed) and 3 (closing) -> OPEN IT YOURSELF (the same sub_447DA0 the
        // player's use calls) and hold; 2 (in motion) -> "Waiting for door to open", hold; 0 (open) ->
        // walk through. Holding turns to the waypoint at the 2-degree door tolerance.
        if (_doors is not null
            && _graph?.NodeAt(CurrentNode) is { } from
            && (from.Criteria & GraphNodeCriteria.Door) != 0
            && (next.Criteria & GraphNodeCriteria.Door) != 0)
        {
            if (!_doorSearched)
            {
                _doorAtHop = _doors(from.Position);
                _doorSearched = true;
            }

            if (_doorAtHop is { } door)
            {
                int state = door.UseState;
                if (state is 1 or 3)
                {
                    door.CommandOpen();
                    TurnTowards(waypoint, AiMovementStep.DoorFacingTolerance);
                    return;
                }

                if (state == 2)
                {
                    // "Waiting for door to open"
                    TurnTowards(waypoint, AiMovementStep.DoorFacingTolerance);
                    return;
                }
            }
        }
        else
        {
            _doorAtHop = null;
            _doorSearched = false;
        }

        // Turn towards the waypoint as fast as the move-to-node clamp allows -- pi, which is "as fast as you
        // like" (ai.md 7).
        TurnTowards(waypoint, 0f);

        AiMovementOutcome outcome = _steering.Step(
            Position, Orientation, waypoint, 0f, 0f, out AiSteeringFrame frame);
        LastSteeringForward = frame.Forward;
        LastSteeringOutcome = outcome;
        LastAdvanceRm = rootMotionThisTick;


        if (outcome == AiMovementOutcome.WaypointReached)
        {
            CurrentNode = next.Id;
            _route.RemoveAt(0);

            // The next leg is measured from where the soldier actually is, not from the node it nominally
            // reached -- arrival is a 0.3 m box, so those differ. Reaching the waypoint also drops the
            // door handle (ai+15946/+15948 cleared in the arrival branch), so the next door hop
            // searches afresh.
            _legOrigin = Position;
            _doorAtHop = null;
            _doorSearched = false;
            return;
        }

        // Forward is a literal 1.0 in the frame, so the distance covered is the clip's own root motion.
        if (frame.Forward > 0f && rootMotionThisTick > 0.0)
        {
            double x = Position.X + (Math.Sin(-Facing) * rootMotionThisTick);
            double y = Position.Y + (Math.Cos(Facing) * rootMotionThisTick);
            LastMovedTo = new Vector3d(x, y, Position.Z);
            LastFacingDeg = Facing * 180.0 / Math.PI;
            Position = new Vector3d(x, y, QueryGroundHeight(x, y, waypoint));
        }
    }

    /// <summary>Finds the ground height at a position, using the terrain query when available.</summary>
    /// <param name="x">World X, in engine units.</param>
    /// <param name="y">World Y, in engine units.</param>
    /// <param name="waypoint">The node being walked to, used as a fallback when no terrain is available.</param>
    /// <returns>The Z to stand at, in engine units.</returns>
    private double QueryGroundHeight(double x, double y, Vector3d waypoint)
    {
        if (_ground is not null && _ground(x, y, Position.Z + GroundProbeAboveUnits, out double groundZ))
        {
            return groundZ;
        }

        return HeightAlongLegTo(waypoint, x, y);
    }

    /// <summary>
    /// The height a soldier walking towards a waypoint should be at, taken from the nav graph.
    /// </summary>
    /// <param name="waypoint">The node being walked to.</param>
    /// <param name="x">The soldier's new X.</param>
    /// <param name="y">The soldier's new Y.</param>
    /// <returns>The new Z, in engine units.</returns>
    /// <remarks>
    /// <para>
    /// <b>A recorded divergence.</b> The original does not do this: the AI steers a <c>Human</c> through the
    /// player's own input frame (§7 and <c>player.md</c> §2), so a soldier's height comes from the same
    /// ground query and slope handling the player uses — <c>HumanGroundQuery</c>, already read in M8 but not
    /// yet wired to soldiers. Until it is, keeping a soldier's own Z made it walk out of a raised position
    /// into mid-air, because a leg leaving a watchtower node kept the tower's height all the way along it.
    /// </para>
    /// <para>
    /// Interpolating along the leg is the honest stand-in, because a nav node's position is on the walkable
    /// surface by construction, so the straight line between two of them tracks the ground the graph was
    /// authored against. It is wrong wherever the ground between two nodes is not planar; the ground query
    /// is the fix, and this is listed in <c>TODO.md</c> as such.
    /// </para>
    /// </remarks>
    private double HeightAlongLegTo(Vector3d waypoint, double x, double y)
    {
        double legX = waypoint.X - _legOrigin.X;
        double legY = waypoint.Y - _legOrigin.Y;
        double lengthSquared = (legX * legX) + (legY * legY);
        if (lengthSquared <= 1.0)
        {
            return waypoint.Z;
        }

        double travelled = (((x - _legOrigin.X) * legX) + ((y - _legOrigin.Y) * legY)) / lengthSquared;
        travelled = Math.Clamp(travelled, 0.0, 1.0);
        return _legOrigin.Z + ((waypoint.Z - _legOrigin.Z) * travelled);
    }

    /// <summary>Takes the combat action, re-arming the behaviour every time.</summary>
    /// <remarks>
    /// <b>Every</b> path into combat comes through here, and that matters: once the machine ends — a
    /// targeting timeout, or a target with no detection slot — its phase is
    /// <see cref="AiCombatPhase.Ended"/> and every later tick returns "ended" at once. Re-arming only on
    /// the scripted path left a soldier that engaged a second way permanently unable to fight, which
    /// reads exactly as a guard that sees you and does nothing. <c>0x48D000</c> arms it when the action
    /// is taken, which is what this is.
    /// </remarks>
    /// <summary>
    /// Puts the running engagement on the pending-action list at COMBAT's own priority position —
    /// what retail's <c>AIAction_Combat</c> record gives arbitration through <c>0x450E10</c>.
    /// </summary>
    private void InstallEngineCombatAction()
    {
        int position = PriorityPositionOf(AiEvent.Combat);

        // 0x450E70's per-install draw; consumed so the shared random stream stays aligned.
        int countdown = (int)((_random.NextDouble() + 1.0) * 0.2 * 30.0);
        AiPendingAction? pending = null;
        pending = new AiPendingAction(position, countdown, run: () =>
        {
            if (Activity == AiSoldierActivity.Engaging && CombatPhase != AiCombatPhase.Ended)
            {
                _activePendingAction = pending;
            }
            else
            {
                _pendingActions.Remove(pending!);
                if (ReferenceEquals(_activePendingAction, pending))
                {
                    _activePendingAction = null;
                }
            }
        });
        _pendingActions.Add(pending);
    }

    private int PriorityPositionOf(AiEvent type)
    {
        IReadOnlyList<AiEvent> order = Events.PriorityOrder;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == type)
            {
                return i;
            }
        }

        return AiEventQueues.UnboundedPriorityCap;
    }

    private void EnterCombat()
    {
        // 0x44D160's civilian case installs no combat callback. It must not inherit the guard arm.
        if (Arm == AiCombatArm.None)
        {
            Activity = AiSoldierActivity.Idle;
            SetTarget(null);
            return;
        }

        _scriptedFire = null;
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _patrolOneShotChild = null;
        SetTarget(_defaultTarget);

        if (Activity != AiSoldierActivity.Engaging || CombatPhase == AiCombatPhase.Ended)
        {
            _combat?.Begin();
            _patrol?.Begin();
            _gunnerCombat?.Begin();
            _combatMachine.Reset();
            _detectionShoutDone = false;

            // The arm installer (0x48ABA0) picks the sub-arm from the BODY: crouched -> the kneel arm
            // (0x48C250/0x48C280), prone -> the prone arm. A patrol crouch-walking its route when
            // combat installs therefore fights kneeling until it moves or the row says otherwise.
            _patrolKneeling = IsCrouching;
        }

        Activity = AiSoldierActivity.Engaging;
        Route = null;
        RequestedAnimation = -1;
        _route.Clear();
        DestinationNode = -1;
    }

    /// <summary>Runs one tick of the combat behaviour and applies what it asks for.</summary>
    /// <remarks>
    /// <para>
    /// The soldier turns with the same clamped steering everything else uses and reports back whether
    /// the facing reached the two-degree tolerance, which is what the firing tick holds fire on.
    /// </para>
    /// <para>
    /// Nothing here fires a round. The command carries the trigger and the host turns it into a shot
    /// through the body's own weapon path, which is what the original does — see
    /// the original.
    /// </para>
    /// </remarks>
    private void Fight(int tick, Vector3d lastSeenFeet, double rootMotionThisTick)
    {
        // Recomputed every combat tick: AdvanceInCombat sets it when it actually steps.
        _movingInCombat = false;
        _rawInputWrites.Clear();

        // sub_48A920: only a currently visible player reaches the bark; the per-action latch is set
        // even when another AI used the shared 90-tick sound gate, so this engagement never retries.
        if (_playerSlot.IsVisible && !_detectionShoutDone)
        {
            DetectionShoutRequestedThisTick = true;
            _detectionShoutDone = true;
        }

        // The shared record's position is refreshed only WHILE THE TARGET IS SEEN (sub_48A920 reads it
        // off the detection slot), and everything combat does -- turning, aiming, hunting, the accuracy
        // distance -- reads the record (+272), not the target's true position. So a guard fights the
        // place the squad last SAW the player; one that never saw it itself borrows the squad's record.
        if (_playerSlot.IsVisible)
        {
            Target?.Refresh(lastSeenFeet);
        }

        Vector3d enemy = Target is { HasBeenSeen: true } shared ? shared.Position : lastSeenFeet;

        if (_combat is null || _weapon is null)
        {
            // No combat row: the old behaviour, which is to stand and look at the target.
            TurnTowards(enemy, AiPatrolRoute.LookAtToleranceRadians);
            return;
        }

        if (_gunnerCombat is not null)
        {
            AiStationaryGun? gun = GunnerId == -1 ? null : _stationaryGun?.Invoke(GunnerId);
            AiGunnerCommand gunner = _gunnerCombat.Update(
                gun,
                enemy,
                _playerSlot.IsRegistered);

            if (gunner.Ended)
            {
                Combat = new AiCombatCommand { Ended = true };
                _channelWrites.Clear();
                if (gunner.MountChannelValue is { } cleanupValue)
                {
                    WriteChannel(AiCombatChannel.PlayAnimationAlternate, cleanupValue);
                }
                Activity = AiSoldierActivity.Idle;
                SetTarget(null);
                return;
            }

            if (gunner.MountChannelValue is { } mountValue)
            {
                Combat = new AiCombatCommand();
                _channelWrites.Clear();
                WriteChannel(AiCombatChannel.PlayAnimationAlternate, mountValue);
                if (gunner.MountedThisTick && gun is not null)
                {
                    Position = gun.MountPosition;
                    Facing = gun.MountFacing;
                }
                return;
            }
        }

        if (_patrol is not null)
        {
            FightAsPatrol(tick, enemy, rootMotionThisTick);
            return;
        }

        AiCombatWeaponState weapon = _weapon();

        // A guard holding a KNIFE never sets up a burst: 0x48D080 tests the held category against 5 and
        // instead turns to the shared record position and raises the close-combat channel every tick,
        // taking no aim slot.
        if (weapon.Category == WeaponCategory.Knife)
        {
            WriteChannel(AiCombatChannel.CloseCombat, 1f);
            TurnTowards(AimPointOf(enemy, _enemyCrouching), (float)AiGuardCombat.AimToleranceRadians);
            Target?.Aim(this, AiAimMode.None);
            Combat = default;
            return;
        }

        AiCombatSense sense = new(
            HasDetectionSlot: _playerSlot.IsRegistered,
            InCone: _playerSlot.IsInCone,
            Visible: _playerSlot.IsVisible,
            TargetPosition: AimPointOf(enemy, _enemyCrouching),
            DistanceToTargetInUnits: Distance(Position, enemy),
            LocomotionState: AiCombatChannels.StateOf(_type, AiStanceChannel.Standing),
            AmmoInClip: weapon.AmmoInClip,
            Category: weapon.Category,
            ClipSize: weapon.ClipSize,
            BurstSize: weapon.BurstSize,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0);

        // The port has no stance machine on a soldier yet, so it is always in its standing state --
        // which is the state the guard behaviour waits for anyway.
        //
        // The accuracy lerp scales over the PLAIN primary view length: 0x48B9C0 reads [HumanAI+176],
        // the third double of the primary block {alpha, gamma, length, alarmLength} -- not the
        // alarm-extended range. Passing the alerted range stretched the ramp and made every alerted
        // guard more accurate at range than retail's.
        AiCombatPhase phaseBeforeUpdate = _combat.Phase;
        AiCombatCommand command = _combat.Update(sense, standing: true, Vision.Primary.Length);
        Combat = command;

        // RPG's two callbacks (0x48CBD0 stand / 0x48CDF0 kneel) share the ordinary fire, reload,
        // target and close-combat children. At a completed decision boundary they change stance on
        // the original 40% draw; the kneeling callback holds channel 1 every tick.
        if (Arm == AiCombatArm.Rpg
            && phaseBeforeUpdate != AiCombatPhase.Stand
            && _combat.Phase == AiCombatPhase.Stand
            && sense.Visible
            && _random.NextDouble() < 0.40000001)
        {
            _rpgKneeling = !_rpgKneeling;
        }

        // The same writes 0x48EF70 makes, recorded rather than applied -- see ChannelWrites.
        _channelWrites.Clear();
        if (Arm == AiCombatArm.Rpg && _rpgKneeling)
        {
            WriteChannel(AiCombatChannel.Kneel, 1f);
        }
        if (command.FirePose)
        {
            WriteChannel(AiCombatChannel.Fire, 1f);
        }

        if (command.PressReload)
        {
            WriteRawInputSlot(8, 1f);
        }

        if (command.StartedBurst is { } burst)
        {
            HitProbability = burst.HitProbability;
        }

        if (command.Aim is { } mode && Target is not null)
        {
            Target.Aim(this, mode);
        }

        if (command.TurnTo is { } point)
        {
            if (MountedGun is { } mounted)
            {
                // State 36 stays attached at the mount heading. The StationaryGun task, not the
                // Human root, consumes the turn inputs and clamps the barrel to its authored cone.
                Facing = mounted.MountFacing;
                _combat.ReportAimed(mounted.Contains(point));
            }
            else
            {
                TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians);
                _combat.ReportAimed(IsFacing(point, (float)AiGuardCombat.AimToleranceRadians));
            }
        }

        if (command.Ended)
        {
            LeaveCombatOccupancy();
            Activity = AiSoldierActivity.Idle;
            SetTarget(null);
        }
    }

    /// <summary>Runs the move-and-fight arm for one tick — <c>0x48ABA0</c>.</summary>
    /// <param name="tick">The engine tick used by the ten-tick evasion cooldown.</param>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="rootMotionThisTick">The run clip's own root motion this tick, for the walk to a destination.</param>
    /// <remarks>
    /// <para>
    /// The chooser decides (<see cref="AiPatrolCombat.Choose"/>) and this turns the decision into
    /// movement, turns and channel writes. Fire and reload are handed to the shared sub-actions, which
    /// is what the original does — it installs the same <c>0x48D8B0</c> and <c>0x48DB10</c> from both
    /// arms.
    /// </para>
    /// <para>
    /// Grenade and close-combat are standing one-shot children. They raise channels 8 and 9 until the
    /// body leaves state 0, then complete while states 8/9 own animations 58/70 to completion.
    /// </para>
    /// </remarks>
    private void FightAsPatrol(int tick, Vector3d enemy, double rootMotionThisTick)
    {
        AiCombatWeaponState weapon = _weapon!();

        // 0x48BC00 asks 0x48BE30 before it runs an already-installed child and before its ordinary
        // chooser. The drained event counters are the exact three words the original tests.
        if (!_patrolProne)
        {
            bool underFire = Events.DrainedCount(AiEvent.TakingDamage) > 0
                || Events.DrainedCount(AiEvent.Gunshot) > 0
                || Events.DrainedCount(AiEvent.GunshotMiss) > 0;
            bool onStair = _graph?.NodeAt(CurrentNode) is { } current
                && (current.Criteria & GraphNodeCriteria.Stair) != 0;
            AiEvasiveAction evasion = _patrol!.ChooseEvasion(
                tick,
                Distance(Position, enemy),
                AvailableRollSpace(),
                underFire,
                onStair);

            if (evasion == AiEvasiveAction.LieDown)
            {
                _patrolProne = true;
                _patrolRollChild = null;
            }
            else if (evasion is AiEvasiveAction.RollLeft or AiEvasiveAction.RollRight)
            {
                _patrolRollChild = evasion;
                _patrolOneShotChild = null;
                _combat!.CancelChild();
            }
        }

        // 0x48D830/0x48D870 keep writing the same raw slot until the body reports the kneeling stance.
        // The body then falls through state 19 to standing after the child stops writing.
        if (_patrolRollChild is { } roll)
        {
            _channelWrites.Clear();
            Combat = default;
            if (CombatState == HumanSoldierStates.KneelingState)
            {
                _patrolRollChild = null;
            }
            else
            {
                WriteRawInputSlot(roll == AiEvasiveAction.RollLeft ? 2 : 3, 1f);
            }

            return;
        }

        // 0x48C540 holds logical channel 2 throughout the prone behavior. State 15 owns the drop,
        // state 26 is stable prone, and releasing the channel selects state 16 (get-up).
        if (_patrolProne)
        {
            _channelWrites.Clear();
            WriteChannel(AiCombatChannel.LieDown, 1f);
            if (CombatState is not HumanSoldierStates.ProneState and not 27 and not 28)
            {
                Combat = default;
                return;
            }
        }

        // 0x48DBF0 and 0x48DBB0 share this exact protocol: while standing, write the action channel;
        // once the body changes state, the child is complete. The parent does not choose again until
        // the one-shot body state itself has returned to standing.
        if (_patrolOneShotChild is { } oneShot)
        {
            _channelWrites.Clear();
            Combat = default;
            if (CombatState == HumanSoldierStates.StandState)
            {
                WriteChannel(
                    oneShot == AiPatrolAction.ThrowGrenade
                        ? AiCombatChannel.ThrowGrenade
                        : AiCombatChannel.CloseCombat,
                    1f);
            }
            else
            {
                _patrolOneShotChild = null;
            }

            return;
        }

        // sub_48BC00 keeps an installed child callback in action+36 and runs it until it completes;
        // sub_48BFD0 is not called again in the middle of a fire/reload. Re-running the chooser every
        // tick let a patrol count its own Aiming slot, switch itself to Crowded and strand an active
        // firing phase without ever reaching the trigger.
        if (_combat!.Phase != AiCombatPhase.Stand)
        {
            _channelWrites.Clear();
            if (_patrolProne)
            {
                WriteChannel(AiCombatChannel.LieDown, 1f);
            }

            RunSharedSubAction(enemy, weapon);
            return;
        }

        // A one-shot child (grenade throw, melee, roll) owns the body until its clip completes — the
        // retail child at action+36, which the stand tick runs INSTEAD of the chooser and clears when
        // it reports done (0x48BC00). The machine leaves StandState for the throw/melee/roll and
        // returns to it when finished, which is the completion signal.
        if (!_patrolProne && (_patrolOneShotChild is not null || _patrolRollChild is not null))
        {
            // A roll's own done test is reaching the KNEEL state (0x48D870 returns sub_48EFE0(C, 1) --
            // rolls end kneeling); the throw/melee children complete back at stand.
            bool rollDone = _patrolRollChild is not null
                && CombatState == HumanSoldierStates.KneelingState;
            if (CombatState == HumanSoldierStates.StandState || rollDone)
            {
                _patrolOneShotChild = null;
                _patrolRollChild = null;
            }
            else
            {
                _channelWrites.Clear();
                Combat = default;
                return;
            }
        }

        // "Waiting for stand state" — retail's stand tick runs the chooser only when the body's live
        // locomotion state IS the type's stand state (sub_48EFE0(C, 0), the same off_541658 channel
        // table AiCombatChannels models; 0x48BC00 logs exactly that string otherwise). The run states
        // are exempt because in retail the movement behaviours (Follow/RePosition/LookForTarget)
        // REPLACE the whole per-tick and are never stand-gated — the port folds them into this one
        // function, so 17/18 must pass or the hunt oscillates move → run state → gate → stand → move.
        if (!_patrolProne && !_patrolKneeling
            && CombatState != HumanSoldierStates.StandState
            && CombatState != HumanSoldierStates.RunState
            && CombatState != HumanSoldierStates.RunFiringState)
        {
            _channelWrites.Clear();
            Combat = default;
            return;
        }

        // A negative "Combat liedown mim distance" stands the kneel and prone arms straight back up:
        // both dispatchers test the row value at +15800 (settings item +620 x 4096) and become the
        // standing arm when it is below zero (0x48C280 / 0x48C540).
        if ((_patrolProne || _patrolKneeling)
            && (CombatParameters?.LieDownMinimumDistanceInUnits ?? 0.0) < 0.0)
        {
            _patrolProne = false;
            _patrolKneeling = false;
            _channelWrites.Clear();
        }

        double distance = Distance(Position, enemy);
        bool visible = _playerSlot.IsVisible;

        // Only the branch that needs a search runs one, as the original's call sites do.
        int? toward = null;
        int? away = null;
        if (visible && distance > (CombatParameters?.TrackingDistanceInUnits ?? double.MaxValue))
        {
            toward = FindFreePosition(enemy, towardTarget: true);
        }
        else if (!visible)
        {
            away = FindFreePosition(enemy, towardTarget: false);
        }

        // "On its own node" gates the whole chooser: rule (a) walks back before anything else fires.
        // Retail's RePosition (0x48C760) walks the BODY onto the node and completes; the port walks the
        // route table, so two cases produce a walk that can never finish and left the soldier
        // repositioning for ever -- seeing the player, engaging, and never firing a shot:
        //  - no graph (CurrentNode is -1): there is nowhere to walk back to at all;
        //  - the route back to its own node is EMPTY (the table says it is already there) while the
        //    body stands outside the node's radius test.
        // Both mean "no walk exists that gets you more on your node than you are", which is what
        // standing on it is FOR -- so both count as standing on it.
        bool standingOnOwnNode = CurrentNode < 0
            || IsStandingOnNode(CurrentNode)
            || (_route.Count == 0 && DestinationNode == CurrentNode);

        // sub_48BFD0 begins with sub_4511F0(target, self, -1). The patrol is no longer occupying an
        // aimer slot while it decides whether a new child action may be installed.
        Target?.Aim(this, AiAimMode.None);

        AiPatrolSense sense = new(
            HasDetectionSlot: _playerSlot.IsRegistered,
            Visible: visible,
            DistanceToTargetInUnits: distance,
            CurrentNodeId: CurrentNode,
            StandingOnOwnNode: standingOnOwnNode,
            AmmoInClip: weapon.AmmoInClip,
            HasGrenade: HasGrenade,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0,
            FreePositionTowardTarget: toward,
            FreePositionAwayFromTarget: away);

        AiPatrolAction action = _patrol!.Choose(sense);
        PatrolAction = action;
        _channelWrites.Clear();
        if (_patrolProne)
        {
            WriteChannel(AiCombatChannel.LieDown, 1f);
        }
        else if (_patrolKneeling)
        {
            // The kneel dispatcher holds channel 1 every tick, exactly as the prone one holds 2.
            WriteChannel(AiCombatChannel.Kneel, 1f);
        }

        Combat = default;

        switch (action)
        {
            case AiPatrolAction.End:
                if (_patrolProne || _patrolKneeling)
                {
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                Activity = AiSoldierActivity.Idle;
                SetTarget(null);
                return;

            case AiPatrolAction.CloseCombat:
                if (_patrolProne || _patrolKneeling)
                {
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                _patrolOneShotChild = action;
                WriteChannel(AiCombatChannel.CloseCombat, 1f);
                TurnTowards(AimPointOf(enemy, _enemyCrouching), (float)AiGuardCombat.AimToleranceRadians);
                return;

            case AiPatrolAction.ThrowGrenade:
                if (_patrolProne)
                {
                    // The retail prone arm only logs "liedown throw grenade"; it installs no child.
                    return;
                }

                _patrolOneShotChild = action;
                WriteChannel(AiCombatChannel.ThrowGrenade, 1f);
                TurnTowards(AimPointOf(enemy, _enemyCrouching), (float)AiGuardCombat.AimToleranceRadians);
                return;

            case AiPatrolAction.LookForTarget:
                if (_patrolProne || _patrolKneeling)
                {
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                Target?.Aim(this, AiAimMode.Crowded);

                // The original walks to its destination EVERY tick and the action is only the upper body,
                // so a soldier hunting a target it has lost keeps moving to the search node while it
                // watches. Reproduced by continuing the walk whenever a combat route is set (the search's
                // one-in-sixteen Follow ticks set it); with no route it is the crowded-out case and only
                // turns to watch.
                if (_route.Count > 0)
                {
                    AdvanceInCombat(rootMotionThisTick);
                }
                else
                {
                    TurnTowards(AimPointOf(enemy, _enemyCrouching), (float)AiGuardCombat.AimToleranceRadians);
                }

                return;

            case AiPatrolAction.RePosition:
                if (_patrolProne || _patrolKneeling)
                {
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                HeadFor(CurrentNode);
                AdvanceInCombat(rootMotionThisTick);
                return;

            case AiPatrolAction.Follow:
                if (_patrolProne || _patrolKneeling)
                {
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                FollowTarget(enemy, rootMotionThisTick);
                return;

            case AiPatrolAction.Fire:
            case AiPatrolAction.Reload:
            default:
                RunSharedSubAction(enemy, weapon);
                return;
        }
    }

    /// <summary>Returns which three-metre lateral roll probes remain inside the current node.</summary>
    private AiRollSpace AvailableRollSpace()
    {
        if (_graph?.NodeAt(CurrentNode) is not { } node)
        {
            return AiRollSpace.None;
        }

        // Orientation column 0 is the body's right vector. At heading zero forward is +Y and right +X.
        double rightX = Math.Cos(Facing);
        double rightY = Math.Sin(Facing);
        const double Probe = 3.0 * GraphNode.MetreInUnits;
        AiRollSpace space = AiRollSpace.None;
        if (IsInsideNodeBox(
            new Vector3d(Position.X - (rightX * Probe), Position.Y - (rightY * Probe), Position.Z),
            node,
            devianceMetres: 0.0))
        {
            space |= AiRollSpace.Left;
        }

        if (IsInsideNodeBox(
            new Vector3d(Position.X + (rightX * Probe), Position.Y + (rightY * Probe), Position.Z),
            node,
            devianceMetres: 0.0))
        {
            space |= AiRollSpace.Right;
        }

        return space;
    }

    /// <summary>Advances on, or hunts for, the target — <c>0x48C860</c>, <c>"Combat - Follow"</c>.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="rootMotionThisTick">The run clip's own root motion this tick, for the walk to a destination.</param>
    /// <remarks>
    /// <b>A patrol fires while it is running at you</b>, with no burst set up and no aim solve — one
    /// round per tick straight into the trigger slot, whenever the target is inside a <b>16.3°</b> cone
    /// about its forward axis (<c>dot &gt; 0.96</c>). §13.7.
    /// </remarks>
    private void FollowTarget(Vector3d enemy, double rootMotionThisTick)
    {
        int destination = _patrol!.DestinationNodeId;
        if (destination < 0)
        {
            return;
        }

        WriteChannel(AiCombatChannel.Stand, 1f);

        if (CurrentNode == destination)
        {
            _patrol.ArrivedAtDestination();
            Target?.Aim(this, AiAimMode.None);
            return;
        }

        if (!HeadFor(destination))
        {
            // A stale or unreachable combat node cannot remain an installed Follow action. Retail's
            // movement action completes when it cannot produce another waypoint, allowing the chooser to
            // install Fire/LookForTarget on the next tick. Keeping the destination forever is the port-only
            // "guard sees me and stares" dead state.
            _route.Clear();
            DestinationNode = -1;
            _patrol.ArrivedAtDestination();
            Target?.Aim(this, AiAimMode.None);
            if (_playerSlot.IsVisible)
            {
                TurnTowards(AimPointOf(enemy, _enemyCrouching), (float)AiGuardCombat.AimToleranceRadians);
            }
            return;
        }

        AdvanceInCombat(rootMotionThisTick);

        if (!_playerSlot.IsVisible)
        {
            Target?.Aim(this, AiAimMode.None);
            return;
        }

        if (IsFacing(AimPointOf(enemy, _enemyCrouching), FollowFireConeRadians))
        {
            if ((Target?.Count(AiAimMode.Aiming) ?? 0) >= AiCombatTarget.MaximumAimers)
            {
                Target?.Aim(this, AiAimMode.Crowded);
                return;
            }

            // Retail's Follow raises the RUN signal (ai+15580 -> slot 26) and the FIRE slot
            // (ai+15500 -> slot 6) and NOTHING ELSE -- 0x48C860 never touches the fire-pose channel
            // (sub_48EF70(_, 6, _) appears only in the standing fire child 0x48D8E0). Raising the
            // fire pose here shoved the body machine into the STANDING fire chain from a moving
            // start (stand -> rack 11 -> ready 10 -> ...), where the stand-state gate then cut the
            // command each tick: guards visibly cycled the rack/recover clips at their destination
            // without one round leaving, because the trigger only forwards from states 6/18/22/27.
            // With run + trigger alone the machine goes 17 -> 18 (run-firing), which DOES forward it.
            Combat = new AiCombatCommand { PullTrigger = true };
            Target?.Aim(this, AiAimMode.Aiming);
            return;
        }

        Target?.Aim(this, AiAimMode.None);
    }

    /// <summary>The fire and reload sub-actions the two arms share.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="weapon">The held weapon's state this tick.</param>
    private void RunSharedSubAction(Vector3d enemy, AiCombatWeaponState weapon)
    {
        AiCombatSense sense = new(
            HasDetectionSlot: _playerSlot.IsRegistered,
            InCone: _playerSlot.IsInCone,
            Visible: _playerSlot.IsVisible,
            TargetPosition: AimPointOf(enemy, _enemyCrouching),
            DistanceToTargetInUnits: Distance(Position, enemy),
            LocomotionState: AiCombatChannels.StateOf(_type, AiStanceChannel.Standing),
            AmmoInClip: weapon.AmmoInClip,
            Category: weapon.Category,
            ClipSize: weapon.ClipSize,
            BurstSize: weapon.BurstSize,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0);

        AiCombatCommand command = _combat!.Update(sense, standing: true, Vision.Primary.Length);
        Combat = command;

        // The shared fire sub-action ending ends the whole engagement, exactly as it does in the guard
        // arm -- the patrol arm used to DISCARD this, so a patrol whose machine ended kept "fighting"
        // with a dead behaviour and never fired again.
        if (command.Ended)
        {
            LeaveCombatOccupancy();
            Activity = AiSoldierActivity.Idle;
            SetTarget(null);
            return;
        }

        if (command.FirePose)
        {
            WriteChannel(AiCombatChannel.Fire, 1f);
        }

        if (command.PressReload)
        {
            WriteRawInputSlot(8, 1f);
        }

        if (command.StartedBurst is { } burst)
        {
            HitProbability = burst.HitProbability;
        }

        if (command.Aim is { } mode && Target is not null)
        {
            Target.Aim(this, mode);
        }

        if (command.TurnTo is { } point)
        {
            TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians);
            _combat.ReportAimed(IsFacing(point, (float)AiGuardCombat.AimToleranceRadians));
        }
    }

    /// <summary>The cone a following patrol will fire through — 16.3°, i.e. <c>acos 0.96</c>.</summary>
    private const float FollowFireConeRadians = 0.2837941f;

    /// <summary>Sets a destination, but only when it is a new one.</summary>
    /// <param name="node">Where to head.</param>
    /// <remarks>
    /// <see cref="GoTo"/> clears the route and re-anchors the leg origin every time it is called, so a
    /// combat behaviour that asks for the same destination on every tick would restart the leg forever
    /// and the soldier would never actually travel. The original sets the destination once
    /// (<c>HumanAI+15848</c>) and the movement stepper walks the queue; this keeps that shape.
    /// </remarks>
    private bool HeadFor(int node)
    {
        if (DestinationNode == node)
        {
            return CurrentNode == node || _route.Count > 0;
        }

        return GoTo(node);
    }

    /// <summary>How close to the claimed sub-slot counts as standing on it — half a metre.</summary>
    private const double FreePositionArrivalInUnits = 2048.0;

    /// <summary>Steps the soldier along its combat route, the same walk the patrol path uses.</summary>
    /// <param name="rootMotionThisTick">The run clip's own root motion this tick, in engine units.</param>
    /// <remarks>
    /// The move-and-fight arm sets a Follow/RePosition destination but the original then <em>walks</em> to
    /// it — the AI steers its <c>Human</c> through the input frame and the body's mover carries it. Without
    /// this the port set a destination it never travelled, which is exactly "guards engage but don't chase".
    /// The distance covered is the run clip's own root motion (M8), so no speed is invented. Once the route
    /// delivers the body to the node, the walk finishes at the <b>claimed sub-slot</b> inside it — retail's
    /// <c>sub_4522A0</c> against the position at <c>HumanAI+16048</c>.
    /// </remarks>
    private void AdvanceInCombat(double rootMotionThisTick)
    {
        if (_route.Count > 0)
        {
            // Tell the renderer to play the walk clip this tick so next tick has root motion to move by.
            _movingInCombat = true;
            Advance(rootMotionThisTick);
            return;
        }

        // The route delivered the soldier to the NODE; the claimed sub-slot inside it is the real
        // destination — retail's walk helper (sub_4522A0) finishes at the position kept in ai+16048,
        // which is what spreads a squad over a large node instead of stacking it on the centre.
        if (_combatFreePosition is { } slot)
        {
            double dx = slot.X - Position.X;
            double dy = slot.Y - Position.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance <= FreePositionArrivalInUnits)
            {
                _combatFreePosition = null;
                return;
            }

            double step = Math.Min(rootMotionThisTick, distance);
            if (step <= 0.0)
            {
                _movingInCombat = true;
                return;
            }

            _movingInCombat = true;
            TurnTowards(slot, AiPatrolRoute.LookAtToleranceRadians);
            Position = new Vector3d(
                Position.X + (dx / distance * step),
                Position.Y + (dy / distance * step),
                GroundZAt(Position.X + (dx / distance * step), Position.Y + (dy / distance * step)));
        }
    }

    private double GroundZAt(double x, double y) =>
        _ground is { } ground && ground(x, y, Position.Z + GroundProbeAboveUnits, out double z)
            ? z
            : Position.Z;

    /// <summary>
    /// The height the free-position can-shoot ray starts above a candidate node — the <c>AIGraph</c>
    /// task's parameter at <c>+128</c> (default 1.0, ctor <c>0x509570</c>) × 4096, read by
    /// <c>sub_48B6E0</c>.
    /// </summary>
    /// <remarks>
    /// Casting from the node's raw ground position instead grazed the terrain and kerbs at the ray's
    /// very first step, so almost every candidate failed the can-shoot test and a hunting patrol could
    /// never find anywhere to go — the "engaging, never moving" stall. The port does not yet model the
    /// AIGraph script parameters, so the constructor's default stands in for all graphs.
    /// </remarks>
    public const double FreePositionRayHeightInUnits = 4096.0;

    /// <summary>One line of combat-decision state, for diffing the port against the retail probe log.</summary>
    /// <returns>The formatted line.</returns>
    public string DescribeCombat()
    {
        Vector3d believed = Target is { HasBeenSeen: true } shared ? shared.Position : _lastSeenFeet;
        int? toward = null;
        int? away = null;
        if (_patrol is not null)
        {
            toward = FindFreePosition(believed, towardTarget: true, claim: false);
            away = FindFreePosition(believed, towardTarget: false, claim: false);
        }

        return $"arm={Arm} act={Activity} pa={PatrolAction} phase={CombatPhase} state={CombatState} " +
            $"vis={_playerSlot.IsVisible} cone={_playerSlot.IsInCone} lost={_patrol?.NotVisibleTicks ?? -1} " +
            $"cap={_pendingActions.PriorityCap} node={CurrentNode}->{DestinationNode} route={_route.Count} " +
            $"moving={_movingInCombat} trigger={Combat.PullTrigger} " +
            $"believed=({believed.X:F0},{believed.Y:F0},{believed.Z:F0}) " +
            $"toward={(toward is { } t ? t.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")} " +
            $"away={(away is { } a ? a.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}";
    }

    /// <summary>Asks <see cref="AiFreePosition"/> where to stand.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="towardTarget">Whether to apply the direction test.</param>
    /// <param name="claim">
    /// Whether an accepted node is claimed and its sub-slot position stored — what the live search does
    /// (<c>0x48B260</c> claims inside the acceptance). Diagnostics pass <see langword="false"/> so a
    /// debug read never moves the occupancy state.
    /// </param>
    /// <returns>A node id, or <see langword="null"/>.</returns>
    private int? FindFreePosition(Vector3d enemy, bool towardTarget, bool claim = true)
    {
        int targetNode = NearestNode(enemy);
        return targetNode < 0 || _graph is null
            ? null
            : AiFreePosition.Find(
                _graph,
                CurrentNode,
                targetNode,
                towardTarget,
                nodeId => _graph.NodeAt(nodeId) is { } node
                    && (_sight is null || _sight(
                        new Vector3d(
                            node.Position.X,
                            node.Position.Y,
                            node.Position.Z + FreePositionRayHeightInUnits),
                        AimPointOf(enemy))),
                occupancy: claim ? _occupancy : null,
                claimant: claim ? this : null,
                claimedPosition: claim ? position => _combatFreePosition = position : null);
    }

    private AiNodeOccupancy? _occupancy;
    private Vector3d? _combatFreePosition;

    /// <summary>Connects the level's shared node-occupancy registry — the list at <c>0x57D768</c>.</summary>
    /// <param name="occupancy">The registry, shared by every brain in the level.</param>
    public void SetNodeOccupancy(AiNodeOccupancy? occupancy) => _occupancy = occupancy;

    /// <summary>The combat action's end releases its node claim, as the end fn <c>0x48D7D0</c> does.</summary>
    private void LeaveCombatOccupancy()
    {
        _occupancy?.Release(this);
        _combatFreePosition = null;
    }

    private bool _patrolKneeling;

    /// <summary>Whether the soldier is standing inside a node.</summary>
    /// <param name="nodeId">The node to test.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool IsStandingOnNode(int nodeId)
    {
        if (_graph?.NodeAt(nodeId) is not { } node)
        {
            return false;
        }

        return IsInsideNodeBox(Position, node, devianceMetres: 0.3);
    }

    /// <summary>IGI 1's oriented node-box test, <c>sub_4F9A40</c>.</summary>
    /// <param name="position">The world position to test.</param>
    /// <param name="node">The graph node and its authored heading.</param>
    /// <param name="devianceMetres">The extra half-extent supplied by the caller.</param>
    /// <returns><see langword="true"/> when the point lies in the rotated XY square.</returns>
    public static bool IsInsideNodeBox(Vector3d position, GraphNode node, double devianceMetres)
    {
        ArgumentNullException.ThrowIfNull(node);

        double dx = position.X - node.Position.X;
        double dy = position.Y - node.Position.Y;
        double sin = Math.Sin(node.Gamma);
        double cos = Math.Cos(node.Gamma);
        double localX = (cos * dx) + (sin * dy);
        double localY = (cos * dy) - (sin * dx);
        double halfExtent = (node.Radius + devianceMetres) * GraphNode.MetreInUnits;
        return localX >= -halfExtent && localX <= halfExtent
            && localY >= -halfExtent && localY <= halfExtent;
    }

    /// <summary>Where an AI aims at a human — its position (feet), half a metre up.</summary>
    /// <param name="target">The target task's position — its feet, not its eye.</param>
    /// <param name="crouching">Whether the target is crouching, which halves the raise.</param>
    /// <remarks>
    /// <c>sub_451380</c> copies the target task's own position out of the shared record and adds
    /// <b>2048 units (0.5 m) standing, 1024 (0.25 m) crouching</b> to Z — verified in the decompile:
    /// <c>a1[2] += crouch ? 1024.0 : 2048.0</c>.
    /// </remarks>
    public static Vector3d AimPointOf(Vector3d target, bool crouching = false) =>
        new(target.X, target.Y, target.Z + (crouching ? 1024.0 : 2048.0));

    private void TurnTowards(Vector3d target, float tolerance)
    {
        double dx = target.X - Position.X;
        double dy = target.Y - Position.Y;
        if ((dx * dx) + (dy * dy) < 1.0)
        {
            return;
        }

        float wanted = (float)Math.Atan2(-dx, dy);
        float error = Wrap(wanted - Facing);
        if (Math.Abs(error) <= tolerance)
        {
            return;
        }

        // The steering clamp is what bounds a turn: pi for moving, about 1.47 degrees for aiming.
        float limit = _steering.YawClamp;
        Facing = Wrap(Facing + Math.Clamp(error, -limit, limit));
    }

    private int NearestNode(Vector3d from) =>
        _graph is null
        || GraphNearestNode.Find(_graph, from, out GraphNode? best, out _) == GraphNearestNodeResult.None
            ? -1
            : best!.Id;

    private int NextBounded(int exclusiveBound) => exclusiveBound <= 0
        ? 0
        : checked((int)_random.NextBounded(checked((uint)exclusiveBound)));

    private static float Wrap(float radians)
    {
        while (radians > MathF.PI)
        {
            radians -= MathF.Tau;
        }

        while (radians < -MathF.PI)
        {
            radians += MathF.Tau;
        }

        return radians;
    }

    private static Vector3d ClosestPointOnSegment(Vector3d start, Vector3d end, Vector3d point)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double lengthSquared = (dx * dx) + (dy * dy) + (dz * dz);
        if (lengthSquared <= 0.0)
        {
            return start;
        }

        double t = (((point.X - start.X) * dx)
            + ((point.Y - start.Y) * dy)
            + ((point.Z - start.Z) * dz)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return new Vector3d(start.X + (dx * t), start.Y + (dy * t), start.Z + (dz * t));
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
