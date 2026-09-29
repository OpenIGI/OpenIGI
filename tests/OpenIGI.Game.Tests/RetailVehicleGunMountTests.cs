using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The guns vehicle models carry, as <c>magicobj.qvm</c> defines them for <c>0x46FA50</c>.</summary>
public sealed class RetailVehicleGunMountTests
{
    [RequiresGameDataTheory]
    [InlineData("614_02_1", "614_03_1", 361f, 15f, -4f, 15f, 15f)]   // T80 turret: the T80 cannon
    [InlineData("614_10_1", "123_01_1", 360f, 45f, -20f, 30f, 30f)]  // T80 coaxial: the M2HB
    [InlineData("601_02_1", "601_05_1", 361f, 75f, -8f, 30f, 30f)]   // APC: the APC gun
    [InlineData("700_11_1", "700_12_1", 135f, 5f, -90f, 50f, 50f)]   // helicopter nose: the MIL gun
    public void EveryVehicleGunNamesItsWeaponByModel(
        string attachment,
        string weaponModel,
        float yawLimit,
        float maximumPitch,
        float minimumPitch,
        float pitchRate,
        float yawRate)
    {
        MagicObjectRegistry magic = Load();

        Assert.True(magic.TryGet(attachment, out MagicObjectDefinition definition));
        MagicGunMount gun = Assert.IsType<MagicGunMount>(definition.Gun);
        Assert.Equal(weaponModel, gun.WeaponModel);
        Assert.Equal(yawLimit, gun.YawLimitDegrees);
        Assert.Equal(maximumPitch, gun.MaximumPitchDegrees);
        Assert.Equal(minimumPitch, gun.MinimumPitchDegrees);
        Assert.Equal(10000, gun.Ammunition);
        Assert.Equal(pitchRate, gun.PitchRateDegreesPerSecond);
        Assert.Equal(yawRate, gun.YawRateDegreesPerSecond);
        Assert.Equal("tank_turret", gun.TurnSound);
        Assert.Equal(6f, gun.MinimumRangeMetres);
    }

    [RequiresGameDataFact]
    public void OnlyGunAttachmentsCarryAMount()
    {
        MagicObjectRegistry magic = Load();

        Assert.True(magic.TryGet("614_07_1", out MagicObjectDefinition deathZone));
        Assert.Null(deathZone.Gun);
    }

    private static MagicObjectRegistry Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        GameLoop loop = new();
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(new ResourceRegistry(fileSystem), loop.Natives);
        return magic;
    }
}
