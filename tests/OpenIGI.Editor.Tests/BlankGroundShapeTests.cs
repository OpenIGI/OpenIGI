using OpenIGI.Editor.Core.Missions;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A blank mission's ground: flat, or rolling from a seed, level under the start and the exit, easing in at an edge.</summary>
public sealed class BlankGroundShapeTests
{
    private static readonly (double X, double Y)[] Pads = [(0, 0), (50, 0)];

    /// <summary>A seed always gives the same ground, and another seed other ground.</summary>
    [Fact]
    public void A_seed_always_gives_the_same_ground()
    {
        double a = BlankGroundShape.Height(BlankGround.Seeded, 1234, 300, -200, 10, 10, Pads);
        double b = BlankGroundShape.Height(BlankGround.Seeded, 1234, 300, -200, 10, 10, Pads);
        double other = BlankGroundShape.Height(BlankGround.Seeded, 99, 300, -200, 10, 10, Pads);
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
    }

    /// <summary>Flat ground is the start's height everywhere; kept ground is what was there.</summary>
    [Fact]
    public void Flat_is_level_and_keep_keeps()
    {
        Assert.Equal(10, BlankGroundShape.Height(BlankGround.Flat, 0, 400, 400, 37, 10, Pads));
        Assert.Equal(37, BlankGroundShape.Height(BlankGround.Keep, 0, 400, 400, 37, 10, Pads));
    }

    /// <summary>Seeded ground is level under the player start and the exit, and within its amplitude everywhere.</summary>
    [Fact]
    public void Seeded_ground_is_level_under_the_pads_and_bounded()
    {
        Assert.Equal(10, BlankGroundShape.Height(BlankGround.Seeded, 7, 0, 0, 0, 10, Pads), 6);
        Assert.Equal(10, BlankGroundShape.Height(BlankGround.Seeded, 7, 50, 5, 0, 10, Pads), 6);
        double low = double.MaxValue, high = double.MinValue;
        for (int x = -1000; x <= 1000; x += 25)
        {
            for (int y = -1000; y <= 1000; y += 25)
            {
                double z = BlankGroundShape.Height(BlankGround.Seeded, 7, x, y, 0, 10, Pads);
                low = Math.Min(low, z);
                high = Math.Max(high, z);
            }
        }

        Assert.InRange(low, 10 - BlankGroundShape.AmplitudeMetres, 10);
        Assert.InRange(high, 10, 10 + BlankGroundShape.AmplitudeMetres);
        Assert.True(high - low > 10, $"the ground hardly rolls: {low:0.0} to {high:0.0} m");
    }

    /// <summary>At a region's edge the ground is what was there, so a play area meets the level without a cliff.</summary>
    [Fact]
    public void A_region_eases_into_the_ground_at_its_edge()
    {
        Assert.Equal(37, BlankGroundShape.Height(BlankGround.Flat, 0, 400, 400, 37, 10, Pads, edgeDistance: 0));
        double halfway = BlankGroundShape.Height(BlankGround.Flat, 0, 400, 400, 37, 10, Pads, edgeDistance: BlankGroundShape.EdgeMetres / 2);
        Assert.InRange(halfway, 10.001, 36.999);
        Assert.Equal(10, BlankGroundShape.Height(BlankGround.Flat, 0, 400, 400, 37, 10, Pads, edgeDistance: BlankGroundShape.EdgeMetres));
    }
}
