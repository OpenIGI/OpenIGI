using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's weapons lying in the level, <c>GunPickup</c>, and the player taking them (<c>HumanPlayer_Pickup</c>, <c>igi2 0x49F8C0</c>).</summary>
public sealed class RetailIgi2GunPickupTests
{
    /// <summary>How far from a grenade the player is tried at.</summary>
    private static readonly double[] Rings = [1000.0, 2000.0, 3000.0];

    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2Session session = Igi2Session.Start();
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain");
        return new Mission1Setup(level, Igi2AnimationLibrary.Load(packs, resources), session, world);
    });

    /// <summary>
    /// The level's pickups given to the player reach its level surroundings too, which a weapon it drops or swaps away joins: without
    /// them the throw made nothing and the weapon was lost.
    /// </summary>
    [Fact]
    public void The_players_surroundings_take_the_levels_pickups()
    {
        Igi2CollisionWorld world = new([], null);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world,
            Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, default, 0f, Game.Ai.AiDifficulty.Normal);
        Igi2PickupList pickups = new([], world, new Igi2GameMaterials());

        player.Human.Pickups = pickups;

        Assert.Same(pickups, Assert.IsType<Igi2LevelSurroundings>(player.Human.Surroundings).Pickups);
    }

    /// <summary>
    /// Mission 1 lays four grenades, in the order it declares them. Each gives one round in its load and one for its first
    /// fire type, and a full load for its second; each reaches at least a metre.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1_lays_four_grenades()
    {
        Igi2PickupList pickups = Pickups();

        Assert.Equal(4, pickups.Items.Count);
        Igi2WeaponTables tables = Mission1.Value.Session.Require<Igi2HumanWeapons>().Tables;
        Assert.All(pickups.Items, pickup =>
        {
            Assert.Equal(4, pickup.WeaponType.WeaponCategory);
            Assert.Equal(1, pickup.AmmoInClip);
            Assert.Equal([1, pickup.WeaponType.AmmoInfo(1, tables).RoundsPerClip], pickup.AmmoForFireType);
            Assert.True(pickup.Reach >= Igi2GunPickup.MinimumReach);
            Assert.False(pickup.IsDelete);
        });
        Assert.Equal(
            pickups.Items.Select(static p => p.Task!.Id).Order(),
            pickups.Items.Select(static p => p.Task!.Id));
    }

    /// <summary>
    /// A grenade may be taken from just above it, and not from beyond its reach across, nor from beyond its reach and two
    /// metres above. Mission 1's lie on a floor of object 3546 under its upper deck, less than a metre over them, so a metre
    /// straight up is already inside it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_grenade_is_within_reach_only_close_by()
    {
        Igi2PickupList pickups = Pickups();
        foreach (Igi2GunPickup pickup in pickups.Items)
        {
            Vector3d p = pickup.Position;
            double reach = pickup.Reach;
            Assert.True(pickup.IsAbleToPickup(new Vector3d(p.X, p.Y, p.Z + 512.0), 0f, pickups.Sight, pickups.Materials));
            Assert.False(pickup.IsAbleToPickup(new Vector3d(p.X + reach + 64.0, p.Y, p.Z), 0f, pickups.Sight, pickups.Materials));
            Assert.False(pickup.IsAbleToPickup(
                new Vector3d(p.X, p.Y, p.Z + reach + Igi2GunPickup.HeightReach + 64.0), 0f, pickups.Sight, pickups.Materials));
        }
    }

    /// <summary>
    /// A wall between the eyes and all three of a grenade's points keeps it from being taken, even within reach: a solid
    /// placed across them blocks every sight (<c>GunPickup_IsAbleToPickup</c>, <c>igi2 0x46BF79</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_grenade_out_of_sight_cannot_be_taken()
    {
        Igi2GunPickup pickup = Pickups().Items[0];
        Vector3d eyes = new(pickup.Position.X, pickup.Position.Y, pickup.Position.Z + 512.0);

        Assert.False(pickup.IsAbleToPickup(eyes, 0f, new BlindSight(), Mission1.Value.Session.Require<Igi2GameMaterials>()));
    }

    /// <summary>
    /// Standing by a grenade where its eyes see it, the player takes it outright, and every other within reach and sight in
    /// the same pass: mission 1's four lie within a metre or so of each other. The first is carried with the message naming
    /// it, each gives the grenades carried its rounds, each sounds, and the next run pass deletes them all from the list.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_takes_a_grenade_it_stands_on()
    {
        _ = Mission1.Value;
        Igi2PickupList pickups = Pickups();
        Igi2GunPickup pickup = pickups.Items[0];
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, Igi2PlayerFakes.FakeSurroundings surroundings) = StandingBy(pickup, pickups);
        Igi2Inventory inventory = weapons.Inventory;
        Igi2AmmoTypeTask grenades = pickup.WeaponType.AmmoInfo(0, inventory.Tables).AmmoType!;
        bool carried = inventory.FindWeapon(pickup.WeaponType.WeaponId)!.IsActive;
        int before = inventory.FindAmmo(grenades.AmmoId)!.Count;
        List<Igi2GunPickup> deleted = [];
        pickups.Deleted += deleted.Add;

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
        player.Update();

        Assert.True(pickup.IsDelete);
        Igi2GunPickup[] taken = [.. pickups.Items.Where(static p => p.IsDelete)];
        Assert.True(inventory.FindWeapon(pickup.WeaponType.WeaponId)!.IsActive);
        int granted = Enumerable.Range(0, Igi2WeaponTypeTask.FireTypeSlots)
            .Where(slot => ReferenceEquals(pickup.WeaponType.AmmoInfo(slot, inventory.Tables).AmmoType, grenades))
            .Sum(slot => pickup.AmmoForFireType[slot]);
        Assert.Equal(before + (granted * taken.Length), inventory.FindAmmo(grenades.AmmoId)!.Count);
        Assert.Equal(taken.Length, surroundings.Sounds.Count(static n => n == Igi2HumanPlayer.PickupSound));
        Assert.Equal(carried ? Array.Empty<string>() : [Igi2HumanPlayer.PickedUpMessagePrefix + pickup.WeaponType.WeaponName], surroundings.Messages);

        pickups.Run();

        Assert.Equal(taken, deleted);
        Assert.DoesNotContain(pickup, pickups.Items);
    }

    /// <summary>On the tick the activate key goes down a grenade is only offered: taking one then is the action menu's.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_activate_key_only_offers_a_grenade()
    {
        _ = Mission1.Value;
        Igi2PickupList pickups = Pickups();
        Igi2GunPickup pickup = pickups.Items[0];
        (Igi2HumanPlayer player, _, _) = StandingBy(pickup, pickups);

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Activate);
        player.Update();

        Assert.False(pickup.IsDelete);
    }

    /// <summary>
    /// A weapon of a category the player carries another of is offered on the action menu as a swap for it; with the swap first,
    /// the activate key takes the weapon and throws the carried one down (<c>HumanPlayer_Pickup</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_action_menu_swaps_a_carried_weapon_for_one_lying()
    {
        _ = Mission1.Value;
        Igi2PickupList pickups = Pickups();
        Igi2GunPickup grenade = pickups.Items[0];
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, Igi2PlayerFakes.FakeSurroundings surroundings) = StandingBy(grenade, pickups);
        Igi2Inventory inventory = weapons.Inventory;
        (Igi2WeaponItem carried, Igi2WeaponTypeTask lying) = inventory.Weapons
            .Where(static w => w.IsActive)
            .SelectMany(w => inventory.Tables.WeaponTypes, (w, t) => (w, t))
            .First(p => p.t.WeaponCategory == p.w.WeaponType.WeaponCategory && p.t.WeaponId != p.w.WeaponType.WeaponId
                && p.t.FixedModel.Length > 0 && inventory.FindWeapon(p.t.WeaponId) is not { IsActive: true }
                && inventory.Weapons.Count(o => o.IsActive && o.WeaponType.WeaponCategory == p.t.WeaponCategory) == 1);
        Igi2GunPickup swap = Igi2GunPickup.Thrown(lying, inventory.Tables, grenade.Position, grenade.Orientation, 0f, isMoving: false, isThrowShort: false, 1, null, 100f);
        pickups.Add(swap);

        for (int tick = 0; tick < 3; tick++)
        {
            Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
            player.Update();
            player.RunDelayed();
        }

        Assert.True(player.ActionMenu.IsFirst(swap, carried.WeaponType));
        Assert.False(swap.IsDelete);

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Activate);
        player.Update();

        Assert.True(swap.IsDelete);
        Assert.True(inventory.FindWeapon(lying.WeaponId)!.IsActive);
        Assert.False(carried.IsActive);
        Assert.Contains(surroundings.Thrown, t => ReferenceEquals(t.WeaponType, carried.WeaponType));
    }

    /// <summary>
    /// The retail weapons' tactical slots come from their sights: the submachine guns and the launchers have their own, every other
    /// primary weapon is a long gun, and the original's loadout keeps the category alone.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_tactical_loadout_splits_the_primary_weapons_by_their_sights()
    {
        Igi2Inventory inventory = Igi2Gameplay.Arm(
            Mission1.Value.Session.Session.Services, Mission1.Value.Level.Root, Mission1.Value.Session.Require<Igi2PlayerTuning>(), Mission1.Value.Library)!.Inventory;
        Igi2PrimaryRole Role(string id) => Igi2CarrySlot.Of(inventory.FindWeapon(id)!.WeaponType, Igi2Loadout.Tactical).Role;

        Assert.Equal(Igi2PrimaryRole.LongGun, Role("WEAPON_ID_AK47"));
        Assert.Equal(Igi2PrimaryRole.LongGun, Role("WEAPON_ID_DRAGUNOV"));
        Assert.Equal(Igi2PrimaryRole.LongGun, Role("WEAPON_ID_SPAS12"));
        Assert.Equal(Igi2PrimaryRole.Smg, Role("WEAPON_ID_MP5SD"));
        Assert.Equal(Igi2PrimaryRole.Smg, Role("WEAPON_ID_UZI"));
        Assert.Equal(Igi2PrimaryRole.Launcher, Role("WEAPON_ID_RPG7"));
        Assert.Equal(Igi2PrimaryRole.None, Role("WEAPON_ID_GLOCK"));
        Assert.Equal(
            Igi2CarrySlot.Of(inventory.FindWeapon("WEAPON_ID_AK47")!.WeaponType, Igi2Loadout.Original),
            Igi2CarrySlot.Of(inventory.FindWeapon("WEAPON_ID_RPG7")!.WeaponType, Igi2Loadout.Original));
    }

    /// <summary>
    /// Under a tactical loadout a launcher and a submachine gun lying by the player are taken outright beside its rifle; under the
    /// original's, the launcher is only offered as a swap for the rifle.
    /// </summary>
    /// <param name="loadout">The loadout.</param>
    [RequiresTitleDataTheory("igi2")]
    [InlineData(Igi2Loadout.Tactical)]
    [InlineData(Igi2Loadout.Original)]
    public void A_tactical_loadout_takes_a_launcher_beside_the_rifle(Igi2Loadout loadout)
    {
        _ = Mission1.Value;
        Igi2PickupList pickups = Pickups();
        Igi2GunPickup grenade = pickups.Items[0];
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, Igi2PlayerFakes.FakeSurroundings surroundings) = StandingBy(grenade, pickups);
        player.Loadout = loadout;
        Igi2Inventory inventory = weapons.Inventory;
        Igi2WeaponItem rifle = CarryOnly(weapons, "WEAPON_ID_AK47")[0];
        Igi2GunPickup launcher = Lay(inventory, pickups, grenade, "WEAPON_ID_RPG7");
        Igi2GunPickup smg = Lay(inventory, pickups, grenade, "WEAPON_ID_MP5SD");

        for (int tick = 0; tick < 3; tick++)
        {
            Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
            player.Update();
            player.RunDelayed();
        }

        bool tactical = loadout == Igi2Loadout.Tactical;
        Assert.Equal(tactical, launcher.IsDelete);
        Assert.Equal(tactical, smg.IsDelete);
        Assert.Equal(tactical, inventory.FindWeapon("WEAPON_ID_RPG7")!.IsActive);
        Assert.Equal(tactical, inventory.FindWeapon("WEAPON_ID_MP5SD")!.IsActive);
        Assert.True(rifle.IsActive);
        Assert.Empty(surroundings.Thrown);
        Assert.Equal(!tactical, player.ActionMenu.Items.Contains(((object)launcher, (object?)rifle.WeaponType)));
    }

    /// <summary>
    /// Under a tactical loadout a rifle lying by the player is offered as a swap for its rifle alone, never its submachine gun, and
    /// the swap throws the rifle down and keeps the submachine gun.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_tactical_loadout_swaps_a_rifle_only_for_the_rifle()
    {
        _ = Mission1.Value;
        Igi2PickupList pickups = Pickups();
        Igi2GunPickup grenade = pickups.Items[0];
        (Igi2HumanPlayer player, Igi2PlayerWeapons weapons, Igi2PlayerFakes.FakeSurroundings surroundings) = StandingBy(grenade, pickups);
        player.Loadout = Igi2Loadout.Tactical;
        Igi2Inventory inventory = weapons.Inventory;
        Igi2WeaponItem[] carried = CarryOnly(weapons, "WEAPON_ID_AK47", "WEAPON_ID_MP5SD");
        Igi2GunPickup m16 = Lay(inventory, pickups, grenade, "WEAPON_ID_M16A2");

        for (int tick = 0; tick < 3; tick++)
        {
            Igi2PlayerFakes.Feed(player, Igi2PlayerAction.None);
            player.Update();
            player.RunDelayed();
        }

        Assert.False(m16.IsDelete);
        Assert.True(player.ActionMenu.IsFirst(m16, carried[0].WeaponType));
        Assert.Contains(((object)m16, (object?)carried[0].WeaponType), player.ActionMenu.Items);
        Assert.DoesNotContain(((object)m16, (object?)carried[1].WeaponType), player.ActionMenu.Items);

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Activate);
        player.Update();

        Assert.True(m16.IsDelete);
        Assert.True(inventory.FindWeapon("WEAPON_ID_M16A2")!.IsActive);
        Assert.False(carried[0].IsActive);
        Assert.True(carried[1].IsActive);
        Assert.Contains(surroundings.Thrown, t => ReferenceEquals(t.WeaponType, carried[0].WeaponType));
    }

    /// <summary>
    /// Putting a weapon away for good keeps the ammunition another weapon carried fires, when asked to: what a tactical loadout's
    /// drop does. Asked not to, it empties it as the original does.
    /// </summary>
    /// <param name="keep">Whether ammunition another weapon fires is kept.</param>
    [RequiresTitleDataTheory("igi2")]
    [InlineData(true)]
    [InlineData(false)]
    public void Ammunition_another_weapon_fires_stays_when_asked(bool keep)
    {
        Igi2Inventory inventory = Igi2Gameplay.Arm(
            Mission1.Value.Session.Session.Services, Mission1.Value.Level.Root, Mission1.Value.Session.Require<Igi2PlayerTuning>(), Mission1.Value.Library)!.Inventory;
        (Igi2WeaponItem dropped, Igi2WeaponItem other) = inventory.Weapons
            .Where(static w => w.WeaponType.WeaponCategory > 0)
            .SelectMany(w => inventory.Weapons, (w, o) => (w, o))
            .First(p => !ReferenceEquals(p.w, p.o) && p.o.WeaponType.WeaponCategory > 0
                && p.w.WeaponType.AmmoInfo(0, inventory.Tables).AmmoType is { } ammo
                && ReferenceEquals(ammo, p.o.WeaponType.AmmoInfo(0, inventory.Tables).AmmoType));
        inventory.EnableWeaponType(dropped.WeaponType.WeaponId, addAmmo: true);
        inventory.EnableWeaponType(other.WeaponType.WeaponId, addAmmo: true);
        Igi2AmmoItem pocket = inventory.FindAmmo(dropped.WeaponType.AmmoInfo(0, inventory.Tables).AmmoType!.AmmoId)!;
        inventory.AddAmmo(pocket.AmmoType.AmmoId, 10);
        int before = pocket.Count;

        Assert.True(inventory.IsAmmoFiredByAnother(dropped, 0));
        inventory.Disable(dropped, removeCarriedAmmo: true, keepAmmoFiredByAnother: keep);

        Assert.False(dropped.IsActive);
        Assert.Equal(keep ? before : 0, pocket.Count);
    }

    // Carries only the given primary weapons, each with a load, the first in hand.
    private static Igi2WeaponItem[] CarryOnly(Igi2PlayerWeapons weapons, params string[] weaponIds)
    {
        Igi2Inventory inventory = weapons.Inventory;
        foreach (Igi2WeaponItem primary in inventory.Weapons.Where(static w => w.IsActive && w.WeaponType.WeaponCategory == 3).ToArray())
        {
            inventory.Disable(primary, removeCarriedAmmo: false);
        }

        Igi2WeaponItem[] carried = [.. weaponIds.Select(id =>
        {
            Assert.True(inventory.EnableWeaponType(id, addAmmo: true));
            return inventory.FindWeapon(id)!;
        })];
        Assert.True(weapons.Activate(carried[0]));
        return carried;
    }

    // A weapon laid where a grenade lies, landed, with a load.
    private static Igi2GunPickup Lay(Igi2Inventory inventory, Igi2PickupList pickups, Igi2GunPickup at, string weaponId)
    {
        Igi2GunPickup pickup = Igi2GunPickup.Thrown(
            inventory.FindWeapon(weaponId)!.WeaponType, inventory.Tables, at.Position, at.Orientation, 0f, isMoving: false, isThrowShort: false, 1, null, 100f);
        pickups.Add(pickup);
        return pickup;
    }

    /// <summary>
    /// The player standing on the floor under a grenade, at the first spot within its reach where standing eyes see it, as
    /// the view sets them a tick after: the feet plus the standing eye height (<c>HumanView_GetOriginalViewPosition</c>).
    /// </summary>
    private static (Igi2HumanPlayer, Igi2PlayerWeapons, Igi2PlayerFakes.FakeSurroundings) StandingBy(Igi2GunPickup pickup, Igi2PickupList pickups)
    {
        Mission1Setup m = Mission1.Value;
        WorldLineTrace down = new(1);
        down.Starts[0] = new Vector3d(pickup.Position.X, pickup.Position.Y, pickup.Position.Z + 64.0);
        m.World.NextZ(down, [-40960.0], Igi2CollisionWorld.TerrainGroup | Igi2CollisionWorld.EveryTypeGroup);
        double floor = down.Intersections[0].Position.Z;
        Vector3d feet = Enumerable.Range(0, 16)
            .SelectMany(static _ => Rings, (a, r) => new Vector3d(
                pickup.Position.X + (r * Math.Cos(a * Math.PI / 8.0)), pickup.Position.Y + (r * Math.Sin(a * Math.PI / 8.0)), floor))
            .First(f => pickup.IsAbleToPickup(new Vector3d(f.X, f.Y, f.Z + 6922.24), 0f, pickups.Sight, pickups.Materials));

        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(m.Session.Session.Services, m.Level.Root, m.Session.Require<Igi2PlayerTuning>(), m.Library)!;
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = Igi2PlayerFakes.OnFloor(weapons);
        surroundings.World.Surfaces.Clear();
        surroundings.World.Surfaces.Add(new Igi2PlayerFakes.FakeCollider(floor));
        player.Position = feet;
        player.UpdateViewBody(updateAnimation: false);
        player.Pickups = pickups;
        return (player, weapons, surroundings);
    }

    private static Igi2PickupList Pickups()
    {
        Mission1Setup m = Mission1.Value;
        return Igi2PickupList.Build(
            m.Level.Root,
            m.Session.Require<Igi2HumanWeapons>().Tables,
            m.Level.ModelFor,
            m.World,
            m.Session.Require<Igi2GameMaterials>());
    }

    private sealed class BlindSight : IIgi2SightQuery
    {
        public bool LineOfSight(WorldLineOfSight sight, uint groups)
        {
            sight.IsHit = true;
            sight.Transparency = 0f;
            return false;
        }
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2AnimationLibrary Library, Igi2Session Session, Igi2CollisionWorld World);
}
