using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The red damage wash: retail chooses a 0.4–0.8 alpha and weights its corners from the round's view
/// direction; each tick then fades them a forty-fifth toward zero.
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

    /// <summary>A directionless hit, such as an explosion, gives every corner the same retail intensity.</summary>
    [Fact]
    public void AZeroDirectionHitRaisesEveryCornerEqually()
    {
        HumanDamageFlash flash = new();
        flash.Hit(Vector3f.Zero, randomUnit: 0.5f);

        Assert.True(flash.IsActive);
        Assert.Equal(0.6f, flash.TopLeft, 5);
        Assert.Equal(0.6f, flash.TopRight, 5);
        Assert.Equal(0.6f, flash.BottomRight, 5);
        Assert.Equal(0.6f, flash.BottomLeft, 5);
    }

    /// <summary>Each tick subtracts one forty-fifth, and the next tick clamps the tiny residual after thirty-six subtractions.</summary>
    [Fact]
    public void FadesAFortyFifthATickAndDiesAtThirtySeven()
    {
        HumanDamageFlash flash = new();
        flash.Hit(Vector3f.Zero, randomUnit: 1f);

        flash.Tick();
        Assert.Equal(0.8f - HumanDamageFlash.FadePerTick, flash.TopLeft, 5);

        for (int i = 0; i < 35; i++)
        {
            flash.Tick();
        }

        Assert.True(flash.TopLeft < HumanDamageFlash.FadePerTick);

        flash.Tick();
        Assert.False(flash.IsActive);
        Assert.Equal(0f, flash.TopLeft);
    }

    /// <summary>The dominant X direction lights its matching edge and dims the other one by half.</summary>
    [Fact]
    public void AHorizontalHitWeightsTheMatchingEdge()
    {
        HumanDamageFlash flash = new();
        flash.Hit(new Vector3f(1f, 0f, 0f), randomUnit: 1f);

        Assert.Equal(0.8f, flash.TopLeft, 5);
        Assert.Equal(0.8f, flash.TopRight, 5);
        Assert.Equal(0.4f, flash.BottomRight, 5);
        Assert.Equal(0.4f, flash.BottomLeft, 5);
    }

    /// <summary>A dominant negative Z direction lights the retail mapper's left edge.</summary>
    [Fact]
    public void ANegativeZHitWeightsTheLeftEdge()
    {
        HumanDamageFlash flash = new();
        flash.Hit(new Vector3f(1f, 0f, -2f), randomUnit: 1f);

        Assert.Equal(0.8f, flash.TopLeft, 5);
        Assert.Equal(0.4f, flash.TopRight, 5);
        Assert.Equal(0.4f, flash.BottomRight, 5);
        Assert.Equal(0.8f, flash.BottomLeft, 5);
    }

    /// <summary>A dominant positive Y direction chooses the same right edge as the retail branch.</summary>
    [Fact]
    public void APositiveYHitWeightsTheRightEdge()
    {
        HumanDamageFlash flash = new();
        flash.Hit(new Vector3f(0f, 2f, 0f), randomUnit: 1f);

        Assert.Equal(0.4f, flash.TopLeft, 5);
        Assert.Equal(0.8f, flash.TopRight, 5);
        Assert.Equal(0.8f, flash.BottomRight, 5);
        Assert.Equal(0.4f, flash.BottomLeft, 5);
    }

    /// <summary>A second hit overwrites its predecessor, including when the new flash is dimmer.</summary>
    [Fact]
    public void AHitReplacesRatherThanAccumulates()
    {
        HumanDamageFlash flash = new();
        flash.Hit(new Vector3f(1f, 0f, 0f), randomUnit: 1f);
        flash.Hit(new Vector3f(-1f, 0f, 0f), randomUnit: 0f);

        Assert.Equal(0.2f, flash.TopLeft, 5);
        Assert.Equal(0.2f, flash.TopRight, 5);
        Assert.Equal(0.4f, flash.BottomRight, 5);
        Assert.Equal(0.4f, flash.BottomLeft, 5);
    }

    /// <summary>The random input is clamped before mapping to the retail alpha range.</summary>
    [Fact]
    public void RetailIntensityIsClamped()
    {
        Assert.Equal(HumanDamageFlash.MinimumHitIntensity, HumanDamageFlash.RetailIntensity(-1f));
        Assert.Equal(0.8f, HumanDamageFlash.RetailIntensity(5f));
    }
}
