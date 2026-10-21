using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>Diagnostics for the debug overlay and the scenario traces, for diffing the port against the retail probe log.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>Gets which of the four ticks this soldier's detection sweep runs on -- its record's <c>+12</c>.</summary>
    public int SweepPhase => _schedule.Phase;

    /// <summary>Diagnostics: <c>+15852/+15856/+15848</c> -- the tracked target node, the re-search flag, the Follow node.</summary>
    public string FollowState => FormattableString.Invariant(
        $"{_followTargetNode}/{(_followResearch ? 1 : 0)}/{_patrol?.DestinationNodeId ?? -1}");

    /// <summary>Diagnostics: this soldier's detection slot for one human -- retail's slot +16/+20/+40/+41/+42.</summary>
    /// <param name="target">The human's task id.</param>
    /// <returns>The cooldowns and flags, or <c>-</c> when the human is not tracked.</returns>
    public string SlotState(int target) => _tracked.TryGetValue(target, out Igi1AiTrackedHuman? human)
        ? FormattableString.Invariant(
            $"cone={human.Slot.ConeCooldown} ray={human.Slot.RayCooldown} incone={(human.Slot.IsInCone ? 1 : 0)} visible={(human.Slot.IsVisible ? 1 : 0)} registered={(human.Slot.IsRegistered ? 1 : 0)}")
        : "-";

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

    /// <summary>Diagnostics: the patrol chooser's out-of-sight count (the stand block's <c>+24</c>), or -1.</summary>
    public int PatrolNotVisibleTicks => _patrol?.NotVisibleTicks ?? -1;

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
}
