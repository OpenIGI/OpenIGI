using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.World;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The session's ammunition and weapons, read from <c>ammo.qvm</c> and <c>weapon.qvm</c> at start-up.</summary>
public sealed class RetailIgi2WeaponTablesTests
{
    private static readonly Lazy<(Igi2WeaponTables Tables, NativeRegistry Natives, TaskTypeRegistry Types)> Session = new(static () =>
    {
        Igi2Session session = Igi2Session.Start();
        return (session.Require<Igi2WeaponTables>(), session.Loop.Natives, session.Loop.Types);
    });

    /// <summary>Every kind of ammunition gets its number, in the order the script made them, as a constant.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Ammunition_ids_count_from_zero()
    {
        (Igi2WeaponTables tables, NativeRegistry natives, _) = Session.Value;

        Assert.Equal(35, tables.AmmoTypes.Count);
        Assert.Equal("AMMO_ID_MAKAROVCLIP", tables.Ammo(0)?.AmmoId);
        Assert.Equal(0, Constant(natives, "AMMO_ID_MAKAROVCLIP"));
        int ak74 = Constant(natives, "AMMO_ID_AK74CLIP");
        Assert.Equal("AMMO_ID_AK74CLIP", tables.Ammo(ak74)?.AmmoId);
        Assert.Equal(-1, Constant(natives, "AMMO_ID_UNDEFINED"));
    }

    /// <summary>The weapons count from the undefined one at zero, which registers nothing.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Weapon_ids_count_from_the_undefined_weapon()
    {
        (Igi2WeaponTables tables, NativeRegistry natives, _) = Session.Value;

        Assert.Equal(Igi2WeaponTables.UndefinedWeapon, tables.Weapon(0)?.WeaponId);
        Assert.Equal(-1, Constant(natives, "WEAPON_ID_UNDEFINED"));
        int ak47 = Constant(natives, "WEAPON_ID_AK47");
        Assert.Equal(1, ak47);
        Igi2WeaponTypeTask weapon = tables.Weapon(ak47)!;
        Assert.Equal("ak47", weapon.WeaponAnimationName);
        Assert.Equal((int)Igi2WeaponAnimFamily.Rifle, weapon.WeaponType);
    }

    /// <summary>
    /// A weapon's gun, inside its primary parameter container, names its ammunition by the ammunition's constant; the
    /// weapon names the gun's type by the type's id.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_ak47_fires_ak74_rounds_from_a_gun()
    {
        (Igi2WeaponTables tables, NativeRegistry natives, TaskTypeRegistry types) = Session.Value;
        Igi2WeaponTypeTask weapon = tables.Weapon(Constant(natives, "WEAPON_ID_AK47"))!;

        Igi2GunTask gun = Assert.Single(weapon.Children.OfType<Igi2WeaponTypeParameterContainerTask>().SelectMany(static c => c.Children).OfType<Igi2GunTask>());
        Assert.Equal(Constant(natives, "AMMO_ID_AK74CLIP"), gun.AmmoTypeId);
        Assert.True(gun.RoundsPerClip > 0);
        Assert.Equal(types.Get(Igi2GunTask.TypeName).Id, weapon.PrimaryFireTaskType);
        Assert.Equal(Igi2WeaponConstants.NoTaskType, Constant(natives, "TASKTYPE_NONE"));
    }

    /// <summary>A weapon's own animation name and family give the player's body its clips.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_ak47_gives_the_body_its_clips()
    {
        (Igi2WeaponTables tables, NativeRegistry natives, _) = Session.Value;
        Igi2WeaponTypeTask weapon = tables.Weapon(Constant(natives, "WEAPON_ID_AK47"))!;
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Igi2BoneHierarchy rig = level.ModelFor(spawn.Model)!.BoneHierarchy!;

        Igi2AnimationClip?[] table = Igi2WeaponTypeAnims.BuildTable(
            Igi2AnimationLibrary.Load(packs, resources),
            rig,
            weapon.WeaponAnimationName,
            (Igi2WeaponAnimFamily)weapon.WeaponType);

        Assert.Equal("stand_ak47", table[0]?.Name);
    }

    private static int Constant(NativeRegistry natives, string name)
    {
        Assert.True(natives.TryResolve(name, out QvmSymbol? symbol), $"{name} is not registered.");
        return symbol.Read().ToInt32();
    }
}
