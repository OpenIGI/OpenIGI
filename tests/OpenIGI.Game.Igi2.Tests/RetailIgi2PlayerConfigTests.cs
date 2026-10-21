using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The retail player tuning, read from <c>HUMANPLAYER/humanplayer.qvm</c> and converted as the original converts it.</summary>
public sealed class RetailIgi2PlayerConfigTests
{
    /// <summary>The script's one config item reads in declaration order, the throwing speed included.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_config_item_reads_the_retail_figures()
    {
        HumanPlayerConfigItemTask item = LoadItem();

        Assert.Equal(2.5f, item.MovementSpeedScale);
        Assert.Equal(7.5f, item.ForwardJumpSpeed);
        Assert.Equal(22f, item.UpwardJumpSpeed);
        Assert.Equal(0.2f, item.InAirControlSpeed);
        Assert.Equal(15f, item.ThrowingBaseVelocity);
        Assert.Equal(0.25f, item.PeekTime);
        Assert.Equal(0.5f, item.ElectricFenceDamageScale);
        Assert.Equal(0.175f, item.HeadDamageMultiplier);
        Assert.Equal(1.8f, item.HeadDamageMultiplierMultiplayer);
        Assert.StartsWith("DefineHumanPlayerWeaponCycle(", item.WeaponCycleScript, StringComparison.Ordinal);
        Assert.StartsWith("DefineHumanPlayerAmmoLimit(", item.AmmoLimitScript, StringComparison.Ordinal);
    }

    /// <summary>The speeds become world units per tick at the client's 24-bit truncating precision, the peek time whole ticks.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_tuning_converts_to_world_units_and_ticks()
    {
        Igi2HumanPlayerGeneral general = Igi2HumanPlayerGeneral.From(LoadItem());

        Assert.Equal(834.3703002929688f, general.UpwardJumpSpeed);
        Assert.Equal(284.4444274902344f, general.ForwardJumpSpeed);
        Assert.Equal(7.585184574127197f, general.InAirControlSpeed);
        Assert.Equal(4096f, general.PeekOffsetX);
        Assert.Equal(7, general.PeekTime);
        Assert.Equal(0.175f, general.DamageMultipliers[(int)Igi2DamagePart.Chest]);
        Assert.Equal(17, general.DamageMultipliersMultiplayer.Count);
    }

    private static HumanPlayerConfigItemTask LoadItem()
    {
        string script = Path.Combine(GameDataSource.Igi2.RequireRoot(), "HUMANPLAYER", "humanplayer.qvm");
        RetailIgi2LevelLoadTests.Harness harness = new(script);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

        List<HumanPlayerConfigItemTask> items = [];
        Collect(harness.LevelRoot, items);
        return Assert.Single(items);
    }

    private static void Collect(GameTask task, List<HumanPlayerConfigItemTask> into)
    {
        if (task is HumanPlayerConfigItemTask item)
        {
            into.Add(item);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, into);
        }
    }
}
