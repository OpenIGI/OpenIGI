using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Pins the mip filter used for textures whose channels are numbers rather than colours.
/// </summary>
/// <remarks>
/// The derived detail map carries a normal's x and y, a roughness multiplier and an occlusion term. The
/// picture filter — <c>MipChainBuilder</c> — decodes each channel from the display encoding, averages, and
/// re-encodes, which is right for a photograph of a wall and a systematic bias on any of those four. These
/// tests exist because that difference is invisible: both filters produce a plausible-looking chain, and
/// the one that is wrong merely tilts every surface a little further with each level.
/// </remarks>
public sealed class LinearMipChainTests
{
    /// <summary>The average must be the arithmetic one, with no display encoding anywhere in it.</summary>
    /// <remarks>
    /// <b>The test that matters most here.</b> Black and white average to 128 on this filter. Through a
    /// display decode and re-encode they average to 188, because half the light is most of the way up the
    /// curve — and 188 in the channel holding a normal's x is a surface leaning hard to one side that
    /// nothing in the source asked for.
    /// </remarks>
    [Fact]
    public void AveragesLinearlyRatherThanThroughTheDisplayEncoding()
    {
        Bgra32[] level0 =
        [
            new(0, 0, 0, 0), new(255, 255, 255, 255),
            new(255, 255, 255, 255), new(0, 0, 0, 0),
        ];

        (Bgra32[] pixels, int levels) = LinearMipChain.Build(level0, 2, 2);

        Assert.Equal(2, levels);
        Assert.Equal(new Bgra32(128, 128, 128, 128), pixels[4]);
    }

    /// <summary>Every channel must survive, in the channel it started in.</summary>
    /// <remarks>
    /// <see cref="Bgra32"/>'s constructor takes its channels in memory order, so its first positional
    /// argument is blue while a sampler calls the first channel red. Writing a filter positionally in the
    /// sampler's order silently transposes the normal's x with the roughness, and the level still looks
    /// like a plausible picture.
    /// </remarks>
    [Fact]
    public void KeepsEachChannelWhereItWas()
    {
        Bgra32[] level0 = new Bgra32[4];
        Array.Fill(level0, new Bgra32(b: 10, g: 20, r: 30, a: 40));

        (Bgra32[] pixels, _) = LinearMipChain.Build(level0, 2, 2);

        Assert.Equal(new Bgra32(10, 20, 30, 40), pixels[4]);
    }

    /// <summary>The chain's shape must be the one the device's uploader walks.</summary>
    /// <remarks>
    /// The backend reads each level's size as <c>Math.Max(1, extent >> level)</c> and lays the levels out
    /// back to back in one buffer, so a filter that stopped the chain a level earlier — or that let an
    /// axis reach zero rather than clamping it — would have every level after the mismatch read from the
    /// wrong offset. A non-square, non-power-of-two size is what catches that: both axes run out at
    /// different levels.
    /// </remarks>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(128, 64)]
    [InlineData(48, 12)]
    public void ChainMatchesTheLayoutTheDeviceUploads(int width, int height)
    {
        Bgra32[] level0 = new Bgra32[width * height];
        Array.Fill(level0, new Bgra32(64, 96, 128, 255));

        (Bgra32[] pixels, int levels) = LinearMipChain.Build(level0, width, height);

        Assert.Equal(LinearMipChain.LevelCount(width, height), levels);

        int expected = 0;
        for (int level = 0; level < levels; level++)
        {
            expected += Math.Max(1, width >> level) * Math.Max(1, height >> level);
        }

        Assert.Equal(expected, pixels.Length);
    }

    /// <summary>A map with nothing in it must come back with nothing in it.</summary>
    /// <remarks>
    /// The neutral detail map is (128, 128, 128, 255): a flat normal, a roughness multiplier of one, and
    /// no occlusion. A filter that drifts on a constant input would make a surface with no map and a
    /// surface with a neutral one shade differently at distance, which is the hardest kind of difference
    /// to attribute to anything.
    /// </remarks>
    [Fact]
    public void AConstantMapIsConstantAtEveryLevel()
    {
        Bgra32 neutral = new(128, 128, 128, 255);
        Bgra32[] level0 = new Bgra32[64 * 64];
        Array.Fill(level0, neutral);

        (Bgra32[] pixels, _) = LinearMipChain.Build(level0, 64, 64);

        Assert.All(pixels, pixel => Assert.Equal(neutral, pixel));
    }
}
