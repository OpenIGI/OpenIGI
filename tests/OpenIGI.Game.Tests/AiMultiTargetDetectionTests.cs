using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The detection scan over every human at once — <c>0x489750</c> and <c>0x44F410</c>'s posts.</summary>
public sealed class AiMultiTargetDetectionTests
{
    private const double Metre = 4096.0;

    [Fact]
    public void EveryEnemyInSightIsDetectedAndTheFirstRegisteredLeadsTheQueue()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        AiDetectionCandidate guard = Human(id: 417, otherSide: true, alive: true, metresAhead: 6, metresAcross: 1);
        AiDetectionCandidate player = Human(AiDetectionCandidate.PlayerId, otherSide: true, alive: true, metresAhead: 5, metresAcross: -1);

        TickUntilSeen(soldier, [guard, player], AiEvent.EnemyDetection, expected: 2);

        // 0x44F410 posts one ENEMYDETECTION per enemy in sight, in registration order, so the one the
        // script hears first -- and the soldier's attention -- is the first registered, not the nearest.
        Assert.Equal(417, soldier.TargetId);
        Assert.False(soldier.TargetIsPlayer);
    }

    [Fact]
    public void AFallenFriendInSightIsReportedAsSuch()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        AiDetectionCandidate corpse = Human(id: 300, otherSide: false, alive: false, metresAhead: 4, metresAcross: 0);

        TickUntilSeen(soldier, [corpse], AiEvent.FriendlyDetection, expected: 1);

        Assert.Equal(0, soldier.Events.DrainedCount(AiEvent.EnemyDetection));
    }

    [Fact]
    public void ALivingFriendIsNeverRegistered()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1) { PlayerIsEnemy = false };
        AiDetectionCandidate player = Human(AiDetectionCandidate.PlayerId, otherSide: false, alive: true, metresAhead: 4, metresAcross: 0);

        for (int tick = 0; tick < 40; tick++)
        {
            soldier.TickAmong(tick, [player], 0.0);
            Assert.Equal(0, soldier.Events.DrainedCount(AiEvent.EnemyDetection));
        }

        Assert.False(soldier.SeesEnemy);
    }

    private static void TickUntilSeen(AiSoldier soldier, AiDetectionCandidate[] humans, AiEvent type, int expected)
    {
        for (int tick = 0; tick < 60; tick++)
        {
            soldier.TickAmong(tick, humans, 0.0);

            // A soldier without a script empties every queue each tick; the drained counts are what it heard.
            if (soldier.Events.DrainedCount(type) == expected)
            {
                return;
            }
        }

        Assert.Fail($"{type} never reached {expected} records in one tick.");
    }

    private static AiDetectionCandidate Human(int id, bool otherSide, bool alive, double metresAhead, double metresAcross)
    {
        Vector3d feet = new(metresAcross * Metre, metresAhead * Metre, 0);
        Vector3d mark = new(feet.X, feet.Y, feet.Z + AiSoldier.DetectionRaiseStandingInUnits);
        return new AiDetectionCandidate(id, otherSide, alive, mark, feet, Crouching: false, Record: null);
    }
}
