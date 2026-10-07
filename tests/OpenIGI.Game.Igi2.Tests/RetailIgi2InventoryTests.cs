using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>What a human carries: the inventory, the natives that fill it and the scripts that call them.</summary>
public sealed class RetailIgi2InventoryTests
{
    private static readonly Lazy<Igi2Session> Session = new(Igi2Session.Start);

    private static Igi2WeaponTables Tables => Session.Value.Require<Igi2WeaponTables>();

    private static Igi2HumanWeapons Weapons => Session.Value.Require<Igi2HumanWeapons>();

    private static Igi2PlayerTuning Tuning => Session.Value.Require<Igi2PlayerTuning>();

    /// <summary>The player tuning's two scripts define the weapon cycle and the ammunition limits, in their own order.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_tuning_defines_the_weapon_cycle_and_the_ammo_limits()
    {
        Assert.Equal(35, Tuning.WeaponCycle.Count);
        Assert.Equal(Constant("WEAPON_ID_AK47"), Tuning.WeaponCycle[0]);
        Assert.Equal(Constant("WEAPON_ID_MEDIPACK"), Tuning.WeaponCycle[^1]);

        Assert.Equal(33, Tuning.AmmoLimits.Count);
        Assert.Equal(new Igi2AmmoLimit(Constant("AMMO_ID_MAKAROVCLIP"), 64), Tuning.AmmoLimits[0]);
        Assert.Equal(new Igi2AmmoLimit(Constant("AMMO_ID_RPG18CLIP"), 0), Tuning.AmmoLimits[^1]);
    }

    /// <summary>
    /// Mission 1's player starts with the MP5SD in hand and a full clip in it, binoculars and thermal goggles that never go in
    /// hand, a knife and a Glock; the Glock's 170 spare rounds stop at its limit of 64.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1_arms_the_player_from_its_weapon_script()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");

        Igi2Inventory inventory = Igi2Gameplay.Arm(Session.Value.Session.Services, level.Root, Tuning)!;

        string[] carried = [.. inventory.Weapons.Where(static w => w.IsActive).Select(static w => w.WeaponType.WeaponId)];
        Assert.Equal(
            ["WEAPON_ID_BINOCULARS", "WEAPON_ID_GLOCK", "WEAPON_ID_KNIFE", "WEAPON_ID_MP5SD", "WEAPON_ID_THERMICAL"],
            carried.Order());
        Igi2WeaponItem mp5 = inventory.FindWeapon("WEAPON_ID_MP5SD")!;
        Assert.Same(mp5, inventory.Current);
        Assert.Equal(mp5.WeaponType.AmmoInfo(0, Tables).RoundsPerClip, mp5.Loaded(0));
        Assert.Equal(300, inventory.FindAmmo("AMMO_ID_MP5CLIP")!.Count);
        Assert.Equal(64, inventory.FindAmmo("AMMO_ID_GLOCKCLIP")!.Count);
    }

    /// <summary>A soldier's script that ends its first call with <c>;</c> gives that soldier only the first weapon.</summary>
    /// <remarks>The text is mission 1's <c>rsg2_1</c> guards' <c>Weapon</c> parameter.</remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_soldier_script_stops_at_its_first_semicolon()
    {
        Igi2Inventory inventory = Weapons.CreateInventory(Tuning.AmmoLimits);

        Weapons.RunWeaponScript(
            inventory,
            "//Normal Guard\n//Human_AddWeapon(\"WEAPON_ID_MAKAROV\"),\nHuman_AddWeapon(\"WEAPON_ID_MAC10\");\n\n"
            + "//Tough Guard\nHuman_AddWeapon(\"WEAPON_ID_AK47\");\n//Human_AddWeapon(\"WEAPON_ID_GRENADE\"),");

        Assert.True(inventory.FindWeapon("WEAPON_ID_MAC10")!.IsActive);
        Assert.False(inventory.FindWeapon("WEAPON_ID_AK47")!.IsActive);
        Assert.False(inventory.FindWeapon("WEAPON_ID_MAKAROV")!.IsActive);
    }

    /// <summary>Binoculars and the thermal goggles are carried but never put in hand; the next weapon is.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Binoculars_never_go_in_hand()
    {
        Igi2Inventory inventory = Weapons.CreateInventory([]);

        Assert.True(inventory.EnableWeaponType("WEAPON_ID_BINOCULARS", addAmmo: true));
        Assert.Null(inventory.Current);
        Assert.True(inventory.EnableWeaponType("WEAPON_ID_GLOCK", addAmmo: true));
        Assert.False(inventory.EnableWeaponType("WEAPON_ID_GLOCK", addAmmo: true));

        Assert.Equal("WEAPON_ID_GLOCK", inventory.Current!.WeaponType.WeaponId);
        Assert.Equal(inventory.Current.Number, Constant("WEAPON_ID_GLOCK"));
        Assert.Same(inventory.Weapons[inventory.CurrentIndex], inventory.Current);
    }

    /// <summary>The limits apply from the inventory's creation, and a count never leaves the range they allow.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Ammo_stays_within_its_limit()
    {
        Igi2Inventory inventory = Weapons.CreateInventory(Tuning.AmmoLimits);
        Igi2AmmoItem makarov = inventory.FindAmmo("AMMO_ID_MAKAROVCLIP")!;

        Assert.Equal(64, makarov.Limit);
        inventory.AddAmmo("AMMO_ID_MAKAROVCLIP", 100);
        Assert.Equal(64, makarov.Count);
        inventory.AddAmmo("AMMO_ID_MAKAROVCLIP", -200);
        Assert.Equal(0, makarov.Count);
        Assert.Equal(Igi2AmmoItem.NoLimit, Weapons.CreateInventory([]).FindAmmo("AMMO_ID_MAKAROVCLIP")!.Limit);
    }

    /// <summary><c>Human_AddAmmoForWeapon</c> adds rounds of whatever the weapon's primary fire type takes.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Ammo_for_a_weapon_goes_to_its_ammunition()
    {
        Igi2Inventory inventory = Weapons.CreateInventory(Tuning.AmmoLimits);

        Weapons.RunWeaponScript(inventory, "Human_AddAmmoForWeapon(\"WEAPON_ID_AK47\", 50)");

        Assert.Equal(50, inventory.FindAmmo("AMMO_ID_AK74CLIP")!.Count);
        Assert.False(inventory.FindWeapon("WEAPON_ID_AK47")!.IsActive);
    }

    private static int Constant(string name)
    {
        Assert.True(Session.Value.Loop.Natives.TryResolve(name, out QvmSymbol? symbol), $"{name} is not registered.");
        return symbol.Read().ToInt32();
    }
}
