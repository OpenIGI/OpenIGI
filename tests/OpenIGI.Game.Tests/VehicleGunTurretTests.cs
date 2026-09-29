using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks the port of <c>AIStationaryGun</c>'s run handler (<c>0x46F390</c>).</summary>
public sealed class VehicleGunTurretTests
{
    // The T80 turret as magicobj.qvm authors it: 361-degree arc, +15/-4 pitch, 15 deg/s both ways, 6 m.
    private static readonly MagicGunMount T80 = new("614_03_1", 361f, 15f, -4f, 10000, 15f, 15f, "tank_turret", 0f, 6f);

    private const double Metre = WorldScene.UnitsPerMetre;

    [Fact]
    public void TheGunTraversesAtItsAuthoredRate()
    {
        VehicleGunTurret gun = new(T80);
        Vector3d abeam = new(-30 * Metre, 0, 0); // 90 degrees to the left: heading +pi/2

        for (int tick = 0; tick < 30; tick++)
        {
            gun.Tick(abeam);
        }

        // 15 degrees a second for one second.
        Assert.Equal(15f, gun.Yaw / 0.017453292f, 2);
    }

    [Fact]
    public void ItFiresOnlyOnceWithinFiveDegreesOfTheTarget()
    {
        VehicleGunTurret gun = new(T80);
        double heading = 46.25 * Math.PI / 180d; // left of the barrel, clear of the 5-degree boundary
        Vector3d target = new(-30 * Metre * Math.Sin(heading), 30 * Metre * Math.Cos(heading), 0);

        int firstShot = -1;
        for (int tick = 0; tick < 120 && firstShot < 0; tick++)
        {
            if (gun.Tick(target))
            {
                firstShot = tick;
            }
        }

        // 0.5 degrees a tick: 41.0 after 82 ticks is still outside the 5-degree window, 41.5 is inside.
        Assert.Equal(82, firstShot);
    }

    [Fact]
    public void ItHoldsFireInsideTheMinimumRange()
    {
        VehicleGunTurret gun = new(T80);
        Vector3d close = new(0, 5 * Metre, 0);
        Vector3d far = new(0, 7 * Metre, 0);

        Assert.False(gun.Tick(close));
        Assert.True(gun.Tick(far));
    }

    [Fact]
    public void ALostTargetIsAimedAtForNinetyTicksThenTheGunReturns()
    {
        VehicleGunTurret gun = new(T80);
        Vector3d abeam = new(-30 * Metre, 0, 0);
        for (int tick = 0; tick < 200; tick++)
        {
            gun.Tick(abeam);
        }

        float aimed = gun.Yaw;
        for (int tick = 0; tick < VehicleGunTurret.LostTargetHoldTicks; tick++)
        {
            Assert.False(gun.Tick(null));
            Assert.Equal(aimed, gun.Yaw);
        }

        gun.Tick(null);
        Assert.True(gun.Yaw < aimed);
    }

    [Fact]
    public void ElevationStopsAtTheMountsLimitAndThatAimNeverFires()
    {
        VehicleGunTurret gun = new(T80);
        Vector3d overhead = new(0, 10 * Metre, 20 * Metre); // about 63 degrees up

        bool fired = false;
        for (int tick = 0; tick < 300; tick++)
        {
            fired |= gun.Tick(overhead);
        }

        Assert.Equal(15f, gun.Pitch / 0.017453292f, 3);
        Assert.False(fired);
    }
}
