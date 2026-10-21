using OpenIGI.Rendering.D3D12;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using Xunit;

namespace OpenIGI.Rendering.Rhi.Tests;

/// <summary>
/// That the backends understand the same interface, value for value.
/// </summary>
/// <remarks>
/// <para>
/// The interface is only an abstraction if every side implements all of it. A value one backend maps and
/// the other refuses is a portability hole that no single-backend test can find: each passes its own
/// suite, the picture differs on one machine, and what reports it is a player.
/// </para>
/// <para>
/// So every enumeration is walked and every translation is asked. They are not compared to each other —
/// they produce different native types and there is nothing to compare — but they must agree about
/// which values they accept and which they refuse.
/// </para>
/// </remarks>
public sealed class BackendAgreementTests
{
    [Fact]
    public void Every_backend_maps_every_texture_format()
    {
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined)
            {
                continue;
            }

            Assert.True(Accepts(() => WebGpuTranslation.Format(format)), $"WebGPU refuses {format}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.TextureFormat(format)), $"Vulkan refuses {format}.");
            Assert.True(Accepts(() => D3D12Translation.ViewFormat(format)), $"Direct3D 12 refuses {format}.");
        }
    }

    [Fact]
    public void Vulkan_and_WebGpu_round_trip_every_texture_format()
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
    public void Direct3D_12_gives_every_texture_format_a_view_format_of_its_own()
    {
        // Direct3D 12 has no way back from a DXGI format to compare a round trip with, so the same trap is
        // checked the other way: two formats sharing one view format would be one of them read as the other.
        Dictionary<Silk.NET.DXGI.Format, RhiTextureFormat> seen = [];
        foreach (RhiTextureFormat format in Enum.GetValues<RhiTextureFormat>())
        {
            if (format == RhiTextureFormat.Undefined)
            {
                continue;
            }

            Silk.NET.DXGI.Format view = D3D12Translation.ViewFormat(format);
            Assert.False(seen.TryGetValue(view, out RhiTextureFormat other), $"{format} and {other} are both {view}.");
            seen[view] = format;
        }
    }

    [Fact]
    public void Every_backend_maps_every_vertex_format()
    {
        foreach (RhiVertexFormat format in Enum.GetValues<RhiVertexFormat>())
        {
            if (format == RhiVertexFormat.Undefined)
            {
                continue;
            }

            Assert.True(Accepts(() => WebGpuTranslation.VertexFormat(format)), $"WebGPU refuses {format}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.VertexFormat(format)), $"Vulkan refuses {format}.");
            Assert.True(Accepts(() => D3D12Translation.VertexFormat(format)), $"Direct3D 12 refuses {format}.");
        }
    }

    [Fact]
    public void Every_backend_maps_every_comparison_blend_and_topology()
    {
        foreach (RhiCompare compare in Enum.GetValues<RhiCompare>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.Compare(compare)), $"WebGPU refuses {compare}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.Compare(compare)), $"Vulkan refuses {compare}.");
            Assert.True(Accepts(() => D3D12Translation.Compare(compare)), $"Direct3D 12 refuses {compare}.");
        }

        foreach (RhiBlendFactor factor in Enum.GetValues<RhiBlendFactor>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.BlendFactor(factor)), $"WebGPU refuses {factor}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.BlendFactor(factor)), $"Vulkan refuses {factor}.");
            Assert.True(Accepts(() => D3D12Translation.BlendFactor(factor, alpha: false)), $"Direct3D 12 refuses {factor}.");
        }

        foreach (RhiBlendOperation operation in Enum.GetValues<RhiBlendOperation>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.BlendOperation(operation)), $"WebGPU refuses {operation}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.BlendOperation(operation)), $"Vulkan refuses {operation}.");
            Assert.True(Accepts(() => D3D12Translation.BlendOperation(operation)), $"Direct3D 12 refuses {operation}.");
        }

        foreach (RhiPrimitiveTopology topology in Enum.GetValues<RhiPrimitiveTopology>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.Topology(topology)), $"WebGPU refuses {topology}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.Topology(topology)), $"Vulkan refuses {topology}.");
            Assert.True(Accepts(() => D3D12Translation.Topology(topology)), $"Direct3D 12 refuses {topology}.");
        }
    }

    [Fact]
    public void Every_backend_maps_every_sampler_and_view_choice()
    {
        foreach (RhiAddressMode mode in Enum.GetValues<RhiAddressMode>())
        {
            Assert.True(Accepts(() => WebGpuTranslation.AddressMode(mode)), $"WebGPU refuses {mode}.");
            Assert.True(Accepts(() => VulkanRhiTranslation.AddressMode(mode)), $"Vulkan refuses {mode}.");
            Assert.True(Accepts(() => D3D12Translation.AddressMode(mode)), $"Direct3D 12 refuses {mode}.");
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
            Assert.True(Accepts(() => D3D12Translation.CullMode(mode)), $"Direct3D 12 refuses {mode}.");
        }
    }

    [Fact]
    public void Vulkan_and_WebGpu_agree_that_only_a_volume_is_three_dimensional()
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
    public void Every_backend_refuses_a_value_that_is_not_one()
    {
        // Neither falls back. A backend that quietly mapped an unknown value to something plausible is
        // one that would draw a wrong picture rather than fail, and the two would then differ silently.
        Assert.False(Accepts(() => WebGpuTranslation.Format((RhiTextureFormat)9999)));
        Assert.False(Accepts(() => VulkanRhiTranslation.TextureFormat((RhiTextureFormat)9999)));
        Assert.False(Accepts(() => WebGpuTranslation.Compare((RhiCompare)9999)));
        Assert.False(Accepts(() => VulkanRhiTranslation.Compare((RhiCompare)9999)));
        Assert.False(Accepts(() => D3D12Translation.ViewFormat((RhiTextureFormat)9999)));
        Assert.False(Accepts(() => D3D12Translation.Compare((RhiCompare)9999)));
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
