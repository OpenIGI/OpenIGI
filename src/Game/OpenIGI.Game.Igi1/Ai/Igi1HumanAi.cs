using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// One soldier's AI runtime — the parts of <c>HumanAI</c> the port has read, joined up and driving a body.
/// </summary>
/// <remarks>
/// <para>
/// This is the integration of what is settled about the original's AI: the nav graph and its precomputed route
/// table, the event pump's arbitration, the two view cones, the detection cascade's three gates and the patrol
/// step machine and waypoint steering. Every rule and constant here is read; what is
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
/// walk-versus-run gait, the idle fallback, and strict occlusion by the port's world-only sight cast.
/// Script events/actions, authored patrol routes, combat target sharing, and hearing are handled by the
/// same facade so they consume the same HumanAI state rather than a second coarse activity model.
/// </para>
/// <para>
/// Like its IGI 2 sibling the class is split by facet, all over the one HumanAI state: this file holds the wiring
/// and the state, and <c>.Perception</c>, <c>.Actions</c>, <c>.Behaviours</c>, <c>.Routing</c>, <c>.Movement</c>,
/// <c>.Combat</c>, <c>.PatrolCombat</c>, <c>.Life</c> and <c>.Diagnostics</c> the rest.
/// </para>
/// </remarks>
public sealed partial class Igi1HumanAi
{
    private readonly GraphFile? _graph;
    private readonly AiDetectionSchedule _schedule;
    // The detection scan's slots (+5624), one per human it has been offered, and its two lists: living
    // enemies (+5648) and fallen friends (+5660), each in registration order.
    private readonly Dictionary<int, Igi1AiTrackedHuman> _tracked = [];
    private readonly List<Igi1AiTrackedHuman> _enemies = [];
    private readonly List<Igi1AiTrackedHuman> _fallenFriends = [];
    private readonly AiDetectionSlot _noSlot = new();

    // The human the soldier's fight -- or, before one, its attention -- is about: the subject of the event
    // that started the fight (the combat record's target, +15016), or the first enemy in sight.
    private int _targetId = NoHuman;
    private bool _registeredOnce;
    private const int NoHuman = int.MinValue;
    private readonly AiMovementStep _steering = new();
    private readonly IgiRandom _random;
    private readonly List<AiWaypoint> _route = [];
    private readonly AiPendingActionList _pendingActions = new();

    // The action the stack ran last: it is the one whose gait and crouch the body is carrying.
    private string? _runningAction;

    // Whether a combat action is installed -- the combat record at +15016. It outlives the ticks a
    // reaction pushed over it runs for, which is why it is not the same thing as Activity.
    private bool _combatInstalled;

    // The node a WalkToNode, RunToNode or MoveToEvent is heading for, or -1.
    private int _goToNode = -1;

    // This tick's inputs, for the action the stack runs.
    private double _rootMotionThisTick;

    /// <summary>Gets or sets whether the body's turn and move wait for <see cref="StepBodyAfterClip"/>.</summary>
    /// <remarks>
    /// The original runs a soldier's HumanAI as a child of its Human, and the Human's own update (<c>0x45D080</c>)
    /// after it: the body machine, which may start a new clip, then the turn and the move by the delta of the clip
    /// that is playing NOW. A renderer that picks the clip after the brain sets this, and steps the body once it
    /// knows the clip.
    /// </remarks>
    public bool StepsBodyAfterClip { get; set; }

    private bool _bodyStepPending;
    private bool _enemyAlive = true;

    // Floats 0 and 1 of this tick's input frame: the turn and the pitch, each over pi.
    private float _turnInput;
    private float _pitchInput;
    private readonly HumanBodyTurn _bodyTurn = new(averages: false);
    private readonly HumanBodyTurn _viewPitchTurn = new(averages: true);

    // The clip's sideways root motion this tick, in the body's own frame.
    private double _rootLateralThisTick;

    // Human+2056 / +2080: the leg an AI's feet are interpolated along. 0x462390 casts no ray for an AI.
    private Vector3d _groundLegFrom;
    private Vector3d _groundLegTo;
    private bool _groundLegSet;

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

        // A scripted AI starts as the constructor leaves it (0x44F060): nothing admitted but its creation,
        // and nothing enabled until the creation branch declares what it wants to hear.
        Events = new AiEventQueues();
    }

    /// <summary>Gets a value indicating whether a behaviour script is driving this soldier.</summary>
    public bool HasScript => _host is not null && _script is not null;

    /// <summary>Gets a value indicating whether a ground query is wired up.</summary>
    public bool HasGroundQuery => _ground is not null;

    /// <summary>Gets a value indicating whether an AI graph grounds this soldier every tick (<c>0x462390</c>'s graph branch).</summary>
    public bool HasGraph => _graph is not null;

    /// <summary>Gets whether that graph is in a placed model's frame, which parents the soldier to the model instead.</summary>
    public bool GraphInParentFrame => _graph?.InParentFrame == true;

    /// <summary>Gets a value indicating whether the body has been stood on its graph's height at least once.</summary>
    /// <remarks>
    /// Until then <see cref="Position"/> is still where the level placed the soldier, which is the model origin
    /// (<c>Human+32</c>) rather than its feet: retail's first AI tick is what replaces it with the graph height
    /// plus the clip's root height (<c>0x462390</c>), and nothing may add the root height before that.
    /// </remarks>
    public bool StoodOnGraph { get; private set; }

    /// <summary>Gets the distance to whatever the last dispatched event was about, in engine units.</summary>
    /// <remarks>What <c>AIFunction_GetEventDistance</c> hands the archetype script.</remarks>
    public double LastEventDistance { get; private set; }

    /// <summary>Gets the actions the archetype script last asked for.</summary>
    public IReadOnlyList<AiProducedAction> LastActions { get; private set; } = [];

    /// <summary>Initializes a new instance of the <see cref="Igi1HumanAi"/> class.</summary>
    /// <param name="graph">The nav graph this soldier walks.</param>
    /// <param name="position">Where it starts, in engine units.</param>
    /// <param name="facing">Its starting heading, in radians about Z.</param>
    /// <param name="phase">Which of the four detection phases it sweeps on — staggering the level's guards.</param>
    /// <param name="seed">The wander seed, so a level is reproducible tick for tick.</param>
    /// <param name="random">The mission-wide IGI random stream, or a seed-local stream for isolated tests.</param>
    /// <param name="soldierClass">The body's class, whose machine the brain drives. Defaults to the ordinary soldier.</param>
    /// <remarks>
    /// <paramref name="graph"/> may be <see langword="null"/>: a soldier whose level ships no graph for
    /// its <c>HumanAI</c> still detects and fights — nothing in the stand-and-fight arm needs a node.
    /// Only patrolling and the combat repositioning searches are graph work, and they no-op without one.
    /// </remarks>
    public Igi1HumanAi(
        GraphFile? graph,
        Vector3d position,
        float facing,
        int phase,
        int seed,
        IgiRandom? random = null,
        HumanSoldierClass? soldierClass = null)
    {
        SoldierClass = soldierClass ?? HumanSoldierClass.Soldier;
        Body = SoldierClass.CreateBody();
        _graph = graph;
        Position = position;
        Facing = facing;
        _random = random ?? new IgiRandom(unchecked((uint)seed));

        // The intervals are NOT the ctor's zeroes: the AI's init handler (sub_4505D0, the same pass that
        // binds the graph and loads MISSION:AI/<id>.qsc) calls sub_4896C0(ai+5624, side, 6, mask, 4, 8),
        // which stores a5/4 = 1 into the cone interval and a6/4 = 2 into the ray interval. With 0x4898D0's
        // interval+1 reload that is a cone test every SECOND sweep (8 ticks) and a sight ray every THIRD
        // (12 ticks) -- the retail acquisition latency. The earlier 0/0 reading came from the ctor path
        // alone, which really does leave the fields zeroed; the init handler overwrites them.
        _schedule = new AiDetectionSchedule(phase & 3, coneInterval: 1, rayInterval: 2);

        Vision = new AiVision();
        // A soldier without a script runs the built-in fallback, which hears every type in type order.
        Events = new AiEventQueues { IsInitialised = true };
        foreach (AiEvent type in Enum.GetValues<AiEvent>())
        {
            Events.Declare(type);
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
    /// <see cref="OpenIGI.Game.Player.HumanSeparation.RadiusInUnits"/> horizontally and
    /// <see cref="OpenIGI.Game.Player.HumanSeparation.HeightInUnits"/> vertically of another soldier <em>and</em> closer
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
    public AiEventQueues Events { get; private set; }

    /// <summary>Diagnostics: the last event type this brain dispatched, or <see langword="null"/>.</summary>
    public AiEvent? LastDispatchedEvent { get; set; }

    /// <summary>Diagnostics: told of every dispatch with its priority position, as retail's <c>0x44FC90</c> runs.</summary>
    public Action<AiEvent, int>? Dispatched { get; set; }

    /// <summary>Gets the live pending-action program used for event interrupt arbitration.</summary>
    public AiPendingActionList PendingActions => _pendingActions;

    /// <summary>Gets the patrol currently being walked, or <see langword="null"/> when there is none.</summary>
    public AiPatrolRoute? Route { get; private set; }

    /// <summary>Gets a value indicating whether the soldier is walking rather than running.</summary>
    /// <remarks>
    /// Three sources name the gait, mirroring the retail per-ticks that call the walk/run setters
    /// (<c>sub_4525E0</c>/<c>sub_452660</c>) every tick: an <c>AIAction_Activate</c>'s run argument
    /// (<c>0x48E7D0</c>), the patrol cursor's gait flag set by <see cref="AiPatrolCommandKind.RunTo"/>
    /// (<c>0x48E920</c>), and <c>AIAction_RunToNode</c> against <c>AIAction_WalkToNode</c>. Only the
    /// action that runs sets it, so a patrol suspended under a fight does not choose the fight's gait.
    /// </remarks>
    public bool IsWalking => _runningAction switch
    {
        ActionActivate when _activate is { } activating => !activating.Run,
        ActionPatrol when Route is { } route => route.IsWalking,
        _ => !_scriptedRunGait,
    };

    private bool _scriptedRunGait;

    /// <summary>Gets the gait the body read this tick: the run byte at <c>Human+3328</c>, set for walking.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="IsWalking"/> is what the running action asks for; this is what the body does. The soldier's
    /// create (<c>0x45CC50</c>) sets the byte, and only a press of input slot 20 -- the AI's channel 0 -- flips it,
    /// in <c>0x45D080</c> AFTER the body machine (<c>0x4EC1E0</c>) has read it. The walk and run setters
    /// (<c>0x4525E0</c>, <c>0x452660</c>) press it when it is wrong for the gait. Their callers: the patrol's start
    /// (<c>0x48E8C0</c>, walk), the patrol runner (<c>0x48E920</c>, from the cursor's gait flag BEFORE the command
    /// runs -- the flag a new cursor starts with is walk, <c>0x4537E0</c>), Activate (<c>0x48E7D0</c>), Move To
    /// Event (<c>0x48E020</c>), Walk/Run To Node (<c>0x48EBF0</c>/<c>0x48EC50</c>) and Combat - Follow
    /// (<c>0x48C860</c>, run).
    /// </para>
    /// <para>
    /// So a patrol that opens on a RunTo walks two ticks first: its first tick sets the walk gait from the new
    /// cursor, its second presses for the run, and the body reads the old byte on that tick too. Probed on
    /// mission 4: squad member 400 went state 3 (walk), 3, then 2 (run), slot 20 set on the second tick and the
    /// byte clear from the third; walking patrols, whose byte was right from the start, never pressed.
    /// </para>
    /// </remarks>
    public bool WalkGait { get; private set; } = true;

    // Human+3328 as the next body tick will read it, and whether this tick's frame presses slot 20.
    private bool _runByte = true;
    private bool _gaitPress;

    // The walk (0x4525E0) and run (0x452660) setters: channel 0 is written non-zero when the byte is wrong.
    private void SetGait(bool walking) => _gaitPress = walking != _runByte;

    /// <summary>Gets a value indicating whether the soldier is crouching.</summary>
    /// <remarks>
    /// The patrol walker (<c>0x48E920</c>) writes the crouch channel on every tick it runs, and the channel
    /// block is cleared at the start of every tick, so the crouch lasts only while the patrol is running.
    /// </remarks>
    public bool IsCrouching => _runningAction == ActionPatrol && (Route?.IsCrouching ?? false);

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

        // 0x48E8C0, the patrol's start: the walk setter, before the cursor's first command. It leaves the movement
        // queue and its destination alone, so a patrol taken up again after a fight plans its first leg from the walk
        // it was on (0x4518E0's start rule reads the old queue) -- clearing them sent mission 4's squad member 404
        // back into the node it had left.
        SetGait(walking: true);
        return true;
    }

    /// <summary>Gets the graph node it was last at.</summary>
    public int CurrentNode { get; private set; }

    /// <summary>Gets the node it is heading for, or <c>-1</c>.</summary>
    public int DestinationNode { get; private set; }

    /// <summary>Gets the alert latch — set while it can see an enemy or an alarm is on.</summary>
    public bool Alerted { get; private set; }

    /// <summary>Gets a value indicating whether it can currently see its enemy.</summary>
    public bool SeesEnemy => TargetSlot.IsVisible;

    /// <summary>Gets a value indicating whether the enemy it can currently see is the player.</summary>
    public bool SeesPlayer => SeesEnemy && TargetIsPlayer;

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

    // The event type being dispatched -- the queue the natives' gAIEventList points at -- or none outside a dispatch.
    private AiEvent? _currentEventType;
    private int _currentEventSource;
    private int _currentEventSubject;
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
    private Igi1ScriptedFire? _scriptedFire;

    // AIAction_RunPanicking (0x48E270 / 0x48E300 / 0x48E430 / 0x48E4A0): the first argument is
    // an escape radius in metres. The start callback chooses one graph node outside it and the run
    // callback owns the AI until that node is reached.
    private int _panicDestination = -1;

    // AIAction_FallFlat (0x48EB40 / 0x48EB60): channel 2 is held until currentTick > start+seconds*30.
    private int _fallFlatDurationTicks;
    private int _fallFlatDeadline = -1;

    // AIAction_Stunned (0x44D8F0 -> 0x48E130 -> 0x48E150): the real argument is converted
    // to ticks on the first run, and channel 3 is held at animation id 38 until currentTick > deadline.
    // -1 while no stun is installed.
    private int _stunnedDurationTicks = -1;
    private int _stunnedDeadline = -1;

    // AIAction_PlaySound carries a 32-byte definition name and an attach flag. The audio system lives
    // above OpenIGI.Game, so the action reports the request through this seam.
    private Action<string, bool>? _scriptSound;

    // sub_48A920's arg-block byte +33: a visible player may produce one detection bark during this
    // combat action. The mission-wide 90-tick gate lives in SoldierRenderer, beside all other AIs.
    private bool _detectionShoutDone;

    // HumanAI+15852, the node nearest the target as 0x48A920 last found it (-1 when none), and +15856, the flag it
    // raises when that node changes from a known one. Every combat per-tick runs 0x48A920 first, so the node is
    // tracked through the whole fight, seen or not; Follow (0x48C860) spends the flag on a re-search, and the
    // chooser's out-of-sight move (0x48C212) clears it.
    private int _followTargetNode = -1;
    private bool _followResearch;

    // sub_48A920's squad-ping counter, argBlock+28 of the running combat action.
    private int _alertPingCounter;

    /// <summary>Gets whether this tick's combat pinged the squad — <c>sub_48AB10</c> out of <c>sub_48A920</c>.</summary>
    /// <remarks>The renderer delivers it to this soldier's own side within <see cref="AlertPingRadiusInUnits"/>.</remarks>
    public bool AlertPingDueThisTick { get; private set; }

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

    // The idle-animation table -- AIFunction_{SetAnimationInterval, AddAnimationEntry,
    // GetAnimationToPlay} (0x44EAF0 / 0x44EBB0 / 0x44ECB0). ai+15112 (ids, stride 16), +15120
    // (weights, double), +15204 (count, cap 5), +15192/+15196 interval min/random in ticks, +15200
    // the countdown to the next play (-1 before the script arms it).
    private readonly List<(int Id, double Weight)> _animationEntries = [];
    private int _animationMinTicks;
    private int _animationRandomTicks;
    private int _animationCountdown = -1;
    private bool _scriptedAnimation;
    private int _lastTick;

    /// <summary>Diagnostics: the last tick this brain's <c>Tick</c> was entered.</summary>
    public int LastTick => _lastTick;

    /// <summary>Declares an event type's dispatch priority — <c>AIFunction_SetEventPriority</c> (<c>0x44E0C0</c>).</summary>
    /// <param name="type">The event type the script wants considered.</param>
    /// <remarks>
    /// The original enables the type and <b>appends</b> it to the priority order the first time it is named,
    /// building the whole order in call order — which is what decides, when two events are pending on one
    /// tick, the one the script sees, and makes a type the script never names inaudible.
    /// </remarks>
    public void DeclareEventPriority(AiEvent type) => Events.Declare(type);

    /// <summary>Gets whether the walker wrote forward into this tick's frame — float 4, <c>+15492</c>.</summary>
    /// <remarks>
    /// This is what puts the body in a walking state, and so what plays a clip whose root motion moves it.
    /// A soldier holding at a door, arrived, or turning on the spot writes none and stands.
    /// </remarks>
    public bool WalksForward { get; private set; }

    /// <summary>Gets the id of the human the soldier's fight is about, or its attention before one.</summary>
    /// <remarks><see cref="AiDetectionCandidate.PlayerId"/> for the player; a soldier's task id otherwise.</remarks>
    public int TargetId => _targetId;

    /// <summary>Puts the soldier into combat and points its fight at one human, for the suites that stage a fight.</summary>
    /// <param name="targetId">The id of the human the fight is about.</param>
    internal void EnterCombatForTesting(int targetId)
    {
        EnterCombat();
        _targetId = targetId;
    }

    /// <summary>Gets whether the human the soldier is concerned with is the player.</summary>
    public bool TargetIsPlayer => TargetHuman is { } target ? target.Candidate.IsPlayer : _targetId == AiDetectionCandidate.PlayerId;

    /// <summary>Gets the feet of the human the soldier is concerned with — what it aims at.</summary>
    public Vector3d TargetFeet => TargetHuman?.Candidate.Feet ?? default;

    /// <summary>Gets whether the human the soldier is concerned with is crouching.</summary>
    public bool TargetCrouching => TargetHuman?.Candidate.Crouching ?? false;

    private Igi1AiTrackedHuman? TargetHuman => _tracked.TryGetValue(_targetId, out Igi1AiTrackedHuman? human) ? human : null;

    // The target's slot: 0x489BE0 over the combat record's human, or an empty slot for nobody.
    private AiDetectionSlot TargetSlot => TargetHuman?.Slot ?? _noSlot;

    private bool HasTarget => TargetHuman is not null;

    private bool TargetIsSoldier => TargetHuman is { } target && !target.Candidate.IsPlayer;

    private Vector3d TargetMark => TargetHuman?.Candidate.Mark ?? default;

    private AiCombatTarget? TargetRecord => TargetHuman?.Candidate.Record ?? _defaultTarget;

    /// <summary>Arms the idle-animation cadence — <c>AIFunction_SetAnimationInterval</c> (<c>0x44EAF0</c>).</summary>
    /// <param name="minimumSeconds">The floor of the gap between idle animations.</param>
    /// <param name="randomSeconds">The random span on top of it.</param>
    public void SetAnimationInterval(float minimumSeconds, float randomSeconds)
    {
        _animationMinTicks = (int)(minimumSeconds * 30.0);
        _animationRandomTicks = (int)(randomSeconds * 30.0);
        _animationCountdown = NextBounded(_animationRandomTicks) + _animationMinTicks;
    }

    // 0x44F410, for a living AI with an armed countdown and at least one entry (+15204): at nought it posts
    // AIEVENT_ANIMATION and re-arms from the interval, otherwise it counts one down -- so the gap is the drawn count
    // plus one tick, and the count stands still on a tick the AI does not run.
    private void TickIdleAnimation()
    {
        if (_animationCountdown < 0 || _animationEntries.Count == 0)
        {
            return;
        }

        if (_animationCountdown == 0)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Animation), owner: 0, worldTests: null);
            _animationCountdown = NextBounded(_animationRandomTicks) + _animationMinTicks;
        }
        else
        {
            _animationCountdown--;
        }
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

    /// <inheritdoc cref="Igi1AiGeometry.IsInsideNodeBox"/>
    public static bool IsInsideNodeBox(Vector3d position, GraphNode node, double devianceMetres) =>
        Igi1AiGeometry.IsInsideNodeBox(position, node, devianceMetres);

    /// <inheritdoc cref="Igi1AiGeometry.AimPointOf"/>
    public static Vector3d AimPointOf(Vector3d target, bool crouching = false) =>
        Igi1AiGeometry.AimPointOf(target, crouching);

    private int NextBounded(int exclusiveBound) => exclusiveBound <= 0
        ? 0
        : checked((int)_random.NextBounded(checked((uint)exclusiveBound)));
}
