using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Guards the mip filter's alpha-weighted colour, the fix for cut-out surfaces (chain-link, foliage,
/// railings) rendering black at a distance or under a render scale below one.
/// </summary>
public sealed class MipChainBuilderTests
{
    // Bgra32 is (blue, green, red, alpha).
    private static Bgra32 Rgba(byte r, byte g, byte b, byte a) => new(b, g, r, a);

    [Fact]
    public void AlphaWeightedColourKeepsACutOutWireFromGoingBlack()
    {
        // A 2x2 cut-out: one opaque bright-red wire texel, three fully transparent gap texels that carry
        // black RGB (as a converted cut-out's holes almost always do).
        Bgra32[] levelZero =
        [
            Rgba(255, 0, 0, 255), // the wire
            Rgba(0, 0, 0, 0),     // gap
            Rgba(0, 0, 0, 0),     // gap
            Rgba(0, 0, 0, 0),     // gap
        ];

        (Bgra32[] pixels, int[] offsets) = MipChainBuilder.Build(levelZero, 2, 2);

        // Level one is the single texel the whole 2x2 collapses to.
        Bgra32 mip = pixels[offsets[1]];

        // Weighted by alpha, only the wire contributes colour, so the wire's red survives almost whole.
        // The old flat average mixed it with three black gaps and dropped it to about 137 -- drawn black
        // once the coverage restore lifted the alpha back over the test.
        Assert.True(mip.R >= 250, $"wire red collapsed to {mip.R}; expected it preserved near 255");
        Assert.Equal(0, mip.G);
        Assert.Equal(0, mip.B);

        // Alpha is still the flat average -- coverage is restored later, separately.
        Assert.Equal((255 + 0 + 0 + 0 + 2) / 4, mip.A);
    }

    [Fact]
    public void AnOpaqueTextureIsAveragedExactlyAsBefore()
    {
        // Every alpha equal, so the alpha weighting reduces to the flat average it always was -- the
        // regression guard that this change touches nothing but cut-outs.
        Bgra32[] levelZero =
        [
            Rgba(255, 0, 0, 255),
            Rgba(0, 0, 0, 255),
            Rgba(0, 0, 0, 255),
            Rgba(0, 0, 0, 255),
        ];

        (Bgra32[] pixels, int[] offsets) = MipChainBuilder.Build(levelZero, 2, 2);
        Bgra32 mip = pixels[offsets[1]];

        // One full red among three blacks, averaged in linear light and re-encoded: about 137, and NOT
        // the 255 the weighted path gives a cut-out -- the two paths must stay distinct.
        Assert.InRange(mip.R, 135, 139);
        Assert.Equal(255, mip.A);
    }

    [Fact]
    public void AFullyTransparentQuadFallsBackToTheFlatAverageAndDoesNotDivideByZero()
    {
        // Nothing is drawn here, so the colour is never sampled; the point is only that a zero alpha sum
        // does not divide by zero and the level is still produced.
        Bgra32[] levelZero =
        [
            Rgba(80, 80, 80, 0),
            Rgba(80, 80, 80, 0),
            Rgba(80, 80, 80, 0),
            Rgba(80, 80, 80, 0),
        ];

        (Bgra32[] pixels, int[] offsets) = MipChainBuilder.Build(levelZero, 2, 2);
        Bgra32 mip = pixels[offsets[1]];

        Assert.Equal(0, mip.A);
        Assert.InRange(mip.R, 79, 81);
    }
}
