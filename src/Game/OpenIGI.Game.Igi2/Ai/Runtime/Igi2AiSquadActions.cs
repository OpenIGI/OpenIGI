using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>What a squad does, <c>AISquadActions_e</c>, the value <c>AISquad_GetState</c> gives the script.</summary>
public enum Igi2AiSquadActionKind
{
    /// <summary>Nothing.</summary>
    Idle = 0,

    /// <summary>Hold where it is, looking at its targets.</summary>
    HoldArea = 1,

    /// <summary>Send its leader to see what happened.</summary>
    Inquire = 2,

    /// <summary>Search around a position in two teams.</summary>
    SearchArea = 3,

    /// <summary>Move to a node.</summary>
    Move = 4,

    /// <summary>Advance on a target, one team covering the other.</summary>
    Leapfrog = 5,

    /// <summary>Walk a patrol.</summary>
    Patrol = 6,

    /// <summary>Move some way in a direction and search.</summary>
    MoveSearchInDirection = 7,

    /// <summary>Look a direction, then move that way.</summary>
    LookSearchInDirection = 8,

    /// <summary>Take cover from a danger, then search.</summary>
    Danger = 9,
}

/// <summary>
/// What a change of squad action carries, <c>AISquadActionChangeContext_s</c>: the action coming and going, whether the members'
/// stacks go with it, and what the coming action needs (the outgoing action's DeInit fills it from the current event).
/// </summary>
public sealed class Igi2AiSquadChange
{
    /// <summary>Gets or sets the action coming.</summary>
    public int NextAction { get; set; }

    /// <summary>Gets or sets the action going, which DeInit records.</summary>
    public int PreviousAction { get; set; }

    /// <summary>Gets or sets a value indicating whether the members' stacks are popped.</summary>
    public bool PopMembers { get; set; }

    /// <summary>Gets or sets a value indicating whether a save is being restored (no lines then).</summary>
    public bool Deserializing { get; set; }

    /// <summary>Gets or sets the position the coming action works at.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the direction it works in.</summary>
    public Vector3f Direction { get; set; }

    /// <summary>Gets or sets the member who heard the event, for an inquiry.</summary>
    public Igi2HumanAi? Receiver { get; set; }

    /// <summary>Gets or sets the enemy a leapfrog advances on.</summary>
    public IIgi2Detectable? Target { get; set; }

    /// <summary>Gets or sets the patrol to walk.</summary>
    public int PatrolPathId { get; set; }
}

/// <summary>
/// A squad action, <c>AISquadAction_s</c>: its Init as it is pushed, its Run each tick it is on top, its DeInit as it is popped, and
/// its event handler (always the script's); the state it keeps lives on the subclass.
/// </summary>
public abstract class Igi2AiSquadAction
{
    /// <summary>Gets the kind.</summary>
    public abstract Igi2AiSquadActionKind Kind { get; }

    /// <summary>Gets or sets the line the switch to it was announced with, which it waits out.</summary>
    public IIgi2SoundInstance? SwitchStateSound { get; set; }

    /// <summary>Starts it.</summary>
    /// <param name="squad">The squad.</param>
    /// <param name="change">What the change carries.</param>
    /// <returns><see langword="false"/> when it popped itself at once.</returns>
    public abstract bool Init(Igi2AiSquad squad, Igi2AiSquadChange change);

    /// <summary>Runs it a tick.</summary>
    /// <param name="squad">The squad.</param>
    /// <returns><see langword="false"/> when it popped itself.</returns>
    public virtual bool Run(Igi2AiSquad squad) => true;

    /// <summary>
    /// Ends it (<c>AISquad_SquadActionDefaultDeInitHandler</c>): a hold, search or directed search ends with the "all clear" line
    /// from the leader; the coming action is handed what it needs from the current event; any action but idle and patrol pops the
    /// members' stacks when asked; and going idle clears them and drops their combat-idle events.
    /// </summary>
    /// <param name="squad">The squad.</param>
    /// <param name="change">What the change carries.</param>
    public virtual void DeInit(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        change.PreviousAction = (int)Kind;
        if (Kind is Igi2AiSquadActionKind.HoldArea or Igi2AiSquadActionKind.SearchArea
            or Igi2AiSquadActionKind.MoveSearchInDirection or Igi2AiSquadActionKind.LookSearchInDirection && !change.Deserializing)
        {
            squad.SelectAndPlaySound(13, squad.FirstMember);
        }

        OnDeInit();
        if (squad.CurrentEvent is { } current)
        {
            switch ((Igi2AiSquadActionKind)change.NextAction)
            {
                case Igi2AiSquadActionKind.Inquire:
                    change.Position = current.Position;
                    change.Receiver = current.Receivers.Count > 0 ? current.Receivers[0] : null;
                    break;
                case Igi2AiSquadActionKind.SearchArea or Igi2AiSquadActionKind.Move:
                    change.Position = current.Position;
                    break;
                case Igi2AiSquadActionKind.Leapfrog:
                    change.Position = current.Position;
                    change.Target = current.Reference as IIgi2Detectable;
                    break;
                case Igi2AiSquadActionKind.MoveSearchInDirection or Igi2AiSquadActionKind.LookSearchInDirection or Igi2AiSquadActionKind.Danger:
                    change.Position = current.Position;
                    change.Direction = current.Direction;
                    break;
            }
        }

        if (Kind is not Igi2AiSquadActionKind.Patrol and not Igi2AiSquadActionKind.Idle && change.PopMembers)
        {
            squad.PopMembersActionStacks();
        }

        if (change.NextAction == (int)Igi2AiSquadActionKind.Idle)
        {
            if (change.PopMembers)
            {
                squad.ClearMembersActionStacks();
            }

            squad.RemoveEventFromMembersLists(30);
        }
    }

    /// <summary>Lets go of what the action holds as it ends.</summary>
    protected virtual void OnDeInit()
    {
    }

    /// <summary>Writes the action's own state for a save game, after its kind.</summary>
    /// <param name="words">Where it is written.</param>
    /// <param name="squad">Its squad.</param>
    public virtual void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
    }

    /// <summary>Reads the action's own state back, as <c>AISquad_DeSerializeHandler</c> rebinds each action by its kind.</summary>
    /// <param name="words">Where it is read from.</param>
    /// <param name="squad">Its squad.</param>
    public virtual void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
    }

    /// <summary>The run stream's difficulty test the searches make: a member runs unless the game is on its easiest setting.</summary>
    protected static bool RunsOn(Igi2AiSquad squad) => squad.World.Difficulty != 0;
}

/// <summary>The idle action (<c>AISquad_SquadActionIdle*</c>): one team firing on order, no targets, no danger, members cleared.</summary>
public sealed class Igi2AiSquadIdle : Igi2AiSquadAction
{
    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Idle;

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        squad.MakeOneTeam();
        squad.ClearTargetLists();
        squad.Flags &= ~6;
        squad.Teams[0].FiringMode = 1;
        squad.ClearMembersActionStacks();
        return true;
    }
}

/// <summary>
/// The hold (<c>AISquad_SquadActionHoldArea*</c>): with no targets for five seconds it goes idle; with targets, each combat-idle
/// member with no live enemy listed looks at the nearest target's last position.
/// </summary>
public sealed class Igi2AiSquadHoldArea : Igi2AiSquadAction
{
    private int _timer;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.HoldArea;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_timer);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _timer = words.Int();
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        squad.Teams[0].FiringMode = 1;
        squad.MakeOneTeam();
        _timer = 0;
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (squad.Targets.Count <= 0)
        {
            _timer++;
            if (_timer > 150)
            {
                squad.PopToIdle();
            }

            return true;
        }

        foreach (Igi2HumanAi member in squad.Teams[0].Members.ToArray())
        {
            if (!member.IsTop(Igi2AiContent.CombatIdleInit) || member.GetTarget(visibleOnly: true) is not null)
            {
                continue;
            }

            if (member.NearestLiveEnemyListed() is not null)
            {
                continue;
            }

            Vector3d from = member.Soldier.Position;
            Igi2AiSquadTarget? nearest = null;
            float best = 0f;
            foreach (Igi2AiSquadTarget target in squad.Targets)
            {
                float distance = Igi2AiMath.Distance(from, target.LastPosition);
                if (nearest is null || distance < best)
                {
                    best = distance;
                    nearest = target;
                }
            }

            if (nearest is not null)
            {
                member.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.LookAtPosition, Position = nearest.LastPosition });
            }
        }

        return true;
    }
}

/// <summary>
/// The inquiry (<c>AISquad_SquadActionInquire*</c>): the member who heard leads; everyone looks at the place, the leader goes to
/// see; after the inquire timeout the squad is told <c>AIEVENT_INQUIRE_TIMEOUT</c> (29) at its first target or its leader.
/// </summary>
public sealed class Igi2AiSquadInquire : Igi2AiSquadAction
{
    private int _timer;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Inquire;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_timer);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _timer = words.Int();
    }

    /// <summary>Gets the place being looked into.</summary>
    public Vector3d Position { get; private set; }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        _timer = 0;
        Position = change.Position;
        squad.MakeOneTeam();
        squad.Teams[0].FiringMode = 0;
        int index = change.Receiver is { } receiver ? squad.Teams[0].Members.IndexOf(receiver) : -1;
        if (index < 0)
        {
            squad.PopToIdle();
            return false;
        }

        squad.Teams[0].MakeLeader(index);
        squad.SendOrderToSquad(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.LookAtPosition, Delay = squad.OrderDelay(), Position = Position });
        int delay = squad.OrderDelay();
        squad.FirstMember?.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.Inquire, Delay = delay, Position = change.Position });
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (_timer >= Fpu.ToInt32((Fpu)squad.InquireTimeout * 30.0))
        {
            Vector3d at = squad.Targets.Count > 0 ? squad.Targets[0].LastPosition : squad.FirstMember!.Soldier.Position;
            squad.ReceiveEvent(29, at);
            return true;
        }

        _timer++;
        return true;
    }
}

/// <summary>
/// The area search (<c>AISquad_SquadActionSearchArea*</c>): two teams walk a five-point pattern around the place in staggered
/// columns, a round for each difficulty level, each round wider; then it goes idle.
/// </summary>
public sealed class Igi2AiSquadSearchArea : Igi2AiSquadAction
{
    private static readonly Vector3f[] Pattern =
    [
        new(0f, 0f, 0f), new(-61440f, -61440f, 0f), new(61440f, -61440f, 0f), new(61440f, 61440f, 0f), new(-61440f, 61440f, 0f),
    ];

    private static readonly float[] Scales = [1f, 2f, 3f];
    private readonly int[] _destination = new int[2];
    private readonly int[] _patternIndex = new int[2];
    private int _step;
    private int _rounds;
    private int _scale;
    private Vector3d _center;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.SearchArea;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_destination[0]);
        words.Add(_destination[1]);
        words.Add(_patternIndex[0]);
        words.Add(_patternIndex[1]);
        words.Add(_step);
        words.Add(_rounds);
        words.Add(_scale);
        words.Add(_center);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _destination[0] = words.Int();
        _destination[1] = words.Int();
        _patternIndex[0] = words.Int();
        _patternIndex[1] = words.Int();
        _step = words.Int();
        _rounds = words.Int();
        _scale = words.Int();
        _center = words.Double3();
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        squad.MakeTwoTeams();
        squad.ClearTargetLists();
        squad.ClearMembersPaths();
        squad.ClearMembersActionStacks();
        Igi2HumanAi first = squad.Teams[0].Leader!;
        Igi2HumanAi? second = squad.Teams[1].Leader;
        _center = change.Position;
        _destination[0] = first.CurrentNode;
        _destination[1] = second?.CurrentNode ?? -1;
        _rounds = squad.World.Difficulty + 1;
        _step = 0;
        _scale = 0;
        _patternIndex[0] = 0;
        _patternIndex[1] = 4;
        squad.Teams[0].FiringMode = 0;
        squad.Teams[1].FiringMode = 0;
        squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
        squad.SetFormation(1, Igi2AiFormations.StaggeredColumn);
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Igi2HumanAi?[] leaders = [squad.Teams[0].Leader, squad.Teams[1].Leader];
        if (squad.FormationTimer == 0)
        {
            squad.SetFormation(0, Igi2AiFormations.StaggeredColumn);
            squad.SetFormation(1, Igi2AiFormations.StaggeredColumn);
        }

        for (int j = 0; j < 2; j++)
        {
            if (leaders[j] is { } leader && _destination[j] != leader.DestinationNode)
            {
                ResendMoves(squad, leaders);
                return true;
            }
        }

        bool arrived = leaders[0]!.CurrentNode == _destination[0];
        if (leaders[1] is { } second)
        {
            arrived &= second.CurrentNode == _destination[1];
        }

        if (!arrived)
        {
            return true;
        }

        if (_step < 5)
        {
            for (int i = 0; i < 2; i++)
            {
                if (leaders[i] is not { } leader)
                {
                    continue;
                }

                Vector3f offset = Pattern[_patternIndex[i]];
                float scale = Scales[_scale];
                Vector3d at = new(
                    (((Fpu)offset.X * scale) + _center.X).Value, (((Fpu)offset.Y * scale) + _center.Y).Value, ((Fpu)offset.Z * scale).F + _center.Z);
                if (leader.Graph is { } graph && graph.Nearest(at, false) is { } node)
                {
                    _destination[i] = node.Id;
                    leader.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Node = node.Id, IsRun = RunsOn(squad) });
                }

                if (i != 0)
                {
                    _patternIndex[1]--;
                    if (_patternIndex[1] < 0)
                    {
                        _patternIndex[1] += 5;
                    }
                }
                else
                {
                    _patternIndex[0]++;
                }
            }

            _step++;
            return true;
        }

        if (_rounds > 1)
        {
            if (squad.FirstMember is { } leader)
            {
                squad.SelectAndPlaySound(3, leader);
            }

            _rounds--;
            _step = 0;
            _patternIndex[0] = 0;
            _scale++;
            _patternIndex[1] = 4;
            return true;
        }

        squad.PopToIdle();
        return true;
    }

    private void ResendMoves(Igi2AiSquad squad, Igi2HumanAi?[] leaders)
    {
        for (int j = 0; j < 2; j++)
        {
            leaders[j]?.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.MoveToNode, Node = _destination[j], IsRun = RunsOn(squad) });
        }
    }
}

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

/// <summary>
/// The leapfrog (<c>AISquad_SquadActionLeapFrog*</c>): one team moves up on the target in a V while the other, which sees it,
/// covers; a second apart the teams are redrawn and, when the covering team has the target, the moving team's leader runs on.
/// </summary>
public sealed class Igi2AiSquadLeapfrog : Igi2AiSquadAction
{
    private int _node;
    private int _movingTeam;
    private int _timer;
    private IIgi2Detectable? _target;
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
        _target = words.Int() == 1 ? squad.World.Player as Detection.IIgi2Detectable : null;
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

/// <summary>
/// Where a covering team spreads to (<c>AISquadSpreadOutContext_s</c>): the spread-out center, eight places to either side of it,
/// the turn of the next place, and the wait before another member moves.
/// </summary>
public sealed class Igi2AiSpreadOut
{
    private readonly Vector3d[] _places = new Vector3d[8];

    /// <summary>Gets or sets the spread-out center.</summary>
    public Vector3d Center { get; set; }

    /// <summary>Writes its state for a save game: its center, its places, its timer and its turn.</summary>
    /// <param name="words">Where it is written.</param>
    public void SaveState(Igi2AiSquadWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(Center);
        foreach (Vector3d place in _places)
        {
            words.Add(place);
        }

        words.Add(UpdateTimer);
        words.Add(Turn);
    }

    /// <summary>Reads its state back.</summary>
    /// <param name="words">Where it is read from.</param>
    public void LoadState(Igi2AiSquadWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Center = words.Double3();
        for (int i = 0; i < _places.Length; i++)
        {
            _places[i] = words.Double3();
        }

        UpdateTimer = words.Int();
        Turn = words.Int();
    }

    /// <summary>Gets or sets the ticks before another member moves.</summary>
    public int UpdateTimer { get; set; }

    /// <summary>Gets or sets which place is next.</summary>
    public int Turn { get; set; }

    /// <summary>
    /// The squad's center moved away from the other squads it crowds (<c>AISquad_FindSpreadOutCenter</c>): its first member, pushed
    /// by the average of how far each other live squad (over four metres off) overlaps it.
    /// </summary>
    /// <param name="squad">The squad.</param>
    /// <returns>The center.</returns>
    public static Vector3d FindCenter(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Vector3d center = squad.FirstMember?.Soldier.Position ?? default;
        float radius = squad.AreaRadius(center);
        float dx = 0f, dy = 0f, dz = 0f;
        int count = 0;
        foreach (Igi2AiSquad other in squad.World.Squads)
        {
            if (ReferenceEquals(other, squad) || other.IsDead)
            {
                continue;
            }

            Vector3d at = other.FirstMember?.Soldier.Position ?? default;
            float otherRadius = other.AreaRadius(at);
            float x = ((Fpu)center.X - at.X).F, y = ((Fpu)center.Y - at.Y).F, z = ((Fpu)center.Z - at.Z).F;
            float length = Fpu.Sqrt((((Fpu)x * x) + ((Fpu)y * y)) + (((Fpu)center.Z - at.Z) * ((Fpu)center.Z - at.Z))).F;
            if (length > 4096f)
            {
                Fpu reach = (Fpu)otherRadius + radius;
                if (length < reach.Value)
                {
                    Fpu push = reach / length;
                    count++;
                    y = (push * y).F;
                    z = (push * z).F;
                    dx = (((Fpu)x * push) + dx).F;
                    dy = ((Fpu)y + dy).F;
                    dz = ((Fpu)z + dz).F;
                }
            }
        }

        if (count == 0)
        {
            return center;
        }

        Fpu share = 1.0 / count;
        dy = (share * dy).F;
        dz = (share * dz).F;
        return new Vector3d((((Fpu)dx * share) + center.X).Value, ((Fpu)dy + center.Y).Value, ((Fpu)dz + center.Z).Value);
    }

    /// <summary>
    /// Moves one covering member sideways (<c>AISquad_SpreadTeamSideways</c>): every five seconds at most, half the time, a member
    /// settled for five seconds, off cover, not activating or moving, with no shotgun and at least thirty percent of a clip, moves to
    /// the next place across the line to the target, when two of its team see the target and no one stands there.
    /// </summary>
    /// <param name="squad">The squad.</param>
    /// <param name="target">The target's position.</param>
    /// <param name="team">The covering team.</param>
    public void SpreadSideways(Igi2AiSquad squad, Vector3d target, int team)
    {
        ArgumentNullException.ThrowIfNull(squad);
        int turn = squad.FireIndex;
        Igi2HumanAi? member = turn < squad.Teams[team].Members.Count ? squad.Teams[team].Members[turn] : null;
        if (member is null)
        {
            return;
        }

        if (UpdateTimer > 0)
        {
            UpdateTimer--;
            return;
        }

        if (member.MemberTimer < 150 || squad.World.Random.NextSingle() < 0.5f || member.IsOnCoverNode() || member.IsOnTheWayToCoverNode()
            || member.IsTop(Igi2AiContent.ActivateInit) || member.IsTop(Igi2AiContent.MoveToNodeSquadOrderInit) || member.IsShotgunSoldier()
            || !member.IsLessThanPercentageAmmoLeftInWeapon(30f))
        {
            return;
        }

        int seeing = squad.Teams[team].Members.Count(static m => m.GetTarget(visibleOnly: true) is not null);
        if (seeing < 2)
        {
            return;
        }

        double ax = target.X - Center.X;
        float ay = ((Fpu)target.Y - Center.Y).F;
        if (ax != 0.0 || ay != 0.0)
        {
            double bx = -ax;
            Fpu length = Fpu.Sqrt(((Fpu)ay * ay) + ((Fpu)bx * bx));
            float across = ((Fpu)ay * ((Fpu)1.0 / length)).F;
            Fpu along = (Fpu)bx * ((Fpu)1.0 / length);
            float distance = squad.Task.FormationDistance;
            for (int i = 0; i < 8; i++)
            {
                double side = (i & 1) == 0 ? -4096.0 : 4096.0;
                _places[i] = new Vector3d(
                    ((((Fpu)across * distance) * side) + Center.X).Value, (((along * distance) * side) + Center.Y).Value, Center.Z);
            }
        }

        if (member.Graph is not { } graph || graph.NodeAt(member.CurrentNode) is not { } start)
        {
            return;
        }

        if (graph.ClosestNodeFromStartingNode(start, _places[squad.FireIndex], 50) is not { } node)
        {
            return;
        }

        foreach (Igi2HumanAi other in squad.Teams[team].Members)
        {
            if (other.CurrentNode == node.Id)
            {
                return;
            }
        }

        member.DestinationNode = node.Id;
        member.MemberTimer = 0;
        UpdateTimer = 150;
    }
}

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

/// <summary>
/// The danger (<c>AISquad_SquadActionDanger*</c>): after half a second everyone takes cover from it; once no one is still moving
/// or activating for a second, each sweeps its eyes (crouched on a coin toss, unless its cover forbids); after the danger timeout the
/// squad is told <c>AIEVENT_DANGER_TIMEOUT</c> (38) and, a tick later, goes idle. A danger during a danger skips to the end.
/// </summary>
public sealed class Igi2AiSquadDanger : Igi2AiSquadAction
{
    private Vector3d _position;
    private Vector3f _direction;
    private int _stage;
    private int _timer;

    /// <inheritdoc/>
    public override Igi2AiSquadActionKind Kind => Igi2AiSquadActionKind.Danger;

    /// <inheritdoc/>
    public override void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(_position);
        words.Add(_direction);
        words.Add(_stage);
        words.Add(_timer);
    }

    /// <inheritdoc/>
    public override void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(words);
        _position = words.Double3();
        _direction = words.Single3();
        _stage = words.Int();
        _timer = words.Int();
    }

    /// <inheritdoc/>
    public override bool Init(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        _stage = 0;
        _timer = 0;
        _direction = change.Direction;
        _position = change.Position;
        if ((squad.Flags & 2) != 0)
        {
            _stage = 2;
            _timer = -1 - Fpu.ToInt32((Fpu)squad.DangerTimeout * -30.0);
        }
        else
        {
            squad.MakeOneTeam();
            squad.ClearMembersPaths();
            squad.PopMembersActionStacks();
        }

        squad.Flags |= 2;
        return true;
    }

    /// <inheritdoc/>
    public override bool Run(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        switch (_stage)
        {
            case 0:
                if (_timer < 15)
                {
                    _timer++;
                    return true;
                }

                _timer = 0;
                _stage = 1;
                squad.ClearMembersPaths();
                squad.PopMembersActionStacks();
                squad.SendOrderToSquad(new Igi2AiSquadOrder
                {
                    Kind = Igi2AiSquadOrderKind.TakeCover,
                    Delay = squad.OrderDelay(),
                    Position = _position,
                    // The order's direction is read from what the C left on its stack past the position: its first value is the
                    // danger's third, and the rest are whatever was there before.
                    Direction = new Vector3f(_direction.Z, 0f, 0f),
                });
                return true;
            case 1:
                foreach (Igi2HumanAi member in squad.Teams[0].Members)
                {
                    if (!member.IsEmptyMovementQueue || member.IsTop(Igi2AiContent.ActivateInit))
                    {
                        return true;
                    }
                }

                int waited = _timer + 1;
                _timer++;
                if (waited > 30)
                {
                    _stage = 2;
                    _timer = 0;
                }

                return true;
            case 2:
                _timer++;
                if (_timer == 1)
                {
                    foreach (Igi2HumanAi member in squad.Teams[0].Members.ToArray())
                    {
                        bool crouch = (squad.World.Random.NextRaw() & 1) == 0;
                        if (member.Actions.Top is { } top && top.Run == Igi2AiContent.TakeCoverRun && !Igi2AiContent.CoverAllowsCrouch(top))
                        {
                            crouch = false;
                        }

                        member.ReceiveSquadOrder(new Igi2AiSquadOrder { Kind = Igi2AiSquadOrderKind.VisualSweep, IsCrouch = crouch });
                    }

                    return true;
                }

                int timeout = Fpu.ToInt32((Fpu)squad.DangerTimeout * 30.0);
                if (_timer == timeout)
                {
                    squad.PopMembersActionStacks();
                    squad.ReceiveEvent(new Igi2AiSquadEvent { Kind = 38, Position = _position, Direction = _direction });
                    return true;
                }

                if (_timer > timeout)
                {
                    squad.PopMembersActionStacks();
                    squad.PopToIdle();
                    return false;
                }

                return true;
            default:
                return true;
        }
    }
}
