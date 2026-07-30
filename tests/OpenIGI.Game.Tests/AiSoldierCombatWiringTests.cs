using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
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

    // Level 1's own nav graph, so the soldier is standing somewhere the game actually puts one.
    private static GraphFile Graph()
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");

        return GraphFile.Read(File.ReadAllBytes(path));
    }

    private static Vector3d NodePosition(GraphFile graph) => graph.Nodes[0].Position;
}
