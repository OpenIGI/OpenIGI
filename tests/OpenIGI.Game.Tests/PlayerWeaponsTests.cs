using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

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
    public void DeathClearsPendingM203FireAndTheLastM203Shot()
    {
        PlayerWeapons weapons = new([14]);
        AmmoType primaryAmmo = new() { AmmoId = 0, ScriptId = "AMMO_ID_556", CalibreId = 556 };
        weapons.Carry(M16A2, primaryAmmo);
        weapons.Ammunition.Add(M203Ammo.AmmoId, 2);
        weapons.SetM203Ammunition(M203Ammo);
        Equip(weapons);

        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.AlternateFire));
        Assert.Single(weapons.M203Shots);

        weapons.StopForDeath();

        Assert.Empty(weapons.M203Shots);
        Assert.False(weapons.Flags.HasFlag(HumanWeaponFlagWord.AlternateFire));
        Assert.False(weapons.ResolveFire(
            default,
            new Vector3f(0f, 1f, 0f),
            new Vector3f(1f, 0f, 0f),
            new Vector3f(0f, 0f, 1f)));
        Assert.Empty(weapons.M203Shots);
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
        Assert.False(weapons.HideFirstPersonWeapon);
        Assert.Equal(0, held.Gun.RoundsInClip);

        Tick(weapons, new PlayerInputState(), Frame(), clipEnded: true);
        Assert.False(weapons.HideFirstPersonWeapon);
        Assert.Equal(17, held.Gun.RoundsInClip);
        Assert.Equal(50 - 17, weapons.Ammunition.Count(NineMillimetre.AmmoId));
    }

    private static readonly AmmoType TwelveGauge = new() { AmmoId = 5, ScriptId = "AMMO_ID_12", CalibreId = 12 };

    private static WeaponType Spas12 => new()
    {
        WeaponId = 8,
        ScriptId = "WEAPON_ID_SPAS12",
        Category = WeaponCategory.Gun,
        CalibreId = 12,
        RoundsPerMinute = 50,
        RoundsPerClip = 7,
        MaximumRoundsPerBurst = 1,
        BulletsPerRound = 12,
        StandAnimation = 58,
        FireAnimation1 = 60,
        FireAnimation2 = -1,
        FireAnimation3 = -1,
        ReloadAnimation = 61,
        Weapon = new WeaponTaskBinding("TASKTYPE_GUNSPAS12", 0, [], string.Empty, 0),
    };

    [Fact]
    public void TheSpasLoadsAShellAClipUntilTheTubeIsFull()
    {
        // 0x472AE0: open (61), a shell in at the end of every insert clip (62), then close (63).
        PlayerWeapons weapons = new([8]);
        weapons.Carry(Spas12, TwelveGauge);
        weapons.Ammunition.Add(TwelveGauge.AmmoId, 10);
        Equip(weapons, clipEnded: false);
        CarriedWeapon held = weapons.InHand!.Value;
        held.Gun.State.RoundsInClip = 4;

        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.Reload));
        Assert.Equal(HumanWeaponState.Reloading, weapons.State);
        Assert.Equal(WeaponAction.ShellReloadOpen, weapons.RequestedAction);
        Assert.Equal(new WeaponAnimations(61, -1, false, false, WeaponAnimationTable.DefaultBlendSeconds, 0f),
            WeaponAnimationTable.Select(held.Weapon, WeaponAction.ShellReloadOpen));

        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(WeaponAction.ShellReloadInsert, weapons.RequestedAction);
        Assert.Equal(4, held.Gun.RoundsInClip);

        for (int shell = 5; shell <= 6; shell++)
        {
            Tick(weapons, input, Frame(), clipEnded: true);
            Assert.Equal(shell, held.Gun.RoundsInClip);
            Assert.Equal(WeaponAction.ShellReloadInsert, weapons.RequestedAction);
        }

        // The seventh fills the tube, and the gun closes.
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(7, held.Gun.RoundsInClip);
        Assert.Equal(WeaponAction.ShellReloadClose, weapons.RequestedAction);
        Assert.Equal(HumanWeaponState.Reloading, weapons.State);

        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.Equal(10 - 3, weapons.Ammunition.Count(TwelveGauge.AmmoId));
        Assert.Equal(0, held.Gun.State.ReloadStage);
    }

    [Fact]
    public void TheSpasClosesWhenThePocketRunsDry()
    {
        PlayerWeapons weapons = new([8]);
        weapons.Carry(Spas12, TwelveGauge);
        weapons.Ammunition.Add(TwelveGauge.AmmoId, 2);
        Equip(weapons, clipEnded: false);
        CarriedWeapon held = weapons.InHand!.Value;
        held.Gun.State.RoundsInClip = 0;

        PlayerInputState input = new();
        Tick(weapons, input, Frame());
        Tick(weapons, input, Frame(), clipEnded: true);
        Tick(weapons, input, Frame(), clipEnded: true);
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(2, held.Gun.RoundsInClip);

        // The tube has room but the pocket is empty: the insert clip that was playing ends, and the gun closes.
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(WeaponAction.ShellReloadClose, weapons.RequestedAction);
        Tick(weapons, input, Frame(), clipEnded: true);
        Assert.Equal(HumanWeaponState.Ready, weapons.State);
        Assert.Equal(2, held.Gun.RoundsInClip);
    }

    [Fact]
    public void M16A2ReloadLeavesTheGunAttachedWhileTheRetailViewClipPlays()
    {
        PlayerWeapons weapons = new([14]);
        AmmoType primaryAmmo = new() { AmmoId = 0, ScriptId = "AMMO_ID_556", CalibreId = 556 };
        weapons.Carry(M16A2, primaryAmmo);
        weapons.Ammunition.Add(primaryAmmo.AmmoId, 60);
        Equip(weapons, clipEnded: false);

        CarriedWeapon held = weapons.InHand!.Value;
        held.Gun.State.RoundsInClip = 0;
        Tick(weapons, new PlayerInputState(), Frame());

        Assert.Equal(HumanWeaponState.Reloading, weapons.State);
        Assert.Equal(WeaponAction.Reload, weapons.RequestedAction);
        Assert.False(weapons.HideFirstPersonWeapon);
    }

    [Fact]
    public void DualUziReloadUsesTheNoGunFirstPersonPresentation()
    {
        PlayerWeapons weapons = new([40]);
        weapons.Carry(DualUzi, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 64);
        Equip(weapons, clipEnded: false);

        CarriedWeapon held = weapons.InHand!.Value;
        held.Gun.State.RoundsInClip = 0;
        Tick(weapons, new PlayerInputState(), Frame());

        Assert.Equal(HumanWeaponState.Reloading, weapons.State);
        Assert.True(weapons.HideFirstPersonWeapon);
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

    [Fact]
    public void M16A2AlternateFireConsumesM203AmmoWithoutConsumingTheRifleMagazine()
    {
        PlayerWeapons weapons = new([14]);
        AmmoType primaryAmmo = new() { AmmoId = 0, ScriptId = "AMMO_ID_556", CalibreId = 556 };
        weapons.Carry(M16A2, primaryAmmo);
        weapons.Ammunition.Add(M203Ammo.AmmoId, 2);
        weapons.SetM203Ammunition(M203Ammo);
        Equip(weapons);

        int rifleRounds = weapons.InHand!.Value.Gun.RoundsInClip;
        PlayerInputState input = new();
        Tick(weapons, input, Frame(PlayerInputSlot.AlternateFire));

        Assert.True(weapons.FiredThisTick);
        Assert.Equal(WeaponAction.Fire, weapons.RequestedAction);
        Assert.Equal(1, weapons.RequestedFireVariant);
        M203Projectile shot = Assert.Single(weapons.M203Shots);
        Assert.Equal(270f, shot.MuzzleVelocityKilometresPerHour);
        Assert.Equal(5f, shot.ExplosionRadiusMetres);
        Assert.Equal(1f, shot.ActivateRadiusMetres);
        Assert.Equal(1, weapons.Ammunition.Count(M203Ammo.AmmoId));
        Assert.Equal(rifleRounds, weapons.InHand!.Value.Gun.RoundsInClip);
        Assert.Empty(weapons.Shots);
    }

    [Fact]
    public void RejectedM203SimulationShotRefundsItsConsumedRoundThroughTheWeaponContract()
    {
        PlayerWeapons weapons = new([14]);
        AmmoType primaryAmmo = new() { AmmoId = 0, ScriptId = "AMMO_ID_556", CalibreId = 556 };
        weapons.Carry(M16A2, primaryAmmo);
        weapons.Ammunition.Add(M203Ammo.AmmoId, 1);
        weapons.SetM203Ammunition(M203Ammo);
        Equip(weapons);

        Tick(weapons, new PlayerInputState(), Frame(PlayerInputSlot.AlternateFire));

        Assert.Equal(0, weapons.Ammunition.Count(M203Ammo.AmmoId));
        Assert.True(weapons.RefundRejectedSimulationShot());
        Assert.Equal(1, weapons.Ammunition.Count(M203Ammo.AmmoId));
        Assert.False(weapons.RefundRejectedSimulationShot());
        Assert.Equal(1, weapons.Ammunition.Count(M203Ammo.AmmoId));
    }

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
        GunProjectile shot = Assert.Single(weapons.Shots);
        Assert.Equal(2, weapons.InHand?.Gun.RoundsInClip);
        float held = 12f;
        float curve = MathF.Pow(held * 50f, 0.7f) + (held * 0.1f);
        float tap = MathF.Pow(3f * 50f, 0.7f) + 0.3f;
        float fullCharge = MathF.Pow(60f * 50f, 0.7f) + 6f;
        float expectedShare = (curve - tap) / (fullCharge - tap);
        float expectedSpeed = 15f * (PlayerWeapons.MinimumThrowShare
            + ((PlayerWeapons.MaximumThrowShare - PlayerWeapons.MinimumThrowShare) * expectedShare));
        Assert.Equal(expectedSpeed, shot.SpeedMetresPerSecond, 0.001f);
        Assert.Equal(
            shot.SpeedMetresPerSecond * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond,
            shot.VelocityUnitsPerTick.Length,
            0.01f);

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
}
