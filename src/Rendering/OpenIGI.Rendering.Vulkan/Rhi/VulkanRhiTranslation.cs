using OpenIGI.Rendering.Rhi;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// The renderer's vocabulary in Vulkan's own terms.
/// </summary>
/// <remarks>
/// <para>
/// The twin of the WebGPU backend's translation, and written to the same rule: every mapping is total
/// over the renderer's enumeration and throws on a value it does not cover rather than falling back. A
/// silent substitution is the worst kind of bug a backend can have — a format quietly changed, a
/// comparison quietly inverted — because the picture is wrong in a way no error ever mentions, and
/// because the two backends then differ for a reason nothing reports.
/// </para>
/// <para>
/// Pure and static throughout, so it is checked without a device. That matters more here than it looks:
/// these are the values that decide whether the Vulkan and WebGPU pictures agree, and they can be held
/// against each other in a test rather than against a screenshot.
/// </para>
/// </remarks>
public static class VulkanRhiTranslation
{
    /// <summary>The Vulkan form of a texture format.</summary>
    /// <param name="format">The renderer's format.</param>
    /// <returns>The Vulkan format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one the renderer defines.</exception>
    public static Format TextureFormat(RhiTextureFormat format) => format switch
    {
        RhiTextureFormat.R8Unorm => Format.R8Unorm,
        RhiTextureFormat.Rg8Unorm => Format.R8G8Unorm,
        RhiTextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        RhiTextureFormat.Rgba8UnormSrgb => Format.R8G8B8A8Srgb,
        RhiTextureFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
        RhiTextureFormat.Bgra8UnormSrgb => Format.B8G8R8A8Srgb,
        RhiTextureFormat.R16Float => Format.R16Sfloat,
        RhiTextureFormat.Rg16Float => Format.R16G16Sfloat,
        RhiTextureFormat.Rgba16Float => Format.R16G16B16A16Sfloat,
        RhiTextureFormat.R32Float => Format.R32Sfloat,
        RhiTextureFormat.Rg32Float => Format.R32G32Sfloat,
        RhiTextureFormat.Rgba32Float => Format.R32G32B32A32Sfloat,
        RhiTextureFormat.R32Uint => Format.R32Uint,

        // Packed, and packed the other way round from how the name reads: the alpha pair is in the high
        // bits, so Vulkan spells it A2B10G10R10 for what everything else calls RGB10A2.
        RhiTextureFormat.Rgb10A2Unorm => Format.A2B10G10R10UnormPack32,
        RhiTextureFormat.Rg11B10Ufloat => Format.B10G11R11UfloatPack32,
        RhiTextureFormat.Depth32Float => Format.D32Sfloat,
        RhiTextureFormat.Depth24PlusStencil8 => Format.D24UnormS8Uint,
        RhiTextureFormat.Depth16Unorm => Format.D16Unorm,
        RhiTextureFormat.Bc1RgbaUnorm => Format.BC1RgbaUnormBlock,
        RhiTextureFormat.Bc1RgbaUnormSrgb => Format.BC1RgbaSrgbBlock,
        RhiTextureFormat.Bc7RgbaUnorm => Format.BC7UnormBlock,
        RhiTextureFormat.Bc7RgbaUnormSrgb => Format.BC7SrgbBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a texture format."),
    };

    /// <summary>The renderer's form of a Vulkan format, for what a surface reports.</summary>
    /// <param name="format">The Vulkan format.</param>
    /// <returns>The renderer's format, or <see cref="RhiTextureFormat.Undefined"/> for one it has no name for.</returns>
    /// <remarks>
    /// Partial on purpose: this reads what a driver says its surface supports, and a driver lists formats
    /// this renderer has no use for. The caller picks again from what it does understand.
    /// </remarks>
    public static RhiTextureFormat TextureFormat(Format format) => format switch
    {
        Format.R8Unorm => RhiTextureFormat.R8Unorm,
        Format.R8G8Unorm => RhiTextureFormat.Rg8Unorm,
        Format.R8G8B8A8Unorm => RhiTextureFormat.Rgba8Unorm,
        Format.R8G8B8A8Srgb => RhiTextureFormat.Rgba8UnormSrgb,
        Format.B8G8R8A8Unorm => RhiTextureFormat.Bgra8Unorm,
        Format.B8G8R8A8Srgb => RhiTextureFormat.Bgra8UnormSrgb,
        Format.R16Sfloat => RhiTextureFormat.R16Float,
        Format.R16G16Sfloat => RhiTextureFormat.Rg16Float,
        Format.R16G16B16A16Sfloat => RhiTextureFormat.Rgba16Float,
        Format.R32Sfloat => RhiTextureFormat.R32Float,
        Format.R32G32Sfloat => RhiTextureFormat.Rg32Float,
        Format.R32G32B32A32Sfloat => RhiTextureFormat.Rgba32Float,
        Format.R32Uint => RhiTextureFormat.R32Uint,
        Format.A2B10G10R10UnormPack32 => RhiTextureFormat.Rgb10A2Unorm,
        Format.B10G11R11UfloatPack32 => RhiTextureFormat.Rg11B10Ufloat,
        Format.D32Sfloat => RhiTextureFormat.Depth32Float,
        Format.D24UnormS8Uint => RhiTextureFormat.Depth24PlusStencil8,
        Format.D16Unorm => RhiTextureFormat.Depth16Unorm,
        Format.BC1RgbaUnormBlock => RhiTextureFormat.Bc1RgbaUnorm,
        Format.BC1RgbaSrgbBlock => RhiTextureFormat.Bc1RgbaUnormSrgb,
        Format.BC7UnormBlock => RhiTextureFormat.Bc7RgbaUnorm,
        Format.BC7SrgbBlock => RhiTextureFormat.Bc7RgbaUnormSrgb,
        _ => RhiTextureFormat.Undefined,
    };

    /// <summary>The Vulkan form of a vertex element's format.</summary>
    /// <param name="format">The renderer's format.</param>
    /// <returns>The Vulkan format.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one the renderer defines.</exception>
    public static Format VertexFormat(RhiVertexFormat format) => format switch
    {
        RhiVertexFormat.Float32 => Format.R32Sfloat,
        RhiVertexFormat.Float32x2 => Format.R32G32Sfloat,
        RhiVertexFormat.Float32x3 => Format.R32G32B32Sfloat,
        RhiVertexFormat.Float32x4 => Format.R32G32B32A32Sfloat,
        RhiVertexFormat.Unorm8x4 => Format.R8G8B8A8Unorm,
        RhiVertexFormat.Uint8x4 => Format.R8G8B8A8Uint,
        RhiVertexFormat.Uint32 => Format.R32Uint,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a vertex format."),
    };

    /// <summary>The Vulkan form of an index width.</summary>
    /// <param name="format">The renderer's width.</param>
    /// <returns>The Vulkan width.</returns>
    public static IndexType IndexFormat(RhiIndexFormat format) =>
        format == RhiIndexFormat.Uint16 ? IndexType.Uint16 : IndexType.Uint32;

    /// <summary>The Vulkan form of what a buffer is for.</summary>
    /// <param name="usage">The renderer's usage.</param>
    /// <returns>The Vulkan usage.</returns>
    /// <remarks>
    /// Mapping is not a usage flag in Vulkan — it is a property of the memory a buffer is bound to — so
    /// the two map-read and map-write uses add nothing here and decide where the allocation goes
    /// instead. A query resolve is an ordinary transfer destination.
    /// </remarks>
    public static BufferUsageFlags BufferUsage(RhiBufferUsage usage)
    {
        BufferUsageFlags mapped = BufferUsageFlags.None;

        if (usage.HasFlag(RhiBufferUsage.CopySource))
        {
            mapped |= BufferUsageFlags.TransferSrcBit;
        }

        if (usage.HasFlag(RhiBufferUsage.CopyDestination) || usage.HasFlag(RhiBufferUsage.QueryResolve))
        {
            mapped |= BufferUsageFlags.TransferDstBit;
        }

        if (usage.HasFlag(RhiBufferUsage.Vertex))
        {
            mapped |= BufferUsageFlags.VertexBufferBit;
        }

        if (usage.HasFlag(RhiBufferUsage.Index))
        {
            mapped |= BufferUsageFlags.IndexBufferBit;
        }

        if (usage.HasFlag(RhiBufferUsage.Uniform))
        {
            mapped |= BufferUsageFlags.UniformBufferBit;
        }

        if (usage.HasFlag(RhiBufferUsage.Storage))
        {
            mapped |= BufferUsageFlags.StorageBufferBit;
        }

        if (usage.HasFlag(RhiBufferUsage.Indirect))
        {
            mapped |= BufferUsageFlags.IndirectBufferBit;
        }

        if (usage.HasFlag(RhiBufferUsage.RayTracingInput))
        {
            // A build reads geometry through the buffer's device address rather than through a binding,
            // so the address has to be askable for; the storage use is what the build's own shaders need.
            mapped |= BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr
                | BufferUsageFlags.ShaderDeviceAddressBit
                | BufferUsageFlags.StorageBufferBit;
        }

        return mapped;
    }

    /// <summary>Whether a buffer's memory has to be visible to the host.</summary>
    /// <param name="usage">The renderer's usage.</param>
    /// <returns><see langword="true"/> when it will be mapped.</returns>
    public static bool NeedsHostVisibleMemory(RhiBufferUsage usage) =>
        usage.HasFlag(RhiBufferUsage.MapRead) || usage.HasFlag(RhiBufferUsage.MapWrite);

    /// <summary>The Vulkan form of what a texture is for.</summary>
    /// <param name="usage">The renderer's usage.</param>
    /// <param name="depth">Whether the format holds depth, which decides which attachment it is.</param>
    /// <returns>The Vulkan usage.</returns>
    public static ImageUsageFlags TextureUsage(RhiTextureUsage usage, bool depth)
    {
        ImageUsageFlags mapped = ImageUsageFlags.None;

        if (usage.HasFlag(RhiTextureUsage.CopySource))
        {
            mapped |= ImageUsageFlags.TransferSrcBit;
        }

        if (usage.HasFlag(RhiTextureUsage.CopyDestination))
        {
            mapped |= ImageUsageFlags.TransferDstBit;
        }

        if (usage.HasFlag(RhiTextureUsage.Sampled))
        {
            mapped |= ImageUsageFlags.SampledBit;
        }

        if (usage.HasFlag(RhiTextureUsage.Storage))
        {
            mapped |= ImageUsageFlags.StorageBit;
        }

        if (usage.HasFlag(RhiTextureUsage.RenderTarget))
        {
            mapped |= depth
                ? ImageUsageFlags.DepthStencilAttachmentBit
                : ImageUsageFlags.ColorAttachmentBit;
        }

        return mapped;
    }

    /// <summary>The Vulkan form of which stages see a binding.</summary>
    /// <param name="stages">The renderer's stages.</param>
    /// <returns>The Vulkan stages.</returns>
    public static ShaderStageFlags ShaderStages(RhiShaderStages stages)
    {
        ShaderStageFlags mapped = ShaderStageFlags.None;

        if (stages.HasFlag(RhiShaderStages.Vertex))
        {
            mapped |= ShaderStageFlags.VertexBit;
        }

        if (stages.HasFlag(RhiShaderStages.Fragment))
        {
            mapped |= ShaderStageFlags.FragmentBit;
        }

        if (stages.HasFlag(RhiShaderStages.Compute))
        {
            mapped |= ShaderStageFlags.ComputeBit;
        }

        return mapped;
    }

    /// <summary>The Vulkan descriptor a binding needs.</summary>
    /// <param name="type">What the slot holds.</param>
    /// <returns>The descriptor type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The type is not one the renderer defines.</exception>
    /// <remarks>
    /// A texture and its sampler are two descriptors, never a combined one. Vulkan would allow the
    /// combined form and WGSL has no word for it, so the shared shape is the split one and this backend
    /// takes the small cost of a second descriptor rather than the large cost of a second layout.
    /// </remarks>
    public static DescriptorType BindingType(RhiBindingType type) => type switch
    {
        RhiBindingType.UniformBuffer => DescriptorType.UniformBuffer,
        RhiBindingType.ReadOnlyStorageBuffer or RhiBindingType.StorageBuffer => DescriptorType.StorageBuffer,
        RhiBindingType.SampledTexture => DescriptorType.SampledImage,
        RhiBindingType.StorageTexture => DescriptorType.StorageImage,
        RhiBindingType.Sampler or RhiBindingType.ComparisonSampler => DescriptorType.Sampler,
        RhiBindingType.AccelerationStructure => DescriptorType.AccelerationStructureKhr,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a binding type."),
    };

    /// <summary>Whether a binding's offset is supplied when it is bound rather than when it is made.</summary>
    /// <param name="type">What the slot holds.</param>
    /// <param name="dynamic">Whether the layout asked for a dynamic offset.</param>
    /// <returns>The descriptor type, dynamic where one was asked for.</returns>
    public static DescriptorType BindingType(RhiBindingType type, bool dynamic)
    {
        if (!dynamic)
        {
            return BindingType(type);
        }

        return type switch
        {
            RhiBindingType.UniformBuffer => DescriptorType.UniformBufferDynamic,
            RhiBindingType.ReadOnlyStorageBuffer or RhiBindingType.StorageBuffer =>
                DescriptorType.StorageBufferDynamic,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type), type, "Only a buffer binding may have a dynamic offset."),
        };
    }

    /// <summary>The Vulkan form of a depth or stencil comparison.</summary>
    /// <param name="compare">The renderer's comparison.</param>
    /// <returns>The Vulkan comparison.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The comparison is not one the renderer defines.</exception>
    public static CompareOp Compare(RhiCompare compare) => compare switch
    {
        RhiCompare.Never => CompareOp.Never,
        RhiCompare.Less => CompareOp.Less,
        RhiCompare.Equal => CompareOp.Equal,
        RhiCompare.LessEqual => CompareOp.LessOrEqual,
        RhiCompare.Greater => CompareOp.Greater,
        RhiCompare.NotEqual => CompareOp.NotEqual,
        RhiCompare.GreaterEqual => CompareOp.GreaterOrEqual,
        RhiCompare.Always => CompareOp.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(compare), compare, "Not a comparison."),
    };

    /// <summary>The Vulkan form of one side of a blend equation.</summary>
    /// <param name="factor">The renderer's factor.</param>
    /// <returns>The Vulkan factor.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not one the renderer defines.</exception>
    public static BlendFactor BlendFactor(RhiBlendFactor factor) => factor switch
    {
        RhiBlendFactor.Zero => Silk.NET.Vulkan.BlendFactor.Zero,
        RhiBlendFactor.One => Silk.NET.Vulkan.BlendFactor.One,
        RhiBlendFactor.Source => Silk.NET.Vulkan.BlendFactor.SrcColor,
        RhiBlendFactor.OneMinusSource => Silk.NET.Vulkan.BlendFactor.OneMinusSrcColor,
        RhiBlendFactor.SourceAlpha => Silk.NET.Vulkan.BlendFactor.SrcAlpha,
        RhiBlendFactor.OneMinusSourceAlpha => Silk.NET.Vulkan.BlendFactor.OneMinusSrcAlpha,
        RhiBlendFactor.Destination => Silk.NET.Vulkan.BlendFactor.DstColor,
        RhiBlendFactor.OneMinusDestination => Silk.NET.Vulkan.BlendFactor.OneMinusDstColor,
        RhiBlendFactor.DestinationAlpha => Silk.NET.Vulkan.BlendFactor.DstAlpha,
        RhiBlendFactor.OneMinusDestinationAlpha => Silk.NET.Vulkan.BlendFactor.OneMinusDstAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Not a blend factor."),
    };

    /// <summary>The Vulkan form of how the two sides of a blend are combined.</summary>
    /// <param name="operation">The renderer's operation.</param>
    /// <returns>The Vulkan operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operation is not one the renderer defines.</exception>
    public static BlendOp BlendOperation(RhiBlendOperation operation) => operation switch
    {
        RhiBlendOperation.Add => BlendOp.Add,
        RhiBlendOperation.Subtract => BlendOp.Subtract,
        RhiBlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
        RhiBlendOperation.Min => BlendOp.Min,
        RhiBlendOperation.Max => BlendOp.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not a blend operation."),
    };

    /// <summary>The Vulkan form of which channels a target is written to.</summary>
    /// <param name="write">The renderer's mask.</param>
    /// <returns>The Vulkan mask.</returns>
    public static ColorComponentFlags ColorWrite(RhiColorWrite write)
    {
        ColorComponentFlags mapped = 0;

        if (write.HasFlag(RhiColorWrite.Red))
        {
            mapped |= ColorComponentFlags.RBit;
        }

        if (write.HasFlag(RhiColorWrite.Green))
        {
            mapped |= ColorComponentFlags.GBit;
        }

        if (write.HasFlag(RhiColorWrite.Blue))
        {
            mapped |= ColorComponentFlags.BBit;
        }

        if (write.HasFlag(RhiColorWrite.Alpha))
        {
            mapped |= ColorComponentFlags.ABit;
        }

        return mapped;
    }

    /// <summary>The Vulkan form of what vertices are assembled into.</summary>
    /// <param name="topology">The renderer's topology.</param>
    /// <returns>The Vulkan topology.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The topology is not one the renderer defines.</exception>
    public static PrimitiveTopology Topology(RhiPrimitiveTopology topology) => topology switch
    {
        RhiPrimitiveTopology.PointList => PrimitiveTopology.PointList,
        RhiPrimitiveTopology.LineList => PrimitiveTopology.LineList,
        RhiPrimitiveTopology.LineStrip => PrimitiveTopology.LineStrip,
        RhiPrimitiveTopology.TriangleList => PrimitiveTopology.TriangleList,
        RhiPrimitiveTopology.TriangleStrip => PrimitiveTopology.TriangleStrip,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Not a topology."),
    };

    /// <summary>The Vulkan form of which face is discarded.</summary>
    /// <param name="mode">The renderer's mode.</param>
    /// <returns>The Vulkan mode.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one the renderer defines.</exception>
    public static CullModeFlags CullMode(RhiCullMode mode) => mode switch
    {
        RhiCullMode.None => CullModeFlags.None,
        RhiCullMode.Front => CullModeFlags.FrontBit,
        RhiCullMode.Back => CullModeFlags.BackBit,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a cull mode."),
    };

    /// <summary>The Vulkan form of which winding is the front face.</summary>
    /// <param name="face">The renderer's winding.</param>
    /// <returns>The Vulkan winding.</returns>
    public static FrontFace FrontFace(RhiFrontFace face) =>
        face == RhiFrontFace.Clockwise ? Silk.NET.Vulkan.FrontFace.Clockwise : Silk.NET.Vulkan.FrontFace.CounterClockwise;

    /// <summary>The Vulkan form of whether a vertex buffer advances per vertex or per instance.</summary>
    /// <param name="mode">The renderer's step mode.</param>
    /// <returns>The Vulkan input rate.</returns>
    public static VertexInputRate StepMode(RhiVertexStepMode mode) =>
        mode == RhiVertexStepMode.Instance ? VertexInputRate.Instance : VertexInputRate.Vertex;

    /// <summary>The Vulkan form of a filter.</summary>
    /// <param name="filter">The renderer's filter.</param>
    /// <returns>The Vulkan filter.</returns>
    public static Filter Filter(RhiFilter filter) =>
        filter == RhiFilter.Nearest ? Silk.NET.Vulkan.Filter.Nearest : Silk.NET.Vulkan.Filter.Linear;

    /// <summary>The Vulkan form of a filter between mip levels.</summary>
    /// <param name="filter">The renderer's filter.</param>
    /// <returns>The Vulkan mip mode.</returns>
    public static SamplerMipmapMode MipmapFilter(RhiFilter filter) =>
        filter == RhiFilter.Nearest ? SamplerMipmapMode.Nearest : SamplerMipmapMode.Linear;

    /// <summary>The Vulkan form of what a coordinate outside the texture reads.</summary>
    /// <param name="mode">The renderer's mode.</param>
    /// <returns>The Vulkan address mode.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not one the renderer defines.</exception>
    public static SamplerAddressMode AddressMode(RhiAddressMode mode) => mode switch
    {
        RhiAddressMode.Repeat => SamplerAddressMode.Repeat,
        RhiAddressMode.MirrorRepeat => SamplerAddressMode.MirroredRepeat,
        RhiAddressMode.ClampToEdge => SamplerAddressMode.ClampToEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not an address mode."),
    };

    /// <summary>The Vulkan form of how a shader addresses a texture view.</summary>
    /// <param name="dimension">The renderer's dimension.</param>
    /// <returns>The Vulkan view type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The dimension is not one the renderer defines.</exception>
    public static ImageViewType ViewDimension(RhiTextureViewDimension dimension) => dimension switch
    {
        RhiTextureViewDimension.Texture2D => ImageViewType.Type2D,
        RhiTextureViewDimension.Texture2DArray => ImageViewType.Type2DArray,
        RhiTextureViewDimension.Cube => ImageViewType.TypeCube,
        RhiTextureViewDimension.Texture3D => ImageViewType.Type3D,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Not a view dimension."),
    };

    /// <summary>The Vulkan form of a texture's own dimension, which an array is not.</summary>
    /// <param name="dimension">The renderer's dimension.</param>
    /// <returns>The Vulkan image type.</returns>
    /// <remarks>
    /// An array and a cube are two-dimensional images with layers; only a volume is three-dimensional.
    /// Creating an array as a volume is accepted and then reads back wrong, because a volume filters
    /// between its slices and an array does not.
    /// </remarks>
    public static ImageType TextureDimension(RhiTextureViewDimension dimension) =>
        dimension == RhiTextureViewDimension.Texture3D ? ImageType.Type3D : ImageType.Type2D;

    /// <summary>The Vulkan form of what happens to an attachment when a pass begins.</summary>
    /// <param name="op">The renderer's operation.</param>
    /// <returns>The Vulkan operation.</returns>
    public static AttachmentLoadOp LoadOp(RhiLoadOp op) =>
        op == RhiLoadOp.Load ? AttachmentLoadOp.Load : AttachmentLoadOp.Clear;

    /// <summary>The Vulkan form of what happens to an attachment when a pass ends.</summary>
    /// <param name="op">The renderer's operation.</param>
    /// <returns>The Vulkan operation.</returns>
    public static AttachmentStoreOp StoreOp(RhiStoreOp op) =>
        op == RhiStoreOp.Discard ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store;

    /// <summary>The Vulkan form of what a query set counts.</summary>
    /// <param name="type">The renderer's type.</param>
    /// <returns>The Vulkan query type.</returns>
    public static QueryType QueryType(RhiQueryType type) =>
        type == RhiQueryType.Occlusion ? Silk.NET.Vulkan.QueryType.Occlusion : Silk.NET.Vulkan.QueryType.Timestamp;

    /// <summary>Which aspects of an image a copy or a view touches.</summary>
    /// <param name="format">The format the image holds.</param>
    /// <returns>The aspect mask.</returns>
    /// <remarks>
    /// Depth and colour are different aspects and naming the wrong one is a validation error rather than
    /// something ignored, so it is derived from the format instead of being passed around.
    /// </remarks>
    public static ImageAspectFlags Aspects(RhiTextureFormat format)
    {
        if (!format.IsDepth())
        {
            return ImageAspectFlags.ColorBit;
        }

        return format.HasStencil()
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.DepthBit;
    }
}
