using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
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
        Mission1Setup m = Mission1.Value;
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
        Mission1Setup m = Mission1.Value;
        Igi2PickupList pickups = Pickups();
        Igi2GunPickup pickup = pickups.Items[0];
        (Igi2HumanPlayer player, _, _) = StandingBy(pickup, pickups);

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Activate);
        player.Update();

        Assert.False(pickup.IsDelete);
    }

    /// <summary>
    /// The player standing on the floor under a grenade, at the first spot within its reach where standing eyes see it, as
    /// the view sets them a tick after: the feet plus the standing eye height (<c>HumanView_GetOriginalViewPosition</c>).
    /// </summary>
    private static (Igi2HumanPlayer, Igi2PlayerWeapons, Igi2PlayerFakes.FakeSurroundings) StandingBy(Igi2GunPickup pickup, Igi2PickupList pickups)
    {
        Mission1Setup m = Mission1.Value;
        Igi2LineTrace down = new(1);
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
        public bool LineOfSight(Igi2LineOfSight sight, uint groups)
        {
            sight.IsHit = true;
            sight.Transparency = 0f;
            return false;
        }
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2AnimationLibrary Library, Igi2Session Session, Igi2CollisionWorld World);
}
