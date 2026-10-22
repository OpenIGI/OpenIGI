namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The move (<c>AISquad_SquadActionMove*</c>): the leader runs to the node nearest the place, the rest in a staggered column; it is
/// over when the leader stands there.
/// </summary>
public sealed class Igi2AiSquadMove : Igi2AiSquadAction
{
    private int _node;
    private Igi2HumanAi? _ordered;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Move;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(squad);
        words.Add(_node);
        int orderedIndex = -1, index = 0;
        if (_ordered is not null)
        {
            foreach (Igi2HumanAi member in squad.Members)
            {
                if (ReferenceEquals(member, _ordered)) { orderedIndex = index; break; }
                index++;
            }
        }
        words.Add(orderedIndex);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(squad);
        _node = words.Int();
        int ordered = words.Int();
        _ordered = ordered >= 0 ? squad.Members.ElementAtOrDefault(ordered) : null;
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        squad.MakeOneTeam();
        Igi2HumanAi leader = squad.FirstMember!;
        _node = leader.Graph is { } graph && graph.Nearest(change.Position, false) is { } node
            ? node.Id
            : leader.CurrentNode;
        if (leader.CurrentNode != _node)
        {
            _ordered = leader;
            int delay = squad.OrderDelay();
            leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Delay = delay, Node = _node, IsRun = true });
            squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Igi2HumanAi leader = squad.FirstMember!;
        if (leader.CurrentNode == _node)
        {
            squad.PopToIdle();
            return true;
        }

        if (squad.FormationTimer == 0)
        {
            squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        }

        if (ReferenceEquals(leader, _ordered))
        {
            if (leader.IsTop(Igi2AiContent.MoveToNodeSquadOrderInit))
            {
                return true;
            }

            if ((leader.Actions.Top?.Priority ?? -1) == leader.Events.RankOf(26))
            {
                return true;
            }
        }

        _ordered = leader;
        int delay = squad.OrderDelay();
        leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Delay = delay, Node = _node, IsRun = true });
        return true;
    }
}
