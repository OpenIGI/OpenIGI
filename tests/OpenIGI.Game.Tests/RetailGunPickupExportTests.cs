using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A gun pickup's <c>isPickedUp</c>, which retail exports on byte <c>+297</c> (<c>0x46D170</c>) and level scripts read.
/// </summary>
public sealed class RetailGunPickupExportTests
{
    [RequiresGameDataFact]
    public void MissionElevensCountedGunReportsItsPickup()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        WeaponTypeRegistry weapons = new();
        weapons.Register(loop.Natives);
        weapons.Load(fileSystem, resources, loop.Natives);
        AmmoTypeRegistry ammo = new();
        ammo.Register(loop.Natives);
        ammo.Load(fileSystem, resources, loop.Natives);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        _ = new MissionLoader(missions, fileSystem, resources, level).Load(11);

        GunPickupRegistry pickups = GunPickupRegistry.Build(root, weapons, ammo, loop.Variables);
        GunPickup counted = Assert.Single(pickups.Items, static p => p.Task?.Name == "GunPickup_202");

        Assert.True(loop.Variables.TryRead("GunPickup_202.isPickedUp", out QvmValue before));
        Assert.Equal(0, before.ToInt32());
        counted.IsCollected = true;
        Assert.True(loop.Variables.TryRead("GunPickup_202.isPickedUp", out QvmValue after));
        Assert.Equal(1, after.ToInt32());
    }
}
