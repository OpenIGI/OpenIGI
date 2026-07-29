using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Reads the retail weapon roster and checks it against the rest of the game's data.
/// </summary>
public sealed class RetailWeaponConfigTests
{
    [RequiresGameDataFact]
    public void TheRosterIsTwentyFourWeapons()
    {
        WeaponRegistry weapons = Load(out _, out _);

        Assert.Equal(24, weapons.Weapons.Count);
        Assert.Equal(24, weapons.ById.Count);
        Assert.All(weapons.Weapons, w => Assert.NotEmpty(w.ScriptId));
        Assert.All(weapons.Weapons, w => Assert.StartsWith("WEAPON_ID_", w.ScriptId, StringComparison.Ordinal));
    }

    [RequiresGameDataFact]
    public void TheIdsAreSparseAndNotInScriptOrder()
    {
        // A reader that assumed "position in the file is the id" would be wrong: the Glock is first
        // and is id 1, but the Colt is second and is 21.
        WeaponRegistry weapons = Load(out _, out _);

        Assert.Equal(1, weapons.Weapons[0].WeaponId);
        Assert.Equal(21, weapons.Weapons[1].WeaponId);

        List<int> ids = [.. weapons.ById.Keys.Order()];
        Assert.Equal(1, ids[0]);
        Assert.Equal(44, ids[^1]);
        Assert.True(ids.Count < ids[^1], "the id space is sparse");
    }

    [RequiresGameDataFact]
    public void EveryWeaponsCalibreNamesAnAmmoDirectory()
    {
        // Real calibres name a numerically-titled directory under ammo/ - 919, 556, 762 and so on.
        // The special types are numbered from 10000 up and their directories are NAMED instead
        // (GRENADE, DRAGUNOV, MEDIPACK), so the id does not give the directory and the mapping lives
        // in the ammo scripts. The knife is -1 because it consumes nothing.
        WeaponRegistry weapons = Load(out _, out _);
        string ammoRoot = Path.Combine(GameData.RequireRoot(), "ammo");
        int numeric = 0;
        int special = 0;

        foreach (WeaponConfigTask weapon in weapons.Weapons)
        {
            if (weapon.CalibreId < 0)
            {
                Assert.Equal("WEAPON_ID_KNIFE", weapon.ScriptId);
                continue;
            }

            if (weapon.CalibreId >= 10000)
            {
                special++;
                continue;
            }

            Assert.True(
                Directory.Exists(Path.Combine(
                    ammoRoot,
                    weapon.CalibreId.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                $"{weapon.ScriptId} takes calibre {weapon.CalibreId}, which has no ammo directory");
            numeric++;
        }

        Assert.Equal(18, numeric);
        Assert.Equal(5, special);
    }

    [RequiresGameDataFact]
    public void ASemiAutomaticBurstIsOneAndAnAutomaticsIsItsWholeClip()
    {
        // The burst cap is either 1 or the magazine, never anything between - so it is a mode flag
        // expressed as a number rather than a tunable.
        WeaponRegistry weapons = Load(out _, out _);

        foreach (WeaponConfigTask weapon in weapons.Weapons)
        {
            Assert.True(
                weapon.MaximumRoundsPerBurst == 1 || weapon.MaximumRoundsPerBurst == weapon.RoundsPerClip,
                $"{weapon.ScriptId}: burst {weapon.MaximumRoundsPerBurst} against clip {weapon.RoundsPerClip}");
        }
    }

    [RequiresGameDataFact]
    public void EveryWeaponsAnimationsExistInThePlayersSet()
    {
        // Each weapon names two families of animation, and they live in different skeletons.
        //
        // The UpperBody* ones resolve in 000.iff, the set HumanPlayer asks for - because the player's
        // legs are already running a locomotion state, so only its torso plays the weapon. The
        // full-body ones (Stand/Move/Fire/Reload) are for AI soldiers and are NOT all in 000: the
        // Glock's FireAnim1 of 16 is in every shipped set except the player's.
        WeaponRegistry weapons = Load(out _, out _);
        Formats.Animation.BoneAnimationSet player = LoadSet("000");

        int upper = 0;
        foreach (WeaponConfigTask weapon in weapons.Weapons)
        {
            foreach (int id in new[]
            {
                weapon.UpperBodyStandAnimation, weapon.UpperBodyWalkAnimation,
                weapon.UpperBodyCrouchAnimation, weapon.UpperBodyCrouchRunAnimation,
                weapon.UpperBodyRunAnimation, weapon.UpperBodyFireAnimation,
                weapon.UpperBodyReloadAnimation,
            })
            {
                if (id < 0)
                {
                    continue;
                }

                Assert.True(
                    player.TryGet(id, out _),
                    $"{weapon.ScriptId} names upper-body animation {id}, absent from the player's set");
                upper++;
            }
        }

        // 114, not 24 x 7 = 168: plenty of weapons leave upper-body slots at -1, and the knife and
        // the emplacement guns leave most of them.
        Assert.Equal(114, upper);

        // And the split is real, not an artefact of which ids happen to exist: at least one full-body
        // id is missing from the player's set precisely because those clips belong to the soldiers.
        Assert.Contains(
            weapons.Weapons,
            w => w.FireAnimation1 >= 0 && !player.TryGet(w.FireAnimation1, out _));
    }

    [RequiresGameDataFact]
    public void PublishingTheScriptIdsRetiresThePlayerConfigsPlaceholders()
    {
        // This is the divergence the player config recorded: WEAPON_ID_* are pattern-registered, so
        // their values live in the weapon roster rather than in the executable. Load the roster first
        // and humanplayer.qvm resolves them for real - no placeholder is invented at all.
        WeaponRegistry weapons = Load(out ResourceRegistry resources, out NativeRegistry natives);

        int published = weapons.PublishScriptIds(natives);
        Assert.Equal(24, published);

        HumanPlayerConfig player = new();
        player.Register(natives);
        player.Load(resources, natives);

        Assert.All(
            player.PlaceholderIds.Keys,
            name => Assert.StartsWith("AMMO_ID_", name, StringComparison.Ordinal));
        Assert.DoesNotContain(
            player.PlaceholderIds.Keys,
            name => name.StartsWith("WEAPON_ID_", StringComparison.Ordinal));

        // And the cycle now carries the real ids: the Glock really is 1.
        Assert.Contains(1, player.WeaponCycle);
        Assert.Equal("WEAPON_ID_GLOCK", weapons.ById[1].ScriptId);
    }

    [RequiresGameDataFact]
    public void TheWeaponCycleAndTheRosterAgree()
    {
        WeaponRegistry weapons = Load(out ResourceRegistry resources, out NativeRegistry natives);
        weapons.PublishScriptIds(natives);

        HumanPlayerConfig player = new();
        player.Register(natives);
        player.Load(resources, natives);

        // Every weapon the player can cycle to is a real weapon in the roster.
        Assert.All(player.WeaponCycle, id => Assert.True(weapons.TryGet(id, out _), $"cycle names {id}"));

        // The roster is larger than the cycle: it also holds the vehicle and emplacement guns.
        Assert.True(weapons.Weapons.Count > player.WeaponCycle.Count);
    }

    private static Formats.Animation.BoneAnimationSet LoadSet(string name) =>
        Formats.Animation.BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", $"{name}.iff")));

    private static WeaponRegistry Load(out ResourceRegistry resources, out NativeRegistry natives)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        resources = new ResourceRegistry(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        natives = loop.Natives;
        WeaponRegistry.RegisterTypes(loop.Types);

        // The script names enum constants the engine owns. Numbering them by symbol order is the same
        // deliberate fallback the magic-object registry uses; nothing here depends on their values.
        QvmProgram program = QvmProgram.Read(
            File.ReadAllBytes(GameData.Resolve("weapons/weaponconfig.qvm")));
        int next = 1;
        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is not null && !natives.Contains(symbol))
            {
                natives.RegisterConstant(symbol, next++);
            }
        }

        WeaponRegistry weapons = new();
        weapons.Load(resources, natives, loop.Tasks);
        return weapons;
    }
}
