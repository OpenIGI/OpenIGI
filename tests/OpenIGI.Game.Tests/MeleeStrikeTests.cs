using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Unit and integration tests for close-combat melee strikes (issue #235).
/// </summary>
public sealed class MeleeStrikeTests
{
    private const double Metre = 4096.0;

    [Fact]
    public void MeleeStrikeZoneIsThirteenAndCarriesUnitMultiplier()
    {
        Assert.Equal(13, MeleeStrike.Zone);
        Assert.Equal(HumanDamage.FrontZone, MeleeStrike.Zone);

        // Zone 13 carries a 1.0 multiplier for both player and soldier tables.
        Assert.Equal(1.0f, HumanHitZoneTable.Player[MeleeStrike.Zone].Multiplier);
        Assert.Equal(1.0f, HumanHitZoneTable.Soldier[MeleeStrike.Zone].Multiplier);
    }

    [Theory]
    [InlineData(0, 0.75f)]  // Easy (GD_1) -> x0.75
    [InlineData(1, 1.0f)]   // Normal (GD_2) -> x1.0
    [InlineData(2, 1.25f)]  // Hard (GD_3) -> x1.25
    public void MeleeDamageScalesWithPlayerDifficulty(int difficulty, float expectedScale)
    {
        float damageScale = HumanHealth.DifficultyDamageScale(difficulty);
        Assert.Equal(expectedScale, damageScale);

        HumanHealth health = new(HumanHitZoneTable.Player, maximumHealth: 1.0f)
        {
            DamageScale = damageScale,
        };

        const float strikeDamageFactor = 0.5f;
        float dealt = health.Apply(MeleeStrike.Zone, strikeDamageFactor);

        Assert.Equal(strikeDamageFactor * expectedScale, dealt);
        Assert.Equal(dealt, health.Damage);
        Assert.Equal(1, health.Hits);
        Assert.Equal(1.0f - dealt, health.RemainingFraction);
    }

    [Fact]
    public void RepeatedMeleeStrikesAccumulateDamageAndCanBeLethal()
    {
        HumanHealth health = new(HumanHitZoneTable.Player, maximumHealth: 1.0f)
        {
            DamageScale = 1.0f,
        };

        health.Apply(MeleeStrike.Zone, 0.4f);
        Assert.False(health.IsDead);
        Assert.Equal(0.4f, health.Damage, 3);
        Assert.Equal(0.6f, health.RemainingFraction, 3);

        health.Apply(MeleeStrike.Zone, 0.4f);
        Assert.False(health.IsDead);
        Assert.Equal(0.8f, health.Damage, 3);
        Assert.Equal(0.2f, health.RemainingFraction, 3);

        health.Apply(MeleeStrike.Zone, 0.4f);
        Assert.True(health.IsDead);
        Assert.True(health.Damage >= 1.0f);
        Assert.Equal(0f, health.RemainingFraction);
        Assert.Equal(3, health.Hits);
    }

    [Fact]
    public void AiSoldierCloseCombatConsumesDamageOnceAndResets()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemyFeet = new(position.X, position.Y + (5 * Metre / 4), position.Z);
        Vector3d enemyEye = new(enemyFeet.X, enemyFeet.Y, enemyFeet.Z + (5 * Metre / 4));

        const float hitProbability = 1.0f; // 100% chance to connect
        const float damage = 0.6f;
        AiSoldier soldier = PatrolSoldier(position, hitProbability, damage);

        int tick = 1;
        while (tick < 20 && soldier.PatrolAction != AiPatrolAction.CloseCombat)
        {
            soldier.Tick(tick++, enemyEye, 0.0, enemyFeet: enemyFeet);
        }

        Assert.Equal(AiPatrolAction.CloseCombat, soldier.PatrolAction);

        // First consume succeeds with full damage.
        bool hasStrike = soldier.ConsumeMeleeStrike(out float strikeDamage);
        Assert.True(hasStrike);
        Assert.Equal(damage, strikeDamage);

        // Second consume on the same strike is empty (one-shot).
        bool secondStrike = soldier.ConsumeMeleeStrike(out float secondDamage);
        Assert.False(secondStrike);
        Assert.Equal(0f, secondDamage);
    }

    [Fact]
    public void AiSoldierCloseCombatProducesNoDamageWhenProbabilityFails()
    {
        Vector3d position = new(1000, 2000, 3000);
        Vector3d enemyFeet = new(position.X, position.Y + (5 * Metre / 4), position.Z);
        Vector3d enemyEye = new(enemyFeet.X, enemyFeet.Y, enemyFeet.Z + (5 * Metre / 4));

        const float hitProbability = 0f; // 0% chance to connect
        const float damage = 0.6f;
        AiSoldier soldier = PatrolSoldier(position, hitProbability, damage);

        int tick = 1;
        while (tick < 20 && soldier.PatrolAction != AiPatrolAction.CloseCombat)
        {
            soldier.Tick(tick++, enemyEye, 0.0, enemyFeet: enemyFeet);
        }

        Assert.Equal(AiPatrolAction.CloseCombat, soldier.PatrolAction);

        // Consume returns false because probability roll yielded 0 damage.
        bool hasStrike = soldier.ConsumeMeleeStrike(out float strikeDamage);
        Assert.False(hasStrike);
        Assert.Equal(0f, strikeDamage);
    }

    private static AiSoldier PatrolSoldier(
        Vector3d position,
        float hitProbability,
        float damage,
        int seed = 1)
    {
        AiSoldier soldier = new(graph: null, position, facing: 0f, phase: 0, seed: seed);
        soldier.SetCombat(
            AiType.PatrolAk,
            new AiCombatParameters(new HumanAiConfigItemTask
            {
                AiTypeName = "AITYPE_PATROL_AK",
                DifficultyName = "GD_2",
                TargetingTimeoutSeconds = 15f,
                TrackingDistanceMetres = 100f,
                GrenadeThrowProbability = 0f,
                CloseCombatHitProbability = hitProbability,
                CloseCombatDamage = damage,
                MinimumClipFraction = 0.8f,
                MaximumRoundsPerSequence = 30,
                HitProbabilityAtMinimumRange = 1f,
                HitProbabilityAtMaximumRange = 0.3f,
                AimPrecision = 0.6f,
            }),
            () => new AiCombatWeaponState(30, 30, 30, WeaponCategory.Gun));
        return soldier;
    }

    [RequiresGameDataFact]
    public void Animation70CarriesEvent1052ForGunpunchAudioAtImpactTime()
    {
        string path = System.IO.Path.Combine(OpenIGI.TestSupport.GameData.RequireRoot(), "common", "anims", "001.iff");
        Formats.Animation.BoneAnimationSet set = Formats.Animation.BoneAnimationSet.Read(System.IO.File.ReadAllBytes(path));
        Assert.True(set.TryGet(70, out Formats.Animation.BoneAnimation anim));
        Assert.Equal(5760f, anim.Duration);

        Formats.Animation.BoneAnimationEvent meleeEvent = Assert.Single(anim.Events, e => e.EventId == 1052);
        Assert.Equal(1280f, meleeEvent.Time);

        HumanSoldierTask task = new()
        {
            Model = "test",
            BoneHierarchy = set.Skeleton.Id,
            StandAnimation = 2,
            Position = default,
            Gamma = 0f,
        };
        SoldierView view = new(task, set);
        Assert.True(view.PlayStateAnimation(70, loops: false));

        // Tick at 30 fps: Event 1052 at 1280.0 should fire exactly at tick 8 (1280 / 160).
        int eventFiredTick = -1;
        for (int tick = 1; tick <= 15; tick++)
        {
            view.Tick();
            if (view.Bones.SourceOf(0)?.CrossedEvent(1052) == true)
            {
                eventFiredTick = tick;
            }
        }

        Assert.Equal(8, eventFiredTick);
    }
}
