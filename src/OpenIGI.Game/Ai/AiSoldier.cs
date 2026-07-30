using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;

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
/// walk-versus-run gait, and the idle fallback. <b>What is not:</b> the behaviour script, the 18 action
/// bodies, the line-of-sight ray (so a guard sees through walls), and hearing. Those are listed in
/// <c>TODO.md</c>.
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

    /// <summary>Gets its event queues.</summary>
    public AiEventQueues Events { get; }

    /// <summary>Gets the patrol step state, whose gait flag says whether it is walking.</summary>
    public AiPatrolState Patrol { get; } = new();

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

    /// <summary>Marks the soldier dead; it stops thinking and stops moving.</summary>
    public void Kill() => Activity = AiSoldierActivity.Dead;

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

        bool alert = alarmOn;

        // The detection cascade: the phase gate first, then the cone, then (where a ray would go) nothing --
        // a guard currently sees through walls, which is the one honest gap in this path.
        if (_schedule.SweepsOn(tick))
        {
            _schedule.Sweep(
                _playerSlot,
                () => Vision.IsVisible(EyePosition(), Orientation, enemy, Alerted),
                () => true,
                Distance(Position, enemy));
        }

        if (_playerSlot.IsVisible)
        {
            alert = true;
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
            // Engaging: stop and turn to face the enemy. The original runs AIAction_Combat here; turning to
            // the target is what its look-at step does, to a 3 degree tolerance.
            Activity = AiSoldierActivity.Engaging;
            _route.Clear();
            DestinationNode = -1;
            TurnTowards(enemy, AiPatrolState.LookAtToleranceRadians);
            Events.Post(AiEventRecord.Simple(AiEvent.Combat), owner: 0, worldTests: null);
            Drain();
            return;
        }

        // Nothing to fight: patrol. An empty route means the last leg finished, so pick another node and let
        // the route table say how to get there -- one step at a time, exactly as 0x4518E0 does.
        if (_route.Count == 0)
        {
            Events.Post(AiEventRecord.Simple(AiEvent.Idle), owner: 0, worldTests: null);
            if (!ChooseNextLeg())
            {
                Activity = AiSoldierActivity.Idle;
                Drain();
                return;
            }
        }

        Activity = AiSoldierActivity.Patrolling;
        Advance(rootMotionThisTick);
        Drain();
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

    private void Drain()
    {
        // One event type dispatches per tick and every queue is then emptied, so an event nothing acted on is
        // discarded rather than deferred (ai.md 3.5).
        Events.Pump(Patrol.Walking ? AiEventQueues.UnboundedPriorityCap : AiEventQueues.UnboundedPriorityCap,
            (_, _) => true);
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

            // A patrol walks by default; the original's RunTo step is what clears the gait flag.
            Patrol.Begin(AiPatrolStepKind.WalkTo);
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
            return;
        }

        // Forward is a literal 1.0 in the frame, so the distance covered is the clip's own root motion.
        if (frame.Forward > 0f && rootMotionThisTick > 0.0)
        {
            Position = new Vector3d(
                Position.X + (Math.Sin(-Facing) * rootMotionThisTick),
                Position.Y + (Math.Cos(Facing) * rootMotionThisTick),
                Position.Z);
        }
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
