using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

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
        Igi1HumanAi soldier = Soldier(out Vector3d position);

        Assert.True(soldier.HearGunshot(Away(position, 10 * Metre), silenced: false));
        Assert.True(soldier.Events.PendingCount(AiEvent.Gunshot) > 0);
        Assert.Equal(-1, soldier.Events.HeadOf(AiEvent.Gunshot)?.Source);
    }

    /// <summary>A shot forty metres off is out of earshot and queues nothing.</summary>
    [RequiresGameDataFact]
    public void ASoldierDoesNotHearAShotBeyondThirtyMetres()
    {
        Igi1HumanAi soldier = Soldier(out Vector3d position);

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
        Igi1HumanAi atSource = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.True(atSource.HearGunshot(default, 0.0));

        Igi1HumanAi negativeRange = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.True(negativeRange.HearGunshot(default, -1.0));
        Assert.Equal(1, negativeRange.Events.PendingCount(AiEvent.Gunshot));
    }

    [Fact]
    public void DisabledGunshotAndAlertEventsAreNotReportedOrLatched()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Events.SetEnabled(AiEvent.Gunshot, false);
        Assert.False(soldier.HearGunshot(default, 1.0));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Gunshot));

        soldier.Events.SetEnabled(AiEvent.Alert, false);
        Assert.False(soldier.ReceiveAlert(default, source: 4));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));

        soldier.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.False(soldier.Alerted);
    }

    /// <summary>
    /// Hearing is news, not alertness: <c>0x44F410</c> sets the alert latch from the alarm and from a combat
    /// target the scan still registers, never from an event.
    /// </summary>
    [Fact]
    public void AHeardGunshotQueuesTheEventWithoutLatchingTheAlert()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.HearGunshot(new Vector3d(2 * Metre, 0, 0), 4 * Metre));
        soldier.Tick(0, default, rootMotionThisTick: 0.0);

        Assert.False(soldier.Alerted);
    }

    [Fact]
    public void ASquadAlertQueuesTheEventWithoutLatchingTheAlert()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveAlert(new Vector3d(2 * Metre, 0, 0), source: 7));
        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.Alert));
        soldier.Tick(0, default, rootMotionThisTick: 0.0);

        Assert.False(soldier.Alerted);
    }

    [Fact]
    public void DamageQueuesTakingDamageWithoutLatchingTheAlert()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d attacker = new(3 * Metre, 0, 0);

        Assert.True(soldier.ReceiveDamage(attacker));

        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(3 * Metre, soldier.LastEventDistance);
        soldier.Tick(0, default, rootMotionThisTick: 0.0);
        Assert.False(soldier.Alerted);
    }

    [Fact]
    public void RejectedDamageDoesNotLatchPerceptionOrPropagateAnAlert()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
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
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Events.SetEnabled(AiEvent.TakingDamage, false);

        Assert.False(soldier.ReceiveDamage(new Vector3d(4 * Metre, 0, 0)));
        soldier.Kill();

        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
    }

    [Fact]
    public void AnAcceptedLethalExplosionDamageQueuesDamageBeforeDeath()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveDamage(new Vector3d(2 * Metre, 0, 0)));
        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.TakingDamage));
        soldier.Kill();

        Assert.Equal(1, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
    }

    [Fact]
    public void ADeadSoldierDoesNotReceiveDamageOrSquadAlerts()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Kill();

        Assert.False(soldier.ReceiveDamage(default));

        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.TakingDamage));
        Assert.False(soldier.ReceiveAlert(default, source: 9));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.Alert));
    }

    /// <summary>
    /// The alert ping reaches seven metres and nothing else refuses it: admission (<c>0x450110</c>) has no
    /// "already alert" or "one queued" gate, so a second fighting neighbour's ping queues beside the first.
    /// </summary>
    [Fact]
    public void SquadAlertsHonorRangeAndDeathOnly()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveAlert(new Vector3d(Metre, 0, 0), source: 3));
        Assert.True(soldier.ReceiveAlert(new Vector3d(Metre, 0, 0), source: 4));
        Assert.Equal(2, soldier.Events.PendingCount(AiEvent.Alert));

        Igi1HumanAi boundary = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.True(boundary.ReceiveAlert(new Vector3d(Igi1HumanAi.AlertPingRadiusInUnits, 0, 0)));

        Igi1HumanAi outOfRange = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.False(outOfRange.ReceiveAlert(new Vector3d(Igi1HumanAi.AlertPingRadiusInUnits + 1, 0, 0)));

        Igi1HumanAi dead = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        dead.Kill();
        Assert.False(dead.ReceiveAlert(default));
    }

    /// <summary>A dead soldier hears nothing.</summary>
    [RequiresGameDataFact]
    public void ADeadSoldierHearsNothing()
    {
        Igi1HumanAi soldier = Soldier(out Vector3d position);
        soldier.Kill();

        Assert.False(soldier.HearGunshot(Away(position, 1 * Metre), silenced: false));
    }

    [Fact]
    public void ExplosionUsesTheNativeTypeTwentyOneAndAuthoredEnvelope()
    {
        Vector3d position = new(10_000, 20_000, 30_000);
        Igi1HumanAi soldier = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        double range = 5 * Metre;
        Vector3d centre = Away(position, range);

        Assert.True(soldier.ReceiveExplosion(centre, range));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.Explosion);
        Assert.NotNull(queued);
        Assert.Equal(AiEvent.Explosion, queued.Value.Type);
        Assert.Equal(AiEventFilter.InRange, queued.Value.Filter);
        Assert.Equal((float)range, queued.Value.Radius);
        Assert.Equal(centre, queued.Value.Position);
        Assert.Equal(-1, queued.Value.Source);
    }

    [Fact]
    public void ExplosionDoesNotCrossItsAuthoredRangeOrReachDeadSoldiers()
    {
        Vector3d position = new(10_000, 20_000, 30_000);
        double range = 5 * Metre;
        Igi1HumanAi outside = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        Igi1HumanAi dead = new(graph: null, position, facing: 0f, phase: 0, seed: 1);
        dead.Kill();

        Assert.False(outside.ReceiveExplosion(Away(position, range + 1), range));
        Assert.Equal(0, outside.Events.PendingCount(AiEvent.Explosion));
        Assert.False(dead.ReceiveExplosion(position, range));
        Assert.Equal(0, dead.Events.PendingCount(AiEvent.Explosion));
    }

    [Fact]
    public void ABulletPassingInsideTwoMetresPostsGunshotMissAtItsClosestPoint()
    {
        Vector3d feet = new(10_000, 20_000, 30_000);
        Igi1HumanAi soldier = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
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
        Igi1HumanAi inside = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d insideEye = inside.EyePosition();
        Assert.True(inside.ReceiveGunshotMiss(
            Away(insideEye, (2 * Metre) - 1),
            Away(insideEye, (2 * Metre) - 1),
            default));

        Igi1HumanAi boundary = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
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
        Igi1HumanAi inside = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Vector3d impact = Away(feet, 5 * Metre);

        Assert.True(inside.ReceiveGroundImpact(impact, shooter));

        AiEventRecord? queued = inside.Events.HeadOf(AiEvent.GroundImpact);
        Assert.NotNull(queued);
        Assert.Equal(impact, queued.Value.Position);
        Assert.Equal(5 * Metre, queued.Value.Radius);
        Assert.Equal((AiEventFilter)0x46, queued.Value.Filter);
        Assert.Equal(-1, queued.Value.Source);
        Assert.Equal(-1, queued.Value.Subject);

        Igi1HumanAi outside = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveGroundImpact(Away(feet, (5 * Metre) + 1), shooter));
        Assert.Equal(0, outside.Events.PendingCount(AiEvent.GroundImpact));
    }

    [Fact]
    public void ADeadSoldierIgnoresGroundImpact()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        soldier.Kill();

        Assert.False(soldier.ReceiveGroundImpact(default, default));
        Assert.Equal(0, soldier.Events.PendingCount(AiEvent.GroundImpact));
    }

    [Fact]
    public void GrenadeThrownUsesTheRetailInViewEvent()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d eye = soldier.EyePosition();
        Vector3d visible = new(0, 4 * Metre, eye.Z);

        Assert.True(soldier.ReceiveGrenadeThrown(visible));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.GrenadeThrown);
        Assert.NotNull(queued);
        Assert.Equal(visible, queued.Value.Position);
        Assert.Equal(AiEventFilter.InView, queued.Value.Filter);
        Assert.Equal(-1f, queued.Value.Radius);
        Assert.Equal(0, queued.Value.Source);
        Assert.Equal(0, queued.Value.Subject);

        Igi1HumanAi behind = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.False(behind.ReceiveGrenadeThrown(new Vector3d(0, -Metre, behind.EyePosition().Z)));
        Assert.Equal(0, behind.Events.PendingCount(AiEvent.GrenadeThrown));
    }

    [Fact]
    public void GrenadeLandUsesTheNativeFiveMetreInRangeEvent()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d contact = new(5 * Metre, 0, 0);

        Assert.True(soldier.ReceiveGrenadeLand(contact));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.GrenadeLand);
        Assert.NotNull(queued);
        Assert.Equal(contact, queued.Value.Position);
        Assert.Equal(AiEventFilter.InRange, queued.Value.Filter);
        Assert.Equal(Igi1HumanAi.GrenadeLandRadiusInUnits, queued.Value.Radius);
        Assert.Equal(0, queued.Value.Source);
        Assert.Equal(0, queued.Value.Subject);

        Igi1HumanAi outside = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveGrenadeLand(new Vector3d((5 * Metre) + 1, 0, 0)));
        Assert.Equal(0, outside.Events.PendingCount(AiEvent.GrenadeLand));
    }

    [Fact]
    public void FlashbangUsesTheNativeTenMetreViewAndRangeEvent()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d flash = new(0, 9 * Metre, soldier.EyePosition().Z);

        Assert.True(soldier.ReceiveFlashbang(flash));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.Flashbang);
        Assert.NotNull(queued);
        Assert.Equal(flash, queued.Value.Position);
        Assert.Equal(AiEventFilter.InView | AiEventFilter.InRange, queued.Value.Filter);
        Assert.Equal(Igi1HumanAi.FlashbangEventRadiusInUnits, queued.Value.Radius);

        Igi1HumanAi outside = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveFlashbang(new Vector3d(0, 11 * Metre, outside.EyePosition().Z)));
        Assert.Equal(0, outside.Events.PendingCount(AiEvent.Flashbang));
    }

    [Fact]
    public void ExplosionUsesTheNativeFiveMetreEnvelope()
    {
        Igi1HumanAi soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Vector3d explosion = new(5 * Metre, 0, 0);

        Assert.True(soldier.ReceiveExplosion(explosion));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.Explosion);
        Assert.NotNull(queued);
        Assert.Equal(AiEventFilter.InRange, queued.Value.Filter);
        Assert.Equal(Igi1HumanAi.GrenadeLandRadiusInUnits, queued.Value.Radius);

        Igi1HumanAi outside = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveExplosion(new Vector3d((5 * Metre) + 1, 0, 0)));
        Assert.Equal(0, outside.Events.PendingCount(AiEvent.Explosion));
    }

    [Fact]
    public void DamagingFallImpactUsesTheRetailTenMetreRadius()
    {
        Vector3d feet = new(10_000, 20_000, 30_000);
        Vector3d impact = Away(feet, HumanFallDamage.DamagingImpactHearingRadiusUnits);
        Igi1HumanAi soldier = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveGroundImpact(
            impact,
            shooterPosition: impact,
            HumanFallDamage.DamagingImpactHearingRadiusUnits));

        AiEventRecord? queued = soldier.Events.HeadOf(AiEvent.GroundImpact);
        Assert.NotNull(queued);
        Assert.Equal(HumanFallDamage.DamagingImpactHearingRadiusUnits, queued.Value.Radius);

        Igi1HumanAi outside = new(graph: null, feet, facing: 0f, phase: 0, seed: 1);
        Assert.False(outside.ReceiveGroundImpact(
            Away(feet, HumanFallDamage.DamagingImpactHearingRadiusUnits + 1),
            shooterPosition: impact,
            HumanFallDamage.DamagingImpactHearingRadiusUnits));
    }

    private static Vector3d Away(Vector3d from, double units) => new(from.X + units, from.Y, from.Z);

    private static Igi1HumanAi Soldier(out Vector3d position)
    {
        string path = Path.Combine(
            GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
        position = graph.Nodes[0].Position;
        return new Igi1HumanAi(graph, position, facing: 0f, phase: 0, seed: 1);
    }
}
