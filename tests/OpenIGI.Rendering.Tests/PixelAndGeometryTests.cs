using OpenIGI.Core.Graphics;
using Xunit;

namespace OpenIGI.Rendering.Tests;

public sealed class PixelAndGeometryTests
{
    [Fact]
    public void Bgra32_PacksAsTheOriginalDword()
    {
        // The original's in-memory pixel is 0xAARRGGBB, i.e. bytes B, G, R, A.
        Bgra32 pixel = Bgra32.FromRgba(0x11, 0x22, 0x33, 0x44);

        Assert.Equal(0x44112233u, pixel.ToPacked());
        Assert.Equal(0x33, pixel.B);
        Assert.Equal(0x22, pixel.G);
        Assert.Equal(0x11, pixel.R);
        Assert.Equal(0x44, pixel.A);
    }

    [Fact]
    public void Bgra32_RoundTripsThroughItsPackedForm()
    {
        for (uint value = 0; value < 0x1000; value++)
        {
            uint packed = (value * 2654435761u) | 0xFF000000u;
            Assert.Equal(packed, Bgra32.FromPacked(packed).ToPacked());
        }
    }

    [Fact]
    public void Bgra32_IsExactlyFourBytes()
    {
        Assert.Equal(4, System.Runtime.CompilerServices.Unsafe.SizeOf<Bgra32>());
        Assert.Equal(4, Bgra32.Size);
    }

    [Fact]
    public void Vertex2D_MatchesTheStrideTheBackendUploads()
    {
        Assert.Equal(Vertex2D.Size, System.Runtime.CompilerServices.Unsafe.SizeOf<Vertex2D>());
    }

    [Fact]
    public void RectF_FromEdgesAndBackAgree()
    {
        RectF rect = RectF.FromEdges(10.0f, 20.0f, 40.0f, 60.0f);

        Assert.Equal(10.0f, rect.X);
        Assert.Equal(20.0f, rect.Y);
        Assert.Equal(30.0f, rect.Width);
        Assert.Equal(40.0f, rect.Height);
        Assert.Equal(40.0f, rect.Right);
        Assert.Equal(60.0f, rect.Bottom);
    }

    [Fact]
    public void RectF_ContainsIsLeftAndTopInclusive()
    {
        RectF rect = new(0, 0, 10, 10);

        Assert.True(rect.Contains(0, 0));
        Assert.True(rect.Contains(9.99f, 9.99f));
        Assert.False(rect.Contains(10, 5));
        Assert.False(rect.Contains(5, 10));
        Assert.False(rect.Contains(-0.01f, 5));
    }

    [Fact]
    public void Viewport_ReportsTheRightAndBottomEdgesDirect3DWants()
    {
        Viewport viewport = new(16, 32, 320, 240);

        Assert.Equal(336, viewport.Right);
        Assert.Equal(272, viewport.Bottom);
        Assert.Equal(0.0f, viewport.MinDepth);
        Assert.Equal(1.0f, viewport.MaxDepth);
    }

    [Theory]
    [InlineData(128, 64, 7, 2, 1)]
    [InlineData(256, 256, 9, 1, 1)]
    [InlineData(64, 64, 1, 64, 64)]
    public void TextureDescription_HalvesBothAxesWithoutClamping(
        int width,
        int height,
        int mipLevels,
        int expectedLastWidth,
        int expectedLastHeight)
    {
        TextureDescription description = new(width, height, mipLevels, TextureFilter.Linear, TextureAddressMode.Clamp);

        (int w, int h) = description.GetMipSize(mipLevels - 1);

        Assert.Equal(expectedLastWidth, w);
        Assert.Equal(expectedLastHeight, h);
    }

    [Fact]
    public void TextureHandle_NoneIsInvalidAndCompares()
    {
        Assert.False(TextureHandle.None.IsValid);
        Assert.True(new TextureHandle(1).IsValid);
        Assert.Equal(new TextureHandle(7), new TextureHandle(7));
        Assert.NotEqual(new TextureHandle(7), new TextureHandle(8));
    }
}
