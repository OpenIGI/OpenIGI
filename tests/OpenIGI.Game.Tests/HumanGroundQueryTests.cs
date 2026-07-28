using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the ground query's step-down budget and probe geometry against <c>0x462390</c>.
/// </summary>
public sealed class HumanGroundQueryTests
{
    [Fact]
    public void TheStepDownBudgetHasThreeValuesAndSlidingWins()
    {
        Assert.Equal(0.0, HumanGroundQuery.StepDownBudget(grounded: false, sliding: false));
        Assert.Equal(2048.0, HumanGroundQuery.StepDownBudget(grounded: true, sliding: false));
        Assert.Equal(8192.0, HumanGroundQuery.StepDownBudget(grounded: true, sliding: true));

        // Sliding is tested after grounded in the original, so it overrides even when airborne.
        Assert.Equal(8192.0, HumanGroundQuery.StepDownBudget(grounded: false, sliding: true));
    }

    [Fact]
    public void TheBudgetsAreNothingHalfAMetreAndTwoMetres()
    {
        Assert.Equal(0.0, HumanGroundQuery.AirborneStepDownInUnits / 4096.0, 4);
        Assert.Equal(0.5, HumanGroundQuery.GroundedStepDownInUnits / 4096.0, 4);
        Assert.Equal(2.0, HumanGroundQuery.SlidingStepDownInUnits / 4096.0, 4);

        // The sliding leash is four times the walking one — which is what keeps a human glued to a
        // steep slope instead of bouncing down it in a series of little falls.
        Assert.Equal(
            4.0,
            HumanGroundQuery.SlidingStepDownInUnits / HumanGroundQuery.GroundedStepDownInUnits,
            6);
    }

    [Fact]
    public void FallingNeverSnapsToGround()
    {
        double budget = HumanGroundQuery.StepDownBudget(grounded: false, sliding: false);

        // Any gap at all leaves an airborne human airborne.
        Assert.False(HumanGroundQuery.IsStandingOn(1.0, budget));

        // Touching, or already below the surface, still counts.
        Assert.True(HumanGroundQuery.IsStandingOn(0.0, budget));
        Assert.True(HumanGroundQuery.IsStandingOn(-100.0, budget));
    }

    [Fact]
    public void WalkingOverAKerbKeepsTheFeetPlanted()
    {
        double budget = HumanGroundQuery.StepDownBudget(grounded: true, sliding: false);

        // A 0.3 m drop is inside the budget: the human is snapped down rather than starting a fall.
        Assert.True(HumanGroundQuery.IsStandingOn(0.3 * 4096.0, budget));

        // A 0.7 m drop is not.
        Assert.False(HumanGroundQuery.IsStandingOn(0.7 * 4096.0, budget));
    }

    [Fact]
    public void ADropExactlyOnTheBudgetStillCounts()
    {
        // The comparison is <=, not <.
        double budget = HumanGroundQuery.StepDownBudget(grounded: true, sliding: false);
        Assert.True(HumanGroundQuery.IsStandingOn(budget, budget));
    }

    [Fact]
    public void TheProbeStartsWhereTheWallSweepsMiddleRayDoes()
    {
        // Both passes use the same two heights, which is what keeps them agreeing about where a
        // human's legs are.
        (_, double middleStanding, _) = HumanWallProbe.ProbeHeights(crouching: false);
        (_, double middleCrouching, _) = HumanWallProbe.ProbeHeights(crouching: true);

        Assert.Equal(middleStanding, HumanGroundQuery.ProbeOrigin(crouching: false));
        Assert.Equal(middleCrouching, HumanGroundQuery.ProbeOrigin(crouching: true));
    }

    [Fact]
    public void TheRoofFlagUsesTheStandingProbeHeight()
    {
        (_, _, double high) = HumanWallProbe.ProbeHeights(crouching: false);
        Assert.Equal(HumanGroundQuery.RoofClearanceInUnits, high);

        Assert.True(HumanGroundQuery.IsUnderRoof(1.5 * 4096.0));
        Assert.False(HumanGroundQuery.IsUnderRoof(2.0 * 4096.0));

        // Exactly 1.8 m is not "under" — the comparison is strict.
        Assert.False(HumanGroundQuery.IsUnderRoof(HumanGroundQuery.RoofClearanceInUnits));
    }

    [Fact]
    public void TheMaterialProbeIsAShortStabAroundTheGroundPoint()
    {
        Assert.Equal(0.05, HumanGroundQuery.MaterialProbeAboveInUnits / 4096.0, 4);
        Assert.Equal(0.5, HumanGroundQuery.MaterialProbeDepthInUnits / 4096.0, 4);
    }

    [Fact]
    public void TheGroundAngleScaleIsTwoOverPi() =>
        // Six places, not seven: the constant is a float, and 2/pi rounded to single precision
        // (0.63661975) already differs from the double in the eighth digit.
        Assert.Equal(2.0 / Math.PI, HumanGroundQuery.GroundAngleScale, 6);
}
