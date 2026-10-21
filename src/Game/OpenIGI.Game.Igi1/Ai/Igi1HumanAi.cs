using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

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
/// </remarks>
public sealed class Igi1HumanAi
{
    private readonly GraphFile? _graph;
    private readonly AiDetectionSchedule _schedule;
    // The detection scan's slots (+5624), one per human it has been offered, and its two lists: living
    // enemies (+5648) and fallen friends (+5660), each in registration order.
    private readonly Dictionary<int, TrackedHuman> _tracked = [];
    private readonly List<TrackedHuman> _enemies = [];
    private readonly List<TrackedHuman> _fallenFriends = [];
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

    /// <summary>
    /// One item of the movement queue — <c>0x452080</c>'s record: the point walked to (<c>+8</c>), whether a
    /// door stands on the hop (<c>+32</c>), and the node that becomes the current one when it is reached
    /// (<c>+36</c>).
    /// </summary>
    private readonly record struct AiWaypoint(Vector3d Point, int Node, bool Door);

    /// <summary>
    /// The fraction of the eye-to-mark segment beyond which an object contact no longer hides the target —
    /// <c>0x489B20</c>'s <c>fraction &gt;= 0.79000002</c>.
    /// </summary>
    /// <remarks>
    /// Terrain always blocks (<c>0x4CE410</c> forces its fraction to zero). An object — a crate, a wall, a
    /// building, most of what the levels are made of — hides the target only when it is nearer than this,
    /// so cover pressed up against the target leaves it visible.
    /// </remarks>
    public const double SightObstructionTolerance = 0.79000002;

    /// <summary>
    /// How far above its feet a standing human reports itself to detection — the Z offset the descriptor
    /// handler adds in <c>sub_465820</c>: 2826.24 units (0.69 m), stored as the dwords <c>0xE147AE14</c> and
    /// <c>0x40A6147A</c>.
    /// </summary>
    /// <remarks>
    /// Vision does <b>not</b> test the target's eye. The candidate describes itself for the sweep
    /// (<c>sub_45FE30</c>) as its <em>position plus this raise</em> — mid-body — and the cone test, the
    /// sight ray and the stored distance all take that one point. Testing the eye let a guard acquire a
    /// player whose head cleared a sill while retail's waist-height mark stayed hidden.
    /// </remarks>
    public static readonly double DetectionRaiseStandingInUnits =
        BitConverter.Int64BitsToDouble(0x40A6147AE147AE14);

    /// <summary>
    /// The crouched raise — <c>sub_465820</c>'s other constant, 1433.6 units (0.35 m), the dwords
    /// <c>0x66666666</c> and <c>0x40966666</c>.
    /// </summary>
    public static readonly double DetectionRaiseCrouchedInUnits =
        BitConverter.Int64BitsToDouble(0x4096666666666666);

    /// <summary>How far above its feet a standing human's origin (<c>Human+32</c>) is — 0.97 m.</summary>
    /// <remarks>
    /// The port keeps a human's position at its feet; IGI.exe keeps it at the model origin, which its ground
    /// query stands at the node or ground height plus the root bone's height (<c>0x462390</c> via
    /// <c>sub_48A340</c>). Measured on the retail game: the player landed at 0.97 m over the port's ground at
    /// the mission-1 spawn, and guard 2003's origin matches the port's drawn origin (the soldier model's
    /// lowest vertex is 0.970 m below it) to the centimetre.
    /// </remarks>
    public const double HumanOriginStandingInUnits = 0.97 * 4096.0;

    /// <summary>How far above its feet a crouched human's origin is — 0.46 m, measured on the retail player.</summary>
    public const double HumanOriginCrouchedInUnits = 0.46 * 4096.0;

    /// <summary>How far above its feet a human's detection mark is: its origin plus <c>sub_465820</c>'s raise.</summary>
    /// <param name="crouching">Whether the human is crouched.</param>
    /// <returns>The height in units.</returns>
    /// <remarks>
    /// <c>sub_465820</c> adds the raise to <c>Human+32</c>, the origin -- not the feet. Adding it to the
    /// feet put every mark, and so every sight ray, about a metre low.
    /// </remarks>
    public static double DetectionMarkAboveFeet(bool crouching) => crouching
        ? HumanOriginCrouchedInUnits + DetectionRaiseCrouchedInUnits
        : HumanOriginStandingInUnits + DetectionRaiseStandingInUnits;

    /// <summary>Tests whether the line between two world points is clear enough to see along.</summary>
    /// <param name="from">The eye.</param>
    /// <param name="to">The target.</param>
    /// <returns><see langword="true"/> when the target can be seen.</returns>
    public delegate bool LineOfSight(Vector3d from, Vector3d to);

    /// <summary>Returns whether a world hit leaves the target visible.</summary>
    /// <param name="targetDistance">The distance from the observer to the target.</param>
    /// <param name="blockerDistance">The distance from the observer to the nearest world hit.</param>
    /// <returns><see langword="true"/> when no world hit occurs before the target.</returns>
    public static bool WorldBlockerAllowsSight(double targetDistance, double blockerDistance) =>
        targetDistance <= 0.0 || blockerDistance >= targetDistance;

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
    private int _currentEventSource;
    private int _currentEventSubject;
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

    /// <summary>Gets which of the four ticks this soldier's detection sweep runs on -- its record's <c>+12</c>.</summary>
    public int SweepPhase => _schedule.Phase;

    /// <summary>Diagnostics: <c>+15852/+15856/+15848</c> -- the tracked target node, the re-search flag, the Follow node.</summary>
    public string FollowState => FormattableString.Invariant(
        $"{_followTargetNode}/{(_followResearch ? 1 : 0)}/{_patrol?.DestinationNodeId ?? -1}");

    /// <summary>Diagnostics: this soldier's detection slot for one human -- retail's slot +16/+20/+40/+41/+42.</summary>
    /// <param name="target">The human's task id.</param>
    /// <returns>The cooldowns and flags, or <c>-</c> when the human is not tracked.</returns>
    public string SlotState(int target) => _tracked.TryGetValue(target, out TrackedHuman? human)
        ? FormattableString.Invariant(
            $"cone={human.Slot.ConeCooldown} ray={human.Slot.RayCooldown} incone={(human.Slot.IsInCone ? 1 : 0)} visible={(human.Slot.IsVisible ? 1 : 0)} registered={(human.Slot.IsRegistered ? 1 : 0)}")
        : "-";

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

    private readonly record struct ActivateTask(int TaskId, bool Run, int Node);

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
        Vector3d? point = _currentEventPosition ?? (HasTarget ? TargetMark : null);
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
    /// original's discard-not-defer rule.
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
            fromSameSide: !PlayerIsEnemy);

    /// <summary>Delivers a gunshot using the weapon record's exact AI detection range.</summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    public bool HearGunshot(Vector3d shotPosition, double range)
        => HearGunshot(shotPosition, range, fromSameSide: !PlayerIsEnemy);

    /// <summary>
    /// Delivers a gunshot using the weapon record's exact AI detection range, knowing whose side fired it.
    /// </summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <param name="fromSameSide">Whether the shooter is on this soldier's own side.</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    /// <remarks>
    /// The weapon's fire handler (<c>0x479780</c>) posts the record with the shooter's side and filter
    /// <c>0x46</c>, and admission (<c>0x450110</c>) rejects a record whose side is the listener's own under
    /// bit <c>0x40</c>. So nobody hears their own side firing: a guard does not come running because a
    /// teammate opened up, and a friendly does not react to the player's shots.
    /// </remarks>
    public bool HearGunshot(Vector3d shotPosition, double range, bool fromSameSide) =>
        HearGunshot(shotPosition, range, fromSameSide, AiDetectionCandidate.PlayerId);

    /// <summary>Delivers a gunshot, knowing whose side fired it and who fired it.</summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <param name="fromSameSide">Whether the shooter is on this soldier's own side.</param>
    /// <param name="shooter">The shooter's id — the record's source and subject (<c>0x479780</c>).</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    public bool HearGunshot(Vector3d shotPosition, double range, bool fromSameSide, int shooter)
    {
        if (Activity == AiSoldierActivity.Dead || fromSameSide)
        {
            return false;
        }

        range = Math.Max(0.0, range);
        double distance = Vector3d.Distance(Position, shotPosition);
        if (distance > range)
        {
            return false;
        }

        bool posted = Events.Post(
            new AiEventRecord(AiEvent.Gunshot, -1, AiEventFilter.None, (float)range, shotPosition, shooter, shooter),
            owner: RuntimeId,
            worldTests: null);
        if (!posted)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = shotPosition;

        RefreshBelievedTarget(shooter);

        return true;
    }

    /// <summary>Points the target record at where the target really is, after an admitted stimulus.</summary>
    /// <remarks>
    /// <c>sub_48A920</c> runs <c>sub_451380</c> over the shared record whenever the tick drained one of
    /// ALERT, WALK, GROUNDIMPACT, DOOR, FENCE, LADDER, TAKINGDAMAGE, GUNSHOT, GRENADELAND or GUNSHOTMISS,
    /// even though the target is not in sight. EXPLOSION is not among them. It also has to run for the
    /// first stimulus an idle guard receives, or SetCombat starts it fighting the zero vector.
    /// </remarks>
    /// <param name="subject">The human the stimulus names; the player unless the caller says otherwise.</param>
    private void RefreshBelievedTarget(int subject = AiDetectionCandidate.PlayerId)
    {
        // Before a fight the stimulus turns the soldier's attention to whoever it names; in one, the record
        // keeps its human and is only refreshed.
        if (!_combatInstalled && _tracked.ContainsKey(subject))
        {
            _targetId = subject;
        }

        if (!HasTarget)
        {
            return;
        }

        _lastSeenFeet = TargetFeet;
        SetTarget(TargetRecord);
        Target?.Refresh(_lastSeenFeet);
    }

    /// <summary>
    /// How far the EXPLOSION event of a demolition, a placed mine, a missile, a wreck or a camera carries —
    /// 25 m (<c>0x47C80000</c>), whatever the size of the blast itself.
    /// </summary>
    /// <remarks>
    /// Every poster hands <c>0x480FE0</c> its own fixed radius for the AI event, separate from the blast's
    /// damage radius: <c>ExplodeObject</c> (<c>0x481A60</c>), <c>ExplodeMagicObj</c> (<c>0x43C0B0</c>), the
    /// missile (<c>0x428390</c>), a destroyed vehicle (<c>0x42E5A0</c>), the proximity mine
    /// (<c>0x476340</c>) and the security camera (<c>0x441770</c>) all pass this.
    /// </remarks>
    public const double LargeExplosionEventRadiusInUnits = 25.0 * 4096.0;

    /// <summary>How far a hand grenade's EXPLOSION event carries — 10 m (<c>0x47200000</c>, <c>0x476AC0</c>).</summary>
    public const double GrenadeExplosionEventRadiusInUnits = 10.0 * 4096.0;

    /// <summary>Delivers the native type-21 explosion event when the blast reaches this soldier.</summary>
    /// <param name="explosionPosition">The world-space centre of the explosion.</param>
    /// <param name="rangeInUnits">The event's own radius, fixed by the kind of explosion, in engine units.</param>
    /// <returns><see langword="true"/> when the event was queued.</returns>
    /// <remarks>
    /// IGI1 registers <c>AIEVENT_EXPLOSION</c> as event 21 in the native AI registrar at
    /// <c>0x44EEB0</c>/<c>0x450B20</c>. It is a sensed event with no human subject, so the native
    /// equivalent is an in-range position record rather than a gunshot record or a subject filter.
    /// </remarks>
    public bool ReceiveExplosion(
        Vector3d explosionPosition,
        double rangeInUnits = GrenadeLandRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || !double.IsFinite(rangeInUnits) || rangeInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, explosionPosition);
        if (distance > rangeInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = explosionPosition;

        return Events.Post(
            new AiEventRecord(
                AiEvent.Explosion,
                -1,
                AiEventFilter.InRange,
                (float)rangeInUnits,
                explosionPosition,
                -1,
                0),
            owner: RuntimeId,
            worldTests: null);
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
    public bool ReceiveGunshotMiss(Vector3d segmentStart, Vector3d segmentEnd, Vector3d shooterPosition) =>
        ReceiveGunshotMiss(segmentStart, segmentEnd, shooterPosition, PlayerIsEnemy, PlayerSource);

    /// <summary>Delivers a near miss from any shooter — <c>0x4606F0</c>, which asks only whose side fired.</summary>
    /// <param name="segmentStart">The beginning of the round's live segment.</param>
    /// <param name="segmentEnd">The end of the round's live segment, where it stopped.</param>
    /// <param name="shooterPosition">The shooter's position, for the actions that read the event's source.</param>
    /// <param name="fromOtherSide">Whether the shooter is on the other side from this soldier.</param>
    /// <param name="source">The shooter's id — a soldier's task id, or <c>-1</c> for the player.</param>
    /// <returns><see langword="true"/> when the strict two-metre test passed and the event was posted.</returns>
    /// <remarks>
    /// The handler runs for any round, the player's or a soldier's: a human the round passes that is not on
    /// the shooter's side broadcasts GUNSHOTMISS at each hearable point, with the shooter's side and filter
    /// <c>0x46</c>, so only the other side hears it — a guard hears a friendly's round going past, and a
    /// friendly a guard's.
    /// </remarks>
    public bool ReceiveGunshotMiss(
        Vector3d segmentStart, Vector3d segmentEnd, Vector3d shooterPosition, bool fromOtherSide, int source)
    {
        if (Activity == AiSoldierActivity.Dead || !fromOtherSide)
        {
            return false;
        }

        Vector3d eye = EyePosition();
        Vector3d closest = Vector3d.ClosestOnSegment(segmentStart, segmentEnd, eye);
        double distance = Vector3d.Distance(eye, closest);
        if (distance >= GunshotMissRadiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = shooterPosition;
        RefreshBelievedTarget(source);

        // -1 is the renderer's player-originator sentinel. The event position is the actual nearest point
        // on the round, while BeginFallFlat resolves the source separately to the player's live position.
        Events.Post(
            new AiEventRecord(AiEvent.GunshotMiss, -1, AiEventFilter.None, 0.0f, closest, source, source),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    /// <summary>Takes a round's near-miss point, as <c>0x4606F0</c> broadcasts it before the round is marched.</summary>
    /// <param name="point">The nearest point on the round's line to the human it passed, in world units.</param>
    /// <param name="fromOtherSide">Whether the shooter is on another side from this soldier (filter <c>0x40</c>).</param>
    /// <param name="source">The shooter's id.</param>
    /// <param name="shooterPosition">The shooter's position, for the actions that read the event's source.</param>
    /// <returns>Whether the event was admitted.</returns>
    /// <remarks>
    /// The broadcast carries radius 8192 and filter <c>0x46</c>; <c>0x450110</c> measures the radius from the
    /// AI's own position (<c>+32</c>), inclusive.
    /// </remarks>
    public bool ReceiveRoundPassing(Vector3d point, bool fromOtherSide, int source, Vector3d shooterPosition)
    {
        if (Activity == AiSoldierActivity.Dead || !fromOtherSide)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, point);
        if (distance > GunshotMissRadiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = shooterPosition;
        RefreshBelievedTarget(source);
        Events.Post(
            new AiEventRecord(AiEvent.GunshotMiss, -1, AiEventFilter.None, 0.0f, point, source, source),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    // The source a player-originated record carries -- the renderer's sentinel for "the player".
    private const int PlayerSource = -1;

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
        double radiusInUnits = GroundImpactRadiusInUnits) =>
        ReceiveHumanNoise(AiEvent.GroundImpact, impactPosition, shooterPosition, radiusInUnits);

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
    public bool ReceiveWalk(Vector3d stepPosition, Vector3d walkerPosition, double radiusInUnits) =>
        ReceiveHumanNoise(AiEvent.Walk, stepPosition, walkerPosition, radiusInUnits);

    /// <summary>The radius of the noise a door, a fence climb or a ladder step makes — 5 m (<c>0x46A00000</c>).</summary>
    public const double HumanNoiseRadiusInUnits = 5.0 * 4096.0;

    /// <summary>
    /// Delivers a noise an enemy human makes — footsteps, a landing or a round's impact, a door it opens or
    /// closes, a fence it climbs, a ladder step.
    /// </summary>
    /// <param name="type">
    /// <see cref="AiEvent.Walk"/>, <see cref="AiEvent.GroundImpact"/>, <see cref="AiEvent.Door"/>,
    /// <see cref="AiEvent.Fence"/> or <see cref="AiEvent.Ladder"/>.
    /// </param>
    /// <param name="position">Where the noise is — the human's own position for a door, fence or ladder.</param>
    /// <param name="source">The human's live position, for the actions that read the event's source.</param>
    /// <param name="radiusInUnits">How far it carries.</param>
    /// <returns><see langword="true"/> when the soldier heard it.</returns>
    /// <remarks>
    /// Each is broadcast to every AI with filter <c>0x46</c> — in range, from a live human, of the other
    /// side — and the range test passes at exactly the radius (<c>0x450110</c> rejects only
    /// <c>radius &lt; distance</c>). The posters are <c>0x463920</c> (walk), <c>0x408460</c> (landing),
    /// <c>0x40C270</c> (a door the player toggles, opened or picked), <c>0x411770</c> (every tick of a fence
    /// climb but its first and last) and the three ladder states' step (<c>0x40D6B0</c>, <c>0x40D920</c>,
    /// <c>0x40DA80</c>).
    /// </remarks>
    public bool ReceiveHumanNoise(AiEvent type, Vector3d position, Vector3d source, double radiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || !PlayerIsEnemy || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, position);
        if (distance > radiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = source;
        RefreshBelievedTarget();

        Events.Post(
            new AiEventRecord(
                type,
                -1,
                AiEventFilter.InRange | AiEventFilter.SubjectIsHuman | AiEventFilter.OtherSide,
                (float)radiusInUnits,
                position,
                -1,
                -1),
            owner: RuntimeId,
            worldTests: null);
        return true;
    }

    /// <summary>Receives the retail grenade-takeoff event (type 17).</summary>
    /// <param name="grenadePosition">The grenade task's position, in engine units.</param>
    /// <returns><see langword="true"/> when the in-view admission test passed.</returns>
    /// <remarks>
    /// IGI1's grenade task posts <c>AIEVENT_GRENADETHROWN</c> with filter <c>InView</c>, radius -1 and
    /// no human subject. It is not a gunshot and must not be routed through the hearing path: a guard
    /// that sees the grenade receives the event even when it is outside the gunshot radius.
    /// </remarks>
    public bool ReceiveGrenadeThrown(Vector3d grenadePosition)
    {
        if (Activity == AiSoldierActivity.Dead
            || !Vision.IsVisible(EyePosition(), EyeOrientation(), grenadePosition, Alerted))
        {
            return false;
        }

        LastEventDistance = Vector3d.Distance(Position, grenadePosition);
        _playerEventSourcePosition = grenadePosition;
        return Events.Post(
            new AiEventRecord(
                AiEvent.GrenadeThrown,
                -1,
                AiEventFilter.InView,
                -1.0f,
                grenadePosition,
                0,
                0),
            owner: RuntimeId,
            worldTests: null);
    }

    /// <summary>The retail grenade-land event radius: five metres, or 20480 engine units.</summary>
    public const double GrenadeLandRadiusInUnits = 5.0 * 4096.0;

    /// <summary>Receives the retail bounce event (type 18).</summary>
    /// <param name="grenadePosition">The grenade task's contact position.</param>
    /// <param name="radiusInUnits">The native event radius, five metres by default.</param>
    /// <returns><see langword="true"/> when the in-range admission test passed.</returns>
    public bool ReceiveGrenadeLand(
        Vector3d grenadePosition,
        double radiusInUnits = GrenadeLandRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, grenadePosition);
        if (distance > radiusInUnits)
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = grenadePosition;
        bool posted = Events.Post(
            new AiEventRecord(
                AiEvent.GrenadeLand,
                -1,
                AiEventFilter.InRange,
                (float)radiusInUnits,
                grenadePosition,
                0,
                0),
            owner: RuntimeId,
            worldTests: null);
        if (posted)
        {
            RefreshBelievedTarget();
        }

        return posted;
    }

    /// <summary>The native flashbang AI event radius: ten metres, or 40960 engine units.</summary>
    public const double FlashbangEventRadiusInUnits = 10.0 * 4096.0;

    /// <summary>Receives the retail flashbang event (type 19).</summary>
    /// <param name="grenadePosition">The flash task's world position.</param>
    /// <param name="radiusInUnits">The native event radius, ten metres by default.</param>
    /// <returns><see langword="true"/> when both native view and range filters passed.</returns>
    public bool ReceiveFlashbang(
        Vector3d grenadePosition,
        double radiusInUnits = FlashbangEventRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, grenadePosition);
        if (distance > radiusInUnits
            || !Vision.IsVisible(EyePosition(), EyeOrientation(), grenadePosition, Alerted))
        {
            return false;
        }

        LastEventDistance = distance;
        _playerEventSourcePosition = grenadePosition;
        return Events.Post(
            new AiEventRecord(
                AiEvent.Flashbang,
                -1,
                AiEventFilter.InView | AiEventFilter.InRange,
                (float)radiusInUnits,
                grenadePosition,
                0,
                0),
            owner: RuntimeId,
            worldTests: null);
    }

    /// <summary>Posts the type-15 event a non-lethal hit delivers to a HumanAI.</summary>
    /// <param name="attackerPosition">The attacking human's position, used by distance/behind natives.</param>
    /// <returns><see langword="true"/> when the <see cref="AiEvent.TakingDamage"/> event was queued.</returns>
    /// <param name="attacker">The attacker's id — the record's source and subject (<c>0x4553E0</c>).</param>
    public bool ReceiveDamage(Vector3d attackerPosition, int attacker = AiDetectionCandidate.PlayerId)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return false;
        }

        bool posted = Events.Post(
            new AiEventRecord(
                AiEvent.TakingDamage,
                -1,
                AiEventFilter.None,
                0.0f,
                attackerPosition,
                attacker,
                attacker),
            owner: RuntimeId,
            worldTests: null);

        if (!posted)
        {
            return false;
        }

        LastEventDistance = Vector3d.Distance(Position, attackerPosition);
        _playerEventSourcePosition = attackerPosition;
        RefreshBelievedTarget(attacker);
        return true;
    }

    /// <summary>The alert ping's reach — 28672 units, 7 m (<c>sub_48AB10</c> pushes float bits <c>0x46E00000</c>).</summary>
    public const double AlertPingRadiusInUnits = 28672.0;

    /// <summary>How often an engaged guard pings — every 20 ticks (<c>sub_48A920</c>'s <c>% 20</c> counter).</summary>
    public const int AlertPingPeriodTicks = 20;

    /// <summary>Receives a fighting neighbour's alert ping — <c>AIEVENT_ALERT</c> out of <c>sub_48AB10</c>.</summary>
    /// <param name="alerterPosition">The alerting guard's own position — the record's position field.</param>
    /// <param name="source">The alerting guard's runtime task identity, used by <c>SendResponse</c>.</param>
    /// <returns><see langword="false"/> when out of the ping's reach or dead.</returns>
    /// <remarks>
    /// The record: type 5 (<c>ALERT</c>), filter mask <c>0x26</c> (in range, a human subject, the same side),
    /// radius 7 m, source = the alerting guard, subject = the target it is fighting. Admission has no other
    /// gate: an already-alert guard, or one with an alert queued, still takes the record. The side test is
    /// the caller's, which knows both teams; the distance gate is applied here.
    /// </remarks>
    /// <param name="target">
    /// The alerting soldier's own target — the record's subject (<c>0x48AB10</c> sends
    /// <c>{self, target}</c>).
    /// </param>
    public bool ReceiveAlert(Vector3d alerterPosition, int source = 0, int target = AiDetectionCandidate.PlayerId)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, alerterPosition);
        if (distance > AlertPingRadiusInUnits)
        {
            return false;
        }

        bool posted = Events.Post(
            new AiEventRecord(
                AiEvent.Alert, -1, AiEventFilter.None, (float)AlertPingRadiusInUnits,
                alerterPosition, source, target),
            owner: 0,
            worldTests: null);
        if (!posted)
        {
            return false;
        }

        LastEventDistance = distance;

        // The same target refresh arm handles both hearing and squad alert records. A nearby guard that
        // has never seen Jones therefore inherits the fighting guard's target record and turns on the
        // squad's current player position instead of world origin.
        RefreshBelievedTarget(target);

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

    /// <summary>
    /// Ends the actions the alarm is meant to have installed — <c>AIFunction_RemoveAlarmActions</c>
    /// (<c>0x44DBC0</c>), which <c>guard.qsc</c> calls on <c>ALARMOFF</c>.
    /// </summary>
    /// <remarks>
    /// The original ends every action whose priority equals the word at <c>HumanAI+2108</c>. That word is
    /// the ninth entry of the priority order — an event <em>type</em> — compared against a priority
    /// <em>position</em>, so it ends what the event at that position installed, not the alarm run. With the
    /// archetypes' order, the ninth entry is GUNSHOT (16) and position 16 is EXPLOSION; an alarm run
    /// installed from COMBAT carries on after the alarm is switched off. Reproduced as it is.
    /// </remarks>
    public void RemoveAlarmActions()
    {
        IReadOnlyList<AiEvent> order = Events.PriorityOrder;
        int priority = order.Count > AlarmActionsOrderIndex ? (int)order[AlarmActionsOrderIndex] : 0;
        foreach (AiPendingAction action in _pendingActions.Actions.ToArray())
        {
            if (action.Priority == priority)
            {
                FinishAction(action);
            }
        }
    }

    private const string ActionPatrol = "AIAction_Patrol";
    private const string ActionCombat = "AIAction_Combat";
    private const string ActionActivate = "AIAction_Activate";

    // HumanAI+2108 - 2076: which entry of the priority order 0x44DBC0 reads.
    private const int AlarmActionsOrderIndex = 8;

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

    private TrackedHuman? TargetHuman => _tracked.TryGetValue(_targetId, out TrackedHuman? human) ? human : null;

    // The target's slot: 0x489BE0 over the combat record's human, or an empty slot for nobody.
    private AiDetectionSlot TargetSlot => TargetHuman?.Slot ?? _noSlot;

    private bool HasTarget => TargetHuman is not null;

    private bool TargetIsSoldier => TargetHuman is { } target && !target.Candidate.IsPlayer;

    private Vector3d TargetMark => TargetHuman?.Candidate.Mark ?? default;

    private AiCombatTarget? TargetRecord => TargetHuman?.Candidate.Record ?? _defaultTarget;

    /// <summary>One human the scan has been offered, and the slot it keeps for it.</summary>
    private sealed class TrackedHuman(AiDetectionCandidate candidate)
    {
        public AiDetectionCandidate Candidate { get; set; } = candidate;

        public AiDetectionSlot Slot { get; } = new();
    }

    /// <summary>Gets the view's pitch above the horizon, in radians — <c>view+292</c>.</summary>
    /// <remarks>
    /// Steered through float 1 of the frame and turned by the body exactly as the yaw is (<c>0x464800</c>,
    /// clamped to a right angle). A walk and an idle level it; an aim, a look at an event and a patrol's
    /// LookAtNode point it at their target's own height.
    /// </remarks>
    public float ViewPitch { get; private set; }

    /// <summary>Gets the view's orientation — the body's heading tipped by <see cref="ViewPitch"/>.</summary>
    /// <remarks>What the eye snapshot (<c>0x489960</c>) tests its cones against: the view matrix at <c>+120</c>.</remarks>
    public Matrix3f ViewOrientation => Matrix3f.FromEngineEulerAngles(ViewPitch + RecoilPitch, 0f, Facing + RecoilYaw);

    /// <summary>Gets the orientation matrix a renderer should draw it with.</summary>
    public Matrix3f Orientation => Matrix3f.FromEngineEulerAngles(0f, 0f, Facing + RecoilYaw);

    /// <summary>Gets the yaw the weapon's kick still holds the body off its heading by -- the view's <c>+308</c>.</summary>
    /// <remarks>
    /// 0x4610C0 builds the body's matrix from the heading plus this (<c>Human+1288 = +1292 + view+308</c>), so the
    /// kick turns the body, its movement and its eye, and the AI's next turn measures against it.
    /// </remarks>
    public float RecoilYaw { get; private set; }

    /// <summary>Gets the pitch the weapon's kick still holds the view off -- the view's <c>+296</c>, added in 0x464800.</summary>
    public float RecoilPitch { get; private set; }

    /// <summary>Folds the last round's kick into the recoil the view carries -- 0x462E50, every tick, after the turn and move.</summary>
    /// <remarks>
    /// Each offset becomes (kick + offset) x 0.9, so a burst climbs and a pause lets it settle. The kick is the one the
    /// weapon returned on the PREVIOUS tick: the gun fires after this, and its kick waits in the fire message for the
    /// next tick's pass. So a soldier's AI, which runs before its body, measures its turn against the recoil as it was
    /// when it last fired. Fitted on mission 4's squad member 405 to a millionth of a radian over a whole burst.
    /// </remarks>
    public void SettleRecoil()
    {
        RecoilPitch = (float)((_pendingKick.Pitch + (double)RecoilPitch) * 0.89999998);
        RecoilYaw = (float)((_pendingKick.Yaw + (double)RecoilYaw) * 0.89999998);
        _pendingKick = default;
    }

    /// <summary>Keeps the kick the weapon returned for a round, for the next tick's <see cref="SettleRecoil"/>.</summary>
    /// <param name="pitchKick">The round's pitch kick (the fire message's <c>+32</c>).</param>
    /// <param name="yawKick">Its yaw kick (<c>+36</c>).</param>
    public void TakeWeaponKick(float pitchKick, float yawKick) => _pendingKick = (pitchKick, yawKick);

    // The kick of the round fired last tick, waiting for this tick's SettleRecoil.
    private (float Pitch, float Yaw) _pendingKick;

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

    /// <summary>Diagnostic: the current route's first waypoint, in engine units.</summary>
    public Vector3d? DebugWaypoint => _route.Count > 0 ? _route[0].Point : null;

    /// <summary>Gives the soldier an occlusion test, so it stops seeing through walls.</summary>
    /// <param name="sight">The test, or <see langword="null"/> to see through everything.</param>
    public void SetLineOfSight(LineOfSight? sight) => _sight = sight;

    /// <summary>
    /// Gets or sets what hears of every sight ray this soldier casts: from, to, and the task id of the human it looks
    /// at (<see cref="AiDetectionCandidate.PlayerId"/> for the player).
    /// </summary>
    /// <remarks>
    /// The sight cast <c>0x489B20</c> -> <c>0x4CE410</c> visits every dynamic object along the whole segment before it
    /// consults static geometry, and the human filter it passes (<c>0x42FC50</c>) admits every human but the looker
    /// and the target. A human the segment meets has its bone palette rebuilt (<c>0x45F800</c> -> <c>0x45F240</c>),
    /// which is where its own eye's matrix comes from -- so being looked past turns a bystander's eye.
    /// </remarks>
    public Action<Vector3d, Vector3d, int>? SightCast { get; set; }

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
    /// <param name="enemyEye">The target eye in engine units, the same point <c>Tick</c> is fed.</param>
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
        return (Vector3d.Distance(Position, enemyEye), inCone, clear, Alerted, TargetSlot.IsVisible);
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
    /// <c>AIAction_Combat</c> dispatches by type and this now follows it: a <c>GUARD_*</c> type,
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
        if (SoldierClass.SwapsClipsByAiType && Body is HumanSoldierBodyMachine soldierBody)
        {
            soldierBody.UseClips(SoldierClipsFor(type));
        }

        Arm = AiCombatArms.For(type);

        // The patrol arm's own fire and reload sub-actions ARE the guard arm's - the original installs
        // 0x48D8B0 and 0x48DB10 from both - so the shared machinery is kept and only the DECISION
        // differs, which is how the original splits them.
        _combat = Arm == AiCombatArm.None
            ? null
            : new AiGuardCombat(parameters, _random.NextDouble)
            {
                // Only GuardCombatStand (0x48D080) draws the dead-store shot delay; the patrol arm's Fire installs the
                // shared fire child with its burst and nothing else.
                DrawsStandDelay = Arm == AiCombatArm.Guard,
            };
        _patrol = Arm is AiCombatArm.Patrol or AiCombatArm.NamedCharacter
            ? new AiPatrolCombat(parameters, _random.NextDouble, closeQuarters: Arm == AiCombatArm.Patrol)
            : null;
        _gunnerCombat = Arm == AiCombatArm.Gunner ? new AiGunnerCombat(this) : null;

        CombatParameters = parameters;
    }

    // 0x45CF90's switch over the brain's type: which Human+824 a HumanSoldier plays.
    private static HumanBodyStateTable SoldierClipsFor(AiType type) => type switch
    {
        AiType.Priboi => HumanSoldierStates.PriboiTable,
        AiType.Civilian => HumanSoldierStates.CivilianTable,
        AiType.SpetnazPatrolUzi or AiType.SpetnazPatrolAk or AiType.SpetnazPatrolSpas
            or AiType.SpetnazGuardUzi or AiType.SpetnazGuardAk or AiType.SpetnazGuardSpas
            => HumanSoldierStates.SpetsnazTable,
        _ => HumanSoldierStates.Table,
    };

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

    /// <summary>Takes a recorded close-combat strike, if one is waiting.</summary>
    /// <param name="damage">Receives the row's damage factor on a hit, or zero.</param>
    /// <returns><see langword="true"/> when a strike was pending.</returns>
    public bool ConsumeMeleeStrike(out float damage)
    {
        damage = _pendingMeleeStrike;
        _pendingMeleeStrike = 0f;
        return damage > 0f;
    }

    private bool _movingInCombat;
    private AiPatrolAction? _patrolOneShotChild;
    private AiEvasiveAction? _patrolRollChild;
    private bool _patrolProne;

    /// <summary>The damage of a close-combat strike chosen this tick, waiting to be consumed.</summary>
    /// <remarks>
    /// Recorded at the trigger tick (the <c>CloseCombat</c> case below) and read once by whoever
    /// ticks the brain — the renderer forwards player-directed strikes to the mission, which owns
    /// the player's health. Zero means no strike is pending. NPC-directed strikes are recorded
    /// the same way but currently discarded there: NPC-on-NPC melee has no victim pipeline yet.
    /// </remarks>
    private float _pendingMeleeStrike;

    /// <summary>Gets a value indicating whether the soldier is walking a combat route this tick.</summary>
    /// <remarks>
    /// True whenever the move-and-fight arm stepped the soldier — advancing on a target, repositioning, or
    /// hunting a lost one while it watches. The renderer plays the WALK clip for it, because the walk clip's
    /// own root motion is what the next tick's <see cref="AdvanceInCombat"/> moves by; playing the fire pose
    /// instead (as the raw action would, since a hunting soldier's action is <c>LookForTarget</c> most
    /// ticks) leaves the clip with no motion and the chase never travels.
    /// </remarks>
    public bool IsMovingInCombat => _movingInCombat;

    /// <summary>Gets a value indicating whether a combat action is installed — <c>HumanAI+15016</c> is set.</summary>
    /// <remarks>
    /// What <c>sub_4528F0</c> answers. <c>KnifeStab</c> (<c>0x46DFB0</c>) reads it to decide a stab is a silent
    /// kill: a soldier with no fight on is stabbed at a damage factor of 1000.
    /// </remarks>
    public bool HasCombatRecord => _combatInstalled;

    /// <summary>Gets the soldier's combat row, once one has been given.</summary>
    public AiCombatParameters? CombatParameters { get; private set; }

    /// <summary>Gets the soldier's AI type.</summary>
    public AiType Type => _type;

    /// <summary>Gets what the combat behaviour asked for on the last tick it ran.</summary>
    public AiCombatCommand Combat { get; private set; }

    /// <summary>Gets the class of the body this brain drives.</summary>
    public HumanSoldierClass SoldierClass { get; }

    /// <summary>Gets the body machine — <c>Human+748</c> of the soldier this brain drives.</summary>
    public HumanBodyMachine Body { get; }

    /// <summary>Gets the body's state — the real machine, not the old inference.</summary>
    public int CombatState => Body.State;

    /// <summary>Gets whether this tick entered a new body state rather than running its per-tick callback.</summary>
    public bool CombatStateChangedThisTick => Body.ChangedThisTick;

    /// <summary>Runs one tick of the body machine and returns the state to animate.</summary>
    /// <param name="animationDone">Whether the state's clip finished a cycle this tick — the machine's <c>0x1</c> bit.</param>
    /// <returns>The body state, whose clip the renderer plays.</returns>
    /// <remarks>
    /// The body reads the input frame this tick's writes built — each channel on the slot its AI type
    /// resolves it to, every raw write on its own slot, and the trigger on slot 6 — so each class's signal
    /// handler finds its bits where the original's does. See <see cref="HumanBodyInput"/>.
    /// </remarks>
    public int TickCombatState(bool animationDone)
    {
        // The gun was taken this tick: the dispatch seats the body in state 36 and zeroes slot 24 before the
        // machine reads the frame.
        bool seated = _seatedOnGunThisTick && Body is HumanSoldierBodyMachine;
        if (seated)
        {
            ((HumanSoldierBodyMachine)Body).EnterUseObject();
        }

        _seatedOnGunThisTick = false;
        int state = Body.Tick(new HumanBodyInput(
            WrittenFrame(dropUseObject: seated),
            animationDone,
            Flinch: _flinchZone >= 0,
            Dead: Activity == AiSoldierActivity.Dead,
            MovingForward: IsMovingInCombat,
            Running: _runByte,
            HitZone: _flinchZone,
            TenseRun: _tenseRunThisTick,
            Forward: WalksForward));

        // 0x45D080 clears the flinch bit straight after the machine's tick: it is a one-tick pulse, and a
        // state that does not test it on that tick loses it.
        _flinchZone = -1;

        // ...and flips the run byte on a slot-20 press, after the machine has read it (see WalkGait).
        WalkGait = _runByte;
        if (_gaitPress)
        {
            _runByte = !_runByte;
            _gaitPress = false;
        }

        return state;
    }

    /// <summary>Raises the flinch bit a hit that passed the soldier's recoil rule sets.</summary>
    /// <param name="zone">The zone the hit struck — <c>Human+744</c>, which names the flinch clip.</param>
    public void Flinch(int zone) => _flinchZone = zone;

    // The zone of this tick's flinching hit, or -1: machine+8's 0x10000 bit with Human+744 beside it.
    private int _flinchZone = -1;

    // Whether the gunner took its gun this tick, for the body's object dispatch.
    private bool _seatedOnGunThisTick;

    /// <summary>Keeps a mounted gunner on the gun as it turns: behind the barrel, facing along it, at his own height.</summary>
    /// <remarks>
    /// The entry attaches the body to the gun (<c>0x463310</c> keeps its place in the gun's frame, <c>0x4635F0</c> sets
    /// the attached flag) and the human's frame is then the gun's times that place (<c>0x4610C0</c>), so the gunner
    /// swings round the stand with the traverse. The seat is the same point in the gun's turned frame either way.
    /// </remarks>
    public void RideMountedGun()
    {
        if (MountedGun is { } gun)
        {
            SeatOn(gun);
        }
    }

    // State 36's entry (0x45C8B0) stands the body behind the gun: it takes the gun's position and frame (0x444B30), moves
    // 2867.2 units along the frame's axis in X and Y only, and copies Z from the human's own Human+48 -- the mount
    // never changes the body's height. The gun's position is its pivot, 1.3 m over the deck on mission 8's M2HBs;
    // taking its Z stood the gunner in mid-air.
    private void SeatOn(AiStationaryGun gun)
    {
        Vector3d mount = gun.MountPosition;
        Position = new Vector3d(mount.X, mount.Y, Position.Z);
        Facing = gun.MountFacing;
    }

    // The frame slot the use-object signal reads -- channel 4's.
    private const int UseObjectSlot = 24;

    /// <summary>The input frame this tick's writes put together — <c>Human+432</c>.</summary>
    /// <param name="dropUseObject">Leave out slot 24, which the object dispatch has just consumed.</param>
    private PlayerInputFrame WrittenFrame(bool dropUseObject = false)
    {
        PlayerInputFrame frame = default;
        foreach (AiChannelWrite write in _channelWrites)
        {
            if (write.Slot != AiCombatChannels.None && !(dropUseObject && write.Slot == UseObjectSlot))
            {
                frame[write.Slot] = write.Value;
            }
        }

        foreach (AiRawInputWrite write in _rawInputWrites)
        {
            frame[write.Slot] = write.Value;
        }

        if (Combat.PullTrigger)
        {
            frame[(int)PlayerInputSlot.Fire] = 1f;
        }

        return frame;
    }

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

        return Vector3d.Distance(Position, position);
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

    /// <summary>Installs whatever the archetype script asked for on the last dispatch.</summary>
    /// <param name="actions">The produced actions, in script order.</param>
    /// <remarks>
    /// <para>
    /// The original hands one produced action to <c>0x450E70</c> and several to <c>0x450F10</c>, and both do
    /// the same three things. First <c>0x450EE0</c> ends every action above the first pushable one on the
    /// stack. Then each produced action goes on at the <b>head</b> and its start function runs.
    /// </para>
    /// <para>
    /// The produced list is built in script order (each <c>AIAction_*</c> appends at the tail through
    /// <c>0x450E10</c> -> <c>0x4AF8F0</c>), and <c>0x450F10</c> walks it from the TAIL back, adding each to the
    /// head (<c>0x4AF910</c>). So the FIRST action the script named ends up on top and runs first: a guard's
    /// COMBAT branch, <c>Activate(alarm trigger)</c> then <c>Combat</c>, runs to the alarm and fights after.
    /// Installing in script order had the fight on top, and a guard that saw you never went for the alarm.
    /// </para>
    /// <para>
    /// Every action takes the flag word of the first one walked -- the LAST the script named. A start that
    /// finishes its own action (SetCombat posts COMBAT; a refused Activate never begins) leaves nothing on the
    /// stack.
    /// </para>
    /// </remarks>
    public void ApplyActions(IReadOnlyList<AiProducedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        // A copy: the script host reuses its produced-action list for the next soldier it runs.
        LastActions = [.. actions];
        if (actions.Count == 0)
        {
            return;
        }

        _pendingActions.PopToPushable(action => EndAction(action.Name));
        bool pushable = (FlagsOf(actions[^1]) & AiActionFlagWord.Pushable) != 0;
        for (int index = actions.Count - 1; index >= 0; index--)
        {
            AiProducedAction produced = actions[index];
            // 0x450F10 draws the 6..11-tick countdown before each start. Only an action with no run
            // function waits on it, but every AI subsystem shares this random stream.
            int countdown = (int)((_random.NextDouble() + 1.0) * 0.2 * 30.0);
            AiPendingAction? node = null;
            node = new AiPendingAction(produced.Slot, countdown, () => RunAction(node!), pushable, produced.Name);
            _pendingActions.Push(node);
            StartAction(produced);
            if (!IsActionActive(produced.Name))
            {
                FinishAction(node);
            }
        }
    }

    // The flag word is every AIAction_* native's last argument (0x44D0E0, 0x44D160, 0x44D6E0, ...).
    private static AiActionFlagWord FlagsOf(AiProducedAction action) =>
        AiScriptApi.Natives.FirstOrDefault(native => native.Name == action.Name) is { } native
            ? (AiActionFlagWord)action.Argument(native.ArgumentCount - 1)
            : AiActionFlagWord.None;

    /// <summary>Runs an action's start function — the <c>+20</c> each <c>AIAction_*</c> wrapper installs.</summary>
    private void StartAction(AiProducedAction action)
    {
        switch (action.Name)
        {
            case ActionPatrol:
                // The first argument is a patrol PATH id: a PatrolPath task whose child tasks are the
                // commands. Resolving it is the whole point of the registry.
                BeginPatrol(action.Argument(0));
                break;

            case "AIAction_RunToNode":
                _scriptedRunGait = true;
                BeginGoTo(action.Argument(0));
                break;

            case "AIAction_WalkToNode":
                _scriptedRunGait = false;
                BeginGoTo(action.Argument(0));
                break;

            case ActionCombat:
                EnterCombat();
                break;

            case "AIAction_SetCombat":
                // 0x48E560 does NOT install a combat arm. It synthesizes an AIEVENT_COMBAT record
                // carrying the triggering event's position, posts it to this AI's OWN queue
                // (sub_450D50) and ends itself at once (sub_450FF0). The arm arrives on the next
                // tick, when that COMBAT event dispatches and the script's own COMBAT branch runs --
                // which is also where guard.qvm keeps its run-to-the-alarm logic, and what places
                // AIAction_Combat on the action stack at COMBAT's own priority position so nothing
                // ranked below it (further gunshots, footsteps) can interrupt the engagement.
                // Treating SetCombat as a direct combat install skipped all three: every heard
                // event re-entered combat, wiped the hunt route, and the guard never went anywhere.
                Events.Post(
                    new AiEventRecord(
                        AiEvent.Combat,
                        -1,
                        AiEventFilter.None,
                        0.0f,
                        _currentEventPosition ?? TargetMark,
                        _currentEventSource,
                        _currentEventSubject),
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
                break;

            case ActionActivate:
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
                    BeginGoTo(NearestNode(toInvestigate));
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
                    _route.Clear();
                    DestinationNode = NearestNode(Position);
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Plans a walk to a graph node — <c>0x4518E0</c>.</summary>
    /// <param name="node">The destination node id.</param>
    /// <param name="point">
    /// An exact point to finish at inside the destination node, or <see langword="null"/> for its centre.
    /// </param>
    /// <param name="claim">
    /// Whether the walk claims its destination's cell first (<c>0x4518E0</c>'s fourth argument), as the patrol
    /// walker, WalkToNode, RunToNode and MoveToEvent do.
    /// </param>
    /// <returns><see langword="true"/> when the whole route was planned.</returns>
    /// <remarks>
    /// <para>
    /// The queue is of <b>points</b>, not nodes. Where a hop leaves a node the walker aims for the edge of
    /// that node's square on the line to the next one, and where it arrives, for the edge it crosses
    /// (<c>0x4520C0</c>); the destination's own centre — or the exact point, when it lies inside the node —
    /// is last. So a soldier cuts across a node's square instead of touching every centre.
    /// </para>
    /// <para>
    /// It starts from the current node when the soldier is standing in it. Otherwise it first walks back into
    /// a node: the current one, or the next node of the old queue when the soldier is already inside that,
    /// or when that one is nearer the destination by the route's cost than the current node is by distance.
    /// </para>
    /// <para>
    /// A route that breaks off (<c>"Error in graph %d routenet"</c>) keeps what was built and the goal, as
    /// the original does, and the walk carries on along it.
    /// </para>
    /// </remarks>
    public bool GoTo(int node, Vector3d? point = null, bool claim = false)
    {
        if (_graph?.RouteTable is not { } routes || _graph.NodeAt(node) is not { } target)
        {
            return false;
        }

        // 0x4518E0's fourth argument: the walk claims its destination's cell before it plans (sub_452D20),
        // and the claimed position is the one a later RePosition walks back to.
        if (claim && _occupancy is not null)
        {
            _combatFreePosition = _occupancy.ClaimAt(_graph, target, this, point ?? target.Position);
        }

        int start = RouteStart(node, routes);
        _route.Clear();
        DestinationNode = node;

        int at = start == -1 ? CurrentNode : start;
        if (start != -1)
        {
            GraphNode entered = _graph.NodeAt(start)!;
            Vector3d toward = new(entered.Position.X - Position.X, entered.Position.Y - Position.Y, 0.0);
            _route.Add(new AiWaypoint(EdgePoint(entered, toward, exit: false), start, Door: false));
        }

        if (at == node)
        {
            _route.Add(new AiWaypoint(point ?? target.Position, node, Door: false));
            return true;
        }

        while (at >= 0
            && routes.TryGetStep(at, node, out int next, out _)
            && _graph.NodeAt(at) is { } from
            && _graph.NodeAt(next) is { } to)
        {
            bool door = IsDoorNode(from) && IsDoorNode(to);
            Vector3d hop = to.Position - from.Position;
            if (next == node)
            {
                bool atPoint = false;
                if (point is { } exact)
                {
                    _route.Add(new AiWaypoint(EdgePoint(to, hop, exit: false), node, door));
                    atPoint = IsInsideNodeBox(exact, to, NodeDevianceMetres);
                }

                _route.Add(atPoint
                    ? new AiWaypoint(new Vector3d(point!.Value.X, point.Value.Y, to.Position.Z), node, Door: false)
                    : new AiWaypoint(to.Position, node, door));
                return true;
            }

            // The first hop leaves by the current node's edge only from inside it.
            if (at != CurrentNode || IsInsideNodeBox(Position, from, NodeDevianceMetres))
            {
                _route.Add(new AiWaypoint(EdgePoint(from, hop, exit: true), at, door));
            }

            _route.Add(new AiWaypoint(EdgePoint(to, hop, exit: false), next, Door: false));
            at = next;
        }

        return false;
    }

    // The deviance every node-square test in the planner and the walker adds to the radius -- 0.3 m.
    private const double NodeDevianceMetres = 0.3;

    // Which node the planner starts from, or -1 to walk from the current node as it stands.
    private int RouteStart(int destination, GraphRouteTable routes)
    {
        if (CurrentNode < 0
            || _graph!.NodeAt(CurrentNode) is not { } current
            || IsInsideNodeBox(Position, current, NodeDevianceMetres))
        {
            return -1;
        }

        double fromCurrent = Vector3d.Distance(current.Position, Position);
        int next = -1;
        foreach (AiWaypoint waypoint in _route)
        {
            if (waypoint.Node != CurrentNode)
            {
                next = waypoint.Node;
                break;
            }
        }

        if (next < 0 || _graph.NodeAt(next) is not { } ahead)
        {
            return CurrentNode;
        }

        if (IsInsideNodeBox(Position, ahead, NodeDevianceMetres))
        {
            return next;
        }

        if (next == destination)
        {
            return Vector3d.Distance(ahead.Position, Position) < fromCurrent ? next : CurrentNode;
        }

        // "ERROR IN ROUTENET" leaves the current node as the start.
        return routes.TryGetStep(next, destination, out _, out float cost) && cost < fromCurrent
            ? next
            : CurrentNode;
    }

    /// <summary>
    /// Where a line out of a node's centre leaves its square — <c>0x4520C0</c>.
    /// </summary>
    /// <param name="node">The node, whose square is its radius either side of the centre on the world axes.</param>
    /// <param name="direction">The line's direction; only its horizontal part is used.</param>
    /// <param name="exit">
    /// <see langword="true"/> for the edge the line leaves by, <see langword="false"/> for the one it enters by.
    /// </param>
    /// <returns>The point on the edge at the centre's height, or the centre when the line is too short to leave.</returns>
    private static Vector3d EdgePoint(GraphNode node, Vector3d direction, bool exit)
    {
        double half = node.Radius * GraphNode.MetreInUnits;
        double dx = direction.X;
        double dy = direction.Y;
        double offsetX;
        double offsetY;
        if (Math.Abs(dx) > half && Math.Abs(half / dx * dy) <= half)
        {
            offsetX = Math.CopySign(half, dx);
            offsetY = Math.Abs(half / dx) * dy;
        }
        else if (Math.Abs(dy) > half && Math.Abs(half / dy * dx) <= half)
        {
            offsetX = Math.Abs(half / dy) * dx;
            offsetY = Math.CopySign(half, dy);
        }
        else
        {
            return node.Position;
        }

        return exit
            ? new Vector3d(node.Position.X + offsetX, node.Position.Y + offsetY, node.Position.Z)
            : new Vector3d(node.Position.X - offsetX, node.Position.Y - offsetY, node.Position.Z);
    }

    private static bool IsDoorNode(GraphNode node) => (node.Criteria & GraphNodeCriteria.Door) != 0;

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
        _deathClipEnded = false;
        Route = null;
        _route.Clear();
        DestinationNode = -1;
        _scriptedFire = null;
        _panicDestination = -1;
        _fallFlatDeadline = -1;
        _goToNode = -1;
        _combatInstalled = false;
        _runningAction = null;
        Combat = new AiCombatCommand();
        _channelWrites.Clear();
        _rawInputWrites.Clear();
        _patrolOneShotChild = null;
        _patrolRollChild = null;
        _patrolProne = false;
        _patrolKneeling = false;
        _deadClaimsReleased = false;
        _combatFreePosition = null;
        _gunnerCombat?.End();

        // Death leaves the shared target record. The original's combat action ends when its owner dies
        // and the end function (0x48D7D0) runs sub_4512A0, which swap-removes this attacker and frees
        // the record when it was the last. Without the release a dead guard kept holding its AIMING
        // slot, and once four had died mid-fight Count(Aiming) stayed >= 4 for ever -- every guard that
        // engaged afterwards was pushed to Crowded and stood there, seeing the player and doing
        // nothing, until the level was restarted.
        SetTarget(null);

        // Death ends every action on the stack. The claims -- the alarm panel's handles at ai+15024/+15028 and
        // the node cell at +16032 -- are kept until the death clip has run (see TickDeadAi).
        _pendingActions.Clear();
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
    /// <see cref="Igi1HumanAi"/> is the managed equivalent: CREATE will run again on its first tick, while
    /// immutable level services (graph, scripts, collision queries and combat row) are reattached here.
    /// The caller must assign a unique <see cref="RuntimeId"/> before registering the new brain alongside
    /// retained corpses, and rebind view-tracking callbacks to its new rendered body.
    /// </remarks>
    public Igi1HumanAi Respawned(
        Vector3d position,
        float facing,
        Func<Igi1HumanAi, AiCombatWeaponState>? weapon = null)
    {
        int seed = Math.Max(0, RuntimeId - 1);
        Igi1HumanAi fresh = new(_graph, position, facing, seed & 3, seed, _random, SoldierClass)
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

    // Whether the death clip has ended, after which the corpse no longer slides.
    private bool _deathClipEnded;

    /// <summary>
    /// A dead soldier's HumanAI tick, before its body's: once the death clip has run (Human+828, set by
    /// <c>0x45C740</c> the tick after the clip ends), <c>0x44F410</c> lets go of the alarm panel's claim handles and
    /// its node cell, so a guard shot on its way to the alarm leaves it for the next one only once the body lies still.
    /// </summary>
    public void TickDeadAi()
    {
        if (Activity != AiSoldierActivity.Dead || !_deathClipEnded || _deadClaimsReleased)
        {
            return;
        }

        _deadClaimsReleased = true;
        ReleaseActivateClaims();
        _occupancy?.ReleaseDead(this);
    }

    // Whether TickDeadAi has let go of the dead soldier's claims.
    private bool _deadClaimsReleased;

    /// <summary>
    /// Steps a dead body for one tick — the death state's per-tick (<c>0x45C700</c>) joined to the soldier
    /// update's ordinary root-motion move and ground query (<c>0x45D080</c>).
    /// </summary>
    /// <param name="rootMotionDelta">
    /// What the death clip's own root-motion track moved this tick, in the body's model space. The soldier
    /// update rotates exactly this into world space and hands it to the movement integrator.
    /// </param>
    /// <param name="deathClipEnded">Whether the death clip ended this tick, after which the corpse stops sliding.</param>
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
    public void TickDeadBody(Vector3f rootMotionDelta, bool deathClipEnded)
    {
        if (Activity != AiSoldierActivity.Dead)
        {
            return;
        }

        Vector3d local = new(rootMotionDelta.X, rootMotionDelta.Y, rootMotionDelta.Z);
        Vector3d world = SoldierDeathFall.RotateAboutZ(local, Facing);

        // 0x45C700 adds the fall solver's velocity until the death clip ends, then hands the corpse to
        // 0x45C740, which adds nothing more -- from the tick after: the tick the clip ends still slides.
        Vector3d slide = _deathClipEnded ? default : DeathFallVelocity;
        _deathClipEnded |= deathClipEnded;
        double x = Position.X + world.X + slide.X;
        double y = Position.Y + world.Y + slide.Y;
        double z = Position.Z + world.Z + slide.Z;

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
    /// How far the soldier's current animation moves it this tick along its own forward axis, in engine
    /// units — signed, so a clip that steps back moves the soldier back. The engine moves a human by its
    /// clip's root motion and nothing else, so this is the only speed source.
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
    /// <param name="rootMotionLateral">The clip's root motion along the body's own right axis, in engine units.</param>
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
        bool enemyIsSoldier = false,
        double rootMotionLateral = 0.0)
    {
        // One human on offer. Without a body position the point given is tested as it is; with one, the
        // mark is the body's own mid-point.
        Vector3d feet = enemyFeet ?? enemy;
        Vector3d mark = enemyFeet is null
            ? enemy
            : new Vector3d(
                feet.X,
                feet.Y,
                feet.Z + DetectionMarkAboveFeet(enemyCrouching));
        AiDetectionCandidate candidate = new(
            enemyIsSoldier ? OfferedSoldierId : AiDetectionCandidate.PlayerId,
            OtherSide: enemyIsSoldier || PlayerIsEnemy,
            Alive: enemyAlive,
            mark,
            feet,
            enemyCrouching,
            _defaultTarget);
        TickAmong(tick, [candidate], rootMotionThisTick, alarmOn, alarmWasOn, rootMotionLateral);
    }

    // The id the one-candidate form gives an offered soldier.
    private const int OfferedSoldierId = int.MaxValue;

    /// <summary>Runs one 30 Hz tick against every human the scan offers.</summary>
    /// <param name="tick">The engine tick counter, which the detection phase gate keys off.</param>
    /// <param name="candidates">
    /// Every human the scan offers this tick, in the global human list's order — the player and every
    /// soldier, each described from this soldier's side.
    /// </param>
    /// <param name="rootMotionThisTick">The clip's root motion along the body's forward axis, signed, in units.</param>
    /// <param name="alarmOn">Whether an alarm the soldier watches is sounding.</param>
    /// <param name="alarmWasOn">Whether that alarm sounded on the preceding tick, for the falling-edge event.</param>
    /// <param name="rootMotionLateral">The clip's root motion along the body's own right axis, in engine units.</param>
    public void TickAmong(
        int tick,
        IReadOnlyList<AiDetectionCandidate> candidates,
        double rootMotionThisTick,
        bool alarmOn = false,
        bool alarmWasOn = false,
        double rootMotionLateral = 0.0)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (Activity == AiSoldierActivity.Dead)
        {
            return;
        }

        _lastTick = tick;
        _rootMotionThisTick = rootMotionThisTick;
        _rootLateralThisTick = rootMotionLateral;
        OfferCandidates(candidates);
        _enemyAlive = TargetHuman?.Candidate.Alive ?? true;

        // 0x44F410 zeroes the whole input-channel block (+15476, 0xA0 bytes) before anything runs: only the
        // action that runs this tick writes to it, so a suspended one leaves nothing behind.
        _rawInputWrites.Clear();
        _channelWrites.Clear();
        _gaitPress = false;
        WalksForward = false;
        _tenseRunThisTick = false;
        Combat = new AiCombatCommand();
        DetectionShoutRequestedThisTick = false;
        AlertPingDueThisTick = false;

        // A scripted one-shot clip releases the animation slot the moment the renderer says it ended, so the
        // soldier goes idle and its script's IDLE branch may request the clip again. That holds for staged
        // cutscene actors too: 0x48DEC0 completes PlayAnimation once channel 3 leaves the clip, and the
        // original replays mission 8's guard 3336 (clip 49) and mission 1's 1666 (clip 242) every cycle,
        // each replay adding the clip's root motion again.
        if (_scriptedAnimation && _animationFinished)
        {
            _animationFinished = false;
            RequestedAnimation = -1;
            _scriptedAnimation = false;
        }

        // An AI's FIRST event is always its creation: before initialisation only type 0 passes admission, and
        // the mask is not consulted at all. Never posting it left every archetype script's
        // CREATE branch unrun -- which is where guard.qsc sets its view length, alarm length, alpha and
        // gamma. Guards were therefore seeing through the ENGINE DEFAULTS from 0x44F060 (+/-57.3 degrees
        // vertical, +/-59.5 horizontal, 26 m) instead of the archetype's own 40 m / 50 m / 50 / 45.
        //
        // And that is ALL the first tick does. 0x44F410's first pass (+15636 still clear) dispatches CREATE and
        // jumps straight to the pump and the action runner (0x44FD50, 0x44FEC0): no sweep, no detection, no
        // alarm or idle-animation check and no IDLE, which the next tick is the first to post. Probed on
        // mission 4: CREATE on an AI's first tick, IDLE -- and with it the patrol -- one tick later, at level
        // start and when a container opens alike. The port posted IDLE alongside CREATE, and every soldier ran
        // a tick ahead of retail's, its sweeps a tick out of phase.
        if (!_created)
        {
            _created = true;
            if (HasScript)
            {
                Events.DispatchCreate(Dispatch);
            }
            else
            {
                Events.Post(AiEventRecord.Simple(AiEvent.Create), owner: 0, worldTests: null);
            }

            RunScript();
            if (_pendingActions.Tick() is null)
            {
                RunWithoutAction();
            }

            StepBodyOrWait();
            return;
        }

        // 0x44F410: the first tick the AI finds its Human dead (0x451330) ends every action on the stack (0x450FF0 for
        // each), hands AIEVENT_DEAD to the script there and then (0x44FC90), and latches +15034. A dead Human is never
        // scanned again.
        if (HumanDead)
        {
            if (!_deathDispatched)
            {
                _deathDispatched = true;
                foreach (AiPendingAction action in _pendingActions.Actions.ToArray())
                {
                    FinishAction(action);
                }

                if (HasScript)
                {
                    Dispatch(AiEvent.Dead, PriorityPositionOf(AiEvent.Dead));
                }
            }

            return;
        }

        // The idle-animation deadline (ai+15200): passing it posts AIEVENT_ANIMATION and re-arms.
        if (_nextAnimationTick >= 0 && tick >= _nextAnimationTick)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Animation), owner: 0, worldTests: null);
            _nextAnimationTick = tick + _animationMinTicks
                + NextBounded(_animationRandomTicks);
        }

        // The detection scan (0x489750) runs on the soldier's phase tick: it registers each human by the
        // mode-6 rule -- other side and alive into the enemy list, own side and dead into the fallen-friend
        // list -- unregisters the rest, and sweeps every registered slot through the cone and then the
        // sight ray behind it (0x4898D0), the expensive test paid only for someone inside a cone.
        if (_schedule.SweepsOn(tick))
        {
            Scan(tick);
        }
        else if (!_registeredOnce)
        {
            // A soldier the level has just handed its humans has, in the original, been scanning since the
            // level began: the humans it would have registered are registered already.
            Register(tick);
        }

        _registeredOnce = true;

        // Before a fight the soldier's attention goes to the first enemy in sight, in registration order --
        // the one whose detection event the pump will hand the script first.
        if (!_combatInstalled && _enemies.Find(human => human.Slot.IsVisible) is { } seen)
        {
            _targetId = seen.Candidate.Id;
        }

        if (TargetSlot.IsVisible)
        {
            LastEventDistance = TargetSlot.Distance;
            _lastSeenFeet = TargetFeet;
            SetTarget(TargetRecord);
            Target?.Refresh(_lastSeenFeet);
        }

        // 0x44F410 posts ENEMYDETECTION for every enemy in sight and FRIENDLYDETECTION for every fallen
        // friend in sight, every tick, each naming its human -- which is who a SetCombat it triggers fights.
        // No shipped script declares FRIENDLYDETECTION, so admission drops those, as in the original. Each post
        // reads the human's life as it is now (0x463170), not as the last sweep registered it: an enemy killed since
        // stays in the list, and in sight, until the next sweep, but is no longer posted.
        foreach (TrackedHuman enemy in _enemies)
        {
            if (enemy.Slot.IsVisible && enemy.Candidate.Alive)
            {
                PostDetection(AiEvent.EnemyDetection, enemy);
            }
        }

        foreach (TrackedHuman friend in _fallenFriends)
        {
            if (friend.Slot.IsVisible && !friend.Candidate.Alive)
            {
                PostDetection(AiEvent.FriendlyDetection, friend);
            }
        }

        if (alarmOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOn), owner: 0, worldTests: null);
        }
        else if (alarmWasOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOff), owner: 0, worldTests: null);
        }

        // 0x44F410 recomputes the alert latch from scratch every tick from two causes only: the alarm, or a
        // combat record (+15016) whose target the detection scan still registers (0x489BE0 tests the slot's
        // registered byte, not its visibility). Hearing, damage and alerts do not set it, and a fighting
        // guard that loses sight keeps the alarm view length for as long as its target lives.
        Alerted = alarmOn
            || (_combatInstalled && Target is not null && TargetSlot.IsRegistered);

        // 0x44F410 posts no COMBAT of its own -- that is the SetCombat runner's (0x48E560), which the script
        // reaches from ENEMYDETECTION -- and posts IDLE whenever the action stack is empty (0x450DC0).
        // Declared priority, not a pending-event test, keeps IDLE behind the events that matter: a "nothing
        // pending" condition never let IDLE through while the alarm posted ALARMON every tick, which froze
        // a guard in place for the rest of the alarm.
        if (!_pendingActions.HasPending)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Idle), owner: 0, worldTests: null);
        }

        // ONE event type dispatches per tick, in priority order, and the script decides what to do about it.
        RunScript();

        // 0x44FEC0, every tick, after the pump: the head of the action stack runs, and only the head.
        // Whatever lies beneath it is suspended rather than cancelled, and carries on once everything
        // above it has finished -- a pushable fight outlasts a flashbang's flinch.
        if (_pendingActions.Tick() is null)
        {
            RunWithoutAction();
        }

        StepBodyOrWait();
    }

    /// <summary>Takes this tick's humans: their descriptions update, and anyone gone from the list is dropped.</summary>
    private void OfferCandidates(IReadOnlyList<AiDetectionCandidate> candidates)
    {
        HashSet<int> offered = [];
        foreach (AiDetectionCandidate candidate in candidates)
        {
            offered.Add(candidate.Id);
            if (_tracked.TryGetValue(candidate.Id, out TrackedHuman? human))
            {
                human.Candidate = candidate;
            }
            else
            {
                _tracked[candidate.Id] = new TrackedHuman(candidate);
            }
        }

        foreach (int gone in _tracked.Keys.Where(id => !offered.Contains(id)).ToArray())
        {
            Unregister(_tracked[gone]);
            _tracked.Remove(gone);
        }

        // With nobody yet in mind, the soldier's attention rests on the first human on offer.
        if (!_tracked.ContainsKey(_targetId) && candidates.Count > 0)
        {
            _targetId = candidates[0].Id;
        }
    }

    /// <summary>One pass of the detection scan — <c>0x489750</c> and <c>0x4898D0</c>.</summary>
    private void Scan(int tick)
    {
        Register(tick);

        Eye = Vision.Capture(EyePosition(), EyeOrientation(), Alerted);
        AiEyeSnapshot snapshot = Eye;
        Vector3d eye = EyePosition();
        foreach (TrackedHuman human in _enemies.Concat(_fallenFriends))
        {
            Vector3d mark = human.Candidate.Mark;
            int looked = human.Candidate.Id;
            _schedule.Sweep(
                human.Slot,
                () => snapshot.IsVisible(mark),
                () =>
                {
                    SightCast?.Invoke(eye, mark, looked);
                    return _sight is null || _sight(eye, mark);
                },
                Vector3d.Distance(eye, mark));
        }
    }

    // The scan's classification: other side and alive into the enemy list, own side and dead into the
    // fallen-friend list, anything else out.
    private void Register(int tick)
    {
        foreach (TrackedHuman human in _tracked.Values)
        {
            AiDetectionCandidate candidate = human.Candidate;
            List<TrackedHuman>? list = candidate.OtherSide && candidate.Alive ? _enemies
                : !candidate.OtherSide && !candidate.Alive ? _fallenFriends
                : null;
            if (list is null)
            {
                Unregister(human);
            }
            else if (!human.Slot.IsRegistered)
            {
                human.Slot.Register(tick);
                list.Add(human);
            }
        }
    }

    private void Unregister(TrackedHuman human)
    {
        human.Slot.IsVisible = false;
        human.Slot.IsInCone = false;
        human.Slot.Unregister();
        _enemies.Remove(human);
        _fallenFriends.Remove(human);
    }

    private void PostDetection(AiEvent type, TrackedHuman human) =>
        Events.Post(
            new AiEventRecord(
                type, -1, AiEventFilter.None, 0.0f, human.Candidate.Feet, human.Candidate.Id, human.Candidate.Id),
            owner: 0,
            worldTests: null);

    /// <summary>Runs one tick of an action the stack chose — its <c>+24</c> — and ends it when it is done.</summary>
    private void RunAction(AiPendingAction action)
    {
        _runningAction = action.Name;
        int tick = _lastTick;
        switch (action.Name)
        {
            case ActionCombat:
                RunCombat(tick);
                break;

            case ActionPatrol:
                RunPatrolCommand(tick);
                break;

            case ActionActivate:
                // 0x48E7D0 sets the gait from its run argument every tick.
                SetGait(walking: _activate is not { Run: true });
                RunActivate();
                break;

            case "AIAction_FallFlat":
                RunFallFlat(tick);
                break;

            case "AIAction_Stunned":
                RunStunned(tick);
                break;

            case "AIAction_FireAtNode":
            case "AIAction_FireAtTask":
                Activity = AiSoldierActivity.FiringAtTask;
                RunScriptedFire();
                break;

            case "AIAction_RunPanicking":
                RunPanicking();
                break;

            case "AIAction_LookAtEvent":
                RunLookAtEvent(tick);
                break;

            case "AIAction_WalkToNode":
            case "AIAction_RunToNode":
            case "AIAction_MoveToEvent":
                // 0x48EBF0 / 0x48EC50 / 0x48E020 set their gait every tick.
                SetGait(walking: !_scriptedRunGait);
                if (StepTowardsNode(_goToNode))
                {
                    _goToNode = -1;
                    Activity = AiSoldierActivity.Idle;
                }

                break;

            case "AIAction_Idle":
                // 0x48E520 -> 0x4513F0 only brings the view back to level, at the pitch clamp; it never
                // ends, so an idle AI stays idle until something is pushed over it.
                Activity = AiSoldierActivity.Idle;
                _pitchInput = Math.Clamp(-ViewPitch, -_steering.PitchClamp, _steering.PitchClamp) / MathF.PI;
                break;

            case "AIAction_PlayAnimation":
                // 0x48DEC0 holds the clip on animation channel 3 until it has played; the body is the
                // clip's, whatever fight it was pushed over.
                Activity = AiSoldierActivity.Idle;
                break;

            default:
                // KickGrenade's runner (0x48E250) does nothing.
                break;
        }

        if (!IsActionActive(action.Name))
        {
            FinishAction(action);
        }
    }

    /// <summary>What the soldier does with nothing on its stack.</summary>
    /// <remarks>
    /// A scripted AI with no action writes no channel and stands. A soldier without a script keeps the
    /// built-in fallback's one habit: it walks a route it was sent along.
    /// </remarks>
    private void RunWithoutAction()
    {
        _runningAction = null;
        if (HasScript || _route.Count == 0)
        {
            _scriptedRunGait = false;
            Activity = AiSoldierActivity.Idle;
            return;
        }

        Activity = AiSoldierActivity.Patrolling;
        Advance();
    }

    /// <summary>The combat action's per-tick: the arm, unless its target is gone.</summary>
    private void RunCombat(int tick)
    {
        // The target is dead: leave combat outright rather than keep firing at where it fell. The
        // unregister (0x4894E0) is immediate, and 0x44F410 releases the combat record whose target the
        // scan no longer registers, so this is too. The body is left as it is: the end (0x48D7D0) releases the
        // record and nothing else, and a soldier caught mid-burst runs its fire clip out. Follow, RePosition and
        // LookForTarget hold the tense run (slot 26) before they find the target gone, so a chaser stands up from
        // the run as its fight ends.
        if (!_enemyAlive)
        {
            if (IsPatrolMovementInstalled)
            {
                _tenseRunThisTick = true;
            }

            EndCombat();
            return;
        }

        Activity = AiSoldierActivity.Engaging;
        if (_combatEntryTick)
        {
            _combatEntryTick = false;
            return;
        }

        Fight(tick, _lastSeenFeet);
    }

    // The patrol arm's stance entry still to run: a tick that only installs the stance per-tick.
    private bool _combatEntryTick;

    /// <summary>
    /// The LookAtEvent per-tick (<c>0x48E0A0</c>): sets its deadline on its first tick, turns to the event
    /// through the turn-to-point entry, and completes once it is both aimed inside 2 degrees and past it.
    /// </summary>
    private void RunLookAtEvent(int tick)
    {
        Vector3d watched = _lookAtPoint!.Value;
        if (_lookAtDeadline < 0)
        {
            _lookAtDeadline = tick + _lookAtDurationTicks;
        }

        Activity = AiSoldierActivity.Idle;
        if (TurnTowards(watched, (float)AiGuardCombat.AimToleranceRadians) && tick > _lookAtDeadline)
        {
            _lookAtPoint = null;
        }
    }

    /// <summary>
    /// The Activate per-tick (<c>0x48E7D0</c>): walk to the panel's node, press on arrival, release the
    /// claims. Retail picks the gait from the action's run flag and presses through input channel 4 -- the
    /// port walks its ordinary route and invokes the activation directly; both are recorded stand-ins.
    /// </summary>
    private void RunActivate()
    {
        ActivateTask pressing = _activate!.Value;
        if (!StepTowardsNode(pressing.Node))
        {
            Activity = AiSoldierActivity.Patrolling;
            return;
        }

        bool mountedGun = false;
        if (pressing.TaskId == GunnerId
            && _gunnerCombat is not null
            && _stationaryGun?.Invoke(pressing.TaskId) is { } gun)
        {
            // StationaryGun's player interaction is a temporary input state. Calling its generic
            // BeginUse/CompleteUse pair here would mount a private player sentinel and immediately
            // release it, leaving the scripted gunner visibly beside an empty M2HB. The AI arm owns
            // the real occupant.
            _activateTask?.Invoke(pressing.TaskId);
            mountedGun = _gunnerCombat.MountImmediately(gun);
            if (mountedGun)
            {
                _seatedOnGunThisTick = true;
                SeatOn(gun);
            }
        }
        else
        {
            _activateTask?.Invoke(pressing.TaskId);
        }

        ReleaseActivateClaims();
        Activity = mountedGun ? AiSoldierActivity.Engaging : AiSoldierActivity.Idle;
    }

    /// <summary>The RunPanicking per-tick: run to the chosen node, and end on arrival.</summary>
    private void RunPanicking()
    {
        if (StepTowardsNode(_panicDestination))
        {
            _panicDestination = -1;
            Activity = AiSoldierActivity.Idle;
            return;
        }

        Activity = AiSoldierActivity.Panicking;
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
    /// <see cref="AiPatrolCommandKind.SetSpeed"/> wedges a human patrol — reproduced rather than
    /// smoothed over.
    /// </para>
    /// </remarks>
    private void RunPatrolCommand(int tick)
    {
        AiPatrolRoute route = Route!;

        // 0x48E920 sets the gait from the cursor's flag before it looks at the command, so a RunTo changes it from
        // the tick after: the flag is the one the last movement command left, or a new cursor's walk.
        SetGait(route.IsWalking);
        if (route.Current is not { } command)
        {
            Activity = AiSoldierActivity.Idle;
            return;
        }

        // The crouch flag is written to the kneel channel at the top of every tick, before the command
        // runs: a Crouch command kneels from the tick after it, and the first tick of a walk still kneels.
        if (route.IsCrouching)
        {
            WriteChannel(AiCombatChannel.Kneel, 1f);
        }

        bool finished = route.Begin(tick);

        switch (command.Kind)
        {
            case AiPatrolCommandKind.Animation:
                // 0x48E920's stand action: the clip goes to the body through channel 3 once, which puts it in the
                // play-animation state (35) until the clip ends, and the command is done when the body is back in
                // its stand state -- its kneel, under a Crouch.
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                if (route.AnimationRequested)
                {
                    WriteChannel(AiCombatChannel.PlayAnimation, command.Operand);
                    finished = false;
                }
                else
                {
                    finished = AiCombatChannels.IsInStance(
                        _type, route.IsCrouching ? AiStanceChannel.Kneeling : AiStanceChannel.Standing, CombatState);
                }

                break;

            case AiPatrolCommandKind.Delay:
                Activity = AiSoldierActivity.Idle;
                RequestedAnimation = -1;
                break;

            case AiPatrolCommandKind.WalkTo:
            case AiPatrolCommandKind.RunTo:
                RequestedAnimation = -1;
                finished = StepTowardsNode(command.Operand);
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

    /// <summary>
    /// The walk to a node that the patrol walker, the go-to actions, the alarm run and panic share —
    /// <c>0x4522A0</c> into <c>0x4518E0</c>.
    /// </summary>
    /// <returns><see langword="true"/> on the tick the walk ends.</returns>
    /// <remarks>
    /// It plans only when the goal changes, and walks the queue otherwise, so a soldier pushed off its line
    /// does not re-plan. A plan that fails returns before stepping; the goal is then set, and on the next tick
    /// the soldier walks whatever part of the route was built and ends when it runs out.
    /// </remarks>
    private bool StepTowardsNode(int node)
    {
        if (_graph?.RouteTable is null)
        {
            return true;
        }

        // The patrol walker (0x48E920), WalkToNode/RunToNode (0x48EBF0/0x48EC50) and MoveToEvent (0x48E020)
        // all plan with the claim flag set.
        if (DestinationNode != node && !GoTo(node, claim: true))
        {
            return false;
        }

        Activity = AiSoldierActivity.Patrolling;
        return Advance();
    }

    /// <summary>A patrol's LookAtNode — <c>0x452580</c>, the look-at entry into the turn.</summary>
    /// <returns><see langword="true"/> once facing the node within 3 degrees.</returns>
    /// <remarks>
    /// The look-at entry sets the turn clamp to 0.0259 rad of input per tick (<c>0x3CD4629B</c>), so a
    /// patrol turns to look at a node at about 24 degrees a second rather than snapping to it; the walk and
    /// the combat turns set it back to pi.
    /// </remarks>
    private bool FaceNode(int node)
    {
        if (_graph?.NodeAt(node) is not { } target)
        {
            return true;
        }

        _steering.YawClamp = LookAtTurnClamp;
        _steering.PitchClamp = LookAtTurnClamp;
        return SteerTowards(target.Position, AiPatrolRoute.LookAtToleranceRadians);
    }

    // 0x452580's clamp on both angles, in radians -- 0x3CD4629B.
    private const float LookAtTurnClamp = 0.025925925f;

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
            // TakingDamage is deliberately not a fallback combat-install trigger: the patrol combat arm
            // consumes its drained count for evasion, and re-installing combat for every hit interrupts
            // persistent prone behavior.
            if (TargetSlot.IsVisible
                || Events.PendingCount(AiEvent.Combat) > 0
                || Events.PendingCount(AiEvent.Gunshot) > 0
                || Events.PendingCount(AiEvent.GunshotMiss) > 0
                || Events.PendingCount(AiEvent.Alert) > 0)
            {
                if (!_combatInstalled)
                {
                    ApplyActions([new AiProducedAction(
                        ActionCombat, [(int)AiActionFlagWord.Pushable], PriorityPositionOf(AiEvent.Combat))]);
                }
                else
                {
                    EnterCombat();
                }
            }

            Drain();
            return;
        }

        Events.Pump(_pendingActions.PriorityCap, Dispatch);
    }

    /// <summary>Runs the script for one event type at a priority position, and installs what it produced.</summary>
    private bool Dispatch(AiEvent type, int slot)
    {
        // What sub_44D850 reads mid-dispatch: the current event's own position, off the queue head.
        AiEventRecord? current = Events.HeadOf(type);
        _currentEventPosition = current?.Position;
        _currentEventSource = current?.Source ?? 0;
        _currentEventSubject = current?.Subject ?? 0;

        // AIFunction_GetEventDistance (0x44E930) measures the CURRENT queue record from the AI at
        // dispatch time. Keeping the value written when an event was posted let a later visibility
        // sweep or alert overwrite a queued gunshot's distance before guard.qvm saw it.
        if (_currentEventPosition is { } eventPosition)
        {
            LastEventDistance = Vector3d.Distance(Position, eventPosition);
        }

        Dispatched?.Invoke(type, slot);
        bool handled = _host!.Run(_script!, this, type, slot, _archetypeScript);
        LastDispatchedEvent = type;
        if (handled)
        {
            ApplyActions(_host.Produced);
        }

        _currentEventPosition = null;
        _currentEventSource = 0;
        _currentEventSubject = 0;
        return handled;
    }

    private bool IsActionActive(string name) => name switch
    {
        ActionPatrol => Route is not null,
        ActionCombat => _combatInstalled,
        "AIAction_Dead" => Activity == AiSoldierActivity.Dead,
        "AIAction_FallFlat" => _fallFlatDeadline != -1,
        ActionActivate => _activate is not null,
        "AIAction_WalkToNode" or "AIAction_RunToNode" or "AIAction_MoveToEvent" => _goToNode >= 0,
        "AIAction_FireAtNode" or "AIAction_FireAtTask" => _scriptedFire is not null,
        "AIAction_PlayAnimation" => _scriptedAnimation,
        "AIAction_LookAtEvent" => _lookAtPoint is not null,
        "AIAction_Stunned" => _stunnedDurationTicks >= 0,
        "AIAction_KickGrenade" => _hasKickableGrenade,
        "AIAction_RunPanicking" => _panicDestination >= 0,
        "AIAction_Idle" => true,

        // SetCombat posts COMBAT and ends in its start (0x48E560); PlaySound in its first run (0x48DF80).
        _ => false,
    };

    /// <summary>Takes an action off the stack and runs its end function — <c>0x450FF0</c>.</summary>
    private void FinishAction(AiPendingAction action)
    {
        _pendingActions.Remove(action);
        EndAction(action.Name);
    }

    /// <summary>An action's end function — the <c>+32</c> <c>0x450FF0</c> runs, however the action ended.</summary>
    private void EndAction(string name)
    {
        switch (name)
        {
            case ActionPatrol:
                Route = null;
                RequestedAnimation = -1;
                break;

            case ActionCombat:
                EndCombat();
                break;

            case ActionActivate:
                // 0x48E870 releases the two claim handles.
                ReleaseActivateClaims();
                break;

            case "AIAction_FallFlat":
                _fallFlatDeadline = -1;
                break;

            case "AIAction_WalkToNode":
            case "AIAction_RunToNode":
            case "AIAction_MoveToEvent":
                _goToNode = -1;
                _scriptedRunGait = false;
                break;

            case "AIAction_FireAtNode":
            case "AIAction_FireAtTask":
                EndScriptedFire();
                break;

            case "AIAction_PlayAnimation":
                if (_scriptedAnimation)
                {
                    RequestedAnimation = -1;
                    _scriptedAnimation = false;
                }

                break;

            case "AIAction_LookAtEvent":
                _lookAtPoint = null;
                break;

            case "AIAction_Stunned":
                if (_stunnedDurationTicks >= 0)
                {
                    RequestedAnimation = -1;
                }

                _stunnedDurationTicks = -1;
                _stunnedDeadline = -1;
                break;

            case "AIAction_RunPanicking":
                _panicDestination = -1;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The combat action's end function, <c>0x48D7D0</c>: release the combat record and the aim slot in
    /// it, and leave a mounted gun.
    /// </summary>
    /// <remarks>
    /// It runs however the fight ends — the arm giving up, the target dying, the action being ended to
    /// make way for another — and nothing else: the body is left as it is, for whatever runs next.
    /// </remarks>
    private void EndCombat()
    {
        _combatInstalled = false;
        _combat?.Cancel();
        _gunnerCombat?.End();
        _movingInCombat = false;
        _patrolOneShotChild = null;
        _patrolRollChild = null;
        _patrolProne = false;
        _patrolKneeling = false;
        _repositionLatched = false;
        _lookForTargetLatched = false;

        // The node claim outlives the fight: nothing in IGI.exe releases HumanAI+16032 when combat ends --
        // only death (0x44F410), a claiming walk (0x4518E0) or a search that claims elsewhere replaces it.
        SetTarget(null);
        if (Activity == AiSoldierActivity.Engaging)
        {
            Activity = AiSoldierActivity.Idle;
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
            && Vector3d.Distance(Position, sourcePosition) < AiScriptApi.GunshotRange)
        {
            return;
        }

        _fallFlatDurationTicks = Math.Max(0, (int)(seconds * 30.0));
        _fallFlatDeadline = -2;
        Activity = AiSoldierActivity.FallenFlat;
    }

    private Vector3d? ResolveCurrentEventSourcePosition()
    {
        // Player-authored sensed events use -1 because the player is not one of the renderer-local soldier
        // ids. By the time an event is dispatched Tick has supplied the player's current feet position.
        if (_tracked.TryGetValue(_currentEventSource, out TrackedHuman? human))
        {
            return human.Candidate.Feet;
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
    }

    private void BeginStunned(double seconds)
    {
        _stunnedDurationTicks = Math.Max(0, (int)(seconds * 30.0));
        _stunnedDeadline = -1;
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
        _stunnedDurationTicks = -1;
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
        Body.Reset();
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

        _panicDestination = destination.Value;
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

        GoTo(node);
        _activate = new ActivateTask(taskId, run, node);
        _activateTaskId = taskId;
        Activity = AiSoldierActivity.Patrolling;
    }

    /// <summary>The start shared by WalkToNode, RunToNode and MoveToEvent: name the node to walk to.</summary>
    /// <param name="node">The destination, or a negative id when there is none, which leaves nothing to run.</param>
    private void BeginGoTo(int node)
    {
        _goToNode = node >= 0 && _graph?.NodeAt(node) is not null ? node : -1;
        if (_goToNode >= 0)
        {
            Activity = AiSoldierActivity.Patrolling;
        }
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

    /// <summary>Diagnostics: the eye matrix a sweep run now would test against -- the bound eye, or the body's.</summary>
    public Matrix3f CurrentEyeOrientation => EyeOrientation();

    /// <summary>The eye height fallback for a soldier with no attached body rig.</summary>
    public const double HumanSeparationEyeHeight = 1.8 * 4096.0;

    /// <summary>How far above the soldier's feet the ground probe starts — 0.5 m.</summary>
    private const double GroundProbeAboveUnits = 2048.0;

    /// <summary>How far below the soldier's feet the ground probe reaches — 2 m (the sliding step-down budget).</summary>
    private const double GroundProbeBelowUnits = 8192.0;

    private void Drain()
    {
        // One event type dispatches per tick and every queue is then emptied, so an event nothing acted on is
        // discarded rather than deferred.
        Events.Pump(AiEventQueues.UnboundedPriorityCap, (_, _) => true);
    }

    /// <summary>
    /// Walks the movement queue for one tick — the planner's stepping loop around <c>0x451540</c>.
    /// </summary>
    /// <returns><see langword="true"/> once the queue is empty: the walk is over.</returns>
    /// <remarks>
    /// <para>
    /// Reaching a waypoint (within 0.3 m on both of the body's horizontal axes, height ignored) pops it,
    /// makes its node the current one and steps the next waypoint in the same tick
    /// (<c>while (sub_451540(ai) == 0)</c>), so a walker never pauses at an intermediate point. Only an
    /// empty queue ends the walk, and that also clears the goal.
    /// </para>
    /// <para>
    /// The walker only writes the frame: the turn, a level view and forward. The body turns and moves after
    /// it, on its own clip — see <see cref="StepBody"/>.
    /// </para>
    /// </remarks>
    private bool Advance()
    {
        while (_route.Count > 0)
        {
            AiWaypoint waypoint = _route[0];
            if (waypoint.Door && HoldForDoor(waypoint.Point))
            {
                return false;
            }

            AiMovementOutcome outcome = _steering.Step(
                Position, Orientation, waypoint.Point, 0f, 0f, out AiSteeringFrame frame);
            LastSteeringForward = frame.Forward;
            LastSteeringOutcome = outcome;
            if (outcome == AiMovementOutcome.WaypointReached)
            {
                // Reaching the waypoint also drops the door handle (ai+15946/+15948), so the next door hop
                // searches afresh.
                CurrentNode = waypoint.Node;
                _route.RemoveAt(0);
                _doorAtHop = null;
                _doorSearched = false;
                continue;
            }

            TurnTowards(waypoint.Point, 0f, level: true);
            WalksForward = true;
            return false;
        }

        DestinationNode = -1;
        return true;
    }

    /// <summary>A door hop's hold — the door branch of <c>0x451540</c>.</summary>
    /// <returns><see langword="true"/> while the soldier waits on the door rather than walking.</returns>
    /// <remarks>
    /// The walker finds the nearest door once per hop and acts on its state: 1 (closed) and 3 (closing) —
    /// open it with the player's own use (<c>sub_447DA0</c>) and hold; 2 (moving) — "Waiting for door to
    /// open", hold; 0 (open) — walk through. Holding turns to the waypoint at the 2-degree door tolerance.
    /// </remarks>
    private bool HoldForDoor(Vector3d waypoint)
    {
        if (_doors is null)
        {
            return false;
        }

        if (!_doorSearched)
        {
            _doorAtHop = _graph?.NodeAt(CurrentNode) is { } from ? _doors(from.Position) : null;
            _doorSearched = true;
        }

        if (_doorAtHop is not { } door)
        {
            return false;
        }

        int state = door.UseState;
        if (state is 1 or 3)
        {
            door.CommandOpen();
        }
        else if (state != 2)
        {
            return false;
        }

        TurnTowards(waypoint, AiMovementStep.DoorFacingTolerance, level: true);
        return true;
    }

    /// <summary>
    /// The part of the body's update the AI's frame drives — <c>0x45D080</c>: turn (<c>0x4610C0</c>), then
    /// move on the clip's own root motion (<c>0x4614B0</c>) at the graph's height (<c>0x462390</c>).
    /// </summary>
    /// <remarks>
    /// <para>The turn is the one the player's own mouse goes through: see <see cref="HumanBodyTurn"/>.</para>
    /// <para>
    /// An AI's height is not found by a ray at all: <c>0x462390</c> interpolates it along the leg between the
    /// last two waypoints, which by construction lie on the ground the graph was authored against.
    /// </para>
    /// </remarks>
    private void StepBodyOrWait()
    {
        if (StepsBodyAfterClip)
        {
            _bodyStepPending = true;
            return;
        }

        StepBody();
    }

    /// <summary>Turns and moves the body by the delta of the clip that is playing after this tick's machine.</summary>
    /// <param name="rootMotion">The clip's delta along the body's forward axis, in units.</param>
    /// <param name="rootMotionLateral">Its delta along the body's right axis, in units.</param>
    public void StepBodyAfterClip(double rootMotion, double rootMotionLateral)
    {
        if (!_bodyStepPending)
        {
            return;
        }

        _bodyStepPending = false;
        _rootMotionThisTick = rootMotion;
        _rootLateralThisTick = rootMotionLateral;
        StepBody();
    }

    private void StepBody()
    {
        float turn = _turnInput;
        float pitch = _pitchInput;
        _turnInput = 0f;
        _pitchInput = 0f;
        LastAdvanceRm = _rootMotionThisTick;
        if (IsMounted)
        {
            // 0x4610C0's attached branch takes the emplacement's frame instead, and the body rides the gun.
            _bodyTurn.Reset();
            _viewPitchTurn.Reset();
            return;
        }

        Facing = Wrap((float)(Facing + _bodyTurn.Step(turn)));
        ViewPitch = Math.Clamp((float)(ViewPitch + _viewPitchTurn.Step(pitch)), -MathF.PI / 2f, MathF.PI / 2f);

        // The clip's delta, rotated by the turned body into the world, every tick and whatever the AI asked
        // for: the walk moves a walker, a roll carries a rolling soldier sideways, a step back steps back.
        Vector3d world = SoldierDeathFall.RotateAboutZ(
            new Vector3d(_rootLateralThisTick, _rootMotionThisTick, 0.0), Facing + RecoilYaw);
        double x = Position.X + world.X;
        double y = Position.Y + world.Y;
        if (world.X != 0.0 || world.Y != 0.0)
        {
            LastMovedTo = new Vector3d(x, y, Position.Z);
            LastFacingDeg = Facing * 180.0 / Math.PI;
        }

        // 0x462390 grounds an AI on its graph every tick: along the leg it is walking, or, with nothing to
        // walk, at its current node's height.
        Position = new Vector3d(x, y, GroundHeight(x, y));
        StoodOnGraph = _graph is not null;
    }

    /// <summary>An AI's feet height — <c>0x462390</c>'s graph branch.</summary>
    /// <param name="x">The new X.</param>
    /// <param name="y">The new Y.</param>
    /// <returns>The height along the leg to the head waypoint, or the current node's with nothing to walk.</returns>
    private double GroundHeight(double x, double y)
    {
        if (_route.Count == 0)
        {
            return _graph?.NodeAt(CurrentNode)?.Position.Z ?? Position.Z;
        }

        Vector3d head = _route[0].Point;
        if (!_groundLegSet)
        {
            _groundLegFrom = Position;
            _groundLegTo = Position;
            _groundLegSet = true;
        }

        if (Vector3d.Distance(head, _groundLegTo) > 100.0)
        {
            _groundLegFrom = _groundLegTo;
            _groundLegTo = head;
        }

        double length = Vector3d.Distance(_groundLegTo, _groundLegFrom);
        double remaining = length > 0.0
            ? Math.Clamp(Vector3d.Distance(_groundLegTo, new Vector3d(x, y, Position.Z)) / length, 0.0, 1.0)
            : 0.0;
        return ((1.0 - remaining) * (_groundLegTo.Z - _groundLegFrom.Z)) + _groundLegFrom.Z;
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

    /// <summary>Takes the combat action, re-arming the behaviour every time.</summary>
    /// <remarks>
    /// <b>Every</b> path into combat comes through here, and that matters: once the machine ends — a
    /// targeting timeout, or a target with no detection slot — its phase is
    /// <see cref="AiCombatPhase.Ended"/> and every later tick returns "ended" at once. Re-arming only on
    /// the scripted path left a soldier that engaged a second way permanently unable to fight, which
    /// reads exactly as a guard that sees you and does nothing. <c>0x48D000</c> arms it when the action
    /// is taken, which is what this is.
    /// </remarks>
    private void EnterCombat()
    {
        // 0x44D160's civilian case installs no combat callback. It must not inherit the guard arm.
        if (Arm == AiCombatArm.None)
        {
            Activity = AiSoldierActivity.Idle;
            SetTarget(null);
            return;
        }

        _patrolOneShotChild = null;

        // The record's human is the subject of the event being handled -- whoever SetCombat's triggering
        // event named. An event that names nobody the scan knows keeps the soldier's attention.
        if (_tracked.ContainsKey(_currentEventSubject))
        {
            _targetId = _currentEventSubject;
        }

        SetTarget(TargetRecord);

        if (!_combatInstalled || CombatPhase == AiCombatPhase.Ended)
        {
            // The installers leave the body as they find it and pick the sub-arm from it: crouched -> the
            // kneel arm (0x48ABA0 -> 0x48C250, the RPG's 0x48CB40 -> its kneel tick), prone -> the prone
            // arm (0x48C510). A patrol crouching on its route when combat installs fights kneeling.
            bool crouched = IsCrouching || AiCombatChannels.IsInStance(_type, AiStanceChannel.Kneeling, CombatState);
            bool prone = AiCombatChannels.IsInStance(_type, AiStanceChannel.Prone, CombatState);
            _combat?.Begin(crouched);
            _patrol?.Begin();
            _gunnerCombat?.Begin();
            _detectionShoutDone = false;
            _alertPingCounter = 0;
            _patrolProne = prone && Arm == AiCombatArm.Patrol;
            _repositionLatched = false;
            _lookForTargetLatched = false;

            // The run function is the stance's entry, so no movement behaviour of an earlier fight is installed.
            PatrolAction = default;
            _patrolKneeling = crouched && !_patrolProne && Arm == AiCombatArm.Patrol;

            // SetCombat's start (0x44D160) is 0x48ABA0 for the patrol types, which installs a stance ENTRY as the
            // run function -- 0x48BBD0, 0x48C250 or 0x48C510 -- and that entry's whole tick is to install the stance's
            // per-tick (CombatStand 0x48BC00, Kneel 0x48C280, LieDown 0x48C540) for the next. The guards' start
            // (0x48D000) installs GuardCombatStand itself, which runs at once. Probed on mission 4: patrol 411 took
            // combat on tick 4384 and drew its first evasion roll (0x48BE30, from CombatStand) on 4385.
            _combatEntryTick = (int)_type is >= 7 and <= 10 or >= 15 and <= 19 or >= 23 and <= 25;
        }

        _combatInstalled = true;
        Activity = AiSoldierActivity.Engaging;
        RequestedAnimation = -1;

        // The movement queue and its destination (+15920) outlive the action that planned them: SetCombat
        // (0x44D160) touches neither, so a patrol caught mid-leg that repositions to the node it claimed for the
        // leg (0x4518E0 sees the same destination) carries on along the queue it already had.
    }

    /// <summary>Runs one tick of the combat behaviour and applies what it asks for.</summary>
    /// <remarks>
    /// <para>
    /// The soldier turns with the same clamped steering everything else uses and reports back whether
    /// the facing reached the two-degree tolerance, which is what the firing tick holds fire on.
    /// </para>
    /// <para>
    /// Nothing here fires a round. The command carries the trigger and the host turns it into a shot
    /// through the body's own weapon path, which is what the original does.
    /// </para>
    /// </remarks>
    private void Fight(int tick, Vector3d lastSeenFeet)
    {
        // Recomputed every combat tick: AdvanceInCombat sets it when it actually steps.
        _movingInCombat = false;
        _rawInputWrites.Clear();

        // sub_48A920: the bark needs a visible target whose class is HumanPlayer (word_5385B0), tested before
        // the per-action latch -- so a soldier fighting an NPC neither shouts nor spends its one bark, and
        // still challenges the player when it turns on him. The latch is set even when another AI holds
        // the shared 90-tick sound gate, so this engagement never retries.
        if (TargetSlot.IsVisible && !TargetIsSoldier && !_detectionShoutDone)
        {
            DetectionShoutRequestedThisTick = true;
            _detectionShoutDone = true;
        }

        // sub_48A920 pings the squad on the engagement's first tick with a valid target and every 20th
        // after, counting in the combat action's own block -- not on a mission-wide tick -- and only
        // while the detection scan still registers the target.
        if (TargetSlot.IsRegistered)
        {
            AlertPingDueThisTick = _alertPingCounter == 0;
            _alertPingCounter = (_alertPingCounter + 1) % AlertPingPeriodTicks;
        }

        // The shared record's position is refreshed only WHILE THE TARGET IS SEEN (sub_48A920 reads it
        // off the detection slot). Hunting and the accuracy distance read the record (+272), so a guard
        // searches where the squad last SAW the player; one that never saw it borrows the squad's record.
        // Turning to an unseen target and aiming a burst do not: see TrueAimPoint.
        if (TargetSlot.IsVisible)
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

        bool mounted = false;
        if (_gunnerCombat is not null)
        {
            AiStationaryGun? gun = GunnerId == -1 ? null : _stationaryGun?.Invoke(GunnerId);
            AiGunnerCommand gunner = _gunnerCombat.Update(
                gun,
                enemy,
                TargetSlot.IsRegistered);

            if (gunner.Ended)
            {
                Combat = new AiCombatCommand { Ended = true };
                _channelWrites.Clear();
                if (gunner.MountChannelValue is { } cleanupValue)
                {
                    WriteChannel(AiCombatChannel.PlayAnimationAlternate, cleanupValue);
                }

                EndCombat();
                return;
            }

            if (gunner.MountChannelValue is { } mountValue)
            {
                Combat = new AiCombatCommand();
                _channelWrites.Clear();
                WriteChannel(AiCombatChannel.PlayAnimationAlternate, mountValue);
                if (gunner.MountedThisTick && gun is not null)
                {
                    _seatedOnGunThisTick = true;
                    SeatOn(gun);
                }
                return;
            }

            mounted = gunner.RunMountedWeapon;
        }

        if (_patrol is not null)
        {
            FightAsPatrol(tick, enemy);
            return;
        }

        // A knife gets no branch of its own. 0x48D080's close-combat test reads HumanAI+15836 == 5, a word
        // only the constructor ever writes, so it is dead; a guard holding a knife reaches the fire child,
        // sets up no burst (sub_48B9C0 has none for it) and stands.
        AiCombatWeaponState weapon = _weapon();

        AiCombatSense sense = new(
            HasDetectionSlot: TargetSlot.IsRegistered,
            InCone: TargetSlot.IsInCone,
            Visible: TargetSlot.IsVisible,
            TargetPosition: TrueAimPoint(),
            DistanceToTargetInUnits: Vector3d.Distance(Position, enemy),
            LocomotionState: AiCombatChannels.StateOf(_type, AiStanceChannel.Standing),
            AmmoInClip: weapon.AmmoInClip,
            Category: weapon.Category,
            ClipSize: weapon.ClipSize,
            BurstSize: weapon.BurstSize,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0);

        // 0x48D080 decides nothing unless the body is in its type's stand state (sub_48EFE0(C, 0)): after a
        // burst the body racks down through ready and recover to standing before the next one.
        //
        // The accuracy lerp scales over the PLAIN primary view length: 0x48B9C0 reads [HumanAI+176],
        // the third double of the primary block {alpha, gamma, length, alarmLength} -- not the
        // alarm-extended range. Passing the alerted range stretched the ramp and made every alerted
        // guard more accurate at range than retail's.
        AiCombatCommand command;
        if (mounted)
        {
            command = _combat.UpdateMounted(sense, enemy, Vision.Primary.Length);
        }
        else if (Arm == AiCombatArm.Rpg)
        {
            // 0x48CBD0 gates on the stand state, 0x48CDF0 (and the reload it installs) on the kneel one.
            AiStanceChannel stance = _combat.RpgKneeling ? AiStanceChannel.Kneeling : AiStanceChannel.Standing;
            command = _combat.UpdateRpg(
                sense, AiCombatChannels.IsInStance(_type, stance, CombatState), Vision.Primary.Length);
        }
        else
        {
            bool standing = AiCombatChannels.IsInStance(_type, AiStanceChannel.Standing, CombatState);
            command = _combat.Update(sense, standing, Vision.Primary.Length);
        }

        Combat = command;

        // The same writes 0x48EF70 makes, recorded rather than applied -- see ChannelWrites. The RPG's
        // kneel tick and every child it installs hold channel 1 on every tick.
        _channelWrites.Clear();
        if (Arm == AiCombatArm.Rpg && _combat.RpgKneeling)
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
            if (MountedGun is { } emplacement)
            {
                // State 36 stays attached to the gun. The StationaryGun task, not the Human root, consumes the turn
                // inputs and clamps the barrel to its authored cone; the body rides its frame.
                SeatOn(emplacement);
                _combat.ReportAimed(emplacement.Contains(point));
            }
            else
            {
                _combat.ReportAimed(TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians));
            }
        }

        if (command.Ended)
        {
            EndCombat();
        }
    }

    /// <summary>Runs the move-and-fight arm for one tick — <c>0x48ABA0</c>.</summary>
    /// <param name="tick">The engine tick used by the ten-tick evasion cooldown.</param>
    /// <param name="enemy">Where the target is.</param>
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
    private void FightAsPatrol(int tick, Vector3d enemy)
    {
        AiCombatWeaponState weapon = _weapon!();
        TrackTargetNode(LiveTargetNodePoint() ?? LiveTargetFeet(enemy));

        // 0x48BC00 asks 0x48BE30 before it runs an already-installed child and before its ordinary
        // chooser. The drained event counters are the exact three words the original tests. Follow,
        // RePosition and LookForTarget replace that per-tick while they run, so a moving soldier never
        // asks, and the named characters' per-tick (0x48ACC0) never calls it at all.
        if (!_patrolProne && Arm == AiCombatArm.Patrol && !IsPatrolMovementInstalled)
        {
            bool underFire = Events.DrainedCount(AiEvent.TakingDamage) > 0
                || Events.DrainedCount(AiEvent.Gunshot) > 0
                || Events.DrainedCount(AiEvent.GunshotMiss) > 0;
            bool onStair = _graph?.NodeAt(CurrentNode) is { } current
                && (current.Criteria & GraphNodeCriteria.Stair) != 0;
            AiEvasiveAction evasion = _patrol!.ChooseEvasion(
                tick,
                Vector3d.Distance(Position, enemy),
                AvailableRollSpace(),
                underFire,
                onStair);

            if (evasion == AiEvasiveAction.LieDown)
            {
                // sub_48AE70 installs the prone arm with no child, which cancels whatever was running.
                _patrolProne = true;
                _patrolRollChild = null;
                _patrolOneShotChild = null;
                _combat!.CancelChild();
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

        // 0x48C540 holds logical channel 2 throughout the prone behavior and chooses only once the body
        // is in the type's prone state (sub_48EFE0(C, 2)); releasing the channel selects the get-up.
        if (_patrolProne)
        {
            _channelWrites.Clear();
            WriteChannel(AiCombatChannel.LieDown, 1f);
            if (!AiCombatChannels.IsInStance(_type, AiStanceChannel.Prone, CombatState))
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
            HoldPosture();
            RunSharedSubAction(enemy, weapon);
            return;
        }

        // 0x48C860 replaces the arm's per-tick until the soldier arrives: the chooser is not asked, so a
        // chaser runs its whole leg instead of stopping to shoot the moment the target is in range. It
        // re-searches only when the target has moved onto another node.
        if (IsPatrolMovementInstalled && PatrolAction == AiPatrolAction.Follow)
        {
            _channelWrites.Clear();
            Combat = default;
            _tenseRunThisTick = true;

            // 0x48C860 runs: the run setter, every tick of the leg.
            SetGait(walking: false);

            // 0x48C860: the flag 0x48A920 raised buys one search, toward the target only while it is seen
            // (sub_48B050's third argument is the target record's visible byte, +41), and the walk is redirected
            // only to a node other than the one it stands on. Searching only while the target was visible, and on
            // a node change noticed only then, sent mission 4's guard 411 back the way it came the moment it saw
            // the squad again, where retail's ran its leg out.
            if (_followResearch)
            {
                _followResearch = false;
                if (FindFreePosition(LiveTargetFeet(enemy), towardTarget: TargetSlot.IsVisible) is int nearer
                    && nearer != CurrentNode)
                {
                    _patrol!.RedirectTo(nearer);
                }
            }

            FollowTarget(enemy);
            return;
        }

        // 0x48C760 likewise replaces the per-tick until the walk to the claimed cell arrives (0x4522A0 returns
        // true and 0x48AE70 reinstalls the parent). The node the soldier stands on changes along the way
        // without ending it, so a patrol caught mid-leg runs the whole leg -- re-asking the chooser each tick
        // stopped it at the first node it crossed.
        // 0x48CAB0, the tick after the stand arm chose it: one turn toward where the target is (0x451380's point,
        // to within 2 degrees), and the parent per-tick is reinstalled for the next tick. It walks nowhere.
        if (_lookForTargetLatched)
        {
            _lookForTargetLatched = false;
            _channelWrites.Clear();
            Combat = default;
            _tenseRunThisTick = true;
            TurnTowards(LiveTargetNodePoint() ?? AimPointOf(enemy, TargetCrouching), 0.034906585f);
            return;
        }

        if (_repositionLatched && PatrolAction == AiPatrolAction.RePosition)
        {
            _channelWrites.Clear();
            Combat = default;
            _tenseRunThisTick = true;
            // The walk was planned when RePosition was installed; the latch only follows it. Re-planning
            // here would hand an arrived soldier a fresh one-point route every tick, and it would never end.
            if (_route.Count > 0)
            {
                AdvanceInCombat();
            }

            // Arrived (or nowhere left to walk): 0x4522A0 reports it on the tick the queue empties, and 0x48AE70
            // reinstalls the parent per-tick for the next one.
            if (_route.Count == 0)
            {
                _repositionLatched = false;
            }

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

            // ...and an animation the patrol was playing is stopped: channel 3 at -1 (0x48BC00).
            if (AiCombatChannels.IsInStance(_type, AiStanceChannel.AnimationPlaying, CombatState))
            {
                WriteChannel(AiCombatChannel.PlayAnimation, -1f);
            }

            return;
        }

        // "Waiting for kneel state" -- 0x48C280 holds channel 1 and chooses only in the kneel state.
        if (_patrolKneeling && !AiCombatChannels.IsInStance(_type, AiStanceChannel.Kneeling, CombatState))
        {
            _channelWrites.Clear();
            HoldPosture();
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

        double distance = Vector3d.Distance(Position, enemy);
        bool visible = TargetSlot.IsVisible;

        // Only the branch that needs a search runs one, as the original's call sites do: a search CLAIMS
        // the node it accepts, so running both every tick moved the soldier's claim about on ticks the
        // chooser never used it -- and, rejecting its own claim next time, flipped between two nodes.
        // sub_48B050 places the target with 0x451380 -- the human itself, live -- not the shared record the
        // aim and the distance read, so a search out of sight still knows where the target went.
        Vector3d live = LiveTargetFeet(enemy);
        Func<int?> toward = () => FindFreePosition(live, towardTarget: true);
        Func<int?> away = () => FindFreePosition(live, towardTarget: false);

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
            HasDetectionSlot: TargetSlot.IsRegistered,
            Visible: visible,
            DistanceToTargetInUnits: distance,
            CurrentNodeId: CurrentNode,
            StandingOnOwnNode: standingOnOwnNode,
            AmmoInClip: weapon.AmmoInClip,
            HasGrenade: HasGrenade,
            AimersOnTarget: Target?.Count(AiAimMode.Aiming) ?? 0,
            FreePositionTowardTarget: toward,
            FreePositionAwayFromTarget: away);

        _chooserCalls++;
        AiPatrolAction action = _patrol!.Choose(sense);
        PatrolAction = action;
        _channelWrites.Clear();

        // The kneel and prone arms answer a move with the stand arm (0x48BBD0) and the move's own per-tick
        // on the same tick, writing the posture channel 0: the soldier sets off as it rises, with no
        // wait for the stand state and no second decision.
        //
        // The STAND arm only installs it: 0x48BC00 hands Follow and RePosition to 0x48AE70 with their entries
        // (0x48C850, 0x48C750), which set the run function to the movement's per-tick and return -- so the walk
        // starts on the next tick, from the latched branches above. Probed on mission 4: patrol 411 chose Follow on
        // tick 4385, wrote its first forward on 4386 (the body entering run state 17 then) and moved from 4387.
        bool standArmInstallsMove = !_patrolProne && !_patrolKneeling
            && action is AiPatrolAction.RePosition or AiPatrolAction.Follow or AiPatrolAction.LookForTarget;
        if ((_patrolProne || _patrolKneeling)
            && action is AiPatrolAction.RePosition or AiPatrolAction.Follow or AiPatrolAction.LookForTarget)
        {
            _patrolProne = false;
            _patrolKneeling = false;
        }

        HoldPosture();

        Combat = default;

        switch (action)
        {
            case AiPatrolAction.End:
                // Every arm ends the action outright (case 8 -> sub_450FF0); the body rises by itself
                // once nothing holds its posture channel.
                EndCombat();
                return;

            case AiPatrolAction.CloseCombat:
                if (_patrolProne || _patrolKneeling)
                {
                    // Close enough to strike from the ground: the arm only becomes the stand arm, and the
                    // strike is chosen again once the body is up.
                    _patrolProne = false;
                    _patrolKneeling = false;
                    _channelWrites.Clear();
                    return;
                }

                // 0x48DBB0 only raises channel 9; it does not turn the body.
                _patrolOneShotChild = action;
                WriteChannel(AiCombatChannel.CloseCombat, 1f);

                // The strike resolves with the trigger: the damage model (probability-gated factor)
                // is attested retail data, while the exact native strike tick is not — see
                // AiPatrolCombat.RollMeleeStrike. Range was already established by the chooser.
                float strike = _patrol.RollMeleeStrike();
                _pendingMeleeStrike = SoldierClass.LandsCloseCombatBlow ? strike : 0f;
                return;

            case AiPatrolAction.ThrowGrenade:
                if (_patrolProne || _patrolKneeling)
                {
                    // The prone and kneel arms only log "liedown/kneel throw grenade"; neither installs
                    // a child (0x48C540, 0x48C280).
                    return;
                }

                // 0x48DBF0 only raises channel 8; it does not turn the body.
                _patrolOneShotChild = action;
                WriteChannel(AiCombatChannel.ThrowGrenade, 1f);
                return;

            case AiPatrolAction.LookForTarget:
                Target?.Aim(this, AiAimMode.Crowded);

                // 0x48AE70 with the entry 0x48CAA0: the stand arm installs 0x48CAB0, which runs on the next tick;
                // the kneel and prone arms answer with the stand arm and the turn on this one.
                if (standArmInstallsMove)
                {
                    _lookForTargetLatched = true;
                }
                else
                {
                    TurnTowards(LiveTargetNodePoint() ?? AimPointOf(enemy, TargetCrouching), 0.034906585f);
                }

                return;

            case AiPatrolAction.RePosition:
                // 0x48C760 walks to the CLAIMED cell (HumanAI+16048) on the claimed node (+16032), not to the
                // node the soldier last stood on -- for a patrol caught mid-leg that is where its leg was
                // going. Without a claim it only logs "HumanAI #%d has no nodeoccupationitem" and stands.
                if (_occupancy is null)
                {
                    HeadFor(CurrentNode);
                    if (!standArmInstallsMove)
                    {
                        AdvanceInCombat();
                    }

                    _repositionLatched = _route.Count > 0;
                }
                else if (_occupancy.ClaimedNode(this) is int claimed)
                {
                    HeadFor(claimed);
                    if (!standArmInstallsMove)
                    {
                        AdvanceInCombat();
                    }

                    _repositionLatched = _route.Count > 0;
                }

                return;

            case AiPatrolAction.Follow:
                // The out-of-sight search that chose this walk spends the re-search flag (0x48C212).
                if (!visible)
                {
                    _followResearch = false;
                }

                if (!standArmInstallsMove)
                {
                    FollowTarget(enemy);
                }

                return;

            case AiPatrolAction.Fire:
            case AiPatrolAction.Reload:
            default:
                RunSharedSubAction(enemy, weapon);
                return;
        }
    }

    /// <summary>Whether a movement behaviour — Follow, RePosition or a walking LookForTarget — owns the tick.</summary>
    private bool IsPatrolMovementInstalled =>
        (PatrolAction == AiPatrolAction.Follow && _patrol!.DestinationNodeId >= 0)
        || (PatrolAction == AiPatrolAction.RePosition && _repositionLatched)
        || _lookForTargetLatched;

    /// <summary>Holds the posture channel the kneel and prone arms, and their children, write every tick.</summary>
    private void HoldPosture()
    {
        if (_patrolProne)
        {
            WriteChannel(AiCombatChannel.LieDown, 1f);
        }
        else if (_patrolKneeling)
        {
            WriteChannel(AiCombatChannel.Kneel, 1f);
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
    /// <remarks>
    /// <b>A patrol fires while it is running at you</b>, with no burst set up and no aim solve — one
    /// round per tick straight into the trigger slot, whenever the target is inside a <b>16.3°</b> cone
    /// about its forward axis (<c>dot &gt; 0.96</c>).
    /// </remarks>
    private void FollowTarget(Vector3d enemy)
    {
        int destination = _patrol!.DestinationNodeId;
        if (destination < 0)
        {
            return;
        }

        WriteChannel(AiCombatChannel.Stand, 1f);

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
            if (TargetSlot.IsVisible)
            {
                TurnTowards(AimPointOf(enemy, TargetCrouching), (float)AiGuardCombat.AimToleranceRadians);
            }
            return;
        }

        AdvanceInCombat();

        // 0x4522A0 to the claimed cell: the leg ends on the tick its queue empties -- standing on the destination
        // node is not enough while the cell inside it is still ahead -- and 0x48AE70 reinstalls the parent.
        if (_route.Count == 0)
        {
            _patrol.ArrivedAtDestination();
            Target?.Aim(this, AiAimMode.None);
            return;
        }

        if (!TargetSlot.IsVisible)
        {
            Target?.Aim(this, AiAimMode.None);
            return;
        }

        if (IsFacing(AimPointOf(enemy, TargetCrouching), FollowFireConeRadians))
        {
            if ((Target?.Count(AiAimMode.Aiming) ?? 0) >= AiCombatTarget.MaximumAimers
                && Target?.ModeOf(this) != AiAimMode.Aiming)
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

    /// <summary>Where the target really is, raised to where the combat code aims — <c>sub_451380</c>.</summary>
    /// <remarks>
    /// The guard's turn to an unseen target and every burst's aim point read the target task's own position
    /// (0.5 m up, 0.25 m crouched), not the shared record the rest of combat reads; only the distance, and a
    /// knife guard's turn, use the record.
    /// </remarks>
    // 0x451380 reads the target's own origin (Human+32, the pose's root above the feet), not a fixed standing height.
    private Vector3d TrueAimPoint() => TargetHuman?.Candidate.NodeQueryPoint ?? AimPointOf(TargetFeet, TargetCrouching);

    /// <summary>The fire and reload sub-actions the two arms share.</summary>
    /// <param name="enemy">Where the target is.</param>
    /// <param name="weapon">The held weapon's state this tick.</param>
    private void RunSharedSubAction(Vector3d enemy, AiCombatWeaponState weapon)
    {
        AiCombatSense sense = new(
            HasDetectionSlot: TargetSlot.IsRegistered,
            InCone: TargetSlot.IsInCone,
            Visible: TargetSlot.IsVisible,
            TargetPosition: TrueAimPoint(),
            DistanceToTargetInUnits: Vector3d.Distance(Position, enemy),
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
            EndCombat();
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
            _combat.ReportAimed(TurnTowards(point, (float)AiGuardCombat.AimToleranceRadians));
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

        // The combat walk finishes at the claimed sub-slot inside the node (0x4522A0 with ai+16048).
        return GoTo(node, _combatFreePosition);
    }

    /// <summary>How close to the claimed sub-slot counts as standing on it — half a metre.</summary>
    private const double FreePositionArrivalInUnits = 2048.0;

    /// <summary>Steps the soldier along its combat route, the same walk the patrol path uses.</summary>
    /// <remarks>
    /// The move-and-fight arm sets a Follow/RePosition destination but the original then <em>walks</em> to
    /// it — the AI steers its <c>Human</c> through the input frame and the body's mover carries it. Without
    /// this the port set a destination it never travelled, which is exactly "guards engage but don't chase".
    /// The distance covered is the run clip's own root motion (M8), so no speed is invented. Once the route
    /// delivers the body to the node, the walk finishes at the <b>claimed sub-slot</b> inside it — retail's
    /// <c>sub_4522A0</c> against the position at <c>HumanAI+16048</c>.
    /// </remarks>
    private void AdvanceInCombat()
    {
        if (_route.Count > 0)
        {
            // The walk clip plays while the walker writes forward, so the next tick has root motion to move by.
            Advance();
            _movingInCombat = WalksForward;
        }
    }

    /// <summary>
    /// The height the free-position can-shoot ray starts above a candidate node — the <c>AIGraph</c>
    /// task's parameter at <c>+128</c> (default 1.0, ctor <c>0x509570</c>) × 4096, read by
    /// <c>sub_48B6E0</c>.
    /// </summary>
    /// <remarks>
    /// Casting from the node's raw ground position instead grazed the terrain and kerbs at the ray's
    /// very first step, so almost every candidate failed the can-shoot test and a hunting patrol could
    /// never find anywhere to go — the "engaging, never moving" stall. This is only the constructor's
    /// default: the live height is <see cref="FreePositionRayHeightOf"/>, from each graph's own task.
    /// </remarks>
    public const double FreePositionRayHeightInUnits = 4096.0;

    /// <summary>The can-shoot ray's height above a node of this graph — its task's cover mid offset × 4096.</summary>
    /// <param name="graph">The graph, or <see langword="null"/> for the constructor's default.</param>
    /// <returns>The height in units.</returns>
    public static double FreePositionRayHeightOf(GraphFile? graph) =>
        (graph?.CoverMidOffsetMetres ?? 1.0) * GraphNode.MetreInUnits;

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
            $"vis={TargetSlot.IsVisible} cone={TargetSlot.IsInCone} lost={_patrol?.NotVisibleTicks ?? -1} " +
            $"cap={_pendingActions.PriorityCap} node={CurrentNode}->{DestinationNode} route={_route.Count} " +
            $"moving={_movingInCombat} trigger={Combat.PullTrigger} " +
            $"believed=({believed.X:F0},{believed.Y:F0},{believed.Z:F0}) " +
            $"toward={(toward is { } t ? t.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")} " +
            $"away={(away is { } a ? a.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}";
    }

    /// <summary>One line of this tick's decision state, for scenario traces.</summary>
    /// <returns>The stack, the event that ran, what is queued, and what the scan holds on the target.</returns>
    public string DescribeTick()
    {
        string stack = string.Join(">", _pendingActions.Actions.Select(a => a.Name.Replace("AIAction_", "", StringComparison.Ordinal)));
        string pending = string.Join(
            ",",
            Enumerable.Range(0, 24).Select(i => (AiEvent)i).Where(t => Events.PendingCount(t) > 0).Select(t => $"{t}x{Events.PendingCount(t)}"));
        string enemies = string.Join(
            ",",
            _enemies.Select(e => $"{e.Candidate.Id}{(e.Slot.IsInCone ? "c" : "")}{(e.Slot.IsVisible ? "v" : "")}"));
        string patrol = Route is { } walking ? $"path {walking.Path.Id} cmd {walking.Current?.Kind}({walking.Current?.Operand})" : "-";
        return $"act={Activity} run={_runningAction?.Replace("AIAction_", "", StringComparison.Ordinal) ?? "-"} stack=[{stack}] patrol=[{patrol}] alarm={AlarmTriggerId}/{AlarmControlId}/{AlarmAccess}:{(AlarmTriggerId != -1 ? GetAlarmTriggerDistance() / 4096.0 : -1):0.0}m last=[{string.Join(";", LastActions.Select(a => $"{a.Name.Replace("AIAction_", "", StringComparison.Ordinal)}({string.Join(",", a.Arguments)})"))}] " +
            $"evt={LastDispatchedEvent?.ToString() ?? "-"} pend=[{pending}] tgt={_targetId} reg={TargetSlot.IsRegistered} " +
            $"cone={TargetSlot.IsInCone} vis={TargetSlot.IsVisible} alert={Alerted} inst={_combatInstalled} phase={CombatPhase} " +
            $"state={CombatState} trig={Combat.PullTrigger} pa={PatrolAction} route={_route.Count} dest={DestinationNode} node={CurrentNode} onNode={CurrentNode < 0 || IsStandingOnNode(CurrentNode)} latched={_repositionLatched} chooses={_chooserCalls} claim={_occupancy?.ClaimedNode(this)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} " +
            $"believed={(Target is { HasBeenSeen: true } seenRecord ? $"({seenRecord.Position.X / 4096.0:0.0},{seenRecord.Position.Y / 4096.0:0.0}) tnode={NearestNode(seenRecord.Position)}" : "-")} " +
            $"enemies=[{enemies}] facing={Facing * 180.0 / Math.PI:0} eyeYaw={Math.Atan2(-Eye.Orientation.M12, Eye.Orientation.M22) * 180.0 / Math.PI:0} " +
            $"pos=({Position.X / 4096.0:0.0},{Position.Y / 4096.0:0.0},{Position.Z / 4096.0:0.0})";
    }

    /// <summary>How the free-position search judges the target's node and its links, for scenario traces.</summary>
    /// <returns>One entry per candidate: full, can-shoot and on-the-route.</returns>
    public string DescribeFreeSearch()
    {
        Vector3d enemy = Target is { HasBeenSeen: true } shared ? shared.Position : _lastSeenFeet;
        int targetNode = NearestNode(enemy);
        if (_graph is null || targetNode < 0)
        {
            return "-";
        }

        IEnumerable<int> candidates = new[] { targetNode }.Concat(_graph.LinksOf(targetNode).Select(link => link.Other(targetNode)));
        return string.Join(
            " ",
            candidates.Select(id =>
            {
                GraphNode? node = _graph.NodeAt(id);
                bool full = _occupancy?.IsFull(_graph, id) ?? false;
                bool shoot = node is not null && (_sight is null || _sight(
                    new Vector3d(node.Position.X, node.Position.Y, node.Position.Z + FreePositionRayHeightOf(_graph)),
                    AimPointOf(enemy)));
                bool route = AiFreePosition.ShortensTheRoute(_graph, id, CurrentNode, targetNode);
                return $"{id}:{(full ? "F" : "")}{(shoot ? "s" : "")}{(route ? "r" : "")}";
            }));
    }

    /// <summary>The graph nodes within a radius of a point, for scenario traces.</summary>
    /// <param name="point">The centre, in engine units.</param>
    /// <param name="radius">The radius, in engine units.</param>
    /// <returns>One line per node: id, position in metres, distance, radius and links.</returns>
    public IEnumerable<string> DescribeNodesNear(Vector3d point, double radius)
    {
        if (_graph is null)
        {
            yield break;
        }

        foreach (GraphNode node in _graph.Nodes)
        {
            double distance = Vector3d.Distance(node.Position, point);
            if (distance <= radius)
            {
                string links = string.Join(",", _graph.LinksOf(node.Id).Select(link => link.Other(node.Id)));
                yield return $"node {node.Id} ({node.Position.X / 4096.0:0.0},{node.Position.Y / 4096.0:0.0},{node.Position.Z / 4096.0:0.0}) " +
                    $"d={distance / 4096.0:0.0} r={node.Radius:0.00} criteria={node.Criteria} links=[{links}]";
            }
        }
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
        int targetNode = NearestNode(LiveTargetNodePoint() ?? enemy);
        int? found = FindFreePositionFrom(targetNode, enemy, towardTarget, claim);
        if (claim)
        {
            LastFreeSearch = $"t{_lastTick} from={CurrentNode} target={targetNode} toward={towardTarget} -> {found?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
        }

        return found;
    }

    /// <summary>Diagnostics: the patrol chooser's out-of-sight count (the stand block's <c>+24</c>), or -1.</summary>
    public int PatrolNotVisibleTicks => _patrol?.NotVisibleTicks ?? -1;

    /// <summary>Gets or sets whether the soldier's Human is dead (<c>0x451330</c>), which the AI reads at the top of its tick.</summary>
    public bool HumanDead { get; set; }

    // +15034: AIEVENT_DEAD has been dispatched.
    private bool _deathDispatched;

    /// <summary>Gets or sets where the soldier's view stands (the view's <c>+224</c>, copied to <c>HumanAI+56</c>).</summary>
    public Func<Vector3d>? ViewPositionProvider { get; set; }

    /// <summary>Diagnostics: the node this soldier holds a claim on (<c>HumanAI+16032</c>), or -1.</summary>
    public int ClaimedNodeId => _occupancy?.ClaimedNode(this) ?? -1;

    /// <summary>Diagnostics: the eye the turn measures from, the tracked aim and the target point, for the probe's diff.</summary>
    public string AimText
    {
        get
        {
            Vector3d eye = ViewPositionProvider?.Invoke() ?? EyePosition();
            Vector3d trk = _combat?.TrackedAim ?? default;
            Vector3d tgt = TargetHuman is null ? default : TrueAimPoint();
            return FormattableString.Invariant(
                $"eye=({eye.X:0.0},{eye.Y:0.0},{eye.Z:0.0}) trk=({trk.X:0.0},{trk.Y:0.0},{trk.Z:0.0}) tgt=({tgt.X:0.0},{tgt.Y:0.0},{tgt.Z:0.0}) vp={ViewPitch:0.000000}");
        }
    }

    /// <summary>Diagnostics: the claimed cell's position (<c>HumanAI+16048</c>), x and y.</summary>
    public string ClaimedPositionText => _combatFreePosition is { } p
        ? FormattableString.Invariant($"({p.X:0.0},{p.Y:0.0})")
        : "-";

    /// <summary>The last claiming free-position search, for scenario traces.</summary>
    public string LastFreeSearch { get; private set; } = "-";

    private int? FindFreePositionFrom(int targetNode, Vector3d enemy, bool towardTarget, bool claim)
    {
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
                            node.Position.Z + FreePositionRayHeightOf(_graph)),
                        // 0x48B6E0 aims the can-shoot ray at 0x451380's point, the target's origin raised.
                        LiveTargetNodePoint() ?? AimPointOf(enemy))),
                occupancy: claim ? _occupancy : null,
                claimant: claim ? this : null,
                claimedPosition: claim ? position => _combatFreePosition = position : null);
    }

    private AiNodeOccupancy? _occupancy;
    private Vector3d? _combatFreePosition;

    /// <summary>Connects the level's shared node-occupancy registry — the list at <c>0x57D768</c>.</summary>
    /// <param name="occupancy">The registry, shared by every brain in the level.</param>
    public void SetNodeOccupancy(AiNodeOccupancy? occupancy) => _occupancy = occupancy;

    private bool _patrolKneeling;

    // How many times the patrol chooser has run -- for scenario traces.
    private int _chooserCalls;

    // Whether RePosition (0x48C760) owns the per-tick until its walk arrives.
    private bool _repositionLatched;

    // Whether LookForTarget (0x48CAB0) is the run function: installed by the stand arm, it runs one tick.
    private bool _lookForTargetLatched;

    // Slot 26, the tense run, which RePosition, Follow and LookForTarget write first thing on every tick they run
    // (HumanAI+15580 = 1.0).
    private bool _tenseRunThisTick;

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

    /// <summary>Where an AI aims at a human — its origin, half a metre up.</summary>
    /// <param name="target">The target's feet, as the port keeps a human's position.</param>
    /// <param name="crouching">Whether the target is crouching, which halves the raise.</param>
    /// <remarks>
    /// <c>sub_451380</c> copies the target task's own position (<c>Human+32</c>, its origin) out of the shared
    /// record and adds <b>2048 units (0.5 m) standing, 1024 (0.25 m) crouching</b> to Z, picking
    /// 1024 when the target is crouched and 2048 otherwise. The port's position is the feet, so the origin's height
    /// goes in first: chest height, where adding the raise to the feet aimed at the shins.
    /// </remarks>
    public static Vector3d AimPointOf(Vector3d target, bool crouching = false) =>
        new(target.X, target.Y, target.Z + (crouching
            ? HumanOriginCrouchedInUnits + 1024.0
            : HumanOriginStandingInUnits + 2048.0));

    /// <summary>The turn-to-point entry — <c>0x4525B0</c>, which sets the clamps to pi first.</summary>
    /// <param name="target">The point to turn to.</param>
    /// <param name="tolerance">How close counts as facing it.</param>
    /// <param name="level">
    /// Keep the view level rather than pitching it at the point's height — the entry's last argument, set
    /// by the door wait and by the walker, which levels the view as it goes.
    /// </param>
    /// <returns>Whether the view was already within the tolerance, in yaw and pitch, before this tick's turn.</returns>
    private bool TurnTowards(Vector3d target, float tolerance, bool level = false)
    {
        _steering.YawClamp = MathF.PI;
        _steering.PitchClamp = MathF.PI;
        return SteerTowards(target, tolerance, level);
    }

    /// <summary>
    /// Writes the turn toward a point into the frame — <c>0x4522D0</c>: the yaw error, clamped, over pi.
    /// </summary>
    /// <returns>Whether both errors, measured before this tick's turn, are within the tolerance.</returns>
    /// <remarks>
    /// It writes the frame whether or not the view is already good enough; the body does the turning. The
    /// pitch is the elevation from the eye (float 1), or back to level when <paramref name="level"/> is set.
    /// </remarks>
    private bool SteerTowards(Vector3d target, float tolerance, bool level = false)
    {
        double dx = target.X - Position.X;
        double dy = target.Y - Position.Y;
        float yawError = 0f;
        if ((dx * dx) + (dy * dy) >= 1.0)
        {
            yawError = Wrap((float)Math.Atan2(-dx, dy) - (Facing + RecoilYaw));
        }

        float yawLimit = _steering.YawClamp;
        _turnInput = Math.Clamp(yawError, -yawLimit, yawLimit) / MathF.PI;

        float elevation = 0f;
        if (!level)
        {
            // 0x4522D0 measures from the AI's frame (+56), which is the view's position: the origin raised 0.7 m, or
            // 0.35 m with the crouch byte set -- not the eye bone.
            Vector3d eye = ViewPositionProvider?.Invoke() ?? EyePosition();
            double ex = target.X - eye.X;
            double ey = target.Y - eye.Y;
            elevation = (float)Math.Atan2(target.Z - eye.Z, Math.Sqrt((ex * ex) + (ey * ey)));
        }

        float pitchError = elevation - (ViewPitch + RecoilPitch);
        float pitchLimit = _steering.PitchClamp;
        _pitchInput = Math.Clamp(pitchError, -pitchLimit, pitchLimit) / MathF.PI;
        return Math.Abs(yawError) <= tolerance && Math.Abs(pitchError) <= tolerance;
    }

    /// <summary>Where the target human stands now, seen or not -- what <c>0x451380</c> reads (its <c>+32</c>).</summary>
    /// <param name="fallback">The point to use when no human is tracked as the target.</param>
    /// <remarks>
    /// The shared record's copy (<c>+272</c>) is refreshed only while the target is seen or an event arrives
    /// (<c>0x48A920</c>); the node lookup <c>0x4530A0</c> and the free-position search <c>0x48B050</c> both call
    /// <c>0x451380</c> instead, which reads the target human itself. Mission 4's guard 411 lost sight of the squad for
    /// 36 ticks and its node still moved from 13 to 14 halfway through.
    /// </remarks>
    private Vector3d LiveTargetFeet(Vector3d fallback) => TargetHuman is null ? fallback : TargetFeet;

    // 0x451380's point for a node search: the target's origin raised half a metre (0x4530A0, 0x48B050).
    private Vector3d? LiveTargetNodePoint() => TargetHuman?.Candidate.NodeQueryPoint;

    /// <summary>0x48A920's tracking of the node nearest the target -- <c>HumanAI+15852</c> and <c>+15856</c>.</summary>
    /// <param name="enemy">Where the target is.</param>
    private void TrackTargetNode(Vector3d enemy)
    {
        int node = NearestNode(enemy);
        if (node < 0)
        {
            _followTargetNode = -1;
        }
        else if (_followTargetNode == -1)
        {
            _followTargetNode = node;
        }
        else if (_followTargetNode != node)
        {
            _followTargetNode = node;
            _followResearch = true;
        }
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

}
