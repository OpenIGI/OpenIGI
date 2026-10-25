using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The medipack through the weapon machine: its entry and syringe-loop clips, the dose cadence, and the
/// charges it spends and gives back.
/// </summary>
public sealed class PlayerMedipackTests
{
    [Fact]
    public void MedipackConsumesOneReserveAndRestoresAuthoredHealthFraction()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 2);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        Equip(weapons);

        PlayerInputState input = new();
        PlayerInputFrame firePress = default;
        firePress[(int)PlayerInputSlot.Fire] = 1f;
        input.Accept(firePress);
        weapons.Tick(input, clipEnded: false, onGround: true);
        Assert.True(weapons.MedipackStartedThisTick);
        weapons.Tick(input, clipEnded: true, onGround: true);
        Assert.True(weapons.TryUseMedipack(health));

        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
        Assert.Equal(0.5f, health.Damage, 3);
        Assert.False(weapons.TryUseMedipack(health));
    }

    [Fact]
    public void MedipackIgnoresReloadInputBecauseItIsAnInjectionAction()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 2);
        Equip(weapons);

        PlayerInputState input = new();
        PlayerInputFrame reload = default;
        reload[(int)PlayerInputSlot.Reload] = 1f;
        input.Accept(reload);

        weapons.Tick(input, clipEnded: true, onGround: true);

        Assert.NotEqual(HumanWeaponState.Reloading, weapons.State);
        Assert.Null(weapons.RequestedAction);
        Assert.Equal(2, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
    }

    [Fact]
    public void MedipackUsesOneShotEntryThenALoopingSyringeAction()
    {
        WeaponAnimations entry = WeaponAnimationTable.Select(Medipack, WeaponAction.Fire);
        WeaponAnimations sustained = WeaponAnimationTable.Select(Medipack, WeaponAction.MedipackLoop);

        Assert.False(entry.LoopFullBody);
        Assert.True(sustained.LoopFullBody);
        Assert.Equal(Medipack.FireAnimation2, sustained.FullBody);
    }

    [Fact]
    public void MedipackDoesNotReplayTheEntryClipWhenRetailLoopSlotIsAbsent()
    {
        WeaponType retailWithoutLoop = new()
        {
            Category = WeaponCategory.Medipack,
            FireAnimation1 = Medipack.FireAnimation1,
            FireAnimation2 = -1,
            UpperBodyFireAnimation = Medipack.UpperBodyFireAnimation,
        };

        WeaponAnimations sustained = WeaponAnimationTable.Select(
            retailWithoutLoop,
            WeaponAction.MedipackLoop);

        // FUN_0040FB90 forwards the raw slot to FUN_004D61D0. A missing slot is not permission to
        // turn the entry injection into a repeating one-shot animation.
        Assert.Equal(-1, sustained.FullBody);
        Assert.NotEqual(retailWithoutLoop.FireAnimation1, sustained.FullBody);
    }

    [Fact]
    public void MedipackBeginsDosingOnlyAfterItsOneShotEntryClipEnds()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 1);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        Equip(weapons);

        PlayerInputState input = new();
        input.Accept(Frame(PlayerInputSlot.Fire));
        weapons.Tick(input, clipEnded: false, onGround: true);
        Assert.True(weapons.MedipackStartedThisTick);

        // 0x40FB90 waits for the entry clip, then starts the sustained syringe loop whose handler
        // is FUN_0040FBE0. A normal advancing entry clip is not yet a dosing tick.
        weapons.Tick(input, clipEnded: false, onGround: true);
        Assert.False(weapons.TryUseMedipack(health, animationProgress: 0.25f));
        weapons.Tick(input, clipEnded: true, onGround: true);
        Assert.True(weapons.TryUseMedipack(health, animationProgress: 0.5f));
        Assert.Equal(0.5f, health.Damage, 3);
        Assert.Equal(0, weapons.Ammunition.Count(MedipackAmmo.AmmoId));

        weapons.Tick(input, clipEnded: true, onGround: true);
        Assert.False(weapons.TryUseMedipack(health, animationProgress: 0.75f));
        Assert.Equal(0.5f, health.Damage, 3);

        weapons.Tick(input, clipEnded: true, onGround: true);
        Assert.False(weapons.TryUseMedipack(health, animationProgress: 1f));
        Assert.Equal(0.5f, health.Damage, 3);
        Assert.False(weapons.TryUseMedipack(health, animationProgress: 1f));
    }

    [Fact]
    public void MedipackAtFullHealthDoesNotConsumeACharge()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 1);
        HumanHealth health = new(HumanHitZoneTable.Player);
        Equip(weapons);

        PlayerInputState input = new();
        input.Accept(Frame(PlayerInputSlot.Fire));
        weapons.Tick(input, clipEnded: false, onGround: true);
        weapons.Tick(input, clipEnded: false, onGround: true);

        Assert.False(weapons.TryUseMedipack(health, animationProgress: 0.5f));
        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
    }

    [Fact]
    public void ANewMedipackPickupAddsItsAuthoredChargesToNativeReserve()
    {
        PlayerWeapons weapons = new([31]);
        WeaponPickupResult pickup = weapons.PickUp(Medipack, MedipackAmmo, rounds: 50);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        PlayerInputState input = new();

        Assert.True(pickup.AddedWeapon);
        Assert.Equal(50, pickup.AddedReserveRounds);
        Assert.Equal(50, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
        Assert.Equal(GunTiming.UnloadedClipSentinel, weapons.InHand!.Value.Gun.RoundsInClip);

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Assert.False(weapons.TryUseMedipack(health));
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);

        Assert.True(weapons.TryUseMedipack(health));
        Assert.Equal(GunTiming.UnloadedClipSentinel, weapons.InHand!.Value.Gun.RoundsInClip);
        Assert.Equal(49, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
        Assert.Equal(0.5f, health.Damage, 3);
    }

    [Fact]
    public void MedipackStillActivatesWhenOutgoingClipAlreadyEnded()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 1);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        Equip(weapons);

        PlayerInputState input = new();
        input.Accept(Frame(PlayerInputSlot.Fire));
        weapons.Tick(input, clipEnded: true, onGround: true);

        Assert.False(weapons.TryUseMedipack(health));
        weapons.Tick(input, clipEnded: true, onGround: true);
        Assert.True(weapons.TryUseMedipack(health));
        Assert.Equal(0, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
        Assert.Equal(0.5f, health.Damage, 3);
    }

    [Fact]
    public void FullHealthMedipackDoesNotConsumeOrRemainArmed()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 1);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        PlayerInputState input = new();

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));

        Assert.False(weapons.TryUseMedipack(health));
        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));

        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health));
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health));
        Tick(weapons, input, Frame(), clipEnded: false);
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
    }

    [Fact]
    public void HoldingFireKeepsOneSyringeLoopAndConsumesChargesAtTheRetailCooldown()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 2);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        PlayerInputState input = new();
        PlayerInputFrame fire = Frame(PlayerInputSlot.Fire);

        Tick(weapons, input, fire);
        Assert.False(weapons.TryUseMedipack(health));
        Tick(weapons, input, fire, clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health));

        // FUN_0040FBE0 sets Human+0xD6C from its 30 Hz (60 / RPM * 30) expression, which is 15 for the retail
        // medipack. The loop stays visible during those updates but neither charge nor health moves.
        Tick(weapons, input, fire, clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health));
        for (int i = 1; i < GunTiming.TicksPerRound(Medipack.RoundsPerMinute); i++)
        {
            Tick(weapons, input, fire, clipEnded: true);
            Assert.False(weapons.TryUseMedipack(health));
        }

        Tick(weapons, input, fire, clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health));
        Assert.Equal(WeaponAction.Fire, weapons.RequestedAction);
        Assert.Equal(2, weapons.RequestedFireVariant);

        // The ClipEnded input here is a loop wrap, not a request to leave/re-enter Medipack and replay
        // the one-shot entry.
        Assert.Equal(0.25f, health.Damage, 3);
        Assert.Equal(0, weapons.Ammunition.Count(MedipackAmmo.AmmoId));

        // An empty pack runs FireAnimation3 once, rather than looping a firearm reload.
        Tick(weapons, input, fire, clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health));
        Assert.Equal(0, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);

        Tick(weapons, input, Frame(), clipEnded: false);
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
    }

    [Fact]
    public void HeldMedipackDoesNotRestartItsLoopForEachCooldownDose()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 3);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        PlayerInputState input = new();
        PlayerInputFrame fire = Frame(PlayerInputSlot.Fire);

        Tick(weapons, input, fire);
        Assert.Equal(WeaponAction.Fire, weapons.RequestedAction);
        Tick(weapons, input, fire, clipEnded: true);
        Assert.Equal(WeaponAction.MedipackLoop, weapons.RequestedAction);
        Assert.True(weapons.TryUseMedipack(health));

        // The native handler keeps the same looping clip alive while Human+0xD6C counts down. A
        // cooldown tick must therefore raise no new animation action; replaying FireAnimation2 here
        // would reset the clip to its first frame and make the syringe look like one-shot doses.
        int cooldown = GunTiming.TicksPerRound(Medipack.RoundsPerMinute);
        for (int tick = 0; tick < cooldown; tick++)
        {
            Tick(weapons, input, fire, clipEnded: true);
            Assert.Null(weapons.RequestedAction);
            Assert.Equal(HumanWeaponState.Medipack, weapons.State);
            Assert.False(weapons.TryUseMedipack(health));
        }

        Tick(weapons, input, fire, clipEnded: true);
        Assert.Null(weapons.RequestedAction);
        Assert.True(weapons.TryUseMedipack(health));
        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
    }

    [Fact]
    public void EmptyMedipackReserveCannotBeConsumedAndActionReturnsReady()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        PlayerInputState input = new();

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));

        Assert.False(weapons.TryUseMedipack(health));
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health));
        Assert.Equal(0.75f, health.Damage, 3);
        Assert.Equal(WeaponAction.Fire, weapons.RequestedAction);
        Assert.Equal(2, weapons.RequestedFireVariant);
        Tick(weapons, input, Frame());
        Tick(weapons, input, Frame(), clipEnded: false);
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
    }

    [Fact]
    public void MedipackUsesNativeSpecialAmmoWhenItsCachedBindingIsMissing()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, ammo: null);
        weapons.Ammunition.Add(PlayerWeapons.MedipackAmmoId, 1);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        PlayerInputState input = new();

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));

        Assert.False(weapons.TryUseMedipack(health));
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health));
        Assert.True(weapons.MedipackUsedThisTick);
        Assert.Equal(0, weapons.Ammunition.Count(PlayerWeapons.MedipackAmmoId));
        Assert.Equal(0.5f, health.Damage, 3);
    }

    [Fact]
    public void MedipackContinuesDosingWhenItsLoopClipWraps()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 2);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        PlayerInputState input = new();

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Assert.False(weapons.TryUseMedipack(health, 0.25f));
        Assert.Equal(0.75f, health.Damage, 3);

        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health, 0.5f));
        Assert.Equal(0.5f, health.Damage, 3);
        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));

        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health, 1f));
        for (int i = 1; i < GunTiming.TicksPerRound(Medipack.RoundsPerMinute); i++)
        {
            Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
            Assert.False(weapons.TryUseMedipack(health, 1f));
        }

        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health, 1f));
        Assert.Equal(0.25f, health.Damage, 3);
        Assert.Equal(0, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
    }

    [Fact]
    public void MedipackStopsWhenAnInjectionReachesFullHealth()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 1);
        Equip(weapons);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.1f);
        PlayerInputState input = new();

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health, 0.5f));
        Assert.Equal(1f, health.RemainingFraction, 3);
        Assert.Equal(WeaponAction.Fire, weapons.RequestedAction);
        Assert.Equal(2, weapons.RequestedFireVariant);

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: false);
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.Equal(0, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
    }

    [Fact]
    public void ReleasingMedipackRunsRetailThirdFireClipBeforeReady()
    {
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 2);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        Equip(weapons);
        PlayerInputState input = new();

        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health));

        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Assert.Equal(WeaponAction.Fire, weapons.RequestedAction);
        Assert.Equal(2, weapons.RequestedFireVariant);
        Assert.Equal(Medipack.FireAnimation3, WeaponAnimationTable.Select(Medipack, weapons.RequestedAction!.Value, weapons.RequestedFireVariant).FullBody);

        Tick(weapons, input, Frame(), clipEnded: false);
        Assert.Equal(HumanWeaponState.Medipack, weapons.State);
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
    }

    [Fact]
    public void TappingMedipackStillLandsItsFirstDoseWhenFireIsReleasedAsTheLoopBegins()
    {
        // Regression: a quick tap lets go of Fire while the one-shot entry clip is still playing, so by
        // the tick the sustained syringe loop begins the Fire flag is already up. FUN_0040FBE0 applies
        // one dose on that tick regardless -- a released Fire only *schedules* the terminal clip, it does
        // not skip the fall-through heal. An earlier port gated the dose on Fire being held, so a tapped
        // medipack played its animation, put itself away, and never healed.
        PlayerWeapons weapons = new([31]);
        weapons.Carry(Medipack, MedipackAmmo);
        weapons.Ammunition.Add(MedipackAmmo.AmmoId, 2);
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.75f);
        Equip(weapons);

        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Assert.False(weapons.TryUseMedipack(health));

        // The loop begins on a tick where Fire has already been released.
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.True(weapons.TryUseMedipack(health));
        Assert.Equal(0.5f, health.Damage, 3);
        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));

        // One dose only: the release scheduled the put-away, so it does not keep dosing.
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.False(weapons.TryUseMedipack(health));
        Assert.Equal(0.5f, health.Damage, 3);
        Assert.Equal(1, weapons.Ammunition.Count(MedipackAmmo.AmmoId));
    }
}
