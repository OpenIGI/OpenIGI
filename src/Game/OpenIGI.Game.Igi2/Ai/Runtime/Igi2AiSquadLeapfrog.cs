using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The leapfrog (<c>AISquad_SquadActionLeapFrog*</c>): one team moves up on the target in a V while the other, which sees it,
/// covers; a second apart the teams are redrawn and, when the covering team has the target, the moving team's leader runs on.
/// </summary>
public sealed class Igi2AiSquadLeapfrog : Igi2AiSquadAction
{
    private int _node;
    private int _movingTeam;
    private int _timer;
    private IAiDetectable? _target;
    private readonly Igi2AiSpreadOut _spread = new();

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Leapfrog;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(squad);
        words.Add(_node);
        words.Add(_movingTeam);
        words.Add(_timer);
        words.Add(_target is not null && ReferenceEquals(_target, squad.World.Player) ? 1 : 0);
        _spread.SaveState(words);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(squad);
        _node = words.Int();
        _movingTeam = words.Int();
        _timer = words.Int();
        _target = words.Int() == 1 ? squad.World.Player as IAiDetectable : null;
        _spread.LoadState(words);
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        squad.MakeOneTeam();
        foreach (Igi2HumanAi member in squad.Teams[0].Members)
        {
            member.DestinationNode = member.CurrentNode;
        }

        _node = -1;
        _movingTeam = 1;
        _target = change.Target ?? (squad.Targets.Count > 0 ? squad.Targets[0].Enemy : null);
        _spread.UpdateTimer = 0;
        _timer = 0;
        _spread.Center = Igi2AiSpreadOut.FindCenter(squad);
        _spread.Turn = 0;
        squad.Teams[_movingTeam].FiringMode = 1;
        squad.Teams[1 - _movingTeam].FiringMode = 1;
        UpdateTeams(squad);
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (_target is null)
        {
            squad.PopToIdle();
            return true;
        }

        Advance(squad);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnDeInit() => _target = null;

    /// <summary>
    /// Sorts the members into the moving and covering teams (<c>AISquad_UpdateLeapfrogTeams</c>): one that sees the target and is
    /// near it, or aims a sniper rifle at it, or lies flat, or has just joined (and is not reloading), or stands on its cover node,
    /// covers; the rest move. The moving team's leader's footing is the node it moves from.
    /// </summary>
    private void UpdateTeams(Igi2AiSquad squad)
    {
        squad.MakeOneTeam();
        int covering = 1 - _movingTeam;
        float distance = squad.DistanceToTarget(covering);
        List<Igi2HumanAi> all = [.. squad.Teams[0].Members];
        squad.Teams[0].Members.Clear();
        squad.Teams[1].Members.Clear();
        foreach (Igi2HumanAi member in all)
        {
            bool covers = (member.GetTarget(visibleOnly: true) is not null
                    && (distance < 122880f || member.HasSniperScope() || member.Soldier.Pose == Igi2HumanPose.LyingFlat || member.MemberTimer < 150)
                    && !member.IsShotgunSoldier())
                || member.IsOnCoverNode();
            squad.Teams[covers ? covering : _movingTeam].Members.Add(member);
        }

        if (squad.Teams[_movingTeam].Leader is { } leader)
        {
            _node = leader.CurrentNode;
        }

        squad.TeamCount = 2;
    }

    /// <summary>The leapfrog's tick, <c>AISquad_SquadActionLeapFrogAdvance</c>.</summary>
    private void Advance(Igi2AiSquad squad)
    {
        int moving = _movingTeam;
        int covering = 1 - moving;
        float distance = squad.DistanceToTarget(covering);
        if (squad.FormationTimer == 0)
        {
            squad.SetFormation(moving, Igi2AiFormations.V);
        }

        if (_timer > 0)
        {
            _timer--;
            return;
        }

        if (distance > 122880f && squad.Teams[covering].Members.Count > 1)
        {
            _spread.Center = Igi2AiSpreadOut.FindCenter(squad);
            _spread.SpreadSideways(squad, _target!.DetectablePosition, covering);
        }

        _timer = 30;
        UpdateTeams(squad);
        foreach (Igi2HumanAi member in squad.Teams[covering].Members.ToArray())
        {
            if (!member.IsTop(Igi2AiContent.CombatIdleInit) && !member.IsTop(Igi2AiContent.ActivateInit))
            {
                member.Actions.PopUntilPushable(member);
            }
        }

        if (squad.FirstMemberWithVisibleTarget(covering) < 0 && squad.FirstMemberWithVisibleTarget(moving) < 0)
        {
            return;
        }

        if (squad.Teams[moving].Members.Count <= 0)
        {
            return;
        }

        Igi2HumanAi leader = squad.Teams[moving].Leader!;
        if (!leader.IsEmptyMovementQueue || leader.Graph is not { } graph)
        {
            return;
        }

        if (graph.Nearest(_target!.DetectablePosition, false) is not { } node)
        {
            return;
        }

        int from = leader.CurrentNode;
        _node = node.Id;
        if (node.Id == from)
        {
            return;
        }

        int idle = squad.FirstMemberWithoutVisibleTarget(moving);
        if (idle >= 0)
        {
            squad.Teams[moving].MakeLeader(idle);
        }

        if (squad.Teams[moving].Leader is { } announcer)
        {
            squad.SelectAndPlaySound(11, announcer);
        }

        int delay = squad.OrderDelay();
        squad.Teams[moving].Leader?.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Delay = delay, Node = _node, IsRun = true });
        squad.SetFormation(moving, Igi2AiFormations.V);
        squad.ClearTeamsMemberTimers(moving);
    }
}
