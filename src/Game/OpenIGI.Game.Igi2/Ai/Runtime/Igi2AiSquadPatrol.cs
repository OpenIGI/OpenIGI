namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The patrol (<c>AISquad_SquadActionPatrol*</c>): the leader is ordered to walk the patrol and the rest follow in a column; it is
/// over when the leader stops patrolling for anything less urgent than a sighting.
/// </summary>
public sealed class Igi2AiSquadPatrol : Igi2AiSquadAction
{
    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Patrol;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(PatrolPathId);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        PatrolPathId = words.Int();
    }

    /// <summary>Gets the patrol walked, the script's <c>AISquad_GetCurrentPatrolID</c>.</summary>
    public int PatrolPathId { get; private set; }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        squad.MakeOneTeam();
        if (squad.FirstMember is not { } leader)
        {
            squad.PopToIdle();
            return false;
        }

        PatrolPathId = change.PatrolPathId;
        int delay = squad.OrderDelay();
        leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.Patrol, Delay = delay, Node = change.PatrolPathId });
        squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (squad.FirstMember is not { } leader)
        {
            squad.PopToIdle();
            return true;
        }

        bool patrolling = leader.IsTop(Igi2AiContent.PatrolInit);
        bool over = !patrolling && (leader.Actions.Top?.Priority ?? -1) != leader.Events.RankOf(26);
        if (squad.FormationTimer == 0 && patrolling)
        {
            squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        }

        if (over)
        {
            squad.PopToIdle();
        }

        return true;
    }
}
