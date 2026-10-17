using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The per-class hit-zone tables, and what they make each weapon worth.
/// </summary>
public sealed class HumanHitZoneTableTests
{
    [Fact]
    public void BothTablesHaveOneRowPerZone()
    {
        // 0x489E30 copies exactly seventeen, which is the third independent confirmation of the count -
        // after the sub-object map and the array's own extent.
        Assert.Equal(HumanDamage.ZoneCount, HumanHitZoneTable.Player.Count);
        Assert.Equal(HumanDamage.ZoneCount, HumanHitZoneTable.Soldier.Count);
    }

    [Fact]
    public void MaximumHealthIsOneSoAMultiplierIsAFractionOfALife()
    {
        Assert.Equal(1f, HumanHitZoneTable.MaximumHealth);

        // Which makes the arithmetic legible: a torso is worth 1.0, so a weapon whose damage factor is
        // a quarter needs four hits.
        Assert.Equal(4, HumanHitZoneTable.HitsToKill(damageFactor: 0.25f, zoneMultiplier: 1f));
        Assert.Equal(2, HumanHitZoneTable.HitsToKill(0.5f, 1f));
        Assert.Equal(1, HumanHitZoneTable.HitsToKill(1f, 1f));
    }

    [Fact]
    public void ASoldierIsNotAsToughAsThePlayerInTheHead()
    {
        // Limbs and torso match exactly, so a body shot costs the same either way.
        for (int zone = 0; zone <= HumanDamage.RearZone; zone++)
        {
            Assert.Equal(HumanHitZoneTable.Player[zone].Multiplier, HumanHitZoneTable.Soldier[zone].Multiplier);
        }

        // The head and neck do not. A soldier's are lethal; the player's are worth a torso.
        foreach (int zone in new[] { HumanDamage.ProximityZone, 16 })
        {
            Assert.Equal(HumanHitZoneTable.LethalMultiplier, HumanHitZoneTable.Soldier[zone].Multiplier);
            Assert.Equal(1f, HumanHitZoneTable.Player[zone].Multiplier);
        }

        Assert.NotEqual(
            HumanHitZoneTable.Player.Select(z => z.LimbAnimation),
            HumanHitZoneTable.Soldier.Select(z => z.LimbAnimation));
    }

    [Fact]
    public void TheFourLimbsRunFromHalfToFull()
    {
        // The sub-object map's four chains of three land on multipliers of 0.5, 0.75 and 1.0 - an
        // extremity is worth half of what the top of the limb is.
        foreach (int chainStart in new[] { 0, 3, 6, 9 })
        {
            Assert.Equal(0.5f, HumanHitZoneTable.Player[chainStart].Multiplier);
            Assert.Equal(0.75f, HumanHitZoneTable.Player[chainStart + 1].Multiplier);
            Assert.Equal(1.0f, HumanHitZoneTable.Player[chainStart + 2].Multiplier);

            // And all three share an animation, which is what makes them one limb.
            Assert.Equal(
                HumanHitZoneTable.Player[chainStart].LimbAnimation,
                HumanHitZoneTable.Player[chainStart + 2].LimbAnimation);
        }
    }

    [Fact]
    public void TheTorsoAndTheTwoDirectionalZonesAreAllWorthOne()
    {
        foreach (int zone in new[] { HumanDamage.DefaultZone, HumanDamage.FrontZone, HumanDamage.RearZone })
        {
            Assert.Equal(1.0f, HumanHitZoneTable.Player[zone].Multiplier);
        }
    }

    [Fact]
    public void TheProximityZoneIsAOneHitKillOnASoldier()
    {
        // The evidence that zone 15 is the head: on a soldier its multiplier is 100 against a maximum
        // of 1.0, so anything with a damage factor above 0.01 kills instantly there.
        Assert.Equal(
            HumanHitZoneTable.LethalMultiplier,
            HumanHitZoneTable.Soldier[HumanDamage.ProximityZone].Multiplier);
        Assert.Equal(1, HumanHitZoneTable.HitsToKill(0.05f, HumanHitZoneTable.LethalMultiplier));

        // Zone 16 is lethal too, so head and neck are one region split in two - and they are the only
        // two on the soldier's table.
        Assert.Equal(
            [HumanDamage.ProximityZone, 16],
            HumanHitZoneTable.Soldier
                .Index()
                .Where(p => p.Item.Multiplier >= HumanHitZoneTable.LethalMultiplier)
                .Select(p => p.Index));

        // The player's table has none at all.
        Assert.DoesNotContain(
            HumanHitZoneTable.Player,
            z => z.Multiplier >= HumanHitZoneTable.LethalMultiplier);
    }

    [RequiresGameDataFact]
    public void EveryWeaponInTheRosterKillsInstantlyWithAHeadshot()
    {
        // The weakest weapon in the game has a damage factor of 0.05, which is still five times what a
        // lethal zone needs. So there is no weapon a headshot fails to kill a SOLDIER with - the player
        // has no lethal zone at all.
        WeaponTypeRegistry weapons = LoadWeapons();

        foreach (WeaponType weapon in weapons.Weapons.Where(w => w.DamageFactor > 0f))
        {
            Assert.Equal(1, HumanHitZoneTable.HitsToKill(weapon.DamageFactor, HumanHitZoneTable.LethalMultiplier));
        }
    }

    [RequiresGameDataFact]
    public void TheTorsoHitCountsAreTheOnesTheGamePlaysLike()
    {
        // A worked check against the real roster: these are the numbers a player experiences.
        WeaponTypeRegistry weapons = LoadWeapons();

        int TorsoHits(string scriptId)
        {
            WeaponType w = weapons.Weapons.First(x => x.ScriptId == scriptId);
            return HumanHitZoneTable.HitsToKill(w.DamageFactor, HumanHitZoneTable.Player[HumanDamage.DefaultZone].Multiplier);
        }

        Assert.Equal(4, TorsoHits("WEAPON_ID_GLOCK"));      // 0.25
        Assert.Equal(2, TorsoHits("WEAPON_ID_DRAGUNOV"));   // 0.5
        Assert.Equal(7, TorsoHits("WEAPON_ID_MP5SD"));      // 0.15

        // An extremity takes twice as many, because it is worth half.
        WeaponType glock = weapons.Weapons.First(x => x.ScriptId == "WEAPON_ID_GLOCK");
        Assert.Equal(8, HumanHitZoneTable.HitsToKill(glock.DamageFactor, HumanHitZoneTable.Player[0].Multiplier));
    }

    [RequiresGameDataFact]
    public void AllThreeTrailingFieldsAreAnimationIdsInTheOwningClassesOwnSet()
    {
        // They read like bone indices and they are not: the skeletons carry 33 bones and the player's
        // values run to 97. Every one of them resolves as an animation in the set its own class uses,
        // and the two tables' values do not resolve in each other's - which is what settles it.
        Formats.Animation.BoneAnimationSet player = LoadSet("000");
        Formats.Animation.BoneAnimationSet soldier = LoadSet("001");

        Assert.Equal(33, player.Skeleton.BoneCount);

        int[] playerIds = [.. Ids(HumanHitZoneTable.Player)];
        int[] soldierIds = [.. Ids(HumanHitZoneTable.Soldier)];

        // A bone index could not exceed the bone count; these do.
        Assert.Contains(playerIds, id => id > player.Skeleton.BoneCount);

        Assert.All(playerIds, id => Assert.True(player.TryGet(id, out _), $"player id {id}"));
        Assert.All(soldierIds, id => Assert.True(soldier.TryGet(id, out _), $"soldier id {id}"));

        // They are per-set. An id is only an index, so the ranges overlap and some of each table's
        // values happen to exist in the other set too - but never all of them, which is what says the
        // tables are addressing their own class's animations rather than a shared numbering.
        int[] playerReactions = [.. Reactions(HumanHitZoneTable.Player)];
        int[] soldierReactions = [.. Reactions(HumanHitZoneTable.Soldier)];

        Assert.Equal(playerReactions.Length, playerReactions.Count(id => player.TryGet(id, out _)));
        Assert.Equal(soldierReactions.Length, soldierReactions.Count(id => soldier.TryGet(id, out _)));

        Assert.True(
            playerReactions.Count(id => soldier.TryGet(id, out _)) < playerReactions.Length,
            "the player's reaction ids should not all exist in a soldier's set");
        Assert.True(
            soldierReactions.Count(id => player.TryGet(id, out _)) < soldierReactions.Length,
            "the soldiers' reaction ids should not all exist in the player's set");
    }

    private static IEnumerable<int> Reactions(IReadOnlyList<HumanHitZone> table) =>
        table.SelectMany(z => new[] { z.ReactionA, z.ReactionB }).Distinct().Order();

    [RequiresGameDataFact]
    public void TheHitZoneMultipliersComeFromHumanPlayerQvm()
    {
        // The two halves of this meet here. M8 read humanplayer.qvm's 43 arguments and found that 9
        // onwards were two 17-row tables it could not name. M9 found what the tables ARE, from the
        // binary. DefineHumanPlayer (0x410070) writes arguments 9..25 into 0x5389F8 and 26..42 into
        // 0x538B08 - which are the hit-zone tables - so the multipliers are script data, not compiled
        // constants, and the port should read them from the script.
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        HumanPlayerConfig config = new();
        config.Register(loop.Natives);
        config.Load(resources, loop.Natives);

        Assert.NotNull(config.Parameters);
        HumanPlayerParameters parameters = config.Parameters;

        Assert.Equal(HumanDamage.ZoneCount, HumanPlayerParameters.TableRowCount);
        Assert.Equal(9, HumanPlayerParameters.FirstTableArgument);

        // The script's own values are the multipliers this port hardcoded from the binary's static
        // initialiser. If those disagreed, the static data would be stale and the script would win.
        // The script's values are the player's real multipliers - head and neck worth a torso, not
        // lethal. The executable's compiled initialiser says 100 there and is stale.
        Assert.Equal(
            HumanHitZoneTable.Player.Select(z => z.Multiplier),
            parameters.FirstTable);

        // The second table the script fills is written and never read; its head and neck carry 100000.
        Assert.Equal(HumanHitZoneTable.UnusedSecondTable, parameters.SecondTable);
        Assert.NotEqual(parameters.FirstTable, parameters.SecondTable);
    }

    private static IEnumerable<int> Ids(IReadOnlyList<HumanHitZone> table) =>
        table.SelectMany(z => new[] { z.LimbAnimation, z.ReactionA, z.ReactionB }).Distinct().Order();

    private static Formats.Animation.BoneAnimationSet LoadSet(string name) =>
        Formats.Animation.BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff")));

    private static WeaponTypeRegistry LoadWeapons()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());

        WeaponTypeRegistry weapons = new();
        weapons.Register(loop.Natives);
        weapons.Load(fileSystem, resources, loop.Natives);
        return weapons;
    }
}
