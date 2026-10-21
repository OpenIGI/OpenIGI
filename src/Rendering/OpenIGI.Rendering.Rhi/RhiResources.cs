namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// Something a device made, which the device takes back when it is disposed.
/// </summary>
/// <remarks>
/// Every resource carries the label it was created with. It is not decoration: it is what a Vulkan
/// implementation hands to the debug-utils extension and what a browser shows in its own capture, so a
/// frame taken from either side names the same things.
/// </remarks>
public interface IRhiResource : IDisposable
{
    /// <summary>Gets the name this resource was created with, for captures and diagnostics.</summary>
    string Label { get; }
}

/// <summary>A block of device memory a shader, a draw or a copy may read.</summary>
public interface IRhiBuffer : IRhiResource
{
    /// <summary>Gets how many bytes it holds.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets what it may be used for.</summary>
    RhiBufferUsage Usage { get; }

    /// <summary>Gets what a shader reaches this buffer by without a binding, or nought where it cannot.</summary>
    /// <remarks>
    /// <para>
    /// For data a shader looks up indirectly -- a traced hit's material record names the mesh it struck,
    /// and the shader then reads that mesh's vertices -- where no bind group could name every buffer in the
    /// world. On Vulkan it is the buffer's device address; on Direct3D 12 it is the index of a raw view of
    /// it in the shader-visible descriptor heap. Either way a shader reads it through one accessor,
    /// <c>Common/HitRecords.slang</c>, and nothing above the backend interprets it.
    /// </para>
    /// <para>
    /// Only a buffer created with <see cref="RhiBufferUsage.RayTracingInput"/> has one, on a device that
    /// offers acceleration structures.
    /// </para>
    /// </remarks>
    ulong ShaderHandle => 0;
}

/// <summary>An image, of one or more mip levels and one or more layers.</summary>
public interface IRhiTexture : IRhiResource
{
    /// <summary>Gets its width in texels.</summary>
    int Width { get; }

    /// <summary>Gets its height in texels.</summary>
    int Height { get; }

    /// <summary>Gets its depth for a volume, or its layer count for an array; one otherwise.</summary>
    int DepthOrArrayLayers { get; }

    /// <summary>Gets how many mip levels it has.</summary>
    int MipLevelCount { get; }

    /// <summary>Gets how many samples each texel has.</summary>
    int SampleCount { get; }

    /// <summary>Gets what one texel holds.</summary>
    RhiTextureFormat Format { get; }

    /// <summary>Gets what it may be used for.</summary>
    RhiTextureUsage Usage { get; }

    /// <summary>Gets the view over the whole texture, which most uses want.</summary>
    IRhiTextureView View { get; }

    /// <summary>Creates a view over part of the texture.</summary>
    /// <param name="descriptor">Which part, and how it is read.</param>
    /// <returns>The view, owned by the texture and released with it.</returns>
    IRhiTextureView CreateView(in RhiTextureViewDescriptor descriptor);
}

/// <summary>A way of looking at a texture: which mips, which layers, and in which format.</summary>
public interface IRhiTextureView : IRhiResource
{
    /// <summary>Gets the texture this looks at.</summary>
    IRhiTexture Texture { get; }

    /// <summary>Gets the format reads and writes go through, which may differ from the texture's.</summary>
    RhiTextureFormat Format { get; }
}

/// <summary>How a texture is filtered and what happens past its edge.</summary>
public interface IRhiSampler : IRhiResource;

/// <summary>One shader module of the library, compiled for this backend's target.</summary>
public interface IRhiShaderModule : IRhiResource;

/// <summary>The shape of a bind group: what it holds, where, and which stages see it.</summary>
public interface IRhiBindGroupLayout : IRhiResource;

/// <summary>A set of resources bound together, against a layout.</summary>
public interface IRhiBindGroup : IRhiResource;

/// <summary>The bind group layouts a pipeline takes, and how much push data.</summary>
public interface IRhiPipelineLayout : IRhiResource;

/// <summary>A compiled graphics pipeline.</summary>
public interface IRhiRenderPipeline : IRhiResource
{
    /// <summary>Gets the layout its bind groups and push data are bound against.</summary>
    IRhiPipelineLayout Layout { get; }
}

/// <summary>A compiled compute pipeline.</summary>
public interface IRhiComputePipeline : IRhiResource
{
    /// <summary>Gets the layout its bind groups and push data are bound against.</summary>
    IRhiPipelineLayout Layout { get; }
}

/// <summary>What a query set counts.</summary>
public enum RhiQueryType
{
    /// <summary>When the GPU reached a point, in ticks. Needs <see cref="RhiFeatures.TimestampQuery"/>.</summary>
    Timestamp,

    /// <summary>How many samples a draw wrote, which is what an occlusion test asks.</summary>
    Occlusion,
}

/// <summary>A block of query slots the GPU writes and the host reads back.</summary>
public interface IRhiQuerySet : IRhiResource
{
    /// <summary>Gets what it counts.</summary>
    RhiQueryType Type { get; }

    /// <summary>Gets how many slots it has.</summary>
    int Count { get; }
}

/// <summary>Recorded work, ready to submit.</summary>
public interface IRhiCommandBuffer : IRhiResource;

/// <summary>Which part of a texture a view looks at.</summary>
/// <param name="Format">The format to read through, or <see cref="RhiTextureFormat.Undefined"/> for the texture's own.</param>
/// <param name="BaseMipLevel">The first mip level.</param>
/// <param name="MipLevelCount">How many mip levels, or zero for the rest.</param>
/// <param name="BaseArrayLayer">The first layer.</param>
/// <param name="ArrayLayerCount">How many layers, or zero for the rest.</param>
/// <param name="Dimension">How the view is addressed by a shader.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public readonly record struct RhiTextureViewDescriptor(
    RhiTextureFormat Format = RhiTextureFormat.Undefined,
    int BaseMipLevel = 0,
    int MipLevelCount = 0,
    int BaseArrayLayer = 0,
    int ArrayLayerCount = 0,
    RhiTextureViewDimension Dimension = RhiTextureViewDimension.Texture2D,
    string Label = "");

/// <summary>How a shader addresses a texture view.</summary>
public enum RhiTextureViewDimension
{
    /// <summary>A flat image.</summary>
    Texture2D,

    /// <summary>An array of flat images.</summary>
    Texture2DArray,

    /// <summary>A cube's six faces.</summary>
    Cube,

    /// <summary>A volume.</summary>
    Texture3D,
}

/// <summary>What a buffer is created as.</summary>
/// <param name="SizeInBytes">How many bytes it holds.</param>
/// <param name="Usage">What it may be used for.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public readonly record struct RhiBufferDescriptor(
    ulong SizeInBytes,
    RhiBufferUsage Usage,
    string Label = "");

/// <summary>What a texture is created as.</summary>
/// <param name="Width">Its width in texels.</param>
/// <param name="Height">Its height in texels.</param>
/// <param name="Format">What one texel holds.</param>
/// <param name="Usage">What it may be used for.</param>
/// <param name="DepthOrArrayLayers">Its depth for a volume, or its layer count for an array.</param>
/// <param name="MipLevelCount">How many mip levels.</param>
/// <param name="SampleCount">How many samples each texel has.</param>
/// <param name="Dimension">Whether it is flat, a volume, or an array.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public readonly record struct RhiTextureDescriptor(
    int Width,
    int Height,
    RhiTextureFormat Format,
    RhiTextureUsage Usage,
    int DepthOrArrayLayers = 1,
    int MipLevelCount = 1,
    int SampleCount = 1,
    RhiTextureViewDimension Dimension = RhiTextureViewDimension.Texture2D,
    string Label = "");

/// <summary>How a texture is filtered between texels and mips, and what happens past its edge.</summary>
public enum RhiFilter
{
    /// <summary>The nearest texel.</summary>
    Nearest,

    /// <summary>Interpolated between texels.</summary>
    Linear,
}

/// <summary>What a coordinate outside the texture reads.</summary>
public enum RhiAddressMode
{
    /// <summary>The texture repeats.</summary>
    Repeat,

    /// <summary>The texture repeats, mirrored.</summary>
    MirrorRepeat,

    /// <summary>The edge texel extends outwards.</summary>
    ClampToEdge,
}

/// <summary>What a sampler is created as.</summary>
/// <param name="MinFilter">How it filters when the texture is minified.</param>
/// <param name="MagFilter">How it filters when the texture is magnified.</param>
/// <param name="MipFilter">How it filters between mip levels.</param>
/// <param name="AddressU">What happens past the edge horizontally.</param>
/// <param name="AddressV">What happens past the edge vertically.</param>
/// <param name="AddressW">What happens past the edge in depth.</param>
/// <param name="MaxAnisotropy">
/// How many samples an anisotropic lookup may take; one for none. Both backends clamp it to what the
/// adapter offers rather than refusing.
/// </param>
/// <param name="Compare">
/// The comparison a shadow lookup makes, or <see langword="null"/> for an ordinary sampler. A sampler
/// with one may only be bound to a <see cref="RhiBindingType.ComparisonSampler"/> entry.
/// </param>
/// <param name="LodMin">The lowest mip level it will read.</param>
/// <param name="LodMax">The highest mip level it will read.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
/// <param name="MipLodBias">
/// How many levels the mip selection is pushed by; negative is sharper. Honoured where the adapter
/// offers it -- WebGPU has no such sampler state and samples unbiased.
/// </param>
public readonly record struct RhiSamplerDescriptor(
    RhiFilter MinFilter = RhiFilter.Linear,
    RhiFilter MagFilter = RhiFilter.Linear,
    RhiFilter MipFilter = RhiFilter.Linear,
    RhiAddressMode AddressU = RhiAddressMode.ClampToEdge,
    RhiAddressMode AddressV = RhiAddressMode.ClampToEdge,
    RhiAddressMode AddressW = RhiAddressMode.ClampToEdge,
    int MaxAnisotropy = 1,
    RhiCompare? Compare = null,
    float LodMin = 0f,
    float LodMax = 1000f,
    string Label = "",
    float MipLodBias = 0f);
