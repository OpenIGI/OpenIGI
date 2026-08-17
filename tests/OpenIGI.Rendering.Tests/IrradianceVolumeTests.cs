using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins the probe field's geometry, its harmonics, and the test that stops light leaking through walls.
/// </summary>
public sealed class IrradianceVolumeTests
{
    [Fact]
    public void ProbesAreLaidOutWithXVaryingFastest()
    {
        // The GL upload walks the arrays straight into a volume texture, whose memory order this is.
        // Getting it wrong does not fail, it transposes the level's lighting.
        IrradianceVolume volume = new(new Vector3f(10f, 20f, 30f), 2f, 4, 3, 2);

        Assert.Equal(0, volume.IndexOf(0, 0, 0));
        Assert.Equal(1, volume.IndexOf(1, 0, 0));
        Assert.Equal(4, volume.IndexOf(0, 1, 0));
        Assert.Equal(12, volume.IndexOf(0, 0, 1));
        Assert.Equal(24, volume.ProbeCount);
    }

    [Fact]
    public void AProbeSitsAtItsOwnGridPosition()
    {
        IrradianceVolume volume = new(new Vector3f(10f, 20f, 30f), 2.5f, 4, 4, 4);

        Vector3f position = volume.PositionOf(2, 1, 3);

        Assert.Equal(15f, position.X, 4);
        Assert.Equal(22.5f, position.Y, 4);
        Assert.Equal(37.5f, position.Z, 4);
    }

    [Fact]
    public void AConstantFieldGivesTheSameIrradianceInEveryDirection()
    {
        // The zeroth harmonic alone is light arriving equally from everywhere, and a surface should
        // receive the same from it whichever way it faces.
        float[] coefficients = [0.7f, 0f, 0f, 0f];

        Assert.Equal(0.7f, IrradianceVolume.Evaluate(coefficients, new Vector3f(1f, 0f, 0f)), 5);
        Assert.Equal(0.7f, IrradianceVolume.Evaluate(coefficients, new Vector3f(0f, 0f, -1f)), 5);
    }

    [Fact]
    public void ADirectionalFieldIsBrightestFacingItAndNeverNegative()
    {
        // Light arriving from +Z. A surface facing it receives most, one facing away receives none --
        // and "none" has to be zero rather than the negative number the fit produces, because negative
        // irradiance is not a dim surface, it is a hole in the picture.
        float[] coefficients = [0.5f, 0f, 0f, 0.5f];

        float facing = IrradianceVolume.Evaluate(coefficients, new Vector3f(0f, 0f, 1f));
        float sideways = IrradianceVolume.Evaluate(coefficients, new Vector3f(1f, 0f, 0f));
        float away = IrradianceVolume.Evaluate(coefficients, new Vector3f(0f, 0f, -1f));

        Assert.Equal(1f, facing, 5);
        Assert.Equal(0.5f, sideways, 5);
        Assert.Equal(0f, away, 5);
    }

    [Fact]
    public void AProbeRoundTripsItsCoefficientsAndMoments()
    {
        IrradianceVolume volume = new(default, 1f, 2, 2, 2);
        float[] written = [.. Enumerable.Range(1, IrradianceVolume.CoefficientsPerProbe).Select(i => i * 0.5f)];

        volume.SetProbe(5, written, 3.5f, 16.25f);

        ReadOnlySpan<float> stored = volume.Coefficients.Slice(
            5 * IrradianceVolume.CoefficientsPerProbe,
            IrradianceVolume.CoefficientsPerProbe);

        Assert.Equal(written, stored.ToArray());
        Assert.Equal(3.5f, volume.Distances[10], 5);
        Assert.Equal(16.25f, volume.Distances[11], 5);
    }

    [Fact]
    public void AProbeRejectsTheWrongNumberOfCoefficients() =>
        Assert.Throws<ArgumentException>(() =>
            new IrradianceVolume(default, 1f, 1, 1, 1).SetProbe(0, [1f, 2f, 3f], 1f, 1f));

    [Fact]
    public void ASurfaceNearerThanAnythingTheProbeSeesIsFullyTrusted()
    {
        // The probe sees geometry at four metres on average; a surface one metre away is unambiguously
        // on the probe's side of it, and there is nothing for the test to doubt.
        Assert.Equal(1f, IrradianceVolume.ChebyshevWeight(1f, 4f, 17f), 5);
    }

    [Fact]
    public void ASurfaceWellBeyondWhatTheProbeSeesIsDistrusted()
    {
        // A probe that consistently sees a wall two metres off, asked about a surface eight metres
        // away: whatever is out there, this probe is not describing it. This is the wall-leak case.
        float weight = IrradianceVolume.ChebyshevWeight(8f, 2f, 4.1f);

        Assert.InRange(weight, 0f, 0.05f);
    }

    [Fact]
    public void AProbeWithNothingCloseByStaysUsefulFurtherOut()
    {
        // Open ground: the probe sees geometry at thirty metres with a wide spread, so a surface five
        // metres past its mean is still plausibly the same space. Distrusting that would leave every
        // outdoor surface falling back to flat ambient, which is the field doing nothing.
        float weight = IrradianceVolume.ChebyshevWeight(35f, 30f, 1000f);

        Assert.InRange(weight, 0.5f, 1f);
    }

    [Fact]
    public void TheVolumeRefusesAnEmptyOrInvertedGrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrradianceVolume(default, 1f, 0, 4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IrradianceVolume(default, 0f, 4, 4, 4));
    }
}
