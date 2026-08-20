using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A soldier hearing a gunshot — the delivery of <see cref="AiEvent.Gunshot"/>, gated by the two ranges the
/// AI module registers: 30 m normally, 1 m silenced.
/// </summary>
public sealed class AiHearingTests
{
    private const double Metre = 4096.0;

    /// <summary>A shot ten metres off is heard and queues the event.</summary>
    [RequiresGameDataFact]
    public void ASoldierHearsAShotWithinThirtyMetres()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Assert.True(soldier.HearGunshot(Away(position, 10 * Metre), silenced: false));
        Assert.True(soldier.Events.PendingCount(AiEvent.Gunshot) > 0);
        Assert.Equal(-1, soldier.Events.HeadOf(AiEvent.Gunshot)?.Source);
    }

    /// <summary>A shot forty metres off is out of earshot and queues nothing.</summary>
    [RequiresGameDataFact]
    public void ASoldierDoesNotHearAShotBeyondThirtyMetres()
    {
        AiSoldier soldier = Soldier(out Vector3d position);

        Assert.False(soldier.HearGunshot(Away(position, 40 * Metre), silenced: false));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Gunshot));
    }

    /// <summary>
    /// A silenced weapon carries a thirtieth as far: a shot two metres away is heard unsilenced and missed
    /// silenced, and a shot inside a metre is heard even silenced.
    /// </summary>
    [RequiresGameDataFact]
    public void ASilencedShotIsHeardOnlyWithinAMetre()
    {
        Assert.True(Soldier(out Vector3d a).HearGunshot(Away(a, 2 * Metre), silenced: false));
        Assert.False(Soldier(out Vector3d b).HearGunshot(Away(b, 2 * Metre), silenced: true));
        Assert.True(Soldier(out Vector3d c).HearGunshot(Away(c, 0.8 * Metre), silenced: true));
    }

    /// <summary>The weapon row's exact radius, rather than a binary silenced flag, gates delivery.</summary>
    [RequiresGameDataFact]
    public void AWeaponSpecificDetectionRangeIsUsedExactly()
    {
        Assert.True(Soldier(out Vector3d a).HearGunshot(Away(a, 3 * Metre), 4 * Metre));
        Assert.False(Soldier(out Vector3d b).HearGunshot(Away(b, 5 * Metre), 4 * Metre));
    }

    [Fact]
    public void AZeroDistanceShotIsHeardAndANegativeRangeIsClampedToZero()
    {
        AiSoldier atSource = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.True(atSource.HearGunshot(default, 0.0));

        AiSoldier negativeRange = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.True(negativeRange.HearGunshot(default, -1.0));
        Assert.Equal(1, negativeRange.Events.PendingCount(AiEvent.Gunshot));
    }

    [Fact]
    public void DisabledGunshotAndAlertEventsAreNotReportedOrLatched()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Events.SetEnabled(AiEvent.Gunshot, false);
        Assert.False(soldier.HearGunshot(default, 1.0));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Gunshot));

        soldier.Events.SetEnabled(AiEvent.Alert, false);
        Assert.False(soldier.ReceiveAlert(default, source: 4));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));

        soldier.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.False(soldier.Alerted);
    }

    [Fact]
    public void AHeardGunshotRaisesTheCombatPerceptionAlertOnTheNextTick()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.HearGunshot(new Vector3d(2 * Metre, 0, 0), 4 * Metre));
        soldier.Tick(0, default, rootMotionThisTick: 0.0);

        Assert.True(soldier.Alerted);
    }

    [Fact]
    public void ACombatAlertRaisesTheCombatPerceptionAlertOnTheNextTick()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveAlert(new Vector3d(2 * Metre, 0, 0), source: 7));
        soldier.Tick(0, default, rootMotionThisTick: 0.0);

        Assert.True(soldier.Alerted);
    }

    [Fact]
    public void DamageQueuesTakingDamageAndRaisesCombatOnTheNextTick()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d attacker = new(3 * Metre, 0, 0);

        Assert.True(soldier.ReceiveDamage(attacker));

        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(3 * Metre, soldier.LastEventDistance);
        soldier.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.True(soldier.Alerted);
    }

    [Fact]
    public void RejectedDamageDoesNotLatchPerceptionOrPropagateAnAlert()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Events.SetEnabled(AiEvent.TakingDamage, false);
        Vector3d attacker = new(3 * Metre, 0, 0);

        Assert.False(soldier.ReceiveDamage(attacker));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
        Assert.Equal(0.0, soldier.LastEventDistance);

        soldier.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.False(soldier.Alerted);
    }

    [Fact]
    public void ARejectedLethalBulletDamageDoesNotQueueDamageOrSquadAlert()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Events.SetEnabled(AiEvent.TakingDamage, false);

        Assert.False(soldier.ReceiveDamage(new Vector3d(4 * Metre, 0, 0)));
        soldier.Kill();

        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
    }

    [Fact]
    public void AnAcceptedLethalExplosionDamageQueuesDamageBeforeDeath()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveDamage(new Vector3d(2 * Metre, 0, 0)));
        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.TakingDamage));
        soldier.Kill();

        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
    }

    [Fact]
    public void ADeadSoldierDoesNotReceiveDamageOrSquadAlerts()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Kill();

        Assert.False(soldier.ReceiveDamage(default));

        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.False(soldier.ReceiveAlert(default, source: 9));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
    }

    [Fact]
    public void SquadAlertsHonorRangeDeadAndDuplicateEventSemantics()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveAlert(new Vector3d(Metre, 0, 0), source: 3));
        Assert.False(soldier.ReceiveAlert(new Vector3d(Metre, 0, 0), source: 4));
        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.Alert));

        AiSoldier boundary = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.True(boundary.ReceiveAlert(new Vector3d(AiSoldier.AlertPingRadiusInUnits, 0, 0)));

        soldier.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.True(soldier.Alerted);
        Assert.False(soldier.ReceiveAlert(default, source: 5));

        AiSoldier outOfRange = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.False(outOfRange.ReceiveAlert(new Vector3d(AiSoldier.AlertPingRadiusInUnits + 1, 0, 0)));

        AiSoldier dead = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        dead.Kill();
        Assert.False(dead.ReceiveAlert(default));
    }

    /// <summary>A dead soldier hears nothing.</summary>
    [RequiresGameDataFact]
    public void ADeadSoldierHearsNothing()
    {
        AiSoldier soldier = Soldier(out Vector3d position);
        soldier.Kill();

        Assert.False(soldier.HearGunshot(Away(position, 1 * Metre), silenced: false));
    }

    [Fact]
    public void ABulletPassingInsideTwoMetresPostsGunshotMissAtItsClosestPoint()
    {
        Vector3d feet = new(10_000, 20_000, 30_000);
        AiSoldier soldier = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Vector3d eye = soldier.EyePosition();
        Vector3d start = new(eye.X - Metre, eye.Y + Metre, eye.Z);
        Vector3d end = new(eye.X + Metre, eye.Y + Metre, eye.Z);

        Assert.True(soldier.ReceiveGunshotMiss(start, end, new Vector3d(0, 0, 0)));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.GunshotMiss);
        Assert.NotNull(queued);
        AiEventRecord posted = queued.Value;
        Assert.Equal(new Vector3d(eye.X, eye.Y + Metre, eye.Z), posted.Position);
        Assert.Equal(-1, posted.Source);
        Assert.Equal(Metre, soldier.LastEventDistance);
    }

    [Fact]
    public void GunshotMissUsesTheOriginalStrictTwoMetreBoundary()
    {
        AiSoldier inside = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d insideEye = inside.EyePosition();
        Assert.True(inside.ReceiveGunshotMiss(
            Away(insideEye, (2 * Metre) - 1),
            Away(insideEye, (2 * Metre) - 1),
            default));

        AiSoldier boundary = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d boundaryEye = boundary.EyePosition();
        Assert.False(boundary.ReceiveGunshotMiss(
            Away(boundaryEye, 2 * Metre),
            Away(boundaryEye, 2 * Metre),
            default));
        Assert.Equal(0, boundary.Events.PendingCount(AiEvent.GunshotMiss));
    }

    [Fact]
    public void GroundImpactUsesTheCapturedInclusiveFiveMetreRadiusAndRealContact()
    {
        Vector3d feet = new(10_000, 20_000, 30_000);
        Vector3d shooter = new(-1, -2, -3);
        AiSoldier inside = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Vector3d impact = Away(feet, 5 * Metre);

        Assert.True(inside.ReceiveGroundImpact(impact, shooter));

        AiEventRecord? queued = inside.Events.HeadOf(AiEvent.GroundImpact);
        Assert.NotNull(queued);
        Assert.Equal(impact, queued.Value.Position);
        Assert.Equal(5 * Metre, queued.Value.Radius);
        Assert.Equal((AiEventFilter)0x46, queued.Value.Filter);
        Assert.Equal(-1, queued.Value.Source);
        Assert.Equal(-1, queued.Value.Subject);

        AiSoldier outside = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveGroundImpact(Away(feet, (5 * Metre) + 1), shooter));
        Assert.Equal(0, outside.Events.PendingCount(AiEvent.GroundImpact));
    }

    [Fact]
    public void ADeadSoldierIgnoresGroundImpact()
    {
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Kill();

        Assert.False(soldier.ReceiveGroundImpact(default, default));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.GroundImpact));
    }

    [Fact]
    public void DamagingFallImpactUsesTheRetailTenMetreRadius()
    {
        Vector3d feet = new(10_000, 20_000, 30_000);
        Vector3d impact = Away(feet, HumanFallDamage.DamagingImpactHearingRadiusUnits);
        AiSoldier soldier = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveGroundImpact(
            impact,
            shooterPosition: impact,
            HumanFallDamage.DamagingImpactHearingRadiusUnits));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.GroundImpact);
        Assert.NotNull(queued);
        Assert.Equal(HumanFallDamage.DamagingImpactHearingRadiusUnits, queued.Value.Radius);

        AiSoldier outside = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveGroundImpact(
            Away(feet, HumanFallDamage.DamagingImpactHearingRadiusUnits + 1),
            shooterPosition: impact,
            HumanFallDamage.DamagingImpactHearingRadiusUnits));
    }

    private static Vector3d Away(Vector3d from, double units) => new(from.X + units, from.Y, from.Z);

    private static AiSoldier Soldier(out Vector3d position)
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
        position = graph.Nodes[0].Position;
        return new AiSoldier(graph, position, facing: 0f, phase: 0, seed: 1);
    }
}
