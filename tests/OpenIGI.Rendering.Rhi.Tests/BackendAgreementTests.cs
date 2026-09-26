using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// That the two backends understand the same interface, value for value.
/// </summary>
/// <remarks>
/// <para>
/// The interface is only an abstraction if both sides implement all of it. A value one backend maps and
/// the other refuses is a portability hole that no single-backend test can find: each passes its own
/// suite, the picture differs on one machine, and what reports it is a player.
/// </para>
/// <para>
/// So every enumeration is walked and both translations are asked. They are not compared to each other —
/// they produce different native types and there is nothing to compare — but they must agree about
/// which values they accept and which they refuse.
/// </para>
/// </remarks>
public sealed class BackendAgreementTests
{
    [Fact]
    public void Both_backends_map_every_texture_format()
    {
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined)
            {
                continue;
            }

            Assert.True(Accepts(() => WebGpuTranslation.Format(format)), $"WebGPU refuses {format}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.TextureFormat(format)), $"Vulkan refuses {format}.");
        }
    }

    [Fact]
    public void Both_backends_round_trip_every_texture_format()
    {
        // A one-way mapping is where a format becomes its neighbour: the write goes somewhere and the
        // read comes back as something else, and nothing between the two says so.
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined)
            {
                continue;
            }

            Assert.Equal(format, WebGpuTranslation.Format(WebGpuTranslation.Format(format)));
            Assert.Equal(format, VulkanRhiTranslation.TextureFormat(VulkanRhiTranslation.TextureFormat(format)));
        }
    }

    [Fact]
    public void Both_backends_map_every_vertex_format()
    {
        foreach (RhiVertexFormat format in Enum.GetValues<RhiVertexFormat>())
        {
            if (format == RhiVertexFormat.Undefined)
            {
                continue;
            }

            Assert.True(Accepts(() => WebGpuTranslation.VertexFormat(format)), $"WebGPU refuses {format}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.VertexFormat(format)), $"Vulkan refuses {format}.");
        }
    }

    [Fact]
    public void Both_backends_map_every_comparison_blend_and_topology()
    {
        foreach (RhiCompare compare in Enum.GetValues<RhiCompare>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.Compare(compare)), $"WebGPU refuses {compare}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.Compare(compare)), $"Vulkan refuses {compare}.");
        }

        foreach (RhiBlendFactor factor in Enum.GetValues<RhiBlendFactor>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.BlendFactor(factor)), $"WebGPU refuses {factor}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.BlendFactor(factor)), $"Vulkan refuses {factor}.");
        }

        foreach (RhiBlendOperation operation in Enum.GetValues<RhiBlendOperation>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.BlendOperation(operation)), $"WebGPU refuses {operation}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.BlendOperation(operation)), $"Vulkan refuses {operation}.");
        }

        foreach (RhiPrimitiveTopology topology in Enum.GetValues<RhiPrimitiveTopology>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.Topology(topology)), $"WebGPU refuses {topology}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.Topology(topology)), $"Vulkan refuses {topology}.");
        }
    }

    [Fact]
    public void Both_backends_map_every_sampler_and_view_choice()
    {
        foreach (RhiAddressMode mode in Enum.GetValues<RhiAddressMode>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.AddressMode(mode)), $"WebGPU refuses {mode}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.AddressMode(mode)), $"Vulkan refuses {mode}.");
        }

        foreach (RhiTextureViewDimension dimension in Enum.GetValues<RhiTextureViewDimension>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.ViewDimension(dimension)), $"WebGPU refuses {dimension}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.ViewDimension(dimension)), $"Vulkan refuses {dimension}.");
        }

        foreach (RhiCullMode mode in Enum.GetValues<RhiCullMode>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.CullMode(mode)), $"WebGPU refuses {mode}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.CullMode(mode)), $"Vulkan refuses {mode}.");
        }
    }

    [Fact]
    public void Both_backends_agree_that_only_a_volume_is_three_dimensional()
    {
        // The same trap on both sides: an array and a cube are two-dimensional images with layers, and
        // creating one as a volume is accepted and then reads back wrong.
        foreach (RhiTextureViewDimension dimension in Enum.GetValues<RhiTextureViewDimension>())
        {
            bool webgpu = WebGpuTranslation.TextureDimension(dimension)
                == OpenIGI.Rendering.WebGpu.Native.WGPUTextureDimension._3D;

            bool vulkan = VulkanRhiTranslation.TextureDimension(dimension)
                == Silk.NET.Vulkan.ImageType.Type3D;

            Assert.Equal(webgpu, vulkan);
            Assert.Equal(dimension == RhiTextureViewDimension.Texture3D, vulkan);
        }
    }

    [Fact]
    public void Both_backends_refuse_a_value_that_is_not_one()
    {
        // Neither falls back. A backend that quietly mapped an unknown value to something plausible is
        // one that would draw a wrong picture rather than fail, and the two would then differ silently.
        Assert.False(Accepts(() => WebGpuTranslation.Format((RhiTextureFormat)9999)));
        Assert.False(Accepts(() => VulkanRhiTranslation.TextureFormat((RhiTextureFormat)9999)));
        Assert.False(Accepts(() => WebGpuTranslation.Compare((RhiCompare)9999)));
        Assert.False(Accepts(() => VulkanRhiTranslation.Compare((RhiCompare)9999)));
    }

    private static bool Accepts(Action mapping)
    {
        try
        {
            mapping();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
