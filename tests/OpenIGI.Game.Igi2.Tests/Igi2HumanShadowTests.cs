using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A body's mark (<c>shadow.c</c>) and the pool of blood it becomes once the body has come to rest.</summary>
public sealed class Igi2HumanShadowTests
{
    [Fact]
    public void A_pool_spreads_from_nothing_over_five_seconds()
    {
        Igi2HumanShadow shadow = new();
        Assert.False(shadow.IsEnabled);
        shadow.Place(new Vector3d(1.0, 2.0, 3.0), new Vector3f(0f, 0f, 2f), 0f);
        Assert.Equal(new Vector3f(0f, 0f, 1f), shadow.Normal);

        shadow.BecomePool(0.5f, spread: true);
        Assert.True(shadow.IsEnabled);
        Assert.Equal(Igi2HumanShadow.PoolTexture, shadow.Texture);
        Assert.Equal(0f, shadow.Size);

        int ticks = 0;
        while (shadow.Size < Igi2HumanShadow.Radius)
        {
            shadow.Run();
            ticks++;
        }

        Assert.InRange(ticks, 150, 151);
    }

    [Fact]
    public void A_mark_fades_with_height_but_never_below_three_tenths()
    {
        Igi2HumanShadow shadow = new();
        shadow.Place(default, new Vector3f(0f, 0f, 1f), 0f);
        Assert.Equal(0.8f, shadow.Strength, 5);
        shadow.Place(default, new Vector3f(0f, 0f, 1f), Igi2HumanShadow.MaxDistance);
        Assert.Equal(0.3f, shadow.Strength, 5);
    }
}
