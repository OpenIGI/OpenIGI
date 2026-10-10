using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The whole chain from "a soldier can see somebody" to "the trigger is down", driven end to end.
/// </summary>
/// <remarks>
/// Every piece of this had a test of its own and the join between them did not, which is exactly where
/// it broke. These drive a real <see cref="AiSoldier"/> over a real retail nav graph.
/// </remarks>
public sealed class AiSoldierCombatWiringTests
{
    private const double Metre = 4096.0;

    // The clip the fixture's weapon query reports. The desktop drains it when a round comes out, and the
    // AI counts its burst by watching it CHANGE -- a query that never moved would hold the trigger for
    // ever and never leave the firing phase.
    private int _clip = 30;

    /// <summary>A soldier that can see an enemy engages, aims and ends up holding the trigger down.</summary>
    [RequiresGameDataFact]
    public void ASoldierThatSeesAnEnemyEndsUpPullingTheTrigger()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        // Five metres in front of it, at chest height, and well inside a guard's forty-metre cone.
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));

        bool sawIt = false;
        bool engaged = false;
        bool fired = false;

        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);

            sawIt |= soldier.SeesEnemy;
            engaged |= soldier.Activity == AiSoldierActivity.Engaging;
            fired |= soldier.Combat.PullTrigger;
        }

        Assert.True(sawIt, "the soldier never saw the enemy at five metres");
        Assert.True(engaged, "the soldier saw the enemy and never engaged");
        Assert.True(fired, $"the soldier engaged and never pulled the trigger (phase {soldier.CombatPhase})");
    }

    /// <summary>A solid world blocker near the target must not drive the combat handoff.</summary>
    [RequiresGameDataFact]
    public void ALateWorldBlockerStillOccludesTheTarget()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        soldier.SetLineOfSight((from, to) =>
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            return AiSoldier.WorldBlockerAllowsSight(distance, distance * 0.9);
        });
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));

        bool sawIt = false;
        bool fired = false;
        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
            sawIt |= soldier.SeesEnemy;
            fired |= soldier.Combat.PullTrigger;
        }

        Assert.False(sawIt, "a solid world blocker at 90% of the segment became transparent");
        Assert.False(fired, "the soldier fired through solid world geometry");
    }

    [RequiresGameDataFact]
    public void VisibleEngagementRequestsExactlyOneDetectionShout()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        int requests = 0;

        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            if (soldier.DetectionShoutRequestedThisTick)
            {
                requests++;
            }

            Spend(soldier);
        }

        Assert.Equal(1, requests);
    }

    /// <summary>
    /// "Hey you!" is for the player: <c>sub_48A920</c> tests the target's class against HumanPlayer's before the
    /// per-action latch, so a soldier fighting an NPC stays silent and still challenges the player later.
    /// </summary>
    [RequiresGameDataFact]
    public void ASoldierFightingAnNpcKeepsItsChallengeForThePlayer()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        int requests = 0;

        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0, enemyIsSoldier: tick <= 150);
            if (soldier.DetectionShoutRequestedThisTick)
            {
                Assert.True(tick > 150, $"the soldier challenged an NPC on tick {tick}");
                requests++;
            }

            Spend(soldier);
        }

        Assert.Equal(1, requests);
    }

    /// <summary>
    /// An engaged soldier pings its squad on the engagement's first tick with a target and every twentieth
    /// after — <c>sub_48A920</c>'s counter lives in the combat action, not on the mission's clock.
    /// </summary>
    [RequiresGameDataFact]
    public void AnEngagedSoldierPingsItsSquadFromItsFirstTickEveryTwenty()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        List<int> pings = [];
        int engagedFrom = -1;

        for (int tick = 1; tick <= 120; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            if (engagedFrom < 0 && soldier.Activity == AiSoldierActivity.Engaging)
            {
                engagedFrom = tick;
            }

            if (soldier.AlertPingDueThisTick)
            {
                pings.Add(tick);
            }

            Spend(soldier);
        }

        Assert.NotEmpty(pings);
        Assert.Equal(engagedFrom, pings[0]);
        Assert.All(pings.Zip(pings.Skip(1)), pair => Assert.Equal(AiSoldier.AlertPingPeriodTicks, pair.Second - pair.First));
    }

    [Fact]
    public void TheDetectionMarkSitsWhereSub465820PutsIt()
    {
        // 0xE147AE14:0x40A6147A standing and 0x66666666:0x40966666 crouched.
        Assert.Equal(2826.24, AiSoldier.DetectionRaiseStandingInUnits, precision: 9);
        Assert.Equal(1433.6, AiSoldier.DetectionRaiseCrouchedInUnits, precision: 9);
    }

    /// <summary>
    /// The combat behaviour is <b>re-armed</b> every time the soldier engages, however it got there.
    /// </summary>
    /// <remarks>
    /// Once the machine ends — a targeting timeout, or a target it never had a detection slot for — its
    /// phase is <see cref="AiCombatPhase.Ended"/> and every later tick returns "ended" immediately. A
    /// soldier that engaged again without re-arming would therefore never fire again for the rest of the
    /// level, which is what it looked like: guards that see you and do nothing.
    /// </remarks>
    [RequiresGameDataFact]
    public void EngagingAgainReArmsTheBehaviour()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        Vector3d gone = new(position.X, position.Y + (5000 * Metre), position.Z);

        // See it, engage, fire.
        for (int tick = 1; tick <= 200; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
        }

        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);

        // Lose it for long enough to run the targeting timeout out.
        for (int tick = 201; tick <= 1400; tick++)
        {
            soldier.Tick(tick, gone, rootMotionThisTick: 0.0);
            Spend(soldier);
        }

        Assert.NotEqual(AiSoldierActivity.Engaging, soldier.Activity);

        // And find it again.
        bool firedAgain = false;
        for (int tick = 1401; tick <= 1700; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
            firedAgain |= soldier.Combat.PullTrigger;
        }

        Assert.True(firedAgain, $"the soldier re-engaged and never fired again (phase {soldier.CombatPhase})");
    }

    /// <summary>
    /// A soldier running the shipped guard QVM re-arms its combat behaviour when it sees the target again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AIEVENT_ENEMYDETECTION</c> produces <c>AIAction_SetCombat</c>; that posts
    /// <c>AIEVENT_COMBAT</c>, whose shipped guard branch produces <c>AIAction_Combat</c>. This test uses
    /// the retail QVM so an invented empty handler cannot justify bypassing authored mission scripts.
    /// </para>
    /// <para>
    /// The script must remain the authority for installing the combat action.
    /// </para>
    /// </remarks>
    [RequiresGameDataFact]
    public void AScriptedSoldierReArmsCombatWhenItSeesTheTargetAgain()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        natives.RegisterConstant("GAME_FREQUENCY", 30);
        natives.RegisterRealConstant("WORLD_METER", 4096.0);
        natives.RegisterRealConstant("MATH_PI", Math.PI);
        AiScriptHost host = new(natives);
        QvmProgram guard = QvmProgram.Read(File.ReadAllBytes(Path.Combine(
            GameData.RequireRoot(), "common", "ai", "guard.qvm")));
        soldier.SetScript(host, guard);

        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        Vector3d gone = new(position.X, position.Y + (5000 * Metre), position.Z);

        for (int tick = 1; tick <= 200; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
        }

        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);

        // Lose it long enough for the targeting timeout to end the behaviour.
        for (int tick = 201; tick <= 1400; tick++)
        {
            soldier.Tick(tick, gone, rootMotionThisTick: 0.0);
            Spend(soldier);
        }

        // And walk back into view: the arm must be re-installed and the trigger pulled again.
        bool firedAgain = false;
        for (int tick = 1401; tick <= 1700; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
            Spend(soldier);
            firedAgain |= soldier.Combat.PullTrigger;
        }

        Assert.True(
            firedAgain,
            $"the scripted soldier saw the target again and never fired (activity {soldier.Activity}, "
                + $"phase {soldier.CombatPhase})");
    }

    /// <summary>A soldier given no combat row falls back to standing and facing, as it did before.</summary>
    [RequiresGameDataFact]
    public void ASoldierWithNoCombatRowStillTurnsToFaceTheEnemy()
    {
        GraphFile graph = Graph();
        Vector3d position = NodePosition(graph);
        AiSoldier soldier = new(graph, position, facing: 0.6f, phase: 0, seed: 1);

        Vector3d enemy = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        float before = soldier.Facing;

        for (int tick = 1; tick <= 200; tick++)
        {
            soldier.Tick(tick, enemy, rootMotionThisTick: 0.0);
        }

        Assert.NotEqual(before, soldier.Facing);
        Assert.False(soldier.Combat.PullTrigger);
    }

    /// <summary>
    /// Combat aims at the enemy's FEET raised half a metre — <c>sub_451380</c> — not at the eye it was
    /// seen with.
    /// </summary>
    /// <remarks>
    /// The aim point is the target task's own position (<c>Human+32</c>, its origin, 0.97 m over the feet the
    /// port keeps) plus <b>2048 units</b> (0.5 m), or <b>1024</b>
    /// (0.25 m) crouched — read straight out of <c>IGI.exe</c>'s <c>sub_451380</c>:
    /// <c>a1[2] += crouch ? 1024.0 : 2048.0</c>, where the crouch is the <i>target's</i> flag
    /// (<c>Human+829</c>). Feeding the eye instead put the point half a metre <i>above the head</i>, and
    /// every resolved round arrived at head height.
    /// </remarks>
    [RequiresGameDataFact]
    public void CombatAimsAtTheOriginRaisedHalfAMetre()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Vector3d feet = new(position.X, position.Y + (5 * Metre), position.Z);
        Vector3d eye = new(feet.X, feet.Y, feet.Z + (1.62 * Metre));

        Vector3d? aim = null;
        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, eye, rootMotionThisTick: 0.0, enemyFeet: feet);
            Spend(soldier);
            if (soldier.Combat.PullTrigger && soldier.Combat.TurnTo is { } point)
            {
                aim = point;
                break;
            }
        }

        Assert.NotNull(aim);
        // sub_451380 raises the ORIGIN (Human+32), which stands 0.97 m over the feet the port keeps.
        Assert.Equal(feet.Z + AiSoldier.HumanOriginStandingInUnits + 2048.0, aim.Value.Z, precision: 3);
    }

    /// <summary>
    /// A guard firing at a target above or below it has pitched its view onto the aim point by the time the
    /// trigger is down — the round's direction, not the body's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The human's fire message (<c>0x462E50</c>) carries its view's position and orientation
    /// (<c>HumanView+224</c>, <c>+248</c>, copied by <c>0x465800</c>/<c>0x465880</c>), and <c>Gun_Fire</c>
    /// (<c>0x478BA0</c>) casts along the message's orientation. So a round leaves along the view's pitch,
    /// which the AI steers onto its aim point the same way it steers its heading.
    /// </para>
    /// <para>
    /// The BODY does not pitch, in retail or here: <c>0x4610C0</c> zeroes the body's pitch on the ground and
    /// turns only its heading, and the upper-body clip's orientation source (<c>Human+1616</c>, bound by
    /// <c>0x489ED0</c>) is a constant copied in when the clip starts.
    /// </para>
    /// </remarks>
    /// <param name="rise">How far above the guard's feet the target's feet stand, in metres.</param>
    [RequiresGameDataTheory]
    [InlineData(6.0)]
    [InlineData(-4.0)]
    public void AGuardFiresAlongItsViewPitchedOntoATargetAboveOrBelow(double rise)
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Vector3d feet = new(position.X, position.Y + (12 * Metre), position.Z + (rise * Metre));
        Vector3d eye = new(feet.X, feet.Y, feet.Z + (1.62 * Metre));

        Vector3d? aim = null;
        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, eye, rootMotionThisTick: 0.0, enemyFeet: feet);
            Spend(soldier);
            if (soldier.Combat.PullTrigger && soldier.Combat.TurnTo is { } point)
            {
                aim = point;
                break;
            }
        }

        Assert.NotNull(aim);

        Vector3d from = soldier.EyePosition();
        double run = Math.Sqrt(Math.Pow(aim.Value.X - from.X, 2) + Math.Pow(aim.Value.Y - from.Y, 2));
        double wanted = Math.Atan2(aim.Value.Z - from.Z, run);

        // The round's forward: the view's +Y, as SoldierRenderer.SpreadRound takes it.
        Vector3d forward = soldier.ViewOrientation.Transposed().Transform(new Vector3d(0.0, 1.0, 0.0));
        double flown = Math.Atan2(forward.Z, Math.Sqrt((forward.X * forward.X) + (forward.Y * forward.Y)));

        Assert.Equal(Math.Sign(rise), Math.Sign(flown));
        Assert.True(
            Math.Abs(flown - wanted) <= AiGuardCombat.AimToleranceRadians + 1e-4,
            $"round pitched {flown * 180 / Math.PI:F2} deg, aim point at {wanted * 180 / Math.PI:F2} deg");
    }

    /// <summary>A crouching target halves the raise — 1024 units, 0.25 m.</summary>
    [RequiresGameDataFact]
    public void ACrouchingTargetIsAimedAtAQuarterMetre()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Vector3d feet = new(position.X, position.Y + (5 * Metre), position.Z);
        Vector3d eye = new(feet.X, feet.Y, feet.Z + (0.9 * Metre));

        Vector3d? aim = null;
        for (int tick = 1; tick <= 300; tick++)
        {
            soldier.Tick(tick, eye, rootMotionThisTick: 0.0, enemyFeet: feet, enemyCrouching: true);
            Spend(soldier);
            if (soldier.Combat.PullTrigger && soldier.Combat.TurnTo is { } point)
            {
                aim = point;
                break;
            }
        }

        Assert.NotNull(aim);
        Assert.Equal(feet.Z + AiSoldier.HumanOriginCrouchedInUnits + 1024.0, aim.Value.Z, precision: 3);
    }

    /// <summary>
    /// A soldier whose level ships no nav graph still sees, engages and fires — it just cannot patrol.
    /// </summary>
    /// <remarks>
    /// Nothing in the detection cascade or the stand-and-fight arm (<c>0x48D000</c>…) touches a node.
    /// Leaving such a soldier brainless made an inert prop — "a guard standing there playing no
    /// animation and never reacting" — where the retail one would have shot back from where it stood.
    /// </remarks>
    [RequiresGameDataFact]
    public void ASoldierWithoutAGraphStillFights()
    {
        Vector3d position = new(1000 * Metre, 1000 * Metre, 0);
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_GUARD_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.8f,
                RandomClipFraction = 0.2f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
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

        Assert.True(fired, $"the graphless soldier never fired (activity {soldier.Activity}, phase {soldier.CombatPhase})");
        Assert.False(soldier.BeginPatrol(1), "a graphless soldier cannot patrol");
    }

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
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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
        AiSoldier soldier = new(
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

        AiSoldier soldier = new(graph, start, facing: 0f, phase: 0, seed: 1);
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
        AiSoldier second = new(graph, start, facing: 0f, phase: 1, seed: 2);
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
        AiSoldier soldier = new(graph, start, facing: 0f, phase: 0, seed: 1)
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
        AiSoldier soldier = new(graph, start.Position, facing: 0f, phase: 0, seed: 1);

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

    /// <summary>A dead soldier releases its slot on the shared target record — <c>sub_4512A0</c>.</summary>
    /// <remarks>
    /// The original's combat action ends when its owner dies and the end function (<c>0x48D7D0</c>)
    /// swap-removes the attacker. Without the release, four dead guards kept holding
    /// <see cref="AiAimMode.Aiming"/> and every guard that engaged afterwards was pushed to
    /// <see cref="AiAimMode.Crowded"/> for the rest of the level — it saw the player, entered combat,
    /// and stood there doing nothing.
    /// </remarks>
    [RequiresGameDataFact]
    public void ADeadSoldierFreesItsAimSlot()
    {
        AiCombatTarget record = new(new object());

        AiSoldier[] squad = new AiSoldier[4];
        for (int i = 0; i < squad.Length; i++)
        {
            squad[i] = new AiSoldier(graph: null, new Vector3d(i * Metre, 0, 0), 0f, phase: 0, seed: i);
            squad[i].SetTarget(record);
            record.Aim(squad[i], AiAimMode.Aiming);
        }

        Assert.Equal(AiCombatTarget.MaximumAimers, record.Count(AiAimMode.Aiming));

        foreach (AiSoldier dead in squad)
        {
            dead.Kill();
        }

        Assert.Equal(0, record.Count(AiAimMode.Aiming));
        Assert.False(record.IsLive);
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
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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
        AiSoldier soldier = PatrolOneShotSoldier(position, grenadeProbability: 1f);
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
        AiSoldier soldier = PatrolOneShotSoldier(position, grenadeProbability: 0f);

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
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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

    /// <summary>
    /// A heard shot can start combat before the shooter has ever been visible: the living enemy's
    /// detection slot is already registered by classification, which is what retail combat tests.
    /// </summary>
    [Fact]
    public void AHeardUnseenEnemyHasACombatDetectionSlotAndSharedTarget()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemyBehind = new(position.X, position.Y - (10 * Metre), position.Z);
        AiCombatTarget target = new("player");
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetDefaultTarget(target);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_GUARD_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));

        soldier.Tick(1, enemyBehind, rootMotionThisTick: 0.0, enemyFeet: enemyBehind);
        Assert.False(soldier.SeesEnemy);
        Assert.True(soldier.HearGunshot(enemyBehind, AiScriptApi.GunshotRange));

        soldier.ApplyActions([new AiProducedAction("AIAction_SetCombat", [], 0)]);
        soldier.Tick(2, enemyBehind, rootMotionThisTick: 0.0, enemyFeet: enemyBehind);

        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
        Assert.NotEqual(AiCombatPhase.Ended, soldier.CombatPhase);
        Assert.Same(target, soldier.Target);
        Assert.Equal(enemyBehind, target.Position);
    }

    /// <summary>A living enemy is reclassified after a prior dead tick unregistered its slot.</summary>
    [Fact]
    public void ARespawnedEnemyReRegistersItsDetectionSlot()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemy = new(position.X, position.Y - Metre, position.Z);
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_GUARD_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));

        // Dead, the enemy is not registered; alive again, the scan registers it on the soldier's next phase
        // tick (0x489750 runs on tick % 4 == phase), and a fight started then has its slot.
        soldier.Tick(1, enemy, 0.0, enemyAlive: false);
        for (int tick = 2; tick <= 4; tick++)
        {
            soldier.Tick(tick, enemy, 0.0, enemyAlive: true);
        }

        soldier.ApplyActions([new AiProducedAction("AIAction_SetCombat", [], 0)]);
        soldier.Tick(5, enemy, 0.0, enemyAlive: true);

        Assert.NotEqual(AiCombatPhase.Ended, soldier.CombatPhase);
    }

    // What the desktop's firing path does: a round out of the clip, and the clip back when it reloads.
    private void Spend(AiSoldier soldier)
    {
        if (soldier.Combat.PullTrigger && _clip > 0)
        {
            _clip--;
        }
        else if (soldier.Combat.PressReload || soldier.Combat.ReloadWeaponDirectly)
        {
            _clip = 30;
        }
    }

    private AiSoldier Soldier(out Vector3d position)
    {
        GraphFile graph = Graph();
        position = NodePosition(graph);

        AiSoldier soldier = new(graph, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.GuardAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_GUARD_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                MinimumClipFraction = 0.8f,
                RandomClipFraction = 0.2f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(_clip, 30, 30, WeaponCategory.Gun));

        return soldier;
    }

    private static AiSoldier PatrolOneShotSoldier(Vector3d position, float grenadeProbability)
    {
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.PatrolAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_PATROL_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                TrackingDistanceMetres = 100f,
                // A soldier with rounds in its clip draws against the "when reload" probability (+15756).
                GrenadeThrowProbability = grenadeProbability,
                GrenadeThrowWhenReloadingProbability = grenadeProbability,
                GrenadeMinimumDistanceMetres = 1f,
                GrenadeMaximumDistanceMetres = 20f,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));
        return soldier;
    }

    // Level 1's own nav graph, so the soldier is standing somewhere the game actually puts one.
    private static GraphFile Graph()
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");

        return GraphFile.Read(File.ReadAllBytes(path));
    }

    private static Vector3d NodePosition(GraphFile graph) => graph.Nodes[0].Position;
}
