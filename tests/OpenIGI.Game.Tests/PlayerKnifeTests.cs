using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The knife through the weapon machine: one swing per pull, its own cadence, nothing spent and no
/// recoil.
/// </summary>
public sealed class PlayerKnifeTests
{
    /// <summary>
    /// Holding the button keeps the knife swinging: 0x40FA80 goes back to the ready hub at the clip's end, and the ready tick and
    /// the melee entry ask nothing of the gun, which resets its one-round burst. Retail, mission 1: a 90-tick hold swung every
    /// 24 ticks, 23 in the melee state and one in ready.
    /// </summary>
    [Fact]
    public void HoldingFireWithTheKnifeKeepsSwingingAtTheClipsPace()
    {
        PlayerWeapons weapons = new([20]);
        weapons.Carry(Knife);
        Equip(weapons);

        PlayerInputState input = new();
        List<int> swings = [];
        for (int tick = 0; tick < 90; tick++)
        {
            // A 23-tick swing clip, ended on its last tick.
            bool ended = weapons.State == HumanWeaponState.Striking && swings.Count > 0 && tick - swings[^1] == 22;
            Tick(weapons, input, Frame(PlayerInputSlot.Fire), clipEnded: ended);
            if (weapons.FiredThisTick)
            {
                swings.Add(tick);
            }
        }

        Assert.True(swings.Count >= 3, $"{swings.Count} swings in 90 ticks.");
        Assert.All(swings.Zip(swings.Skip(1), static (a, b) => b - a), static gap => Assert.Equal(24, gap));
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

        // Six ticks down, six up: the swing's own clock, not the tapping, sets the pace.
        List<int> spacing = Spacing(weapons, tapEvery: 6, ticks: 200);
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

        int rounds = Spacing(weapons, tapEvery: 6, ticks: 400).Count + 1;
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
