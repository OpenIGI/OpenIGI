namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// The optional things a device can do, each of which some device cannot.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a real difference between the machines this runs on, not a hypothetical: a
/// browser has no ray query and no timestamp query, MoltenVK reports its own sample counts, and a
/// desktop driver may or may not filter a 32-bit float texture. A pass asks before it uses one and has
/// something to do when the answer is no — that is the rule the whole renderer already follows for
/// hardware ray tracing, and it is the rule that makes a second backend possible at all.
/// </para>
/// <para>
/// Presence, not preference. Whether a feature that exists should be used is a setting, and settings
/// live above this.
/// </para>
/// </remarks>
/// <param name="RayQuery">Whether a shader may trace rays against an acceleration structure.</param>
/// <param name="TimestampQuery">Whether the device can be asked how long a pass took on the GPU.</param>
/// <param name="Float32Filterable">
/// Whether a 32-bit float texture can be sampled with filtering rather than only fetched. WebGPU makes
/// this an optional feature and several adapters do not offer it; where it is absent a pass that wants a
/// smooth lookup table has to store it at half precision instead.
/// </param>
/// <param name="DepthClamp">
/// Whether depth may be clamped rather than clipped at the near and far planes, which is what lets a
/// shadow caster behind the light's near plane still cast.
/// </param>
/// <param name="IndirectFirstInstance">Whether an indirect draw may start at a non-zero instance.</param>
/// <param name="TextureCompressionBc">Whether the BC block formats can be uploaded without decoding.</param>
public readonly record struct RhiFeatures(
    bool RayQuery,
    bool TimestampQuery,
    bool Float32Filterable,
    bool DepthClamp,
    bool IndirectFirstInstance,
    bool TextureCompressionBc)
{
    /// <summary>Gets a device that offers none of them, which is the safe assumption before asking.</summary>
    public static RhiFeatures None => default;
}

/// <summary>
/// The sizes a device will not exceed, as it reports them.
/// </summary>
/// <remarks>
/// These are read rather than assumed because the interesting ones genuinely differ by an order of
/// magnitude between a desktop driver and a browser: WebGPU guarantees 16 KiB of workgroup storage where
/// a desktop card offers 32 or 48, and a pass that tiles its shared memory to the larger number simply
/// fails to create on the smaller one. The temporal resolve already chooses its tile size from this, so
/// the quantity is not new — only the place it is read from.
/// </remarks>
/// <param name="MaxTextureDimension2D">The largest width or height a 2D texture may have.</param>
/// <param name="MaxTextureDimension3D">The largest extent a 3D texture may have on any axis.</param>
/// <param name="MaxTextureArrayLayers">The most layers a texture array may have.</param>
/// <param name="MaxBindGroups">How many bind groups may be bound at once.</param>
/// <param name="MaxSamplersPerStage">How many samplers one stage may use.</param>
/// <param name="MaxSampledTexturesPerStage">How many sampled textures one stage may use.</param>
/// <param name="MaxStorageBuffersPerStage">How many storage buffers one stage may use.</param>
/// <param name="MaxStorageTexturesPerStage">How many storage textures one stage may use.</param>
/// <param name="MaxUniformBufferBindingSize">The largest uniform buffer range one binding may cover.</param>
/// <param name="MaxStorageBufferBindingSize">The largest storage buffer range one binding may cover.</param>
/// <param name="MaxPushDataBytes">
/// How many bytes one <c>SetPushData</c> may carry. Vulkan's push constants are at least 128 bytes and
/// often 256; WebGPU has none, and the implementation there spends a uniform ring on it instead.
/// </param>
/// <param name="MaxComputeWorkgroupStorageBytes">How much shared memory one workgroup may declare.</param>
/// <param name="MaxComputeInvocationsPerWorkgroup">How many invocations one workgroup may have.</param>
/// <param name="MaxComputeWorkgroupsPerDimension">The largest dispatch on any one axis.</param>
/// <param name="MaxColorAttachments">How many colour attachments one render pass may write.</param>
/// <param name="MaxSampleCount">The highest multisample count a target may be created with.</param>
public readonly record struct RhiLimits(
    int MaxTextureDimension2D,
    int MaxTextureDimension3D,
    int MaxTextureArrayLayers,
    int MaxBindGroups,
    int MaxSamplersPerStage,
    int MaxSampledTexturesPerStage,
    int MaxStorageBuffersPerStage,
    int MaxStorageTexturesPerStage,
    ulong MaxUniformBufferBindingSize,
    ulong MaxStorageBufferBindingSize,
    int MaxPushDataBytes,
    int MaxComputeWorkgroupStorageBytes,
    int MaxComputeInvocationsPerWorkgroup,
    int MaxComputeWorkgroupsPerDimension,
    int MaxColorAttachments,
    int MaxSampleCount)
{
    /// <summary>Gets the limits every WebGPU adapter is required to meet.</summary>
    /// <remarks>
    /// The floor, and a useful one to write a pass against: anything that fits here fits everywhere.
    /// These are the values from the WebGPU specification's table of default limits, with
    /// <see cref="MaxPushDataBytes"/> set to what that implementation spends on a uniform ring.
    /// </remarks>
    public static RhiLimits WebGpuDefaults => new(
        MaxTextureDimension2D: 8192,
        MaxTextureDimension3D: 2048,
        MaxTextureArrayLayers: 256,
        MaxBindGroups: 4,
        MaxSamplersPerStage: 16,
        MaxSampledTexturesPerStage: 16,
        MaxStorageBuffersPerStage: 8,
        MaxStorageTexturesPerStage: 4,
        MaxUniformBufferBindingSize: 65536,
        MaxStorageBufferBindingSize: 134217728,
        MaxPushDataBytes: 256,
        MaxComputeWorkgroupStorageBytes: 16384,
        MaxComputeInvocationsPerWorkgroup: 256,
        MaxComputeWorkgroupsPerDimension: 65535,
        MaxColorAttachments: 8,
        MaxSampleCount: 4);

    /// <summary>Whether these limits meet another set, so anything written against those fits here.</summary>
    /// <param name="floor">The limits to meet.</param>
    /// <returns><see langword="true"/> when every limit is at least the other's.</returns>
    public bool Meets(RhiLimits floor) =>
        MaxTextureDimension2D >= floor.MaxTextureDimension2D
        && MaxTextureDimension3D >= floor.MaxTextureDimension3D
        && MaxTextureArrayLayers >= floor.MaxTextureArrayLayers
        && MaxBindGroups >= floor.MaxBindGroups
        && MaxSamplersPerStage >= floor.MaxSamplersPerStage
        && MaxSampledTexturesPerStage >= floor.MaxSampledTexturesPerStage
        && MaxStorageBuffersPerStage >= floor.MaxStorageBuffersPerStage
        && MaxStorageTexturesPerStage >= floor.MaxStorageTexturesPerStage
        && MaxUniformBufferBindingSize >= floor.MaxUniformBufferBindingSize
        && MaxStorageBufferBindingSize >= floor.MaxStorageBufferBindingSize
        && MaxPushDataBytes >= floor.MaxPushDataBytes
        && MaxComputeWorkgroupStorageBytes >= floor.MaxComputeWorkgroupStorageBytes
        && MaxComputeInvocationsPerWorkgroup >= floor.MaxComputeInvocationsPerWorkgroup
        && MaxComputeWorkgroupsPerDimension >= floor.MaxComputeWorkgroupsPerDimension
        && MaxColorAttachments >= floor.MaxColorAttachments
        && MaxSampleCount >= floor.MaxSampleCount;
}
