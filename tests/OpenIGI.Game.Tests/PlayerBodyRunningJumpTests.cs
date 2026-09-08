using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A running jump keeps the pace it took off with. <c>0x40BA70</c> normalises <c>Human+1312</c>, which
/// the update has not yet refilled for this tick, so the direction comes from the run step rather than
/// from the jump clip that is about to replace it.
/// </summary>
public sealed class PlayerBodyRunningJumpTests
{
    /// <summary>The forward step a run cycle covers in one tick, in the body's own frame.</summary>
    private static Vector3f RunStep => new(0f, 200f, 0f);

    /// <summary>What the jump clip itself covers — near enough to nothing.</summary>
    private static Vector3f JumpStep => new(0f, 0f, 0f);

    private static PlayerCommand Running => new(1f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false);

    private static PlayerCommand RunningJump => new(1f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: true);

    private static PlayerBody OnFlatGround()
    {
        const double FloorZ = 0.0;
        PlayerBody body = new();

        body.Walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = default;
            if (end.Z >= origin.Z || origin.Z < FloorZ || end.Z > FloorZ)
            {
                return false;
            }

            hit = new WorldRayHit(
                new Vector3d(origin.X, origin.Y, FloorZ),
                new Vector3f(0f, 0f, 1f),
                origin.Z - FloorZ);
            return true;
        };

        body.Position = new Vector3d(0.0, 0.0, FloorZ);
        body.Tick(Running, terrain: null, RunStep);
        return body;
    }

    [Fact]
    public void ARunningJumpCarriesTheFullHorizontalSpeed()
    {
        PlayerBody body = OnFlatGround();

        // Two run ticks so the previous step is unambiguously the run's, then the take-off tick -- on
        // which the animator has already switched to state 28 and the clip covers no ground at all.
        body.Tick(Running, terrain: null, RunStep);
        body.Tick(RunningJump, terrain: null, JumpStep);

        Assert.False(body.IsGrounded);
        Assert.Equal((float)body.JumpHorizontalSpeed, body.Velocity.Y, 0.5f);
        Assert.Equal(0f, body.Velocity.X, 0.5f);
        Assert.Equal((float)body.JumpSpeed, body.Velocity.Z, 0.5f);
    }

    [Fact]
    public void TheRunningJumpActuallyTravels()
    {
        PlayerBody body = OnFlatGround();
        body.Tick(Running, terrain: null, RunStep);

        double beforeY = body.Position.Y;
        body.Tick(RunningJump, terrain: null, JumpStep);

        // The regression this guards: taking the direction from the jump clip left the body hopping on
        // the spot, because that clip's root motion is zero and the normalise fell through to nothing.
        Assert.True(body.Position.Y - beforeY > 100.0);
    }

    [Fact]
    public void AStandingJumpStillGoesStraightUp()
    {
        PlayerBody body = new();
        body.Position = new Vector3d(0.0, 0.0, 0.0);
        body.Walls = OnFlatGround().Walls;

        body.Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false), null, default);
        body.Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: true), null, default);

        // Both components of Human+1312 are zero, so 0x40BA70 skips the normalise and leaves them.
        Assert.Equal(0f, body.Velocity.X, 0.5f);
        Assert.Equal(0f, body.Velocity.Y, 0.5f);
        Assert.Equal((float)body.JumpSpeed, body.Velocity.Z, 0.5f);
    }

    [Fact]
    public void AStrafingJumpCarriesTheDirectionItWasStrafing()
    {
        PlayerBody body = new();
        body.Position = new Vector3d(0.0, 0.0, 0.0);
        body.Walls = OnFlatGround().Walls;

        Vector3f strafeStep = new(200f, 0f, 0f);
        PlayerCommand strafing = new(0f, 1f, 0f, 0f, Run: true, Crouch: false, Jump: false);

        body.Tick(strafing, terrain: null, strafeStep);
        body.Tick(strafing, terrain: null, strafeStep);
        body.Tick(new PlayerCommand(0f, 1f, 0f, 0f, Run: true, Crouch: false, Jump: true), null, JumpStep);

        Assert.Equal((float)body.JumpHorizontalSpeed, body.Velocity.X, 0.5f);
        Assert.Equal(0f, body.Velocity.Y, 0.5f);
    }
}
