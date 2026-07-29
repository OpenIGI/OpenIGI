using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What each mission starts the player carrying — read from the level's own <c>objects.qvm</c>.
/// </summary>
public sealed class RetailPlayerLoadoutTests
{
    /// <summary>The campaign's mission count.</summary>
    public const int Missions = 14;

    [RequiresGameDataFact]
    public void MissionOneStartsWithTheKnifeGlockMp5AndBinoculars()
    {
        // The loadout the first mission is remembered for. It is declared, not computed: four weapon
        // tasks and one ammunition task, all children of the level's own HumanPlayer.
        Assert.True(PlayerLoadout.TryRead(Load(1), out PlayerLoadout? loadout));
        Assert.NotNull(loadout);

        Assert.Equal(
            ["WEAPON_ID_BINOCULARS", "WEAPON_ID_MP5SD", "WEAPON_ID_GLOCK", "WEAPON_ID_KNIFE"],
            loadout.Weapons.Select(w => w.WeaponId));

        // A subclass per weapon family, which is why keying on the type name alone would miss things.
        Assert.Equal(["Binocular", "GunMP5SD", "Gun", "Gun"], loadout.Weapons.Select(w => w.TaskType));

        LoadoutAmmo ammo = Assert.Single(loadout.Ammunition);
        Assert.Equal("AMMO_ID_919", ammo.AmmoId);
        Assert.Equal(49, ammo.Count);
    }

    [RequiresGameDataFact]
    public void EveryMissionDeclaresALoadoutAndAlmostEveryOneGivesAKnife()
    {
        // Reading all fourteen is what shows this is the mechanism rather than a level-1 accident.
        int withKnife = 0;

        for (int mission = 1; mission <= Missions; mission++)
        {
            Assert.True(PlayerLoadout.TryRead(Load(mission), out PlayerLoadout? loadout), $"mission {mission}");
            Assert.NotNull(loadout);
            Assert.NotEmpty(loadout.Weapons);

            if (loadout.Has("WEAPON_ID_KNIFE"))
            {
                withKnife++;
            }
        }

        Assert.Equal(Missions, withKnife);
    }

    [RequiresGameDataFact]
    public void TheStealthMissionCarriesNothingThatFires()
    {
        // Mission 7 is the one you go into with binoculars and a knife. If the reader were inventing a
        // default loadout rather than reading the level, this is where it would show.
        Assert.True(PlayerLoadout.TryRead(Load(7), out PlayerLoadout? loadout));
        Assert.NotNull(loadout);

        Assert.Equal(["WEAPON_ID_BINOCULARS", "WEAPON_ID_KNIFE"], loadout.Weapons.Select(w => w.WeaponId));
        Assert.Empty(loadout.Ammunition);
    }

    [RequiresGameDataFact]
    public void SlotIsZeroEverywhereExceptMissionOne()
    {
        // The finding that stops Slot being read as an inventory index. Thirteen missions leave it zero;
        // mission 1 alone marks the Glock and the knife with 1, and two weapons cannot share one slot.
        for (int mission = 2; mission <= Missions; mission++)
        {
            Assert.True(PlayerLoadout.TryRead(Load(mission), out PlayerLoadout? loadout));
            Assert.NotNull(loadout);
            Assert.All(loadout.Weapons, w => Assert.Equal(0, w.Slot));
        }

        Assert.True(PlayerLoadout.TryRead(Load(1), out PlayerLoadout? first));
        Assert.NotNull(first);
        Assert.Equal([0, 0, 1, 1], first.Weapons.Select(w => w.Slot));
    }

    [RequiresGameDataFact]
    public void EveryWeaponDeclaredResolvesAgainstTheRosterAndNamesAModel()
    {
        // The link that makes drawing one possible: a WEAPON_ID_* from the level resolves to a roster
        // entry, and that entry names the model file to put in the player's hands.
        WeaponTypeRegistry roster = RetailWeaponRoster.Value;

        for (int mission = 1; mission <= Missions; mission++)
        {
            Assert.True(PlayerLoadout.TryRead(Load(mission), out PlayerLoadout? loadout));
            Assert.NotNull(loadout);

            foreach (LoadoutWeapon weapon in loadout.Weapons)
            {
                Assert.True(
                    roster.TryGetByScriptId(weapon.WeaponId, out WeaponType type),
                    $"mission {mission} declares {weapon.WeaponId}, which the roster does not carry");
                Assert.False(
                    string.IsNullOrEmpty(type.GunModel),
                    $"{weapon.WeaponId} names no gun model");
            }
        }
    }

    private static LevelTask Load(int mission)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        PlayerTaskTypes.RegisterAll(loop.Types);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources);
        _ = new MissionLoader(registry, fileSystem, resources, loader, null, new TerrainLoader(resources, loop.Natives))
            .Load(mission);

        return root;
    }
}
