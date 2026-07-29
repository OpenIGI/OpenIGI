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
    public void TheDirectMissileFliesStraightOnPurposeRatherThanByOmission()
    {
        // It does have a case; that case just clears both steering inputs. Worth distinguishing from
        // the bomb, which has no case.
        Assert.True(MissileFlight.IsSteered(MissileType.MissileDirect));
    }

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
        Assert.Equal(ExplosionDamage.DefaultGrenadeDamageFactor * 5f, MissileExplosion.DamageFactor);

        float grenadeRadius = ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres);
        Assert.Equal(MissileExplosion.RadiusUnits, grenadeRadius / 2f);
    }

    [Fact]
    public void OnlyTheMissileActuallyTapers()
    {
        // The grenade ships with a falloff of zero, so its damage is a step. The missile's is hardcoded
        // at 1.75 metres, so it is the one blast in the game whose damage actually falls off.
        Assert.Equal(0f, ExplosionDamage.DefaultGrenadeFalloffMetres);
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
