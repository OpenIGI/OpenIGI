using OpenIGI.Core.Configuration;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Regression coverage for issue 57's opt-in player cheats.</summary>
public sealed class PlayerCheatTests
{
    private static WeaponType TestWeapon => new()
    {
        WeaponId = 99,
        ScriptId = "WEAPON_ID_TEST",
        Category = WeaponCategory.Pistol,
        CalibreId = 919,
        RoundsPerMinute = 600,
        RoundsPerClip = 6,
        MaximumRoundsPerBurst = 6,
        BulletsPerRound = 1,
        EffectiveRange = 80f,
        FixedRecoilX = 1f,
    };

    private static AmmoType TestAmmo => new()
    {
        AmmoId = 4,
        ScriptId = "AMMO_ID_TEST",
        CalibreId = 919,
    };

    [Fact]
    public void RetailPlayerOptionsLeaveHumanPlayerTuningUntouched()
    {
        PlayerBody body = new() { Parameters = RetailParameters() };

        body.ApplyCheats(PlayerCheatOptions.Retail);

        Assert.Equal(1.75f, body.DeltaTranslationScale);
        Assert.Equal(17.5f * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour, body.JumpHorizontalSpeed);
        Assert.Equal(27f * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour, body.JumpSpeed);
        Assert.False(body.Health.Invulnerable);
    }

    [Fact]
    public void SpeedAndHighJumpScaleTheirOwnTuningValues()
    {
        PlayerBody body = new() { Parameters = RetailParameters() };
        PlayerCheatOptions cheats = PlayerCheatOptions.Retail with
        {
            SpeedMultiplier = 2f,
            JumpMultiplier = 3f,
        };

        body.ApplyCheats(cheats);

        Assert.Equal(3.5f, body.DeltaTranslationScale);
        Assert.Equal(2f * 17.5f * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour, body.JumpHorizontalSpeed);
        Assert.Equal(3f * 27f * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour, body.JumpSpeed);
        Assert.Equal(2f * 0.5f * HumanPlayerParameters.UnitsPerTickPerKilometrePerHour, body.AirControlSpeed);
    }

    [Fact]
    public void LiveHealthScalePreservesDamageFractionAndRestoresRetailMaximum()
    {
        HumanHealth health = new(HumanHitZoneTable.Player, 3f);
        health.ApplyDirect(HumanDamage.FrontZone, 1f);
        PlayerBody body = new();
        body.UseHealth(health);

        body.ApplyCheats(PlayerCheatOptions.Retail with { HealthMultiplier = 2f });

        Assert.Equal(6f, health.MaximumHealth, 5);
        Assert.Equal(2f, health.Damage, 5);
        Assert.Equal(2f / 3f, health.RemainingFraction, 5);

        body.ApplyCheats(PlayerCheatOptions.Retail);

        Assert.Equal(3f, health.MaximumHealth, 5);
        Assert.Equal(1f, health.Damage, 5);
        Assert.Equal(2f / 3f, health.RemainingFraction, 5);
    }

    [Fact]
    public void InfiniteHealthBlocksHitAndFallDamageButRetailHealthStillTakesIt()
    {
        HumanHealth retail = new(HumanHitZoneTable.Player, 3f);
        Assert.True(retail.Apply(HumanDamage.FrontZone, 1f) > 0f);
        Assert.True(retail.ApplyDirect(HumanDamage.FrontZone, 1f) > 0f);

        HumanHealth infinite = new(HumanHitZoneTable.Player, 3f) { Invulnerable = true };
        Assert.Equal(0f, infinite.Apply(HumanDamage.FrontZone, 1f));
        Assert.Equal(0f, infinite.ApplyDirect(HumanDamage.FrontZone, 1f));
        Assert.Equal(0f, infinite.Damage);
        Assert.Equal(0, infinite.Hits);
        Assert.False(infinite.IsDead);
    }

    [Fact]
    public void InfiniteAmmoFillsTheMagazineAndNeverTouchesTheReserve()
    {
        PlayerWeapons weapons = new([TestWeapon.WeaponId]);
        HeldGun gun = weapons.Carry(TestWeapon, TestAmmo);
        gun.State.RoundsInClip = 1;
        weapons.Ammunition.Add(TestAmmo.AmmoId, 3);

        weapons.ApplyCheats(PlayerCheatOptions.Retail with { InfiniteAmmo = true });
        Assert.Equal(TestWeapon.RoundsPerClip, gun.RoundsInClip);

        gun.Fire(true, default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f), new IgiRandom(1));

        Assert.Equal(TestWeapon.RoundsPerClip, gun.RoundsInClip);
        Assert.Equal(3, weapons.Ammunition.Count(TestAmmo.AmmoId));
    }

    [Fact]
    public void AWeaponCarriedAfterInfiniteAmmoWasSwitchedOnIsCoveredToo()
    {
        PlayerWeapons weapons = new([TestWeapon.WeaponId]);
        weapons.ApplyCheats(PlayerCheatOptions.Retail with { InfiniteAmmo = true });

        HeldGun gun = weapons.Carry(TestWeapon, TestAmmo);

        Assert.True(gun.PreserveMagazine);
        Assert.Equal(TestWeapon.RoundsPerClip, gun.RoundsInClip);
    }

    [Fact]
    public void SwitchingInfiniteAmmoOffMakesFiringConsumeRoundsAgain()
    {
        PlayerWeapons weapons = new([TestWeapon.WeaponId]);
        HeldGun gun = weapons.Carry(TestWeapon, TestAmmo);
        weapons.ApplyCheats(PlayerCheatOptions.Retail with { InfiniteAmmo = true });

        weapons.ApplyCheats(PlayerCheatOptions.Retail);
        gun.Fire(true, default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f), new IgiRandom(1));

        Assert.False(gun.PreserveMagazine);
        Assert.Equal(TestWeapon.RoundsPerClip - 1, gun.RoundsInClip);
    }

    [Fact]
    public void ARejectedShotIsNotRefundedIntoAMagazineItNeverLeft()
    {
        PlayerWeapons weapons = ArmedWeapons(PlayerCheatOptions.Retail with { InfiniteAmmo = true });
        FireOnce(weapons);

        Assert.True(weapons.FiredThisTick);
        Assert.False(weapons.RefundRejectedSimulationShot());
        Assert.Equal(TestWeapon.RoundsPerClip, weapons.InHand!.Value.Gun.RoundsInClip);
    }

    [Fact]
    public void NoReloadRefillsTheMagazineFromTheReserveUntilItRunsDry()
    {
        PlayerWeapons weapons = ArmedWeapons(PlayerCheatOptions.Retail with { NoReload = true });
        HeldGun gun = weapons.InHand!.Value.Gun;
        weapons.Ammunition.Add(TestAmmo.AmmoId, 1);

        FireOnce(weapons);
        Assert.Equal(TestWeapon.RoundsPerClip - 1, gun.RoundsInClip);

        // The next tick tops the magazine back up from the reserve...
        TickIdle(weapons);
        Assert.Equal(TestWeapon.RoundsPerClip, gun.RoundsInClip);
        Assert.Equal(0, weapons.Ammunition.Count(TestAmmo.AmmoId));

        // ...and with the reserve spent, a fired round stays gone.
        TickIdle(weapons, GunCooldownTicks);
        FireOnce(weapons);
        TickIdle(weapons);
        Assert.Equal(TestWeapon.RoundsPerClip - 1, gun.RoundsInClip);
    }

    [Fact]
    public void PreserveMagazineKeepsTheFiredRoundWhileRetailFiringConsumesIt()
    {
        HeldGun retail = new(TestWeapon, TestAmmo);
        retail.State.RoundsInClip = TestWeapon.RoundsPerClip;
        retail.Fire(true, default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f), new IgiRandom(2));
        Assert.Equal(TestWeapon.RoundsPerClip - 1, retail.RoundsInClip);

        HeldGun cheat = new(TestWeapon, TestAmmo) { PreserveMagazine = true };
        cheat.State.RoundsInClip = TestWeapon.RoundsPerClip;
        cheat.Fire(true, default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f), new IgiRandom(2));
        Assert.Equal(TestWeapon.RoundsPerClip, cheat.RoundsInClip);
    }

    [Fact]
    public void NoRecoilSuppressesTheViewKickButRetailFiringStillKicks()
    {
        PlayerWeapons retail = ArmedWeapons(PlayerCheatOptions.Retail);
        FireOnce(retail);

        PlayerWeapons cheat = ArmedWeapons(PlayerCheatOptions.Retail with { NoRecoil = true });
        FireOnce(cheat);

        Assert.NotEqual(0f, retail.RecoilPitch);
        Assert.Equal(0f, cheat.RecoilPitch);
    }

    [Fact]
    public void AllWeaponsCanExtendTheCycleWithoutDuplicatingOwnedWeapons()
    {
        PlayerWeapons weapons = new([TestWeapon.WeaponId]);
        weapons.Carry(TestWeapon, TestAmmo);

        weapons.EnsureCycleContains(123);
        weapons.EnsureCycleContains(123);

        Assert.True(weapons.HasWeapon(TestWeapon.WeaponId));
        Assert.False(weapons.HasWeapon(123));
    }

    [Fact]
    public void CheatsStayRetailUntilCheatsAreEnabled()
    {
        PlayerCheatOptions configured = PlayerCheatOptions.Retail with
        {
            SpeedMultiplier = 3f,
            AllWeapons = true,
            InfiniteHealth = true,
        };

        Assert.Equal(PlayerCheatOptions.Retail, PlayerCheatOptions.Activate(configured, false));

        PlayerCheatOptions active = PlayerCheatOptions.Activate(configured, true);
        Assert.Equal(3f, active.SpeedMultiplier);
        Assert.True(active.AllWeapons);
        Assert.True(active.InfiniteHealth);
    }

    [Fact]
    public void CheatVariablesAreCheatGatedAndRetailByDefault()
    {
        Assert.Equal(CVarOptions.Cheat, PlayerCheatCVars.Speed.Flags);
        Assert.Equal(CVarOptions.Cheat, PlayerCheatCVars.InfiniteAmmo.Flags);
        Assert.Equal(CVarOptions.Cheat, PlayerCheatCVars.UnlockAllLevels.Flags);
        Assert.Equal(CVarOptions.Developer | CVarOptions.Hidden, PlayerCheatCVars.DeveloperMode.Flags);
        Assert.Equal(CVarOptions.Developer | CVarOptions.Hidden, PlayerCheatCVars.CheatsEnabled.Flags);
        Assert.Equal(PlayerCheatOptions.Retail, PlayerCheatOptions.Current);
    }

    private static HumanPlayerParameters RetailParameters()
    {
        float[] values = new float[HumanPlayerParameters.ArgumentCount];
        values[0] = 1.75f;
        values[1] = 17.5f;
        values[2] = 27f;
        values[3] = 0.5f;
        values[4] = 0.85f;
        values[5] = 0.85f;
        values[6] = 0.25f;
        values[7] = 3f;
        values[8] = 0.5f;
        return new HumanPlayerParameters(values);
    }

    private static PlayerWeapons ArmedWeapons(PlayerCheatOptions cheats)
    {
        PlayerWeapons weapons = new([TestWeapon.WeaponId]);
        weapons.Carry(TestWeapon, TestAmmo);
        weapons.ApplyCheats(cheats);

        PlayerInputState input = new();
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel; i++)
        {
            input.Accept(default);
            weapons.Tick(input, clipEnded: true, onGround: true);
            weapons.Sway.Advance();
            weapons.ResolveFire(default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f));
            if (weapons.State == HumanWeaponState.Ready && weapons.InHand is not null)
            {
                return weapons;
            }
        }

        throw new InvalidOperationException("test weapon did not equip");
    }

    private static void FireOnce(PlayerWeapons weapons)
    {
        PlayerInputState input = new();
        PlayerInputFrame frame = default;
        if (PlayerInputBindings.TryGetAction(PlayerInputSlot.Fire, out PlayerAction action))
        {
            frame[(int)PlayerInputSlot.Fire] = (uint)action;
        }

        // Two ticks of trigger: the fire state's entry tick (0x40EF60) asks nothing of the gun, the next one fires.
        for (int tick = 0; tick < 2; tick++)
        {
            input.Accept(frame);
            weapons.Tick(input, clipEnded: true, onGround: true);
            weapons.Sway.Advance();
            weapons.ResolveFire(default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f));
        }
    }

    // Enough ticks for the test weapon to come off its between-rounds cooldown with room to spare.
    private static int GunCooldownTicks => 2 * GunTiming.TicksPerRound(TestWeapon.RoundsPerMinute);

    private static void TickIdle(PlayerWeapons weapons, int ticks = 1)
    {
        PlayerInputState input = new();
        for (int i = 0; i < ticks; i++)
        {
            input.Accept(default);
            weapons.Tick(input, clipEnded: true, onGround: true);
            weapons.Sway.Advance();
            weapons.ResolveFire(default, new Vector3f(0f, 1f, 0f), new Vector3f(1f, 0f, 0f), new Vector3f(0f, 0f, 1f));
        }
    }
}
