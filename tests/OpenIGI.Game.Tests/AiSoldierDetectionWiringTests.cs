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
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The same end-to-end chain for what a fight tells the rest of the level: the detection shout and mark,
/// the squad ping, and the aim and detection slots a soldier claims and gives back.
/// </summary>
public sealed class AiSoldierDetectionWiringTests : AiSoldierCombatWiringFixture
{
    [RequiresGameDataFact]
    public void VisibleEngagementRequestsExactlyOneDetectionShout()
    {
        Igi1HumanAi soldier = Soldier(out Vector3d position);
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
        Igi1HumanAi soldier = Soldier(out Vector3d position);
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
        Igi1HumanAi soldier = Soldier(out Vector3d position);
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
        Assert.All(pings.Zip(pings.Skip(1)), pair => Assert.Equal(Igi1HumanAi.AlertPingPeriodTicks, pair.Second - pair.First));
    }

    [Fact]
    public void TheDetectionMarkSitsWhereSub465820PutsIt()
    {
        // 0xE147AE14:0x40A6147A standing and 0x66666666:0x40966666 crouched.
        Assert.Equal(2826.24, Igi1HumanAi.DetectionRaiseStandingInUnits, precision: 9);
        Assert.Equal(1433.6, Igi1HumanAi.DetectionRaiseCrouchedInUnits, precision: 9);
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

        Igi1HumanAi[] squad = new Igi1HumanAi[4];
        for (int i = 0; i < squad.Length; i++)
        {
            squad[i] = new Igi1HumanAi(graph: null, new Vector3d(i * Metre, 0, 0), 0f, phase: 0, seed: i);
            squad[i].SetTarget(record);
            record.Aim(squad[i], AiAimMode.Aiming);
        }

        Assert.Equal(AiCombatTarget.MaximumAimers, record.Count(AiAimMode.Aiming));

        foreach (Igi1HumanAi dead in squad)
        {
            dead.Kill();
        }

        Assert.Equal(0, record.Count(AiAimMode.Aiming));
        Assert.False(record.IsLive);
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
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
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
}
