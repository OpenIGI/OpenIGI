using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks the firing path against the shipped weapon scripts rather than against transcribed literals.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PlayerWeaponsTests"/> proves the machine behaves; this proves the numbers it behaves on
/// are the game's. Every assertion here reads a roster field and derives from it — none of them is a
/// value chosen by this port.
/// </para>
/// </remarks>
public sealed class RetailWeaponFiringTests
{
    /// <summary>The one roster entry whose burst cap is neither one nor its magazine.</summary>
    /// <remarks>
    /// Clip 1, burst 60. An earlier note claimed the pair is <i>always</i> 1 or <c>Rounds/Clip</c>;
    /// across the twenty-four entries exactly this one is not. It costs nothing, because the magazine
    /// of one empties on the first use and <c>Gun_Fire</c> tests burst before the magazine — the cap is
    /// simply unreachable.
    /// </remarks>
    private const string BurstCapException = "WEAPON_ID_MEDIPACK";

    [RequiresGameDataFact]
    public void EveryWeaponButTheMedipackCapsItsBurstAtOneOrItsWholeMagazine()
    {
        // The one comparison in Gun_Fire gives both semi-automatic and fully automatic behaviour, and
        // this is why: nothing between the two ever ships on anything that shoots.
        int exceptions = 0;

        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            bool obeys = weapon.MaximumRoundsPerBurst == 1
                || weapon.MaximumRoundsPerBurst == weapon.RoundsPerClip;

            if (!obeys)
            {
                exceptions++;
                Assert.Equal(BurstCapException, weapon.ScriptId);
                continue;
            }

            Assert.True(
                obeys,
                $"{weapon.ScriptId} caps its burst at {weapon.MaximumRoundsPerBurst} of "
                    + $"{weapon.RoundsPerClip} rounds.");
        }

        // Named rather than tolerated: if the medipack were ever brought into line this test should
        // fail and the note should change with it.
        Assert.Equal(1, exceptions);
    }

    [RequiresGameDataFact]
    public void EveryWeaponsRateOfFireIsAWholeNumberOfTicks()
    {
        int measured = 0;

        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            Assert.True(weapon.RoundsPerMinute > 0, $"{weapon.ScriptId} has no rate of fire.");

            int ticks = GunTiming.TicksPerRound(weapon.RoundsPerMinute);
            measured++;

            // The lower bound is what matters: a rate high enough to truncate to zero would let a
            // weapon fire every tick regardless of its record, and the fastest thing in the roster --
            // the sentry gun at 1000 rounds a minute -- lands on exactly 1. The upper bound is the
            // emplacement guns' 12 rounds a minute, five seconds a round.
            Assert.InRange(ticks, 1, 150);
            Assert.Equal((int)(60.0 / weapon.RoundsPerMinute * 30.0), ticks);
        }

        Assert.Equal(RetailWeaponRoster.Value.Weapons.Count, measured);
    }

    [RequiresGameDataFact]
    public void TheKnifeIsAGunWithATwelveTickSwingAndNoAmmunition()
    {
        WeaponType knife = Weapon("WEAPON_ID_KNIFE");

        // Category 3 is what sends it to the striking state instead of the firing one; everything
        // else about it is an ordinary firearm's fields.
        Assert.Equal(PlayerWeapons.StrikingCategory, (int)knife.Category);
        Assert.Equal(140, knife.RoundsPerMinute);
        Assert.Equal(12, GunTiming.TicksPerRound(knife.RoundsPerMinute));
        Assert.Equal(1, knife.MaximumRoundsPerBurst);
        Assert.Equal(-1, knife.RoundsPerClip);

        // No calibre, so no ammunition record -- which is exactly why a knife never runs out and never
        // spends anything: both guards in Gun_Fire's tail sit behind that record.
        Assert.Equal(-1, knife.CalibreId);

        // A metre of reach, and a cone of exactly zero, which makes Gun_Fire skip the sampling.
        Assert.Equal(1f, knife.EffectiveRange);
        Assert.Equal(4096f, GunShotBallistics.RangeInUnits(knife));
        Assert.Equal(0f, GunFiring.ConeAngle(knife, roundsThisBurst: 0));
        Assert.Equal(0f, GunFiring.ConeAngle(knife, roundsThisBurst: 10));

        // Its fire animations are the ones the striking state cycles.
        Assert.Equal(28, knife.FireAnimation1);
        Assert.Equal(29, knife.FireAnimation2);
        Assert.Equal(30, knife.FireAnimation3);
    }

    [RequiresGameDataFact]
    public void TheKnifesProjectileIsAStabAndItsWeaponClassIsAGun()
    {
        WeaponType knife = Weapon("WEAPON_ID_KNIFE");

        // The answer to whether melee runs through the gun path: it does. Only the class spawned per
        // projectile differs, which is the whole of what makes a swing not a shot.
        Assert.Equal("TASKTYPE_GUN", knife.Weapon.TypeName);
        Assert.Equal("TASKTYPE_KNIFESTAB", knife.Projectile.TypeName);
    }

    [RequiresGameDataFact]
    public void TheGlockIsSemiAutomaticAtFourTicks()
    {
        WeaponType glock = Weapon("WEAPON_ID_GLOCK");

        Assert.Equal(WeaponCategory.Pistol, glock.Category);
        Assert.Equal(400, glock.RoundsPerMinute);

        // 4.5 truncates to 4, so the Glock's real cadence is 450 rounds a minute rather than its
        // stated 400.
        Assert.Equal(4, GunTiming.TicksPerRound(glock.RoundsPerMinute));
        Assert.Equal(1, glock.MaximumRoundsPerBurst);
        Assert.Equal(17, glock.RoundsPerClip);
        Assert.Equal(919, glock.CalibreId);

        // The recoil pair the human-facing fire handler samples: a fixed 1.5 degrees of pitch and no
        // random term at all, so every Glock shot kicks identically.
        Assert.Equal(1.5f, glock.FixedRecoilX);
        Assert.Equal(0f, glock.RandomRecoilX);
        Assert.Equal(0f, glock.FixedRecoilZ);
        Assert.Equal(0f, glock.RandomRecoilZ);
    }

    [RequiresGameDataFact]
    public void TheMp5SdIsFullyAutomaticAtTwoTicks()
    {
        WeaponType mp5 = Weapon("WEAPON_ID_MP5SD");

        Assert.Equal(WeaponCategory.Gun, mp5.Category);
        Assert.Equal(700, mp5.RoundsPerMinute);
        Assert.Equal(2, GunTiming.TicksPerRound(mp5.RoundsPerMinute));

        // The burst cap IS the magazine, which is how holding the trigger empties it.
        Assert.Equal(32, mp5.RoundsPerClip);
        Assert.Equal(mp5.RoundsPerClip, mp5.MaximumRoundsPerBurst);
        Assert.Equal(919, mp5.CalibreId);

        // Its own weapon class, with a task tail -- one of the five the roster names.
        Assert.Equal("TASKTYPE_GUNMP5SD", mp5.Weapon.TypeName);
    }

    [RequiresGameDataFact]
    public void EverySpreadPairIsSignedAboutZeroSoTheConeMustBeAbsolute()
    {
        int negative = 0;

        foreach (WeaponType weapon in RetailWeaponRoster.Value.Weapons)
        {
            if (weapon.MinimumSpread < 0f)
            {
                negative++;
            }

            // Whatever the sign in the data, the cone the gun opens is never negative -- Gun_Fire takes
            // the absolute value AFTER converting to radians.
            Assert.True(GunFiring.ConeAngle(weapon, 0) >= 0f, $"{weapon.ScriptId}'s near cone is negative.");
            Assert.True(GunFiring.ConeAngle(weapon, 9) >= 0f, $"{weapon.ScriptId}'s far cone is negative.");
        }

        // If none of them were negative the absolute value would be dead code and this reading would be
        // unsupported. Several are.
        Assert.True(negative > 0, "no weapon stores a negative minimum spread.");
    }

    [RequiresGameDataFact]
    public void MissionOnesLoadoutStartsWithTheKnifeAndCyclesToTheGlockAndTheMp5()
    {
        // The end-to-end acceptance, on the real data: level 1 declares BINOCULARS, MP5SD, GLOCK and
        // KNIFE, and the cycle table decides which of them you open with.
        IReadOnlyList<int> cycle = RetailHumanPlayerConfig.Value.WeaponCycle;
        Assert.NotEmpty(cycle);

        PlayerWeapons weapons = new(cycle);
        foreach (string id in new[] { "WEAPON_ID_MP5SD", "WEAPON_ID_GLOCK", "WEAPON_ID_KNIFE" })
        {
            weapons.Carry(Weapon(id));
        }

        PlayerInputState input = new();
        Settle(weapons, input);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        Assert.Equal("WEAPON_ID_GLOCK", Next(weapons, input));
        Assert.Equal("WEAPON_ID_MP5SD", Next(weapons, input));
        Assert.Equal("WEAPON_ID_KNIFE", Next(weapons, input));
    }

    [RequiresGameDataFact]
    public void TheNumberKeysSelectCategoriesAndStepWithinThem()
    {
        // 0x411000 selects a CATEGORY, not a weapon, and pressing the same key again walks to the next
        // member you are carrying. The retail table puts the knife alone in category 1 and the three
        // pistols in category 2.
        HumanPlayerConfig config = RetailHumanPlayerConfig.Value;
        Assert.Equal(HumanPlayerWeaponCategory.CategoryCount, config.Categories.Count);

        PlayerWeapons weapons = new(config.WeaponCycle);
        weapons.UseCategories(config.Categories);
        foreach (string id in new[] { "WEAPON_ID_KNIFE", "WEAPON_ID_GLOCK", "WEAPON_ID_MP5SD" })
        {
            weapons.Carry(Weapon(id));
        }

        PlayerInputState input = new();
        Settle(weapons, input);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        // The category holding the Glock, whichever the table says it is.
        int glock = Weapon("WEAPON_ID_GLOCK").WeaponId;
        int category = -1;
        for (int i = 0; i < config.Categories.Count; i++)
        {
            if (config.Categories[i].WeaponIds.Contains(glock))
            {
                category = i;
                break;
            }
        }

        Assert.True(category >= 0, "no category holds the Glock.");
        Assert.Equal("WEAPON_ID_GLOCK", Press(weapons, input, PlayerInputSlot.WeaponCategory1 + category));

        // Pressing it again steps within the category, and with only the Glock carried out of the three
        // pistols it comes back round to the Glock rather than to something not carried.
        Assert.Equal("WEAPON_ID_GLOCK", Press(weapons, input, PlayerInputSlot.WeaponCategory1 + category));
    }

    [RequiresGameDataFact]
    public void NextWeaponAdvancesFromTheWeaponChosenByCategoryTwo()
    {
        HumanPlayerConfig config = RetailHumanPlayerConfig.Value;
        WeaponType glock = Weapon("WEAPON_ID_GLOCK");
        WeaponType mp5 = Weapon("WEAPON_ID_MP5SD");
        int category = CategoryOf(config, glock.WeaponId);

        PlayerWeapons weapons = new(config.WeaponCycle);
        weapons.UseCategories(config.Categories);
        weapons.Carry(Weapon("WEAPON_ID_KNIFE"));
        weapons.Carry(glock);
        weapons.Carry(mp5);

        PlayerInputState input = new();
        Settle(weapons, input);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        Assert.Equal("WEAPON_ID_GLOCK", Press(
            weapons,
            input,
            PlayerInputSlot.WeaponCategory1 + category));
        Assert.Equal("WEAPON_ID_MP5SD", Next(weapons, input));
    }

    [RequiresGameDataFact]
    public void OneCategoryPressIsOneSwitch()
    {
        // A key held for several ticks is ONE rising edge and must not walk the category. The original
        // tests the edge, not the level, for all nine.
        HumanPlayerConfig config = RetailHumanPlayerConfig.Value;

        PlayerWeapons weapons = new(config.WeaponCycle);
        weapons.UseCategories(config.Categories);
        foreach (string id in new[] { "WEAPON_ID_KNIFE", "WEAPON_ID_GLOCK", "WEAPON_ID_MP5SD" })
        {
            weapons.Carry(Weapon(id));
        }

        PlayerInputState input = new();
        Settle(weapons, input);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        int glockCategory = CategoryOf(config, Weapon("WEAPON_ID_GLOCK").WeaponId);
        PlayerInputSlot slot = PlayerInputSlot.WeaponCategory1 + glockCategory;
        Assert.True(PlayerInputBindings.TryGetAction(slot, out PlayerAction action));

        PlayerInputFrame held = default;
        held[(int)slot] = (uint)action;

        // Hold it down for a dozen ticks -- longer than a swap -- and only one switch may happen.
        for (int i = 0; i < 12; i++)
        {
            input.Accept(held);
            weapons.Tick(input, clipEnded: false, onGround: true);
            weapons.Sway.Advance();
        }

        Settle(weapons, input);
        Assert.Equal("WEAPON_ID_GLOCK", weapons.InHand?.Weapon.ScriptId);
    }

    private static int CategoryOf(HumanPlayerConfig config, int weaponId)
    {
        for (int i = 0; i < config.Categories.Count; i++)
        {
            if (config.Categories[i].WeaponIds.Contains(weaponId))
            {
                return i;
            }
        }

        Assert.Fail($"no category holds weapon {weaponId}.");
        return -1;
    }

    private static string? Press(PlayerWeapons weapons, PlayerInputState input, PlayerInputSlot slot)
    {
        PlayerInputFrame pressed = default;
        if (PlayerInputBindings.TryGetAction(slot, out PlayerAction action))
        {
            pressed[(int)slot] = (uint)action;
        }

        input.Accept(pressed);
        weapons.Tick(input, clipEnded: false, onGround: true);
        weapons.Sway.Advance();

        Settle(weapons, input);
        return weapons.InHand?.Weapon.ScriptId;
    }

    // Runs until the machine is idle again. A swap tips the rig out of the frame and back -- eleven
    // ticks each way -- so nothing about it is instant.
    private static void Settle(PlayerWeapons weapons, PlayerInputState input)
    {
        for (int i = 0; i < 4 * HumanViewSway.TicksToTravel; i++)
        {
            input.Accept(default);
            weapons.Tick(input, clipEnded: false, onGround: true);
            weapons.Sway.Advance();

            if (weapons.State == HumanWeaponState.Ready && weapons.InHand is not null)
            {
                return;
            }
        }
    }

    private static string? Next(PlayerWeapons weapons, PlayerInputState input)
    {
        PlayerInputFrame pressed = default;
        pressed[(int)PlayerInputSlot.NextWeapon] = (uint)PlayerAction.NextWeapon;

        input.Accept(pressed);
        weapons.Tick(input, clipEnded: false, onGround: true);
        weapons.Sway.Advance();

        Settle(weapons, input);
        return weapons.InHand?.Weapon.ScriptId;
    }

    private static WeaponType Weapon(string scriptId)
    {
        Assert.True(
            RetailWeaponRoster.Value.TryGetByScriptId(scriptId, out WeaponType weapon),
            $"{scriptId} is not in the roster.");

        return weapon;
    }
}
