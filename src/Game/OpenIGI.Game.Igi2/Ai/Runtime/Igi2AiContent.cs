using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// The soldiers' actions, <c>humanaicontent.c</c>: each action's callbacks (its Init, Run, Delete and the sub-behaviours it runs
/// inside itself), the orders a squad gives, and the firing they share.
/// </summary>
/// <remarks>
/// An action is recognised by its Init, as the original compares <c>pfInitCallback</c>; the callbacks are static methods so that
/// two delegates made from one compare equal.
/// </remarks>
public static partial class Igi2AiContent
{
    /// <summary>The soldier states the content asks after, <c>HumanAIContent_apnStateList</c>: one list for every AI type.</summary>
    private static readonly int[] States = [0, 1, 27, 36, 84, 85, 100];

    /// <summary>The channels the content writes, <c>HumanAIContent_apnChannelList</c>: walk, kneel, lie, stand action, activate, aim, select weapon, throw, close combat, turn, press switch.</summary>
    private static readonly int[] Channels = [23, 24, 25, 26, 27, 30, 31, 32, 33, 34, 35];

    /// <summary>Gets the soldier channel a content channel writes, <c>HumanAIContent_GetHumanChannelFromContentChannel</c>.</summary>
    /// <param name="contentChannel">The content channel.</param>
    /// <returns>The soldier channel.</returns>
    public static int HumanChannel(int contentChannel) => Channels[contentChannel];

    /// <summary>Gets the soldier state a content state stands for.</summary>
    /// <param name="contentState">The content state.</param>
    /// <returns>The soldier state.</returns>
    public static int SoldierState(int contentState) => States[contentState];

    /// <summary>Pops an action as its own callbacks do: its Delete, then it and its state go.</summary>
    private static void Done(Igi2HumanAi ai, Igi2AiActionItem item) => ai.Actions.Pop(ai, item);

    /// <summary>Takes a fresh action with a state, <c>HumanAI_GetFreeActionStackItem</c> and <c>HumanAI_CreateActionContext</c>.</summary>
    private static Igi2AiActionItem NewItem(Igi2HumanAi ai, object? context, int flags, int delay, int priority, Igi2AiActionCallback init)
    {
        Igi2AiActionItem item = ai.Actions.Take();
        if (context is not null && ai.Actions.TakeContext())
        {
            item.Context = context;
        }

        item.Flags = flags;
        item.Delay = delay;
        item.Priority = priority;
        item.Init = init;
        return item;
    }

    /// <summary>
    /// Acts on a squad's order (<c>HumanAIContent_ReceiveSquadOrder</c>): each order becomes an action, most after clearing the
    /// stack, ranked at a move (<c>AIEVENT_LEADER_ORDER</c>, 28) unless the order sets a rank, then added.
    /// </summary>
    /// <param name="ai">The soldier's mind.</param>
    /// <param name="order">The order.</param>
    public static void ReceiveSquadOrder(Igi2HumanAi ai, Igi2AiSquadOrder order)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(order);
        int moveRank = ai.Events.RankOf(28);
        Igi2AiActionItem? top = ai.Actions.Top;
        switch (order.Kind)
        {
            case Igi2AiSquadOrderKind.LookAtPosition:
            {
                LookContext context = new() { Position = order.Position };
                Igi2AiActionItem item = NewItem(ai, context, 0, IsLooking(top) ? top!.Delay : order.Delay, 0, LookAtPositionInit);
                ai.Actions.Clear(ai);
                item.Priority = ai.Events.RankOf(46);
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.LookAtEvent:
            {
                if (top is not null && top.Priority < order.Timer)
                {
                    return;
                }

                LookContext context = new() { Position = order.Position };
                Igi2AiActionItem item = NewItem(ai, context, 0, IsLooking(top) ? top!.Delay : order.Delay, order.Timer, LookAtEventInit);
                ai.Actions.Clear(ai);
                float share = ai.World.Random.NextSingle();
                context.SecondsToLook = ((((Fpu)share * 0.20000000298023224) + 0.800000011920929) * 15.0).F;
                context.StartTime = 0;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.LookInApproxDirection:
            {
                int rank = (int)order.Direction.Z;
                // A rough look already running, or a more important action, keeps its place.
                if (top is not null && (top.Init == (Igi2AiActionCallback)LookInApproxDirectionInit || top.Priority < rank))
                {
                    return;
                }

                float share = (((Fpu)ai.World.Random.NextSingle() * 0.20000000298023224) + 0.800000011920929).F;
                ai.Actions.Clear(ai);
                ApproxContext context = new()
                {
                    Position = order.Position,
                    SectorResolution = order.SectorResolution,
                    TimeLeft = Fpu.ToInt32((double)order.Timer * share),
                    StartTime = Fpu.ToInt32((double)order.Timer * share),
                };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, rank, LookInApproxDirectionInit);
                item.Run = LookInApproxDirectionRun;
                item.Delete = LookInApproxDirectionDelete;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.MoveToNode:
            {
                NodeContext context = new() { Node = order.Node, IsRun = order.IsRun };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, MoveToNodeSquadOrderInit);
                item.Run = MoveToNodeSquadOrderRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.Fire:
                ai.IsAllowedToFire = true;
                FireOnTarget(ai);
                return;
            case Igi2AiSquadOrderKind.Formation:
            {
                FormationContext context = new()
                {
                    RelativePosition = order.RelativePosition,
                    RelativeDirection = order.RelativeDirection,
                    Leader = order.Leader,
                };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, MoveInFormationInit);
                item.Delete = MoveInFormationDelete;
                item.Run = MoveInFormationRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.Inquire:
            {
                NodeContext context = new() { Position = order.Position };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, InquireSquadOrderInit);
                item.Run = InquireSquadOrderRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.WaitForLeapfrog:
            {
                NodeContext context = new() { Position = order.Position };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, WaitForLeapfrogInit);
                item.Run = WaitForLeapfrogRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.Activate:
            {
                ActivateContext context = new() { TaskId = order.Node, IsWalkRun = true, Node = -1, NextNode = -1 };
                Igi2AiActionItem item = NewItem(ai, context, 1, 0, ai.Events.RankOf(21), ActivateInit);
                item.Delete = ActivateDelete;
                item.Run = ActivateRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.Patrol:
            {
                PatrolContext context = new() { PatrolPathId = order.Node, StartIndex = 0 };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, PatrolInit);
                item.Delete = PatrolDelete;
                item.Run = PatrolRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.ThrowGrenade:
                if (ai.HasGrenade)
                {
                    Igi2AiActionItem item = NewItem(ai, null, 1, 0, ai.Events.RankOf(21), CombatStandFireGrenadeInit);
                    item.Run = CombatStandFireGrenadeRun;
                    item.SquadOrder = (int)order.Kind;
                    ai.AddAction(item);
                }

                return;
            case Igi2AiSquadOrderKind.TakeCover:
            {
                CoverContext context = new() { Position = order.Position, Direction = order.Direction };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, TakeCoverInit);
                item.Delete = TakeCoverDelete;
                item.Run = TakeCoverRun;
                item.SquadOrder = (int)order.Kind;
                ai.AddAction(item);
                return;
            }

            case Igi2AiSquadOrderKind.VisualSweep:
            {
                LookAroundContext context = new() { IsCrouch = order.IsCrouch };
                Igi2AiActionItem item = NewItem(ai, context, 0, order.Delay, moveRank, LookAroundInit);
                item.Run = LookAroundRun;
                ai.World.VisualSweepPattern = (ai.World.VisualSweepPattern + 1) % 6;
                ai.AddAction(item);
                return;
            }
        }
    }

    private static bool IsLooking(Igi2AiActionItem? top) =>
        top is not null
        && (top.Init == (Igi2AiActionCallback)LookAtPositionInit || top.Init == (Igi2AiActionCallback)LookAtEventInit
            || top.Init == (Igi2AiActionCallback)LookInApproxDirectionInit);

    /// <summary>
    /// Readies a burst (<c>HumanAIContent_InitFire</c>): from a mounted gun the eyes are its; a grenade in hand marks the shot as one
    /// thrown; the aim goes to the target.
    /// </summary>
    internal static void InitFire(Igi2HumanAi ai)
    {
        if (ai.IsState(5) && ai.Soldier.SeatView is { } seat)
        {
            ai.View.ViewOrientation = [.. seat.Orientation];
            ai.View.ViewPosition = seat.Position;
        }

        if (ai.ActiveWeaponType is { } weaponType)
        {
            if (weaponType.WeaponType == Igi2WeaponConstants.WeaponTypeGrenade)
            {
                ai.Soldier.WeaponHitProbability = 1f;
            }

            ai.AimPosition = ai.TargetPosition;
        }
    }

    /// <summary>
    /// Fires at the target (<c>HumanAIContent_RunFire</c>): with a weapon ready and fewer than three rounds pending, it aims, turns
    /// to the aim, and once it faces it marks the shot's reach to the target plus five metres and sets the weapon's hit chance from
    /// its accuracy at that range (rising to double over eight seconds of seeing the target), then fires.
    /// </summary>
    /// <returns><see langword="true"/> once it fired or cannot.</returns>
    internal static bool RunFire(Igi2HumanAi ai)
    {
        if (ai.IsState(5) && ai.Soldier.SeatView is { } seat)
        {
            ai.View.ViewOrientation = [.. seat.Orientation];
            ai.View.ViewPosition = seat.Position;
        }

        if (ai.ActiveWeaponType is not { } weaponType || ai.Soldier.Weapon.Firing is not { } firing)
        {
            return true;
        }

        ai.AimPosition = ai.TargetPosition;

        // Only a gun, a pistol or a shotgun is fired this way.
        if (weaponType.WeaponType > Igi2WeaponConstants.WeaponTypeShotgun)
        {
            return true;
        }

        ai.SetChannel(5, 1f);
        if (!ai.FacePositionSelectSpeed(ai.AimPosition, 1.2217304706573486f, level: true))
        {
            return false;
        }

        float distance = Igi2AiMath.Distance(ai.AimPosition, ai.Soldier.Position);
        firing.GunShot.Options |= (Igi2GunShotOptions)3;
        firing.GunShot.RangeLimit = (((Fpu)distance * 0.000244140625) + 5.0).F;
        float accuracy = Igi2HumanAiConfig.CalculateAccuracy(
            ai.Tweak(Igi2AiTweakIndex.MinRangeAccuracy),
            ai.Tweak(Igi2AiTweakIndex.MedRangeAccuracy),
            ai.Tweak(Igi2AiTweakIndex.AccuracyMedRange),
            ai.Tweak(Igi2AiTweakIndex.AccuracyMaxRange),
            ai.Tweak(Igi2AiTweakIndex.MedRangeAccuracyFactor),
            ai.Tweak(Igi2AiTweakIndex.MaxRangeAccuracyFactor),
            distance);
        double chance = accuracy;
        if (ai.GetTarget(visibleOnly: true) is { } target && ai.Detection.Find(target) is { } item)
        {
            double seen = ai.World.LevelTick() - item.BecameVisibleTick;
            seen = seen >= 240.0 ? 240.0 : seen;
            chance = (((Fpu)(double)(int)seen * 0.004166666883975267 + 1.0) * accuracy).Value;
        }

        firing.HitProbability = (float)(chance >= 0.0 ? (chance > 1.0 ? 1.0 : chance) : 0.0);
        ai.Channels[6] = 1f;
        return true;
    }

    /// <summary>
    /// Reloads when the clip is empty (<c>HumanAIContent_ReloadWeaponCB</c>), facing the target meanwhile.
    /// </summary>
    /// <returns><see langword="true"/> once loaded.</returns>
    public static bool ReloadWeapon(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(ai);
        if (ai.ActiveWeaponType is null || ai.AmmoInWeapon() != 0)
        {
            return true;
        }

        ai.Channels[6] = 0f;
        ai.Channels[8] = 1f;
        ai.FaceTarget();
        return false;
    }

    /// <summary>
    /// The burst a squad's fire order runs inside an action (<c>HumanAIContent_FireAtTargetPositionCB</c>): a sniper fires only
    /// kneeling or standing; an empty clip reloads; with a target, a rocket soldier holds fire past 110 metres; between bursts the
    /// trigger is let go; a new burst is sized to the clip's share (five to ten, double within ten metres) after a drawn pause; while
    /// allowed, each round is fired.
    /// </summary>
    /// <returns>Always <see langword="false"/>: the burst runs until the action ends, or <see langword="true"/> with no target.</returns>
    public static bool FireAtTargetPosition(Igi2HumanAi ai, Igi2AiActionItem item)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(item);
        int ammo = ai.AmmoInWeapon();
        if (ai.HasSniperScope() && ai.Soldier.Locomotion.CurrentState is not (0 or 27))
        {
            return false;
        }

        if (ammo == 0)
        {
            ai.FireDelayTimer = 0;
            ai.ShotsBeforeDelay = 0;
            item.ChildRun = ReloadWeapon;
            return false;
        }

        ai.SetChannel(5, 1f);
        if (ai.GetTarget(visibleOnly: true) is null || ai.ActiveWeaponType is not { } weaponType)
        {
            return true;
        }

        float distance = Igi2AiMath.Distance(ai.TargetPosition, ai.Soldier.Position);
        if (distance > 450560f && ai.IsRpgSoldier())
        {
            return true;
        }

        if (ai.FireDelayTimer != 0)
        {
            ai.Soldier.ReleaseTrigger();
            ai.FireDelayTimer--;
        }
        else if (ai.ShotsBeforeDelay <= 0)
        {
            ai.IsAllowedToFire = false;
            int burst = Fpu.ToInt32((double)ai.RoundsPerClip() * ai.Tweak(Igi2AiTweakIndex.PercentageFiredPerBurst));
            ai.ShotsBeforeDelay = burst >= 5 ? (burst > 10 ? 10 : burst) : 5;
            double share = ai.World.Random.NextDouble();
            if (weaponType.RoundsInSuccession(0) <= 1)
            {
                ai.FireDelayTimer = Fpu.ToInt32(((0.05000000074505806 * share) + 0.1) * 30.0);
                return false;
            }

            int members = ai.Squad?.MemberCount ?? 0;
            ai.FireDelayTimer = Fpu.ToInt32(((members * share * 0.1) + 0.4000000059604645) * 30.0);
            if (distance < 40960f)
            {
                ai.ShotsBeforeDelay *= 2;
            }
        }
        else if (ai.IsAllowedToFire)
        {
            InitFire(ai);
            if (RunFire(ai))
            {
                ai.ShotsBeforeDelay--;
            }
        }

        return false;
    }

    /// <summary>
    /// Puts the burst inside the top action when a listed enemy is in sight (<c>HumanAIContent_FireOnTarget</c>).
    /// </summary>
    internal static void FireOnTarget(Igi2HumanAi ai)
    {
        if (ai.Actions.Top is not { } top)
        {
            return;
        }

        foreach (Igi2DetectionItem enemy in ai.Detection.Enemies)
        {
            if (enemy.FullVisible[0] || enemy.FullVisible[1] || enemy.FullVisible[2])
            {
                top.ChildRun ??= FireAtTargetPosition;
                return;
            }
        }
    }
}
