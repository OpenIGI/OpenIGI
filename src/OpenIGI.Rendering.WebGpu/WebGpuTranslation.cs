using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// The renderer's vocabulary in WebGPU's own terms.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a total function over the renderer's enumeration, and each throws on a value it
/// does not cover rather than falling back. A fallback here is the worst kind of bug: a format quietly
/// substituted, a comparison quietly inverted, and a picture that is wrong in a way no error ever
/// mentions. The one place a substitution IS made is stated out loud and reported — see
/// <see cref="WebGpuFormatPolicy"/>.
/// </para>
/// <para>
/// Pure and static throughout, so the whole translation is testable without a device, which is where
/// nearly all of the risk in a second backend actually lives.
/// </para>
/// </remarks>
public static class WebGpuTranslation
{
    /// <summary>The WebGPU form of a texture format.</summary>
    /// <param name="format">The renderer's format.</param>
    /// <returns>The WebGPU format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one the renderer defines.</exception>
    public static WGPUTextureFormat Format(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => WGPUTextureFormat.R8Unorm,
        RhiTextureFormat.Rg8Unorm => WGPUTextureFormat.RG8Unorm,
        RhiTextureFormat.Rgba8Unorm => WGPUTextureFormat.RGBA8Unorm,
        RhiTextureFormat.Rgba8UnormSrgb => WGPUTextureFormat.RGBA8UnormSrgb,
        RhiTextureFormat.Bgra8Unorm => WGPUTextureFormat.BGRA8Unorm,
        RhiTextureFormat.Bgra8UnormSrgb => WGPUTextureFormat.BGRA8UnormSrgb,
        RhiTextureFormat.R16Float => WGPUTextureFormat.R16Float,
        RhiTextureFormat.Rg16Float => WGPUTextureFormat.RG16Float,
        RhiTextureFormat.Rgba16Float => WGPUTextureFormat.RGBA16Float,
        RhiTextureFormat.R32Float => WGPUTextureFormat.R32Float,
        RhiTextureFormat.Rg32Float => WGPUTextureFormat.RG32Float,
        RhiTextureFormat.Rgba32Float => WGPUTextureFormat.RGBA32Float,
        RhiTextureFormat.R32Uint => WGPUTextureFormat.R32Uint,
        RhiTextureFormat.Rgb10A2Unorm => WGPUTextureFormat.RGB10A2Unorm,
        RhiTextureFormat.Rg11B10Ufloat => WGPUTextureFormat.RG11B10Ufloat,
        RhiTextureFormat.Depth32Float => WGPUTextureFormat.Depth32Float,
        RhiTextureFormat.Depth24PlusStencil8 => WGPUTextureFormat.Depth24PlusStencil8,
        RhiTextureFormat.Depth16Unorm => WGPUTextureFormat.Depth16Unorm,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a texture format."),
    };

    /// <summary>The renderer's form of a WebGPU texture format, for what the surface reports.</summary>
    /// <param name="format">The WebGPU format.</param>
    /// <returns>The renderer's format, or <see cref="RhiTextureFormat.Undefined"/> for one it has no name for.</returns>
    /// <remarks>
    /// Partial on purpose, and undefined rather than throwing: this reads what an adapter says its
    /// surface prefers, and an adapter may name a format this renderer has no use for. The caller picks
    /// again from what it does understand.
    /// </remarks>
    public static RhiTextureFormat Format(WGPUTextureFormat format) => format switch
    {
        WGPUTextureFormat.R8Unorm => RhiTextureFormat.R8Unorm,
        WGPUTextureFormat.RG8Unorm => RhiTextureFormat.Rg8Unorm,
        WGPUTextureFormat.RGBA8Unorm => RhiTextureFormat.Rgba8Unorm,
        WGPUTextureFormat.RGBA8UnormSrgb => RhiTextureFormat.Rgba8UnormSrgb,
        WGPUTextureFormat.BGRA8Unorm => RhiTextureFormat.Bgra8Unorm,
        WGPUTextureFormat.BGRA8UnormSrgb => RhiTextureFormat.Bgra8UnormSrgb,
        WGPUTextureFormat.R16Float => RhiTextureFormat.R16Float,
        WGPUTextureFormat.RG16Float => RhiTextureFormat.Rg16Float,
        WGPUTextureFormat.RGBA16Float => RhiTextureFormat.Rgba16Float,
        WGPUTextureFormat.R32Float => RhiTextureFormat.R32Float,
        WGPUTextureFormat.RG32Float => RhiTextureFormat.Rg32Float,
        WGPUTextureFormat.RGBA32Float => RhiTextureFormat.Rgba32Float,
        WGPUTextureFormat.R32Uint => RhiTextureFormat.R32Uint,
        WGPUTextureFormat.RGB10A2Unorm => RhiTextureFormat.Rgb10A2Unorm,
        WGPUTextureFormat.RG11B10Ufloat => RhiTextureFormat.Rg11B10Ufloat,
        WGPUTextureFormat.Depth32Float => RhiTextureFormat.Depth32Float,
        WGPUTextureFormat.Depth24PlusStencil8 => RhiTextureFormat.Depth24PlusStencil8,
        WGPUTextureFormat.Depth16Unorm => RhiTextureFormat.Depth16Unorm,
        _ => RhiTextureFormat.Undefined,
    };

    /// <summary>The WebGPU form of a vertex element's format.</summary>
    /// <param name="format">The renderer's format.</param>
    /// <returns>The WebGPU format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one the renderer defines.</exception>
    public static WGPUVertexFormat VertexFormat(RhiVertexFormat format) => format switch
    {
        RhiVertexFormat.Float32 => WGPUVertexFormat.Float32,
        RhiVertexFormat.Float32x2 => WGPUVertexFormat.Float32x2,
        RhiVertexFormat.Float32x3 => WGPUVertexFormat.Float32x3,
        RhiVertexFormat.Float32x4 => WGPUVertexFormat.Float32x4,
        RhiVertexFormat.Unorm8x4 => WGPUVertexFormat.Unorm8x4,
        RhiVertexFormat.Uint8x4 => WGPUVertexFormat.Uint8x4,
        RhiVertexFormat.Uint32 => WGPUVertexFormat.Uint32,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a vertex format."),
    };

    /// <summary>The WebGPU form of an index width.</summary>
    /// <param name="format">The renderer's width.</param>
    /// <returns>The WebGPU width.</returns>
    public static WGPUIndexFormat IndexFormat(RhiIndexFormat format) =>
        format == RhiIndexFormat.Uint16 ? WGPUIndexFormat.Uint16 : WGPUIndexFormat.Uint32;

    /// <summary>The WebGPU form of what a buffer is for.</summary>
    /// <param name="usage">The renderer's usage.</param>
    /// <returns>The WebGPU usage.</returns>
    public static WGPUBufferUsage BufferUsage(RhiBufferUsage usage)
    {
        WGPUBufferUsage mapped = WGPUBufferUsage.None;

        if (usage.HasFlag(RhiBufferUsage.CopySource))
        {
            mapped |= WGPUBufferUsage.CopySrc;
        }

        if (usage.HasFlag(RhiBufferUsage.CopyDestination))
        {
            mapped |= WGPUBufferUsage.CopyDst;
        }

        if (usage.HasFlag(RhiBufferUsage.Vertex))
        {
            mapped |= WGPUBufferUsage.Vertex;
        }

        if (usage.HasFlag(RhiBufferUsage.Index))
        {
            mapped |= WGPUBufferUsage.Index;
        }

        if (usage.HasFlag(RhiBufferUsage.Uniform))
        {
            mapped |= WGPUBufferUsage.Uniform;
        }

        if (usage.HasFlag(RhiBufferUsage.Storage))
        {
            mapped |= WGPUBufferUsage.Storage;
        }

        if (usage.HasFlag(RhiBufferUsage.Indirect))
        {
            mapped |= WGPUBufferUsage.Indirect;
        }

        if (usage.HasFlag(RhiBufferUsage.MapRead))
        {
            mapped |= WGPUBufferUsage.MapRead;
        }

        if (usage.HasFlag(RhiBufferUsage.MapWrite))
        {
            mapped |= WGPUBufferUsage.MapWrite;
        }

        if (usage.HasFlag(RhiBufferUsage.QueryResolve))
        {
            mapped |= WGPUBufferUsage.QueryResolve;
        }

        return mapped;
    }

    /// <summary>The WebGPU form of what a texture is for.</summary>
    /// <param name="usage">The renderer's usage.</param>
    /// <returns>The WebGPU usage.</returns>
    public static WGPUTextureUsage TextureUsage(RhiTextureUsage usage)
    {
        WGPUTextureUsage mapped = WGPUTextureUsage.None;

        if (usage.HasFlag(RhiTextureUsage.CopySource))
        {
            mapped |= WGPUTextureUsage.CopySrc;
        }

        if (usage.HasFlag(RhiTextureUsage.CopyDestination))
        {
            mapped |= WGPUTextureUsage.CopyDst;
        }

        if (usage.HasFlag(RhiTextureUsage.Sampled))
        {
            mapped |= WGPUTextureUsage.TextureBinding;
        }

        if (usage.HasFlag(RhiTextureUsage.Storage))
        {
            mapped |= WGPUTextureUsage.StorageBinding;
        }

        if (usage.HasFlag(RhiTextureUsage.RenderTarget))
        {
            mapped |= WGPUTextureUsage.RenderAttachment;
        }

        return mapped;
    }

    /// <summary>The WebGPU form of which stages see a binding.</summary>
    /// <param name="stages">The renderer's stages.</param>
    /// <returns>The WebGPU stages.</returns>
    public static WGPUShaderStage ShaderStages(RhiShaderStages stages)
    {
        WGPUShaderStage mapped = WGPUShaderStage.None;

        if (stages.HasFlag(RhiShaderStages.Vertex))
        {
            mapped |= WGPUShaderStage.Vertex;
        }

        if (stages.HasFlag(RhiShaderStages.Fragment))
        {
            mapped |= WGPUShaderStage.Fragment;
        }

        if (stages.HasFlag(RhiShaderStages.Compute))
        {
            mapped |= WGPUShaderStage.Compute;
        }

        return mapped;
    }

    /// <summary>The WebGPU form of a depth or stencil comparison.</summary>
    /// <param name="compare">The renderer's comparison.</param>
    /// <returns>The WebGPU comparison.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The comparison is not one the renderer defines.</exception>
    public static WGPUCompareFunction Compare(RhiCompare compare) => compare switch
    {
        RhiCompare.Never => WGPUCompareFunction.Never,
        RhiCompare.Less => WGPUCompareFunction.Less,
        RhiCompare.Equal => WGPUCompareFunction.Equal,
        RhiCompare.LessEqual => WGPUCompareFunction.LessEqual,
        RhiCompare.Greater => WGPUCompareFunction.Greater,
        RhiCompare.NotEqual => WGPUCompareFunction.NotEqual,
        RhiCompare.GreaterEqual => WGPUCompareFunction.GreaterEqual,
        RhiCompare.Always => WGPUCompareFunction.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(compare), compare, "Not a comparison."),
    };

    /// <summary>The WebGPU form of one side of a blend equation.</summary>
    /// <param name="factor">The renderer's factor.</param>
    /// <returns>The WebGPU factor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not one the renderer defines.</exception>
    public static WGPUBlendFactor BlendFactor(RhiBlendFactor factor) => factor switch
    {
        RhiBlendFactor.Zero => WGPUBlendFactor.Zero,
        RhiBlendFactor.One => WGPUBlendFactor.One,
        RhiBlendFactor.Source => WGPUBlendFactor.Src,
        RhiBlendFactor.OneMinusSource => WGPUBlendFactor.OneMinusSrc,
        RhiBlendFactor.SourceAlpha => WGPUBlendFactor.SrcAlpha,
        RhiBlendFactor.OneMinusSourceAlpha => WGPUBlendFactor.OneMinusSrcAlpha,
        RhiBlendFactor.Destination => WGPUBlendFactor.Dst,
        RhiBlendFactor.OneMinusDestination => WGPUBlendFactor.OneMinusDst,
        RhiBlendFactor.DestinationAlpha => WGPUBlendFactor.DstAlpha,
        RhiBlendFactor.OneMinusDestinationAlpha => WGPUBlendFactor.OneMinusDstAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Not a blend factor."),
    };

    /// <summary>The WebGPU form of how the two sides of a blend are combined.</summary>
    /// <param name="operation">The renderer's operation.</param>
    /// <returns>The WebGPU operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operation is not one the renderer defines.</exception>
    public static WGPUBlendOperation BlendOperation(RhiBlendOperation operation) => operation switch
    {
        RhiBlendOperation.Add => WGPUBlendOperation.Add,
        RhiBlendOperation.Subtract => WGPUBlendOperation.Subtract,
        RhiBlendOperation.ReverseSubtract => WGPUBlendOperation.ReverseSubtract,
        RhiBlendOperation.Min => WGPUBlendOperation.Min,
        RhiBlendOperation.Max => WGPUBlendOperation.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not a blend operation."),
    };

    /// <summary>The WebGPU form of which channels a target is written to.</summary>
    /// <param name="write">The renderer's mask.</param>
    /// <returns>The WebGPU mask.</returns>
    public static WGPUColorWriteMask ColorWrite(RhiColorWrite write)
    {
        WGPUColorWriteMask mapped = WGPUColorWriteMask.None;

        if (write.HasFlag(RhiColorWrite.Red))
        {
            mapped |= WGPUColorWriteMask.Red;
        }

        if (write.HasFlag(RhiColorWrite.Green))
        {
            mapped |= WGPUColorWriteMask.Green;
        }

        if (write.HasFlag(RhiColorWrite.Blue))
        {
            mapped |= WGPUColorWriteMask.Blue;
        }

        if (write.HasFlag(RhiColorWrite.Alpha))
        {
            mapped |= WGPUColorWriteMask.Alpha;
        }

        return mapped;
    }

    /// <summary>The WebGPU form of what vertices are assembled into.</summary>
    /// <param name="topology">The renderer's topology.</param>
    /// <returns>The WebGPU topology.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The topology is not one the renderer defines.</exception>
    public static WGPUPrimitiveTopology Topology(RhiPrimitiveTopology topology) => topology switch
    {
        RhiPrimitiveTopology.PointList => WGPUPrimitiveTopology.PointList,
        RhiPrimitiveTopology.LineList => WGPUPrimitiveTopology.LineList,
        RhiPrimitiveTopology.LineStrip => WGPUPrimitiveTopology.LineStrip,
        RhiPrimitiveTopology.TriangleList => WGPUPrimitiveTopology.TriangleList,
        RhiPrimitiveTopology.TriangleStrip => WGPUPrimitiveTopology.TriangleStrip,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Not a topology."),
    };

    /// <summary>The WebGPU form of which face is discarded.</summary>
    /// <param name="mode">The renderer's mode.</param>
    /// <returns>The WebGPU mode.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one the renderer defines.</exception>
    public static WGPUCullMode CullMode(RhiCullMode mode) => mode switch
    {
        RhiCullMode.None => WGPUCullMode.None,
        RhiCullMode.Front => WGPUCullMode.Front,
        RhiCullMode.Back => WGPUCullMode.Back,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a cull mode."),
    };

    /// <summary>The WebGPU form of which winding is the front face.</summary>
    /// <param name="face">The renderer's winding.</param>
    /// <returns>The WebGPU winding.</returns>
    public static WGPUFrontFace FrontFace(RhiFrontFace face) =>
        face == RhiFrontFace.Clockwise ? WGPUFrontFace.CW : WGPUFrontFace.CCW;

    /// <summary>The WebGPU form of whether a vertex buffer advances per vertex or per instance.</summary>
    /// <param name="mode">The renderer's step mode.</param>
    /// <returns>The WebGPU step mode.</returns>
    public static WGPUVertexStepMode StepMode(RhiVertexStepMode mode) =>
        mode == RhiVertexStepMode.Instance ? WGPUVertexStepMode.Instance : WGPUVertexStepMode.Vertex;

    /// <summary>The WebGPU form of a filter.</summary>
    /// <param name="filter">The renderer's filter.</param>
    /// <returns>The WebGPU filter.</returns>
    public static WGPUFilterMode Filter(RhiFilter filter) =>
        filter == RhiFilter.Nearest ? WGPUFilterMode.Nearest : WGPUFilterMode.Linear;

    /// <summary>The WebGPU form of a filter between mip levels, which is its own enumeration.</summary>
    /// <param name="filter">The renderer's filter.</param>
    /// <returns>The WebGPU filter.</returns>
    public static WGPUMipmapFilterMode MipmapFilter(RhiFilter filter) =>
        filter == RhiFilter.Nearest ? WGPUMipmapFilterMode.Nearest : WGPUMipmapFilterMode.Linear;

    /// <summary>The WebGPU form of what a coordinate outside the texture reads.</summary>
    /// <param name="mode">The renderer's mode.</param>
    /// <returns>The WebGPU mode.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one the renderer defines.</exception>
    public static WGPUAddressMode AddressMode(RhiAddressMode mode) => mode switch
    {
        RhiAddressMode.Repeat => WGPUAddressMode.Repeat,
        RhiAddressMode.MirrorRepeat => WGPUAddressMode.MirrorRepeat,
        RhiAddressMode.ClampToEdge => WGPUAddressMode.ClampToEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not an address mode."),
    };

    /// <summary>The WebGPU form of how a shader addresses a texture view.</summary>
    /// <param name="dimension">The renderer's dimension.</param>
    /// <returns>The WebGPU dimension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The dimension is not one the renderer defines.</exception>
    public static WGPUTextureViewDimension ViewDimension(RhiTextureViewDimension dimension) => dimension switch
    {
        RhiTextureViewDimension.Texture2D => WGPUTextureViewDimension._2D,
        RhiTextureViewDimension.Texture2DArray => WGPUTextureViewDimension._2DArray,
        RhiTextureViewDimension.Cube => WGPUTextureViewDimension.Cube,
        RhiTextureViewDimension.Texture3D => WGPUTextureViewDimension._3D,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Not a view dimension."),
    };

    /// <summary>The WebGPU form of a texture's own dimension, which an array is not.</summary>
    /// <param name="dimension">The renderer's dimension.</param>
    /// <returns>The WebGPU dimension.</returns>
    /// <remarks>
    /// An array and a cube are both two-dimensional textures with layers; only a volume is a three-
    /// dimensional one. Creating an array as a volume is accepted and then reads back wrong, because a
    /// volume filters between its slices and an array does not.
    /// </remarks>
    public static WGPUTextureDimension TextureDimension(RhiTextureViewDimension dimension) =>
        dimension == RhiTextureViewDimension.Texture3D ? WGPUTextureDimension._3D : WGPUTextureDimension._2D;

    /// <summary>The WebGPU form of what happens to an attachment when a pass begins.</summary>
    /// <param name="op">The renderer's operation.</param>
    /// <returns>The WebGPU operation.</returns>
    public static WGPULoadOp LoadOp(RhiLoadOp op) =>
        op == RhiLoadOp.Load ? WGPULoadOp.Load : WGPULoadOp.Clear;

    /// <summary>The WebGPU form of what happens to an attachment when a pass ends.</summary>
    /// <param name="op">The renderer's operation.</param>
    /// <returns>The WebGPU operation.</returns>
    public static WGPUStoreOp StoreOp(RhiStoreOp op) =>
        op == RhiStoreOp.Discard ? WGPUStoreOp.Discard : WGPUStoreOp.Store;

    /// <summary>The WebGPU form of what a query set counts.</summary>
    /// <param name="type">The renderer's type.</param>
    /// <returns>The WebGPU type.</returns>
    public static WGPUQueryType QueryType(RhiQueryType type) =>
        type == RhiQueryType.Occlusion ? WGPUQueryType.Occlusion : WGPUQueryType.Timestamp;

    /// <summary>The WebGPU form of how a storage texture binding is accessed.</summary>
    /// <param name="access">The renderer's access.</param>
    /// <returns>The WebGPU access.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The access is not one the renderer defines.</exception>
    public static WGPUStorageTextureAccess StorageAccess(RhiStorageAccess access) => access switch
    {
        RhiStorageAccess.WriteOnly => WGPUStorageTextureAccess.WriteOnly,
        RhiStorageAccess.ReadOnly => WGPUStorageTextureAccess.ReadOnly,
        RhiStorageAccess.ReadWrite => WGPUStorageTextureAccess.ReadWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(access), access, "Not a storage access."),
    };

    /// <summary>How a sampled texture binding reads its texture.</summary>
    /// <param name="declared">What the shader says it reads out of the texture.</param>
    /// <param name="format">The format the binding will be filled with, where one is stated.</param>
    /// <param name="features">What the device offers.</param>
    /// <returns>The sample type to declare.</returns>
    /// <remarks>
    /// Part translation, part decision, and WebGPU forces both: a binding declares what it reads and
    /// whether that can be filtered, and binding a texture that disagrees with either is a validation
    /// failure. What it reads is the shader's own word — Vulkan makes no such distinction, so nothing
    /// else in this renderer would ever have carried it. Whether it can be FILTERED depends on the
    /// adapter: a 32-bit float is filterable only where <c>float32-filterable</c> is offered.
    /// </remarks>
    public static WGPUTextureSampleType SampleType(
        RhiTextureSampleType declared,
        RhiTextureFormat format,
        RhiFeatures features)
    {
        switch (declared)
        {
            case RhiTextureSampleType.Depth:
                return WGPUTextureSampleType.Depth;

            case RhiTextureSampleType.Uint:
                return WGPUTextureSampleType.Uint;

            case RhiTextureSampleType.Sint:
                return WGPUTextureSampleType.Sint;

            default:
                break;
        }

        if (format.IsDepth())
        {
            return WGPUTextureSampleType.Depth;
        }

        if (format is RhiTextureFormat.R32Uint)
        {
            return WGPUTextureSampleType.Uint;
        }

        bool wide = format is RhiTextureFormat.R32Float or RhiTextureFormat.Rg32Float
            or RhiTextureFormat.Rgba32Float;

        return wide && !features.Float32Filterable
            ? WGPUTextureSampleType.UnfilterableFloat
            : WGPUTextureSampleType.Float;
    }
}
