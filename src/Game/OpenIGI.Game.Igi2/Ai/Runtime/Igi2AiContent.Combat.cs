using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>The standing and fighting actions: idle, combat idle, use, patrol, cover, formation and the squad's moves.</content>
public static partial class Igi2AiContent
{
    /// <summary>The upper-body idles an idle soldier picks from, <c>HumanSoldier</c> states 104 to 114.</summary>
    private static readonly int[] IdleStates = [104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114];

    // --- Idle (HumanAIContent_Idle*) -------------------------------------------------------------------------------------------

    /// <summary>Waits ten to nineteen drawn seconds, <c>HumanAIContent_IdleInitCB</c>.</summary>
    public static void IdleInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        IdleContext context = item.ContextAs<IdleContext>();
        uint draw = ai.World.Random.NextRaw();
        context.TicksUsed = 0;
        context.IsIdleAnimPlayed = false;
        context.Timer = (int)(30 * ((draw % 10u) + 10u));
        item.Run = IdleRun;
        ai.DestinationNode = ai.CurrentNode;
    }

    /// <summary>
    /// Stands at its post (<c>HumanAIContent_IdleRunCB</c>): a second in, once its blend is done, it plays one of eleven drawn
    /// upper-body idles its weapon has (or none); it walks back to its node and faces the way it was placed; done once the wait is
    /// spent there.
    /// </summary>
    public static void IdleRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        IdleContext context = item.ContextAs<IdleContext>();
        Igi2HumanSoldier soldier = ai.Soldier;
        if (!context.IsIdleAnimPlayed && context.TicksUsed > 30 && soldier.IsBlendFinished)
        {
            int state = IdleStates[(int)(ai.World.Random.NextRaw() % 11u)];
            soldier.SetUpperBodyAnimationFromState(soldier.HasWeaponAnimationForState(state) ? state : 0, 1f);
            context.IsIdleAnimPlayed = true;
        }

        Vector3d at = soldier.Position;
        Vector3f facing = ai.OriginalViewDirection;
        float z = ((Fpu)facing.Z * 4096.0).F;
        Vector3d ahead = new((facing.X * 4096.0) + at.X, (facing.Y * 4096.0) + at.Y, z + at.Z);
        ai.MakeWalk();
        if (!ai.MoveToNode(ai.OriginalNode))
        {
            return;
        }

        ai.SelectTurnSpeed();
        if (ai.FacePosition(ahead, TurnTolerance, level: true))
        {
            int timer = context.Timer;
            context.Timer = timer - 1;
            context.TicksUsed++;
            if (timer == 1)
            {
                Done(ai, item);
            }
        }
    }

    // --- Combat idle (HumanAIContent_CombatIdle*, …HandleCombatIdleStance) ---------------------------------------------------

    /// <summary>
    /// Settles to fight where it stands (<c>HumanAIContent_CombatIdleInitCB</c>): one of the squad's combat-idle members, or with no
    /// target in sight, or carrying a rocket launcher, keeps its stance (stands or tests lying by a value fixed for the soldier); the
    /// first to see a target marks the squad's members combat-idle and tests lying first.
    /// </summary>
    public static void CombatIdleInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        CombatIdleContext context = item.ContextAs<CombatIdleContext>();
        if ((ai.Flags & 0x10) != 0 || ai.GetTarget(visibleOnly: true) is null || ai.IsRpgSoldier())
        {
            context.Trace = ai.SoldierParity != 0 ? 1 : 3;
        }
        else
        {
            ai.Squad?.SetMemberFlags(0x10);
            context.Trace = 0;
            ai.MemberTimer = 0;
        }

        context.Stance = 2;
        context.PreviousStance = 2;
        item.Run = CombatIdleRun;
        item.Delete = CombatIdleDelete;
        ai.DestinationNode = ai.CurrentNode;
    }

    /// <summary>
    /// Fights where it stands (<c>HumanAIContent_CombatIdleRunCB</c>): done once no longer alerted; it holds its node (or a near
    /// one when crowded), runs to cover when it finds some, keeps its stance by whether it can see the target from it, and faces the
    /// target; a vehicle's soldier only aims.
    /// </summary>
    public static void CombatIdleRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        CombatIdleContext context = item.ContextAs<CombatIdleContext>();
        Igi2HumanSoldier soldier = ai.Soldier;
        Igi2AiSquad? squad = ai.Squad;
        if ((ai.Flags & 4) == 0)
        {
            Done(ai, item);
            return;
        }

        if (soldier.Pose == Igi2HumanPose.LyingFlat)
        {
            squad?.SetMemberFlags(0x10);
        }

        IIgi2Detectable? target = ai.GetTarget(visibleOnly: true) ?? ai.NearestLiveEnemyListed();
        if (target is Igi2Human human)
        {
            Vector3d a = human.Position, b = soldier.Position;
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            if (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) < 6144.0)
            {
                ai.DestinationNode = ai.CurrentNode;
            }
        }

        if (ai.CurrentNode != ai.DestinationNode && squad?.SquadType != 2)
        {
            soldier.ScaleAnimationTimeStep(1.5f);
            if (item.ChildRun is { } child && child(ai, item))
            {
                item.ChildRun = null;
            }

            if (!ai.MoveToNode(ai.DestinationNode))
            {
                item.Flags |= 1;
                item.Priority = ai.Events.RankOf(21);
                ai.MakeRun();
                context.Trace = 3;
                return;
            }

            context.Trace = 0;
            context.Stance = 2;
            ai.FaceTarget();
            return;
        }

        item.Flags &= ~1;
        item.Priority = ai.Events.RankOf(30);
        if (item.ChildRun is { } run)
        {
            if (run(ai, item))
            {
                item.ChildRun = null;
            }
        }
        else
        {
            ai.FaceTarget();
        }

        if (squad?.SquadType == 2 || context.Stance == 0 || !ai.HandleDispersal())
        {
            HandleCombatIdleStance(ai, item);
            if (squad?.SquadType == 2)
            {
                if (soldier.Locomotion.CurrentState != Igi2SoldierState.StandThrowGrenade && ai.GetTarget(visibleOnly: true) is null && target is not null)
                {
                    ai.LastTargetPosition = target.DetectablePosition;
                }
            }
            else
            {
                if (ai.GetTarget(visibleOnly: true) is null)
                {
                    ai.FaceTarget();
                    return;
                }

                if (ai.HandleCoverSearch(null))
                {
                    ai.DestinationNode = ai.CoverSearch.CoverNode;
                }
            }
        }
    }

    /// <summary>Lets the squad's combat-idle mark go for it, <c>HumanAIContent_CombatIdleDeleteCB</c>.</summary>
    public static void CombatIdleDelete(Igi2HumanAi ai, Igi2AiActionItem item) => ai.Flags &= ~0x10;

    /// <summary>
    /// The stance a fighting soldier keeps (<c>HumanAIContent_HandleCombatIdleStance</c>): not on a door node, it lies, kneels or
    /// stands as its test left it (kneeling while it reloads from lying); with a target in sight it tests whether the target can be
    /// seen from lying, then from kneeling, settling on the first that does (kneeling within seven metres), and standing beyond
    /// thirty if it cannot lie.
    /// </summary>
    private static void HandleCombatIdleStance(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        CombatIdleContext context = item.ContextAs<CombatIdleContext>();
        IIgi2Detectable? target = ai.GetTarget(visibleOnly: true);
        if (ai.Graph?.NodeAt(ai.CurrentNode) is { } doorNode && (doorNode.Criteria & Igi2AiGraph.DoorCriteria) != 0)
        {
            return;
        }

        int stance = context.Stance;
        context.PreviousStance = stance;
        if (item.ChildRun == (Igi2AiChildCallback)ReloadWeapon && stance == 2)
        {
            stance = 1;
        }

        if (stance is 0 or 1)
        {
            ai.SetChannel(stance == 0 ? 2 : 1, 1f);
        }

        if (target is null)
        {
            return;
        }

        double rise;
        switch (context.Trace)
        {
            case 0:
                rise = 0.0;
                break;
            case 1:
                rise = 2048.0;
                break;
            case 2 or 3:
                return;
            default:
                return;
        }

        Vector3d at = ai.Soldier.Position;
        Vector3d start = new(at.X, at.Y, rise + at.Z);
        Vector3d aimed = target.DetectablePosition;
        Vector3d end = new(aimed.X, aimed.Y, aimed.Z + 2048.0);
        float distance = Igi2AiMath.Distance(at, target.DetectablePosition);
        bool clear = ai.World.Detection is not { } sight || sight.LineOfSight(start, end, 0x18000000u, out float transparency)
            || transparency >= 0.800000011920929f;
        if (clear)
        {
            if (context.Trace != 0)
            {
                if (context.Trace == 1)
                {
                    context.Trace = 3;
                    context.Stance = distance <= 28672f ? 2 : 1;
                }

                return;
            }

            if (distance > 122880f)
            {
                context.Trace = 3;
                context.Stance = 0;
                return;
            }
        }
        else if (context.Trace != 0)
        {
            if (context.Trace == 1)
            {
                context.Trace = 3;
            }

            return;
        }

        context.Trace = 1;
    }

    // --- Activating (HumanAIContent_Activate*) ---------------------------------------------------------------------------------

    /// <summary>
    /// Goes to use something (<c>HumanAIContent_ActivateInitCB</c>): the squad's alarm trigger counts its control too; it is given up
    /// at once if another soldier uses it (or the control), the control is ringing, it is no placed object, or no node is near; else
    /// the soldier makes for the node nearest it, or the next one out from there when that node is its first; the thing (and the
    /// control) is marked in use and the soldier activating.
    /// </summary>
    public static void ActivateInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ActivateContext context = item.ContextAs<ActivateContext>();
        item.Run = ActivateRun;
        item.Delete = ActivateDelete;
        Igi2AiWorld world = ai.World;
        object? control = null;
        bool isAlarm = false;
        if (ai.Squad is { } squad && context.TaskId == squad.Task.AlarmTriggerId)
        {
            if ((uint)squad.Task.AlarmControlId >= 0x1000u || world.TaskById(squad.Task.AlarmControlId) is not { } found)
            {
                context.Node = -1;
                Done(ai, item);
                return;
            }

            control = found;
            isAlarm = true;
        }

        if ((uint)context.TaskId >= 0x1000u || world.TaskById(context.TaskId) is not { } thing)
        {
            context.Node = -1;
            Done(ai, item);
            return;
        }

        context.Node = -1;
        context.NextNode = -1;
        context.IsActivating = false;
        if (world.Activations.IsInUse(thing)
            || (isAlarm && world.Activations.IsInUse(control!))
            || control is Igi2AlarmControlTask { IsAlarm: true }
            || world.TaskPosition(context.TaskId) is not { } position
            || ai.Graph is not { } graph
            || graph.Nearest(position, false) is not { } closest)
        {
            Done(ai, item);
            return;
        }

        int next = -1;
        if (ai.RecurseNodesFromNode(closest, 10, node =>
        {
            next = node.Id;
            return true;
        }))
        {
            if (closest.Id == next)
            {
                context.Node = next;
            }
            else
            {
                context.Node = closest.Id;
                context.NextNode = next;
            }
        }

        ai.ActivationEntry = world.Activations.Mark(thing, ai);
        if (isAlarm)
        {
            ai.ControlActivationEntry = world.Activations.Mark(control!, ai);
        }

        ai.Flags |= 8;
    }

    /// <summary>
    /// Walks or runs to the thing and uses it (<c>HumanAIContent_ActivateRunCB</c>): a switch pressed within the last second is left;
    /// at the node it turns to the thing and presses (holding the press through the switch state) unless it is the squad's gun, then
    /// tells the others the thing is used, and, for the gun, mans it.
    /// </summary>
    public static void ActivateRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ActivateContext context = item.ContextAs<ActivateContext>();
        Igi2HumanSoldier soldier = ai.Soldier;
        object? thing = (uint)context.TaskId < 0x1000u ? ai.World.TaskById(context.TaskId) : null;
        if (thing is Igi2SwitchTask pressed && ai.World.LevelTick() - pressed.LastPressedTick < 30)
        {
            Done(ai, item);
            return;
        }

        if (item.ChildRun is { } child && child(ai, item))
        {
            item.ChildRun = null;
        }

        if (context.IsWalkRun)
        {
            soldier.ScaleAnimationTimeStep(1.2999999523162842f);
            ai.MakeRun();
        }
        else
        {
            ai.MakeWalk();
        }

        if (context.Node == -1)
        {
            if (context.NextNode == -1)
            {
                Done(ai, item);
            }

            return;
        }

        if (!ai.AttackMoveToNode(context.Node))
        {
            return;
        }

        int task = context.TaskId;
        Vector3d at = ai.World.TaskPosition(task) ?? soldier.Position;
        if (!ai.FacePositionSelectSpeed(at, 0.1745329350233078f, level: true))
        {
            return;
        }

        int gun = ai.Squad?.Task.StationaryGunId ?? -1;
        if (task != gun)
        {
            if (!context.IsActivating)
            {
                ai.SetChannel(10, task);
                if (ai.IsState(6))
                {
                    context.IsActivating = true;
                }

                return;
            }

            if (ai.IsState(6))
            {
                return;
            }
        }

        ai.NotifyTaskHasBeenActivated(task);
        ai.SetChannel(4, task);
        context.Node = -1;
        Done(ai, item);
        if (task == gun)
        {
            Igi2AiActionItem man = NewItem(ai, new NodeContext(), 1, 0, ai.Events.RankOf(26), CombatStationaryGunInit);
            ai.AddAction(man);
        }
    }

    /// <summary>Releases the thing and the control and stops activating, <c>HumanAIContent_ActivateDeleteCB</c>.</summary>
    public static void ActivateDelete(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (ai.ActivationEntry is { } entry)
        {
            ai.World.Activations.Release(entry);
            ai.ActivationEntry = null;
        }

        if (ai.ControlActivationEntry is { } control)
        {
            ai.World.Activations.Release(control);
            ai.ControlActivationEntry = null;
        }

        ai.Flags &= ~8;
    }

    // --- Patrol (HumanAIContent_Patrol*) ---------------------------------------------------------------------------------------

    /// <summary>
    /// Starts a patrol (<c>HumanAIContent_PatrolInitCB</c>): with the idle view; a patrol already running whose command lies on the
    /// new path resumes there; otherwise the path is pushed from the command asked, or the action is given up when it has none.
    /// </summary>
    public static void PatrolInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        PatrolContext context = item.ContextAs<PatrolContext>();
        context.LookTicks = 0;
        ai.UseIdleView();
        item.Run = PatrolRun;
        item.Delete = PatrolDelete;
        if (ai.Patrols.Top is { Path: { } path, Current: { } current } top)
        {
            int index = path.Commands.ToList().IndexOf(current);
            if (index >= 0)
            {
                top.Init(ai.World.PatrolPath(context.PatrolPathId), index);
                return;
            }

            ai.Patrols.Pop();
        }

        Igi2PatrolControl pushed = new();
        pushed.Init(ai.World.PatrolPath(context.PatrolPathId), context.StartIndex);
        if (pushed.Path is not null)
        {
            ai.Patrols.Push(pushed);
        }
        else
        {
            Done(ai, item);
        }
    }

    /// <summary>
    /// A tick of the patrol (<c>HumanAIContent_PatrolRunCB</c>): the current command runs (an animation, a delay, a walk or run to
    /// a node, a crouch, a look at a node, a quit, a wait for a person to pass in front or for an expression, an upper-body
    /// animation); once it is done the patrol steps on and the soldier is told <c>AIEVENT_PATROL_COMMAND_DONE</c> (32), the
    /// forward channel held when the next command walks or runs.
    /// </summary>
    public static void PatrolRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        PatrolContext context = item.ContextAs<PatrolContext>();
        if (ai.Patrols.Top is not { Current: { } command } control)
        {
            return;
        }

        Igi2PatrolPathCommandTask? next = control.Next;
        if (item.ChildRun is { } child && child(ai, item))
        {
            item.ChildRun = null;
        }

        if (control.IsCrouch)
        {
            ai.SetChannel(1, 1f);
        }

        int parameter = command.Parameter;
        bool advance;
        switch (command.Command)
        {
            case Igi2PatrolCommand.Animation:
                if (control.UserModify == -1)
                {
                    control.UserModify = 0;
                    ai.SetChannel(3, parameter);
                    return;
                }

                advance = control.IsCrouch ? ai.IsState(2) : ai.IsState(0);
                break;
            case Igi2PatrolCommand.Delay:
                int tick = ai.World.LevelTick();
                if (control.UserModify == -1)
                {
                    control.UserModify = parameter + tick;
                }

                advance = tick > control.UserModify;
                break;
            case Igi2PatrolCommand.WalkTo or Igi2PatrolCommand.RunTo:
                bool walking = command.Command == Igi2PatrolCommand.WalkTo;
                control.IsCrouch = false;
                control.IsWalk = walking;
                bool arrived = walking ? ai.PatrolMoveToNode(parameter) : ai.AttackMoveToNode(parameter);
                if ((ai.PathSearch.Flags & 1) != 0)
                {
                    ai.Channels[4] = 0f;
                    advance = true;
                    break;
                }

                if (walking)
                {
                    ai.MakeWalk();
                }
                else
                {
                    ai.MakeRun();
                }

                advance = arrived;
                break;
            case Igi2PatrolCommand.Crouch:
                control.IsCrouch = true;
                advance = true;
                break;
            case Igi2PatrolCommand.LookAtNode:
                if (ai.Graph?.NodeAt(parameter) is not { } node)
                {
                    advance = true;
                    break;
                }

                context.LookTicks = ai.FacePositionSelectSpeed(node.Position, 0.05235987901687622f, level: true) ? context.LookTicks + 1 : 0;
                if (next is null)
                {
                    StepPatrol(ai, control);
                    return;
                }

                advance = next.Command == Igi2PatrolCommand.Delay || context.LookTicks > 60;
                if (!advance)
                {
                    return;
                }

                break;
            case Igi2PatrolCommand.Quit:
                Done(ai, item);
                return;
            case Igi2PatrolCommand.WaitForPerson:
                if (ai.World.TaskById(parameter) is not Igi2Human person || person.IsDead)
                {
                    advance = true;
                    break;
                }

                float[] o = ai.DetectionView.Orientation;
                Vector3d at = ai.Soldier.Position;
                advance = (((person.Position.Y - at.Y) * o[4]) + ((person.Position.X - at.X) * o[1])) >= 0.0;
                break;
            case Igi2PatrolCommand.WaitForExpression:
                advance = ai.World.EvaluateCommand(command);
                break;
            case Igi2PatrolCommand.SetUpperBodyAnim:
                Igi2HumanSoldier soldier = ai.Soldier;
                if (command.Text.Length != 0)
                {
                    if ((soldier.UpperBodyFlags & 1) != 0)
                    {
                        soldier.UpperBodyFlags &= ~1;
                        soldier.ClearUpperBodyAnimation(0f);
                    }

                    soldier.SetUpperBodyAnimation(command.Text, 0.5f);
                }
                else
                {
                    soldier.ClearUpperBodyAnimation(0.5f);
                    soldier.UpperBodyFlags |= 1;
                    soldier.ClearUpperBodyAnimation(0f);
                }

                advance = true;
                break;
            default:
                return;
        }

        if (!advance)
        {
            return;
        }

        if (next is { Command: Igi2PatrolCommand.WalkTo or Igi2PatrolCommand.RunTo })
        {
            ai.Channels[4] = 1f;
        }

        StepPatrol(ai, control);
    }

    private static void StepPatrol(Igi2HumanAi ai, Igi2PatrolControl control)
    {
        control.StepForward();
        control.UserModify = -1;
        ai.Receive(new Igi2AiEvent(32, 0, 0, 0f, null, default, null));
    }

    /// <summary>Pops the patrol, <c>HumanAIContent_PatrolDeleteCB</c>.</summary>
    public static void PatrolDelete(Igi2HumanAi ai, Igi2AiActionItem item) => ai.Patrols.Pop();

    // --- Taking cover (HumanAIContent_TakeCover*) ------------------------------------------------------------------------------

    /// <summary>
    /// Picks the nearest free cover within ten metres that covers from the danger (its position plus its direction; the soldier's
    /// own place when none was given), noting the sector facing it and holding the cover (<c>HumanAIContent_TakeCoverInitCB</c>).
    /// </summary>
    public static void TakeCoverInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        CoverContext context = item.ContextAs<CoverContext>();
        Vector3d at = ai.Soldier.Position;
        Vector3d check = context.Position.X == 0.0 && context.Position.Y == 0.0 && context.Position.Z == 0.0
            ? at
            : new Vector3d(
                context.Direction.X + context.Position.X, context.Direction.Y + context.Position.Y, context.Direction.Z + context.Position.Z);
        Igi2AiCoverItem? best = null;
        float bestDistance = 0f;
        if (ai.Graph is { } graph)
        {
            foreach (Igi2AiCoverItem cover in graph.Cover)
            {
                if (graph.NodeAt(cover.NodeId) is not { } node)
                {
                    break;
                }

                context.Sector = 0;
                Vector3d p = node.Position;
                double distance = Math.Sqrt(((at.X - p.X) * (at.X - p.X)) + ((at.Y - p.Y) * (at.Y - p.Y)) + ((at.Z - p.Z) * (at.Z - p.Z)));
                if (distance < 40960.0 && (cover.Flags & 1) == 0 && graph.ProvidesCover(cover, check)
                    && (best is null || (float)distance < bestDistance))
                {
                    bestDistance = (float)distance;
                    best = cover;
                    context.Sector = Igi2AiGraph.CoverSector(p, check);
                }
            }
        }

        context.Cover = best;
        if (best is not null)
        {
            best.Flags |= 1;
        }

        item.Run = TakeCoverRun;
    }

    /// <summary>
    /// Runs to the cover and faces the danger, kneeling unless that sector of the cover forbids it; with no cover it faces the
    /// danger where it stands (<c>HumanAIContent_TakeCoverRunCB</c>).
    /// </summary>
    public static void TakeCoverRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        CoverContext context = item.ContextAs<CoverContext>();
        if (context.Cover is { } cover)
        {
            ai.MakeRun();
            if (ai.AttackMoveToNode(cover.NodeId))
            {
                ai.ClearMovementQueue();
                if (ai.FacePositionSelectSpeed(context.Position, 0.1745329350233078f, level: true) && (cover.CoverCircle[context.Sector] & 4) == 0)
                {
                    ai.SetChannel(1, 1f);
                }
            }
        }
        else
        {
            ai.ClearMovementQueue();
            ai.SelectTurnSpeed();
            ai.FacePosition(context.Position, 0.1745329350233078f, level: true);
        }
    }

    /// <summary>Nothing, <c>HumanAIContent_TakeCoverDeleteCB</c>.</summary>
    public static void TakeCoverDelete(Igi2HumanAi ai, Igi2AiActionItem item)
    {
    }

    /// <summary>Whether a cover taken leaves the soldier free to crouch, the danger's check on the take-cover action.</summary>
    /// <param name="item">The take-cover action.</param>
    /// <returns><see langword="false"/> when its sector forbids kneeling.</returns>
    public static bool CoverAllowsCrouch(Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        CoverContext context = item.ContextAs<CoverContext>();
        return context.Cover is not { } cover || (cover.CoverCircle[context.Sector] & 4) == 0;
    }

    // --- Rough look (HumanAIContent_LookInApproxDirection*) --------------------------------------------------------------------

    /// <summary>
    /// Picks a point five metres away in the event's sector of the circle, at a drawn place within it
    /// (<c>HumanAIContent_LookInApproxDirectionInitCB</c>).
    /// </summary>
    public static void LookInApproxDirectionInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ApproxContext context = item.ContextAs<ApproxContext>();
        Vector3d at = ai.Soldier.Position;
        double dx = context.Position.X - at.X, dy = context.Position.Y - at.Y;
        float dz = (float)(context.Position.Z - at.Z);
        double gamma = Igi2Pose.CalcGamma(dx, dy);
        if (gamma < 0.0)
        {
            gamma += 6.2831854820251465;
        }

        int sector = Fpu.ToInt32((double)context.SectorResolution * gamma * 0.15915494309189526);
        double angle = (((double)(ushort)ai.World.Random.NextRaw() * IgiRandom.Scale) + sector) * 6.283185307179589 / context.SectorResolution;
        double s = Math.Sin(angle), c = Math.Cos(angle);
        float zero = (float)(dz * 0.0);
        float x = (float)((c * 0.0) + (-s * 20480.0) + zero);
        double y = (c * 20480.0) + (s * 0.0) + zero;
        context.Position = new Vector3d(x + at.X, y + at.Y, dz + at.Z);
        item.Run = LookInApproxDirectionRun;
    }

    /// <summary>
    /// Aims at and turns to the point (unless ranked as a plain sighting); once facing it, after a second it plays its stand look
    /// and lowers its aim; done when the time is up or a target shows, telling itself <c>AIEVENT_LOOKAT_DONE</c> (40) when none did
    /// (<c>…RunCB</c>).
    /// </summary>
    public static void LookInApproxDirectionRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ApproxContext context = item.ContextAs<ApproxContext>();
        if (item.Priority != ai.Events.RankOf(4))
        {
            ai.LastTargetPosition = context.Position;
            ai.TargetPosition = context.Position;
            ai.SetChannel(5, 1f);
        }

        ai.SelectTurnSpeed();
        if (!ai.FacePosition(context.Position, FineTolerance, level: true))
        {
            return;
        }

        int left = context.TimeLeft;
        if (left <= 0 || ai.GetTarget(visibleOnly: true) is not null)
        {
            if (ai.GetTarget(visibleOnly: true) is null)
            {
                ai.Receive(new Igi2AiEvent(40, 0, 0, 0f, null, default, null));
            }

            Done(ai, item);
            return;
        }

        int elapsed = context.StartTime - (left - 1);
        context.TimeLeft = left - 1;
        if (elapsed > 30)
        {
            ai.StandActionAnimation = "standdble";
            ai.SetChannel(3, 1f);
            ai.SetChannel(5, 0f);
        }
    }

    /// <summary>With no target, lowers the eyes and forgets the last target, <c>…DeleteCB</c>.</summary>
    public static void LookInApproxDirectionDelete(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (ai.GetTarget(visibleOnly: true) is null)
        {
            ai.Channels[29] = 0f;
            ai.Channels[30] = 0f;
            ai.LastTargetPosition = default;
        }
    }

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

    // --- Offensive and defensive combat (HumanAIContent_OffensiveCombat*, …DefensiveCombat*) --------------------------------

    /// <summary>No node chosen, <c>HumanAIContent_OffensiveCombatInitCB</c>.</summary>
    public static void OffensiveCombatInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.ContextAs<NodeContext>().Node = -1;
        item.RunInit = OffensiveCombatInit;
        item.Run = OffensiveCombatRun;
    }

    /// <summary>
    /// Attacks (<c>HumanAIContent_OffensiveCombatRunCB</c>): runs its burst; faces a target in sight; with none, makes for the node
    /// nearest the nearest live enemy listed (done with none), running there.
    /// </summary>
    public static void OffensiveCombatRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        if (item.ChildRun is { } child)
        {
            if (child(ai, item))
            {
                item.ChildRun = null;
            }

            return;
        }

        if (ai.GetTarget(visibleOnly: true) is not null)
        {
            context.Node = -1;
            ai.FaceTarget();
            return;
        }

        if (context.Node == -1)
        {
            IIgi2Detectable? enemy = ai.GetTarget(visibleOnly: true) ?? ai.NearestLiveEnemyListed();
            if (enemy is not null && ai.Graph?.Nearest(enemy.DetectablePosition, false) is { } node)
            {
                context.Node = node.Id;
            }
            else
            {
                Done(ai, item);
            }

            return;
        }

        ai.MakeRun();
        if (ai.AttackMoveToNode(context.Node))
        {
            Done(ai, item);
        }
    }

    /// <summary>Holds, <c>HumanAIContent_DefensiveCombatInitCB</c>.</summary>
    public static void DefensiveCombatInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.RunInit = DefensiveCombatInit;
        item.Run = DefensiveCombatRun;
    }

    /// <summary>Runs its burst, or faces a target or the nearest live enemy listed, done with none (<c>…RunCB</c>).</summary>
    public static void DefensiveCombatRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (item.ChildRun is { } child)
        {
            if (child(ai, item))
            {
                item.ChildRun = null;
            }

            return;
        }

        if (ai.GetTarget(visibleOnly: true) is not null || ai.NearestLiveEnemyListed() is not null)
        {
            ai.FaceTarget();
        }
        else
        {
            Done(ai, item);
        }
    }

    // --- Grenade (HumanAIContent_CombatStandFireGrenade*) ----------------------------------------------------------------------

    /// <summary>
    /// Throws its grenade at the last target, between seven and forty metres off and not within seven metres of a live friend there;
    /// it throws once the throw has begun and it faces the target (<c>HumanAIContent_CombatStandFireGrenadeInitCB</c>).
    /// </summary>
    public static void CombatStandFireGrenadeInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (!ai.HasGrenade)
        {
            Done(ai, item);
            return;
        }

        Igi2HumanSoldier soldier = ai.Soldier;
        Vector3d target = ai.LastTargetPosition;
        Vector3d at = soldier.Position;
        float dx = (float)(at.X - target.X), dy = (float)(at.Y - target.Y);
        double dz = at.Z - target.Z;
        double distance = Math.Sqrt((dz * dz) + (dx * (double)dx) + (dy * (double)dy));
        if (distance < 28672.0 || distance > 163840.0 || target == default)
        {
            Done(ai, item);
            return;
        }

        foreach (Igi2Human human in ai.World.Humans)
        {
            if (ReferenceEquals(human, soldier) || human is not Igi2HumanSoldier { IsActive: true } friend || friend.IsDead || friend.Team != soldier.Team)
            {
                continue;
            }

            Vector3d p = friend.Position;
            if (Math.Abs(p.X - target.X) < 28672.0 && Math.Abs((float)(p.Y - target.Y)) < 28672.0 && Math.Abs((float)(p.Z - target.Z)) < 28672.0)
            {
                Done(ai, item);
                return;
            }
        }

        ai.SetChannel(7, 1f);
        item.Run = soldier.Locomotion.CurrentState == Igi2SoldierState.StandThrowGrenade
            && ai.FacePositionSelectSpeed(target, 0.1745329350233078f, level: true)
            ? CombatStandFireGrenadeRun
            : CombatStandFireGrenadeInit;
    }

    /// <summary>Done once the throw ends, standing or aiming again, <c>…RunCB</c>.</summary>
    public static void CombatStandFireGrenadeRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (ai.Soldier.Locomotion.CurrentState is 0 or 15)
        {
            Done(ai, item);
        }
    }

    // --- Standing fire and close combat (HumanAIContent_CombatStandFireRunCB, …CloseCombatRunCB) -------------------------------

    /// <summary>Aims at the target and fires, <c>HumanAIContent_CombatStandFireRunCB</c>.</summary>
    /// <returns><see langword="true"/> once fired.</returns>
    public static bool CombatStandFire(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ai.TargetPosition = ai.GetTargetPosition(out _);
        return RunFire(ai);
    }

    /// <summary>Strikes, done once the soldier is out of its stand, <c>HumanAIContent_CombatStandCloseCombatRunCB</c>.</summary>
    /// <returns><see langword="true"/> once struck.</returns>
    public static bool CombatStandCloseCombat(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ai.SetChannel(8, 1f);
        return !ai.IsState(0);
    }

    // --- Mounted gun (HumanAIContent_CombatStationaryGun*) ---------------------------------------------------------------------

    /// <summary>Mans the gun, <c>HumanAIContent_CombatStationaryGunInitCB</c>.</summary>
    public static void CombatStationaryGunInit(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        item.Run = CombatStationaryGunRun;
        item.RunInit = CombatStationaryGunInit;
        item.Delete = CombatStationaryGunDelete;
        item.ContextAs<NodeContext>().Timer = 0;
    }

    /// <summary>
    /// Fights from the gun (<c>HumanAIContent_CombatStationaryGunRunCB</c>): leaves it when it is wrecked or after five seconds with
    /// no target; runs its burst; and settles into the gun's stand.
    /// </summary>
    public static void CombatStationaryGunRun(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        NodeContext context = item.ContextAs<NodeContext>();
        if (ai.Soldier.MountedGun is not { } gun)
        {
            Done(ai, item);
            return;
        }

        if (ai.GunnerId == -1)
        {
            ai.GunnerId = gun.TaskId;
        }

        if (gun.IsWrecked || context.Timer > 150)
        {
            Done(ai, item);
            return;
        }

        context.Timer = ai.GetTarget(visibleOnly: true) is not null ? 0 : context.Timer + 1;
        if (item.ChildRun is { } child)
        {
            if (child(ai, item))
            {
                item.ChildRun = null;
            }
        }
        else if (!ai.IsState(5) && ai.IsState(4))
        {
            ai.SetChannel(3, -1f);
        }
    }

    /// <summary>Steps off the gun and lets its trigger go, <c>HumanAIContent_CombatStationaryGunDeleteCB</c>.</summary>
    public static void CombatStationaryGunDelete(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        if (ai.Soldier.MountedGun is { } gun)
        {
            if (ai.IsState(5))
            {
                ai.SetChannel(3, 1f);
            }

            gun.ReleaseTrigger();
        }

        ai.GunnerId = -1;
    }
}
