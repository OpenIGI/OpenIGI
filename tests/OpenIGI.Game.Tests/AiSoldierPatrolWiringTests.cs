using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The same end-to-end chain for soldiers that move while they fight: patrols with nowhere to go, alarm
/// runs, stationary guns, doors on the path, and the patrol's own grenade, close-combat and damage
/// children.
/// </summary>
public sealed class AiSoldierPatrolWiringTests : AiSoldierCombatWiringFixture
{
    /// <summary>
    /// A patrol-arm soldier with no node to walk back to still fires instead of repositioning for ever.
    /// </summary>
    /// <remarks>
    /// The chooser's rule (a) walks a soldier back onto its claimed node before anything else. With no
    /// graph — or with an empty route back — that walk can never finish, and the soldier stood there
    /// engaging and never firing ("a guard sees me, stands there and does nothing"). Retail's
    /// RePosition (<c>0x48C760</c>) walks the body onto the node and terminates, so a walk that cannot
    /// progress counts as arrived.
    /// </remarks>
    [RequiresGameDataFact]
    public void APatrolWithNowhereToRepositionStillFires()
    {
        Vector3d position = new(1000 * Metre, 1000 * Metre, 0);
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.PatrolAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_PATROL_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.2f,
                RandomClipFraction = 0.3f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.8f,
            }),
            () => new AiCombatWeaponState(_clip, 30, 30, WeaponCategory.Gun));

        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));

        bool fired = false;
        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
            fired |= soldier.Combat.PullTrigger;
        }

        Assert.True(
            fired,
            $"the patrol never fired (activity {soldier.Activity}, action {soldier.PatrolAction})");
    }

    /// <summary>
    /// A route that breaks off keeps the goal, as <c>0x4518E0</c> does ("Error in graph %d routenet"),
    /// with nothing to walk: the plan reports the failure and the queue is empty.
    /// </summary>
    [RequiresGameDataFact]
    public void AnUnreachableDestinationKeepsTheGoalWithNothingToWalk()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat")));

        (GraphNode From, GraphNode To)? unreachable = null;
        foreach (GraphNode from in graph.Nodes)
        {
            foreach (GraphNode to in graph.Nodes)
            {
                if (from.Id != to.Id && graph.RouteTable!.EnumerateRoute(from.Id, to.Id).Count == 0)
                {
                    unreachable = (from, to);
                    break;
                }
            }

            if (unreachable is not null)
            {
                break;
            }
        }

        Assert.NotNull(unreachable);
        Igi1HumanAi soldier = new(
            graph, unreachable.Value.From.Position, facing: 0f, phase: 0, seed: 1);

        Assert.False(soldier.GoTo(unreachable.Value.To.Id));
        Assert.Equal(unreachable.Value.To.Id, soldier.DestinationNode);
        Assert.Null(soldier.DebugWaypoint);
    }

    /// <summary>
    /// <c>AIAction_Activate</c>: the guard claims the alarm, walks to it, presses it, and releases the
    /// claims — <c>0x48E620</c> / <c>0x48E7D0</c> / <c>0x48E870</c>.
    /// </summary>
    /// <remarks>
    /// The target being the guard's own alarm trigger claims the alarm CONTROL too, so no second guard
    /// starts the same run; and a task already claimed makes the action never start at all.
    /// </remarks>
    [RequiresGameDataFact]
    public void AGuardRunsToItsAlarmPressesItAndReleasesTheClaims()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat")));
        Vector3d start = graph.Nodes[0].Position;
        Vector3d panel = graph.Nodes[1].Position;

        Igi1HumanAi soldier = new(graph, start, facing: 0f, phase: 0, seed: 1);
        soldier.AlarmTriggerId = 500;
        soldier.AlarmControlId = 600;

        AiActivationClaims claims = new();
        int pressed = -1;
        soldier.SetActivateSupport(
            claims,
            (int id, out Vector3d position) =>
            {
                position = panel;
                return id == 500;
            },
            id => pressed = id);

        soldier.ApplyActions([new AiProducedAction("AIAction_Activate", [500, 1, 0], 0)]);

        Assert.True(claims.Contains(500), "the trigger was not claimed at start");
        Assert.True(claims.Contains(600), "the control was not claimed alongside the trigger");

        Vector3d farEnemy = new(start.X, start.Y + (1000 * Metre), start.Z);
        for (int tick = 1; tick <= 5000 && pressed == -1; tick++)
        {
            soldier.Tick(tick, farEnemy, rootMotionThisTick: 200.0);
        }

        Assert.Equal(500, pressed);
        Assert.False(claims.Contains(500), "the trigger claim was not released");
        Assert.False(claims.Contains(600), "the control claim was not released");

        // A second guard told to press an ALREADY-CLAIMED panel never starts the walk.
        claims.Claim(500);
        Igi1HumanAi second = new(graph, start, facing: 0f, phase: 1, seed: 2);
        second.SetActivateSupport(claims, (int id, out Vector3d p) => { p = panel; return true; }, _ => pressed = -2);
        second.ApplyActions([new AiProducedAction("AIAction_Activate", [500, 1, 0], 0)]);
        for (int tick = 1; tick <= 2000; tick++)
        {
            second.Tick(tick, farEnemy, rootMotionThisTick: 200.0);
        }

        Assert.NotEqual(-2, pressed);
    }

    /// <summary>A scripted gunner keeps the emplacement claimed when its activation walk arrives.</summary>
    [RequiresGameDataFact]
    public void AStationaryGunnerMountsAfterItsScriptedActivation()
    {
        GraphFile graph = Graph();
        Vector3d start = graph.Nodes[0].Position;
        Vector3d emplacement = graph.Nodes[1].Position;

        // The stand's position is its pivot, over the floor the gunner stands on: mission 8's M2HBs stand 1.3 m up.
        AiStationaryGun gun = new(500, new Vector3d(emplacement.X, emplacement.Y, emplacement.Z + 5345.0), Matrix3f.Identity);
        AiActivationClaims claims = new();
        Igi1HumanAi soldier = new(graph, start, facing: 0f, phase: 0, seed: 1)
        {
            GunnerId = 500,
        };
        soldier.SetCombat(
            AiType.Gunner,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_GUNNER",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));
        soldier.SetStationaryGunResolver(id => id == 500 ? gun : null);
        soldier.SetActivateSupport(
            claims,
            (int id, out Vector3d position) =>
            {
                position = emplacement;
                return id == 500;
            },
            _ => { });

        // gunner.qsc's COMBAT branch while the gun is free: Activate, then Combat, both pushable. 0x450F10
        // walks the produced list from its tail, adding each at the head, so the walk to the gun is on top
        // and the fight waits beneath it.
        soldier.ApplyActions([
            new AiProducedAction("AIAction_Activate", [500, 1, (int)AiActionFlagWord.Pushable], 5),
            new AiProducedAction("AIAction_Combat", [(int)AiActionFlagWord.Pushable], 5),
        ]);
        Assert.Equal(
            ["AIAction_Activate", "AIAction_Combat"],
            soldier.PendingActions.Actions.Select(action => action.Name));

        // The fight runs, and 0x48D340 takes the gun once the target has held its cone for 30 ticks.
        Vector3d farEnemy = new(start.X, start.Y + (500 * Metre), start.Z);
        for (int tick = 1; tick <= 5000 && !soldier.IsMounted; tick++)
        {
            soldier.Tick(tick, farEnemy, rootMotionThisTick: 200.0);
        }

        Assert.True(soldier.IsMounted, "the gunner never took the emplacement");
        Assert.Same(gun, soldier.MountedGun);

        // The object dispatch seats the body in the use-object state (36) and consumes the mount's slot 24.
        // State 36's entry (0x45C8B0) moves the body in X and Y only and keeps its own height.
        double heightBeforeMount = soldier.Position.Z;
        soldier.TickCombatState(animationDone: false);
        Assert.Equal(HumanSoldierStates.UseObjectState, soldier.CombatState);
        Assert.Equal(gun.MountPosition.X, soldier.Position.X);
        Assert.Equal(gun.MountPosition.Y, soldier.Position.Y);
        Assert.Equal(heightBeforeMount, soldier.Position.Z);
        Assert.True(gun.MountPosition.Z - soldier.Position.Z > 4096.0, "the gunner was lifted to the gun's pivot");
        Assert.True(gun.IsOccupied);

        // Attached to the gun (0x463310, 0x4610C0), the body swings round the stand with the traverse.
        gun.Steer(0f, 0.5f);
        gun.TickOccupied();
        soldier.RideMountedGun();
        Assert.Equal(gun.MountPosition.X, soldier.Position.X, 6);
        Assert.Equal(gun.MountPosition.Y, soldier.Position.Y, 6);
        Assert.Equal(heightBeforeMount, soldier.Position.Z);
        Assert.Equal(gun.MountFacing, soldier.Facing, 5);

        // The walk to the gun has finished; the fight it was installed over is what runs now.
        Assert.Equal(["AIAction_Combat"], soldier.PendingActions.Actions.Select(action => action.Name));
    }

    /// <summary>
    /// A walker whose hop is a <b>door hop</b> stops, opens the door itself, waits for it, and only
    /// then walks through — <c>sub_451540</c>'s door branch.
    /// </summary>
    /// <remarks>
    /// The planner flags a queue item when <b>both</b> ends of the hop carry
    /// <see cref="GraphNodeCriteria.Door"/>; the walker then finds the nearest door once per hop and
    /// branches on its use state (<c>sub_447D70</c>): 1 or 3 → toggle it open, 2 → "Waiting for door to
    /// open", 0 → proceed. Level 1's own graph supplies the door nodes.
    /// </remarks>
    [RequiresGameDataFact]
    public void AWalkerOpensADoorOnItsPathAndWaitsForIt()
    {
        GraphFile graph = Graph();

        // A hop whose two ends both carry the DOOR bit AND are a single route-table step apart --
        // the only kind the planner flags.
        (int From, int To)? hop = null;
        foreach (GraphNode node in graph.Nodes)
        {
            if ((node.Criteria & GraphNodeCriteria.Door) == 0)
            {
                continue;
            }

            foreach (GraphNode other in graph.Nodes)
            {
                if (other.Id == node.Id || (other.Criteria & GraphNodeCriteria.Door) == 0)
                {
                    continue;
                }

                if (graph.RouteTable?.EnumerateRoute(node.Id, other.Id) is { Count: 1 })
                {
                    hop = (node.Id, other.Id);
                    break;
                }
            }

            if (hop is not null)
            {
                break;
            }
        }

        Assert.NotNull(hop);

        GraphNode start = graph.NodeAt(hop.Value.From)!;
        Igi1HumanAi soldier = new(graph, start.Position, facing: 0f, phase: 0, seed: 1);

        DoorState door = new(maxAngleDegrees: 90f, openTimeSeconds: 1f);
        soldier.SetDoorQuery(_ => door);

        Assert.True(soldier.GoTo(hop.Value.To));

        Vector3d farEnemy = new(
            start.Position.X + (5000 * Metre), start.Position.Y, start.Position.Z);
        Vector3d before = soldier.Position;

        // First tick: the closed door is opened rather than walked into -- the walker writes no forward, so
        // the body stands and its clip carries it nowhere.
        soldier.Tick(1, farEnemy, rootMotionThisTick: 0.0);
        door.Tick();

        Assert.Equal(2, door.UseState);
        Assert.False(soldier.WalksForward);
        Assert.Equal(before, soldier.Position);

        // It holds while the door swings. The clip, and so its root motion, follows the body: it walks only
        // on a tick after the walker wrote forward.
        for (int tick = 2; tick <= 30; tick++)
        {
            soldier.Tick(tick, farEnemy, rootMotionThisTick: soldier.WalksForward ? 200.0 : 0.0);
            door.Tick();
            Assert.Equal(before, soldier.Position);
        }

        // Once the door is open the walk resumes.
        Assert.True(door.IsFullyOpen);
        for (int tick = 31; tick <= 41; tick++)
        {
            soldier.Tick(tick, farEnemy, rootMotionThisTick: soldier.WalksForward ? 200.0 : 0.0);
            door.Tick();
        }

        Assert.NotEqual(before, soldier.Position);
    }

    /// <summary>
    /// Once a patrol installs Stand-Fire, crowding cannot interrupt that child action before it reaches
    /// the trigger. Retail's <c>sub_48BC00</c> runs action+36 to completion and does not call the chooser
    /// again; only the next child action is subject to the three-aimer patrol limit.
    /// </summary>
    [Fact]
    public void APatrolFinishesAnInstalledBurstEvenWhenOtherGuardsFillTheAimSlots()
    {
        int clip = 30;
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemyFeet = new(position.X, position.Y + (5 * Metre), position.Z);
        Vector3d enemy = new(enemyFeet.X, enemyFeet.Y, enemyFeet.Z + (5 * Metre / 4));
        AiCombatTarget target = new("player");
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetDefaultTarget(target);
        soldier.SetCombat(
            AiType.PatrolAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_PATROL_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                TrackingDistanceMetres = 15f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(clip, 30, 30, WeaponCategory.Gun));

        int tick = 1;
        while (tick < 100 && soldier.CombatPhase != AiCombatPhase.Fire)
        {
            soldier.Tick(tick++, enemy, rootMotionThisTick: 0.0, enemyFeet: enemyFeet);
        }

        Assert.Equal(AiCombatPhase.Fire, soldier.CombatPhase);
        Assert.Equal(AiPatrolAction.Fire, soldier.PatrolAction);

        // Four other shooters fill the record. A fresh chooser must wait, but this soldier already owns
        // an installed Fire callback and retail lets that callback finish.
        for (int i = 0; i < AiCombatTarget.MaximumAimers; i++)
        {
            object other = new();
            target.Add(other);
            target.Aim(other, AiAimMode.Aiming);
        }

        bool fired = false;
        for (; tick < 140; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0, enemyFeet: enemyFeet);
            if (soldier.Combat.PullTrigger)
            {
                fired = true;
                clip--;
                break;
            }
        }

        Assert.True(fired, "the installed patrol burst was abandoned when the shared aim slots filled");
    }

    [Fact]
    public void PatrolGrenadeChildWritesOnceAndBodyOwnsAnimationToCompletion()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemyFeet = new(position.X, position.Y + (5 * Metre), position.Z);
        Vector3d enemyEye = new(enemyFeet.X, enemyFeet.Y, enemyFeet.Z + (5 * Metre / 4));
        Igi1HumanAi soldier = PatrolOneShotSoldier(position, grenadeProbability: 1f);
        soldier.SetGrenadeAvailable(true);

        int tick = 1;
        while (tick < 20 && soldier.PatrolAction != AiPatrolAction.ThrowGrenade)
        {
            soldier.Tick(tick++, enemyEye, 0.0, enemyFeet: enemyFeet);
        }

        Assert.Equal(AiPatrolAction.ThrowGrenade, soldier.PatrolAction);
        Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.ThrowGrenade);
        Assert.Equal(HumanSoldierStates.GrenadeThrowState, soldier.TickCombatState(animationDone: false));

        soldier.Tick(tick, enemyEye, 0.0, enemyFeet: enemyFeet);
        Assert.DoesNotContain(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.ThrowGrenade);
        Assert.Equal(HumanSoldierStates.GrenadeThrowState, soldier.TickCombatState(animationDone: false));
        Assert.Equal(HumanSoldierStates.StandState, soldier.TickCombatState(animationDone: true));
    }

    [Fact]
    public void PatrolCloseCombatChildUsesMeasuredStateAndAnimation()
    {
        Vector3d position = new(1000, 2000, 3000);

        // 1.25 m, not 1 m: detection tests the target's mid-body mark (feet + 0.7 m, sub_465820), and at
        // 1 m that mark sits 47.7 degrees below the rig-less 1.8 m fallback eye -- outside the +/-45 degree
        // default vertical cone. Retail has the same below-the-cone blind spot at arm's length; the melee
        // range itself is 1.5 m, so 1.25 m stays a close-combat scenario the cone actually admits.
        Vector3d enemyFeet = new(position.X, position.Y + (5 * Metre / 4), position.Z);
        Vector3d enemyEye = new(enemyFeet.X, enemyFeet.Y, enemyFeet.Z + (5 * Metre / 4));
        Igi1HumanAi soldier = PatrolOneShotSoldier(position, grenadeProbability: 0f);

        int tick = 1;
        while (tick < 20 && soldier.PatrolAction != AiPatrolAction.CloseCombat)
        {
            soldier.Tick(tick++, enemyEye, 0.0, enemyFeet: enemyFeet);
        }

        Assert.Equal(AiPatrolAction.CloseCombat, soldier.PatrolAction);
        Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.CloseCombat);
        Assert.Equal(HumanSoldierStates.CloseCombatState, soldier.TickCombatState(animationDone: false));
        Assert.Equal(70, HumanSoldierStates.Table.AnimationOf(soldier.CombatState));

        soldier.Tick(tick, enemyEye, 0.0, enemyFeet: enemyFeet);
        Assert.DoesNotContain(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.CloseCombat);
        Assert.Equal(HumanSoldierStates.CloseCombatState, soldier.TickCombatState(animationDone: false));
        Assert.Equal(HumanSoldierStates.StandState, soldier.TickCombatState(animationDone: true));
    }

    [Fact]
    public void PatrolDamageEventInstallsPersistentProneBehavior()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemyFeet = new(position.X, position.Y + (8 * Metre), position.Z);
        Vector3d enemyEye = new(enemyFeet.X, enemyFeet.Y, enemyFeet.Z + (5 * Metre / 4));
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.PatrolAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_PATROL_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                TrackingDistanceMetres = 100f,
                EvasiveActionProbability = 1f,
                EvasiveStandRollOrLieDownProbability = 0f,
                LieDownMinimumDistanceMetres = 1f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));

        // The evasion routine sets its cooldown even when no shot event exists. Keep the measured
        // type-15 condition present until the first eligible patrol tick rather than assuming which
        // creation/detection tick first installed combat.
        int damageTick = 1;
        do
        {
            soldier.ReceiveDamage(enemyFeet);
            soldier.Tick(damageTick++, enemyEye, 0.0, enemyFeet: enemyFeet);
        }
        while (damageTick < 50
            && !soldier.ChannelWrites.Any(write => write.Channel == AiCombatChannel.LieDown));

        Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.LieDown);
        Assert.Equal(HumanSoldierStates.LieDownState, soldier.TickCombatState(animationDone: false));

        soldier.Tick(damageTick, enemyEye, 0.0, enemyFeet: enemyFeet);
        Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.LieDown);
        Assert.Equal(HumanSoldierStates.ProneState, soldier.TickCombatState(animationDone: true));

        soldier.Tick(damageTick + 1, enemyEye, 0.0, enemyFeet: enemyFeet);
        Assert.Contains(soldier.ChannelWrites, write => write.Channel == AiCombatChannel.LieDown);
        Assert.Equal(HumanSoldierStates.ProneState, soldier.TickCombatState(animationDone: false));
    }
}
