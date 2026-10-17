using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

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

        Igi2Inventory inventory = Igi2Gameplay.Arm(Session.Value.Session.Services, level.Root, Tuning)!.Inventory;

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

    /// <summary>
    /// A carried gun with nothing loaded and nothing carried for it may only be selected when its type allows that; a round
    /// carried makes it selectable, and a weapon not carried never is.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Only_a_weapon_that_can_fire_is_selectable()
    {
        Igi2Inventory inventory = Weapons.CreateInventory(Tuning.AmmoLimits);
        Igi2WeaponItem glock = inventory.FindWeapon("WEAPON_ID_GLOCK")!;
        Assert.False(inventory.IsSelectable(glock));

        inventory.EnableWeaponType("WEAPON_ID_GLOCK", addAmmo: false);
        Assert.Equal(0, glock.Loaded(0));
        Assert.Equal(glock.WeaponType.SelectableWhenEmpty, inventory.IsSelectable(glock));

        inventory.AddAmmo("AMMO_ID_GLOCKCLIP", 1);
        Assert.True(inventory.IsSelectable(glock));
        Assert.Equal(new Igi2FireTypeRounds(Tables.Ammo(Constant("AMMO_ID_GLOCKCLIP")), 0, 1, glock.WeaponType.AmmoInfo(0, Tables).BulletsPerRound, glock.WeaponType.AmmoInfo(0, Tables).RoundsPerClip), inventory.FireTypeRounds(glock, 0));
    }

    /// <summary>The next weapon of a category is the first carried one after the given weapon, and none follows the last.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_next_weapon_of_a_category_does_not_wrap()
    {
        Igi2Inventory inventory = PlayerLoadout();
        Igi2WeaponItem glock = inventory.FindWeapon("WEAPON_ID_GLOCK")!;

        Assert.Same(glock, inventory.NextInCategory(Constant("WEAPONCATEGORY_SIDEARM"), null));
        Assert.Null(inventory.NextInCategory(Constant("WEAPONCATEGORY_SIDEARM"), glock.WeaponType));
        Assert.Same(inventory.FindWeapon("WEAPON_ID_MP5SD"), inventory.NextInCategory(Constant("WEAPONCATEGORY_PRIMARY"), null));
    }

    /// <summary>Disabling a weapon empties it, and its carried ammunition too when asked; clearing takes everything.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Disabling_and_clearing_take_weapons_and_rounds_away()
    {
        Igi2Inventory inventory = PlayerLoadout();
        Igi2WeaponItem glock = inventory.FindWeapon("WEAPON_ID_GLOCK")!;

        inventory.Disable(glock, removeCarriedAmmo: true);
        Assert.False(glock.IsActive);
        Assert.Equal(0, glock.Loaded(0));
        Assert.Equal(0, inventory.FindAmmo("AMMO_ID_GLOCKCLIP")!.Count);
        Assert.Equal(300, inventory.FindAmmo("AMMO_ID_MP5CLIP")!.Count);

        inventory.ClearAll();
        Assert.Null(inventory.Current);
        Assert.DoesNotContain(inventory.Weapons, static w => w.IsActive);
        Assert.All(inventory.Ammo, static a => Assert.Equal(0, a.Count));
    }

    /// <summary>The weapon in hand slows its carrier in full and every other carried weapon by half.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_weapon_in_hand_slows_in_full_and_the_rest_by_half()
    {
        Igi2Inventory inventory = PlayerLoadout();

        double expected = inventory.Weapons
            .Where(static w => w.IsActive)
            .Sum(w => ReferenceEquals(w, inventory.Current) ? w.WeaponType.MovementSpeedModifier : w.WeaponType.MovementSpeedModifier * 0.5);

        Assert.Equal(expected, inventory.SummedMovementSlowDown, 5);
    }

    /// <summary>The control answers from the weapon in hand: its category, precision, zoom, launcher and body clips.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_control_answers_from_the_weapon_in_hand()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(Session.Value.Session.Services, level.Root, Tuning, clips)!;
        Igi2WeaponTypeTask mp5 = weapons.Inventory.Current!.WeaponType;

        Assert.Equal(Constant("WEAPONCATEGORY_PRIMARY"), weapons.ActiveCategory);
        Assert.Equal(mp5.StandingPrecisionDegrees, weapons.ActivePrecision!.Value.Standing);
        Assert.Equal(mp5.TurningPrecisionModifier, weapons.ActivePrecision!.Value.TurningModifier);
        Assert.False(weapons.ActiveIsMissileLauncher);

        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Igi2BoneHierarchy rig = level.ModelFor(spawn.Model)!.BoneHierarchy!;
        IReadOnlyList<Igi2AnimationClip?> body = weapons.BodyClips(rig)!;
        Assert.Same(body, weapons.BodyClips(rig));
        Assert.Equal(
            Igi2WeaponTypeAnims.BuildTable(clips, rig, mp5.WeaponAnimationName, (Igi2WeaponAnimFamily)mp5.WeaponType)[0]?.Name,
            body[0]?.Name);

        Igi2PlayerWeapons launcher = new(Weapons.CreateInventory([]), Weapons.FireTaskTypes, clips);
        launcher.Inventory.EnableWeaponType("WEAPON_ID_RPG7", addAmmo: true);
        Assert.True(launcher.ActiveIsMissileLauncher);
        Igi2PlayerWeapons sniper = new(Weapons.CreateInventory([]), Weapons.FireTaskTypes, clips);
        sniper.Inventory.EnableWeaponType("WEAPON_ID_DRAGUNOV", addAmmo: true);
        Assert.True(sniper.ActiveIsZoom);
        Assert.False(weapons.ActiveIsZoom);
    }

    private static Igi2Inventory PlayerLoadout() =>
        Igi2Gameplay.Arm(Session.Value.Session.Services, Igi2Level.Find("location1", "level1").Root, Tuning)!.Inventory;

    private static int Constant(string name)
    {
        Assert.True(Session.Value.Loop.Natives.TryResolve(name, out QvmSymbol? symbol), $"{name} is not registered.");
        return symbol.Read().ToInt32();
    }
}
