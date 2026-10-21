using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Player;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The knife through the weapon machine: one swing per pull, its own cadence, nothing spent and no
/// recoil.
/// </summary>
public sealed class PlayerKnifeTests
{
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
    public void TheKnifeHasNoRecoilAtAll()
    {
        IgiRandom random = new(3);
        (float x, float z) = HeldGun.SampleRecoil(Knife, random);

        Assert.Equal(0f, x);
        Assert.Equal(0f, z);
    }
}
