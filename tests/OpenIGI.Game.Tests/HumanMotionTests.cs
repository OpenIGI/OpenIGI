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
    public void GravityIsExactlyNinePointEightMetresPerSecondSquared()
    {
        // 44.600887 units per tick squared, at 30 ticks a second and 4096 units a metre.
        const double ticksSquaredPerSecondSquared = 30.0 * 30.0;
        const double unitsPerMetre = 4096.0;

        double metresPerSecondSquared =
            HumanMotion.GravityPerTick * ticksSquaredPerSecondSquared / unitsPerMetre;

        Assert.Equal(9.8, metresPerSecondSquared, 1e-6);
    }

    [Fact]
    public void GravityIsSubtractedBeforeTheDragNotAfter()
    {
        // The ordering is the whole difference between two visibly different falls, so it is worth a
        // test of its own rather than trusting the expression.
        Vector3f start = new(0f, 0f, 0f);
        Vector3f after = HumanMotion.Integrate(start);

        Assert.Equal(
            (0f - HumanMotion.GravityPerTick) * HumanMotion.DragPerTick,
            after.Z,
            1e-4f);

        // Damping first would give a different number.
        Assert.NotEqual((0f * HumanMotion.DragPerTick) - HumanMotion.GravityPerTick, after.Z);
    }

    [Fact]
    public void HorizontalVelocityOnlyDecays()
    {
        Vector3f after = HumanMotion.Integrate(new Vector3f(100f, -50f, 0f));

        Assert.Equal(100f * HumanMotion.DragPerTick, after.X, 1e-4f);
        Assert.Equal(-50f * HumanMotion.DragPerTick, after.Y, 1e-4f);
    }

    [Fact]
    public void AFallConvergesOnTerminalVelocityAndNeverPassesIt()
    {
        Vector3f velocity = new(0f, 0f, 0f);
        for (int tick = 0; tick < 4000; tick++)
        {
            velocity = HumanMotion.Integrate(velocity);
            Assert.True(velocity.Z >= HumanMotion.TerminalVelocity - 1e-2f);
        }

        Assert.Equal(HumanMotion.TerminalVelocity, velocity.Z, 1e-2f);

        // Which is about 32 metres a second - a plausible terminal velocity, not an artefact.
        double metresPerSecond = -HumanMotion.TerminalVelocity * 30.0 / 4096.0;
        Assert.InRange(metresPerSecond, 30.0, 34.0);
    }

    [Fact]
    public void AJumpRisesAndFallsBackInAPlausibleTime()
    {
        // The retail jump velocity is 1024 units a tick. Integrating it should give an arc a person
        // would recognise rather than a hop or a moon jump.
        const float jump = 1024f;

        Vector3f velocity = new(0f, 0f, jump);
        double height = 0.0;
        int ticks = 0;
        double apex = 0.0;

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

        double apexMetres = apex / 4096.0;

        // Checked against the closed form rather than against a guess at what a jump should look like:
        // without drag the apex is v squared over twice g, and the drag can only lower it.
        double undamped = (double)jump * jump / (2.0 * HumanMotion.GravityPerTick) / 4096.0;

        Assert.True(apexMetres < undamped, "the drag must lower the apex");
        Assert.True(apexMetres > undamped * 0.85, "the drag should not halve it either");

        // For the record, since it is the number the game actually produces: 1024 units a tick is
        // 7.5 m/s, and that reaches a shade over two and a half metres.
        Assert.InRange(apexMetres, 2.4, 2.8);
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
