using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Reads the ammunition table and joins it to the weapon roster.
/// </summary>
public sealed class RetailAmmoTypeTests
{
    [RequiresGameDataFact]
    public void TheTableIsFourteenTypesNumberedWithoutGaps()
    {
        AmmoTypeRegistry ammo = Load(out _, out _, out _);

        Assert.Equal(14, ammo.Types.Count);
        Assert.Equal(14, ammo.ById.Count);

        // The id is an inventory slot - 0x46DC40 indexes with it - so a gap would leave a hole.
        Assert.Equal(Enumerable.Range(0, 14), ammo.ById.Keys.Order());
        Assert.All(ammo.Types, t => Assert.StartsWith("AMMO_ID_", t.ScriptId, StringComparison.Ordinal));
    }

    [RequiresGameDataFact]
    public void TheSpecialCalibresAreNumberedFromTenThousandInAKnownOrder()
    {
        // This is the mapping the weapon roster cannot give: a weapon names calibre 10002 and there is
        // no ammo/10002 directory, because the special types live in NAMED directories. Only these
        // scripts say which is which.
        AmmoTypeRegistry ammo = Load(out _, out _, out _);

        Dictionary<int, string> byCalibre = ammo.Types.ToDictionary(t => t.CalibreId, t => t.ScriptId);

        Assert.Equal("AMMO_ID_GRENADE", byCalibre[10000]);
        Assert.Equal("AMMO_ID_PROXIMITYMINE", byCalibre[10001]);
        Assert.Equal("AMMO_ID_M203", byCalibre[10002]);
        Assert.Equal("AMMO_ID_FLASHBANG", byCalibre[10003]);
        Assert.Equal("AMMO_ID_DRAGUNOV", byCalibre[10004]);
        Assert.Equal("AMMO_ID_MEDIPACK", byCalibre[10005]);

        // Six special and eight real, and the real ones all name a numeric directory.
        Assert.Equal(6, ammo.Types.Count(t => t.CalibreId >= AmmoTypeRegistry.FirstSpecialCalibre));
        Assert.Equal<int[]>(
            [12, 44, 127, 357, 556, 762, 919, 1000],
            [.. ammo.Types.Where(t => t.CalibreId < AmmoTypeRegistry.FirstSpecialCalibre)
                          .Select(t => t.CalibreId).Order()]);
    }

    [RequiresGameDataFact]
    public void EveryWeaponsCalibreFindsItsAmmunition()
    {
        // 0x4789C0 - the gun's init - takes the weapon record's calibre and caches the ammo record it
        // finds. If a weapon named a calibre nothing supplies, its gun would have no ammunition at all.
        AmmoTypeRegistry ammo = Load(out _, out _, out WeaponTypeRegistry weapons);

        foreach (WeaponType weapon in weapons.Weapons)
        {
            if (weapon.CalibreId < 0)
            {
                // The knife consumes nothing, and -1 deliberately matches no ammunition.
                Assert.Equal("WEAPON_ID_KNIFE", weapon.ScriptId);
                Assert.False(ammo.TryGetByCalibre(weapon.CalibreId, out _));
                continue;
            }

            Assert.True(
                ammo.TryGetByCalibre(weapon.CalibreId, out AmmoType found),
                $"{weapon.ScriptId} takes calibre {weapon.CalibreId}, which nothing supplies");
            Assert.Equal(weapon.CalibreId, found.CalibreId);
        }
    }

    [RequiresGameDataFact]
    public void TwoAmmunitionTypesAreSuppliedButNamedByNoWeapon()
    {
        // Fourteen ammunition types against twelve calibres any weapon names.
        //
        // The M203's rounds are reached through the M16A2's launcher tail rather than through a
        // Calibre ID, so nothing in the roster points at them - that one is by design.
        //
        // The .357 is the interesting one, and it corroborates the two-revisions finding: the inert
        // weaponconfig.qsc gives BOTH revolvers .357, but the shipped weapon scripts moved the Colt
        // Anaconda to .44 and the Desert Eagle to 9x19. So the .357 ammunition still ships, still
        // loads, and is now used by nothing. Loading the declaration instead would make it live again.
        AmmoTypeRegistry ammo = Load(out _, out _, out WeaponTypeRegistry weapons);

        HashSet<int> named = [.. weapons.Weapons.Select(w => w.CalibreId)];
        List<string> unreferenced =
            [.. ammo.Types.Where(t => !named.Contains(t.CalibreId)).Select(t => t.ScriptId).Order()];

        Assert.Equal(["AMMO_ID_357", "AMMO_ID_M203"], unreferenced);

        // The .44 and the 9x19 that replaced it are both named, so this is a move rather than a loss.
        Assert.Contains(44, named);
        Assert.Contains(919, named);
    }

    [RequiresGameDataFact]
    public void OnlyTheBulletCalibresDrawATracer()
    {
        // 0x47ABD0 tests this before doing anything else. Shot, the 1000 calibre and every thrown or
        // special type draw none - none of them fires a visible round.
        AmmoTypeRegistry ammo = Load(out _, out _, out _);

        List<string> withTracer = [.. ammo.Types.Where(t => t.DrawsTracer).Select(t => t.ScriptId).Order()];

        Assert.Equal(
            ["AMMO_ID_127", "AMMO_ID_357", "AMMO_ID_44", "AMMO_ID_556", "AMMO_ID_762", "AMMO_ID_919"],
            withTracer);

        Assert.All(
            ammo.Types.Where(t => t.CalibreId >= AmmoTypeRegistry.FirstSpecialCalibre),
            t => Assert.False(t.DrawsTracer, t.ScriptId));
    }

    [RequiresGameDataFact]
    public void ATracerRunsFromItsNearColourToItsFarOne()
    {
        // The six colour components are a gradient along the shot: 9x19 goes amber at the muzzle to
        // yellow at the tip.
        AmmoTypeRegistry ammo = Load(out _, out _, out _);
        AmmoType nine = ammo.Types.First(t => t.ScriptId == "AMMO_ID_919");

        Assert.Equal(new TracerColour(250, 200, 0), nine.NearColour);
        Assert.Equal(new TracerColour(255, 255, 0), nine.FarColour);

        (float r, float g, float b) = AmmoTracer.ColourAt(nine, 0f);
        Assert.Equal(250 * TracerColour.ComponentScale, r);
        Assert.Equal(200 * TracerColour.ComponentScale, g);
        Assert.Equal(0f, b);

        (r, g, b) = AmmoTracer.ColourAt(nine, 1f);
        Assert.Equal(1f, r, 1e-6f);
        Assert.Equal(1f, g, 1e-6f);
        Assert.Equal(0f, b);

        // Clamping happens after interpolating, so overshooting saturates rather than extrapolating.
        (r, g, _) = AmmoTracer.ColourAt(nine, 4f);
        Assert.Equal(1f, r);
        Assert.Equal(1f, g);
    }

    [Fact]
    public void TheDistanceFadeIsGentleAndFloored()
    {
        // Only a tenth of the brightness is at stake across the full 500 metres, and the result never
        // falls below a fifth.
        Assert.Equal(1f, AmmoTracer.IntensityAt(1f, 0f));
        Assert.Equal(0.9f, AmmoTracer.IntensityAt(1f, AmmoTracer.MaximumDistance), 1e-6f);

        // Beyond 500 m the distance is clamped, so it stops fading rather than going dark.
        Assert.Equal(
            AmmoTracer.IntensityAt(1f, AmmoTracer.MaximumDistance),
            AmmoTracer.IntensityAt(1f, AmmoTracer.MaximumDistance * 10f));

        // And a dim round is floored.
        Assert.Equal(AmmoTracer.MinimumIntensity, AmmoTracer.IntensityAt(0.05f, 0f));
    }

    [RequiresGameDataFact]
    public void LoadingTheAmmunitionRetiresTheLastOfThePlayerConfigPlaceholders()
    {
        // With both tables loaded, humanplayer.qvm resolves every pattern-registered symbol for real
        // and HumanPlayerConfig invents nothing at all - which is the whole of the M8 divergence gone.
        AmmoTypeRegistry ammo = Load(out ResourceRegistry resources, out NativeRegistry natives, out _);

        Assert.All(ammo.Types, t => Assert.True(natives.Contains(t.ScriptId), t.ScriptId));

        HumanPlayerConfig player = new();
        player.Register(natives);
        player.Load(resources, natives);

        Assert.Empty(player.PlaceholderIds);
    }

    private static AmmoTypeRegistry Load(
        out ResourceRegistry resources,
        out NativeRegistry natives,
        out WeaponTypeRegistry weapons)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        resources = new ResourceRegistry(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        natives = loop.Natives;

        weapons = new WeaponTypeRegistry();
        weapons.Register(natives);
        weapons.Load(fileSystem, resources, natives);

        AmmoTypeRegistry ammo = new();
        ammo.Register(natives);
        ammo.Load(fileSystem, resources, natives);
        return ammo;
    }
}
