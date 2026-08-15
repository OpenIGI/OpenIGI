using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Drives the weapon machine and the gun together, and measures what comes out.
/// </summary>
/// <remarks>
/// <para>
/// The point of these is the <b>cadence</b>. Nothing in the human counts rounds: the firing state asks
/// for one on every tick the trigger is down and the gun answers when its own cooldown allows, so the
/// spacing between rounds has to fall out of <c>Rounds/Minute</c> alone. A test that stubbed the gun
/// would prove nothing.
/// </para>
/// <para>
/// The weapon records here are the retail ones, transcribed as literals so the test runs without game
/// data; <see cref="RetailWeaponFiringTests"/> checks the same numbers against the real scripts.
/// </para>
/// </remarks>
public sealed class PlayerWeaponsTests
{
    private static readonly int[] Cycle = [20, 1, 7];

    /// <summary>The knife, as <c>weapons/KNIFE/weapon.qvm</c> declares it.</summary>
    private static WeaponType Knife => new()
    {
        WeaponId = 20,
        ScriptId = "WEAPON_ID_KNIFE",
        Category = WeaponCategory.Knife,
        CalibreId = -1,
        RoundsPerMinute = 140,
        RoundsPerClip = -1,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 1,
        EffectiveRange = 1f,
        StandAnimation = 26,
        FireAnimation1 = 28,
        FireAnimation2 = 29,
        FireAnimation3 = 30,
    };

    /// <summary>The Glock 17 — semi-automatic, because its burst cap is one.</summary>
    private static WeaponType Glock => new()
    {
        WeaponId = 1,
        ScriptId = "WEAPON_ID_GLOCK",
        Category = WeaponCategory.Pistol,
        CalibreId = 919,
        RoundsPerMinute = 400,
        RoundsPerClip = 17,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 1,
        EffectiveRange = 80f,
        MinimumSpread = -1.5f,
        MaximumSpread = 1.5f,
        FixedRecoilX = 1.5f,
        StandAnimation = 14,
        FireAnimation1 = 16,
        FireAnimation2 = -1,
        FireAnimation3 = -1,
    };

    /// <summary>The MP5 SD3 — fully automatic, because its burst cap is the whole magazine.</summary>
    private static WeaponType Mp5Sd => new()
    {
        WeaponId = 7,
        ScriptId = "WEAPON_ID_MP5SD",
        Category = WeaponCategory.Gun,
        CalibreId = 919,
        RoundsPerMinute = 700,
        RoundsPerClip = 32,
        MaximumRoundsPerBurst = 32,
        BulletsPerRound = 1,
        EffectiveRange = 80f,
        MinimumSpread = -2f,
        MaximumSpread = 2f,
        FixedRecoilX = 0.8f,
        RandomRecoilX = 0.8f,
        StandAnimation = 18,
        FireAnimation1 = 20,
    };

    private static AmmoType NineMillimetre => new() { AmmoId = 0, ScriptId = "AMMO_ID_919", CalibreId = 919 };

    private static WeaponType Binoculars => new()
    {
        WeaponId = 30,
        ScriptId = "WEAPON_ID_BINOCULARS",
        Category = WeaponCategory.Binocular,
        Weapon = new WeaponTaskBinding("TASKTYPE_BINOCULAR", 0, [0.5f, 0.1f, 1.0f], string.Empty, 0),
    };

    private static WeaponType Medipack => new()
    {
        WeaponId = 31,
        ScriptId = "WEAPON_ID_MEDIPACK",
        Category = WeaponCategory.Medipack,
        CalibreId = 10005,
        // The shipped medipack fires its healing handler at 120 RPM: one dose per 15 ticks.
        RoundsPerMinute = 120,
        RoundsPerClip = 1,
        FireAnimation1 = 77,
        FireAnimation2 = 78,
        FireAnimation3 = 79,
        Weapon = new WeaponTaskBinding("TASKTYPE_MEDIPACK", 0, [0.25f], string.Empty, 0),
    };

    private static AmmoType MedipackAmmo => new() { AmmoId = 12, ScriptId = "AMMO_ID_MEDIPACK", CalibreId = 10005 };

    [Fact]
    public void BinocularBindingSelectsTheCarriedBinoculars()
    {
        PlayerWeapons weapons = new([30, 20]);
        weapons.Carry(Knife);
        weapons.Carry(Binoculars);
        Equip(weapons);

        PlayerInputState input = new();
        PlayerInputFrame binocularPress = default;
        binocularPress[(int)PlayerInputSlot.Binoculars] = 1f;
        input.Accept(binocularPress);
        weapons.Tick(input, clipEnded: true, onGround: true);

        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);
        for (int i = 0; i < 10 && weapons.State != HumanWeaponState.Ready; i++)
        {
            input.Accept(default);
            weapons.Tick(input, clipEnded: true, onGround: true);
        }

        Assert.Equal("WEAPON_ID_BINOCULARS", weapons.InHand?.Weapon.ScriptId);
        Assert.True(weapons.ConsumeBinocularToggle());
    }

    [Fact]
    public void BinocularsIgnoreFireReloadAndPeekActions()
    {
        PlayerWeapons weapons = new([30]);
        weapons.Carry(Binoculars);
        Equip(weapons);

        PlayerInputState input = new();
        PlayerInputFrame actions = default;
        actions[(int)PlayerInputSlot.Fire] = 1f;
        actions[(int)PlayerInputSlot.Reload] = 1f;
        actions[(int)PlayerInputSlot.Peek] = 1f;
        input.Accept(actions);

        weapons.Tick(input, clipEnded: true, onGround: true);

        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.False(weapons.FiredThisTick);
        Assert.Null(weapons.RequestedAction);
    }

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
    public void BinocularViewUsesAuthoredDefaultAndClampsZoomToAuthoredBounds()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;
        SightZoomFrame opened = view.Tick(
            binoculars,
            togglePressed: true,
            zoomInHeld: false,
            zoomOutHeld: false,
            tanX: HumanView.DefaultTanX,
            viewModelTanX: HumanView.DefaultViewModelTanX);

        Assert.True(view.IsOpen);
        Assert.True(view.OpenedThisTick);
        Assert.False(view.ClosedThisTick);
        Assert.True(view.ZoomTargetChangedThisTick);
        Assert.Equal(HumanView.DefaultTanX * BinocularsView.ZoomInFactor, opened.TanX, 5);
        Assert.Equal(HumanView.DefaultViewModelTanX, opened.ViewModelTanX);

        // Retail is level-triggered: the handler is invoked every simulation tick while the
        // binoculars are open, multiplying by 0.95999998 on each ZoomIn tick and by 1.04 on each
        // ZoomOut tick. It does NOT use an edge or a cooldown — holding the key steps every tick
        // until the respective authored minimum/maximum tangent is reached, where it stays.
        SightZoomFrame narrowed = view.Tick(binoculars, false, true, false, opened.TanX, opened.ViewModelTanX);
        Assert.Equal(opened.TanX * BinocularsView.ZoomInFactor, narrowed.TanX, 5);
        Assert.True(view.ZoomChangedThisTick);
        Assert.True(view.ZoomTargetChangedThisTick);
        narrowed = view.Tick(binoculars, false, false, false, narrowed.TanX, narrowed.ViewModelTanX);
        // Releasing the key holds the current magnification instead of continuing toward the last
        // retail target. Only holding a zoom input multiplies on the next tick.
        Assert.Equal(opened.TanX * BinocularsView.ZoomInFactor, narrowed.TanX, 5);
        Assert.Equal(HumanView.DefaultViewModelTanX, narrowed.ViewModelTanX);
        Assert.False(view.ZoomChangedThisTick);
        Assert.False(view.ZoomTargetChangedThisTick);
        for (int i = 0; i < 100; i++)
        {
            narrowed = view.Tick(binoculars, false, true, false, narrowed.TanX, narrowed.ViewModelTanX);
            Assert.False(float.IsNaN(narrowed.TanX));
        }

        Assert.Equal(0.1f, narrowed.TanX, 3);

        SightZoomFrame closing = view.Tick(
            binoculars,
            togglePressed: true,
            zoomInHeld: false,
            zoomOutHeld: false,
            tanX: narrowed.TanX,
            viewModelTanX: narrowed.ViewModelTanX);
        Assert.False(view.IsOpen);
        Assert.False(view.OpenedThisTick);
        Assert.True(view.ClosedThisTick);
        Assert.True(view.ZoomTargetChangedThisTick);

        // Closing starts the authored return transition; it does not snap the world camera back in one
        // tick. The loop cue must also stop on close — see bin_zoom_1 one-shot at mission teardown
        // or death (Ghidra FUN_004B5860 strip + FUN_00470570 0x3c23d70a syringe vs firearm report).
        Assert.Equal(0.1f * BinocularsView.ZoomOutFactor, closing.TanX, 5);
        Assert.Equal(HumanView.DefaultViewModelTanX, closing.ViewModelTanX);
    }

    [Fact]
    public void BinocularViewUsesTheRetailMultiplicativeZoomFactors()
    {
        BinocularsView view = new();
        WeaponType binoculars = Binoculars;

        SightZoomFrame opened = view.Tick(
            binoculars,
            togglePressed: true,
            zoomInHeld: false,
            zoomOutHeld: false,
            tanX: HumanView.DefaultTanX,
            viewModelTanX: HumanView.DefaultViewModelTanX);
        Assert.Equal(HumanView.DefaultTanX * BinocularsView.ZoomInFactor, opened.TanX, precision: 5);
        Assert.True(view.ZoomChangedThisTick);
        // No progress or filter check — retail input is exact; r2 shows immediate multiply & clamp.

        // Exactly one zoom factor per held tick at 0.95999998 / 1.04 (IGI.EXE 0x5399D0 constants).
        SightZoomFrame belowThreshold = view.Tick(
            binoculars,
            togglePressed: false,
            zoomInHeld: true,
            zoomOutHeld: false,
            tanX: 0.79f,
            viewModelTanX: HumanView.DefaultViewModelTanX);
        Assert.Equal(0.79f * BinocularsView.ZoomInFactor, belowThreshold.TanX, precision: 5);

        SightZoomFrame wider = view.Tick(
            binoculars,
            togglePressed: false,
            zoomInHeld: false,
            zoomOutHeld: true,
            tanX: belowThreshold.TanX,
            viewModelTanX: HumanView.DefaultViewModelTanX);
        Assert.Equal(belowThreshold.TanX * BinocularsView.ZoomOutFactor, wider.TanX, precision: 5);
        Assert.True(wider.TanX > belowThreshold.TanX);
    }

    [Fact]
    public void OpticalLatchesCanBeResetWhenFirstPersonControlEnds()
    {
        BinocularsView binoculars = new();
        binoculars.Tick(Binoculars, true, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(binoculars.IsOpen);
        binoculars.Reset();
        Assert.False(binoculars.IsOpen);

        Mp5SightZoom sight = new();
        WeaponType mp5 = new()
        {
            WeaponId = 7,
            ScriptId = "WEAPON_ID_MP5SD",
            Category = WeaponCategory.Gun,
            Weapon = new WeaponTaskBinding("TASKTYPE_GUNMP5SD", 0, [0.1f], string.Empty, 0),
        };
        sight.Tick(mp5, true, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(sight.IsActive);
        sight.Reset();
        Assert.False(sight.IsActive);
        SightZoomFrame sightFrame = sight.Tick(mp5, false, 0.2f, 0.2f);
        Assert.Equal(HumanView.DefaultTanX, sightFrame.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, sightFrame.ViewModelTanX);

        DragunovScope scope = new();
        WeaponType dragunov = new()
        {
            WeaponId = 32,
            ScriptId = "WEAPON_ID_DRAGUNOV",
            Category = WeaponCategory.Gun,
            Weapon = new WeaponTaskBinding("TASKTYPE_GUNDRAGUNOV", 0, [0.1f], string.Empty, 0),
        };
        scope.Tick(dragunov, true, false, false, false, HumanView.DefaultTanX, HumanView.DefaultViewModelTanX);
        Assert.True(scope.IsScoped);
        scope.Reset();
        Assert.False(scope.IsScoped);
    }

    [Fact]
    public void TheFirstEntryOfTheCycleThePlayerCarriesEndsUpInHand()
    {
        // Human+3400 starts at -1, so the first tick advances to cycle index 0. The shipped cycle
        // starts with the knife, which is why level 1 opens with a blade.
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Carry(Knife);

        Assert.Null(weapons.InHand);

        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void GunPickupAddsANewWeaponWithoutChangingWhatIsHeld()
    {
        PlayerWeapons weapons = new([20, 1, 7]);
        weapons.Carry(Knife);
        Equip(weapons);

        WeaponPickupResult result = weapons.PickUp(Glock, NineMillimetre, Glock.RoundsPerClip);

        Assert.True(result.AddedWeapon);
        Assert.Equal(0, result.AddedReserveRounds);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
        CarriedWeapon pickedUp = Assert.Single(weapons.Carried, carried => carried.Weapon.WeaponId == Glock.WeaponId);
        Assert.Equal(Glock.RoundsPerClip, pickedUp.Gun.RoundsInClip);
    }

    [Fact]
    public void GunPickupTurnsADuplicateWeaponIntoOneFullMagazineOfReserveAmmo()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 2);

        WeaponPickupResult result = weapons.PickUp(Glock, NineMillimetre, Glock.RoundsPerClip);

        Assert.False(result.AddedWeapon);
        Assert.Equal(Glock.RoundsPerClip, result.AddedReserveRounds);
        Assert.Single(weapons.Carried);
        Assert.Equal(2 + Glock.RoundsPerClip, weapons.Ammunition.Count(NineMillimetre.AmmoId));
    }

    [Fact]
    public void RuntimeAndAuthoredGunPickupsUseTheSameFullMagazinePayload()
    {
        PlayerWeapons weapons = new([1]);

        WeaponPickupResult result = weapons.PickUp(Glock, NineMillimetre, Glock.RoundsPerClip);

        CarriedWeapon gun = Assert.Single(weapons.Carried);
        Assert.True(result.AddedWeapon);
        Assert.Equal(Glock.RoundsPerClip, gun.Gun.RoundsInClip);
    }

    [Fact]
    public void TheCycleSkipsWhatIsNotCarried()
    {
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Mp5Sd, NineMillimetre);

        Equip(weapons);

        // Entries 0 and 1 -- knife and Glock -- are not carried, so it steps on to the MP5SD.
        Assert.Equal("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void NextWeaponStepsThroughTheCycle()
    {
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Knife);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Carry(Mp5Sd, NineMillimetre);

        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        PlayerInputState input = new();
        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_GLOCK", weapons.InHand?.Weapon.ScriptId);

        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);

        // And it wraps.
        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void BothSwayAnglesArriveOnTheSameTick()
    {
        // 35 degrees at 0.0581776 and 20 degrees at 0.0332444 are both 10.5 ticks. The rates were
        // chosen to make the pair one motion; a port that picked its own would tip the rig crooked on
        // the way down.
        HumanViewSway sway = new();
        sway.Lower();

        int pitchArrived = -1;
        int yawArrived = -1;
        for (int tick = 1; tick <= 40; tick++)
        {
            sway.Advance();

            if (pitchArrived < 0 && sway.Pitch == HumanViewSway.LoweredPitch)
            {
                pitchArrived = tick;
            }

            if (yawArrived < 0 && sway.Yaw == HumanViewSway.LoweredYaw)
            {
                yawArrived = tick;
            }
        }

        Assert.Equal(HumanViewSway.TicksToTravel, pitchArrived);
        Assert.Equal(HumanViewSway.TicksToTravel, yawArrived);
        Assert.True(sway.Settled);
    }

    [Fact]
    public void TheSwaySnapsToItsTargetRatherThanApproachingIt()
    {
        // The exact equality in Settled is only safe because the step snaps on overshoot. A step that
        // merely got close would leave every weapon state waiting for ever.
        HumanViewSway sway = new();
        sway.MoveTo(-1f, 0f, 0.3f, 0f);

        for (int i = 0; i < 10; i++)
        {
            sway.Advance();
        }

        Assert.Equal(-1f, sway.Pitch);
        Assert.True(sway.Settled);
    }

    [Fact]
    public void AWeaponSwapHidesTheExchangeBehindTheLoweredRig()
    {
        // The whole point of the animation: the model is not exchanged until the rig is out of the
        // frame, and control does not come back until it has returned.
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Knife);
        weapons.Carry(Glock, NineMillimetre);
        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.NextWeapon));
        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);

        // The rig tips out of the frame, and all the way down the knife is still what you are holding.
        for (int i = 0; i < HumanViewSway.TicksToTravel - 1; i++)
        {
            Tick(weapons, input, Frame());
            Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
        }

        Assert.True(weapons.Sway.Settled);
        Assert.Equal(HumanViewSway.LoweredPitch, weapons.Sway.Pitch);
        Assert.Equal(HumanViewSway.LoweredYaw, weapons.Sway.Yaw);

        // The tick it has arrived is the tick the model changes, and the rig starts back up.
        Tick(weapons, input, Frame());
        Assert.Equal("WEAPON_ID_GLOCK", weapons.InHand?.Weapon.ScriptId);
        Assert.Equal(0f, weapons.Sway.TargetPitch);
        Assert.Equal(HumanWeaponState.ChangingWeapon, weapons.State);

        // Control is not handed back until it is up again, and that takes the same eleven ticks.
        int raising = 0;
        while (weapons.State == HumanWeaponState.ChangingWeapon && raising < 4 * HumanViewSway.TicksToTravel)
        {
            Tick(weapons, input, Frame());
            raising++;
        }

        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.Equal(0f, weapons.Sway.Pitch);
        Assert.Equal(HumanViewSway.TicksToTravel, raising);
    }

    [Fact]
    public void TheSwayIsTheIdentityWhenNothingIsBeingPutAway()
    {
        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);
        Equip(weapons);

        Assert.True(weapons.Sway.Settled);
        Assert.Equal(Matrix3f.Identity, weapons.Sway.ToMatrix());
    }

    [Fact]
    public void HoldingFireWithTheKnifeGivesExactlyOneSwing()
    {
        // Maximum Rounds/Burst is 1 and the burst counter only resets when the trigger is released, so
        // leaning on the button does nothing after the first strike.
        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);

        Equip(weapons);
        int rounds = Run(weapons, fire: true, ticks: 90);

        Assert.Equal(1, rounds);
    }

    [Fact]
    public void TheKnifeSwingsEveryTwelveTicks()
    {
        // 140 rounds a minute is trunc(60/140 * 30) = 12 ticks -- 0.4 seconds. Nothing here chooses
        // that; it is the roster's Rounds/Minute through the gun's clock.
        Assert.Equal(12, GunTiming.TicksPerRound(140));

        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);
        Equip(weapons);

        List<int> spacing = Spacing(weapons, tapEvery: 1, ticks: 200);
        Assert.NotEmpty(spacing);
        Assert.All(spacing, gap => Assert.Equal(12, gap));
    }

    [Fact]
    public void TheKnifeNeitherSpendsNorEmpties()
    {
        // Calibre -1 means no ammunition record, and both the empty test and the decrement sit behind
        // it. Rounds/Clip is -1 and stays -1.
        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);
        Equip(weapons);

        CarriedWeapon held = weapons.InHand!.Value;
        Assert.False(held.Gun.ConsumesAmmunition);

        int rounds = Spacing(weapons, tapEvery: 1, ticks: 400).Count + 1;
        Assert.True(rounds > 10, $"only {rounds} strikes in 400 ticks.");
        Assert.Equal(-1, held.Gun.RoundsInClip);
    }

    [Fact]
    public void TheGlockIsSemiAutomaticAndSpendsOneRoundPerPull()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 100);
        Equip(weapons);

        CarriedWeapon held = weapons.InHand!.Value;
        int before = held.Gun.RoundsInClip;
        Assert.Equal(17, before);

        int rounds = Run(weapons, fire: true, ticks: 60);
        Assert.Equal(1, rounds);
        Assert.Equal(before - 1, held.Gun.RoundsInClip);
    }

    [Fact]
    public void TheGlockFiresEveryFourTicksWhenTapped()
    {
        // 400 rounds a minute is 4.5 ticks exactly, and the original TRUNCATES: the Glock's real rate
        // is 450, not its nominal 400.
        Assert.Equal(4, GunTiming.TicksPerRound(400));

        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 200);
        Equip(weapons);

        List<int> spacing = Spacing(weapons, tapEvery: 1, ticks: 60);
        Assert.NotEmpty(spacing);
        Assert.All(spacing, gap => Assert.Equal(4, gap));
    }

    [Fact]
    public void TheMp5SdKeepsFiringWhileHeldAndEmptiesItsMagazine()
    {
        Assert.Equal(2, GunTiming.TicksPerRound(700));

        PlayerWeapons weapons = new([7]);
        weapons.Carry(Mp5Sd, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 0);
        Equip(weapons);

        CarriedWeapon held = weapons.InHand!.Value;
        Assert.Equal(32, held.Gun.RoundsInClip);

        // Two ticks a round over the whole magazine is 64 ticks; give it a margin and check the
        // magazine is what stopped it, not the cap.
        int rounds = Run(weapons, fire: true, ticks: 120);

        Assert.Equal(32, rounds);
        Assert.Equal(0, held.Gun.RoundsInClip);
    }

    [Fact]
    public void DeathImmediatelyCancelsAHeldWeaponFire()
    {
        PlayerWeapons weapons = new([7]);
        weapons.Carry(Mp5Sd, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 0);
        Equip(weapons);

        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.Fire));
        Assert.Equal(HumanWeaponState.Firing, weapons.State);
        Assert.True(weapons.FiredThisTick);

        weapons.StopForDeath();

        Assert.Equal(HumanWeaponState.Firing, weapons.PreviousState);
        Assert.Equal(HumanWeaponState.Suspended, weapons.State);
        Assert.True(weapons.Flags.HasFlag(HumanWeaponFlagWord.Dead));
        Assert.True(weapons.Flags.HasFlag(HumanWeaponFlagWord.Dying));
        Assert.False(weapons.Flags.HasFlag(HumanWeaponFlagWord.Fire));
        Assert.False(weapons.FiredThisTick);
        Assert.Empty(weapons.Shots);
        Assert.Equal(WeaponAction.None, weapons.RequestedAction);
    }

    [Fact]
    public void TheMp5SdsBurstCapAndItsMagazineRunOutTogether()
    {
        // Maximum Rounds/Burst equals Rounds/Clip on every automatic weapon in the roster, which is
        // how one comparison gives both semi-automatic and fully automatic behaviour.
        Assert.Equal(Mp5Sd.RoundsPerClip, Mp5Sd.MaximumRoundsPerBurst);
    }

    [Fact]
    public void AnEmptyMagazineReloadsWithoutBeingAsked()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 50);
        Equip(weapons, clipEnded: false);

        CarriedWeapon held = weapons.InHand!.Value;
        held.Gun.State.RoundsInClip = 0;

        // The Ready state's reload branch is `(Reload pressed || clip == 0) && reserve && ...`, so an
        // empty magazine takes it with no button at all. The clip-end flag is what releases it, which
        // is why the rounds have not moved yet.
        Tick(weapons, new PlayerInputState(), Frame());
        Assert.Equal(HumanWeaponState.Reloading, weapons.State);
        Assert.Equal(0, held.Gun.RoundsInClip);

        Tick(weapons, new PlayerInputState(), Frame(), clipEnded: true);
        Assert.Equal(17, held.Gun.RoundsInClip);
        Assert.Equal(50 - 17, weapons.Ammunition.Count(NineMillimetre.AmmoId));
    }

    [Fact]
    public void AReloadKeepsWhatIsAlreadyInTheMagazine()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 50);
        Equip(weapons);

        CarriedWeapon held = weapons.InHand!.Value;
        held.Gun.State.RoundsInClip = 8;

        int moved = held.Gun.Reload(weapons.Ammunition);

        // Nine, not seventeen: there is no lost-partial-magazine rule anywhere in this game.
        Assert.Equal(9, moved);
        Assert.Equal(17, held.Gun.RoundsInClip);
        Assert.Equal(41, weapons.Ammunition.Count(NineMillimetre.AmmoId));
    }

    [Fact]
    public void FiringAnEmptyWeaponWithNoReserveGoesDry()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        Equip(weapons);

        weapons.InHand!.Value.Gun.State.RoundsInClip = 0;

        Run(weapons, fire: true, ticks: 1);
        Assert.Equal(HumanWeaponState.DryFire, weapons.State);
    }

    [Fact]
    public void EveryShotStartsAtTheEyeAndGoesDownTheBarrel()
    {
        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);
        Equip(weapons);

        Vector3d eye = new(1000, 2000, 3000);
        Vector3f forward = new(0f, 1f, 0f);

        PlayerInputState input = new();
        input.Accept(Frame(PlayerInputSlot.Fire));
        weapons.Tick(input, clipEnded: false, onGround: true);
        Assert.True(weapons.ResolveFire(eye, forward, new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f)));

        GunProjectile shot = Assert.Single(weapons.Shots);
        Assert.Equal(eye, shot.Origin);

        // The knife's spread is zero, so the cone sampling is skipped entirely and the strike goes
        // straight down the barrel -- and 1 metre of effective range is 4096 units of reach.
        Assert.Equal(forward.X, shot.Direction.X, 6);
        Assert.Equal(forward.Y, shot.Direction.Y, 6);
        Assert.Equal(forward.Z, shot.Direction.Z, 6);
        Assert.Equal(4096f, shot.RangeInUnits);
    }

    [Fact]
    public void TheGlocksRecoilIsExactlyItsFixedTermEveryShot()
    {
        // Fixed 1.5 degrees in pitch with both random terms at zero, so the disc sample is drawn and
        // then multiplied by nothing -- every Glock shot kicks identically.
        IgiRandom random = new(7);
        for (int i = 0; i < 20; i++)
        {
            (float x, float z) = HeldGun.SampleRecoil(Glock, random);
            Assert.Equal(1.5f * GunFiring.DegreesToRadians, x, 6);
            Assert.Equal(0f, z, 6);
        }
    }

    [Fact]
    public void TheKnifeHasNoRecoilAtAll()
    {
        IgiRandom random = new(3);
        (float x, float z) = HeldGun.SampleRecoil(Knife, random);

        Assert.Equal(0f, x);
        Assert.Equal(0f, z);
    }

    [Fact]
    public void TheRandomRecoilLandsInsideTheUnitDiscScaledByTheWeapon()
    {
        IgiRandom random = new(11);
        float limit = (0.8f + 0.8f) * GunFiring.DegreesToRadians;

        for (int i = 0; i < 200; i++)
        {
            (float x, float z) = HeldGun.SampleRecoil(Mp5Sd, random);

            // Fixed 0.8 plus random 0.8 across a unit disc, so pitch is within [0, 1.6] degrees and
            // yaw is pinned at zero because the MP5SD's Z terms are both zero.
            Assert.InRange(x, 0f, limit + 1e-5f);
            Assert.Equal(0f, z, 6);
        }
    }

    [Fact]
    public void RecoilAccumulatesAndDecaysByATenthEachTick()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 100);
        Equip(weapons);

        Run(weapons, fire: true, ticks: 1);
        float afterOne = weapons.RecoilPitch;
        Assert.True(afterOne > 0f);

        // No further round for three ticks -- the cooldown is four -- so the only thing happening is
        // the 0.9 damping.
        Equip(weapons);
        Assert.Equal(afterOne * PlayerWeapons.RecoilDamping, weapons.RecoilPitch, 6);
    }

    // Runs until the cycle has put a weapon in hand and the machine is idle again. The swap is a
    // TWO-PHASE lowering and raising of the rig -- 11 ticks each way -- so it is not instant.
    /// <summary>The frag grenade, as the roster and the physics table declare it.</summary>
    private static WeaponType Grenade => new()
    {
        WeaponId = 3,
        ScriptId = "WEAPON_ID_GRENADE",
        Category = WeaponCategory.Grenade,
        CalibreId = 10000,
        MuzzleVelocity = 15f,
        RoundsPerMinute = 60,
        RoundsPerClip = 3,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 1,
        EffectiveRange = 1f,
        GunModel = "135_01_1",
        StandAnimation = 22,
        FireAnimation1 = 24,
    };

    private static AmmoType GrenadeAmmo => new() { AmmoId = 8, ScriptId = "AMMO_ID_GRENADE", CalibreId = 10000 };

    /// <summary>The throw charges while fire is held and releases the round when it is let go.</summary>
    /// <remarks>The 0x40F590/0x40F640 charge-and-release, with <c>Human+1248</c> as the charge.</remarks>
    [Fact]
    public void AGrenadeChargesWhileHeldAndFliesOnRelease()
    {
        PlayerWeapons weapons = new([3]);
        weapons.Carry(Grenade, GrenadeAmmo);
        Equip(weapons);

        PlayerInputState input = new();

        // Twelve ticks of holding: the machine sits in the throw state, charging, firing nothing.
        for (int tick = 0; tick < 12; tick++)
        {
            Tick(weapons, input, Frame(PlayerInputSlot.Fire));
            Assert.Equal(HumanWeaponState.Throwing, weapons.State);
            Assert.False(weapons.FiredThisTick);
        }

        Assert.Equal(12, weapons.ThrowChargeTicks);

        // The release is the throw: one round leaves on that very tick.
        Tick(weapons, input, Frame());
        Assert.True(weapons.FiredThisTick);
        Assert.Single(weapons.Shots);
        Assert.Equal(2, weapons.InHand?.Gun.RoundsInClip);

        // The retail power curve: longer charges throw harder, between the tap's short toss and the
        // full wind-up's hurl, and the charge caps at retail's sixty ticks.
        Assert.True(
            PlayerWeapons.ChargedThrowSpeed(15f, 12) > PlayerWeapons.ChargedThrowSpeed(15f, 3));
        Assert.Equal(15f * PlayerWeapons.MinimumThrowShare, PlayerWeapons.ChargedThrowSpeed(15f, 3), 0.001f);
        Assert.Equal(15f * PlayerWeapons.MaximumThrowShare, PlayerWeapons.ChargedThrowSpeed(15f, 60), 0.001f);
        Assert.Equal(
            PlayerWeapons.ChargedThrowSpeed(15f, 60),
            PlayerWeapons.ChargedThrowSpeed(15f, 600));
    }

    private static void Equip(PlayerWeapons weapons, bool clipEnded = true)
    {
        PlayerInputState input = new();
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel; i++)
        {
            Tick(weapons, input, Frame(), clipEnded);
            if (weapons.State == HumanWeaponState.Ready && weapons.InHand is not null)
            {
                return;
            }
        }

        Assert.Fail("the weapon cycle never put anything in hand.");
    }

    private static PlayerInputFrame Frame(params PlayerInputSlot[] pressed)
    {
        PlayerInputFrame frame = default;
        foreach (PlayerInputSlot slot in pressed)
        {
            if (PlayerInputBindings.TryGetAction(slot, out PlayerAction action))
            {
                frame[(int)slot] = (uint)action;
            }
        }

        return frame;
    }

    private static void Tick(
        PlayerWeapons weapons,
        PlayerInputState input,
        PlayerInputFrame frame,
        bool clipEnded = false)
    {
        input.Accept(frame);
        weapons.Tick(input, clipEnded, onGround: true);

        // Where 0x464BD0 steps the weapon-change lowering: after the machine has read it. Without this
        // a swap never completes, because the state that swaps waits for the rig to arrive.
        weapons.Sway.Advance();
        weapons.ResolveFire(default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f));
    }

    // A tap, then long enough for the swap's lowering and raising to run out.
    private static void Press(PlayerWeapons weapons, PlayerInputState input, PlayerInputSlot slot)
    {
        Tick(weapons, input, Frame(slot));
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel; i++)
        {
            Tick(weapons, input, Frame());
            if (weapons.State == HumanWeaponState.Ready)
            {
                return;
            }
        }
    }

    // The rig is stood in for by reporting the clip as finished every tick. That is what a LOOPING
    // stand clip does on its wrap and what a one-shot clip does once it is clamped at its duration, and
    // it is the conservative choice: a state that waits on the flag gets to leave, so the cadence these
    // measure comes from the gun's clock alone and never from an animation this test invented.
    private static int Run(PlayerWeapons weapons, bool fire, int ticks, bool clipEnded = true)
    {
        PlayerInputState input = new();
        PlayerInputFrame frame = fire ? Frame(PlayerInputSlot.Fire) : Frame();

        int rounds = 0;
        for (int i = 0; i < ticks; i++)
        {
            Tick(weapons, input, frame, clipEnded);
            if (weapons.FiredThisTick)
            {
                rounds++;
            }
        }

        return rounds;
    }

    // Taps the trigger every `tapEvery` ticks and reports the gaps between the rounds that went off.
    private static List<int> Spacing(PlayerWeapons weapons, int tapEvery, int ticks)
    {
        PlayerInputState input = new();
        List<int> gaps = [];
        int last = -1;

        for (int i = 0; i < ticks; i++)
        {
            bool down = i % (tapEvery * 2) < tapEvery;
            Tick(weapons, input, down ? Frame(PlayerInputSlot.Fire) : Frame(), clipEnded: true);

            if (!weapons.FiredThisTick)
            {
                continue;
            }

            if (last >= 0)
            {
                gaps.Add(i - last);
            }

            last = i;
        }

        return gaps;
    }
}
