using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A body's mark (<c>shadow.c</c>) and the pool of blood it becomes once the body has come to rest.</summary>
public sealed class HumanShadowTests
{
    [Fact]
    public void A_pool_spreads_from_nothing_over_five_seconds()
    {
        HumanShadow shadow = new(HumanShadow.Igi2MaxDistance);
        Assert.False(shadow.IsEnabled);
        shadow.Place(new Vector3d(1.0, 2.0, 3.0), new Vector3f(0f, 0f, 2f), 0f);
        Assert.Equal(new Vector3f(0f, 0f, 1f), shadow.Normal);

        shadow.BecomePool(0.5f, spread: true);
        Assert.True(shadow.IsEnabled);
        Assert.Equal(HumanShadow.PoolTexture, shadow.Texture);
        Assert.Equal(0f, shadow.Size);

        int ticks = 0;
        while (shadow.Size < HumanShadow.Radius)
        {
            shadow.Run();
            ticks++;
        }

        Assert.InRange(ticks, 150, 151);
    }

    [Fact]
    public void A_mark_fades_with_height_but_never_below_three_tenths()
    {
        HumanShadow shadow = new(HumanShadow.Igi2MaxDistance);
        shadow.Place(default, new Vector3f(0f, 0f, 1f), 0f);
        Assert.Equal(0.8f, shadow.Strength, 5);
        shadow.Place(default, new Vector3f(0f, 0f, 1f), HumanShadow.Igi2MaxDistance);
        Assert.Equal(0.3f, shadow.Strength, 5);
    }

    [Fact]
    public void Project_IGIs_mark_fades_over_a_kilometre_so_a_standing_body_keeps_four_fifths()
    {
        // Human_Create hands Shadow_New 4,096,000 units (0x4E0CC0): a body's origin 3877.2 above the ground barely fades it.
        HumanShadow shadow = new(HumanShadow.Igi1MaxDistance);
        shadow.Place(default, new Vector3f(0f, 0f, 1f), 3877.2f);
        Assert.Equal(0.8f * (1f - (3877.2f / 4096000f)), shadow.Strength, 5);
        shadow.Place(default, new Vector3f(0f, 0f, 1f), HumanShadow.Igi1MaxDistance);
        Assert.Equal(0.3f, shadow.Strength, 5);
    }
}
