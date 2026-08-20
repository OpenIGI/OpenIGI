using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// How each kind of missile steers, and what its impact does.
/// </summary>
public sealed class MissileFlightTests
{
    [Fact]
    public void OnlyTheBombIsUnsteered()
    {
        // The bomb has no case in the switch at all - it is not steered badly, it is not steered, and
        // falls under the physics it inherits.
        Assert.False(MissileFlight.IsSteered(MissileType.Bomb));

        foreach (MissileType type in Enum.GetValues<MissileType>().Where(t => t != MissileType.Bomb))
        {
            Assert.True(MissileFlight.IsSteered(type), type.ToString());
        }
    }

    [Fact]
    public void DummyTableMuzzleVelocityDoesNotLaunchAtFiveMetresPerSecond()
    {
        Assert.Equal(
            MissileFlight.FallbackLaunchMetresPerSecond,
            MissileFlight.LaunchSpeedMetresPerSecond(new WeaponType { MuzzleVelocity = 5f }));
        Assert.Equal(
            290f,
            MissileFlight.LaunchSpeedMetresPerSecond(new WeaponType { MuzzleVelocity = 290f }));
    }

    [Fact]
    public void LauncherTypeComesFromTheBindingNotAHardCodedAntiTank()
    {
        Assert.Equal(
            MissileType.MissileDirect,
            MissileFlight.TypeForLauncher(new WeaponType { ScriptId = "WEAPON_ID_RPG18", DisplayName = "RPG-18" }));
        Assert.Equal(
            MissileType.MissileAntiTank,
            MissileFlight.TypeForLauncher(new WeaponType { DisplayName = "TOW AntiTank" }));
        Assert.Equal(
            MissileType.HindHelicopter,
            MissileFlight.TypeForLauncher(new WeaponType { ScriptId = "WEAPON_ID_HIND" }));
    }

    [Fact]
    public void AntiTankNearRegimeUsesTheThreeDimensionalBallisticLob()
    {
        // 0x004280B0 flattens Z only to choose the regime, then 0x00428040 lobs with the 3D length.
        Vector3d origin = new(0, 0, 0);
        Vector3d target = new(40960, 0, 40960);
        float horizontal = 40960f;
        float threeD = (float)Math.Sqrt((40960d * 40960d) + (40960d * 40960d));
        Assert.True(horizontal < MissileFlight.AntiTankDirectRangeUnits);

        float height = MissileFlight.AntiTankAimHeight(40960f, horizontal, threeD);
        Assert.Equal(MissileFlight.BallisticAimHeight(40960f, threeD), height);
        Assert.True(height > MissileFlight.BallisticAimHeight(40960f, horizontal));
    }

    [Fact]
    public void TheDirectMissileFliesStraightOnPurposeRatherThanByOmission()
    {
        // It does have a case; that case just clears both steering inputs. Worth distinguishing from
        // the bomb, which has no case.
        Assert.True(MissileFlight.IsSteered(MissileType.MissileDirect));
    }

    [Fact]
    public void LaunchVelocityUsesTheGuidanceRegimeRatherThanTheWeaponName()
    {
        Vector3d origin = new(0, 0, 0);
        Vector3d target = new(409600, 0, 0);

        Vector3f ballistic = MissileFlight.LaunchVelocity(origin, target, MissileType.MissileBallistic, 30f);
        Assert.True(ballistic.Z > 0f);
        Assert.Equal(30f * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond, Length(ballistic), 0.01f);

        Vector3f antiTank = MissileFlight.LaunchVelocity(origin, target, MissileType.MissileAntiTank, 30f);
        Assert.True(antiTank.Z > ballistic.Z);

        Vector3f direct = MissileFlight.LaunchVelocity(origin, target, MissileType.MissileDirect, 30f);
        Assert.Equal(0f, direct.Z);
        Assert.Equal(30f * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond, direct.X, 0.01f);
    }

    [Fact]
    public void HindLaunchUsesItsThreeMetreForwardOffset()
    {
        Vector3d aim = MissileFlight.AimPoint(
            Vector3d.Zero,
            new Vector3d(100, 200, 300),
            MissileType.HindHelicopter,
            Vector3f.UnitY);

        Assert.Equal(MissileFlight.HindOffsetUnits, aim.Y);
        Assert.Equal(0d, aim.X);
        Assert.Equal(0d, aim.Z);
    }

    private static float Length(Vector3f value) =>
        MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

    [Fact]
    public void TheBallisticLobGrowsWithTheRange()
    {
        // An eighth of the distance, so a point-blank shot is nearly flat and a long one arcs high.
        Assert.Equal(0f, MissileFlight.BallisticAimHeight(0f, 0f));
        Assert.Equal(512f, MissileFlight.BallisticAimHeight(0f, 4096f));
        Assert.Equal(5120f, MissileFlight.BallisticAimHeight(0f, 40960f));

        // The target's own height is carried through.
        Assert.Equal(1000f + 512f, MissileFlight.BallisticAimHeight(1000f, 4096f));
    }

    [Fact]
    public void TheAntiTankMissileLobsInsideThirtyMetres()
    {
        Assert.Equal(30f, MissileFlight.AntiTankDirectRangeUnits / MissileFlight.UnitsPerMetre);

        // Inside the boundary it is exactly the ballistic lob.
        Assert.Equal(
            MissileFlight.BallisticAimHeight(0f, 40960f),
            MissileFlight.AntiTankAimHeight(0f, 40960f));

        // The test is "<=", so exactly thirty metres still lobs.
        Assert.Equal(
            MissileFlight.BallisticAimHeight(0f, MissileFlight.AntiTankDirectRangeUnits),
            MissileFlight.AntiTankAimHeight(0f, MissileFlight.AntiTankDirectRangeUnits));
    }

    [Fact]
    public void BeyondThirtyMetresItClimbsAFixedTwentyFiveAndDives()
    {
        Assert.Equal(25f, MissileFlight.AntiTankClimbUnits / MissileFlight.UnitsPerMetre);

        float justBeyond = MissileFlight.AntiTankDirectRangeUnits + 1f;
        Assert.Equal(MissileFlight.AntiTankClimbUnits, MissileFlight.AntiTankAimHeight(0f, justBeyond));

        // Fixed, not proportional - ten times the range gives the same climb.
        Assert.Equal(
            MissileFlight.AntiTankAimHeight(0f, justBeyond),
            MissileFlight.AntiTankAimHeight(0f, justBeyond * 10f));
    }

    [Fact]
    public void TheBoundaryIsADiscontinuity()
    {
        // At the boundary the lob is 30/8 metres; a hair past it the climb jumps to 25. So an
        // anti-tank missile's trajectory changes abruptly rather than blending.
        float atBoundary = MissileFlight.AntiTankAimHeight(0f, MissileFlight.AntiTankDirectRangeUnits);
        float pastBoundary = MissileFlight.AntiTankAimHeight(0f, MissileFlight.AntiTankDirectRangeUnits + 1f);

        Assert.Equal(15360f, atBoundary);
        Assert.Equal(102400f, pastBoundary);
        Assert.True(pastBoundary > atBoundary * 6f);
    }

    [Fact]
    public void AMissileBlastIsSmallerThanAGrenadesButFarStronger()
    {
        // Five times the damage over half the radius - a missile is a precise, powerful blast where a
        // grenade is a wide, weak one.
        Assert.Equal(5f, MissileExplosion.DamageFactor);
        Assert.Equal(5f, MissileExplosion.DamageFactor);

        float grenadeRadius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);
        Assert.True(MissileExplosion.RadiusUnits < grenadeRadius);
    }

    [Fact]
    public void OnlyTheMissileActuallyTapers()
    {
        // The missile's falloff is fixed at 1.75 metres; the grenade uses its authored config falloff.
        Assert.Equal(1.75f, MissileExplosion.FalloffUnits / MissileFlight.UnitsPerMetre);

        // Halfway across the falloff gives half the damage.
        float half = ExplosionDamage.Dose(
            distanceInUnits: MissileExplosion.RadiusUnits + (MissileExplosion.FalloffUnits / 2f),
            bodyRadiusInUnits: 0f,
            radiusInUnits: MissileExplosion.RadiusUnits,
            falloffInUnits: MissileExplosion.FalloffUnits,
            damageFactor: MissileExplosion.DamageFactor);

        Assert.Equal(MissileExplosion.DamageFactor / 2f, half, 1e-3f);
    }

    [Fact]
    public void AMissileKillsOutrightAnywhereInsideItsRadius()
    {
        // Damage factor 5 against a maximum health of 1 and a torso multiplier of 1.
        float dose = ExplosionDamage.Dose(
            0f, 0f, MissileExplosion.RadiusUnits, MissileExplosion.FalloffUnits, MissileExplosion.DamageFactor);

        Assert.True(dose >= Player.HumanHitZoneTable.MaximumHealth);
        Assert.Equal(1, Player.HumanHitZoneTable.HitsToKill(dose, 1f));
    }

    [Fact]
    public void TheMissileBurstsTenTimesWhereAGrenadeBurstsOnce()
    {
        Assert.Equal(10, MissileExplosion.BurstCount);
        Assert.Equal(5, MissileExplosion.BurstDelayTicks);
    }
}
