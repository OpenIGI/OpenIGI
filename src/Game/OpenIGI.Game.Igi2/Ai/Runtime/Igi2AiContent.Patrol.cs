using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>The using and walking-about actions: activating, the patrol, taking cover and the rough look.</content>
public static partial class Igi2AiContent
{
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
            int index = path.IndexOfCommand(current);
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
                if (ai.World.TaskById(parameter) is not IAiHuman person || person.IsDead)
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
}
