using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using Silk.NET.Vulkan;
using Xunit;

namespace OpenIGI.Rendering.Vulkan.Tests;

/// <summary>
/// The renderer's vocabulary in Vulkan's terms.
/// </summary>
/// <remarks>
/// The same rule the WebGPU side is held to, for the same reason: a format quietly substituted or a
/// comparison quietly inverted produces a picture that is wrong in a way no error mentions — and worse
/// here, because the two backends would then differ and nothing would say which was right. Every
/// mapping is total, every one throws on a value it does not cover, and the round trips go both ways.
/// </remarks>
public sealed class VulkanRhiTranslationTests
{
    [Fact]
    public void Every_texture_format_maps_and_comes_back()
    {
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined)
            {
                continue;
            }

            Format mapped = VulkanRhiTranslation.TextureFormat(format);

            Assert.NotEqual(Format.Undefined, mapped);
            Assert.Equal(format, VulkanRhiTranslation.TextureFormat(mapped));
        }
    }

    [Fact]
    public void An_unnamed_texture_format_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanRhiTranslation.TextureFormat(RhiTextureFormat.Undefined));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanRhiTranslation.TextureFormat((RhiTextureFormat)9999));
    }

    [Fact]
    public void A_format_vulkan_names_and_the_renderer_does_not_comes_back_undefined() =>
        Assert.Equal(RhiTextureFormat.Undefined, VulkanRhiTranslation.TextureFormat(Format.Astc12x12SrgbBlock));

    [Fact]
    public void The_packed_formats_are_spelled_the_other_way_round()
    {
        // Vulkan names a packed format from the high bits down, so what everything else calls RGB10A2 is
        // A2B10G10R10 here. Reading the name as written and picking the same letters in the same order
        // gives the channels backwards, and a sky comes out blue where it should be orange.
        Assert.Equal(
            Format.A2B10G10R10UnormPack32,
            VulkanRhiTranslation.TextureFormat(RhiTextureFormat.Rgb10A2Unorm));

        Assert.Equal(
            Format.B10G11R11UfloatPack32,
            VulkanRhiTranslation.TextureFormat(RhiTextureFormat.Rg11B10Ufloat));
    }

    [Fact]
    public void Every_vertex_format_maps()
    {
        foreach (RhiVertexFormat format in Enum.GetValues<RhiVertexFormat>())
        {
            if (format == RhiVertexFormat.Undefined)
            {
                continue;
            }

            Assert.NotEqual(Format.Undefined, VulkanRhiTranslation.VertexFormat(format));
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanRhiTranslation.VertexFormat(RhiVertexFormat.Undefined));
    }

    [Fact]
    public void Every_comparison_maps_to_the_one_that_means_the_same()
    {
        Assert.Equal(CompareOp.Never, VulkanRhiTranslation.Compare(RhiCompare.Never));
        Assert.Equal(CompareOp.Less, VulkanRhiTranslation.Compare(RhiCompare.Less));
        Assert.Equal(CompareOp.Equal, VulkanRhiTranslation.Compare(RhiCompare.Equal));
        Assert.Equal(CompareOp.LessOrEqual, VulkanRhiTranslation.Compare(RhiCompare.LessEqual));
        Assert.Equal(CompareOp.Greater, VulkanRhiTranslation.Compare(RhiCompare.Greater));
        Assert.Equal(CompareOp.NotEqual, VulkanRhiTranslation.Compare(RhiCompare.NotEqual));
        Assert.Equal(CompareOp.GreaterOrEqual, VulkanRhiTranslation.Compare(RhiCompare.GreaterEqual));
        Assert.Equal(CompareOp.Always, VulkanRhiTranslation.Compare(RhiCompare.Always));
    }

    [Fact]
    public void Every_blend_factor_and_operation_maps()
    {
        foreach (RhiBlendFactor factor in Enum.GetValues<RhiBlendFactor>())
        {
            VulkanRhiTranslation.BlendFactor(factor);
        }

        foreach (RhiBlendOperation operation in Enum.GetValues<RhiBlendOperation>())
        {
            VulkanRhiTranslation.BlendOperation(operation);
        }
    }

    [Fact]
    public void Every_buffer_use_reaches_the_mapped_usage()
    {
        BufferUsageFlags all = VulkanRhiTranslation.BufferUsage(
            RhiBufferUsage.Vertex | RhiBufferUsage.CopyDestination | RhiBufferUsage.Storage);

        Assert.True(all.HasFlag(BufferUsageFlags.VertexBufferBit));
        Assert.True(all.HasFlag(BufferUsageFlags.TransferDstBit));
        Assert.True(all.HasFlag(BufferUsageFlags.StorageBufferBit));
        Assert.False(all.HasFlag(BufferUsageFlags.IndexBufferBit));
    }

    [Fact]
    public void Resolving_a_query_is_a_transfer_destination()
    {
        // Vulkan has no separate use for it, which is the difference from WebGPU: there a buffer has to
        // be created for it outright, and here it is an ordinary copy target.
        Assert.True(VulkanRhiTranslation
            .BufferUsage(RhiBufferUsage.QueryResolve)
            .HasFlag(BufferUsageFlags.TransferDstBit));
    }

    [Fact]
    public void Mapping_is_a_property_of_the_memory_and_not_of_the_buffer()
    {
        // Neither map use adds a usage flag, because Vulkan decides it by which memory the buffer is
        // bound to. What they do is choose host-visible memory, which is a question asked separately.
        Assert.Equal(BufferUsageFlags.None, VulkanRhiTranslation.BufferUsage(RhiBufferUsage.MapRead));
        Assert.True(VulkanRhiTranslation.NeedsHostVisibleMemory(RhiBufferUsage.MapRead));
        Assert.True(VulkanRhiTranslation.NeedsHostVisibleMemory(RhiBufferUsage.MapWrite));
        Assert.False(VulkanRhiTranslation.NeedsHostVisibleMemory(RhiBufferUsage.Storage));
    }

    [Fact]
    public void A_render_target_is_a_colour_or_a_depth_attachment_by_its_format()
    {
        // Naming the wrong one is a validation error rather than something ignored, so it is derived
        // from the format rather than passed in beside it.
        ImageUsageFlags colour = VulkanRhiTranslation.TextureUsage(RhiTextureUsage.RenderTarget, depth: false);
        ImageUsageFlags depth = VulkanRhiTranslation.TextureUsage(RhiTextureUsage.RenderTarget, depth: true);

        Assert.True(colour.HasFlag(ImageUsageFlags.ColorAttachmentBit));
        Assert.False(colour.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit));
        Assert.True(depth.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit));
        Assert.False(depth.HasFlag(ImageUsageFlags.ColorAttachmentBit));
    }

    [Fact]
    public void A_texture_and_its_sampler_are_two_descriptors()
    {
        // Vulkan would allow the combined form and WGSL has no word for it, so the shared shape is the
        // split one. This backend takes the cost of a second descriptor rather than of a second layout.
        Assert.Equal(DescriptorType.SampledImage, VulkanRhiTranslation.BindingType(RhiBindingType.SampledTexture));
        Assert.Equal(DescriptorType.Sampler, VulkanRhiTranslation.BindingType(RhiBindingType.Sampler));
        Assert.Equal(DescriptorType.Sampler, VulkanRhiTranslation.BindingType(RhiBindingType.ComparisonSampler));
    }

    [Fact]
    public void Only_a_buffer_binding_may_have_a_dynamic_offset()
    {
        Assert.Equal(
            DescriptorType.UniformBufferDynamic,
            VulkanRhiTranslation.BindingType(RhiBindingType.UniformBuffer, dynamic: true));

        Assert.Equal(
            DescriptorType.StorageBufferDynamic,
            VulkanRhiTranslation.BindingType(RhiBindingType.StorageBuffer, dynamic: true));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanRhiTranslation.BindingType(RhiBindingType.SampledTexture, dynamic: true));
    }

    [Fact]
    public void An_array_is_a_two_dimensional_image_and_only_a_volume_is_three()
    {
        Assert.Equal(ImageType.Type2D, VulkanRhiTranslation.TextureDimension(RhiTextureViewDimension.Texture2D));
        Assert.Equal(ImageType.Type2D, VulkanRhiTranslation.TextureDimension(RhiTextureViewDimension.Texture2DArray));
        Assert.Equal(ImageType.Type2D, VulkanRhiTranslation.TextureDimension(RhiTextureViewDimension.Cube));
        Assert.Equal(ImageType.Type3D, VulkanRhiTranslation.TextureDimension(RhiTextureViewDimension.Texture3D));

        Assert.Equal(ImageViewType.Type2DArray, VulkanRhiTranslation.ViewDimension(RhiTextureViewDimension.Texture2DArray));
        Assert.Equal(ImageViewType.TypeCube, VulkanRhiTranslation.ViewDimension(RhiTextureViewDimension.Cube));
    }

    [Fact]
    public void A_formats_aspects_come_from_the_format()
    {
        Assert.Equal(ImageAspectFlags.ColorBit, VulkanRhiTranslation.Aspects(RhiTextureFormat.Rgba16Float));
        Assert.Equal(ImageAspectFlags.DepthBit, VulkanRhiTranslation.Aspects(RhiTextureFormat.Depth32Float));
        Assert.Equal(
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            VulkanRhiTranslation.Aspects(RhiTextureFormat.Depth24PlusStencil8));
    }

    [Fact]
    public void Discarding_an_attachment_is_not_storing_it()
    {
        // A tiler is entitled to take this at its word, and on a desktop driver it is what lets a depth
        // buffer never reach memory at all.
        Assert.Equal(AttachmentStoreOp.DontCare, VulkanRhiTranslation.StoreOp(RhiStoreOp.Discard));
        Assert.Equal(AttachmentStoreOp.Store, VulkanRhiTranslation.StoreOp(RhiStoreOp.Store));
        Assert.Equal(AttachmentLoadOp.Clear, VulkanRhiTranslation.LoadOp(RhiLoadOp.Clear));
        Assert.Equal(AttachmentLoadOp.Load, VulkanRhiTranslation.LoadOp(RhiLoadOp.Load));
    }
}
