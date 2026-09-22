using OpenIGI.Rendering.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// What a format is, which decides where it may be attached and how it is read.
/// </summary>
public sealed class RhiFormatTests
{
    [Theory]
    [InlineData(RhiTextureFormat.Depth32Float)]
    [InlineData(RhiTextureFormat.Depth24PlusStencil8)]
    [InlineData(RhiTextureFormat.Depth16Unorm)]
    public void Depth_formats_know_they_are_depth(RhiTextureFormat format) => Assert.True(format.IsDepth());

    [Theory]
    [InlineData(RhiTextureFormat.Rgba16Float)]
    [InlineData(RhiTextureFormat.Bgra8Unorm)]
    [InlineData(RhiTextureFormat.R32Uint)]
    [InlineData(RhiTextureFormat.Undefined)]
    public void Colour_formats_are_not_depth(RhiTextureFormat format) => Assert.False(format.IsDepth());

    [Fact]
    public void Only_one_depth_format_carries_stencil()
    {
        Assert.True(RhiTextureFormat.Depth24PlusStencil8.HasStencil());
        Assert.False(RhiTextureFormat.Depth32Float.HasStencil());
        Assert.False(RhiTextureFormat.Depth16Unorm.HasStencil());
    }

    [Fact]
    public void The_srgb_pairs_are_the_only_converting_formats()
    {
        // Colour space is part of the format, not a step applied afterwards: a view created as sRGB has
        // the hardware convert on every read and write. Mixing the pair up is what makes a whole renderer
        // come out uniformly dark or uniformly washed out.
        Assert.True(RhiTextureFormat.Rgba8UnormSrgb.IsSrgb());
        Assert.True(RhiTextureFormat.Bgra8UnormSrgb.IsSrgb());
        Assert.False(RhiTextureFormat.Rgba8Unorm.IsSrgb());
        Assert.False(RhiTextureFormat.Bgra8Unorm.IsSrgb());
    }

    [Fact]
    public void An_unset_format_is_undefined() =>
        Assert.Equal(RhiTextureFormat.Undefined, default(RhiTextureDescriptor).Format);

    [Fact]
    public void An_unset_vertex_format_is_undefined() =>
        Assert.Equal(RhiVertexFormat.Undefined, default(RhiVertexElement).Format);
}
