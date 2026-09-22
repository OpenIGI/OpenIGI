using OpenIGI.Rendering.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// The row alignment a copy between a buffer and a texture has to meet.
/// </summary>
/// <remarks>
/// WebGPU requires a multiple of 256 bytes per row for any copy spanning more than one row, and a
/// readback that ignores it comes back shifted by a few pixels more on every row — a picture that looks
/// sheared rather than one that fails. Vulkan accepts the padding too, so one number serves both and
/// every readback pads.
/// </remarks>
public sealed class RhiCopyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 256)]
    [InlineData(255, 256)]
    [InlineData(256, 256)]
    [InlineData(257, 512)]
    [InlineData(1920 * 4, 7680)]
    [InlineData(1921 * 4, 7936)]
    public void A_row_is_rounded_up_to_the_alignment(int bytesPerRow, int aligned) =>
        Assert.Equal(aligned, RhiBufferTextureCopy.AlignRow(bytesPerRow));

    [Fact]
    public void An_aligned_row_is_a_multiple_of_the_alignment()
    {
        for (int width = 1; width <= 4096; width += 7)
        {
            int aligned = RhiBufferTextureCopy.AlignRow(width * 4);

            Assert.Equal(0, aligned % RhiBufferTextureCopy.RequiredRowAlignment);
            Assert.True(aligned >= width * 4);
            Assert.True(aligned - (width * 4) < RhiBufferTextureCopy.RequiredRowAlignment);
        }
    }

    [Fact]
    public void An_extent_is_one_layer_deep_unless_it_says_otherwise() =>
        Assert.Equal(1, new RhiExtent3D(16, 16).DepthOrArrayLayers);

    [Fact]
    public void A_region_starts_at_the_origin_of_its_first_mip()
    {
        RhiTextureRegion region = new(Texture: null!);

        Assert.Equal(0, region.MipLevel);
        Assert.Equal(new RhiOrigin3D(0, 0, 0), region.Origin);
    }
}
