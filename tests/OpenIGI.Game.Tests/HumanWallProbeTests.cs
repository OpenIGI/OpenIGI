using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the world-collision sweep's probe geometry and slide rules against <c>0x461A70</c>.
/// </summary>
public sealed class HumanWallProbeTests
{
    [Fact]
    public void TheProbeHeightsAreTenCentimetresAndTwoStanceDependentOnes()
    {
        (double low, double middle, double high) = HumanWallProbe.ProbeHeights(crouching: false);

        Assert.Equal(0.1, low / 4096.0, 4);
        Assert.Equal(0.4, middle / 4096.0, 4);
        Assert.Equal(1.8, high / 4096.0, 4);
    }

    [Fact]
    public void CrouchingLowersTheUpperTwoProbesButNotTheLowest()
    {
        (double standLow, double standMiddle, double standHigh) = HumanWallProbe.ProbeHeights(false);
        (double crouchLow, double crouchMiddle, double crouchHigh) = HumanWallProbe.ProbeHeights(true);

        Assert.Equal(standLow, crouchLow);
        Assert.Equal(0.2, crouchMiddle / 4096.0, 4);
        Assert.Equal(1.3, crouchHigh / 4096.0, 4);
        Assert.True(crouchMiddle < standMiddle);
        Assert.True(crouchHigh < standHigh);
    }

    [Fact]
    public void AGroundedHumanSkipsTheLowestProbe()
    {
        // The step-up mechanism. At 0.1 m the low ray catches every kerb, so while the feet are on
        // something it is not consulted at all — only the two upper probes are cast.
        Assert.Equal((1, 2), HumanWallProbe.ActiveProbes(grounded: true));
        Assert.Equal((0, 3), HumanWallProbe.ActiveProbes(grounded: false));
    }

    [Fact]
    public void OnlyNearlyVerticalSurfacesCountAsWalls()
    {
        Assert.True(HumanWallProbe.IsWall(new Vector3f(1f, 0f, 0f)));
        Assert.True(HumanWallProbe.IsWall(new Vector3f(0f, 1f, 0.05f)));

        // Flat ground and a gentle ramp are not walls — they go to the ground handling instead.
        Assert.False(HumanWallProbe.IsWall(new Vector3f(0f, 0f, 1f)));
        Assert.False(HumanWallProbe.IsWall(new Vector3f(1f, 0f, 1f)));

        // A degenerate normal is not a wall rather than a divide by zero.
        Assert.False(HumanWallProbe.IsWall(default));
    }

    [Fact]
    public void TheLimitIsOnTheNormalisedZ()
    {
        // A long normal whose Z is large in absolute terms but small once normalised is still a wall,
        // which is why the original normalises before comparing.
        Assert.True(HumanWallProbe.IsWall(new Vector3f(1000f, 0f, 50f)));
        Assert.False(HumanWallProbe.IsWall(new Vector3f(1000f, 0f, 500f)));
    }

    [Fact]
    public void FlatteningRemovesZAndRenormalises()
    {
        Vector3f flat = HumanWallProbe.Flatten(new Vector3f(0.6f, 0.8f, 0.5f));

        Assert.Equal(0f, flat.Z);
        Assert.Equal(1f, MathF.Sqrt((flat.X * flat.X) + (flat.Y * flat.Y)), 5);

        // The horizontal direction is preserved, only its length changes.
        Assert.Equal(0.6f, flat.X, 5);
        Assert.Equal(0.8f, flat.Y, 5);
    }

    [Fact]
    public void FlatteningAPurelyVerticalNormalDoesNotDivideByZero()
    {
        Vector3f flat = HumanWallProbe.Flatten(new Vector3f(0f, 0f, 1f));

        Assert.Equal(0f, flat.X);
        Assert.Equal(0f, flat.Y);
        Assert.Equal(0f, flat.Z);
    }

    [Fact]
    public void SlidingLeavesTheDestinationClearOfTheWall()
    {
        // A wall facing +X at x = 1000, with the move trying to reach x = 1500.
        Vector3d destination = new(1500, 200, 0);
        Vector3d hit = new(1000, 200, 0);
        Vector3f normal = new(1f, 0f, 0f);

        Vector3d slid = HumanWallProbe.Slide(destination, hit, normal);

        // Pushed back to the wall plus the skin width, and the along-wall components are untouched.
        Assert.Equal(1000 + HumanWallProbe.SkinInUnits, slid.X, 4);
        Assert.Equal(200, slid.Y, 4);
        Assert.Equal(0, slid.Z, 4);
    }

    [Fact]
    public void SlidingKeepsMotionAlongTheWall()
    {
        // Moving diagonally into a wall facing +X: the X component is stopped, the Y kept.
        Vector3d destination = new(1500, 900, 0);
        Vector3d hit = new(1000, 200, 0);
        Vector3f normal = new(1f, 0f, 0f);

        Vector3d slid = HumanWallProbe.Slide(destination, hit, normal);

        Assert.Equal(1000 + HumanWallProbe.SkinInUnits, slid.X, 4);
        Assert.Equal(900, slid.Y, 4);
    }

    [Fact]
    public void ATiltedWallStillSlidesHorizontally()
    {
        // The normal is flattened first, so a leaning wall never pushes a human up or down it.
        Vector3d destination = new(1500, 200, 5000);
        Vector3d hit = new(1000, 200, 5000);
        Vector3f tilted = new(1f, 0f, 0.05f);

        Vector3d slid = HumanWallProbe.Slide(destination, hit, tilted);

        Assert.Equal(5000, slid.Z, 4);
    }

    [Fact]
    public void TheSkinIsTwoAndAHalfMillimetresAndTheCapIsTen()
    {
        Assert.Equal(0.0025, HumanWallProbe.SkinInUnits / 4096.0, 6);
        Assert.Equal(10, HumanWallProbe.MaximumIterations);
    }
}
