using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// The original's distance fog, checked against the arithmetic read out of <c>IGI.EXE</c> rather than
/// against anything this port computes — see <see cref="SceneFog"/> for the addresses.
/// </summary>
public sealed class SceneFogTests
{
    /// <summary>The whole chain from a level's <c>Fog Amount</c> to an extinction per metre.</summary>
    /// <remarks>
    /// <c>sub_527520</c> publishes the keyframe's <c>density × 0.0005</c>, <c>sub_512C30</c> multiplies
    /// the view depth by <c>0.001</c> before the exponential, and the depth is in game units. 4096 of
    /// those to the metre leaves <c>0.002048</c> per metre for every unit of declared density, and
    /// nothing in the chain may drift from that without the whole picture's visibility changing.
    /// </remarks>
    [Fact]
    public void TheDensityIsTheOriginalsChainOfConstants()
    {
        Assert.Equal(0.002048f, SceneFog.DensityFor(1f), 7);
        Assert.Equal(0.001024f, SceneFog.DensityFor(0.5f), 7);

        // Level 1's own band, and the two distances it implies.
        SceneFog fog = SceneFog.FromGlobalLight(new Vector3f(0.577f, 0.538f, 0.481f), 1.89f);
        Assert.Equal(0.0038707f, fog.DensityPerMetre, 6);
        Assert.Equal(0.5f, fog.FactorAt(179f), 2);
        Assert.Equal(0.01f, fog.FactorAt(1190f), 3);

        // Nothing at the eye is fogged, whatever the density.
        Assert.Equal(1f, fog.FactorAt(0f), 6);
        Assert.Equal(1f, fog.FactorAt(-10f), 6);
    }

    /// <summary>A level that asks for no fog, and the ceiling <c>sub_50FD20</c> clamps to.</summary>
    [Fact]
    public void NoAmountIsNoFogAndTheDensityIsClamped()
    {
        Assert.Equal(0f, SceneFog.DensityFor(0f));
        Assert.Equal(0f, SceneFog.DensityFor(-1f));
        Assert.False(SceneFog.None.IsEnabled);
        Assert.Equal(1f, SceneFog.None.FactorAt(10_000f));

        // Past the clamp the published density stops rising, so the extinction does too.
        float clamped = SceneFog.MaximumDensity * SceneFog.DistanceScale * 4096f;
        Assert.Equal(clamped, SceneFog.DensityFor(1e9f), 0);
    }

    /// <summary>The colour follows the blend, which is what stops a multi-pass surface fogging twice.</summary>
    /// <remarks>
    /// <c>sub_5144E0</c> hands Direct3D the level's colour for an opaque draw and black for the additive
    /// one. The terrain draws its base material opaque and every further material additively, and their
    /// weights sum to one at every vertex: fog the additive passes to the level's colour instead and a
    /// chunk covered by three materials comes back three times as foggy as the ground beside it.
    /// </remarks>
    [Fact]
    public void AnAdditivePassFogsToBlackAndEverythingElseToTheLevelsColour()
    {
        Vector3f colour = new(0.2f, 0.288f, 0.4f);
        SceneFog fog = SceneFog.FromGlobalLight(colour, 0.9f);

        Assert.Equal(colour, fog.ColourFor(BlendMode.Opaque));
        Assert.Equal(colour, fog.ColourFor(BlendMode.AlphaBlend));
        Assert.Equal(default, fog.ColourFor(BlendMode.Additive));
    }

    /// <summary>The colour is clamped on the way in, as the original's setter clamps it.</summary>
    [Fact]
    public void TheColourIsClampedToTheUnitRange()
    {
        SceneFog fog = SceneFog.FromGlobalLight(new Vector3f(-1f, 0.5f, 3f), 1f);
        Assert.Equal(new Vector3f(0f, 0.5f, 1f), fog.Colour);
    }
}
