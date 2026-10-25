using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>Graph routing: the movement queue the planner builds and the walker steps, door hops, and the combat free-position search.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>
    /// One item of the movement queue — <c>0x452080</c>'s record: the point walked to (<c>+8</c>), whether a
    /// door stands on the hop (<c>+32</c>), and the node that becomes the current one when it is reached
    /// (<c>+36</c>).
    /// </summary>
    private readonly record struct AiWaypoint(Vector3d Point, int Node, bool Door);

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
            _route.Add(new AiWaypoint(Igi1AiGeometry.EdgePoint(entered, toward, exit: false), start, Door: false));
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
            bool door = Igi1AiGeometry.IsDoorNode(from) && Igi1AiGeometry.IsDoorNode(to);
            Vector3d hop = to.Position - from.Position;
            if (next == node)
            {
                bool atPoint = false;
                if (point is { } exact)
                {
                    _route.Add(new AiWaypoint(Igi1AiGeometry.EdgePoint(to, hop, exit: false), node, door));
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
                _route.Add(new AiWaypoint(Igi1AiGeometry.EdgePoint(from, hop, exit: true), at, door));
            }

            _route.Add(new AiWaypoint(Igi1AiGeometry.EdgePoint(to, hop, exit: false), next, Door: false));
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

    private int NearestNode(Vector3d from) =>
        _graph is null
        || GraphNearestNode.Find(_graph, from, out GraphNode? best, out _) == GraphNearestNodeResult.None
            ? -1
            : best!.Id;
}
