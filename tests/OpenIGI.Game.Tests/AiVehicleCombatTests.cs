using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class AiVehicleCombatTests
{
    [Fact]
    public void VisiblePlayerIsRetainedThenFiredOnOneTickLater()
    {
        AiVehicleCombat combat = new();
        AiVehicleCombatInput input = Input(targetMetres: 37.0137f);

        Assert.Null(combat.Tick(input));
        Assert.Equal(AiVehicleCombat.TargetRetentionTicks, combat.RetentionTicks);

        VehicleCombatShot shot = Assert.IsType<VehicleCombatShot>(combat.Tick(input));
        Assert.Equal(MissileType.MissileDirect, shot.MissileType);
        Assert.Equal("110_02_1", shot.ModelName);
        Assert.Equal(MissileFlight.FallbackLaunchMetresPerSecond, shot.MuzzleVelocityMetresPerSecond);
    }

    [Fact]
    public void AuthoredRangeOcclusionAndCapabilityGateAcquisition()
    {
        Assert.Null(new AiVehicleCombat().Tick(Input(targetMetres: 50.01f)));
        Assert.Null(new AiVehicleCombat().Tick(Input(targetMetres: 37f) with { HasLineOfSight = false }));
        Assert.Null(new AiVehicleCombat().Tick(Input(targetMetres: 37f) with { CanFire = false }));
        Assert.Null(new AiVehicleCombat().Tick(Input(targetMetres: 37f) with { IsVehicleActive = false }));
    }

    [Fact]
    public void ApcUsesTwelveRpmAndRetainsTargetForSixHundredTicks()
    {
        AiVehicleCombat combat = new();
        AiVehicleCombatInput visible = Input(targetMetres: 37f);
        Assert.Null(combat.Tick(visible));
        Assert.NotNull(combat.Tick(visible));

        AiVehicleCombatInput hidden = visible with { HasLineOfSight = false };
        int retainedShots = 0;
        for (int i = 0; i < AiVehicleCombat.TargetRetentionTicks; i++)
        {
            retainedShots += combat.Tick(hidden) is null ? 0 : 1;
        }

        Assert.Equal(4, retainedShots);
        Assert.Equal(0, combat.RetentionTicks);
        Assert.Null(combat.Tick(hidden));
        Assert.Equal(150, GunTiming.TicksPerRound(12));
    }

    [Fact]
    public void TwelveRoundsPerMinuteReopensTheGunAfterOneHundredFiftyTicks()
    {
        AiVehicleCombat combat = new();
        AiVehicleCombatInput visible = Input(targetMetres: 37f);
        Assert.Null(combat.Tick(visible));
        Assert.NotNull(combat.Tick(visible));

        for (int tick = 1; tick < 150; tick++)
        {
            Assert.Null(combat.Tick(visible));
        }

        Assert.NotNull(combat.Tick(visible));
    }

    [Fact]
    public void RetainedTargetCanBeFiredOnAtItsLastKnownPositionWhileOccluded()
    {
        AiVehicleCombat combat = new();
        AiVehicleCombatInput visible = Input(targetMetres: 37f);

        Assert.Null(combat.Tick(visible));

        VehicleCombatShot shot = Assert.IsType<VehicleCombatShot>(combat.Tick(
            visible with { HasLineOfSight = false }));
        Assert.Equal(visible.TargetPosition, shot.Target);
        Assert.Equal(AiVehicleCombat.TargetRetentionTicks - 1, combat.RetentionTicks);
    }

    [Fact]
    public void AuthoredElevationConeFollowsVehiclePitchInsteadOfWorldHorizontal()
    {
        Matrix3f orientation = Matrix3f.RotationX(-20f * MathF.PI / 180f);
        Vector3d target = orientation.Transposed().Transform(
            new Vector3d(0d, 37d * WorldScene.UnitsPerMetre, 0d));
        AiVehicleCombatInput pitched = Input(targetMetres: 37f) with
        {
            VehicleOrientation = orientation,
            TargetPosition = target,
            ViewAlphaDegrees = 10f,
            ViewGammaDegrees = 10f,
        };

        AiVehicleCombat combat = new();
        Assert.Null(combat.Tick(pitched));
        Assert.NotNull(combat.Tick(pitched));
    }

    [Fact]
    public void CapturedMuzzleOffsetFollowsVehicleForwardAxis()
    {
        AiVehicleCombat combat = new();
        AiVehicleCombatInput input = Input(targetMetres: 37f);
        Assert.Null(combat.Tick(input));
        VehicleCombatShot shot = Assert.IsType<VehicleCombatShot>(combat.Tick(input));

        Assert.Equal(AiVehicleCombat.CapturedMuzzleForwardMetres, shot.Origin.Y / WorldScene.UnitsPerMetre, 3);
        Assert.Equal(AiVehicleCombat.CapturedMuzzleHeightMetres, shot.Origin.Z / WorldScene.UnitsPerMetre, 3);
    }

    [Fact]
    public void VehicleShotUsesMissileCollisionAndPlayerDifficultyDamageBoundary()
    {
        Vector3d feet = new(0d, 37d * WorldScene.UnitsPerMetre, 0d);
        AiVehicleCombatInput input = Input(targetMetres: 37f) with
        {
            TargetPosition = feet + new Vector3d(0d, 0d, 1.6d * WorldScene.UnitsPerMetre),
        };
        AiVehicleCombat combat = new();
        Assert.Null(combat.Tick(input));
        VehicleCombatShot shot = Assert.IsType<VehicleCombatShot>(combat.Tick(input));

        MissileSimulation missiles = new();
        Assert.True(missiles.Spawn(
            shot.Origin,
            shot.Target,
            shot.MissileType,
            shot.MuzzleVelocityMetresPerSecond,
            shot.Direction,
            shot.ModelName));

        bool Player(Vector3d from, Vector3d to, out HumanHit hit) =>
            HumanHitTest.TryCast(from, to, feet, Vector3f.UnitY, pose: null, out hit);
        for (int tick = 0; tick < 90 && missiles.Detonations.Count == 0; tick++)
        {
            missiles.Tick(walls: null, Player);
        }

        MissileDetonation blast = Assert.Single(missiles.Detonations);
        float dose = ExplosionDamage.Dose(
            (float)(feet - blast.Position).Length,
            ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
            blast.RadiusUnits,
            blast.FalloffUnits,
            blast.DamageFactor);
        Assert.True(dose > 0f);

        HumanHealth easy = new(HumanHitZoneTable.Player) { DamageScale = HumanHealth.DifficultyDamageScale(0) };
        HumanHealth hard = new(HumanHitZoneTable.Player) { DamageScale = HumanHealth.DifficultyDamageScale(2) };
        easy.Apply(ExplosionDamage.Zone, dose);
        hard.Apply(ExplosionDamage.Zone, dose);
        Assert.True(hard.Damage > easy.Damage);
    }

    private static AiVehicleCombatInput Input(float targetMetres) => new(
        Vector3d.Zero,
        Matrix3f.Identity,
        new Vector3d(0d, targetMetres * WorldScene.UnitsPerMetre, 0d),
        IsVehicleActive: true,
        CanFire: true,
        ViewAlphaDegrees: 180f,
        ViewGammaDegrees: 360f,
        ViewLengthMetres: 50f,
        HasLineOfSight: true,
        MuzzleVelocityMetresPerSecond: 5f,
        RoundsPerMinute: 12);
}
