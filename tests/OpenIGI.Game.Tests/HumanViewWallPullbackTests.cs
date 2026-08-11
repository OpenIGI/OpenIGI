using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Locks the first-person wall-clearance arithmetic to the values in retail <c>0x464BD0</c>.</summary>
public sealed class HumanViewWallPullbackTests
{
    [Fact]
    public void AHitPullsTheRigBackByTheRemainingHalfMetre()
    {
        Assert.Equal(-1024.0, HumanViewWallPullback.TargetForHit(1024.0));
        Assert.Equal(-2048.0, HumanViewWallPullback.TargetForHit(0.0));
    }

    [Fact]
    public void ItRetreatsFasterThanItReturns()
    {
        double retreat = HumanViewWallPullback.Advance(0.0, -2048.0);
        double returnToReady = HumanViewWallPullback.Advance(-2048.0, 0.0);

        Assert.Equal(-113.77778, retreat, 5);
        Assert.Equal(-1972.148148, returnToReady, 5);
        Assert.True(Math.Abs(retreat) > Math.Abs(returnToReady + 2048.0));
    }

    [Fact]
    public void EachRateClampsAtItsTargetWithoutOvershooting()
    {
        Assert.Equal(-2048.0, HumanViewWallPullback.Advance(-2000.0, -2048.0));
        Assert.Equal(0.0, HumanViewWallPullback.Advance(-50.0, 0.0));
    }
}
