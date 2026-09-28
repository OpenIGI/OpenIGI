using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The two damped accumulators <c>0x4614B0</c> adds to every move, and the three step-down budgets it
/// hands the ground query.
/// </summary>
public sealed class PlayerBodyShoveAndSlideTests
{
    private static PlayerCommand Still => new(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: false);

    /// <summary>A body standing on flat ground, with no terrain and no walls.</summary>
    private static PlayerBody OnFlatGround()
    {
        const double FloorZ = 0.0;
        PlayerBody body = new();

        // A flat floor at Z = 0, answering only the ground query's downward probe. The horizontal
        // probes the wall sweep casts must miss, or the body would be walled in on every side.
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
        body.Tick(Still, terrain: null, rootDelta: default);
        return body;
    }

    [Fact]
    public void AShoveOnFlatGroundSurvivesTheTickAfterIt()
    {
        PlayerBody body = OnFlatGround();
        Vector3d start = body.Position;

        body.AddHitShove(PlayerBody.HitShoveUnitsPerTick, 0f);

        // Flat ground rewrites the slope slide to zero every tick. If the shove shared that accumulator
        // it would be erased here, and a bullet would move the player for exactly one tick.
        body.Tick(Still, terrain: null, rootDelta: default);
        body.Tick(Still, terrain: null, rootDelta: default);
        body.Tick(Still, terrain: null, rootDelta: default);

        Assert.True(body.Position.X - start.X > 100.0);
        Assert.NotEqual(0f, body.HitShove.X);
    }

    [Fact]
    public void TheShoveIsDampedBeforeItMovesTheBody()
    {
        PlayerBody body = OnFlatGround();
        Vector3d start = body.Position;
        body.AddHitShove(1000f, 0f);

        body.Tick(Still, terrain: null, rootDelta: default);
        double first = body.Position.X - start.X;
        body.Tick(Still, terrain: null, rootDelta: default);
        double second = body.Position.X - start.X - first;

        // 0x4614B0 multiplies +1648 by 0.9 and only then adds it: 900 on the first tick, 810 on the next.
        Assert.Equal(900.0, first, 0.5);
        Assert.Equal(810.0, second, 0.5);
    }

    [Fact]
    public void TheShoveDecaysByNineTenthsATick()
    {
        PlayerBody body = OnFlatGround();
        body.AddHitShove(1000f, 0f);

        body.Tick(Still, terrain: null, rootDelta: default);
        float first = body.HitShove.X;

        body.Tick(Still, terrain: null, rootDelta: default);
        float second = body.HitShove.X;

        Assert.Equal(900f, first, 0.5f);
        Assert.Equal(first * 0.9f, second, 0.5f);
    }

    [Fact]
    public void TheShoveRunsOutRatherThanCarryingForever()
    {
        PlayerBody body = OnFlatGround();
        body.AddHitShove(PlayerBody.HitShoveUnitsPerTick, 0f);

        for (int tick = 0; tick < 120; tick++)
        {
            body.Tick(Still, terrain: null, rootDelta: default);
        }

        Assert.True(MathF.Abs(body.HitShove.X) < 0.01f);
    }

    [Fact]
    public void TheSlopeSlideKeepsItsOwnAccumulatorClearOfTheShove()
    {
        PlayerBody body = OnFlatGround();
        body.AddHitShove(500f, 250f);

        body.Tick(Still, terrain: null, rootDelta: default);

        // Flat ground: the slide is rebuilt as nothing every tick, whatever the shove is doing.
        Assert.Equal(0f, body.SlopeSlide.X);
        Assert.Equal(0f, body.SlopeSlide.Y);
    }

    [Fact]
    public void LandingClearsTheShoveWithTheRestOfTheVelocity()
    {
        PlayerBody body = OnFlatGround();
        body.AddHitShove(1000f, 0f);

        // Off the floor, then back down onto it: 0x40BE40 zeroes Human+1636 through +1656 together.
        body.Tick(new PlayerCommand(0f, 0f, 0f, 0f, Run: true, Crouch: false, Jump: true), null, default);
        Assert.False(body.IsGrounded);

        for (int tick = 0; tick < 60 && !body.IsGrounded; tick++)
        {
            body.Tick(Still, terrain: null, rootDelta: default);
        }

        Assert.True(body.IsGrounded);
        Assert.Equal(0f, body.HitShove.X);
        Assert.Equal(Vector3f.Zero, body.Velocity);
    }

    [Theory]
    [InlineData(false, false, 0.0)]
    [InlineData(true, false, 2048.0)]
    [InlineData(true, true, 8192.0)]
    public void TheGroundQueryTakesTheBudgetTheMoverChose(bool grounded, bool sliding, double expected)
    {
        // 0x4614B0 passes 0, 2048 or 8192 to 0x462390 -- and the sliding case is guarded by the
        // on-ground bit, so a jump off a slope gets the airborne zero rather than the slide's 2 m.
        Assert.Equal(expected, HumanGroundQuery.StepDownBudget(grounded, sliding));
    }
}
