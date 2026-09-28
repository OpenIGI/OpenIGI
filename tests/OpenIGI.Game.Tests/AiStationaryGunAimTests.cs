using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The stand's own aim, as <c>0x474190</c>, <c>0x4748B0</c> and <c>0x474420</c> keep it: the occupant's
/// look turns the gun, the gun is posed from its own angles, and it is seen through from behind.
/// </summary>
public sealed class AiStationaryGunAimTests
{
    private const float Degrees = MathF.PI / 180f;

    private static AiStationaryGun Gun(float sideways = 90f) =>
        new(775, new Vector3d(1000.0, 2000.0, 3000.0), Matrix3f.Identity, 25f, -25f, sideways);

    /// <summary><c>0x4748B0</c> holds the elevation between the authored limits and the traverse within the sideways one.</summary>
    [Fact]
    public void SteeringIsHeldInsideTheAuthoredLimits()
    {
        AiStationaryGun gun = Gun();

        gun.Steer(1f, 3f);
        Assert.Equal(25f * Degrees, gun.AimPitch, 5);
        Assert.Equal(90f * Degrees, gun.AimYaw, 5);

        gun.Steer(-2f, -6f);
        Assert.Equal(-25f * Degrees, gun.AimPitch, 5);
        Assert.Equal(-90f * Degrees, gun.AimYaw, 5);
    }

    /// <summary>A sideways limit of a full turn is no limit at all.</summary>
    [Fact]
    public void AFullTurnSidewaysLimitDoesNotClampTheTraverse()
    {
        AiStationaryGun gun = Gun(sideways: 360f);

        gun.Steer(0f, 10f);

        Assert.Equal(10f, gun.AimYaw, 5);
    }

    /// <summary>
    /// With nobody on it the aim sinks five degrees a tick to the lowest elevation, while the shown angles
    /// stay where the last occupant left them.
    /// </summary>
    [Fact]
    public void AnEmptyGunsAimSinksWhileItsShownPoseStays()
    {
        AiStationaryGun gun = Gun();
        gun.Steer(10f * Degrees, 20f * Degrees);
        gun.TickOccupied();

        gun.TickEmpty();
        Assert.Equal(5f * Degrees, gun.AimPitch, 5);

        for (int tick = 0; tick < 20; tick++)
        {
            gun.TickEmpty();
        }

        Assert.Equal(-25f * Degrees, gun.AimPitch, 5);
        Assert.Equal(10f * Degrees, gun.DisplayPitch, 5);
        Assert.Equal(20f * Degrees, gun.DisplayYaw, 5);
    }

    /// <summary>
    /// <c>0x474360</c> writes the round's kick straight into the gun: it is shown on the next tick, and a
    /// tick without a round leaves none.
    /// </summary>
    [Fact]
    public void ARoundsKickIsShownOnTheNextTickOnly()
    {
        AiStationaryGun gun = Gun();
        gun.TickOccupied();

        gun.RecordShot(fired: true, kickPitch: 0.02f, kickYaw: -0.01f);
        Assert.True(gun.FiredThisTick);
        Assert.Equal(1, gun.ShotSequence);

        gun.TickOccupied();
        Assert.Equal(0.02f, gun.DisplayPitch, 5);
        Assert.Equal(-0.01f, gun.DisplayYaw, 5);
        Assert.False(gun.FiredThisTick);

        gun.RecordShot(fired: false, kickPitch: 0.5f, kickYaw: 0.5f);
        gun.TickOccupied();
        Assert.Equal(0f, gun.DisplayPitch, 5);
        Assert.Equal(0f, gun.DisplayYaw, 5);
    }

    /// <summary>
    /// <c>0x474420</c> turns the stand by the traverse and the gun by the elevation and a half turn, so a
    /// level gun fires down the stand's local -Y and a positive traverse swings it anticlockwise.
    /// </summary>
    [Fact]
    public void TheBarrelFollowsTheShownAngles()
    {
        AiStationaryGun gun = Gun();
        gun.TickOccupied();
        AssertClose(new Vector3f(0f, -1f, 0f), Barrel(gun));

        gun.Steer(0f, 90f * Degrees);
        AssertClose(new Vector3f(1f, 0f, 0f), Barrel(gun));

        gun.Steer(10f * Degrees, 0f);
        Vector3f raised = Barrel(gun);
        Assert.Equal(MathF.Sin(10f * Degrees), raised.Z, 4);
        Assert.True(raised.X > 0.9f);
    }

    /// <summary>
    /// The gun sits on the stand's mount point, fires from its own model's muzzle, and is seen from half
    /// a metre back along the barrel and a fifth of a metre up.
    /// </summary>
    [Fact]
    public void TheGunIsMountedFiredAndSightedFromItsOwnPoints()
    {
        AiStationaryGun gun = Gun();
        gun.MountVertex = new Vector3f(0f, 0f, 4096f);
        gun.MuzzleVertex = new Vector3f(0f, 6000f, 0f);
        gun.TickOccupied();

        Assert.Equal(new Vector3d(1000.0, 2000.0, 3000.0 + 4096.0), gun.GunPosition);

        // The gun model's +Y is the barrel, turned onto the stand's -Y by the half turn.
        AssertClose(new Vector3d(1000.0, 2000.0 - 6000.0, 7096.0), gun.MuzzlePosition);
        AssertClose(
            new Vector3d(1000.0, 2000.0 + AiStationaryGun.SightSetBackUnits, 7096.0 + AiStationaryGun.SightRiseUnits),
            gun.SightPosition);
    }

    private static Vector3f Barrel(AiStationaryGun gun)
    {
        Matrix3f m = gun.GunOrientation;
        return new Vector3f(m.M12, m.M22, m.M32);
    }

    private static void AssertClose(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    private static void AssertClose(Vector3d expected, Vector3d actual)
    {
        Assert.Equal(expected.X, actual.X, 2);
        Assert.Equal(expected.Y, actual.Y, 2);
        Assert.Equal(expected.Z, actual.Z, 2);
    }
}
