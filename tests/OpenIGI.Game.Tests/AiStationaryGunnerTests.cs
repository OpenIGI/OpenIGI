using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>IGI 1's emplacement cone and <c>0x48D340</c> mount arbitration.</summary>
public sealed class AiStationaryGunnerTests
{
    [Fact]
    public void TheGunnerIsSeatedWhereTheRetailProbeSawMissionEightsGunnerSit()
    {
        // Probe (IGI_PROBE_HOOKS=aigun), mission 8: soldier 403 takes StationaryGun 778 -- authored at
        // (45728924, 145302512, 175316352), Orientation (0, 0, 1.570796) -- and state 36's entry (0x45C8B0) hands
        // 0x4631A0 the seat (45726059.7, 145302511.9): 0.7 m straight behind the barrel. Its Z is the gunner's own.
        AiStationaryGun gun = new(
            778,
            new Vector3d(45728924.0, 145302512.0, 175316352.0),
            OpenIGI.Game.World.WorldScene.ToOrientation(new Vector3f(0f, 0f, 1.570796f)));

        Vector3d seat = gun.MountPosition;
        Assert.Equal(45726059.7, seat.X, 4.0);
        Assert.Equal(145302511.9, seat.Y, 4.0);
    }

    [Fact]
    public void AGunLeftTurnedSeatsItsNextGunnerBehindWhereItPoints()
    {
        // 0x444B30 reads the stand's frame as it stands (+112, turned by the traverse): the probe saw mission 8's 403
        // seated 45 degrees round gun 778 the second time he took it. The seat stays 0.7 m from the stand and the body
        // faces the way the barrel does.
        AiStationaryGun gun = new(778, default, Matrix3f.RotationZ(MathF.PI / 2f));
        Vector3d straight = gun.MountPosition;
        float straightFacing = gun.MountFacing;

        object previous = new();
        Assert.True(gun.TryMount(previous));
        gun.Steer(0f, MathF.PI / 4f);
        gun.TickOccupied();
        gun.Dismount(previous);

        Vector3d turned = gun.MountPosition;
        Assert.Equal(2867.2, Math.Sqrt((turned.X * turned.X) + (turned.Y * turned.Y)), 1.0);
        double between = Math.Atan2(turned.Y, turned.X) - Math.Atan2(straight.Y, straight.X);
        Assert.Equal(Math.PI / 4.0, Math.Abs(Math.IEEERemainder(between, 2.0 * Math.PI)), 3);
        Assert.Equal(MathF.PI / 4f, MathF.Abs(MathF.IEEERemainder(gun.MountFacing - straightFacing, 2f * MathF.PI)), 3);
    }

    [Theory]
    [InlineData(0.0, -20.0, 0.0)]
    [InlineData(10.0, -20.0, 3.0)]
    [InlineData(-15.0, -10.0, -2.0)]
    public void AnAiGunnerTurnsTheBarrelOntoItsTarget(double x, double y, double z)
    {
        // The barrel is the gun frame's middle column, the way 0x478BA0 fires.
        AiStationaryGun gun = new(778, new Vector3d(1000.0, 2000.0, 300.0), Matrix3f.RotationZ(0.7f));
        object gunner = new();
        Assert.True(gun.TryMount(gunner));
        Vector3d target = gun.GunPosition + gun.Orientation.Transposed().Transform(new Vector3d(x, y, z) * 4096.0);

        gun.AimAt(target);
        gun.TickOccupied();

        Matrix3f frame = gun.GunOrientation;
        Vector3d barrel = new(frame.M12, frame.M22, frame.M32);
        Vector3d toTarget = target - gun.GunPosition;
        double cosine = ((barrel.X * toTarget.X) + (barrel.Y * toTarget.Y) + (barrel.Z * toTarget.Z)) / (barrel.Length * toTarget.Length);
        Assert.True(cosine > 0.9999, $"the barrel is {Math.Acos(Math.Clamp(cosine, -1.0, 1.0)) * 180.0 / Math.PI:0.00} degrees off");
    }

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
    public void ScriptedActivationMountsWithoutTheConeGracePeriod()
    {
        object owner = new();
        AiGunnerCombat combat = new(owner);
        AiStationaryGun gun = Gun();

        Assert.True(combat.MountImmediately(gun));
        Assert.True(combat.IsMounted);
        Assert.Same(gun, combat.MountedGun);
        Assert.True(gun.IsOccupiedBy(owner));
        Assert.Equal(AiGunnerCombat.ConeGraceTicks, combat.ConeTicks);
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
