using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>When the pin comes out of the player's grenade, in both games (<see cref="GrenadeCookLatch"/>), and Project I.G.I.'s fuse in hand.</summary>
public sealed class GrenadeCookTests
{
    [Fact]
    public void TheOriginalsPinComesOutWhenTheGameReachesItPressedOrNot()
    {
        GrenadeCookLatch latch = default;
        Assert.False(latch.PinDue(GrenadeCook.Original, gamePinDue: false));
        Assert.True(latch.PinDue(GrenadeCook.Original, gamePinDue: true));
    }

    [Fact]
    public void AManualPinWaitsForThePressAndForTheGamesPinMoment()
    {
        GrenadeCookLatch latch = default;
        latch.Note(false);
        Assert.False(latch.PinDue(GrenadeCook.Manual, gamePinDue: true));

        // Pressed before the hand reaches the pin, it comes out at the pin, the press kept though the key is let go.
        latch.Note(true);
        Assert.False(latch.PinDue(GrenadeCook.Manual, gamePinDue: false));
        latch.Note(false);
        Assert.True(latch.PinDue(GrenadeCook.Manual, gamePinDue: true));

        latch.Reset();
        Assert.False(latch.PinDue(GrenadeCook.Manual, gamePinDue: true));
    }

    [Fact]
    public void TheOriginalsGrenadeLeavesWithItsWholeFuseHoweverLongItWasHeld()
    {
        (PlayerWeapons weapons, PlayerInputState input) = WindUp(GrenadeCook.Original);
        Hold(weapons, input, 400, PlayerInputSlot.Fire, PlayerInputSlot.Reload);

        Assert.False(weapons.ThrowExplodedInHand);
        Throw(weapons, input);
        Assert.Equal(GrenadePhysics.FuseTicks, weapons.ThrowFuseTicks);
    }

    [Fact]
    public void AManualGrenadeHeldWithoutTheCookKeyLeavesWithItsWholeFuse()
    {
        (PlayerWeapons weapons, PlayerInputState input) = WindUp(GrenadeCook.Manual);
        Hold(weapons, input, 400, PlayerInputSlot.Fire);

        Assert.False(weapons.IsGrenadeTicking);
        Throw(weapons, input);
        Assert.False(weapons.ThrowExplodedInHand);
        Assert.Equal(GrenadePhysics.FuseTicks, weapons.ThrowFuseTicks);
    }

    [Fact]
    public void AManualCookBurnsTheFuseInHandAndTheGrenadeLeavesWithWhatIsLeft()
    {
        (PlayerWeapons weapons, PlayerInputState input) = WindUp(GrenadeCook.Manual);
        Hold(weapons, input, 1, PlayerInputSlot.Fire, PlayerInputSlot.Reload);
        Assert.True(weapons.IsGrenadeTicking);
        Hold(weapons, input, 30, PlayerInputSlot.Fire);

        Throw(weapons, input);
        Assert.False(weapons.ThrowExplodedInHand);

        // Lit with the whole fuse and burnt a tick at a time from then: the press, thirty held, and the four of letting go.
        Assert.Equal(GrenadePhysics.FuseTicks - 35, weapons.ThrowFuseTicks);
        Assert.False(weapons.IsGrenadeTicking);
    }

    [Fact]
    public void AManualCookHeldTooLongGoesOffInHandAndTheHandRecovers()
    {
        (PlayerWeapons weapons, PlayerInputState input) = WindUp(GrenadeCook.Manual);
        Hold(weapons, input, 1, PlayerInputSlot.Fire, PlayerInputSlot.Reload);
        for (int tick = 1; tick < GrenadePhysics.FuseTicks - 1; tick++)
        {
            Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
            Assert.False(weapons.FiredThisTick);
        }

        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.True(weapons.FiredThisTick);
        Assert.True(weapons.ThrowExplodedInHand);
        Assert.Equal(0, weapons.ThrowFuseTicks);

        // No throw is left to play: the stand clip and the next grenade the tick after, and no second grenade on any event.
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEvent: PlayerWeaponStateMachine.ThrowReleaseEvent);
        Assert.False(weapons.FiredThisTick);
        Assert.Equal(WeaponAction.Stand, weapons.RequestedAction);

        // Fire still held once the hand is back up: the next grenade stays in hand until fire has been let go.
        for (int tick = 0; tick < 3 * HumanViewSway.TicksToTravel; tick++)
        {
            Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        }

        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Tick(weapons, input, Frame(), clipEnded: true);
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.Equal(HumanWeaponState.Throwing, weapons.State);
    }

    // A grenade in hand, wound up past its pin with fire held.
    private static (PlayerWeapons Weapons, PlayerInputState Input) WindUp(GrenadeCook cook)
    {
        PlayerWeapons weapons = new([3]) { GrenadeCook = cook };
        weapons.Carry(Grenade, GrenadeAmmo);
        weapons.Ammunition.Add(GrenadeAmmo.AmmoId, 5);
        Equip(weapons);
        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEvent: PlayerWeaponStateMachine.ThrowPinEvent);
        Assert.Equal(HumanWeaponState.Throwing, weapons.State);
        return (weapons, input);
    }

    private static void Hold(PlayerWeapons weapons, PlayerInputState input, int ticks, params PlayerInputSlot[] held)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            Tick(weapons, input, Frame(held), clipEnded: true);
            Assert.False(weapons.FiredThisTick);
        }
    }

    // Fire let go: the release clip, two ticks of it, and its event 3 lets the grenade go.
    private static void Throw(PlayerWeapons weapons, PlayerInputState input)
    {
        Tick(weapons, input, Frame(), clipEnded: true);
        Tick(weapons, input, Frame());
        Tick(weapons, input, Frame());
        Tick(weapons, input, Frame(), clipEvent: PlayerWeaponStateMachine.ThrowReleaseEvent);
        Assert.True(weapons.FiredThisTick);
    }
}
