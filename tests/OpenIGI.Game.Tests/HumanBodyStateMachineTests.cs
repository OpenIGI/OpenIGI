using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The player's body states 0 to 29, against the original's per-ticks and entries (<c>0x408770</c> to <c>0x40BE40</c>).</summary>
public sealed class HumanBodyStateMachineTests
{
    private const HumanMotionState Ground = HumanMotionState.OnGround;

    /// <summary>One transition a tick: a diagonal pressed from a standstill is the straight move first.</summary>
    [Fact]
    public void ADiagonalFromAStandstillIsReachedATickLater()
    {
        HumanMotionState keys = Ground | HumanMotionState.MoveForward | HumanMotionState.MoveRight;

        int first = HumanBodyStateMachine.Next(HumanBodyStateMachine.Stand, keys, jumpPressed: false);
        Assert.Equal(1, first);
        Assert.Equal(2, HumanBodyStateMachine.Next(first, keys, jumpPressed: false));
    }

    /// <summary>The walking forward state hands a strafe to the running diagonal (<c>0x409A80</c>), as the original does.</summary>
    [Fact]
    public void WalkingForwardIntoAStrafeTakesTheRunningDiagonal()
    {
        HumanMotionState keys = Ground | HumanMotionState.Walking | HumanMotionState.MoveForward | HumanMotionState.MoveRight;

        Assert.Equal(2, HumanBodyStateMachine.Next(9, keys, jumpPressed: false));
    }

    /// <summary>A crouch stays down under a low roof once the key is let go (<c>0x40AB60</c>'s <c>0x2010</c>).</summary>
    [Fact]
    public void ALowRoofKeepsTheCrouchDown()
    {
        Assert.Equal(HumanBodyStateMachine.Crouch, HumanBodyStateMachine.Next(HumanBodyStateMachine.Crouch, Ground | HumanMotionState.UnderRoof, false));
        Assert.Equal(HumanBodyStateMachine.Stand, HumanBodyStateMachine.Next(HumanBodyStateMachine.Crouch, Ground, false));
        Assert.Equal(26, HumanBodyStateMachine.Next(26, Ground | HumanMotionState.UnderRoof | HumanMotionState.MoveRight, false));
        Assert.Equal(7, HumanBodyStateMachine.Next(26, Ground | HumanMotionState.MoveRight, false));
    }

    /// <summary>The ground goes first everywhere but the two airborne states, which land into the gait the keys hold.</summary>
    [Fact]
    public void LosingTheGroundFallsAndTheJumpLandsIntoTheHeldGait()
    {
        Assert.Equal(HumanBodyStateMachine.Fall, HumanBodyStateMachine.Next(20, HumanMotionState.MoveForward, false));
        Assert.Equal(HumanBodyStateMachine.Jump, HumanBodyStateMachine.Next(HumanBodyStateMachine.Jump, HumanMotionState.MoveForward, false));
        Assert.Equal(9, HumanBodyStateMachine.Next(HumanBodyStateMachine.Jump, Ground | HumanMotionState.Walking | HumanMotionState.MoveForward, false));
        Assert.Equal(HumanBodyStateMachine.Stand, HumanBodyStateMachine.Next(HumanBodyStateMachine.Fall, Ground | HumanMotionState.MoveForward, false));
    }

    /// <summary>The entries' own lists of the states whose step they keep, asymmetric as they are.</summary>
    [Fact]
    public void EachEntryKeepsThePhaseOnlyFromItsOwnPredecessors()
    {
        Assert.True(HumanBodyStateMachine.KeepsPhase(3, previous: 1));
        Assert.False(HumanBodyStateMachine.KeepsPhase(2, previous: 1));
        Assert.True(HumanBodyStateMachine.KeepsPhase(20, previous: 1));
        Assert.Equal(0f, HumanBodyStateMachine.BlendFor(1, previous: HumanBodyStateMachine.Jump));
        Assert.Equal(HumanBodyStateMachine.BlendSeconds, HumanBodyStateMachine.BlendFor(2, previous: HumanBodyStateMachine.Jump));
    }

    /// <summary>
    /// The eye steps to the crouched offset with the crouch byte and sinks with the root bone over the crouch clip's six-tick
    /// fade, to 0.778 m; a crouch-walk carries it 0.22 m higher on the walking clip's own root.
    /// </summary>
    [RequiresGameDataFact]
    public void TheCrouchSinksTheEyeOverTheBlendAndACrouchWalkCarriesItHigher()
    {
        HumanLocomotionAnimator animator = new(BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff"))));
        PlayerBody body = new() { Position = new Vector3d(0d, 0d, 1000d), Walls = Floor };
        Assert.True(body.PlaceOnGround(terrain: null));

        PlayerCommand stand = new(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false);
        PlayerCommand crouch = stand with { Crouch = true };
        for (int tick = 0; tick < 10; tick++)
        {
            body.TickAnimated(stand, terrain: null, animator);
        }

        Assert.Equal(PlayerBody.StandingEyeHeight, body.EyeHeight, 0.5);

        List<double> eyes = [];
        for (int tick = 0; tick < 8; tick++)
        {
            body.TickAnimated(crouch, terrain: null, animator);
            eyes.Add(body.EyeHeight);
        }

        Assert.True(body.IsCrouched);
        Assert.True(eyes[0] > PlayerBody.CrouchingEyeHeight + 1000.0, "the first crouched tick is still most of the stand's root");
        for (int i = 1; i < 6; i++)
        {
            Assert.True(eyes[i] < eyes[i - 1]);
        }

        Assert.Equal(PlayerBody.CrouchingEyeHeight, eyes[^1], 1.0);

        PlayerCommand crouchWalk = crouch with { Forward = 1f };
        for (int tick = 0; tick < 12; tick++)
        {
            body.TickAnimated(crouchWalk, terrain: null, animator);
        }

        Assert.Equal(20, animator.State);
        Assert.InRange(body.EyeHeight - PlayerBody.CrouchingEyeHeight, 850.0, 950.0);
    }

    /// <summary>
    /// A run started from the stand gathers pace over its six-tick fade (<c>0x4D3020</c>): retail, mission 1 from the spawn,
    /// stepped 0, 139.2, 278.5, 417.7, 556.9, 696.2 and then 835.4 units a tick.
    /// </summary>
    [RequiresGameDataFact]
    public void ARunStartedFromTheStandGathersPaceOverItsFade()
    {
        HumanLocomotionAnimator animator = new(BoneAnimationSet.Read(
            File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff"))));
        animator.Play(HumanBodyStateMachine.Stand, 0f, keepPhase: false);
        for (int tick = 0; tick < 10; tick++)
        {
            animator.Advance();
        }

        animator.Play(1, HumanBodyStateMachine.BlendSeconds, keepPhase: false);
        double[] steps = [.. Enumerable.Range(0, 8).Select(_ => (double)animator.Advance().Y)];

        Assert.Equal(0.0, steps[0], 0.01);
        double full = steps[6];
        for (int k = 1; k <= 6; k++)
        {
            Assert.Equal(full * k / 6.0, steps[k], full * 0.01);
        }
    }

    private static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
    {
        hit = default;
        if (end.Z >= origin.Z || origin.Z < 0d || end.Z > 0d)
        {
            return false;
        }

        hit = new WorldRayHit(new Vector3d(origin.X, origin.Y, 0d), new Vector3f(0f, 0f, 1f), origin.Z);
        return true;
    }
}
