using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>IGI 1's emplacement cone and <c>0x48D340</c> mount arbitration.</summary>
public sealed class AiStationaryGunnerTests
{
    [Fact]
    public void TheConeFacesLocalNegativeYAndUsesStrictBoundaries()
    {
        AiStationaryGun gun = Gun(maximumGamma: 90f);

        Assert.True(gun.Contains(new Vector3d(0.0, -1000.0, 0.0)));
        Assert.False(gun.Contains(new Vector3d(0.0, 1000.0, 0.0)));

        // The retail comparisons are < and >, so the authored edge itself is outside.
        Assert.False(gun.Contains(new Vector3d(-1000.0, 0.0, 0.0)));
        Assert.False(gun.Contains(new Vector3d(1000.0, 0.0, 0.0)));
    }

    [Fact]
    public void TheAuthoredOrientationRotatesTheCone()
    {
        AiStationaryGun gun = new(
            17,
            default,
            Matrix3f.RotationZ(MathF.PI / 2f),
            maximumGammaDegrees: 20f);

        Assert.True(gun.Contains(new Vector3d(1000.0, 0.0, 0.0)));
        Assert.False(gun.Contains(new Vector3d(0.0, -1000.0, 0.0)));
        Assert.Equal(MathF.PI * 1.5f, gun.MountFacing, precision: 5);
    }

    [Fact]
    public void ThirtyInsideTicksPrecedeTheMountWrite()
    {
        object owner = new();
        AiGunnerCombat combat = new(owner);
        AiStationaryGun gun = Gun();
        Vector3d target = new(0.0, -1000.0, 0.0);
        combat.Begin();

        for (int tick = 0; tick < AiGunnerCombat.ConeGraceTicks; tick++)
        {
            AiGunnerCommand waiting = combat.Update(gun, target, hasDetectionSlot: true);
            Assert.True(waiting.RunCarriedWeapon);
            Assert.False(combat.IsMounted);
        }

        AiGunnerCommand mounting = combat.Update(gun, target, hasDetectionSlot: true);
        Assert.Equal(17f, mounting.MountChannelValue);
        Assert.True(mounting.MountedThisTick);
        Assert.True(combat.IsMounted);
        Assert.True(gun.IsOccupied);
    }

    [Fact]
    public void ThirtyOutsideTicksPrecedeTheDismountWrite()
    {
        object owner = new();
        AiGunnerCombat combat = new(owner);
        AiStationaryGun gun = Gun();
        Mount(combat, gun);

        Vector3d behind = new(0.0, 1000.0, 0.0);
        for (int tick = 0; tick < AiGunnerCombat.ConeGraceTicks; tick++)
        {
            AiGunnerCommand grace = combat.Update(gun, behind, hasDetectionSlot: true);
            Assert.True(grace.RunMountedWeapon);
            Assert.True(combat.IsMounted);
        }

        AiGunnerCommand leaving = combat.Update(gun, behind, hasDetectionSlot: true);
        Assert.Equal(1f, leaving.MountChannelValue);
        Assert.True(leaving.DismountedThisTick);
        Assert.False(combat.IsMounted);
        Assert.False(gun.IsOccupied);
    }

    [Fact]
    public void AWeaponOccupiedByAnotherHumanUsesTheCarriedGun()
    {
        AiStationaryGun gun = Gun();
        Assert.True(gun.TryMount(new object()));
        AiGunnerCombat combat = new(new object());
        combat.Begin();

        AiGunnerCommand command = combat.Update(
            gun,
            new Vector3d(0.0, -1000.0, 0.0),
            hasDetectionSlot: true);

        Assert.True(command.RunCarriedWeapon);
        Assert.False(combat.IsMounted);
        Assert.Equal(AiGunnerCombat.ConeGraceTicks, combat.ConeTicks);
    }

    [Fact]
    public void LosingTheDetectionSlotCleansUpTheMount()
    {
        object owner = new();
        AiGunnerCombat combat = new(owner);
        AiStationaryGun gun = Gun();
        Mount(combat, gun);

        AiGunnerCommand command = combat.Update(gun, default, hasDetectionSlot: false);

        Assert.True(command.Ended);
        Assert.Equal(1f, command.MountChannelValue);
        Assert.True(command.DismountedThisTick);
        Assert.False(combat.IsMounted);
        Assert.False(gun.IsOccupied);
    }

    [Fact]
    public void InfiniteAmmoStillChangesItsObservedRoundCounter()
    {
        AiStationaryGun gun = Gun();
        int before = gun.AmmunitionSample;

        Assert.True(gun.ConsumeRound());

        Assert.Equal(before - 1, gun.AmmunitionSample);
        Assert.Equal(-1, gun.RemainingAmmunition);
    }

    private static AiStationaryGun Gun(float maximumGamma = 90f) =>
        new(
            17,
            default,
            Matrix3f.Identity,
            maximumAlphaDegrees: 25f,
            minimumAlphaDegrees: -25f,
            maximumGammaDegrees: maximumGamma,
            ammunition: -1);

    private static void Mount(AiGunnerCombat combat, AiStationaryGun gun)
    {
        Vector3d target = new(0.0, -1000.0, 0.0);
        for (int tick = 0; tick <= AiGunnerCombat.ConeGraceTicks; tick++)
        {
            combat.Update(gun, target, hasDetectionSlot: true);
        }
        Assert.True(combat.IsMounted);

        // The first mounted/in-cone run writes 30 back into the shared grace counter.
        combat.Update(gun, target, hasDetectionSlot: true);
    }
}
