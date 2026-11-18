using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The word under the crosshair as the weapon in hand runs low.</summary>
public sealed class HudAmmoCueTests
{
    [Fact]
    public void A_full_enough_gun_says_nothing_a_low_one_low_ammo_an_empty_one_reload_and_one_with_none_left_no_ammo()
    {
        Assert.Null(HudAmmoCue.Of(Gun(20, 30, 90)));
        Assert.Equal(("LOW AMMO", 0), HudAmmoCue.Of(Gun(4, 30, 90)));
        Assert.Equal(("RELOAD", 1), HudAmmoCue.Of(Gun(0, 30, 90)));
        Assert.Equal(("NO AMMO", 2), HudAmmoCue.Of(Gun(0, 30, 0)));
    }

    [Fact]
    public void Nothing_while_it_reloads_or_for_a_weapon_that_loads_no_rounds()
    {
        Assert.Null(HudAmmoCue.Of(Gun(0, 30, 90) with { State = HudWeaponState.Reloading }));
        Assert.Null(HudAmmoCue.Of(Gun(0, 30, 90) with { Magazine = null }));
        Assert.Null(HudAmmoCue.Of(null));
    }

    private static HudWeaponView Gun(int loaded, int capacity, int reserve) =>
        new("ak47", "AK-47", string.Empty, default, loaded, capacity, reserve, null, HudGauge.Clip, HudWeaponState.Ready);
}
