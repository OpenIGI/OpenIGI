using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A patrol-type soldier that sees an enemy too far to shoot from where it stands <b>advances on it</b> —
/// the move-and-fight arm's Follow, which the port set a destination for but never travelled.
/// </summary>
public sealed class AiChaseTests
{
    private const double Metre = 4096.0;

    /// <summary>
    /// A patrol soldier that has engaged an enemy and then loses sight of it <b>hunts toward where it went</b>
    /// and moves — the search half of the move-and-fight arm. Before the fix it set a search destination it
    /// never travelled, so a guard that lost you simply stood and fired at nothing.
    /// </summary>
    [RequiresGameDataFact]
    public void APatrolSoldierThatLosesSightHuntsAndMoves()
    {
        GraphFile graph = Graph();
        Vector3d position = graph.Nodes[0].Position;
        AiSoldier soldier = new(graph, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.PatrolAk,
            Parameters(trackingMetres: 15f),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));

        // Five metres in front, in the cone: it is seen, and the soldier engages.
        Vector3d seen = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        bool engaged = false;
        for (int tick = 1; tick <= 60 && !engaged; tick++)
        {
            soldier.Tick(tick, seen, rootMotionThisTick: 178.0);
            engaged |= soldier.Activity == AiSoldierActivity.Engaging;
        }

        Assert.True(engaged, "the patrol soldier never engaged the enemy it could see");

        // Now the enemy is well behind it and out of every cone — the soldier can no longer see it and has
        // to hunt. Over the next few seconds it should MOVE toward where the enemy is, not stand and fire.
        Vector3d gone = new(position.X + (25 * Metre), position.Y - (25 * Metre), position.Z);
        Vector3d start = soldier.Position;
        bool hunted = false;
        bool walkedInCombat = false;

        for (int tick = 61; tick <= 600; tick++)
        {
            soldier.Tick(tick, gone, rootMotionThisTick: 178.0);
            hunted |= soldier.PatrolAction is AiPatrolAction.Follow or AiPatrolAction.RePosition;

            // The flag the renderer reads to play the WALK clip (so root motion exists in-game). Without it,
            // a hunting soldier plays the fire pose and never actually travels, however sound the logic is.
            walkedInCombat |= soldier.IsMovingInCombat;
        }

        Assert.True(hunted, "the patrol soldier lost sight of the enemy but never chose to hunt (Follow/RePosition)");
        Assert.True(walkedInCombat, "the soldier hunted but never set IsMovingInCombat, so the game would play the fire pose and never move");
        Assert.True(
            Distance(soldier.Position, start) > 5 * Metre,
            $"the soldier lost sight but barely moved ({Distance(soldier.Position, start) / Metre:0.0} m) — it hunted on the spot");
    }

    /// <summary>
    /// A soldier engaging an enemy that then <b>dies</b> leaves combat and stops pulling the trigger — the
    /// detection scan (<c>0x489750</c>) unregisters a dead human, so the guard loses it rather than emptying
    /// its magazine into the corpse. This is the "guards keep shooting me after I die" fix.
    /// </summary>
    [RequiresGameDataFact]
    public void ASoldierLeavesCombatWhenTheEnemyDies()
    {
        GraphFile graph = Graph();
        Vector3d position = graph.Nodes[0].Position;
        AiSoldier soldier = new(graph, position, facing: 0f, phase: 0, seed: 1);
        soldier.SetCombat(
            AiType.PatrolAk,
            Parameters(trackingMetres: 15f),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));

        // Five metres in front, in the cone: it is seen, and the soldier engages.
        Vector3d seen = new(position.X, position.Y + (5 * Metre), position.Z + (5 * Metre / 4));
        bool engaged = false;
        for (int tick = 1; tick <= 60 && !engaged; tick++)
        {
            soldier.Tick(tick, seen, rootMotionThisTick: 178.0);
            engaged |= soldier.Activity == AiSoldierActivity.Engaging;
        }

        Assert.True(engaged, "the soldier never engaged the enemy it could see");

        // The enemy dies where it stood. A single tick with the enemy no longer alive must take the
        // soldier out of combat -- the unregister is immediate -- and clear its trigger.
        soldier.Tick(61, seen, rootMotionThisTick: 178.0, enemyAlive: false);

        Assert.NotEqual(AiSoldierActivity.Engaging, soldier.Activity);
        Assert.False(soldier.Combat.PullTrigger, "the soldier kept pulling the trigger on the corpse");

        // And it stays out -- feeding the dead enemy every tick never re-engages it.
        for (int tick = 62; tick <= 90; tick++)
        {
            soldier.Tick(tick, seen, rootMotionThisTick: 178.0, enemyAlive: false);
            Assert.NotEqual(AiSoldierActivity.Engaging, soldier.Activity);
        }
    }

    private static AiCombatParameters Parameters(float trackingMetres) =>
        new(new HumanAiConfigItemTask
        {
            AiTypeName = "AITYPE_PATROL_AK",
            DifficultyName = "GD_2",
            TargetingTimeoutSeconds = 15f,
            TrackingDistanceMetres = trackingMetres,
            MinimumClipFraction = 0.8f,
            RandomClipFraction = 0.2f,
            MaximumRoundsPerSequence = 30,
            HitProbabilityAtMinimumRange = 1f,
            HitProbabilityAtMaximumRange = 0.3f,
            AimPrecision = 0.6f,
        });

    private static GraphFile Graph()
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");
        return GraphFile.Read(File.ReadAllBytes(path));
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
