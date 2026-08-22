using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The eight directions a human walks in, and the states that carry them.
/// </summary>
/// <remarks>
/// <para>
/// This file exists because of a wrong reading. State zero's per-tick tests the four movement bits in
/// order and takes the first that matches, which looked like the whole story — so holding forward and
/// right resolved to state 1 and the player walked straight ahead instead of diagonally.
/// </para>
/// <para>
/// It only looks like the whole story from a standstill, where you can only start moving one way. The
/// diagonals live in the movement states' own per-ticks: state 1's (<c>0x408C80</c>) tests forward and
/// right together and moves to state 2. Selecting from the flags directly reaches the same state a tick
/// sooner, which is what <see cref="HumanLocomotionStates.ForDirection"/> does.
/// </para>
/// </remarks>
public sealed class HumanDirectionStateTests
{
    private const double UnitsPerMetre = 4096.0;
    private const HumanMotionState Ground = HumanMotionState.OnGround;

    [Fact]
    public void TheEightDirectionsMapToTheEightStates()
    {
        Assert.Equal(1, Select(HumanMotionState.MoveForward));
        Assert.Equal(2, Select(HumanMotionState.MoveForward | HumanMotionState.MoveRight));
        Assert.Equal(3, Select(HumanMotionState.MoveForward | HumanMotionState.MoveLeft));
        Assert.Equal(4, Select(HumanMotionState.MoveBackward));
        Assert.Equal(5, Select(HumanMotionState.MoveBackward | HumanMotionState.MoveRight));
        Assert.Equal(6, Select(HumanMotionState.MoveBackward | HumanMotionState.MoveLeft));
        Assert.Equal(7, Select(HumanMotionState.MoveRight));
        Assert.Equal(8, Select(HumanMotionState.MoveLeft));
    }

    [Fact]
    public void WalkingIsTheSameEightDirectionsEightStatesHigher()
    {
        for (HumanMotionState held = 0; (int)held < 16; held++)
        {
            HumanMotionState flags = Movement((int)held);
            int standing = Select(flags);

            if (standing == HumanLocomotionStates.Idle)
            {
                continue;
            }

            Assert.Equal(standing + 8, Select(flags | HumanMotionState.Walking));
        }
    }

    [Fact]
    public void HoldingNothingIsIdleAndBeingAirborneIsFalling()
    {
        Assert.Equal(HumanLocomotionStates.Idle, Select(0));
        Assert.Equal(HumanLocomotionStates.Idle, Select(HumanMotionState.Walking));

        // Airborne wins over everything: the movement bits are still read while falling, but by the
        // airborne integrator's air control rather than by the state machine.
        Assert.Equal(
            HumanLocomotionStates.Falling,
            HumanLocomotionStates.ForDirection(HumanMotionState.MoveForward | HumanMotionState.MoveRight));
    }

    [Fact]
    public void OpposingKeysCancelRatherThanPickingOne()
    {
        // Holding both forward and back is not "forward wins" - neither bit can be set at once by the
        // input mapping, but if both arrive the pair must not resolve to a diagonal.
        Assert.Equal(1, Select(HumanMotionState.MoveForward | HumanMotionState.MoveRight | HumanMotionState.MoveLeft));
        Assert.Equal(HumanLocomotionStates.Idle, Select(HumanMotionState.MoveRight | HumanMotionState.MoveLeft));
    }

    [RequiresGameDataFact]
    public void TheDiagonalStatesActuallyTravelDiagonally()
    {
        // The mapping above was read off the animations, not assumed, and this is that reading. Each
        // diagonal state's clip must cover comparable ground on both axes with the signs its name says.
        BoneAnimationSet set = PlayerAnimations();

        AssertTravels(set, 2, +1, +1);
        AssertTravels(set, 3, -1, +1);
        AssertTravels(set, 5, +1, -1);
        AssertTravels(set, 6, -1, -1);

        // And the crouched four, eight higher.
        AssertTravels(set, 10, +1, +1);
        AssertTravels(set, 11, -1, +1);
        AssertTravels(set, 13, +1, -1);
        AssertTravels(set, 14, -1, -1);
    }

    [RequiresGameDataFact]
    public void ADiagonalIsSlightlySlowerThanRunningStraightRatherThanFaster()
    {
        // The check that the diagonal states are real animations rather than the cardinals summed. If
        // walking diagonally were two cardinal steps added it would come out sqrt(2) - about 41% -
        // faster, which is the "diagonal strafing" bug of the era. It does not.
        //
        // Measured against the retail set, a diagonal covers 0.82 of what running forward does. So the
        // original has a diagonal *penalty*, and it comes from nothing but the animator having authored
        // a shorter stride for those clips. Nothing in code enforces it.
        BoneAnimationSet set = PlayerAnimations();

        double ratio = Speed(set, 2) / Speed(set, 1);

        Assert.Equal(0.82, ratio, 0.02);
        Assert.True(ratio < 1.0 / Math.Sqrt(2.0) * 1.25, "a diagonal must not carry the sqrt(2) bonus");
    }

    private static void AssertTravels(BoneAnimationSet set, int state, int signX, int signY)
    {
        Vector3f travel = Travel(set, state);

        Assert.True(
            Math.Sign(travel.X) == signX,
            $"state {state} travelled {travel.X:0} on X, expected the sign {signX}");
        Assert.True(
            Math.Sign(travel.Y) == signY,
            $"state {state} travelled {travel.Y:0} on Y, expected the sign {signY}");

        // Both axes carry real distance - it is a diagonal, not a cardinal with drift.
        Assert.True(Math.Abs(travel.X) > 0.25 * UnitsPerMetre, $"state {state} barely moved on X");
        Assert.True(Math.Abs(travel.Y) > 0.25 * UnitsPerMetre, $"state {state} barely moved on Y");

        double ratio = Math.Abs(travel.X) / Math.Abs(travel.Y);
        Assert.InRange(ratio, 0.5, 2.0);
    }

    private static int Select(HumanMotionState movement) =>
        HumanLocomotionStates.ForDirection(Ground | movement);

    private static HumanMotionState Movement(int held)
    {
        HumanMotionState flags = Ground;

        if ((held & 1) != 0)
        {
            flags |= HumanMotionState.MoveForward;
        }

        if ((held & 2) != 0)
        {
            flags |= HumanMotionState.MoveBackward;
        }

        if ((held & 4) != 0)
        {
            flags |= HumanMotionState.MoveRight;
        }

        if ((held & 8) != 0)
        {
            flags |= HumanMotionState.MoveLeft;
        }

        return flags;
    }

    private static Vector3f Travel(BoneAnimationSet set, int state)
    {
        HumanLocomotionAnimator animator = new(set);
        animator.Enter(state);

        if (!set.TryGet(animator.AnimationId, out BoneAnimation clip))
        {
            Assert.Fail($"state {state} names animation {animator.AnimationId}, which the set does not have");
            return default;
        }

        int ticks = Math.Max(1, (int)Math.Round(clip.Duration / HumanLocomotionAnimator.TimePerTick));
        Vector3f total = default;
        for (int i = 0; i < ticks; i++)
        {
            Vector3f step = animator.Advance();
            total = new Vector3f(total.X + step.X, total.Y + step.Y, total.Z + step.Z);
        }

        return total;
    }

    private static double Speed(BoneAnimationSet set, int state)
    {
        HumanLocomotionAnimator animator = new(set);
        animator.Enter(state);
        Assert.True(set.TryGet(animator.AnimationId, out BoneAnimation clip));

        int ticks = Math.Max(1, (int)Math.Round(clip.Duration / HumanLocomotionAnimator.TimePerTick));
        Vector3f travel = Travel(set, state);

        return Math.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y)) / ticks;
    }

    private static BoneAnimationSet PlayerAnimations() =>
        BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));
}
