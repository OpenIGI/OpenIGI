using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Gravity, drag and root motion — how a human's position actually changes.
/// </summary>
public sealed class HumanMotionTests
{
    [Fact]
    public void GravityIsNearlyTwiceEarthsWhichIsWhyTheJumpIsNotFloaty()
    {
        // 84.741692 units per tick squared, at 30 ticks a second and 4096 units a metre.
        double metresPerSecondSquared = HumanMotion.GravityPerTick * 900.0 / 4096.0;

        Assert.Equal(18.62, metresPerSecondSquared, 0.01);
        Assert.True(metresPerSecondSquared > 2.0 * 9.0, "the airborne gravity is close to twice Earth's");
    }

    [Fact]
    public void TheLadderSlideHasItsOwnGravityAndIsTheOneThatLooksOrdinary()
    {
        // The trap this pair exists to mark: 44.600887 is exactly 9.8 m/s² and sits in an integrator
        // that looks airborne, but it belongs to the ladder slide. Taking it for gravity makes a jump
        // hang in the air for twice as long.
        Assert.Equal(9.8, HumanMotion.LadderSlideGravityPerTick * 900.0 / 4096.0, 1e-6);
        Assert.NotEqual(HumanMotion.LadderSlideGravityPerTick, HumanMotion.GravityPerTick);
    }

    [Fact]
    public void AFallHasNoDampingAndKeepsAccelerating()
    {
        // The airborne integrator applies gravity and nothing else - no drag, and so no terminal
        // velocity. Each tick removes exactly one gravity's worth.
        Vector3f velocity = new(100f, -50f, 0f);
        Vector3f after = HumanMotion.Integrate(velocity);

        Assert.Equal(100f, after.X);
        Assert.Equal(-50f, after.Y);
        Assert.Equal(-HumanMotion.GravityPerTick, after.Z, 1e-3f);

        for (int i = 0; i < 100; i++)
        {
            after = HumanMotion.Integrate(after);
        }

        Assert.Equal(-HumanMotion.GravityPerTick * 101f, after.Z, 1e-1f);
    }

    [Fact]
    public void TheLadderSlideDampsAndSoHasATerminalSpeed()
    {
        Vector3f velocity = default;
        for (int i = 0; i < 4000; i++)
        {
            velocity = HumanMotion.IntegrateLadderSlide(velocity);
        }

        double terminal = -HumanMotion.LadderSlideGravityPerTick * HumanMotion.LadderSlideDragPerTick
            / (1f - HumanMotion.LadderSlideDragPerTick);

        Assert.Equal(terminal, velocity.Z, 1e-2);
    }

    [Fact]
    public void AJumpRisesAndFallsInAboutThreeQuartersOfASecond()
    {
        // 1024 units a tick against this gravity: twelve ticks up, twelve down.
        const float jump = 1024f;

        Vector3f velocity = new(0f, 0f, jump);
        double height = 0.0;
        double apex = 0.0;
        int ticks = 0;

        while (ticks < 300)
        {
            height += velocity.Z;
            apex = Math.Max(apex, height);
            velocity = HumanMotion.Integrate(velocity);
            ticks++;

            if (height <= 0.0 && velocity.Z < 0f)
            {
                break;
            }
        }

        // The discrete sum, not the continuous one: the body is moved by the current velocity and only
        // then does gravity bite, so the rise is the arithmetic series and lands about 8% above the
        // closed form. Thirteen ticks of it, the last of them the one that turns over.
        int rising = (int)Math.Ceiling(jump / HumanMotion.GravityPerTick);
        double expected = (rising * (double)jump) - (HumanMotion.GravityPerTick * rising * (rising - 1) / 2.0);

        Assert.Equal(13, rising);
        Assert.Equal(expected, apex, 1.0);
        Assert.InRange(apex / 4096.0, 1.4, 1.8);
        Assert.InRange(ticks / 30.0, 0.6, 0.95);
    }

    [Fact]
    public void AirControlAddsAlongTheAxesTheBodyFaces()
    {
        // You can steer a jump a little: the integrator adds the movement speed along each held axis
        // every airborne tick, in the body's own frame.
        Matrix3f facing = Matrix3f.Identity;

        Vector3f ahead = HumanMotion.AirControl(forward: 1f, strafe: 0f, facing, speed: 10f);
        Assert.Equal(0f, ahead.X, 1e-4f);
        Assert.Equal(10f, ahead.Y, 1e-4f);

        Vector3f right = HumanMotion.AirControl(0f, 1f, facing, 10f);
        Assert.Equal(10f, right.X, 1e-4f);
        Assert.Equal(0f, right.Y, 1e-4f);

        // Nothing held, nothing added.
        Assert.Equal(default, HumanMotion.AirControl(0f, 0f, facing, 10f));
    }

    [Fact]
    public void RootMotionScalesThenRotates()
    {
        // Walking is the animation's own delta translation, scaled and turned into a world direction.
        // With an identity basis the step is just the scaled delta.
        Vector3f step = HumanMotion.ApplyRootMotion(new Vector3f(10f, 0f, 0f), Matrix3f.Identity);

        Assert.Equal(10f * HumanMotion.DefaultDeltaTranslationScale, step.X, 1e-4f);
        Assert.Equal(0f, step.Y);
        Assert.Equal(0f, step.Z);
    }

    [Fact]
    public void TheGateLeavesTheDeltaUnscaled()
    {
        Vector3f scaled = HumanMotion.ApplyRootMotion(new Vector3f(10f, 0f, 0f), Matrix3f.Identity);
        Vector3f raw = HumanMotion.ApplyRootMotion(
            new Vector3f(10f, 0f, 0f), Matrix3f.Identity, scaleSuppressed: true);

        Assert.Equal(10f, raw.X, 1e-4f);
        Assert.True(scaled.X > raw.X);
    }

    [Fact]
    public void TheRetailScaleIsSevenQuarters()
    {
        Assert.Equal(1.75f, HumanMotion.DefaultDeltaTranslationScale);
    }

    [Fact]
    public void ThereIsNoWalkingSpeedConstantBecauseTheAnimationCarriesIt()
    {
        // A property rather than a value: doubling the animation's delta doubles the step. That is what
        // makes a walk cycle's own ground coverage the speed, and why no constant exists to look up.
        Vector3f small = HumanMotion.ApplyRootMotion(new Vector3f(4f, 0f, 0f), Matrix3f.Identity);
        Vector3f large = HumanMotion.ApplyRootMotion(new Vector3f(8f, 0f, 0f), Matrix3f.Identity);

        Assert.Equal(2f * small.X, large.X, 1e-4f);
    }
}
