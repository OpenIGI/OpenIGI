using OpenIGI.Rendering.Rhi;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace OpenIGI.Rendering.D3D12;

/// <summary>The RHI's vocabulary in Direct3D 12's terms: formats, states, filters and the rest.</summary>
internal static class D3D12Translation
{
    /// <summary>The DXGI format a texture view of an RHI format reads and writes through.</summary>
    /// <param name="format">The RHI format.</param>
    /// <returns>The concrete DXGI format.</returns>
    public static Format ViewFormat(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => Format.FormatR8Unorm,
        RhiTextureFormat.Rg8Unorm => Format.FormatR8G8Unorm,
        RhiTextureFormat.Rgba8Unorm => Format.FormatR8G8B8A8Unorm,
        RhiTextureFormat.Rgba8UnormSrgb => Format.FormatR8G8B8A8UnormSrgb,
        RhiTextureFormat.Bgra8Unorm => Format.FormatB8G8R8A8Unorm,
        RhiTextureFormat.Bgra8UnormSrgb => Format.FormatB8G8R8A8UnormSrgb,
        RhiTextureFormat.R16Float => Format.FormatR16Float,
        RhiTextureFormat.Rg16Float => Format.FormatR16G16Float,
        RhiTextureFormat.Rgba16Float => Format.FormatR16G16B16A16Float,
        RhiTextureFormat.R32Float => Format.FormatR32Float,
        RhiTextureFormat.Rg32Float => Format.FormatR32G32Float,
        RhiTextureFormat.Rgba32Float => Format.FormatR32G32B32A32Float,
        RhiTextureFormat.R32Uint => Format.FormatR32Uint,
        RhiTextureFormat.Rgb10A2Unorm => Format.FormatR10G10B10A2Unorm,
        RhiTextureFormat.Rg11B10Ufloat => Format.FormatR11G11B10Float,
        RhiTextureFormat.Depth32Float => Format.FormatD32Float,
        RhiTextureFormat.Depth24PlusStencil8 => Format.FormatD24UnormS8Uint,
        RhiTextureFormat.Depth16Unorm => Format.FormatD16Unorm,
        RhiTextureFormat.Bc1RgbaUnorm => Format.FormatBC1Unorm,
        RhiTextureFormat.Bc1RgbaUnormSrgb => Format.FormatBC1UnormSrgb,
        RhiTextureFormat.Bc7RgbaUnorm => Format.FormatBC7Unorm,
        RhiTextureFormat.Bc7RgbaUnormSrgb => Format.FormatBC7UnormSrgb,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No Direct3D 12 format stands for it."),
    };

    /// <summary>The format a texture's resource is created in.</summary>
    /// <param name="format">The RHI format.</param>
    /// <returns>
    /// The typeless family where a view may read it another way -- the eight-bit colour formats in either
    /// encoding, depth as a sampled float, the block formats -- and the concrete format otherwise.
    /// </returns>
    public static Format ResourceFormat(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb => Format.FormatR8G8B8A8Typeless,
        RhiTextureFormat.Bgra8Unorm or RhiTextureFormat.Bgra8UnormSrgb => Format.FormatB8G8R8A8Typeless,
        RhiTextureFormat.Depth32Float => Format.FormatR32Typeless,
        RhiTextureFormat.Depth24PlusStencil8 => Format.FormatR24G8Typeless,
        RhiTextureFormat.Depth16Unorm => Format.FormatR16Typeless,
        RhiTextureFormat.Bc1RgbaUnorm or RhiTextureFormat.Bc1RgbaUnormSrgb => Format.FormatBC1Typeless,
        RhiTextureFormat.Bc7RgbaUnorm or RhiTextureFormat.Bc7RgbaUnormSrgb => Format.FormatBC7Typeless,
        _ => ViewFormat(format),
    };

    /// <summary>The format a shader reads a texture through: a depth format reads as its colour twin.</summary>
    /// <param name="format">The view's RHI format.</param>
    /// <returns>The shader-resource view's format.</returns>
    public static Format ShaderReadFormat(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.Depth32Float => Format.FormatR32Float,
        RhiTextureFormat.Depth24PlusStencil8 => Format.FormatR24UnormX8Typeless,
        RhiTextureFormat.Depth16Unorm => Format.FormatR16Unorm,
        _ => ViewFormat(format),
    };

    /// <summary>The format a swapchain is created in for a surface format: sRGB is a view, not a buffer.</summary>
    /// <param name="format">The surface format.</param>
    /// <returns>The swapchain buffer format.</returns>
    public static Format SwapchainFormat(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.Bgra8Unorm or RhiTextureFormat.Bgra8UnormSrgb => Format.FormatB8G8R8A8Unorm,
        RhiTextureFormat.Rgba8Unorm or RhiTextureFormat.Rgba8UnormSrgb => Format.FormatR8G8B8A8Unorm,
        RhiTextureFormat.Rgba16Float => Format.FormatR16G16B16A16Float,
        RhiTextureFormat.Rgb10A2Unorm => Format.FormatR10G10B10A2Unorm,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "A swapchain cannot be created in it."),
    };

    /// <summary>The input-assembler format of a vertex attribute.</summary>
    /// <param name="format">The RHI vertex format.</param>
    /// <returns>The DXGI format.</returns>
    public static Format VertexFormat(RhiVertexFormat format) => format switch
    {
        RhiVertexFormat.Float32 => Format.FormatR32Float,
        RhiVertexFormat.Float32x2 => Format.FormatR32G32Float,
        RhiVertexFormat.Float32x3 => Format.FormatR32G32B32Float,
        RhiVertexFormat.Float32x4 => Format.FormatR32G32B32A32Float,
        RhiVertexFormat.Unorm8x4 => Format.FormatR8G8B8A8Unorm,
        RhiVertexFormat.Uint8x4 => Format.FormatR8G8B8A8Uint,
        RhiVertexFormat.Uint32 => Format.FormatR32Uint,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No Direct3D 12 format stands for it."),
    };

    /// <summary>The index format.</summary>
    /// <param name="format">The RHI index format.</param>
    /// <returns>The DXGI format.</returns>
    public static Format IndexFormat(RhiIndexFormat format) =>
        format == RhiIndexFormat.Uint32 ? Format.FormatR32Uint : Format.FormatR16Uint;

    /// <summary>A comparison.</summary>
    /// <param name="compare">The RHI comparison.</param>
    /// <returns>The Direct3D 12 comparison.</returns>
    public static ComparisonFunc Compare(RhiCompare compare) => compare switch
    {
        RhiCompare.Never => ComparisonFunc.Never,
        RhiCompare.Less => ComparisonFunc.Less,
        RhiCompare.Equal => ComparisonFunc.Equal,
        RhiCompare.LessEqual => ComparisonFunc.LessEqual,
        RhiCompare.Greater => ComparisonFunc.Greater,
        RhiCompare.NotEqual => ComparisonFunc.NotEqual,
        RhiCompare.GreaterEqual => ComparisonFunc.GreaterEqual,
        RhiCompare.Always => ComparisonFunc.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(compare), compare, null),
    };

    /// <summary>A blend factor.</summary>
    /// <param name="factor">The RHI factor.</param>
    /// <param name="alpha">Whether it is for the alpha channel, where colour factors read their alpha twins.</param>
    /// <returns>The Direct3D 12 blend.</returns>
    public static Silk.NET.Direct3D12.Blend BlendFactor(RhiBlendFactor factor, bool alpha) => factor switch
    {
        RhiBlendFactor.Zero => Silk.NET.Direct3D12.Blend.Zero,
        RhiBlendFactor.One => Silk.NET.Direct3D12.Blend.One,
        RhiBlendFactor.Source => alpha ? Silk.NET.Direct3D12.Blend.SrcAlpha : Silk.NET.Direct3D12.Blend.SrcColor,
        RhiBlendFactor.OneMinusSource => alpha ? Silk.NET.Direct3D12.Blend.InvSrcAlpha : Silk.NET.Direct3D12.Blend.InvSrcColor,
        RhiBlendFactor.SourceAlpha => Silk.NET.Direct3D12.Blend.SrcAlpha,
        RhiBlendFactor.OneMinusSourceAlpha => Silk.NET.Direct3D12.Blend.InvSrcAlpha,
        RhiBlendFactor.Destination => alpha ? Silk.NET.Direct3D12.Blend.DestAlpha : Silk.NET.Direct3D12.Blend.DestColor,
        RhiBlendFactor.OneMinusDestination => alpha ? Silk.NET.Direct3D12.Blend.InvDestAlpha : Silk.NET.Direct3D12.Blend.InvDestColor,
        RhiBlendFactor.DestinationAlpha => Silk.NET.Direct3D12.Blend.DestAlpha,
        RhiBlendFactor.OneMinusDestinationAlpha => Silk.NET.Direct3D12.Blend.InvDestAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, null),
    };

    /// <summary>A blend operation.</summary>
    /// <param name="operation">The RHI operation.</param>
    /// <returns>The Direct3D 12 operation.</returns>
    public static BlendOp BlendOperation(RhiBlendOperation operation) => operation switch
    {
        RhiBlendOperation.Add => BlendOp.Add,
        RhiBlendOperation.Subtract => BlendOp.Subtract,
        RhiBlendOperation.ReverseSubtract => BlendOp.RevSubtract,
        RhiBlendOperation.Min => BlendOp.Min,
        RhiBlendOperation.Max => BlendOp.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    /// <summary>A cull mode.</summary>
    /// <param name="mode">The RHI cull mode.</param>
    /// <returns>The Direct3D 12 cull mode.</returns>
    public static Silk.NET.Direct3D12.CullMode CullMode(RhiCullMode mode) => mode switch
    {
        RhiCullMode.None => Silk.NET.Direct3D12.CullMode.None,
        RhiCullMode.Front => Silk.NET.Direct3D12.CullMode.Front,
        RhiCullMode.Back => Silk.NET.Direct3D12.CullMode.Back,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>What a pipeline is built to rasterise.</summary>
    /// <param name="topology">The RHI topology.</param>
    /// <returns>The pipeline's topology type.</returns>
    public static PrimitiveTopologyType TopologyType(RhiPrimitiveTopology topology) => topology switch
    {
        RhiPrimitiveTopology.PointList => PrimitiveTopologyType.Point,
        RhiPrimitiveTopology.LineList or RhiPrimitiveTopology.LineStrip => PrimitiveTopologyType.Line,
        _ => PrimitiveTopologyType.Triangle,
    };

    /// <summary>What a draw assembles its vertices into.</summary>
    /// <param name="topology">The RHI topology.</param>
    /// <returns>The input assembler's topology.</returns>
    public static D3DPrimitiveTopology Topology(RhiPrimitiveTopology topology) => topology switch
    {
        RhiPrimitiveTopology.PointList => D3DPrimitiveTopology.D3DPrimitiveTopologyPointlist,
        RhiPrimitiveTopology.LineList => D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist,
        RhiPrimitiveTopology.LineStrip => D3DPrimitiveTopology.D3DPrimitiveTopologyLinestrip,
        RhiPrimitiveTopology.TriangleList => D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist,
        RhiPrimitiveTopology.TriangleStrip => D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglestrip,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, null),
    };

    /// <summary>An addressing mode.</summary>
    /// <param name="mode">The RHI mode.</param>
    /// <returns>The Direct3D 12 mode.</returns>
    public static Silk.NET.Direct3D12.TextureAddressMode AddressMode(RhiAddressMode mode) => mode switch
    {
        RhiAddressMode.Repeat => Silk.NET.Direct3D12.TextureAddressMode.Wrap,
        RhiAddressMode.MirrorRepeat => Silk.NET.Direct3D12.TextureAddressMode.Mirror,
        RhiAddressMode.ClampToEdge => Silk.NET.Direct3D12.TextureAddressMode.Clamp,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>A sampler's filter, from its three filters, anisotropy and comparison.</summary>
    /// <param name="descriptor">The sampler.</param>
    /// <returns>The Direct3D 12 filter.</returns>
    public static Filter SamplerFilter(in RhiSamplerDescriptor descriptor)
    {
        bool comparison = descriptor.Compare is not null;
        if (descriptor.MaxAnisotropy > 1)
        {
            return comparison ? Filter.ComparisonAnisotropic : Filter.Anisotropic;
        }

        // D3D12_ENCODE_BASIC_FILTER: mip in bit 0, mag in bit 2, min in bit 4, comparison at bit 7.
        int bits = (descriptor.MipFilter == RhiFilter.Linear ? 1 : 0)
            | (descriptor.MagFilter == RhiFilter.Linear ? 1 << 2 : 0)
            | (descriptor.MinFilter == RhiFilter.Linear ? 1 << 4 : 0)
            | (comparison ? 1 << 7 : 0);
        return (Filter)bits;
    }

    /// <summary>A resource's flags for the uses a texture was created with.</summary>
    /// <param name="usage">The uses.</param>
    /// <param name="depth">Whether its format is a depth format.</param>
    /// <returns>The flags.</returns>
    public static ResourceFlags TextureFlags(RhiTextureUsage usage, bool depth)
    {
        ResourceFlags flags = ResourceFlags.None;
        if (usage.HasFlag(RhiTextureUsage.RenderTarget))
        {
            flags |= depth ? ResourceFlags.AllowDepthStencil : ResourceFlags.AllowRenderTarget;
        }

        if (usage.HasFlag(RhiTextureUsage.Storage))
        {
            flags |= ResourceFlags.AllowUnorderedAccess;
        }

        return flags;
    }

    /// <summary>The 32-bit word a constant's bits take in the constant buffer HLSL reads it from.</summary>
    /// <param name="constant">The constant.</param>
    /// <returns>Its bits, a boolean as nought or one.</returns>
    public static uint ConstantBits(RhiConstant constant) => constant.Kind == RhiConstantKind.Bool
        ? (constant.Bits != 0 ? 1u : 0u)
        : constant.Bits;

    /// <summary>A copy footprint's width or height: the texels, rounded up to whole blocks for a block format.</summary>
    /// <param name="format">The format.</param>
    /// <param name="texels">The region's width or height in texels.</param>
    /// <returns>The footprint's extent.</returns>
    public static int FootprintExtent(RhiTextureFormat format, int texels) =>
        format.IsBlockCompressed() ? (texels + 3) & ~3 : texels;

    /// <summary>The number of bytes of one row of blocks or texels, and how many rows a region holds.</summary>
    /// <param name="format">The format.</param>
    /// <param name="width">The region's width in texels.</param>
    /// <param name="height">Its height in texels.</param>
    /// <returns>The bytes of one row and the number of rows.</returns>
    public static (int RowBytes, int Rows) RowLayout(RhiTextureFormat format, int width, int height) =>
        format.IsBlockCompressed()
            ? (((width + 3) / 4) * format.BlockBytes(), (height + 3) / 4)
            : (width * format.BytesPerTexel(), height);
}
