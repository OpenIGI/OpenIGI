using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>What each of Mission 7's firing vehicles carries, read from its model as <c>0x4C4950</c> spawns it.</summary>
public sealed class RetailVehicleArmamentTests
{
    [RequiresGameDataTheory]
    [InlineData("614_01_1", new[] { "WEAPON_ID_T80", "WEAPON_ID_M2HB" })] // T80: cannon, and the coaxial gun on its turret
    [InlineData("601_01_1", new[] { "WEAPON_ID_APC" })]
    [InlineData("700_01_1", new[] { "WEAPON_ID_MIL" })]
    [InlineData("610_01_1", new string[0])]                                // the truck is unarmed
    public void AVehicleModelCarriesTheGunsItsAttachmentsName(string model, string[] expected)
    {
        (LoadedMission mission, MagicObjectRegistry magic, WeaponTypeRegistry roster) = Load(7);

        IReadOnlyList<VehicleGun> guns = VehicleGun.FromModel(
            model,
            name => mission.LevelPack.TryGetModel(name, out MefModel? mef) || mission.CommonPack.TryGetModel(name, out mef)
                ? mef
                : null,
            magic,
            roster);

        Assert.Equal(expected, guns.Select(static gun => gun.Weapon.ScriptId));
    }

    [RequiresGameDataFact]
    public void TheCannonsAreMissilesAndTheMachineGunsAreGunshots()
    {
        (LoadedMission mission, MagicObjectRegistry magic, WeaponTypeRegistry roster) = Load(7);

        IReadOnlyList<VehicleGun> tank = VehicleGun.FromModel(
            "614_01_1",
            name => mission.LevelPack.TryGetModel(name, out MefModel? mef) || mission.CommonPack.TryGetModel(name, out mef)
                ? mef
                : null,
            magic,
            roster);

        Assert.False(tank[0].IsHitscan);
        Assert.True(tank[1].IsHitscan);

        // The coaxial gun rides on the turret: its offset includes the turret's own.
        Assert.True(tank[1].Offset.Z > tank[0].Offset.Z);
    }

    private static (LoadedMission Mission, MagicObjectRegistry Magic, WeaponTypeRegistry Roster) Load(int id)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);

        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(id);
        return (mission, magic, roster);
    }
}
