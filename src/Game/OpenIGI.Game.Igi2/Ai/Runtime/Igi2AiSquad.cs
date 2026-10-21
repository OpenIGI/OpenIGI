using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// A squad's mind, I.G.I. 2's <c>AISquad_s</c> at run time: its soldiers' minds in up to two teams, the events its members report
/// (which its script handles), the enemies they share, a stack of up to four squad actions, and the timers that keep it tense.
/// </summary>
/// <remarks>
/// <para>
/// <c>AISquad_RunHandler</c> counts its members, lets finished lines go and, while any live, keeps an action running, fires its
/// rotating fire order, ages its targets, keeps the members' combat idle, hands the events to the top action (whose handler runs the
/// script), runs that action, and finally runs its soldiers. A squad with no members left only runs its soldiers.
/// </para>
/// <para>
/// The original numbers its formation tick from the squad's heap address; this port numbers it from the squad's task id, which is
/// as arbitrary and stays the same from run to run.
/// </para>
/// </remarks>
public sealed partial class Igi2AiSquad
{
    /// <summary>The most events a squad holds at once.</summary>
    public const int EventCapacity = 32;

    /// <summary>The most enemies a squad tracks.</summary>
    public const int TargetCapacity = 5;

    /// <summary>The most actions on a squad's stack.</summary>
    public const int ActionCapacity = 4;

    private readonly List<Igi2AiSquadEvent> _events = [];
    private readonly List<Igi2AiSquadEvent> _scriptEvents = [];
    private readonly List<Igi2AiSquadTarget> _targets = [];
    private readonly List<Igi2AiSquadAction> _actions = [];
    private readonly List<(IIgi2SoundInstance Sound, Igi2HumanAi Member)> _sounds = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2AiSquad"/> class, as <c>AISquad_CreateHandler</c> makes one.</summary>
    /// <param name="world">The AI module.</param>
    /// <param name="task">The level's <c>AISquad</c> task.</param>
    public Igi2AiSquad(Igi2AiWorld world, Igi2AiSquadTask task)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Teams = [new Igi2AiSquadTeam(), new Igi2AiSquadTeam()];
        FormationTimer = (int)((uint)task.Id % 60u);
    }

    /// <summary>Gets the AI module.</summary>
    public Igi2AiWorld World { get; }

    /// <summary>Gets the level's <c>AISquad</c> task, which holds what the level set.</summary>
    public Igi2AiSquadTask Task { get; }

    /// <summary>Gets or sets its place among the level's squads, which a soldier's report mask is kept by.</summary>
    public int Index { get; set; }

    /// <summary>Gets its two teams.</summary>
    public Igi2AiSquadTeam[] Teams { get; }

    /// <summary>Gets or sets how many teams are in use, <c>nNumTeams</c>: one or two, nought with no members.</summary>
    public int TeamCount { get; set; }

    /// <summary>Gets the enemies its members have reported.</summary>
    public IReadOnlyList<Igi2AiSquadTarget> Targets => _targets;

    /// <summary>Gets the events waiting, the most important first; the last is handled next.</summary>
    public IReadOnlyList<Igi2AiSquadEvent> Events => _events;

    /// <summary>Gets its actions, the bottom first.</summary>
    public IReadOnlyList<Igi2AiSquadAction> Actions => _actions;

    /// <summary>Gets its script, <c>MISSION:AI/Squad_&lt;id&gt;.qsc</c>, once loaded.</summary>
    public QvmProgram? Script { get; private set; }

    /// <summary>Gets or sets its flags, <c>bFlags</c>: 1 flush the events after the script, 2 in danger, 4 the player was seen.</summary>
    public int Flags { get; set; }

    /// <summary>Gets or sets the ticks it stays tense, <c>nTenseTimer</c>: set by a sighting, a hit or a dead friend.</summary>
    public int TenseTimer { get; set; }

    /// <summary>Gets or sets the ticks before its formation is refreshed, cycling over sixty.</summary>
    public int FormationTimer { get; set; }

    /// <summary>Gets or sets which member the fire order rotates to next.</summary>
    public int FireIndex { get; set; }

    /// <summary>Gets or sets the ticks before a new sign of trouble raises its suspicion again.</summary>
    public int DisallowIncreaseSuspicionTimer { get; set; }

    /// <summary>Gets or sets how suspicious it is: 300 a sign, at most 930; past 630 it is told <c>AIEVENT_SUSPICIOUS</c>.</summary>
    public int SuspicionLevel { get; set; }

    /// <summary>Gets or sets the ticks during which it ignores footsteps and the like (events 9 to 11 and 15).</summary>
    public int RushTimer { get; set; }

    /// <summary>Gets or sets the ticks before a nearby voice line plays, <c>nPlaySoundTimer</c>.</summary>
    public int PlaySoundTimer { get; set; }

    /// <summary>Gets or sets the seconds an inquiry lasts, <c>vInquireTimeout</c>: five, then its first member's tuning.</summary>
    public float InquireTimeout { get; set; } = 5f;

    /// <summary>Gets or sets the seconds a danger lasts, <c>vDangerTimeout</c>: ten, then its first member's tuning.</summary>
    public float DangerTimeout { get; set; } = 10f;

    /// <summary>Gets or sets a value indicating whether all its members are dead, the script's <c>&lt;name&gt;.isDead</c>.</summary>
    public bool IsDead { get; set; }

    /// <summary>Gets the kind of squad, <c>eSquadType</c>: two rides a vehicle.</summary>
    public int SquadType => Task.SquadType;

    /// <summary>Gets its first member, the first team's leader, or <see langword="null"/>.</summary>
    public Igi2HumanAi? FirstMember => Teams[0].Leader;

    /// <summary>Gets its current action, <c>AISquad_GetState</c>: idle with none.</summary>
    public int State => _actions.Count > 0 ? (int)_actions[^1].Kind : 0;

    /// <summary>Gets how many enemies it shares.</summary>
    public int SharedTargets => _targets.Count;

    /// <summary>Gets where its first enemy was last reported, <c>AISquad_GetLastTargetPosition(0)</c>.</summary>
    public Vector3d SharedTargetPosition => _targets.Count > 0 ? _targets[0].LastPosition : default;

    /// <summary>Gets the event the script is handling, <c>AISquad_GetCurrentEvent</c>: the last, or <see langword="null"/>.</summary>
    public Igi2AiSquadEvent? CurrentEvent => _events.Count > 0 ? _events[^1] : null;

    /// <summary>Gets how many members it has, <c>AISquad_GetMemberCount</c>.</summary>
    public int MemberCount
    {
        get
        {
            int count = 0;
            for (int t = 0; t < TeamCount; t++)
            {
                count += Teams[t].Members.Count;
            }

            return count;
        }
    }

    /// <summary>Gets every member in team order.</summary>
    public IEnumerable<Igi2HumanAi> Members
    {
        get
        {
            for (int t = 0; t < TeamCount; t++)
            {
                foreach (Igi2HumanAi member in Teams[t].Members.ToArray())
                {
                    yield return member;
                }
            }
        }
    }

    /// <summary>Loads its script (<c>AISquad_UpdateInternalDataHandler</c>).</summary>
    public void UpdateInternalData()
    {
        Script = null;
        if (Task.Id != Engine.Tasks.GameTask.NoId && World.Resources is { } resources)
        {
            Script = Igi2AiWorld.Load(resources, Task.ScriptName);
        }
    }

    /// <summary>
    /// Builds its one team from its soldiers' minds, in order, at most eight (<c>AISquad_BuildTeamFromChildren</c>, run once the
    /// level is loaded).
    /// </summary>
    /// <param name="minds">Its soldiers' minds, in its children's order.</param>
    public void BuildTeam(IEnumerable<Igi2HumanAi> minds)
    {
        ArgumentNullException.ThrowIfNull(minds);
        Teams[0].Members.Clear();
        Teams[1].Members.Clear();
        foreach (Igi2HumanAi mind in minds)
        {
            if (Teams[0].Members.Count >= Igi2AiSquadTeam.Capacity)
            {
                break;
            }

            Teams[0].Members.Add(mind);
            mind.Squad = this;
        }

        TeamCount = Teams[0].Members.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// A tick of the squad (<c>AISquad_RunHandler</c>), before its soldiers run: see the type's remarks.
    /// </summary>
    public void Run()
    {
        UpdateMemberCount();
        MaintainPlayingSounds();
        if (IsDead)
        {
            return;
        }

        if (_actions.Count == 0)
        {
            PushIdle();
        }

        MaintainMisc();
        SendFireOrder();
        MaintainTargetList();
        if (_targets.Count > 0)
        {
            SendCombatIdleEvent();
        }

        // Every action's event handler is AISquad_SquadActionDefaultEventHandler: the script handles the events.
        if (_events.Count > 0)
        {
            RunScriptOnEvents();
        }

        if (_actions.Count == 0)
        {
            PushIdle();
        }

        RunActionStack();
    }

    /// <summary>
    /// Tells the squad of an event (<c>AISquad_ReceiveEvent</c>): a sighting, a hit or a dead friend makes it tense; while it is
    /// rushing it ignores the small sounds; a sighting adds or refreshes a target (the first target pulls its members off what they
    /// were doing, and the player first seen by any squad counts as detected). An event of a kind already waiting that one member
    /// reported adds that member to it; otherwise it is queued by rank. Told during its own script, it waits until the script ends.
    /// </summary>
    /// <param name="squadEvent">The event.</param>
    public void ReceiveEvent(Igi2AiSquadEvent squadEvent)
    {
        ArgumentNullException.ThrowIfNull(squadEvent);
        bool inScript = ReferenceEquals(World.CurrentSquad, this);
        List<Igi2AiSquadEvent> queue = inScript ? _scriptEvents : _events;
        if (queue.Count >= EventCapacity)
        {
            return;
        }

        int kind = squadEvent.Kind;
        if (kind is 21 or 14 or 22)
        {
            TenseTimer = 30 * Task.TenseTimeout;
        }

        if (RushTimer != 0 && kind is >= 9 and <= 11 or 15)
        {
            return;
        }

        if (kind == 21 && squadEvent.Reference is IAiDetectable enemy)
        {
            NoteTarget(enemy, squadEvent.Position);
        }

        if (squadEvent.Receivers.Count == 1)
        {
            foreach (Igi2AiSquadEvent waiting in queue)
            {
                if (waiting.Kind == kind)
                {
                    if (waiting.Receivers.Count < 8)
                    {
                        waiting.Receivers.Add(squadEvent.Receivers[0]);
                    }

                    return;
                }
            }
        }

        Igi2AiSquadEvent queued = new()
        {
            Kind = kind,
            Priority = squadEvent.Priority,
            Reference = squadEvent.Reference,
            Position = squadEvent.Position,
            Direction = squadEvent.Direction,
            SpecificInt = squadEvent.SpecificInt,
        };
        if (squadEvent.Receivers.Count == 1)
        {
            queued.Receivers.Add(squadEvent.Receivers[0]);
        }

        int at = queue.Count;
        queue.Add(queued);
        while (at > 0 && queued.Priority > queue[at - 1].Priority)
        {
            queue[at] = queue[at - 1];
            queue[at - 1] = queued;
            at--;
        }
    }

    /// <summary>Tells the squad of an event with no reporter or reference, at rank nought.</summary>
    /// <param name="kind">The event.</param>
    /// <param name="position">Where.</param>
    public void ReceiveEvent(int kind, Vector3d position) => ReceiveEvent(new Igi2AiSquadEvent { Kind = kind, Position = position });

    private void NoteTarget(IAiDetectable enemy, Vector3d position)
    {
        foreach (Igi2AiSquadTarget target in _targets)
        {
            if (ReferenceEquals(target.Enemy, enemy))
            {
                target.Timer = 0;
                target.LastPosition = position;
                return;
            }
        }

        if (_targets.Count >= TargetCapacity)
        {
            return;
        }

        if (_targets.Count == 0)
        {
            PopMembersActionStacks();
            if (enemy is IAiPlayer player && !World.IsTargetSeenByOtherSquads(this, enemy) && (Flags & 4) == 0)
            {
                Flags |= 4;
                player.NoteDetected();
            }
        }

        _targets.Add(new Igi2AiSquadTarget(enemy, position));
    }

    /// <summary>Whether its members share an enemy, <c>AISquad_TargetSeenBySquad</c>.</summary>
    /// <param name="enemy">The enemy.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsTargetSeen(IAiDetectable enemy) => _targets.Exists(t => ReferenceEquals(t.Enemy, enemy));

    /// <summary>
    /// A sign of trouble (called by the mind for footsteps, shots and the like): unless one was taken in the last three seconds,
    /// suspicion rises by ten seconds' worth, to at most 31; past 21 seconds' worth, the squad is told <c>AIEVENT_SUSPICIOUS</c>.
    /// </summary>
    /// <param name="position">Where the sign was.</param>
    public void IncreaseSuspicion(Vector3d position)
    {
        if (DisallowIncreaseSuspicionTimer != 0)
        {
            return;
        }

        int level = SuspicionLevel + 300;
        DisallowIncreaseSuspicionTimer = 90;
        SuspicionLevel = level;
        if (level > 630)
        {
            SuspicionLevel = level >= 930 ? 930 : level;
            ReceiveEvent(46, position);
        }
    }

    /// <summary>
    /// Counts its members again (<c>AISquad_UpdateMemberCount</c>): the dead drop out; an emptied first team takes the second;
    /// with none left it is dead and forgets its targets.
    /// </summary>
    private void UpdateMemberCount()
    {
        int alive = 0;
        for (int t = 0; t < TeamCount; t++)
        {
            Teams[t].Members.RemoveAll(static m => m.Soldier.IsDead);
            alive += Teams[t].Members.Count;
        }

        for (int t = 0; t < TeamCount; t++)
        {
            if (Teams[t].Members.Count == 0)
            {
                MakeOneTeam();
                break;
            }
        }

        IsDead = alive == 0;
        if (alive == 0)
        {
            ClearTargetLists();
        }
    }

    /// <summary>
    /// The squad's timers (<c>AISquad_MaintainMisc</c>): the voice wait; the fire order's turn moves to the next member; the tense
    /// timer; the formation tick cycles over sixty; suspicion falls a tick once three seconds have passed since it last rose; the
    /// rush.
    /// </summary>
    private void MaintainMisc()
    {
        if (PlaySoundTimer > 0)
        {
            PlaySoundTimer--;
        }

        FireIndex = FireIndex >= MemberCount - 1 ? 0 : FireIndex + 1;
        if (TenseTimer != 0)
        {
            TenseTimer--;
        }

        FormationTimer = FormationTimer != 0 ? FormationTimer - 1 : 60;
        if (DisallowIncreaseSuspicionTimer != 0)
        {
            DisallowIncreaseSuspicionTimer--;
        }
        else if (SuspicionLevel != 0)
        {
            SuspicionLevel--;
        }

        if (RushTimer != 0)
        {
            RushTimer--;
        }
    }

    /// <summary>
    /// Ages its targets (<c>AISquad_MaintainTargetList</c>): one not reported for its target timeout is dropped (the last takes its
    /// place). Once the last goes, its members are pulled off what they were doing (unless it rides a vehicle) and it is told
    /// <c>AIEVENT_TARGET_LOST</c> at where that enemy then was.
    /// </summary>
    private void MaintainTargetList()
    {
        int before = _targets.Count;
        Vector3d lastPosition = default;
        int i = 0;
        while (i < _targets.Count)
        {
            Igi2AiSquadTarget target = _targets[i];
            int timer = target.Timer;
            target.Timer = timer + 1;
            if (timer >= 30 * Task.TargetTimeout)
            {
                lastPosition = target.Enemy.DetectablePosition;
                _targets[i] = _targets[^1];
                _targets.RemoveAt(_targets.Count - 1);
            }
            else
            {
                i++;
            }
        }

        if (before != 0 && _targets.Count == 0)
        {
            if (SquadType != 2)
            {
                PopMembersActionStacks();
            }

            ReceiveEvent(27, lastPosition);
        }
    }

    /// <summary>
    /// The rotating fire order (<c>AISquad_SendFireOrder</c>): with targets, unless a member before this tick's turn is already set to
    /// fire, the member whose turn it is is ordered to fire on them, when its team fires on order.
    /// </summary>
    private void SendFireOrder()
    {
        if (_targets.Count == 0)
        {
            return;
        }

        int turn = FireIndex;
        int count = 0;
        int team = 0;
        int index = 0;
        while (count < turn)
        {
            if (index < Teams[team].Members.Count && Teams[team].Members[index] is { } member && member.IsReadyToFire)
            {
                return;
            }

            index++;
            count++;
            if (index >= Teams[team].Members.Count)
            {
                team++;
                index = 0;
            }
        }

        if (team < 2 && Teams[team].FiringMode == 1 && index < Teams[team].Members.Count)
        {
            Teams[team].Members[index].ReceiveSquadOrder(new Igi2AiSquadOrder
            {
                Kind = Igi2AiSquadOrderKind.Fire,
                Targets = [.. _targets],
            });
        }
    }

    /// <summary>Tells each member not combat-idle to become so, <c>AISquad_SendSquadCombatIdleEvent</c> (event 30).</summary>
    private void SendCombatIdleEvent()
    {
        foreach (Igi2HumanAi member in Members)
        {
            if (!member.IsTop(Igi2AiContent.CombatIdleInit))
            {
                member.Receive(new Igi2AiEvent(30, 0, 0, 0f, null, default, null));
            }
        }
    }

    /// <summary>The ticks before a member acts on an order (<c>AISquad_GetOrderDelay</c>): one draw, quicker in a hold.</summary>
    /// <returns>The ticks.</returns>
    public int OrderDelay()
    {
        (int low, int high) = State == 1 ? (6, 18) : (15, 30);
        return low + Fpu.ToInt32((double)(ushort)World.Random.NextRaw() * IgiRandom.Scale * (high - low));
    }

    /// <summary>
    /// Puts a team's followers in formation behind its leader (<c>AISquad_SetFormation</c>): each not already following is ordered,
    /// after a drawn delay, to its place in the formation scaled by the formation distance (the override when set).
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="formation">The formation.</param>
    public void SetFormation(int team, int formation)
    {
        Igi2AiSquadTeam t = Teams[team];
        Igi2HumanAi? leader = t.Leader;
        for (int i = 1; i < t.Members.Count; i++)
        {
            Igi2HumanAi member = t.Members[i];
            if (member.IsTop(Igi2AiContent.MoveInFormationInit))
            {
                continue;
            }

            int delay = OrderDelay();
            (Vector3f place, Vector3f facing) = Igi2AiFormations.Of(formation, i);
            float distance = Task.OverrideFormationDistance > 0f ? Task.OverrideFormationDistance : Task.FormationDistance;
            member.ReceiveSquadOrder(new Igi2AiSquadOrder
            {
                Kind = Igi2AiSquadOrderKind.Formation,
                Delay = delay,
                Leader = leader,
                RelativeDirection = facing,
                RelativePosition = new Vector3f(
                    (((Fpu)place.X * distance) * 4096.0).F, (((Fpu)place.Y * distance) * 4096.0).F, (((Fpu)place.Z * distance) * 4096.0).F),
            });
        }
    }

    /// <summary>Sets flags on every member's mind, <c>AISquad_SetMemberFlags</c>.</summary>
    /// <param name="flags">The flags.</param>
    public void SetMemberFlags(int flags)
    {
        foreach (Igi2HumanAi member in Members)
        {
            member.Flags |= flags;
        }
    }

    /// <summary>Whether a mind leads one of the teams in use, <c>AISquad_IsTeamLeader</c>.</summary>
    /// <param name="member">The mind.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsTeamLeader(Igi2HumanAi member)
    {
        for (int t = 0; t < TeamCount; t++)
        {
            if (ReferenceEquals(Teams[t].Leader, member))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Orders every member, <c>AISquad_SendOrderToSquad</c>.</summary>
    /// <param name="order">The order.</param>
    public void SendOrderToSquad(Igi2AiSquadOrder order)
    {
        foreach (Igi2HumanAi member in Members)
        {
            member.ReceiveSquadOrder(order);
        }
    }

    /// <summary>Puts every member in the first team, <c>AISquad_MakeOneTeam</c>.</summary>
    public void MakeOneTeam()
    {
        Teams[0].Members.AddRange(Teams[1].Members);
        Teams[1].Members.Clear();
        TeamCount = 1;
    }

    /// <summary>Splits its members in two teams, the second taking the last half of the first (<c>AISquad_MakeTwoTeams</c>).</summary>
    public void MakeTwoTeams()
    {
        MakeOneTeam();
        int moving = Teams[0].Members.Count / 2;
        for (int i = 0; i < moving; i++)
        {
            Igi2HumanAi last = Teams[0].Members[^1];
            Teams[0].Members.RemoveAt(Teams[0].Members.Count - 1);
            Teams[1].Members.Add(last);
        }

        TeamCount = 2;
    }

    /// <summary>Pops each member's actions to a pushable one, <c>AISquad_PopMembersActionStacks</c>.</summary>
    public void PopMembersActionStacks()
    {
        foreach (Igi2HumanAi member in Members)
        {
            member.Actions.PopUntilPushable(member);
        }
    }

    /// <summary>Clears each member's actions, <c>AISquad_ClearMembersActionStacks</c>.</summary>
    public void ClearMembersActionStacks()
    {
        foreach (Igi2HumanAi member in Members)
        {
            member.Actions.Clear(member);
        }
    }

    /// <summary>Stops each member where it is, <c>AISquad_ClearMembersPaths</c>.</summary>
    public void ClearMembersPaths()
    {
        foreach (Igi2HumanAi member in Members)
        {
            member.PathSearch.Clear();
            member.DestinationNode = member.CurrentNode;
            member.ClearMovementQueue();
        }
    }

    /// <summary>Makes each member forget the events of a kind, <c>AISquad_RemoveEventFromMembersLists</c>.</summary>
    /// <param name="kind">The kind.</param>
    public void RemoveEventFromMembersLists(int kind)
    {
        foreach (Igi2HumanAi member in Members)
        {
            member.Events.RemoveKind(kind);
        }
    }

    /// <summary>Forgets its targets and each member's detection, <c>AISquad_ClearTargetLists</c>.</summary>
    public void ClearTargetLists()
    {
        _targets.Clear();
        foreach (Igi2HumanAi member in Members)
        {
            member.Detection.Clear();
        }
    }

    /// <summary>Resets each member's member timer in a team, <c>AISquad_ClearTeamsMemberTimers</c>.</summary>
    /// <param name="team">The team.</param>
    public void ClearTeamsMemberTimers(int team)
    {
        foreach (Igi2HumanAi member in Teams[team].Members)
        {
            member.MemberTimer = 0;
        }
    }

    /// <summary>
    /// How far a team's leader is from the first target, <c>AISquad_GetDistanceToTarget</c>, or -1 with no leader or no target.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The distance.</returns>
    public float DistanceToTarget(int team)
    {
        if (Teams[team].Leader is not { } leader || _targets.Count == 0)
        {
            return -1f;
        }

        Vector3d at = _targets[0].Enemy.DetectablePosition;
        Vector3d from = leader.Soldier.Position;
        float dx = ((Fpu)from.X - at.X).F, dy = ((Fpu)from.Y - at.Y).F;
        Fpu dz = (Fpu)from.Z - at.Z;
        return Fpu.Sqrt((((Fpu)dx * dx) + ((Fpu)dy * dy)) + (dz * dz)).F;
    }

    /// <summary>
    /// How far its farthest member is from a point, <c>AISquad_FindSquadAreaRadius</c>.
    /// </summary>
    /// <param name="center">The point.</param>
    /// <returns>The radius.</returns>
    public float AreaRadius(Vector3d center)
    {
        float radius = 0f;
        foreach (Igi2HumanAi member in Members)
        {
            Vector3d at = member.Soldier.Position;
            float dx = ((Fpu)center.X - at.X).F, dy = ((Fpu)center.Y - at.Y).F;
            Fpu dz = (Fpu)center.Z - at.Z;
            Fpu distance = Fpu.Sqrt((((Fpu)dx * dx) + ((Fpu)dy * dy)) + (dz * dz));
            if (distance.Value > radius)
            {
                radius = distance.F;
            }
        }

        return radius;
    }

    /// <summary>The index of the first member of a team with a target in sight, or -1 (<c>AISquad_GetFirstMemberWithVisibleTarget</c>).</summary>
    /// <param name="team">The team.</param>
    /// <returns>The index.</returns>
    public int FirstMemberWithVisibleTarget(int team) => Teams[team].Members.FindIndex(static m => m.VisibleTargetCount() > 0);

    /// <summary>The index of the first member of a team with no target in sight, or -1.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The index.</returns>
    public int FirstMemberWithoutVisibleTarget(int team) => Teams[team].Members.FindIndex(static m => m.VisibleTargetCount() == 0);

    /// <summary>
    /// A voice line from a member, picked from the line's four takes (<c>AISquad_SelectAndPlaySoundAtMembersPos</c>): a line said
    /// within five seconds by any squad, or one a lone soldier does not say, is skipped; a dying member says nothing. The take is
    /// drawn, the voice suffix is the member's, and the line is kept while it plays (at most five lines at once).
    /// </summary>
    /// <param name="sample">The line, nought to 19.</param>
    /// <param name="member">Who says it.</param>
    /// <returns>The line playing, or <see langword="null"/>.</returns>
    public IIgi2SoundInstance? SelectAndPlaySound(int sample, Igi2HumanAi? member)
    {
        if (member is null)
        {
            return null;
        }

        int tick = World.LevelTick();
        if (tick - World.VoiceLastPlayed[sample] < 150 || (Igi2AiSquadVoices.NotForLoneSoldier(sample) && MemberCount == 1))
        {
            return null;
        }

        if (member.Soldier.LifeState != 0)
        {
            return null;
        }

        World.VoiceLastPlayed[sample] = tick;
        string take = Igi2AiSquadVoices.Take(sample, (int)World.Random.NextBounded(4));
        string line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{take}_{member.VoiceIndex:00}");
        IIgi2SoundInstance? sound = member.Say(member.Soldier, line, queueable: true);
        if (sound is not null && !AddSoundToPlayingList(sound, member))
        {
            sound.Delete();
            return null;
        }

        return sound;
    }

    /// <summary>Keeps a line a member says while it plays, <c>AISquad_AddSoundToPlayingList</c>: at most five.</summary>
    /// <param name="sound">The line.</param>
    /// <param name="member">Who says it.</param>
    /// <returns><see langword="false"/> when five already play.</returns>
    public bool AddSoundToPlayingList(IIgi2SoundInstance sound, Igi2HumanAi member)
    {
        if (_sounds.Count >= 5)
        {
            return false;
        }

        _sounds.Add((sound, member));
        return true;
    }

    /// <summary>Lets finished lines go, and stops a line its speaker died saying (<c>AISquad_MaintainPlayingSounds</c>).</summary>
    private void MaintainPlayingSounds()
    {
        for (int i = _sounds.Count - 1; i >= 0; i--)
        {
            (IIgi2SoundInstance sound, Igi2HumanAi member) = _sounds[i];
            if (member.Soldier.IsDead)
            {
                if (sound.IsAlive)
                {
                    sound.Delete();
                }
            }
            else if (sound.IsAlive)
            {
                continue;
            }

            _sounds[i] = _sounds[^1];
            _sounds.RemoveAt(_sounds.Count - 1);
        }
    }

    /// <summary>Lets every line go, <c>AISquad_ClearPlayingSoundsList</c>.</summary>
    public void ClearPlayingSounds()
    {
        foreach ((IIgi2SoundInstance sound, _) in _sounds)
        {
            sound.Delete();
        }

        _sounds.Clear();
    }

    /// <summary>
    /// Hands the waiting events to the script (<c>AISquad_SquadActionDefaultEventHandler</c>): the script runs for the last event,
    /// which then goes, until none is left (or until the script asks for the rest to be dropped); then the events the script itself
    /// raised become the waiting ones.
    /// </summary>
    internal void RunScriptOnEvents()
    {
        World.CurrentSquad = this;
        while (_events.Count > 0)
        {
            if (Script is { } script)
            {
                World.Execute(script);
            }

            RemoveLastEvent();
            if ((Flags & 1) != 0 && _events.Count > 0)
            {
                while (_events.Count > 0)
                {
                    RemoveLastEvent();
                    Flags &= ~1;
                }
            }
        }

        World.CurrentSquad = null;
        _events.Clear();
        _events.AddRange(_scriptEvents);
        _scriptEvents.Clear();
    }

    /// <summary>Drops the last event, <c>AISquad_RemoveLastEvent</c>.</summary>
    public void RemoveLastEvent()
    {
        if (_events.Count > 0)
        {
            _events.RemoveAt(_events.Count - 1);
        }
    }

    /// <summary>Pushes the idle action, <c>AISquad_PushIdleOntoActionStack</c>.</summary>
    private void PushIdle() => Push(new Igi2AiSquadIdle(), new Igi2AiSquadChange { NextAction = 0, PopMembers = true });

    /// <summary>Pushes an action and runs its Init, <c>AISquad_PushOntoActionStack</c>; a full stack takes nothing.</summary>
    /// <param name="action">The action.</param>
    /// <param name="change">What the change carries.</param>
    public void Push(Igi2AiSquadAction action, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_actions.Count >= ActionCapacity)
        {
            return;
        }

        _actions.Add(action);
        action.Init(this, change);
    }

    /// <summary>Pops the top action after its DeInit, <c>AISquad_PopActionStack</c>.</summary>
    /// <param name="change">What the change carries.</param>
    public void Pop(Igi2AiSquadChange change)
    {
        if (_actions.Count == 0)
        {
            return;
        }

        Igi2AiSquadAction top = _actions[^1];
        top.DeInit(this, change);
        top.SwitchStateSound = null;
        _actions.RemoveAt(_actions.Count - 1);
    }

    /// <summary>Pops the top action into idle, popping or clearing the members' stacks.</summary>
    public void PopToIdle() => Pop(new Igi2AiSquadChange { NextAction = 0, PopMembers = true });

    /// <summary>Pops every action, <c>AISquad_ClearActionStack</c>.</summary>
    /// <param name="popMembers">Whether the members' stacks go too.</param>
    public void ClearActionStack(bool popMembers)
    {
        while (_actions.Count > 0)
        {
            Pop(new Igi2AiSquadChange { NextAction = 0, PopMembers = popMembers });
        }
    }

    /// <summary>
    /// Runs the top action (<c>AISquad_RunActionStack</c>): while the line it was switched in with plays, it waits.
    /// </summary>
    private void RunActionStack()
    {
        if (_actions.Count == 0)
        {
            return;
        }

        Igi2AiSquadAction top = _actions[^1];
        if (top.SwitchStateSound is { } sound)
        {
            if (!sound.IsAlive)
            {
                top.SwitchStateSound = null;
            }

            return;
        }

        top.Run(this);
    }
}
