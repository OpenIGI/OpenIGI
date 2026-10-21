using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>The squad's orders as a member carries them out: formation, inquiry, the move to a node and the leapfrog wait.</content>
public static partial class Igi2AiContent
{
    // --- Formation, inquiry and the squad's moves -------------------------------------------------------------------------------

    /// <summary>Starts with no node chosen, walking or running as the soldier was, <c>HumanAIContent_MoveInFormationSquadOrderInitCB</c>.</summary>
    public static void MoveInFormationInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        FormationContext context = item.ContextAs<FormationContext>();
        context.Node = -1;
        context.Countdown = 0;
        ai.MakeWalk();
    }

    /// <summary>
    /// Keeps its place in formation (<c>HumanAIContent_MoveInFormationSquadOrderRunCB</c>): done with no leader, a dead one, or the
    /// node of its place being the leader's own or where he goes; the place's node is chosen near the leader each second; it walks
    /// with a walking leader and runs otherwise, faster when more than two metres off; it faces the target when its path fails.
    /// </summary>
    public static void MoveInFormationRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        FormationContext context = item.ContextAs<FormationContext>();
        if (context.Leader is not { } leader)
        {
            Done(ai, item);
            return;
        }

        Igi2HumanSoldier body = leader.Soldier;
        if (body.IsDead)
        {
            Done(ai, item);
            return;
        }

        float[] o = body.OrientationRows;
        Vector3f r = context.RelativePosition;
        Vector3d at = body.Position;
        double py = (o[3] * r.X) + (o[5] * r.Z) + (o[4] * r.Y);
        float pz = (float)((o[6] * r.X) + (o[8] * r.Z) + (o[7] * r.Y));
        Vector3d place = new((o[2] * r.Z) + (o[1] * r.Y) + (o[0] * r.X) + at.X, py + at.Y, pz + at.Z);
        if (leader.Graph is not { } graph)
        {
            return;
        }

        Igi2AiGraphNode? node;
        if (context.Countdown <= 0)
        {
            node = graph.NodeAt(leader.CurrentNode) is { } start ? graph.ClosestNodeFromStartingNode(start, place, 20) : null;
            context.Node = node?.Id ?? -1;
            context.Countdown = 30;
        }
        else
        {
            node = graph.NodeAt(context.Node);
            context.Countdown--;
        }

        if (node is null || node.Id == leader.CurrentNode || node.Id == leader.DestinationNode)
        {
            ai.DestinationNode = ai.CurrentNode;
            Done(ai, item);
            return;
        }

        Vector3d me = ai.Soldier.Position;
        if (body.IsWalk)
        {
            ai.MakeWalk();
        }
        else
        {
            ai.MakeRun();
        }

        if (Igi2AiMath.Distance(node.Position, me) > 8192f)
        {
            ai.Soldier.ScaleAnimationTimeStep(1.2999999523162842f);
        }

        if (ai.AttackMoveToNode(node.Id))
        {
            Done(ai, item);
            return;
        }

        if ((ai.PathSearch.Flags & 1) != 0)
        {
            ai.FaceTarget();
        }

        if (item.ChildRun is { } child && child(ai, item))
        {
            item.ChildRun = null;
        }
    }

    /// <summary>Lets go of the leader, <c>HumanAIContent_MoveInFormationSquadOrderDeleteCB</c>.</summary>
    public static void MoveInFormationDelete(Igi2HumanAi ai, Igi2AiActionItem item) => item.ContextAs<FormationContext>().Leader = null;

    /// <summary>
    /// Rescales a soldier's place in formation to a new distance and picks its node anew, <c>HumanAI_RefreshFormationPosition</c>.
    /// </summary>
    /// <param name="ai">The soldier's mind.</param>
    /// <param name="distance">The formation distance, in world units.</param>
    public static void RefreshFormationPosition(Igi2HumanAi ai, float distance)
    {
        ArgumentNullException.ThrowIfNull(ai);
        if (ai.Actions.Top is not { } top || top.Init != (Igi2AiActionCallback)MoveInFormationInit
            || top.Context is not FormationContext { Leader: { } leader } context)
        {
            return;
        }

        Vector3f r = context.RelativePosition;
        double length = Math.Sqrt((r.X * (double)r.X) + (r.Y * (double)r.Y) + (r.Z * (double)r.Z));
        if (length == 0.0)
        {
            return;
        }

        double scale = distance / length;
        r = new Vector3f((float)(r.X * scale), (float)(r.Y * scale), (float)(scale * r.Z));
        context.RelativePosition = r;
        float[] o = leader.Soldier.OrientationRows;
        Vector3d at = leader.Soldier.Position;
        float pz = (float)((o[8] * r.Z) + (o[7] * r.Y) + (o[6] * r.X));
        double py = (o[5] * r.Z) + (o[4] * r.Y) + (o[3] * r.X);
        Vector3d place = new((o[2] * r.Z) + (o[1] * r.Y) + (o[0] * r.X) + at.X, py + at.Y, pz + at.Z);
        if (leader.Graph is { } graph && graph.NodeAt(leader.CurrentNode) is { } start && graph.ClosestNodeFromStartingNode(start, place, 20) is { } node)
        {
            context.Node = node.Id;
        }
    }

    /// <summary>Takes the node nearest the place, <c>HumanAIContent_InquireSquadOrderInitCB</c>.</summary>
    public static void InquireSquadOrderInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        Igi2AiGraphNode? node = null;
        ai.Graph?.ClosestNode(context.Position, allowVehicleNodes: false, out node);
        context.Node = node?.Id ?? -1;
    }

    /// <summary>Runs or walks there, and within five metres turns to look at it, <c>…RunCB</c>.</summary>
    public static void InquireSquadOrderRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        Vector3d at = ai.Soldier.Position;
        double dx = context.Position.X - at.X, dy = context.Position.Y - at.Y, dz = context.Position.Z - at.Z;
        if (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) <= 20480.0)
        {
            ai.SelectTurnSpeed();
            ai.FacePosition(context.Position, TurnTolerance, level: true);
        }
        else
        {
            ai.MakeRun();
            ai.AttackMoveToNode(context.Node);
        }
    }

    /// <summary>Nothing, <c>HumanAIContent_MoveToNodeSquadOrderInitCB</c>.</summary>
    public static void MoveToNodeSquadOrderInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
    }

    /// <summary>
    /// Runs (at the attack turns, facing the target on arrival) or walks (at the patrol turns) to the node, done on arrival
    /// (<c>HumanAIContent_MoveToNodeSquadOrderRunCB</c>).
    /// </summary>
    public static void MoveToNodeSquadOrderRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        if (item.ChildRun is { } child && child(ai, item))
        {
            item.ChildRun = null;
        }

        if (context.IsRun)
        {
            ai.MakeRun();
            if (ai.AttackMoveToNode(context.Node))
            {
                ai.FaceTarget();
                Done(ai, item);
            }
        }
        else
        {
            ai.MakeWalk();
            if (ai.PatrolMoveToNode(context.Node))
            {
                Done(ai, item);
            }
        }
    }

    /// <summary>Holds where it stands, <c>HumanAIContent_WaitForLeapFrogSquadOrderInitCB</c>.</summary>
    public static void WaitForLeapfrogInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.ContextAs<NodeContext>().Timer = -1;
        ai.DestinationNode = ai.CurrentNode;
    }

    /// <summary>Runs its burst, or faces the target, <c>…RunCB</c>.</summary>
    public static void WaitForLeapfrogRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (item.ChildRun is { } child)
        {
            if (child(ai, item))
            {
                item.ChildRun = null;
            }
        }
        else
        {
            item.ContextAs<NodeContext>().Timer = -1;
            ai.FaceTarget();
        }
    }
}
