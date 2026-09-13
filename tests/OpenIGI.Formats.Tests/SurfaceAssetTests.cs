using System.Buffers.Binary;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The reader's half of the container contract that <c>tools/texture-forge</c> writes.
/// </summary>
/// <remarks>
/// <para>
/// A replacement asset in the overrides folder is a multi-image container: layer 0 is the picture, and
/// layer 1 — when there is one — is the packed normal, roughness and occlusion map derived from it. The
/// pair travels as one file so the two cannot be installed separately and drift apart.
/// </para>
/// <para>
/// <b>The other half of this is <c>tools/texture-forge/tests/test_tex.py</c></b>, which asserts the same
/// byte offsets from the writer's side. The two programs never run together, so the layout is written out
/// as literals on both sides rather than round-tripped — a file that is wrong in a way both sides agree
/// on still loads, it just draws the wrong thing.
/// </para>
/// </remarks>
public sealed class SurfaceAssetTests
{
    /// <summary>A single-image container is the game's own texture and carries no map.</summary>
    /// <remarks>
    /// The case that must not regress: a level made entirely of the game's own art has to behave exactly
    /// as it did before replacements existed.
    /// </remarks>
    [Fact]
    public void TheGamesOwnTextureCarriesNoMap()
    {
        SurfaceAsset asset = SurfaceAssetReader.Read(SingleImageTexture(2, 2));

        Assert.Equal(2, asset.Diffuse.Width);
        Assert.Null(asset.Detail);
    }

    /// <summary>One layer is a replacement built without maps, which is a complete asset.</summary>
    [Fact]
    public void AReplacementWithOneLayerIsJustAPicture()
    {
        SurfaceAsset asset = SurfaceAssetReader.Read(Container(2, 1, layers: 1));

        Assert.Equal(2, asset.Diffuse.Width);
        Assert.Null(asset.Detail);
    }

    /// <summary>Two layers are the picture and the map derived from it, in that order.</summary>
    /// <remarks>
    /// The order is the contract. Reading them the other way round would put a normal map on the wall and
    /// the wall's colours into the shading, and both are pictures, so nothing would throw.
    /// </remarks>
    [Fact]
    public void AReplacementWithTwoLayersCarriesItsMapSecond()
    {
        SurfaceAsset asset = SurfaceAssetReader.Read(Container(2, 1, layers: 2));

        Assert.NotNull(asset.Detail);
        Assert.Equal(2, asset.Detail!.Width);

        // The fixture paints layer 0 red and layer 1 green, so which is which is unambiguous.
        Assert.Equal(255, asset.Diffuse.GetLevel(0)[0].R);
        Assert.Equal(255, asset.Detail.GetLevel(0)[0].G);
    }

    /// <summary>The header's declared size is what the layers are read at.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 4)]
    [InlineData(64, 64)]
    public void LayersAreReadAtTheDeclaredSize(int width, int height)
    {
        SurfaceAsset asset = SurfaceAssetReader.Read(Container(width, height, layers: 2));

        Assert.Equal(width, asset.Diffuse.Width);
        Assert.Equal(height, asset.Diffuse.Height);
        Assert.Equal(width, asset.Detail!.Width);
        Assert.Equal(height, asset.Detail.Height);
    }

    /// <summary>A truncated container is refused rather than read past its end.</summary>
    [Fact]
    public void ATruncatedContainerIsRefused()
    {
        byte[] whole = Container(4, 4, layers: 2);

        Assert.Throws<TextureFormatException>(
            () => SurfaceAssetReader.Read(whole.AsSpan(0, whole.Length - 16)));
    }

    /// <summary>Bytes that are not a texture at all are refused.</summary>
    [Fact]
    public void SomethingThatIsNotATextureIsRefused()
    {
        Assert.Throws<TextureFormatException>(
            () => SurfaceAssetReader.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    /// <summary>Builds the container the Python writer produces, from the same literal offsets.</summary>
    private static byte[] Container(int width, int height, int layers)
    {
        const int HeaderSize = 52;
        const int LayerSize = 32;
        const int Bgra8 = 3;

        int pitch = width * 4;
        int payload = pitch * height;
        int first = HeaderSize + (LayerSize * layers);

        byte[] data = new byte[first + (payload * layers)];

        "LOOP"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x04), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x12), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x20), (uint)layers);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x28), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x30), Bgra8);

        for (int layer = 0; layer < layers; layer++)
        {
            Span<byte> descriptor = data.AsSpan(HeaderSize + (layer * LayerSize), LayerSize);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor, (uint)(first + (layer * payload)));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[0x04..], Bgra8);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[0x08..], (ushort)pitch);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[0x0A..], (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[0x0C..], (ushort)height);

            // Blue, green, red, alpha in memory order: layer 0 red, every later layer green.
            Span<byte> pixels = data.AsSpan(first + (layer * payload), payload);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i + 0] = 0;
                pixels[i + 1] = layer == 0 ? (byte)0 : (byte)255;
                pixels[i + 2] = layer == 0 ? (byte)255 : (byte)0;
                pixels[i + 3] = 255;
            }
        }

        return data;
    }

    /// <summary>A version 11 single-image texture, which is what the game itself ships.</summary>
    private static byte[] SingleImageTexture(int width, int height)
    {
        const int HeaderSize = 32;
        const int Bgra8 = 3;

        int pitch = width * 4;
        byte[] data = new byte[HeaderSize + (pitch * height)];

        "LOOP"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x04), 11);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x08), Bgra8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x14), 0);      // secondary format
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x16), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x18), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1A), (ushort)width);   // allocated width
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x1E), 4);               // bytes per pixel

        return data;
    }
}
