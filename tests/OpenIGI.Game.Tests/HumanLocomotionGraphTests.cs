using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks the locomotion transition graph's structure.
/// </summary>
/// <remarks>
/// The graph was extracted mechanically from all 49 per-tick functions, so these are the invariants
/// that say the extraction found the real structure rather than a plausible-looking one. See
/// the original.
/// </remarks>
public sealed class HumanLocomotionGraphTests
{
    [Fact]
    public void EveryTargetIsARealState()
    {
        for (int state = 0; state < HumanLocomotionStates.Count; state++)
        {
            Assert.All(
                HumanLocomotionGraph.From(state),
                t => Assert.InRange(t, 0, HumanLocomotionStates.Count - 1));
        }
    }

    [Fact]
    public void EveryMovementStateChecksTheGroundFirstThenTheTwoDeaths()
    {
        // Every state in 0..27 opens by testing the ground. That uniformity is the strongest sign the
        // extraction is reading real branch order and not incidental immediates.
        int withDeaths = 0;
        for (int state = 0; state < HumanLocomotionGraph.MovementStateCount; state++)
        {
            IReadOnlyList<int> targets = HumanLocomotionGraph.From(state);
            Assert.Equal(HumanLocomotionGraph.Falling, targets[0]);

            // The two death tests follow for all of them except 17 and 18, which are two-branch
            // states that only check the ground and then leave.
            if (targets.Count > 2)
            {
                Assert.Equal(46, targets[1]);
                Assert.Equal(47, targets[2]);
                withDeaths++;
            }
            else
            {
                Assert.True(state is 17 or 18, $"state {state} has only {targets.Count} targets");
            }
        }

        // The loop covers states 0..27 - twenty-eight of them - and 17 and 18 are the only two that
        // stop after the ground test, so twenty-six carry the death pair. (The jump is state 28 and
        // therefore outside this range entirely; its own test covers it.)
        Assert.Equal(26, withDeaths);
    }

    [Fact]
    public void TheJumpStateAloneSkipsTheGroundAndDeathTests()
    {
        // State 28 goes straight to the eight movement states: you land into a motion, and neither
        // losing the ground nor dying is tested while already airborne.
        IReadOnlyList<int> targets = HumanLocomotionGraph.From(HumanLocomotionGraph.Jump);

        Assert.Equal([9, 12, 15, 16, 1, 4, 7, 8], targets);
        Assert.DoesNotContain(HumanLocomotionGraph.Falling, targets);
        Assert.DoesNotContain(46, targets);
    }

    [Fact]
    public void TheRunWalkToggleIsAlwaysTheFourthTest()
    {
        // States 1..16 are eight directions at run and walk speeds. Each one's fourth target is exactly
        // its counterpart, which establishes the pairing rather than assuming it from the numbering.
        for (int state = 1; state <= 16; state++)
        {
            Assert.Equal(
                HumanLocomotionGraph.RunWalkCounterpart(state),
                HumanLocomotionGraph.From(state)[3]);
        }
    }

    [Fact]
    public void RunWalkPairingIsSymmetric()
    {
        for (int state = 1; state <= 16; state++)
        {
            Assert.Equal(
                state,
                HumanLocomotionGraph.RunWalkCounterpart(HumanLocomotionGraph.RunWalkCounterpart(state)));
        }
    }

    [Fact]
    public void EveryRunOrWalkMotionCanJump()
    {
        // 1..16 all reach the jump state, always as the fifth test — immediately after the run/walk
        // toggle.
        for (int state = 1; state <= 16; state++)
        {
            Assert.Equal(HumanLocomotionGraph.Jump, HumanLocomotionGraph.From(state)[4]);
        }
    }

    [Fact]
    public void IdleTestsMoreThanAnyOtherState()
    {
        // State 0 has the longest chain: it is the only one that can reach all eight motions plus
        // both run and walk sets directly, because every other motion state is already committed to a
        // direction.
        int idle = HumanLocomotionGraph.From(0).Count;

        for (int state = 1; state < HumanLocomotionGraph.MovementStateCount; state++)
        {
            Assert.True(
                HumanLocomotionGraph.From(state).Count < idle,
                $"state {state} has {HumanLocomotionGraph.From(state).Count} targets against idle's {idle}");
        }
    }

    [Fact]
    public void TheTerminalStatesTransitionNowhereByAnImmediate()
    {
        // Falling, and a handful of others, compute their target rather than naming it - the landing
        // dispatcher cannot know where it is going until it knows what it landed on. An empty list
        // means "not extractable", not "cannot leave".
        foreach (int state in new[] { 29, 40, 42, 43, 44, 47 })
        {
            Assert.Empty(HumanLocomotionGraph.From(state));
        }
    }

    [Fact]
    public void TheInteractiveStatesAllReturnToIdle()
    {
        // 30..36 are the self-driving set from the animation table. Each one leaves straight back to
        // idle, except 34 which dispatches into the others.
        foreach (int state in new[] { 30, 31, 32, 33, 35, 36 })
        {
            Assert.Equal([0], HumanLocomotionGraph.From(state));
            Assert.True(HumanLocomotionStates.DrivesItself(state));
        }

        // 34 dispatches into the whole set, itself included - and nothing in the movement graph
        // reaches 34, so the set is entered from outside.
        Assert.Equal([30, 31, 32, 33, 34, 35, 36], HumanLocomotionGraph.From(34));
        Assert.False(HumanLocomotionGraph.IsReachableFromIdle(34));
    }

    [Fact]
    public void EveryStateIsReachableFromIdleExceptTheOnesDrivenFromOutside()
    {
        // A state nothing transitions to is entered some other way - by a script, a death message, or
        // the developer chord. Listing them is what makes that claim checkable.
        List<int> unreachable = [];
        for (int state = 0; state < HumanLocomotionStates.Count; state++)
        {
            if (!HumanLocomotionGraph.IsReachableFromIdle(state))
            {
                unreachable.Add(state);
            }
        }

        // Nineteen of the forty-nine, and they group meaningfully: the interactive set 30..36, the
        // climb/slide set 37..40, and a scattering of one-offs including the developer state.
        Assert.Equal(
            [17, 18, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 48],
            unreachable);

        // The developer fly state is among them, which fits: nothing transitions to it, the
        // Ctrl+Shift+F chord pushes it directly.
        Assert.Contains(HumanLocomotionStates.DeveloperState, unreachable);
    }

    [Fact]
    public void TheDeveloperStateReturnsToTheOrdinaryMotions()
    {
        IReadOnlyList<int> targets = HumanLocomotionGraph.From(HumanLocomotionStates.DeveloperState);

        Assert.NotEmpty(targets);
        Assert.All(targets, t => Assert.InRange(t, 0, 16));
    }

    [Fact]
    public void RunWalkCounterpartRejectsStatesOutsideThePairedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HumanLocomotionGraph.RunWalkCounterpart(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HumanLocomotionGraph.RunWalkCounterpart(17));
    }
}
