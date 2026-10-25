using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The look-then-search (<c>AISquad_SquadActionLookSearchInDirection*</c>): the leader looks the way given for three seconds, then
/// goes thirty metres that way; once there it goes idle.
/// </summary>
public sealed class Igi2AiSquadLookSearchInDirection : Igi2AiSquadAction
{
    private Vector3f _direction;
    private int _stage;
    private int _timer;
    private int _node;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.LookSearchInDirection;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_direction);
        words.Add(_stage);
        words.Add(_timer);
        words.Add(_node);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _direction = words.Single3();
        _stage = words.Int();
        _timer = words.Int();
        _node = words.Int();
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

        _direction = change.Direction;
        _stage = 0;
        _timer = 0;
        _node = -1;
        squad.ClearMembersPaths();
        int delay = squad.OrderDelay();
        leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.LookAtPosition, Delay = delay, Position = change.Position });
        squad.SetFormation(0, Igi2AiFormations.V);
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

        if (_stage != 0)
        {
            if (_stage != 1)
            {
                return true;
            }

            if (_node != leader.DestinationNode)
            {
                int delay = squad.OrderDelay();
                leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Delay = delay, Node = _node });
                return true;
            }

            if (_node == leader.CurrentNode)
            {
                squad.PopToIdle();
            }

            return true;
        }

        _timer++;
        if (_timer <= 90)
        {
            return true;
        }

        Vector3d ahead = Igi2AiSquadMoveSearchInDirection.Ahead(leader.Soldier.Position, _direction);
        if (leader.Graph is { } graph && graph.Nearest(ahead, false) is { } node)
        {
            _node = node.Id;
            int delay = squad.OrderDelay();
            leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Delay = delay, Node = _node });
        }

        _stage = 1;
        return true;
    }
}
