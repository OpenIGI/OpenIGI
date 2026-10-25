using OpenIGI.Game.Player;
using Xunit;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>The remaster's weapon wheel: where the pointer points, what letting go takes, and the weapon coming to hand.</summary>
public sealed class WeaponWheelTests
{
    [Fact]
    public void The_first_sector_is_straight_up_and_the_rest_follow_clockwise()
    {
        Assert.Equal(0, WeaponWheel.SectorAt(0f, 4));
        Assert.Equal(1, WeaponWheel.SectorAt(MathF.PI * 0.5f, 4));
        Assert.Equal(2, WeaponWheel.SectorAt(MathF.PI, 4));
        Assert.Equal(3, WeaponWheel.SectorAt(-MathF.PI * 0.5f, 4));

        // Just short of a whole turn is back in the first, which straddles straight up.
        Assert.Equal(0, WeaponWheel.SectorAt(MathF.Tau - 0.1f, 4));
        Assert.Equal(-1, WeaponWheel.SectorAt(0f, 0));
    }

    [Fact]
    public void It_opens_on_the_weapon_in_hand_and_a_twitch_does_not_move_it()
    {
        WeaponWheel wheel = new();
        wheel.Open(4, current: 2);
        Assert.True(wheel.IsOpen);
        Assert.Equal(2, wheel.Hovered);

        Assert.False(wheel.Steer(10f, 5f));
        Assert.Equal(2, wheel.Hovered);
        Assert.Null(wheel.PointerAngle);

        // Letting go on the weapon in hand takes nothing.
        Assert.Equal(-1, wheel.Close());
        Assert.False(wheel.IsOpen);
    }

    [Fact]
    public void Pointing_takes_a_sector_and_letting_go_chooses_it()
    {
        WeaponWheel wheel = new();
        wheel.Open(4, current: 0);

        Assert.True(wheel.Steer(WeaponWheel.DeadZone * 2f, 0f));
        Assert.Equal(1, wheel.Hovered);

        // However far the mouse runs on, the pointer only turns: back left past the middle takes the sector on the left.
        Assert.True(wheel.Steer(-WeaponWheel.Reach * 10f, 0f));
        Assert.Equal(3, wheel.Hovered);
        Assert.Equal(3, wheel.Close());
    }

    [Fact]
    public void It_eases_open_and_shut_in_real_time_and_a_cancel_shuts_it_at_once()
    {
        WeaponWheel wheel = new();
        wheel.Open(3, current: -1);
        wheel.Animate(WeaponWheel.OpenSeconds * 0.5f);
        Assert.Equal(0.5f, wheel.Openness, 3);
        wheel.Animate(WeaponWheel.OpenSeconds);
        Assert.Equal(1f, wheel.Openness);

        wheel.Close();
        wheel.Animate(WeaponWheel.OpenSeconds * 0.25f);
        Assert.Equal(0.75f, wheel.Openness, 3);

        wheel.Open(3, current: -1);
        wheel.Cancel();
        Assert.False(wheel.IsOpen);
        Assert.Equal(0f, wheel.Openness);
    }

    [Fact]
    public void A_weapon_the_wheel_takes_comes_to_hand_on_the_next_ticks_as_a_number_keys_would()
    {
        PlayerWeapons weapons = new(Cycle);
        weapons.UseCategories(
        [
            new HumanPlayerWeaponCategory([20]),
            new HumanPlayerWeaponCategory([1]),
            new HumanPlayerWeaponCategory([7]),
        ]);
        weapons.Carry(Knife);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Carry(Mp5Sd, NineMillimetre);
        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        weapons.RequestWeapon(Mp5Sd.WeaponId);
        PlayerInputState input = new();
        Equip(weapons);
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel && weapons.InHand?.Weapon.WeaponId != Mp5Sd.WeaponId; i++)
        {
            Tick(weapons, input, Frame());
        }

        Assert.Equal("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);

        // The next-weapon key steps on from it, not from where the cycle last stood.
        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.NotEqual("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);

        // One not carried changes nothing.
        string held = weapons.InHand!.Value.Weapon.ScriptId;
        weapons.RequestWeapon(9999);
        Equip(weapons);
        Assert.Equal(held, weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void Binoculars_the_wheel_takes_are_raised_as_their_key_raises_them()
    {
        PlayerWeapons weapons = new([20, 30]);
        weapons.Carry(Knife);
        weapons.Carry(Binoculars);
        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        weapons.RequestWeapon(Binoculars.WeaponId);
        PlayerInputState input = new();
        Tick(weapons, input, Frame());
        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel && weapons.State != HumanWeaponState.Ready; i++)
        {
            Tick(weapons, input, Frame(), clipEnded: true);
        }

        // In hand, and raised to the eye: the toggle the view takes is the one the binoculars' key leaves.
        Assert.Equal("WEAPON_ID_BINOCULARS", weapons.InHand?.Weapon.ScriptId);
        Assert.True(weapons.ConsumeBinocularToggle());
    }
}
