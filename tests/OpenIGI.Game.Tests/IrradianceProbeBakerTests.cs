using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the directions a probe is asked about.
/// </summary>
/// <remarks>
/// A bake that differs between runs is a level that lights differently each time it loads, which is
/// the kind of fault that gets reported as "the shadows moved" and reproduces once in ten tries.
/// </remarks>
public sealed class IrradianceProbeBakerTests
{
    [Fact]
    public void EveryDirectionIsUnitLength()
    {
        foreach (Vector3f direction in IrradianceProbeBaker.FibonacciSphere(64))
        {
            Assert.Equal(1f, MathF.Sqrt(direction.LengthSquared), 4);
        }
    }

    [Fact]
    public void TheDirectionsAreTheSameEveryTime()
    {
        Vector3f[] first = IrradianceProbeBaker.FibonacciSphere(32);
        Vector3f[] second = IrradianceProbeBaker.FibonacciSphere(32);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheDirectionsCancelOut()
    {
        // An even spread over a sphere sums to nothing. A set that does not is one biased towards a
        // pole, which would light every probe as though the sky were only overhead.
        Vector3f sum = default;

        foreach (Vector3f direction in IrradianceProbeBaker.FibonacciSphere(256))
        {
            sum = new Vector3f(sum.X + direction.X, sum.Y + direction.Y, sum.Z + direction.Z);
        }

        Assert.InRange(MathF.Sqrt(sum.LengthSquared) / 256f, 0f, 0.01f);
    }

    [Fact]
    public void BothHemispheresGetTheirShare()
    {
        // Half the sky is above and half below, and a probe under a floor is as informative as one
        // over it -- it is what says the floor is there.
        int above = IrradianceProbeBaker.FibonacciSphere(128).Count(d => d.Z > 0f);

        Assert.InRange(above, 56, 72);
    }

    [Fact]
    public void ASphereOfNoDirectionsIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IrradianceProbeBaker.FibonacciSphere(0));
}
