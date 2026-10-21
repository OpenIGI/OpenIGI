using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// One step of a soldier's way ahead, <c>HumanAIMovementQueueItem_t</c>: where to walk, what to do there (nought walk on, one open a
/// door, four and five a door's flashbang), and the node it stands for.
/// </summary>
public sealed class Igi2AiMovementItem
{
    /// <summary>Gets or sets where to walk.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets what to do there, <c>eAction</c>.</summary>
    public int Action { get; set; }

    /// <summary>Gets or sets the node it stands for, <c>nNodeID</c>.</summary>
    public int NodeId { get; set; }

    /// <summary>Gets or sets whether its stand action has played, <c>tActionContext</c>.</summary>
    public bool IsStarted { get; set; }
}

/// <content>How a mind walks its soldier along the graph and turns it (<c>HumanAI_MoveCharacter*</c>, <c>HumanAI_Face*</c>).</content>
public sealed partial class Igi2HumanAi
{
    /// <summary>A fifth of a radian a tick, the walking and patrolling turn.</summary>
    public const float PatrolTurn = 0.20943951606750488f;

    /// <summary>The attack turn.</summary>
    public const float AttackTurn = 0.3141592741012573f;

    private const double ArrivedAcross = 1228.8;
    private const double ArrivedUp = 8192.0;
    private const double FacingAhead = 0.1745329350233078;
    private const double RollWindow = 0.03490658476948738;
    private const float InverseOfPi = 0.31830987334251404f;
    private readonly List<Igi2AiMovementItem> _movementQueue = [];

    /// <summary>Gets its way ahead, the first step first, <c>tMovementQueue</c>.</summary>
    public List<Igi2AiMovementItem> MovementQueue => _movementQueue;

    /// <summary>Gets a value indicating whether it has no way ahead, <c>HumanAI_IsEmptyMovementQueue</c>.</summary>
    public bool IsEmptyMovementQueue => _movementQueue.Count == 0;

    /// <summary>Gets or sets the way it last moved, <c>eLastMovementDirection</c>: one forward, two left, three right, four back.</summary>
    public int LastMovementDirection { get; set; }

    /// <summary>Lets go of its way ahead, taking each step's weight off its node (<c>HumanAI_ClearMovementQueue</c>).</summary>
    public void ClearMovementQueue()
    {
        foreach (Igi2AiMovementItem item in _movementQueue)
        {
            Graph?.AddWeight(item.NodeId, -20480f);
        }

        _movementQueue.Clear();
    }

    /// <summary>Its way ahead let go, and the door it was using with it (<c>HumanAI_CleanUpMovement</c>).</summary>
    public void CleanUpMovement()
    {
        ClearMovementQueue();
        ReleaseDoor();
    }

    /// <summary>
    /// Whether it stands at a point (<c>HumanAI_IsDestinationReached</c>): within 1228.8 across each way and 8192 up or down.
    /// </summary>
    /// <param name="destination">The point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsDestinationReached(Vector3d destination)
    {
        Vector3d at = Soldier.Position;
        double dx = destination.X - at.X;
        float dy = (float)(destination.Y - at.Y), dz = (float)(destination.Z - at.Z);
        return Math.Abs(dx) <= ArrivedAcross && Math.Abs((double)dy) <= ArrivedAcross && Math.Abs((double)dz) <= ArrivedUp;
    }

    /// <summary>
    /// How far its eyes look above or below level, <c>HumanAI_GetLevelSightDelta</c>: the body's forward axis seen from the view,
    /// as the arc cosine of its normalised forward share, negative when it points down.
    /// </summary>
    /// <returns>The tilt, in radians.</returns>
    public float LevelSightDelta()
    {
        float[] o = View.Orientation, v = View.ViewOrientation;
        Fpu f0 = (((Fpu)o[2] + o[0]) * 0.0) + o[1];
        Fpu f1 = (((Fpu)o[5] + o[3]) * 0.0) + o[4];
        Fpu f2 = (((Fpu)o[8] + o[6]) * 0.0) + o[7];
        Fpu y = (((Fpu)v[7] * f2) + ((Fpu)v[4] * f1)) + ((Fpu)v[1] * f0);
        Fpu z = (((Fpu)v[8] * f2) + ((Fpu)v[5] * f1)) + ((Fpu)v[2] * f0);
        Fpu ay = y, az = z;
        if (y.Value != 0.0 || z.Value != 0.0)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt((y * y) + (z * z));
            ay = y * inverse;
            az = inverse * z;
        }

        float angle = Igi2Pose.Acos(ay.F);
        return az.Value < 0.0 ? -angle : angle;
    }

    /// <summary>Levels its eyes within the aim turn, <c>HumanAI_LevelSight</c>.</summary>
    public void LevelSight()
    {
        double delta = LevelSightDelta();
        float limit = MaxAimDelta;
        _channels[1] = delta >= -limit ? (((Fpu)(delta > limit ? limit : delta)) * InverseOfPi).F : (((Fpu)(-limit)) * InverseOfPi).F;
    }

    /// <summary>Sets the turns a tick, <c>avChannel[8]</c> and <c>[9]</c> in the C's labels.</summary>
    /// <param name="aim">The aim turn.</param>
    /// <param name="rotation">The body turn.</param>
    public void SetTurnLimits(float aim, float rotation)
    {
        MaxAimDelta = aim;
        MaxRotationDelta = rotation;
    }

    /// <summary>The turns for facing: quicker once alerted (<c>HumanAI_FacePositionSelectSpeed</c>'s choice).</summary>
    public void SelectTurnSpeed()
    {
        if ((Flags & 4) != 0)
        {
            SetTurnLimits(0.41887903213500977f, PatrolTurn);
        }
        else
        {
            SetTurnLimits(PatrolTurn, 0.10471975803375244f);
        }
    }

    /// <summary>Walks rather than runs, <c>HumanAI_MakeAIWalk</c>: the walk channel set to toggle when it runs.</summary>
    public void MakeWalk() => SetChannel(0, Soldier.IsWalk ? 0f : 1f);

    /// <summary>Runs rather than walks, <c>HumanAI_MakeAIRun</c>.</summary>
    public void MakeRun() => SetChannel(0, Soldier.IsWalk ? 1f : 0f);

    /// <summary>
    /// Steers toward a point (<c>HumanAI_PointCharacterTowardsDestination</c>): a mind alerted and not activating, with a last target,
    /// strafes when its way runs across its target (left or right, or back while it sees one), forward otherwise; a new way is
    /// taken only once the last has held for a second, unless it turns from one axis to the other.
    /// </summary>
    /// <param name="destination">The point.</param>
    public void PointTowards(Vector3d destination)
    {
        Vector3d at = Soldier.Position;
        if ((Flags & 4) == 0 || (Flags & 8) != 0 || LastTargetPosition == default)
        {
            PointForward(destination);
            return;
        }

        double tx = LastTargetPosition.X - at.X, ty = LastTargetPosition.Y - at.Y;
        float wx = (float)(destination.X - at.X), wy = (float)(destination.Y - at.Y);
        double targetLength = Math.Sqrt((tx * tx) + (ty * ty));
        if (targetLength == 0.0)
        {
            PointForward(destination);
            return;
        }

        float facingX = (float)(1.0 / targetLength * tx);
        float facingY = (float)ty;
        facingY = (float)(facingY * (1.0 / targetLength));
        double zero = 1.0 / targetLength * 0.0;
        double wayLength = Math.Sqrt((wx * wx) + (wy * wy));
        if (wayLength == 0.0)
        {
            PointForward(destination);
            return;
        }

        float ux = (float)(wx * (1.0 / wayLength)), uy = (float)(wy * (1.0 / wayLength));
        double along = (zero * (1.0 / wayLength * 0.0)) + (uy * facingY) + (ux * facingX);
        int way = along > 0.699999988079071
            ? 1
            : along >= -0.699999988079071 ? ((ux * facingY) - (uy * facingX) <= 0.0 ? 2 : 3) : (GetTarget(visibleOnly: true) is not null ? 4 : 1);

        int direction = LastMovementDirection;
        switch (way)
        {
            case 1 or 4:
                if (direction is 1 or 4 || DirectionChangeTimer == 0)
                {
                    direction = way;
                }

                break;
            case 2 or 3:
                if (direction is 2 or 3 || DirectionChangeTimer == 0)
                {
                    direction = way;
                }

                break;
        }

        switch (direction)
        {
            case 2:
                PointLeft(destination);
                break;
            case 3:
                PointRight(destination);
                break;
            case 4:
                PointBackward(destination);
                break;
            default:
                PointForward(destination);
                break;
        }

        if (DirectionChangeTimer == 0)
        {
            DirectionChangeTimer = 30;
        }
    }

    /// <summary>
    /// Walks forward toward a point (<c>HumanAI_PointCharacterTowardsDestinationForward</c>): straight on unless it must turn more
    /// than ten degrees, when it slows to turn; the turn and tilt held to the limits.
    /// </summary>
    private void PointForward(Vector3d destination)
    {
        if (Soldier.Locomotion.CurrentState is 4 or 5 or 6 or 7)
        {
            _channels[4] = 1f;
        }

        if (IsDestinationReached(destination))
        {
            return;
        }

        (float across, float ahead) = LocalOffset(destination);
        double gamma = SignedTurn(across, ahead);
        float tilt = LevelSightDelta();
        if (Math.Abs(gamma) <= FacingAhead)
        {
            _channels[4] = 1f;
        }
        else
        {
            Soldier.ScaleAnimationTimeStep(0.2f);
        }

        Steer(gamma, tilt, 1);
    }

    /// <summary>Strafes left toward a point (<c>…Left</c>); square on and close beside its target, it rolls.</summary>
    private void PointLeft(Vector3d destination)
    {
        _channels[2] = 1f;
        if (IsDestinationReached(destination))
        {
            return;
        }

        (float across, float ahead) = LocalOffset(destination);
        double gamma = SignedTurn(ahead, -(double)across);
        float tilt = LevelSightDelta();
        if (Math.Abs(gamma) <= FacingAhead)
        {
            if (Math.Abs(gamma) < RollWindow && GetTarget(visibleOnly: true) is not null && across > -12288f && across < -10240f && RollDisallowedTimer == 0)
            {
                RollDisallowedTimer = 300;
                _channels[36] = 1f;
            }
        }
        else
        {
            _channels[2] = 0f;
        }

        Steer(gamma, tilt, 2);
    }

    /// <summary>Strafes right toward a point (<c>…Right</c>); square on and close beside its target, it rolls.</summary>
    private void PointRight(Vector3d destination)
    {
        _channels[3] = 1f;
        if (IsDestinationReached(destination))
        {
            return;
        }

        (float across, float ahead) = LocalOffset(destination);
        double gamma = SignedTurn(-(double)ahead, across);
        float tilt = LevelSightDelta();
        if (Math.Abs(gamma) <= FacingAhead)
        {
            if (Math.Abs(gamma) < RollWindow && GetTarget(visibleOnly: true) is not null && across > 10240f && across < 12288f && RollDisallowedTimer == 0)
            {
                RollDisallowedTimer = 300;
                _channels[37] = 1f;
            }
        }
        else
        {
            _channels[3] = 0f;
        }

        Steer(gamma, tilt, 3);
    }

    /// <summary>Backs toward a point (<c>…Backward</c>).</summary>
    private void PointBackward(Vector3d destination)
    {
        _channels[5] = 1f;
        if (IsDestinationReached(destination))
        {
            return;
        }

        (float across, float ahead) = LocalOffset(destination);
        double gamma = SignedTurn(-(double)across, -(double)ahead);
        float tilt = LevelSightDelta();
        if (Math.Abs(gamma) > FacingAhead)
        {
            _channels[5] = 0f;
        }

        Steer(gamma, tilt, 4);
    }

    /// <summary>A point in the body's frame: across (X) and ahead (Y).</summary>
    private (float Across, float Ahead) LocalOffset(Vector3d destination)
    {
        Vector3d at = Soldier.Position;
        float[] o = Soldier.OrientationRows;
        Fpu dx = (Fpu)destination.X - at.X, dy = (Fpu)destination.Y - at.Y, dz = (Fpu)destination.Z - at.Z;
        float across = (((dz * o[6]) + (dy * o[3])) + (dx * o[0])).F;
        float ahead = (((dz * o[7]) + (dy * o[4])) + (dx * o[1])).F;
        return (across, ahead);
    }

    /// <summary>The turn to a direction given as a numerator over a denominator: the arc tangent of their ratio, swung half round when the denominator is negative.</summary>
    private static double SignedTurn(double numerator, double denominator)
    {
        if (denominator == 0.0)
        {
            return 0.0;
        }

        float gamma = (float)Math.Atan2(numerator / denominator, 1.0);
        if (denominator < 0.0)
        {
            gamma = (float)(numerator <= 0.0 ? gamma - 3.1415927410125732 : gamma + 3.1415927410125732);
        }

        return gamma;
    }

    /// <summary>Writes the turn and tilt, held to the limits, and records the way it went.</summary>
    private void Steer(double gamma, double tilt, int direction)
    {
        float aim = MaxAimDelta, rotation = MaxRotationDelta;
        double heldTilt = tilt >= -aim ? (tilt > aim ? aim : tilt) : -aim;
        float heldGamma = (float)(gamma >= -rotation ? (gamma > rotation ? rotation : gamma) : -rotation);
        LastMovementDirection = direction;
        _channels[0] = ((Fpu)heldGamma * InverseOfPi).F;
        _channels[1] = (((Fpu)heldTilt) * InverseOfPi).F;
    }

    /// <summary>
    /// Turns its eyes to a point (<c>HumanAI_FacePosition</c>): from where it sees (its seat on a mounted gun), the point taken into
    /// the view's frame (level with the eyes when asked), the turn and tilt held to the limits. It has faced the point once the turn
    /// left is within the tolerance; until then it turns on the spot.
    /// </summary>
    /// <param name="target">The point.</param>
    /// <param name="tolerance">The turn that counts as facing.</param>
    /// <param name="level">Whether to look level.</param>
    /// <returns><see langword="true"/> once facing it.</returns>
    public bool FacePosition(Vector3d target, float tolerance, bool level)
    {
        Vector3d from = IsTop(Igi2AiContent.CombatStationaryGunInit) && Soldier.SeatView is { } seat ? seat.Position : View.ViewPosition;
        if (level)
        {
            target = new Vector3d(target.X, target.Y, from.Z);
        }

        float[] v = View.ViewOrientation;
        double dx = target.X - from.X, dy = target.Y - from.Y, dz = target.Z - from.Z;
        double lx = (v[3] * dy) + (v[0] * dx) + (v[6] * dz);
        double ly = (v[4] * dy) + (v[7] * dz) + (v[1] * dx);
        double lz = (v[5] * dy) + (v[8] * dz) + (v[2] * dx);
        float gamma = -Igi2Pose.CalcGamma(lx, ly);
        float turn = gamma;
        double alpha;
        if (Math.Abs((double)gamma) <= 1.5707963705062866)
        {
            alpha = Igi2Pose.CalcAlpha(ly, lz);
        }
        else
        {
            double fx = ((v[0] + v[2]) * 0.0) + v[1];
            double fy = ((v[5] + v[3]) * 0.0) + v[4];
            double fz = ((v[8] + v[6]) * 0.0) + v[7];
            alpha = -Igi2Pose.CalcAlpha(fy, fz);
            _ = fx;
            if (Math.Abs(alpha) > 1.5707963705062866)
            {
                alpha = alpha >= 0.0 ? 3.1415927410125732 - alpha : -3.1415927410125732 - alpha;
            }
        }

        float aim = MaxAimDelta, rotation = MaxRotationDelta;
        alpha = alpha >= -aim ? (alpha > aim ? aim : alpha) : -aim;
        turn = turn >= -rotation ? (turn > rotation ? rotation : turn) : -rotation;
        _channels[0] = ((Fpu)turn * InverseOfPi).F;
        _channels[1] = (((Fpu)alpha) * InverseOfPi).F;
        if (Math.Abs(gamma) <= tolerance)
        {
            return true;
        }

        SetChannel(9, 1f);
        return false;
    }

    /// <summary>Turns to a point at the facing turns, <c>HumanAI_FacePositionSelectSpeed</c>.</summary>
    /// <param name="target">The point.</param>
    /// <param name="tolerance">The turn that counts as facing.</param>
    /// <param name="level">Whether to look level.</param>
    /// <returns><see langword="true"/> once facing it.</returns>
    public bool FacePositionSelectSpeed(Vector3d target, float tolerance, bool level)
    {
        SelectTurnSpeed();
        return FacePosition(target, tolerance, level);
    }

    /// <summary>Faces its last target, if it has one, <c>HumanAI_FaceTarget</c>.</summary>
    public void FaceTarget()
    {
        if (LastTargetPosition != default)
        {
            SelectTurnSpeed();
            FacePosition(LastTargetPosition, 0.0872664675116539f, level: true);
        }
    }

    /// <summary>Moves to a node at the patrol turns, <c>HumanAI_PatrolMoveCharacterToNode</c>.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> once there.</returns>
    public bool PatrolMoveToNode(int node)
    {
        SetTurnLimits(PatrolTurn, PatrolTurn);
        return MoveToNode(node);
    }

    /// <summary>Moves to a node at the attack turns, <c>HumanAI_AttackMoveCharacterToNode</c>.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> once there.</returns>
    public bool AttackMoveToNode(int node)
    {
        SetTurnLimits(AttackTurn, AttackTurn);
        return MoveToNode(node);
    }

    /// <summary>
    /// Moves to a node (<c>HumanAI_MoveCharacterToNode</c>): at it already it has arrived; on the way there it keeps walking its path;
    /// otherwise a new path is searched from where it stands (between its legal nodes, from the nearer) and its first step taken. A
    /// node no path reaches kills the soldier, as the original does.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> once there.</returns>
    public bool MoveToNode(int node)
    {
        if (Graph is not { } graph)
        {
            return true;
        }

        if (node == CurrentNode)
        {
            if (graph.NodeAt(node) is { } here && IsDestinationReached(here.Position))
            {
                return Arrive();
            }

            if (Squad?.SquadType == 2)
            {
                return true;
            }
        }

        if (PathSearch.GraphId == graph.Id && PathSearch.SourceNodeId == CurrentStartNode && PathSearch.DestNodeId == node && PathSearch.CurrentPath.Count > 0)
        {
            if (MoveAlong() == 2)
            {
                return Arrive();
            }

            return false;
        }

        int start = CurrentNode;
        if (LegalNodes.IsUsed)
        {
            Vector3d p1 = graph.NodeAt(LegalNodes.Node1)?.Position ?? default;
            Vector3d p2 = graph.NodeAt(LegalNodes.Node2)?.Position ?? default;
            Vector3d p0 = graph.NodeAt(node)?.Position ?? default;
            float d1x = (float)(p1.X - p0.X), d1y = (float)(p1.Y - p0.Y);
            float d2x = (float)(p2.X - p0.X), d2y = (float)(p2.Y - p0.Y), d2z = (float)(p2.Z - p0.Z);
            double toSecond = Math.Sqrt((d2x * (double)d2x) + (d2y * (double)d2y) + (d2z * (double)d2z));
            double toFirst = Math.Sqrt((d1x * (double)d1x) + (d1y * (double)d1y) + ((p1.Z - p0.Z) * (p1.Z - p0.Z)));
            start = toSecond <= toFirst ? LegalNodes.Node2 : LegalNodes.Node1;
        }

        // With no legal nodes, or the nearer of them the node sought, the search starts from its footing.
        if (!LegalNodes.IsUsed || start == node)
        {
            start = CurrentNode;
            if (node == CurrentNode)
            {
                return Arrive();
            }
        }

        PathSearch.Clear();
        if (!PathSearch.CreatePath(graph, start, node, uint.MaxValue))
        {
            Soldier.KillOutright();
            return false;
        }

        graph.UnlockCover(CurrentNode);
        CurrentStartNode = start;
        DestinationNode = node;
        BuildMovementQueue(graph);
        if (LegalNodes.IsUsed && _movementQueue.Count > 1
            && (_movementQueue[1].NodeId == LegalNodes.Node1 || _movementQueue[1].NodeId == LegalNodes.Node2))
        {
            Graph.AddWeight(_movementQueue[0].NodeId, -20480f);
            _movementQueue.RemoveAt(0);
        }

        if (_movementQueue.Count > 0)
        {
            PointTowards(_movementQueue[0].Position);
        }

        return false;
    }

    private bool Arrive()
    {
        PathSearch.Clear();
        DestinationNode = CurrentNode;
        ClearMovementQueue();
        return true;
    }

    /// <summary>
    /// Turns the search's path into the way ahead (<c>HumanAI_CreateQueueItemListFromAStarSearchPath</c>): a step a node, each
    /// weighing on its node; where two door nodes meet and the leader of a hunting squad (types 1, 3, 5) walks into a room, a
    /// flashbang throw at the doorway comes first and then the door, and, past a door node that is not, the steps beyond it.
    /// </summary>
    private void BuildMovementQueue(Igi2AiGraph graph)
    {
        ClearMovementQueue();
        ReleaseDoor();
        List<int> path = [.. PathSearch.CurrentPath.Select(static n => n.NodeId)];
        if (path.Count == 0)
        {
            return;
        }

        Igi2AiGraphNode? previous = graph.NodeAt(path[0]);
        Igi2AiMovementItem last = AddStep(graph, path[0], previous);
        for (int k = 1; k < path.Count; k++)
        {
            Igi2AiGraphNode? next = graph.NodeAt(path[k]);
            if (previous is not null && next is not null && (previous.Criteria & next.Criteria & Igi2AiGraph.DoorCriteria) != 0)
            {
                last.Action = 1;
                if (AiType is 1 or 3 or 5 && Squad is { State: 3 or 7 or 8 } squad && squad.Teams[0].Leader == this && next.Sector is not null)
                {
                    Vector3d from = last.Position;
                    Igi2AiMovementItem flash = new() { NodeId = path[k - 1], Action = 4, Position = next.Position };
                    graph.AddWeight(flash.NodeId, 20480f);
                    _movementQueue.Add(flash);
                    float fx = (float)(flash.Position.X - from.X);
                    double fy = flash.Position.Y - from.Y, fz = flash.Position.Z - from.Z;
                    double length = Math.Sqrt((fx * (double)fx) + (fy * fy) + (fz * fz));
                    if (length != 0.0)
                    {
                        float sy = (float)(6144.0 / length * fy);
                        float sz = (float)(6144.0 / length * (float)fz);
                        flash.Position = new Vector3d(
                            (fx * (6144.0 / length)) + flash.Position.X, sy + flash.Position.Y, sz + flash.Position.Z + 2048.0);
                    }

                    Igi2AiMovementItem door = new() { Action = 1, NodeId = path[k - 1], Position = next.Position };
                    graph.AddWeight(door.NodeId, 20480f);
                    _movementQueue.Add(door);
                }
            }

            last = AddStep(graph, path[k], next);
            previous = next;
        }
    }

    private Igi2AiMovementItem AddStep(Igi2AiGraph graph, int node, Igi2AiGraphNode? at)
    {
        Igi2AiMovementItem step = new() { NodeId = node, Position = at?.Position ?? default };
        graph.AddWeight(node, 20480f);
        _movementQueue.Add(step);
        return step;
    }

    /// <summary>
    /// A tick along the way ahead (<c>HumanAI_MoveCharacter</c>): two with nothing left, one while walking or waiting, nought on
    /// reaching a step, which is then dropped and its node becomes its footing.
    /// </summary>
    private int MoveAlong()
    {
        if (_movementQueue.Count == 0)
        {
            return 2;
        }

        if (!Soldier.IsBlendFinished)
        {
            return 1;
        }

        Igi2AiMovementItem head = _movementQueue[0];
        switch (head.Action)
        {
            case 1:
                if (!IsDestinationReached(head.Position))
                {
                    PointTowards(head.Position);
                    return 1;
                }

                if (OpenDoorOnWay(head) is { } waiting)
                {
                    return waiting;
                }

                break;
            case 2 or 3:
                if (head.IsStarted)
                {
                    if (Soldier.Locomotion.CurrentState != Igi2SoldierState.StandAction)
                    {
                        Vector3d next = _movementQueue.Count > 1 ? _movementQueue[1].Position : default;
                        Soldier.PopPosition(next);
                        head.Position = next;
                        break;
                    }

                    _channels[26] = 1f;
                    return 1;
                }

                _channels[26] = 1f;
                if (Soldier.Locomotion.CurrentState == Igi2SoldierState.StandAction)
                {
                    head.IsStarted = true;
                }

                return 1;
            case 4:
                if (Soldier.Weapon.Inventory?.FindWeapon(Igi2HumanSoldier.FlashbangWeaponId) is not { } flash)
                {
                    break;
                }

                if (!FacePosition(head.Position, 0.0872664675116539f, level: true))
                {
                    return 1;
                }

                _channels[32] = 1f;
                GrenadeWeaponTypeId = flash.WeaponType.Id;
                LastTargetPosition = head.Position;
                if (Soldier.Locomotion.CurrentState == Igi2SoldierState.StandThrowGrenade)
                {
                    head.Action = 5;
                }

                return 1;
            case 5:
                LastTargetPosition = head.Position;
                if (Soldier.Locomotion.CurrentState != Igi2SoldierState.StandThrowGrenade)
                {
                    head.Position = Soldier.Position;
                    if (Soldier.Weapon.Inventory?.FindWeapon("WEAPON_ID_GRENADE") is { } grenade)
                    {
                        GrenadeWeaponTypeId = grenade.WeaponType.Id;
                    }

                    break;
                }

                if (World.DoorNearest(head.Position) is { } blown)
                {
                    blown.HoldOpen(300);
                }

                if (Squad is { } rushing)
                {
                    rushing.RushTimer = 150;
                }

                return 1;
        }

        PointTowards(head.Position);
        if (!IsDestinationReached(head.Position))
        {
            return 1;
        }

        if (CurrentNode != head.NodeId)
        {
            PreviousNode = CurrentNode;
        }

        SetCurrentNode(head.NodeId);
        Graph?.AddWeight(head.NodeId, -20480f);
        _movementQueue.RemoveAt(0);
        LegalNodes = LegalNodes with { IsUsed = false };
        SetLegalNodes();
        ReleaseDoor();
        return 0;
    }

    /// <summary>
    /// At a door on the way (the door step of <c>HumanAI_MoveCharacter</c>): the nearest door is found once; one jammed against
    /// something while the player stands close makes the soldier think it heard him; a closed door is opened (and an open one held
    /// a while); it waits while the door moves.
    /// </summary>
    /// <returns>One while waiting, or <see langword="null"/> to walk on.</returns>
    private int? OpenDoorOnWay(Igi2AiMovementItem head)
    {
        if (!IsDoorTriggered)
        {
            Door = World.DoorNearest(head.Position);
            IsDoorTriggered = true;
        }

        if (Door is not Igi2DoorTask door)
        {
            return null;
        }

        int status = door.Status;
        if (door.StopOnCollision && door.HoldTimer != 0 && status != 0)
        {
            if (_movementQueue.Count > 1 && World.Player is { } player
                && Igi2AiMath.Distance(player.Position, Soldier.Position) < 12288f)
            {
                Igi2AiEvent heard = new(39, player.Team, 0, 614400f, _movementQueue[1].Position, default, player, null);
                Receive(heard);
            }
        }
        else if (Detection.Enemies.FirstOrDefault() is { IsCreatedByGunShot: true })
        {
            Detection.Clear();
        }

        switch (status)
        {
            case 0:
                door.HoldOpen(300);
                return null;
            case 1 or 3:
                door.ToggleOpenBySoldier();
                FacePositionSelectSpeed(head.Position, (float)RollWindow, level: true);
                return 1;
            case 2:
                FacePositionSelectSpeed(head.Position, (float)RollWindow, level: true);
                return 1;
            default:
                return null;
        }
    }

    /// <summary>Takes the two nodes it stands between, its footing and the next step's (<c>HumanAI_SetLegalNodes</c>).</summary>
    public void SetLegalNodes()
    {
        if (LegalNodes.IsUsed)
        {
            return;
        }

        if (_movementQueue.Count > 0 && _movementQueue[0].NodeId != CurrentNode)
        {
            LegalNodes = (CurrentNode, _movementQueue[0].NodeId, true);
        }
        else
        {
            LegalNodes = (CurrentNode, LegalNodes.Node2, false);
        }
    }

    /// <summary>Takes the node nearest a point as its footing and its destination (<c>HumanAI_UpdateNodeIdFromPostition</c>).</summary>
    /// <param name="position">The point.</param>
    public void UpdateNodeFromPosition(Vector3d position)
    {
        if (Graph?.Nearest(position, true) is { } node)
        {
            SetCurrentNode(node.Id);
            CurrentStartNode = node.Id;
            DestinationNode = node.Id;
        }
    }

    /// <summary>Stops where it is, <c>HumanAI_ClearMovementData</c>.</summary>
    public void ClearMovementData()
    {
        PathSearch.Clear();
        DestinationNode = CurrentNode;
        ClearMovementQueue();
    }

    /// <summary>
    /// The way a soldier may roll (<c>HumanAI_ValidateRollDirection</c>): bit one left, bit two right, each when three metres that
    /// way is still on its node.
    /// </summary>
    /// <returns>The ways, or nought off the graph.</returns>
    public int ValidateRollDirection()
    {
        if (Graph?.NodeAt(CurrentNode) is not { } node)
        {
            return 0;
        }

        float[] o = Soldier.OrientationRows;
        Vector3d at = Soldier.Position;
        double sx = ((o[1] + o[2]) * 0.0) - (o[0] * 12288.0);
        double sy = ((o[4] + o[5]) * 0.0) - (o[3] * 12288.0);
        double sz = ((o[7] + o[8]) * 0.0) - (o[6] * 12288.0);
        Vector3d left = new(sx + at.X, sy + at.Y, sz + at.Z);
        Vector3d right = new(at.X - sx, at.Y - sy, sz + at.Z);
        int ways = 3;
        if (!Igi2AiGraph.IsPositionOnNode(left, node, 0.0, checkHeight: false))
        {
            ways = 2;
        }

        if (!Igi2AiGraph.IsPositionOnNode(right, node, 0.0, checkHeight: false))
        {
            ways &= ~2;
        }

        return ways;
    }
}
