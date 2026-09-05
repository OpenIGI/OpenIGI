using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Runs the retail <c>humanplayer/humanplayer.qvm</c> through the real script VM and checks every value
/// it publishes, in the units the natives convert them into.
/// </summary>
/// <remarks>
/// This is the whole of the player's tuning data and it exists only in that one 1457-byte file. The
/// conversions are the point of the test: the script authors speeds in km/h, distances in metres and
/// durations in seconds, and <c>0x410070</c> turns them into engine units per tick, engine units and
/// ticks.
/// </remarks>
public sealed class RetailHumanPlayerConfigTests
{
    [RequiresGameDataFact]
    public void TheGeneralParametersConvertIntoEngineUnits()
    {
        HumanPlayerParameters parameters = Load().Parameters!;

        Assert.NotNull(parameters);
        Assert.Equal(HumanPlayerParameters.ArgumentCount, parameters.RawArguments.Count);

        // Argument 0 is dimensionless and stored as authored.
        Assert.Equal(1.75f, parameters.DeltaTranslationScale);

        // 17.5 and 27 km/h. 27 landing on exactly 1024 units per tick is the check that the
        // 4096000 * (1/108000) factor is read correctly.
        Assert.Equal(17.5f * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour, parameters.JumpHorizontalSpeed);
        Assert.Equal(1024.0f, parameters.JumpVerticalSpeed, 3);

        // 0.85 m becomes 3481.6 units, twice.
        Assert.Equal(3481.6f, parameters.Distance4, 2);
        Assert.Equal(3481.6f, parameters.Distance5, 2);

        // 0.25 s * 30 = 7.5, stored with fistp — so 7 ticks, not 8. A rounding implementation fails here.
        Assert.Equal(7, parameters.Duration6Ticks);

        Assert.Equal(3f, parameters.MaximumHealth);
        Assert.Equal(0.5f, parameters.ElectricFenceDose);
    }

    [RequiresGameDataFact]
    public void TheTwoSeventeenRowTablesCarryTheirRetailValues()
    {
        HumanPlayerParameters parameters = Load().Parameters!;

        float[] expectedFirst =
        [
            0.5f, 0.75f, 1f,
            0.5f, 0.75f, 1f,
            0.5f, 0.75f, 1f,
            0.5f, 0.75f, 1f,
            1f, 1f, 1f, 1f, 1f,
        ];
        float[] expectedSecond =
        [
            0.5f, 0.75f, 1f,
            0.5f, 0.75f, 1f,
            0.5f, 0.75f, 1f,
            0.5f, 0.75f, 1f,
            1f, 1f, 1f, 100000f, 100000f,
        ];

        Assert.Equal(expectedFirst, parameters.FirstTable);
        Assert.Equal(expectedSecond, parameters.SecondTable);
    }

    [RequiresGameDataFact]
    public void TheWeaponCycleIsTheEighteenWeaponWheel()
    {
        HumanPlayerConfig config = Load();

        Assert.Equal(18, config.WeaponCycle.Count);

        // The ids themselves are pattern-registered by the weapon system, which does not exist yet, so
        // what can be asserted is that every entry resolved to a distinct symbol and that the order is
        // the script's. Both are checked through the placeholder table.
        Assert.Equal(18, config.WeaponCycle.Distinct().Count());
        Assert.Equal(config.PlaceholderIds["WEAPON_ID_KNIFE"], config.WeaponCycle[0]);
        Assert.Equal(config.PlaceholderIds["WEAPON_ID_MEDIPACK"], config.WeaponCycle[^1]);
    }

    [RequiresGameDataFact]
    public void TheNineWeaponCategoriesHoldTheRosterInScriptOrder()
    {
        HumanPlayerConfig config = Load();

        Assert.Equal(HumanPlayerWeaponCategory.CategoryCount, config.Categories.Count);
        Assert.Equal([1, 3, 3, 3, 2, 1, 3, 1, 1], config.Categories.Select(c => c.Count));

        // The categories cover the same 18 weapons as the cycle, and no others.
        Assert.Equal(18, config.Categories.Sum(c => c.Count));
        Assert.Equal(
            config.WeaponCycle.Order(),
            config.Categories.SelectMany(c => c.WeaponIds).Order());

        int knife = config.PlaceholderIds["WEAPON_ID_KNIFE"];
        int dragunov = config.PlaceholderIds["WEAPON_ID_DRAGUNOV"];
        Assert.Equal([knife], config.Categories[0].WeaponIds);
        Assert.Equal([dragunov], config.Categories[5].WeaponIds);

        // The two orders are not the same one twice: the categories file the MINIMI with the assault
        // rifles, while the cycle puts it after the DRAGUNOV.
        Assert.NotEqual(config.WeaponCycle, config.Categories.SelectMany(c => c.WeaponIds).ToList());
        Assert.Equal(
            config.PlaceholderIds["WEAPON_ID_MINIMI"],
            config.Categories[3].WeaponIds[2]);
    }

    [RequiresGameDataFact]
    public void TheFiveAmmoLimitsAreTheShippedOnes()
    {
        HumanPlayerConfig config = Load();

        Assert.Equal(5, config.AmmoLimits.Count);
        Assert.Equal(32, config.AmmoLimits[config.PlaceholderIds["AMMO_ID_12"]]);
        Assert.Equal(32, config.AmmoLimits[config.PlaceholderIds["AMMO_ID_357"]]);
        Assert.Equal(200, config.AmmoLimits[config.PlaceholderIds["AMMO_ID_556"]]);
        Assert.Equal(128, config.AmmoLimits[config.PlaceholderIds["AMMO_ID_762"]]);
        Assert.Equal(196, config.AmmoLimits[config.PlaceholderIds["AMMO_ID_919"]]);
    }

    [RequiresGameDataFact]
    public void EveryWeaponAndAmmoSymbolNeededAPlaceholder()
    {
        HumanPlayerConfig config = Load();

        // 18 weapons and 5 ammo types: until the weapon system registers the real ids, all of them come
        // from the fallback. If this count drops, something else has started registering them and the
        // divergence note needs revisiting.
        Assert.Equal(23, config.PlaceholderIds.Count);
        Assert.All(config.PlaceholderIds.Keys, name =>
            Assert.True(
                name.StartsWith("WEAPON_ID_", StringComparison.Ordinal)
                    || name.StartsWith("AMMO_ID_", StringComparison.Ordinal),
                name));
    }

    private static HumanPlayerConfig Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        HumanPlayerConfig config = new();
        config.Register(loop.Natives);
        config.Load(resources, loop.Natives);
        return config;
    }
}
