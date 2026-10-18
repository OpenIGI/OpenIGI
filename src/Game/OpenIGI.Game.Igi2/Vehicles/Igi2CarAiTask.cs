using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>
/// A car's driver, I.G.I. 2's <c>CarAI</c> (carai.c, an <c>EditBoneObj</c>): it follows its patrol route over its AI graph, chases what its
/// car's gun sees, and drives the car through its steering, thrust and brake.
/// </summary>
/// <remarks>
/// <para>
/// <c>CarAI_RunHandler</c>, after its children: its route is started the first time; then, while its car, its graph, and a route or a
/// fight are there, it drives. Its driving expression, where it has one, says whether it is at the wheel. Dead, or not at the wheel, it
/// stops: no speed and full brake. In a fight it heads for the graph node nearest what it saw, and once there turns on the spot to aim
/// the car at it. On its route it carries out the route's commands: to a node it drives (at the speed last set), a delay it waits out
/// braked a tick at a time, a speed it sets, an expression it waits for, and an end or a quit stops it; anything else it passes over.
/// Then the car is driven a tick (<c>CarAI_UpdateCar</c>).
/// </para>
/// <para>
/// Its way to a node is an A* path over the graph (<c>CarAI_MakePath</c>), from whichever end of the edge it is on is nearer the node;
/// it heads for each node of the path in turn, reaching one when it is within three metres across and eight along the car, or three
/// metres round in a fight (<c>CarAI_IsOnNode</c>). Its speed is the target speed where the way runs straight on and five where it
/// turns square (<c>CarAI_FindWantedSpeedPatrol</c>); its thrust closes on that speed a hundredth at a time, braking when it is three
/// over (<c>CarAI_FindWantedThrust</c>); and it steers full lock past ten degrees off, less the more thrust it has.
/// </para>
/// </remarks>
public sealed partial class Igi2CarAiTask : Igi2Task, IIgi2PlacedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "CarAI";

    /// <summary>The ticks a fight is remembered once nothing is seen.</summary>
    public const int DetectionTicks = 600;

    /// <summary>Ten degrees: past it the car steers full lock.</summary>
    public const double FullLock = 0.1745329350233078;

    private readonly Igi2PatrolControl _patrol = new();
    private Igi2AiPathSearch? _search;
    private Igi2AiGraph? _graph;
    private Igi2CarTask? _car;
    private LevelExpression? _driving;
    private Igi2AiGraphNode? _targetNode;
    private Igi2Human? _detected;
    private byte _isDead;
    private bool _isDriving = true;
    private bool _isRouteStarted;
    private int _time;
    private int _detectionCountdown;
    private int _commandDelay;
    private float _commandSpeed;
    private int _pathIndex;
    private int _startNode = -1;
    private int _edgeNode0 = -1;
    private int _edgeNode1 = -1;
    private int _destNode = -1;

    /// <summary>Gets or sets where its driver sits, relative to the car.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its driver's turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its driver's model; none drives unseen.</summary>
    [ScriptParam(ScriptParamType.String16, "Driver Model")]
    public string DriverModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its driver's animation while driving.</summary>
    [ScriptParam(ScriptParamType.String32, "Driving animation")]
    public string DrivingAnimation { get; set; } = string.Empty;

    /// <summary>Gets or sets its driver's animation while not driving.</summary>
    [ScriptParam(ScriptParamType.String32, "Idle animation")]
    public string IdleAnimation { get; set; } = string.Empty;

    /// <summary>Gets or sets its driver's animation as it dies.</summary>
    [ScriptParam(ScriptParamType.String32, "Death animation")]
    public string DeathAnimation { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it is at the wheel.</summary>
    [ScriptParam(ScriptParamType.VarString, "Driving expression")]
    public string DrivingExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its driver's soldier type, whose damage it takes.</summary>
    [ScriptParam(ScriptParamType.String32, "AI Type")]
    public string AiType { get; set; } = string.Empty;

    /// <summary>Gets or sets the task id of the AI graph it drives over.</summary>
    [ScriptParam(ScriptParamType.Int32, "Graph ID")]
    public int GraphId { get; set; } = -1;

    /// <summary>Gets or sets the task id of the patrol route it follows; -1 for none.</summary>
    [ScriptParam(ScriptParamType.Int32, "Route ID")]
    public int RouteId { get; set; } = -1;

    /// <summary>Gets the car it drives, its nearest <c>Car</c> ancestor.</summary>
    public Igi2CarTask? Car => _car;

    /// <summary>Gets a value indicating whether its driver is dead, <c>isDead</c>.</summary>
    public bool IsDead => _isDead != 0;

    /// <summary>Gets a value indicating whether it is at the wheel, <c>isDriving</c>.</summary>
    public bool IsDriving => _isDriving;

    /// <summary>Gets the thrust it wants, <c>vWantedThrust</c>.</summary>
    public float WantedThrust { get; private set; }

    /// <summary>Gets the steering it wants, <c>vWantedGamma</c>.</summary>
    public float WantedGamma { get; private set; }

    /// <summary>Gets the brake it wants, <c>vWantedBreak</c>.</summary>
    public float WantedBreak { get; private set; }

    /// <summary>Gets the speed it wants, <c>vWantedSpeed</c>.</summary>
    public float WantedSpeed { get; private set; }

    /// <summary>Gets the speed it aims at, <c>vTargetSpeed</c>.</summary>
    public float TargetSpeed { get; private set; }

    /// <summary>Gets the brake it aims at, <c>vTargetBreak</c>.</summary>
    public float TargetBreak { get; private set; }

    /// <summary>Gets the node it heads for, <c>ptTargetNode</c>.</summary>
    public Igi2AiGraphNode? TargetNode => _targetNode;

    /// <summary>Gets the human it fights, <c>ptDetectedHuman</c>.</summary>
    public Igi2Human? DetectedHuman => _detected;

    /// <summary>Gets its route.</summary>
    public Igi2PatrolControl Patrol => _patrol;

    /// <summary>Gets or sets the AI world its graph, its route and its route's expressions come from.</summary>
    public Igi2AiWorld? World { get; set; }

    /// <summary>
    /// Finds its car and graph and sets the car back to its start, as the level finishes loading:
    /// <c>CarAI_UpdateInternalDataAfterLoadHandler</c>.
    /// </summary>
    /// <param name="world">The AI world.</param>
    public void Prepare(Igi2AiWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        _search = new Igi2AiPathSearch(world.SearchPool);
        _car = null;
        for (GameTask? up = Parent; up is not null; up = up.Parent)
        {
            if (up is Igi2CarTask car)
            {
                _car = car;
                break;
            }
        }

        _graph = world.Graphs?.Graph(GraphId);
        _car?.Drive(0, [0f, 0f, 0f, Igi2CarTask.HoldChannel], 0);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
        if (!_isRouteStarted && RouteId != -1)
        {
            _patrol.Init(World?.PatrolPath(RouteId), 0);
            _isRouteStarted = true;
        }

        bool isCombat = IsCombat();
        if (_car is not { } car || _graph is null || !(isCombat || _patrol.Path is not null))
        {
            return;
        }

        if (_isDead == 0)
        {
            _driving ??= Igi2LevelExpressions.Create(DrivingExpression, Name, level.Natives);
            _isDriving = _driving.IsEmpty || LevelExpression.Truncate(level.Evaluate(_driving)) != 0;
        }

        if (_isDead != 0 || !_isDriving)
        {
            Stop();
        }
        else
        {
            switch (Steer(isCombat))
            {
                case Igi2CarAiStep.Stop:
                    Stop();
                    break;
                case Igi2CarAiStep.Skip:
                    return;
            }
        }

        // CarAI_UpdateCar: a brake of nought is sent as -1, none.
        int time = _time++;
        car.Drive(time, [WantedGamma, WantedThrust, 0f, WantedBreak == 0f ? Igi2CarTask.HoldChannel : WantedBreak], level.LevelFlow?.Tick ?? time);
    }

    /// <inheritdoc/>
    /// <remarks><c>CarAI_RegisterScriptSymbolsHandler</c>: one byte, <c>isDead</c>.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isDead", () => _isDead, value => _isDead = (byte)value, sizeof(byte));
    }

    // The fight, or the route's command: what it drives towards, whether it stops instead (LABEL_56), or whether the car is not driven
    // at all this tick, as when the route walks to a node the graph does not have.
    private Igi2CarAiStep Steer(bool isCombat)
    {
        if (isCombat)
        {
            Igi2AiGraphNode? node = _targetNode;
            if (node is null || IsOnNode(node.Id))
            {
                if (_detected is { } human && _graph!.ClosestNode(human.Position, false, out Igi2AiGraphNode? closest) != Igi2AiClosestNode.NoNode
                    && closest is not null)
                {
                    _targetNode = closest;
                    MakePath(closest.Id);
                }

                node = _targetNode;
            }

            bool arrived = node is not null && IsOnNode(node.Id);
            TargetSpeed = arrived ? 0f : 15f;
            TargetBreak = arrived ? 1f : 0f;
            UpdateCombat();
            return Igi2CarAiStep.Drive;
        }

        if (_patrol.Current is not { } command)
        {
            return Igi2CarAiStep.Stop;
        }

        switch (command.Command)
        {
            case Igi2PatrolCommand.WalkTo or Igi2PatrolCommand.RunTo:
                if (IsOnNode(command.Parameter))
                {
                    _patrol.StepForward();
                    return Igi2CarAiStep.Drive;
                }

                _targetNode = _graph!.NodeAt(command.Parameter);
                if (_targetNode is null)
                {
                    return Igi2CarAiStep.Skip;
                }

                MakePath(_targetNode.Id);
                TargetSpeed = _commandSpeed;
                TargetBreak = 0f;
                UpdatePatrol();
                return Igi2CarAiStep.Drive;
            case Igi2PatrolCommand.Delay:
                TargetSpeed = 0f;
                TargetBreak = 1f;
                int waited = _commandDelay++;
                if (waited > command.Parameter)
                {
                    _patrol.StepForward();
                    _commandDelay = 0;
                }

                return Igi2CarAiStep.Drive;
            case Igi2PatrolCommand.End or Igi2PatrolCommand.Quit:
                return Igi2CarAiStep.Stop;
            case Igi2PatrolCommand.SetSpeed:
                TargetBreak = 0f;
                _commandSpeed = command.Parameter;
                _patrol.StepForward();
                return Igi2CarAiStep.Drive;
            case Igi2PatrolCommand.WaitForExpression when !(World?.EvaluateCommand(command) ?? false):
                return Igi2CarAiStep.Stop;
            default:
                _patrol.StepForward();
                return Igi2CarAiStep.Drive;
        }
    }

    // LABEL_56: no speed, braked full, unsteered.
    private void Stop()
    {
        TargetSpeed = 0f;
        TargetBreak = 1f;
        WantedBreak = 1f;
        WantedSpeed = 0f;
        WantedGamma = 0f;
    }

    // CarAI_IsCombat: only while its car may fire, and its human is alive. A human its car's detection has in plain sight is fought,
    // and remembered six hundred ticks.
    private bool IsCombat()
    {
        if (_car is not { } car)
        {
            return false;
        }

        if (!car.IsFire || _detected is { IsDead: true })
        {
            _detected = null;
            _detectionCountdown = 0;
            return false;
        }

        if (car.Detection is { } detection)
        {
            foreach (Igi2DetectionItem item in detection.Enemies)
            {
                if ((item.FullVisible[0] || item.FullVisible[1] || item.FullVisible[2]) && item.Target is Igi2Human human)
                {
                    _detectionCountdown = DetectionTicks;
                    _detected = human;
                    return true;
                }
            }
        }

        if (_detectionCountdown != 0)
        {
            _detectionCountdown--;
            return true;
        }

        _detected = null;
        return false;
    }

    // CarAI_IsOnNode: in a fight, within three metres round; otherwise within three metres across the car and eight along it.
    private bool IsOnNode(int nodeId)
    {
        if (_graph?.NodeAt(nodeId) is not { } node || _car is not { } car)
        {
            return false;
        }

        Vector3d at = car.CurrentPosition;
        float dx = (float)(node.Position.X - at.X), dy = (float)(node.Position.Y - at.Y);
        double dz = node.Position.Z - at.Z;
        if (IsCombat())
        {
            return Math.Sqrt((dx * (double)dx) + (dy * (double)dy) + (dz * dz)) < 12288.0;
        }

        IReadOnlyList<float> m = car.CurrentOrientation;
        float lx = (float)((dz * m[6]) + (dy * (double)m[3]) + (dx * (double)m[0]));
        double ly = (dz * m[7]) + (dy * (double)m[4]) + (dx * (double)m[1]);
        return ly < 12288.0 && ly > -12288.0 && lx < 32768.0 && lx > -32768.0;
    }

    // CarAI_MakePath: from the end of its edge nearer the node, or the node nearest the car before it is on any.
    private bool MakePath(int endNode)
    {
        if (_graph is not { } graph || _search is not { } search || _car is not { } car)
        {
            return false;
        }

        int start = _edgeNode0;
        if (start == -1)
        {
            graph.ClosestNode(car.CurrentPosition, false, out Igi2AiGraphNode? closest);
            start = closest?.Id ?? -1;
        }

        if (search.GraphId == graph.Id && search.SourceNodeId == _startNode && search.DestNodeId == endNode && search.CurrentPath.Count > 0)
        {
            return true;
        }

        if (_edgeNode0 != -1 && graph.NodeAt(endNode) is { } dest && graph.NodeAt(_edgeNode0) is { } e0 && graph.NodeAt(_edgeNode1) is { } e1)
        {
            float ex = (float)(dest.Position.X - e1.Position.X), ey = (float)(dest.Position.Y - e1.Position.Y);
            float ez = (float)(dest.Position.Z - e1.Position.Z);
            double toE1 = Math.Sqrt((ex * (double)ex) + (ey * (double)ey) + (ez * (double)ez));
            double fx = dest.Position.X - e0.Position.X, fy = dest.Position.Y - e0.Position.Y, fz = dest.Position.Z - e0.Position.Z;
            double toE0 = Math.Sqrt((fx * fx) + (fy * fy) + (fz * fz));
            start = toE1 <= toE0 ? e1.Id : e0.Id;
        }

        if (!search.CreatePath(graph, start, endNode, 0xFFFFFFFF))
        {
            return false;
        }

        _startNode = start;
        _pathIndex = 0;
        _edgeNode0 = start;
        _edgeNode1 = start;
        _destNode = endNode;
        return true;
    }

    // CarAI_GetPosFromPath: the node of its path it heads for, moving on to the next once on it; with no path, where it is.
    private Vector3d PositionFromPath(Vector3d current)
    {
        if (_graph is not { } graph || _search is not { } search
            || search.GraphId != graph.Id || search.SourceNodeId != _startNode || search.DestNodeId != _destNode
            || search.CurrentPath.Count == 0)
        {
            return current;
        }

        IReadOnlyList<Igi2AiSearchNode> path = search.CurrentPath;
        int at = Math.Min(_pathIndex, path.Count - 1);
        Igi2AiSearchNode node = path[at];
        Vector3d position = graph.NodeAt(node.NodeId)?.Position ?? default;
        if (IsOnNode(node.NodeId) && _pathIndex < path.Count)
        {
            _edgeNode0 = node.NodeId;
            _edgeNode1 = at + 1 < path.Count ? path[at + 1].NodeId : node.NodeId;
            _pathIndex++;
        }

        return position;
    }

    // CarAI_UpdateAIStatePatrol.
    private void UpdatePatrol()
    {
        WantedSpeed = WantedSpeedPatrol();
        FindWantedThrust();
        float gamma = GammaTo(PositionFromPath(_car!.CurrentPosition) - _car.CurrentPosition);
        WantedGamma = SteerFor(gamma);
        WantedGamma = (((((Fpu)1.0 - WantedThrust) * 0.800000011920929) + 0.20000000298023224) * WantedGamma).F;
    }

    // CarAI_UpdateAIStateCombat: with no target speed it turns on the spot to aim at what it fights, driving gently while it is off.
    private void UpdateCombat()
    {
        Igi2CarTask car = _car!;
        bool isAim = TargetSpeed == 0f;
        Vector3d target = PositionFromPath(car.CurrentPosition);
        Vector3d d = isAim && _detected is { } human ? human.Position - car.CurrentPosition : target - car.CurrentPosition;
        float gamma = GammaTo(d);
        WantedGamma = SteerFor(gamma);
        if (!isAim)
        {
            WantedSpeed = WantedSpeedCombat();
        }
        else
        {
            WantedSpeed = Math.Abs(gamma) <= FullLock ? (Math.Abs(gamma) * 28.64788818359375f) : 5f;
        }

        FindWantedThrust();
        WantedBreak = isAim && Math.Abs(gamma) > FullLock ? 0f : TargetBreak;
    }

    // Full lock past ten degrees off, either way; within, in proportion.
    private static float SteerFor(float gamma) =>
        gamma > FullLock ? -1f : gamma < -FullLock ? 1f : ((Fpu)gamma * -1.909859299659729).F;

    // GameFunctions_CalcGamma of a world offset taken into the car's frame.
    private float GammaTo(Vector3d d)
    {
        IReadOnlyList<float> m = _car!.CurrentOrientation;
        double x = (m[6] * d.Z) + (m[3] * d.Y) + (m[0] * d.X);
        double y = (m[7] * d.Z) + (m[4] * d.Y) + (m[1] * d.X);
        return Igi2Pose.CalcGamma(x, y);
    }

    // CarAI_FindWantedSpeedPatrol: near its node (ten metres), by how far the way turns at it; further, by how far off it is.
    private float WantedSpeedPatrol() => SpeedFor(1677721600.0, (angle, speed) => speed < 5f ? speed : null, Corner);

    // CarAI_FindWantedSpeedCombat: near its node (twenty metres), five past ten degrees of turn; further, as on its route.
    private float WantedSpeedCombat() =>
        SpeedFor(
            6710886400.0,
            (angle, speed) => speed >= 5f
                ? angle >= 0.17453293005625406 ? 5f : (float)((((1.3962634354829788 - angle) * 0.7161972265313185) * (speed - 5.0)) + 5.0)
                : speed,
            Corner);

    private static float Corner(double angle, float speed) =>
        (float)((((1.5707963705062866 - angle) * 0.6366197546520227) * (speed - 5.0)) + 5.0);

    private float SpeedFor(double nearSquared, Func<double, float, float?> near, Func<double, float, float> far)
    {
        Igi2CarTask car = _car!;
        Vector3d at = car.CurrentPosition;
        Vector3d d = PositionFromPath(at) - at;
        if (((d.Y * d.Y) + (d.Z * d.Z) + (d.X * d.X)) <= nearSquared)
        {
            Vector3d next = PositionFromPath(car.CurrentPosition) - at;
            double angle = Angle(d, next);
            return near(angle, TargetSpeed) ?? far(angle, TargetSpeed);
        }

        double off = Math.Abs(GammaTo(d));
        return far(off >= 1.5707963705062866 ? 1.5707963705062866 : off, TargetSpeed);
    }

    // CarAI_FindWantedThrust: the speed it is short by, along the car, closed a hundredth at a time; braking when three over, or when
    // its target says so.
    private void FindWantedThrust()
    {
        Igi2CarTask car = _car!;
        IReadOnlyList<float> m = car.CurrentOrientation;
        Vector3f v = car.Body?.Body.Velocity ?? default;
        Fpu along = (((Fpu)m[7] * v.Z) + ((Fpu)m[4] * v.Y)) + ((Fpu)m[1] * v.X);
        Fpu shortBy = (Fpu)WantedSpeed - (along * 0.0263671875);
        float thrust = ((Fpu)car.RealThrust + (shortBy * 0.009999999776482582)).F;
        WantedThrust = thrust < 0f ? 0f : thrust > 1f ? 1f : thrust;
        WantedBreak = TargetBreak == 0f && shortBy.Value >= -3.0 ? 0f : 1f;
    }

    // Real64x3_Angle.
    private static double Angle(Vector3d a, Vector3d b)
    {
        if ((a.X == 0.0 && a.Y == 0.0 && a.Z == 0.0) || (b.X == 0.0 && b.Y == 0.0 && b.Z == 0.0))
        {
            return 0.0;
        }

        double cosine = ((a.X * b.X) + (a.Z * b.Z) + (a.Y * b.Y))
            / (Math.Sqrt((a.Y * a.Y) + (a.Z * a.Z) + (a.X * a.X)) * Math.Sqrt((b.Y * b.Y) + (b.Z * b.Z) + (b.X * b.X)));
        return cosine >= 1.0 ? 0.0 : cosine > -1.0 ? Math.Acos(cosine) : 3.1415926535897944;
    }
}

/// <summary>What a car's driver does with its car this tick.</summary>
internal enum Igi2CarAiStep
{
    /// <summary>Drives it as it wants.</summary>
    Drive,

    /// <summary>Stops it: no speed, braked full, unsteered.</summary>
    Stop,

    /// <summary>Leaves it undriven.</summary>
    Skip,
}
