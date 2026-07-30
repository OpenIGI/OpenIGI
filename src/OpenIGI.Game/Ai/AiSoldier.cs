using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Player;

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
/// <b>What is not:</b> the behaviour script, the 18 action bodies, authored patrol paths (this wanders the
/// graph instead), and hearing. Those are listed in <c>TODO.md</c>.
/// </para>
/// </remarks>
public sealed class AiSoldier
{
    private readonly GraphFile _graph;
    private readonly AiDetectionSchedule _schedule;
    private readonly AiDetectionSlot _playerSlot = new();
    private readonly AiMovementStep _steering = new();
    private readonly Random _random;
    private readonly List<int> _route = [];

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
    private GroundQuery? _ground;
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
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public AiSoldier(GraphFile graph, Vector3d position, float facing, int phase, int seed)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _graph = graph;
        Position = position;
        _legOrigin = position;
        Facing = facing;
        _random = new Random(seed);

        // The cone test runs every other sweep and the ray every fourth, which keeps the cascade's shape
        // without the vision header's own intervals, which are not written by any path read so far.
        _schedule = new AiDetectionSchedule(phase & 3, coneInterval: 1, rayInterval: 3);
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

    /// <summary>Gets its view cones.</summary>
    public AiVision Vision { get; }

    /// <summary>
    /// Gets the eye snapshot the last sweep tested against — <c>HumanAI+14896</c>, rebuilt on every sweep.
    /// </summary>
    public AiEyeSnapshot Eye { get; private set; }

    /// <summary>Gets its event queues.</summary>
    public AiEventQueues Events { get; }

    /// <summary>Gets the patrol currently being walked, or <see langword="null"/> when there is none.</summary>
    public AiPatrolRoute? Route { get; private set; }

    /// <summary>Gets a value indicating whether the soldier is walking rather than running.</summary>
    /// <remarks>A patrol walks by default; only a <see cref="AiPatrolCommandKind.RunTo"/> clears this.</remarks>
    public bool IsWalking => Route?.IsWalking ?? true;

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

    /// <summary>Gets the orientation matrix a renderer should draw it with.</summary>
    public Matrix3f Orientation => Matrix3f.FromEngineEulerAngles(0f, 0f, Facing);

    /// <summary>Gives the soldier an occlusion test, so it stops seeing through walls.</summary>
    /// <param name="sight">The test, or <see langword="null"/> to see through everything.</param>
    public void SetLineOfSight(LineOfSight? sight) => _sight = sight;

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
                    GoTo(action.Argument(0));
                    break;

                case "AIAction_WalkToNode":
                    Route = null;
                    GoTo(action.Argument(0));
                    break;

                case "AIAction_Combat":
                case "AIAction_SetCombat":
                case "AIAction_FireAtTask":
                case "AIAction_FireAtNode":
                    Activity = AiSoldierActivity.Engaging;
                    Route = null;
                    RequestedAnimation = -1;
                    _route.Clear();
                    DestinationNode = -1;
                    break;

                case "AIAction_Dead":
                    Kill();
                    break;

                case "AIAction_Idle":
                    Activity = AiSoldierActivity.Idle;
                    _route.Clear();
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
        if (_graph.RouteTable is null)
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

        _route.Clear();
        DestinationNode = node;
        _legOrigin = Position;

        if (CurrentNode == node)
        {
            return true;
        }

        IReadOnlyList<int> route = _graph.RouteTable.EnumerateRoute(CurrentNode, node);
        if (route.Count == 0)
        {
            return false;
        }

        _route.AddRange(route);
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
        if (facing is { } heading)
        {
            Facing = Wrap(heading);
        }
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
    /// path takes.
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
    public void Tick(int tick, Vector3d enemy, double rootMotionThisTick, bool alarmOn = false)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return;
        }

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
        if (_schedule.SweepsOn(tick))
        {
            Eye = Vision.Capture(EyePosition(), Orientation, Alerted);
            AiEyeSnapshot snapshot = Eye;
            _schedule.Sweep(
                _playerSlot,
                () => snapshot.IsVisible(enemy),
                () => _sight is null || _sight(EyePosition(), enemy),
                Distance(Position, enemy));
        }

        if (_playerSlot.IsVisible)
        {
            alert = true;
            LastEventDistance = _playerSlot.Distance;
            Events.Post(AiEventRecord.Simple(AiEvent.EnemyDetection), owner: 0, worldTests: null);
        }

        if (alarmOn)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.AlarmOn), owner: 0, worldTests: null);
        }

        // The alert latch is recomputed from scratch every tick, from those two causes only (ai.md 3.1).
        Alerted = alert;

        if (_playerSlot.IsVisible)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Combat), owner: 0, worldTests: null);
        }
        else if (_route.Count == 0 && Route is null)
        {
            // Idle is the fallback the original posts only when nothing is pending (ai.md 3.1).
            // A running patrol IS a pending action — re-posting IDLE while one is active would make
            // the script call AIAction_Patrol again, restarting the patrol from command 0 every tick.
            Events.Post(AiEventRecord.Simple(AiEvent.Idle), owner: 0, worldTests: null);
        }

        // ONE event type dispatches per tick, in priority order, and the script decides what to do about it.
        RunScript();

        if (_playerSlot.IsVisible && Activity == AiSoldierActivity.Engaging)
        {
            TurnTowards(enemy, AiPatrolRoute.LookAtToleranceRadians);
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
        GraphNode? target = _graph.NodeAt(node);
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
            if (_playerSlot.IsVisible)
            {
                Activity = AiSoldierActivity.Engaging;
                _route.Clear();
                DestinationNode = -1;
            }

            Drain();
            return;
        }

        Events.Pump(AiEventQueues.UnboundedPriorityCap, (type, slot) =>
        {
            bool handled = _host.Run(_script, this, type, slot, _archetypeScript);
            if (handled)
            {
                ApplyActions(_host.Produced);
            }

            return handled;
        });
    }

    /// <summary>The eye the cones are measured from — the body position raised to eye height.</summary>
    /// <returns>The eye position.</returns>
    /// <remarks>
    /// The original snapshots the head bone's own world position each tick (<c>ai.md</c> §3.1). Until the
    /// soldier's rig is queried for it, the separation cylinder's own half-height is the honest stand-in: it
    /// is a read constant rather than a chosen one.
    /// </remarks>
    public Vector3d EyePosition() =>
        new(Position.X, Position.Y, Position.Z + HumanSeparationEyeHeight);

    /// <summary>The eye height used until the head bone is queried — the separation cylinder's half-height.</summary>
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
        if (_graph.RouteTable is null || _graph.Nodes.Count < 2)
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
            GraphNode candidate = _graph.Nodes[_random.Next(_graph.Nodes.Count)];
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
        GraphNode? next = _graph.NodeAt(_route[0]);
        if (next is null)
        {
            _route.RemoveAt(0);
            return;
        }

        Vector3d waypoint = next.Position;

        // Turn towards the waypoint as fast as the move-to-node clamp allows -- pi, which is "as fast as you
        // like" (ai.md 7).
        TurnTowards(waypoint, 0f);

        AiMovementOutcome outcome = _steering.Step(
            Position, Orientation, waypoint, 0f, 0f, out AiSteeringFrame frame);

        if (outcome == AiMovementOutcome.WaypointReached)
        {
            CurrentNode = next.Id;
            _route.RemoveAt(0);

            // The next leg is measured from where the soldier actually is, not from the node it nominally
            // reached -- arrival is a 0.3 m box, so those differ.
            _legOrigin = Position;
            return;
        }

        // Forward is a literal 1.0 in the frame, so the distance covered is the clip's own root motion.
        if (frame.Forward > 0f && rootMotionThisTick > 0.0)
        {
            double x = Position.X + (Math.Sin(-Facing) * rootMotionThisTick);
            double y = Position.Y + (Math.Cos(Facing) * rootMotionThisTick);
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
        GraphNearestNode.Find(_graph, from, out GraphNode? best, out _) == GraphNearestNodeResult.None
            ? -1
            : best!.Id;

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

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
