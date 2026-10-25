using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The whole chain from "a soldier can see somebody" to "the trigger is down", driven end to end.
/// </summary>
/// <remarks>
/// Every piece of this had a test of its own and the join between them did not, which is exactly where
/// it broke. These drive a real <see cref="Igi1HumanAi"/> over a real retail nav graph.
/// </remarks>
public sealed class AiSoldierCombatWiringTests : AiSoldierCombatWiringFixture
{
    /// <summary>A soldier that can see an enemy engages, aims and ends up holding the trigger down.</summary>
    [RequiresGameDataFact]
    public void ASoldierThatSeesAnEnemyEndsUpPullingTheTrigger()
    {
        Igi1HumanAi soldier = Soldier(out Vector3d position);

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
        Igi1HumanAi soldier = Soldier(out Vector3d position);
        soldier.SetLineOfSight((from, to, _) =>
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            return Igi1HumanAi.WorldBlockerAllowsSight(distance, distance * 0.9);
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
        Igi1HumanAi soldier = Soldier(out Vector3d position);
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
        Igi1HumanAi soldier = Soldier(out Vector3d position);

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
        Igi1HumanAi soldier = new(graph, position, facing: 0.6f, phase: 0, seed: 1);

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
        Igi1HumanAi soldier = Soldier(out Vector3d position);

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
        Assert.Equal(feet.Z + Igi1HumanAi.HumanOriginStandingInUnits + 2048.0, aim.Value.Z, precision: 3);
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
        Igi1HumanAi soldier = Soldier(out Vector3d position);

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
        Igi1HumanAi soldier = Soldier(out Vector3d position);

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
        Assert.Equal(feet.Z + Igi1HumanAi.HumanOriginCrouchedInUnits + 1024.0, aim.Value.Z, precision: 3);
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
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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
}
