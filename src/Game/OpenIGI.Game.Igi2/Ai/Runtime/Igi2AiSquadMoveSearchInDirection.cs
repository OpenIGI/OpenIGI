using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The directed search (<c>AISquad_SquadActionMoveSearchInDirection*</c>): the leader goes to the node nearest the place, then on
/// thirty metres in the direction given; there the squad is told <c>AIEVENT_SEARCH_DIRECTION_DONE</c> (43).
/// </summary>
public sealed class Igi2AiSquadMoveSearchInDirection : Igi2AiSquadAction
{
    private Vector3f _direction;
    private Vector3d _start;
    private int _node;
    private int _stage;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.MoveSearchInDirection;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_direction);
        words.Add(_start);
        words.Add(_node);
        words.Add(_stage);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _direction = words.Single3();
        _start = words.Double3();
        _node = words.Int();
        _stage = words.Int();
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        squad.MakeOneTeam();
        if (squad.FirstMember is not { } leader || change.Direction == default)
        {
            squad.PopToIdle();
            return false;
        }

        Igi2AiGraphNode? node = null;
        leader.Graph?.ClosestNode(change.Position, allowVehicleNodes: false, out node);
        _node = node?.Id ?? leader.CurrentNode;
        _direction = change.Direction;
        _start = node?.Position ?? default;
        _stage = 0;
        squad.ClearMembersPaths();
        squad.ClearMembersActionStacks();
        leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Node = _node, IsRun = RunsOn(squad) });
        squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Igi2HumanAi leader = squad.FirstMember!;
        if (squad.FormationTimer == 0)
        {
            squad.SetFormation(0, Igi2AiFormations.V);
        }

        if (_node != leader.DestinationNode)
        {
            leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Node = _node, IsRun = RunsOn(squad) });
        }

        if (_stage != 0)
        {
            if (_stage == 1 && _node == leader.CurrentNode)
            {
                squad.ReceiveEvent(new Igi2AiSquadEvent { Kind = 43, Position = _start, Direction = _direction });
            }

            return true;
        }

        if (Igi2AiMath.Distance(_start, leader.Soldier.Position) >= 8192f)
        {
            return true;
        }

        Vector3d ahead = Ahead(_start, _direction);
        if (leader.Graph is { } graph && graph.Nearest(ahead, false) is { } node)
        {
            _node = node.Id;
            int delay = squad.OrderDelay();
            leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Delay = delay, Node = _node, IsRun = RunsOn(squad) });
        }

        _stage = 1;
        return true;
    }

    /// <summary>Thirty metres from a point along a direction, nought when the direction is.</summary>
    internal static Vector3d Ahead(Vector3d from, Vector3f direction)
    {
        Fpu length = Fpu.Sqrt((((Fpu)direction.X * direction.X) + ((Fpu)direction.Y * direction.Y)) + ((Fpu)direction.Z * direction.Z));
        double x = 0.0, y = 0.0, z = 0.0;
        if (length.Value > 0.0)
        {
            float scale = ((Fpu)122880.0 / length).F;
            x = ((Fpu)scale * direction.X).Value;
            y = ((Fpu)scale * direction.Y).Value;
            z = ((Fpu)scale * direction.Z).F;
        }

        return new Vector3d(x + from.X, y + from.Y, z + from.Z);
    }
}
