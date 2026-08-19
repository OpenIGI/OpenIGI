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
}
