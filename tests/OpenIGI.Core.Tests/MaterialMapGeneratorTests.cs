using Xunit;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Core.Tests;

/// <summary>
/// Tests for <see cref="MaterialMapGenerator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The channel-order tests are the ones that earn their keep. Every map here is a texture full of
/// plausible-looking numbers, so packing one into the wrong channel produces a picture rather than a
/// failure — the first build of this put roughness where the normal's x belonged and the level still
/// rendered, only wrongly. A test is the only thing that catches that, because the eye cannot.
/// </para>
/// </remarks>
public sealed class MaterialMapGeneratorTests
{
    /// <summary>Deriving with no options given must derive something.</summary>
    /// <remarks>
    /// A record struct always has an implicit parameterless constructor, and it wins overload
    /// resolution over a primary constructor whose parameters are all optional — so
    /// <c>new Options()</c> is the ZERO instance rather than the declared defaults, and a zero normal
    /// strength derives a perfectly flat map while reporting complete success. That is not a
    /// hypothetical: it is what every terrain ground tile was silently getting, because the terrain is
    /// the one caller that derives in process and passes no options.
    /// </remarks>
    [Fact]
    public void DefaultOptionsActuallyDeriveDetail()
    {
        Assert.True(MaterialMapGenerator.Options.Default.NormalStrength > 0f);
        Assert.True(MaterialMapGenerator.Options.Default.OcclusionDirections > 0);
        Assert.True(MaterialMapGenerator.Options.Default.Relief > 0f);

        const int size = 16;
        Bgra32[] pixels = new Bgra32[size * size];

        for (int i = 0; i < pixels.Length; i++)
        {
            byte level = (byte)((i % size) * 16);
            pixels[i] = new Bgra32(level, level, level, 255);
        }

        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, size, size);

        Assert.Contains(maps.NormalX, x => MathF.Abs(x) > 0.01f);
    }

    /// <summary>A flat texture has no relief, so its normal must point straight out.</summary>
    [Fact]
    public void FlatTextureDerivesFlatNormal()
    {
        Bgra32[] pixels = new Bgra32[16 * 16];
        Array.Fill(pixels, new Bgra32(120, 120, 120, 255));

        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, 16, 16);

        Assert.All(maps.NormalX, x => Assert.Equal(0f, x, 4));
        Assert.All(maps.NormalY, y => Assert.Equal(0f, y, 4));
    }

    /// <summary>A flat texture cannot occlude itself, and cannot be rougher than it was asked to be.</summary>
    [Fact]
    public void FlatTextureIsUnoccludedAndUnroughened()
    {
        Bgra32[] pixels = new Bgra32[16 * 16];
        Array.Fill(pixels, new Bgra32(200, 180, 160, 255));

        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, 16, 16);

        Assert.All(maps.Occlusion, value => Assert.Equal(1f, value, 3));
        Assert.All(maps.Roughness, value => Assert.Equal(1f, value, 3));
    }

    /// <summary>A step in luminance must tilt the normal across it, and only across it.</summary>
    /// <remarks>
    /// The sign matters as much as the magnitude: a normal map read under the wrong handedness lights
    /// every bump from the opposite side, which reads as the surface being inverted rather than as a
    /// bug. Ground that rises to the right tilts its normal to the LEFT, hence the negative x.
    /// </remarks>
    [Fact]
    public void RisingLuminanceTiltsTheNormalAgainstTheSlope()
    {
        const int size = 32;
        Bgra32[] pixels = new Bgra32[size * size];

        // One full sine wave across x. Chosen over a step or a single bright column because it wraps
        // seamlessly -- which everything here assumes -- and because it is wide enough to survive the
        // denoise blur, so what is being tested is the sign of the gradient rather than the filter's
        // impulse response.
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float wave = 0.5f + (0.4f * MathF.Sin(MathF.Tau * x / size));
                byte level = (byte)Math.Clamp((int)MathF.Round(wave * 255f), 0, 255);
                pixels[(y * size) + x] = new Bgra32(level, level, level, 255);
            }
        }

        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, size, size);

        int row = size / 2;

        // sin is steepest and rising at x = 0, and steepest and falling half a period later.
        float climbing = maps.NormalX[row * size];
        float descending = maps.NormalX[(row * size) + (size / 2)];

        // Height rises with x, so the normal leans to -x. Getting this sign wrong lights every bump in
        // the game from the opposite side, which reads as the surface being inverted rather than as a
        // bug -- so it is worth pinning rather than eyeballing.
        Assert.True(climbing < -0.05f, $"expected a leftward tilt on the rising slope, got {climbing}.");
        Assert.True(descending > 0.05f, $"expected a rightward tilt on the falling slope, got {descending}.");
    }

    /// <summary>The packed detail map must put each value in the channel the shader samples it from.</summary>
    /// <remarks>
    /// <b>This is the test that matters most in the file.</b> <see cref="Bgra32"/>'s constructor takes
    /// its channels in memory order, so its first positional argument is blue, while a sampler and every
    /// other packer here call the first channel red. Writing the constructor positionally silently
    /// swapped the normal's x with the roughness, and the game rendered a plausible picture anyway.
    /// </remarks>
    [Fact]
    public void PackedDetailPutsEachValueInTheChannelTheShaderReads()
    {
        MaterialMapGenerator.Maps maps = new(
            Width: 1,
            Height: 1,
            Elevation: [0.5f],
            NormalX: [1f],          // fully +x, so red must saturate
            NormalY: [-1f],         // fully -y, so green must bottom out
            Roughness: [1.5f],      // stored halved, so blue is three quarters
            Occlusion: [0.25f]);

        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackDetail(maps, packed, metallic: 0f);

        Assert.Equal(255, packed[0].R);
        Assert.Equal(0, packed[0].G);
        Assert.Equal(191, packed[0].B);

        // Alpha holds how DIELECTRIC the surface is, so an ordinary non-metal map is opaque -- which is
        // also what makes these files openable in an image viewer.
        Assert.Equal(255, packed[0].A);
    }

    /// <summary>Metallic is stored inverted, so that the ordinary case is opaque.</summary>
    [Fact]
    public void PackedDetailStoresMetallicAsItsComplement()
    {
        MaterialMapGenerator.Maps maps = new(1, 1, [0.5f], [0f], [0f], [1f], [1f]);

        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackDetail(maps, packed, metallic: 1f);

        Assert.Equal(0, packed[0].A);
    }

    /// <summary>The ORM map must follow the arrangement every other tool expects.</summary>
    [Fact]
    public void OcclusionRoughnessMetallicFollowsTheUsualChannelOrder()
    {
        MaterialMapGenerator.Maps maps = new(
            Width: 1,
            Height: 1,
            Elevation: [0.5f],
            NormalX: [0f],
            NormalY: [0f],
            Roughness: [1f],        // halved on the way out, so green is a half
            Occlusion: [1f]);       // red saturates

        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackOcclusionRoughnessMetallic(maps, packed, metallic: 1f);

        Assert.Equal(255, packed[0].R);
        Assert.Equal(128, packed[0].G);
        Assert.Equal(255, packed[0].B);
        Assert.Equal(255, packed[0].A);
    }

    /// <summary>The normal map must be written in the usual tangent-space arrangement.</summary>
    [Fact]
    public void PackedNormalIsRedXGreenYBlueZ()
    {
        MaterialMapGenerator.Maps maps = new(1, 1, [0.5f], [1f], [0f], [1f], [1f]);

        Bgra32[] packed = new Bgra32[1];
        MaterialMapGenerator.PackNormal(maps, packed);

        // Fully +x leaves nothing for z, so blue sits at the middle rather than at the top.
        Assert.Equal(255, packed[0].R);
        Assert.Equal(128, packed[0].G);
        Assert.Equal(128, packed[0].B);
    }

    /// <summary>Derivation must wrap at the edges, because every one of these textures tiles.</summary>
    /// <remarks>
    /// Clamping instead flattens the gradient along all four borders, which shows up in play as a seam
    /// grid over the whole level. A texture that is uniform except for one bright column has the same
    /// relief wherever that column sits, including at the very edge.
    /// </remarks>
    [Fact]
    public void DerivationWrapsAtTheEdges()
    {
        const int size = 16;

        static Bgra32[] WithBrightColumn(int column)
        {
            Bgra32[] pixels = new Bgra32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte level = x == column ? (byte)255 : (byte)40;
                    pixels[(y * size) + x] = new Bgra32(level, level, level, 255);
                }
            }

            return pixels;
        }

        MaterialMapGenerator.Maps middle = MaterialMapGenerator.Generate(WithBrightColumn(8), size, size);
        MaterialMapGenerator.Maps edge = MaterialMapGenerator.Generate(WithBrightColumn(0), size, size);

        // The same feature, shifted. Its neighbour's tilt must be the same in both, which is only true
        // if column zero's left-hand neighbour is column fifteen.
        float atMiddle = middle.NormalX[(8 * size) + 7];
        float atEdge = edge.NormalX[(8 * size) + size - 1];

        Assert.Equal(atMiddle, atEdge, 4);
    }

    /// <summary>A cut-out's transparent region is a hole, and a hole has no relief.</summary>
    [Fact]
    public void TransparentTexelsGetNoRelief()
    {
        const int size = 16;
        Bgra32[] pixels = new Bgra32[size * size];

        for (int i = 0; i < pixels.Length; i++)
        {
            // Wildly varying colour, but entirely transparent: the alpha is what decides.
            pixels[i] = new Bgra32((byte)(i * 17), (byte)(i * 5), (byte)(i * 31), 0);
        }

        MaterialMapGenerator.Maps maps = MaterialMapGenerator.Generate(pixels, size, size);

        Assert.All(maps.Elevation, value => Assert.Equal(0.5f, value, 3));
    }
}
