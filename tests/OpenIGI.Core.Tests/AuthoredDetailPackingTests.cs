using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Pins the byte layout <see cref="MaterialMapGenerator.PackAuthoredDetail"/> writes from glTF's maps, which the G-buffer
/// shader undoes for a surface drawn with its own maps.
/// </summary>
/// <remarks>
/// Literal bytes, as <see cref="MaterialMapPackingContractTests"/> does for the derived map, and for the same reason: a
/// channel in the wrong place still renders as a plausible picture.
/// </remarks>
public sealed class AuthoredDetailPackingTests
{
    /// <summary>
    /// The contract: one glTF normal texel, one metal-roughness texel and the two factors, and what the sampler must read.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>r = the normal's red, as it is</description></item>
    /// <item><description>g = 255 - the normal's green: glTF's +Y is up the image, the shader's tangent frame runs down it</description></item>
    /// <item><description>b = roughness itself (green times the roughness factor), not halved</description></item>
    /// <item><description>a = metalness (blue times the metallic factor)</description></item>
    /// </list>
    /// </remarks>
    public static TheoryData<byte, byte, byte, byte, float, float, byte, byte, byte, byte> Contract =>
        new()
        {
            // A flat normal and a rough, non-metal surface at full factors.
            { 128, 128, 255, 0, 0f, 1f, 128, 127, 255, 0 },

            // The normal's extremes, where a swapped or unflipped channel shows.
            { 255, 128, 255, 0, 0f, 1f, 255, 127, 255, 0 },
            { 128, 255, 255, 0, 0f, 1f, 128, 0, 255, 0 },
            { 128, 0, 255, 0, 0f, 1f, 128, 255, 255, 0 },

            // Full metal, smooth.
            { 128, 128, 0, 255, 1f, 1f, 128, 127, 0, 255 },

            // The factors scale the map.
            { 128, 128, 255, 255, 0.5f, 0.5f, 128, 127, 128, 128 },

            // An arbitrary interior point, to catch a rounding rule that only agrees at the ends.
            { 200, 60, 102, 51, 1f, 0.5f, 200, 195, 51, 51 },
        };

    /// <summary>Every packed texel matches the contract byte for byte.</summary>
    /// <param name="normalRed">The glTF normal map's red.</param>
    /// <param name="normalGreen">The glTF normal map's green.</param>
    /// <param name="roughnessGreen">The metal-roughness map's green, its roughness.</param>
    /// <param name="metalBlue">The metal-roughness map's blue, its metalness.</param>
    /// <param name="metallicFactor">glTF's metallic factor.</param>
    /// <param name="roughnessFactor">glTF's roughness factor.</param>
    /// <param name="red">The byte the sampler must read as red.</param>
    /// <param name="green">The byte the sampler must read as green.</param>
    /// <param name="blue">The byte the sampler must read as blue.</param>
    /// <param name="alpha">The byte the sampler must read as alpha.</param>
    [Theory]
    [MemberData(nameof(Contract))]
    public void PackAuthoredDetailMatchesTheContract(
        byte normalRed,
        byte normalGreen,
        byte roughnessGreen,
        byte metalBlue,
        float metallicFactor,
        float roughnessFactor,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackAuthoredDetail(
            new(new[] { Bgra32.FromRgba(normalRed, normalGreen, 255, 255) }, 1),
            new(new[] { Bgra32.FromRgba(0, roughnessGreen, metalBlue, 255) }, 1),
            metallicFactor,
            roughnessFactor,
            packed,
            1,
            1);

        Assert.Equal(red, packed[0].R);
        Assert.Equal(green, packed[0].G);
        Assert.Equal(blue, packed[0].B);
        Assert.Equal(alpha, packed[0].A);
    }

    /// <summary>With neither map, the normal is flat and the factors stand for the surface, as glTF says.</summary>
    [Fact]
    public void NoMapsLeaveAFlatNormalAndTheFactors()
    {
        Bgra32[] packed = new Bgra32[4];
        MaterialMapGenerator.PackAuthoredDetail(default, default, 0.25f, 0.75f, packed, 2, 2);

        Assert.All(packed, texel =>
        {
            Assert.Equal(128, texel.R);
            Assert.Equal(128, texel.G);
            Assert.Equal(191, texel.B);
            Assert.Equal(64, texel.A);
        });
    }

    /// <summary>Each source is sampled to the packed size, nearest texel, whatever its own size.</summary>
    [Fact]
    public void SourcesOfAnotherSizeAreSampledNearest()
    {
        // A 1x2 metal-roughness map, smooth on top and rough below, packed to 2x4.
        Bgra32[] surface = [Bgra32.FromRgba(0, 0, 0, 255), Bgra32.FromRgba(0, 255, 0, 255)];
        Bgra32[] packed = new Bgra32[8];
        MaterialMapGenerator.PackAuthoredDetail(default, new(surface, 1), 0f, 1f, packed, 2, 4);

        Assert.Equal("0,0,0,0,255,255,255,255", string.Join(',', packed.Select(texel => texel.B)));
    }

    /// <summary>A source whose length is not whole rows of its width is refused, not read out of line.</summary>
    [Fact]
    public void ASourceOfBrokenRowsIsRefused()
    {
        Bgra32[] packed = new Bgra32[1];
        Assert.Throws<ArgumentException>(() =>
            MaterialMapGenerator.PackAuthoredDetail(new(new Bgra32[3], 2), default, 0f, 1f, packed, 1, 1));
    }

    /// <summary>A destination smaller than the size asked for is refused.</summary>
    [Fact]
    public void ADestinationTooSmallIsRefused()
    {
        Bgra32[] packed = new Bgra32[3];
        Assert.Throws<ArgumentException>(() =>
            MaterialMapGenerator.PackAuthoredDetail(default, default, 0f, 1f, packed, 2, 2));
    }

    /// <summary>A size whose pixel count overflows is refused, not wrapped.</summary>
    [Fact]
    public void ASizeThatOverflowsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MaterialMapGenerator.PackAuthoredDetail(default, default, 0f, 1f, [], 65536, 65536));
    }
}
