namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// What a buffer will be used for, declared when it is created.
/// </summary>
/// <remarks>
/// Declared up front because both backends need to know before they allocate, and neither can be told
/// afterwards. It is also the one piece of information that lets an implementation place the memory
/// sensibly rather than defensively.
/// </remarks>
[Flags]
public enum RhiBufferUsage
{
    /// <summary>No use. Not valid on its own.</summary>
    None = 0,

    /// <summary>May be the source of a copy.</summary>
    CopySource = 1 << 0,

    /// <summary>May be the destination of a copy, which is what a write from the host is.</summary>
    CopyDestination = 1 << 1,

    /// <summary>May be bound as a vertex buffer.</summary>
    Vertex = 1 << 2,

    /// <summary>May be bound as an index buffer.</summary>
    Index = 1 << 3,

    /// <summary>May be bound as a uniform buffer.</summary>
    Uniform = 1 << 4,

    /// <summary>May be bound as a storage buffer, read or written by a shader.</summary>
    Storage = 1 << 5,

    /// <summary>May supply the arguments of an indirect draw or dispatch.</summary>
    Indirect = 1 << 6,

    /// <summary>May be mapped for reading, which is how a readback reaches the host.</summary>
    MapRead = 1 << 7,

    /// <summary>May be mapped for writing.</summary>
    MapWrite = 1 << 8,

    /// <summary>May receive a query set's resolved results.</summary>
    /// <remarks>
    /// Its own use rather than an ordinary copy destination, because both backends want to know: the
    /// results are written by the device at a fixed stride and a buffer not created for it is refused.
    /// </remarks>
    QueryResolve = 1 << 9,
}

/// <summary>
/// What a texture will be used for, declared when it is created.
/// </summary>
[Flags]
public enum RhiTextureUsage
{
    /// <summary>No use. Not valid on its own.</summary>
    None = 0,

    /// <summary>May be the source of a copy.</summary>
    CopySource = 1 << 0,

    /// <summary>May be the destination of a copy, which is what an upload from the host is.</summary>
    CopyDestination = 1 << 1,

    /// <summary>May be sampled by a shader.</summary>
    Sampled = 1 << 2,

    /// <summary>May be read or written by a shader as a storage image, without a sampler.</summary>
    Storage = 1 << 3,

    /// <summary>May be attached to a render pass as a colour or depth target.</summary>
    RenderTarget = 1 << 4,
}

/// <summary>Which shader stages a binding is visible to.</summary>
[Flags]
public enum RhiShaderStages
{
    /// <summary>No stage.</summary>
    None = 0,

    /// <summary>The vertex stage.</summary>
    Vertex = 1 << 0,

    /// <summary>The fragment stage.</summary>
    Fragment = 1 << 1,

    /// <summary>The compute stage.</summary>
    Compute = 1 << 2,

    /// <summary>Both raster stages, which is what most per-draw data is visible to.</summary>
    Graphics = Vertex | Fragment,

    /// <summary>Every stage.</summary>
    All = Vertex | Fragment | Compute,
}

/// <summary>What one entry of a bind group layout holds.</summary>
/// <remarks>
/// The texture and its sampler are separate entries, which is the only shape WGSL has and the shape the
/// shader library therefore compiles for both targets. A combined one would be a Vulkan-only convenience
/// bought at the price of the browser.
/// </remarks>
public enum RhiBindingType
{
    /// <summary>A uniform buffer.</summary>
    UniformBuffer,

    /// <summary>A storage buffer a shader only reads.</summary>
    ReadOnlyStorageBuffer,

    /// <summary>A storage buffer a shader may write.</summary>
    StorageBuffer,

    /// <summary>A texture read through a sampler.</summary>
    SampledTexture,

    /// <summary>A texture read or written directly, without a sampler.</summary>
    StorageTexture,

    /// <summary>A sampler, declared apart from the texture it reads.</summary>
    Sampler,

    /// <summary>A sampler that compares rather than filters, for shadow lookups.</summary>
    ComparisonSampler,

    /// <summary>An acceleration structure. Only a device offering <see cref="RhiFeatures.RayQuery"/> has one.</summary>
    AccelerationStructure,
}

/// <summary>How a storage texture binding is accessed.</summary>
/// <remarks>
/// WebGPU allows read-only and write-only for every storage format and read-write for only a few, which
/// is why several of this renderer's passes compile for SPIR-V alone. Stating the access here is what
/// lets an implementation refuse at creation rather than produce a shader that does not link.
/// </remarks>
public enum RhiStorageAccess
{
    /// <summary>The shader only writes.</summary>
    WriteOnly,

    /// <summary>The shader only reads.</summary>
    ReadOnly,

    /// <summary>The shader reads and writes the same texel.</summary>
    ReadWrite,
}
