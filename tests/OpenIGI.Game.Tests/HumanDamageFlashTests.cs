using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The red damage wash: a hit raises the four corners, and each tick fades them a forty-fifth toward
/// zero — the decay <c>sub_411300</c> runs, so a full flash lives forty-five ticks.
/// </summary>
public sealed class HumanDamageFlashTests
{
    /// <summary>A fresh flash is dark and paints nothing.</summary>
    [Fact]
    public void StartsInactive()
    {
        HumanDamageFlash flash = new();
        Assert.False(flash.IsActive);
        Assert.Equal(0f, flash.TopLeft);
    }

    /// <summary>A hit raises every corner to full and makes the flash paint.</summary>
    [Fact]
    public void AHitRaisesEveryCorner()
    {
        HumanDamageFlash flash = new();
        flash.Hit();

        Assert.True(flash.IsActive);
        Assert.Equal(1f, flash.TopLeft);
        Assert.Equal(1f, flash.TopRight);
        Assert.Equal(1f, flash.BottomRight);
        Assert.Equal(1f, flash.BottomLeft);
    }

    /// <summary>Each tick subtracts one forty-fifth, and a full flash is gone after forty-five ticks.</summary>
    [Fact]
    public void FadesAFortyFifthATickAndDiesAtFortyFive()
    {
        HumanDamageFlash flash = new();
        flash.Hit();

        flash.Tick();
        Assert.Equal(1f - HumanDamageFlash.FadePerTick, flash.TopLeft, 5);

        // Forty-four more brings it to forty-five ticks in all -- essentially faded, to within a tick's
        // worth (repeated float subtraction leaves a hair, as the original's does).
        for (int i = 0; i < 44; i++)
        {
            flash.Tick();
        }

        Assert.True(flash.TopLeft < HumanDamageFlash.FadePerTick);

        // One more crosses zero and the clamp pins it dark.
        flash.Tick();
        Assert.False(flash.IsActive);
        Assert.Equal(0f, flash.TopLeft);
    }

    /// <summary>A second hit mid-fade tops the wash back up rather than dimming it.</summary>
    [Fact]
    public void AHitNeverDimsWhatIsAlreadyLit()
    {
        HumanDamageFlash flash = new();
        flash.Hit();
        for (int i = 0; i < 10; i++)
        {
            flash.Tick();
        }

        Assert.True(flash.TopLeft < 1f);
        flash.Hit(0.5f);

        // 0.5 is below the ~0.78 still lit, so it must not pull the corner down.
        Assert.True(flash.TopLeft > 0.5f);

        flash.Hit();
        Assert.Equal(1f, flash.TopLeft);
    }

    /// <summary>Intensity is clamped into range.</summary>
    [Fact]
    public void IntensityIsClamped()
    {
        HumanDamageFlash flash = new();
        flash.Hit(5f);
        Assert.Equal(1f, flash.TopLeft);
    }
}
