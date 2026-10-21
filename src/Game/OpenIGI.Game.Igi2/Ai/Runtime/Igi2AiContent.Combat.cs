using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>
/// The standing and fighting actions: idle, combat idle, offensive and defensive combat, the grenade, standing fire and the
/// mounted gun.
/// </content>
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

        IAiDetectable? target = ai.GetTarget(visibleOnly: true) ?? ai.NearestLiveEnemyListed();
        if (target is IAiHuman human)
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
        IAiDetectable? target = ai.GetTarget(visibleOnly: true);
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
        bool clear = ai.World.Detection is not { } sight || sight.LineOfSight(start, end, 0x18000000u, out float transparency, ai.Soldier)
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
            IAiDetectable? enemy = ai.GetTarget(visibleOnly: true) ?? ai.NearestLiveEnemyListed();
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
