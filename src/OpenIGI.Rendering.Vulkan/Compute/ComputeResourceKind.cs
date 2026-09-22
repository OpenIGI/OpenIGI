namespace OpenIGI.Rendering.Vulkan.Compute;

/// <summary>Which kind of descriptor a compute shader's resource needs.</summary>
internal enum ComputeResourceKind
{
    /// <summary>A shader-writable storage buffer, bound with <c>BindStorageBuffer</c>.</summary>
    StorageBuffer,

    /// <summary>An image bound for load/store, bound with <c>BindImage</c>.</summary>
    StorageImage,

    /// <summary>A sampled texture, bound with <c>SetTexture</c> or <c>SetVolumeTexture</c>.</summary>
    SampledImage,

    /// <summary>The world's acceleration structure, for a pass that traces rays against it.</summary>
    Scene,

    /// <summary>A sampler declared apart from the texture it reads, which is the shape WGSL has.</summary>
    /// <remarks>
    /// It carries no state of its own: it resolves to the sampler of whatever texture is bound at the
    /// same slot, so a module that declares the pair is bound by one call exactly as a combined one is.
    /// </remarks>
    Sampler,
}
