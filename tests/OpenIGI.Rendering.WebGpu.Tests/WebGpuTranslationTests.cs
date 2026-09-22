using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering.WebGpu.Native;
using Xunit;

namespace OpenIGI.Rendering.WebGpu.Tests;

/// <summary>
/// The renderer's vocabulary in WebGPU's terms.
/// </summary>
/// <remarks>
/// This is where a second backend goes wrong invisibly. A format mapped to its neighbour, a comparison
/// mapped to its opposite, a flag left out of a bitmask: none of them fails, and each produces a picture
/// that is wrong in a way no error message ever mentions. Every mapping is total and every one is
/// checked, including the round trip where there is one.
/// </remarks>
public sealed class WebGpuTranslationTests
{
    [Fact]
    public void Every_texture_format_maps()
    {
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined)
            {
                continue;
            }

            WGPUTextureFormat mapped = WebGpuTranslation.Format(format);

            Assert.NotEqual(WGPUTextureFormat.Undefined, mapped);
            Assert.Equal(format, WebGpuTranslation.Format(mapped));
        }
    }

    [Fact]
    public void An_unnamed_texture_format_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WebGpuTranslation.Format(RhiTextureFormat.Undefined));
        Assert.Throws<ArgumentOutOfRangeException>(() => WebGpuTranslation.Format((RhiTextureFormat)9999));
    }

    [Fact]
    public void A_format_webgpu_names_and_the_renderer_does_not_comes_back_undefined() =>
        Assert.Equal(RhiTextureFormat.Undefined, WebGpuTranslation.Format(WGPUTextureFormat.ASTC12x12UnormSrgb));

    [Fact]
    public void Every_vertex_format_maps()
    {
        foreach (RhiVertexFormat format in Enum.GetValues<RhiVertexFormat>())
        {
            if (format == RhiVertexFormat.Undefined)
            {
                continue;
            }

            WebGpuTranslation.VertexFormat(format);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => WebGpuTranslation.VertexFormat(RhiVertexFormat.Undefined));
    }

    [Fact]
    public void Every_comparison_maps_to_the_one_that_means_the_same()
    {
        // Mapped one at a time rather than by ordinal, because the two enumerations do not agree about
        // where Undefined sits and an off-by-one here inverts every depth test in the renderer.
        Assert.Equal(WGPUCompareFunction.Never, WebGpuTranslation.Compare(RhiCompare.Never));
        Assert.Equal(WGPUCompareFunction.Less, WebGpuTranslation.Compare(RhiCompare.Less));
        Assert.Equal(WGPUCompareFunction.Equal, WebGpuTranslation.Compare(RhiCompare.Equal));
        Assert.Equal(WGPUCompareFunction.LessEqual, WebGpuTranslation.Compare(RhiCompare.LessEqual));
        Assert.Equal(WGPUCompareFunction.Greater, WebGpuTranslation.Compare(RhiCompare.Greater));
        Assert.Equal(WGPUCompareFunction.NotEqual, WebGpuTranslation.Compare(RhiCompare.NotEqual));
        Assert.Equal(WGPUCompareFunction.GreaterEqual, WebGpuTranslation.Compare(RhiCompare.GreaterEqual));
        Assert.Equal(WGPUCompareFunction.Always, WebGpuTranslation.Compare(RhiCompare.Always));
    }

    [Fact]
    public void Every_blend_factor_maps()
    {
        foreach (RhiBlendFactor factor in Enum.GetValues<RhiBlendFactor>())
        {
            Assert.NotEqual(WGPUBlendFactor.Undefined, WebGpuTranslation.BlendFactor(factor));
        }
    }

    [Fact]
    public void Every_blend_operation_maps()
    {
        foreach (RhiBlendOperation operation in Enum.GetValues<RhiBlendOperation>())
        {
            Assert.NotEqual(WGPUBlendOperation.Undefined, WebGpuTranslation.BlendOperation(operation));
        }
    }

    [Fact]
    public void Every_buffer_use_reaches_the_mapped_usage()
    {
        // One at a time, because a flag left out of the mapping is a buffer that cannot be used the way
        // it was created and an error from the driver naming neither.
        foreach (RhiBufferUsage usage in Enum.GetValues<RhiBufferUsage>())
        {
            // RayTracingInput is the one use with nothing on this side to map to, and it is deliberate:
            // a mesh states it without knowing whether it will ever be traced, and a backend that
            // builds no acceleration structures has nothing to place differently. Dropping it is the
            // right answer, so it is named here rather than caught by the rule below.
            if (usage is RhiBufferUsage.None or RhiBufferUsage.RayTracingInput)
            {
                continue;
            }

            Assert.NotEqual(WGPUBufferUsage.None, WebGpuTranslation.BufferUsage(usage));
        }

        Assert.Equal(
            WGPUBufferUsage.None,
            WebGpuTranslation.BufferUsage(RhiBufferUsage.RayTracingInput));

        // And it does not stop the uses alongside it reaching the buffer.
        Assert.True(WebGpuTranslation
            .BufferUsage(RhiBufferUsage.Vertex | RhiBufferUsage.RayTracingInput)
            .HasFlag(WGPUBufferUsage.Vertex));

        // And together, so the mapping accumulates rather than replacing.
        WGPUBufferUsage all = WebGpuTranslation.BufferUsage(
            RhiBufferUsage.Vertex | RhiBufferUsage.CopyDestination | RhiBufferUsage.Storage);

        Assert.True(all.HasFlag(WGPUBufferUsage.Vertex));
        Assert.True(all.HasFlag(WGPUBufferUsage.CopyDst));
        Assert.True(all.HasFlag(WGPUBufferUsage.Storage));
        Assert.False(all.HasFlag(WGPUBufferUsage.Index));
    }

    [Fact]
    public void Every_texture_use_reaches_the_mapped_usage()
    {
        foreach (RhiTextureUsage usage in Enum.GetValues<RhiTextureUsage>())
        {
            if (usage == RhiTextureUsage.None)
            {
                continue;
            }

            Assert.NotEqual(WGPUTextureUsage.None, WebGpuTranslation.TextureUsage(usage));
        }

        WGPUTextureUsage all = WebGpuTranslation.TextureUsage(
            RhiTextureUsage.Sampled | RhiTextureUsage.RenderTarget);

        Assert.True(all.HasFlag(WGPUTextureUsage.TextureBinding));
        Assert.True(all.HasFlag(WGPUTextureUsage.RenderAttachment));
        Assert.False(all.HasFlag(WGPUTextureUsage.StorageBinding));
    }

    [Fact]
    public void Every_stage_reaches_the_mapped_visibility()
    {
        WGPUShaderStage graphics = WebGpuTranslation.ShaderStages(RhiShaderStages.Graphics);

        Assert.True(graphics.HasFlag(WGPUShaderStage.Vertex));
        Assert.True(graphics.HasFlag(WGPUShaderStage.Fragment));
        Assert.False(graphics.HasFlag(WGPUShaderStage.Compute));

        WGPUShaderStage all = WebGpuTranslation.ShaderStages(RhiShaderStages.All);

        Assert.True(all.HasFlag(WGPUShaderStage.Compute));
        Assert.Equal(WGPUShaderStage.None, WebGpuTranslation.ShaderStages(RhiShaderStages.None));
    }

    [Fact]
    public void Every_colour_channel_reaches_the_mapped_mask()
    {
        Assert.Equal(WGPUColorWriteMask.All, WebGpuTranslation.ColorWrite(RhiColorWrite.All));
        Assert.Equal(WGPUColorWriteMask.None, WebGpuTranslation.ColorWrite(RhiColorWrite.None));

        WGPUColorWriteMask colour = WebGpuTranslation.ColorWrite(
            RhiColorWrite.Red | RhiColorWrite.Green | RhiColorWrite.Blue);

        Assert.False(colour.HasFlag(WGPUColorWriteMask.Alpha));
    }

    [Fact]
    public void An_array_is_a_two_dimensional_texture_and_only_a_volume_is_three()
    {
        // A cube and an array are 2D textures with layers; a volume filters between its slices and they
        // do not. Creating one as the other is accepted and reads back wrong.
        Assert.Equal(WGPUTextureDimension._2D, WebGpuTranslation.TextureDimension(RhiTextureViewDimension.Texture2D));
        Assert.Equal(WGPUTextureDimension._2D, WebGpuTranslation.TextureDimension(RhiTextureViewDimension.Texture2DArray));
        Assert.Equal(WGPUTextureDimension._2D, WebGpuTranslation.TextureDimension(RhiTextureViewDimension.Cube));
        Assert.Equal(WGPUTextureDimension._3D, WebGpuTranslation.TextureDimension(RhiTextureViewDimension.Texture3D));

        Assert.Equal(WGPUTextureViewDimension._2DArray, WebGpuTranslation.ViewDimension(RhiTextureViewDimension.Texture2DArray));
        Assert.Equal(WGPUTextureViewDimension.Cube, WebGpuTranslation.ViewDimension(RhiTextureViewDimension.Cube));
    }

    [Fact]
    public void A_mip_filter_is_its_own_enumeration()
    {
        // Two enumerations with the same members and different types. Passing one where the other belongs
        // compiles in C and does not here, which is the whole reason they are separate methods.
        Assert.Equal(WGPUMipmapFilterMode.Linear, WebGpuTranslation.MipmapFilter(RhiFilter.Linear));
        Assert.Equal(WGPUFilterMode.Nearest, WebGpuTranslation.Filter(RhiFilter.Nearest));
    }

    [Theory]
    [InlineData(RhiTextureFormat.Depth32Float, false, WGPUTextureSampleType.Depth)]
    [InlineData(RhiTextureFormat.Depth16Unorm, true, WGPUTextureSampleType.Depth)]
    [InlineData(RhiTextureFormat.R32Uint, false, WGPUTextureSampleType.Uint)]
    [InlineData(RhiTextureFormat.Rgba16Float, false, WGPUTextureSampleType.Float)]
    [InlineData(RhiTextureFormat.R32Float, false, WGPUTextureSampleType.UnfilterableFloat)]
    [InlineData(RhiTextureFormat.R32Float, true, WGPUTextureSampleType.Float)]
    [InlineData(RhiTextureFormat.Rgba32Float, false, WGPUTextureSampleType.UnfilterableFloat)]
    [InlineData(RhiTextureFormat.Rgba32Float, true, WGPUTextureSampleType.Float)]
    public void A_binding_says_whether_its_texture_can_be_filtered(
        RhiTextureFormat format,
        bool float32Filterable,
        WGPUTextureSampleType expected)
    {
        RhiFeatures features = RhiFeatures.None with { Float32Filterable = float32Filterable };

        Assert.Equal(expected, WebGpuTranslation.SampleType(format, features));
    }
}
