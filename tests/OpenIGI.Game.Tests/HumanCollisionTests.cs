using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the slope slide and the human-versus-human pass of the movement integrator.
/// </summary>
public sealed class HumanCollisionTests
{
    [Fact]
    public void FlatGroundIsZeroDegreesAndAWallIsNinety()
    {
        // 90 - asin(z). Using acos here instead would flip the whole scale, which is exactly the
        // mistake this pins: flat ground has to read zero.
        Assert.Equal(0f, HumanSlopeSlide.SlopeDegrees(new Vector3f(0f, 0f, 1f)), 3);
        Assert.Equal(90f, HumanSlopeSlide.SlopeDegrees(new Vector3f(1f, 0f, 0f)), 3);
        Assert.Equal(45f, HumanSlopeSlide.SlopeDegrees(new Vector3f(1f, 0f, 1f)), 3);

        // A denormalised normal is clamped rather than producing a NaN.
        Assert.Equal(0f, HumanSlopeSlide.SlopeDegrees(new Vector3f(0f, 0f, 50f)), 3);
    }

    [Fact]
    public void TheSlideSpeedsSitOnTheKilometresPerHourGrid()
    {
        // The two shallow speeds are exact km/h through the same 37.9259 factor the tuning parameters
        // use, and the shallowest is bit-for-bit what general argument 3 converts to. The two steep
        // ones are round numbers of *units*, not of km/h — 948 and 1137 come out at 25.00 and 29.98.
        float perKph = HumanPlayerParameters.UnitsPerTickPerKilometrePerHour;

        Assert.Equal(1.5f, HumanSlopeSlide.ModerateSpeed / perKph, 4);
        Assert.Equal(0.5f, HumanSlopeSlide.ShallowSpeed / perKph, 4);
        Assert.Equal(HumanSlopeSlide.ShallowSpeed, 0.5f * perKph, 4);

        Assert.Equal(25.00f, HumanSlopeSlide.SteepSpeed / perKph, 2);
        Assert.Equal(29.98f, HumanSlopeSlide.VerySteepSpeed / perKph, 2);
    }

    [Fact]
    public void GentleGroundDoesNotSlideAtAll()
    {
        Vector3f accumulated = HumanSlopeSlide.Accumulate(default, new Vector3f(0f, 0f, 1f));
        Assert.Equal(default, accumulated);

        // Just under the 45 degree threshold.
        Vector3f shallow = Normal(44f);
        Assert.Equal(default, HumanSlopeSlide.Accumulate(default, shallow));
    }

    [Fact]
    public void TheSteepBandsReplaceAndTheShallowOnesAdd()
    {
        Vector3f existing = new(100f, 0f, 0f);

        // 70 degrees replaces: whatever had built up is discarded. The speed scales the normal's
        // *horizontal* components, which are sin(slope) long — so the result is 948 x sin(70), not
        // 948. The original never renormalises the horizontal part, so a shallow slope slides slower
        // than its band's speed suggests.
        Vector3f steep = HumanSlopeSlide.Accumulate(existing, Normal(70f));
        float magnitude = MathF.Sqrt((steep.X * steep.X) + (steep.Y * steep.Y));

        Assert.Equal(HumanSlopeSlide.SteepSpeed * MathF.Sin(70f * MathF.PI / 180f), magnitude, 1);
        Assert.True(magnitude < HumanSlopeSlide.SteepSpeed);

        // 47 degrees adds, so the accumulator grows tick over tick.
        Vector3f once = HumanSlopeSlide.Accumulate(default, Normal(47f));
        Vector3f twice = HumanSlopeSlide.Accumulate(once, Normal(47f));
        Assert.True(MathF.Abs(twice.X) > MathF.Abs(once.X));
    }

    [Fact]
    public void LosingTheGroundClearsTheSlide()
    {
        Vector3f built = HumanSlopeSlide.Accumulate(default, Normal(70f));
        Assert.NotEqual(default, built);
        Assert.Equal(default, HumanSlopeSlide.Accumulate(built, null));
    }

    [Fact]
    public void DampingIsNinetyPercentPerTick()
    {
        Vector3f damped = HumanSlopeSlide.Damp(new Vector3f(100f, 200f, 300f));

        Assert.Equal(90f, damped.X, 3);
        Assert.Equal(180f, damped.Y, 3);
        Assert.Equal(270f, damped.Z, 3);
    }

    [Fact]
    public void SomeoneFarEnoughAwayNeverBlocks()
    {
        Vector3d start = new(0, 0, 0);
        Vector3d end = new(1000, 0, 0);

        // Outside the radius horizontally.
        Assert.Equal(end, HumanSeparation.Resolve(start, end, new Vector3d(9000, 0, 0)));

        // Inside horizontally but on another floor.
        Assert.Equal(end, HumanSeparation.Resolve(start, end, new Vector3d(1000, 0, 8000)));
    }

    [Fact]
    public void WalkingIntoSomeoneIsRefusedOutright()
    {
        // Not pushed aside — the move is simply not taken.
        Vector3d start = new(0, 0, 0);
        Vector3d end = new(2000, 0, 0);
        Vector3d other = new(3000, 0, 0);

        Assert.Equal(start, HumanSeparation.Resolve(start, end, other));
    }

    [Fact]
    public void WalkingAwayFromSomeoneAlreadyTouchingIsAllowed()
    {
        // The test is "does this move close the gap", so an overlap can always be walked out of.
        // Without that, two humans that spawn touching would be locked in place forever.
        Vector3d start = new(0, 0, 0);
        Vector3d end = new(-2000, 0, 0);
        Vector3d other = new(1000, 0, 0);

        Assert.Equal(end, HumanSeparation.Resolve(start, end, other));
    }

    [Fact]
    public void OneBlockerIsEnough()
    {
        Vector3d start = new(0, 0, 0);
        Vector3d end = new(2000, 0, 0);
        Vector3d[] others = [new Vector3d(90000, 0, 0), new Vector3d(3000, 0, 0)];

        Assert.Equal(start, HumanSeparation.Resolve(start, end, others));
    }

    [Fact]
    public void TheCylinderIsAMetreEightHighAndAboutHalfAMetreAcross()
    {
        Assert.Equal(1.8, HumanSeparation.HeightInUnits / 4096.0, 4);
        Assert.Equal(0.55, HumanSeparation.RadiusInUnits / 4096.0, 4);
    }

    private static Vector3f Normal(float slopeDegrees)
    {
        // A normal whose slope reads as the given angle: tilt it away from +Z by that much.
        float radians = slopeDegrees * MathF.PI / 180f;
        return new Vector3f(MathF.Sin(radians), 0f, MathF.Cos(radians));
    }
}
