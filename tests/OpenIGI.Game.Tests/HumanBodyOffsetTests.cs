using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Locomotion state 44's two eased offsets — <c>0x40DFC0</c>, and what general arguments 4, 5 and 6
/// are actually for.
/// </summary>
public sealed class HumanBodyOffsetTests
{
    private const float Range = 3481.6f;
    private const int Ticks = 7;

    [Fact]
    public void AnOffsetReachesItsRangeInExactlyTheScriptedTicks()
    {
        // This is the whole point of arguments 4 and 6 together: the range and the time to cover it.
        // Retail is 0.85 m in 7 ticks, so a little under a quarter of a second.
        float rate = HumanBodyOffset.Rate(Range, Ticks);
        float offset = 0f;

        for (int i = 0; i < Ticks; i++)
        {
            offset = HumanBodyOffset.Ease(offset, Range, rate);
        }

        Assert.Equal(Range, offset, 1e-2f);
    }

    [Fact]
    public void TheEaseLandsExactlyOnItsTargetRatherThanNear()
    {
        // The original moves and then tests whether it overshot, snapping if so. So the offset settles
        // on the target precisely and stops - it does not creep or oscillate around it.
        float rate = HumanBodyOffset.Rate(Range, Ticks);
        float offset = 0f;

        for (int i = 0; i < 50; i++)
        {
            offset = HumanBodyOffset.Ease(offset, Range, rate);
        }

        Assert.Equal(Range, offset);
        Assert.Equal(Range, HumanBodyOffset.Ease(offset, Range, rate));
    }

    [Fact]
    public void AnOffsetComesBackToZeroTheSameWay()
    {
        float rate = HumanBodyOffset.Rate(Range, Ticks);
        float offset = Range;

        for (int i = 0; i < Ticks; i++)
        {
            offset = HumanBodyOffset.Ease(offset, 0f, rate);
        }

        Assert.Equal(0f, offset, 1e-2f);
    }

    [Fact]
    public void HoldingBothStrafeKeysCancelsRatherThanPickingOne()
    {
        Assert.Equal(-Range, HumanBodyOffset.SidewaysTarget(true, false, Range));
        Assert.Equal(Range, HumanBodyOffset.SidewaysTarget(false, true, Range));
        Assert.Equal(0f, HumanBodyOffset.SidewaysTarget(true, true, Range));
        Assert.Equal(0f, HumanBodyOffset.SidewaysTarget(false, false, Range));
    }

    [Fact]
    public void TheForwardOffsetOnlyExistsWhileCrouched()
    {
        // The whole second block sits behind the crouch byte at Human+829.
        Assert.Equal(Range, HumanBodyOffset.ForwardTarget(forward: true, crouching: true, Range));
        Assert.Equal(0f, HumanBodyOffset.ForwardTarget(forward: true, crouching: false, Range));
        Assert.Equal(0f, HumanBodyOffset.ForwardTarget(forward: false, crouching: true, Range));
    }

    [RequiresGameDataFact]
    public void StandingLeavesTheForwardOffsetWhereItWasInsteadOfEasingItBack()
    {
        // A consequence of that gate rather than a separate rule, and worth pinning because it is the
        // kind of thing a tidier implementation would "fix": stand up mid-lean and the forward offset
        // stops where it is. Only the sideways one keeps moving.
        HumanPlayerParameters parameters = RetailPlayerParameters.Value;

        HumanBodyOffsets offset = new(Sideways: 100f, Forward: 500f);
        HumanBodyOffsets after = HumanBodyOffset.Advance(
            offset, left: false, right: false, forward: true, crouching: false, parameters);

        Assert.Equal(500f, after.Forward);
        Assert.True(after.Sideways < 100f, "the sideways offset should still be easing back to zero");
    }

    [RequiresGameDataFact]
    public void TheRetailRangesAndTimeAreTheScriptsOwn()
    {
        HumanPlayerParameters parameters = RetailPlayerParameters.Value;

        // 0.85 m each way, and 0.25 s truncated to 7 ticks rather than rounded to 8.
        Assert.Equal(3481.6f, parameters.Distance4, 2);
        Assert.Equal(3481.6f, parameters.Distance5, 2);
        Assert.Equal(7, parameters.Duration6Ticks);

        // Which makes the sideways lean about 12 cm a tick.
        Assert.Equal(0.121, HumanBodyOffset.Rate(parameters.Distance4, parameters.Duration6Ticks) / 4096.0, 0.002);
    }
}
