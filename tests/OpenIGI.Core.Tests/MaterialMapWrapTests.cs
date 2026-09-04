using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Tests that pin the wrapping in <see cref="MaterialMapGenerator"/>, which every stage now carries in
/// its own indices rather than recomputing per sample.
/// </summary>
/// <remarks>
/// <para>
/// The blur is a sliding window, the occlusion sweep reads a wrap-padded copy and the gradient reads
/// shifted rows. All three replaced a per-tap modulo with an index maintained across the loop, and all
/// three are wrong in the same quiet way if that index drifts: the maps still come out full of
/// plausible numbers, and only the texels near an edge are wrong. On a tiling texture that shows up in
/// play as a seam grid over the whole level rather than as anything a caller could catch.
/// </para>
/// <para>
/// Shift invariance is the property that catches it. These textures tile, so rolling the input by any
/// number of columns must roll every derived map by the same amount and change nothing else — which is
/// only true if every wrap lands exactly where a modulo would have put it, at every offset rather than
/// at the one an eyeballed example happens to hit.
/// </para>
/// </remarks>
public sealed class MaterialMapWrapTests
{
    /// <summary>Rolling the input must roll every map, at every offset.</summary>
    [Theory]
    [InlineData(16, 1)]
    [InlineData(16, 7)]
    [InlineData(16, 15)]
    [InlineData(32, 5)]
    [InlineData(32, 16)]
    [InlineData(64, 23)]
    public void RollingTheTextureRollsEveryMap(int size, int shift)
    {
        Bgra32[] original = Speckled(size);
        Bgra32[] rolled = RollColumns(original, size, shift);

        MaterialMapGenerator.Maps a = MaterialMapGenerator.Generate(original, size, size);
        MaterialMapGenerator.Maps b = MaterialMapGenerator.Generate(rolled, size, size);

        AssertRolled(a.Elevation, b.Elevation, size, shift, nameof(a.Elevation));
        AssertRolled(a.NormalX, b.NormalX, size, shift, nameof(a.NormalX));
        AssertRolled(a.NormalY, b.NormalY, size, shift, nameof(a.NormalY));
        AssertRolled(a.Roughness, b.Roughness, size, shift, nameof(a.Roughness));
        AssertRolled(a.Occlusion, b.Occlusion, size, shift, nameof(a.Occlusion));
    }

    /// <summary>A texture narrower than its own blur radius must still derive, and stay in range.</summary>
    /// <remarks>
    /// The relief radius is a fraction of the smaller side and the sweep's reach has a floor of three,
    /// so on the smallest textures the game ships both can cover the whole row and then some. A sliding
    /// window that assumes its span fits inside one period reads off the end there.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void TexturesSmallerThanTheKernelStillDerive(int size)
    {
        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(
            Speckled(size), size, size, MaterialMapGenerator.Options.Default with { Relief = 1f });

        Assert.All(maps.Elevation, value => Assert.InRange(value, 0f, 1f));
        Assert.All(maps.Occlusion, value => Assert.InRange(value, 0f, 1f));
        Assert.All(maps.NormalX, value => Assert.InRange(value, -1f, 1f));
        Assert.All(maps.NormalY, value => Assert.InRange(value, -1f, 1f));
        Assert.All(maps.Roughness, value => Assert.InRange(value, 1f, 2f));
    }

    /// <summary>A non-square, non-power-of-two texture must derive over its whole area.</summary>
    /// <remarks>
    /// The vectorised passes walk whole vectors and finish the remainder scalar-side. A width that is a
    /// multiple of the vector count hides a mishandled remainder completely, and every texture this game
    /// ships is a power of two — so the sizes that would catch it are exactly the ones never exercised
    /// in play.
    /// </remarks>
    [Fact]
    public void OddlyShapedTexturesDeriveEverywhere()
    {
        const int width = 13;
        const int height = 7;

        Bgra32[] pixels = new Bgra32[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            byte level = (byte)((i * 37) % 256);
            pixels[i] = new Bgra32(level, (byte)(255 - level), level, 255);
        }

        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, width, height);

        Assert.Equal(width * height, maps.Elevation.Length);
        Assert.All(maps.Elevation, value => Assert.InRange(value, 0f, 1f));
        Assert.All(maps.Occlusion, value => Assert.InRange(value, 0f, 1f));

        // Not a uniform field: a remainder silently skipped leaves its texels at the zero the array was
        // allocated with, which an in-range assertion alone would pass.
        Assert.Contains(maps.Occlusion, value => value < 0.999f);
    }

    private static Bgra32[] Speckled(int size)
    {
        Bgra32[] pixels = new Bgra32[size * size];
        uint state = 0xC0FFEEu;

        for (int i = 0; i < pixels.Length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            byte level = (byte)((state >> 16) & 0xFF);
            pixels[i] = new Bgra32(level, (byte)(level / 2), (byte)(255 - level), 255);
        }

        return pixels;
    }

    private static Bgra32[] RollColumns(Bgra32[] pixels, int size, int shift)
    {
        Bgra32[] rolled = new Bgra32[pixels.Length];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                rolled[(y * size) + ((x + shift) % size)] = pixels[(y * size) + x];
            }
        }

        return rolled;
    }

    private static void AssertRolled(float[] a, float[] b, int size, int shift, string map)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float expected = a[(y * size) + x];
                float actual = b[(y * size) + ((x + shift) % size)];

                Assert.True(
                    MathF.Abs(expected - actual) < 1e-4f,
                    $"{map} at ({x}, {y}) shifted by {shift}: expected {expected}, got {actual}.");
            }
        }
    }
}
