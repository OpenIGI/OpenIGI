namespace OpenIGI.Rendering.Vulkan.Bindless;

/// <summary>Which heap a descriptor lives in, and therefore how a shader reaches it.</summary>
/// <remarks>
/// Samplers are separated from everything else because Vulkan keeps them in their own descriptor buffer
/// with its own address and its own size limits — an adapter will hold a few thousand samplers and
/// hundreds of thousands of resources, and the two cannot share an array.
/// </remarks>
internal enum VulkanDescriptorHeap
{
    /// <summary>The sampler heap.</summary>
    Sampler,

    /// <summary>The resource heap: textures, storage images, buffers and acceleration structures alike.</summary>
    Resource,
}

/// <summary>A slot in one of the bindless heaps, and the index a shader uses to reach it.</summary>
/// <remarks>
/// <para>
/// The whole point of the bindless model is that this is just a number. A shader is handed an integer —
/// through a push constant, a uniform, or a value it computed itself — and indexes the heap with it; no
/// descriptor set is bound per draw, and nothing about the resource is known at pipeline-creation time.
/// That is what lets one dispatch shade a thousand different materials, and what lets a ray hit reach
/// the geometry it struck without the geometry having been bound.
/// </para>
/// <para>
/// The heap is carried alongside for the CPU's benefit only — to free the slot back to the right
/// allocator and to catch a sampler handle being used where a texture was meant. The shader never sees
/// it, because which heap a binding reads is fixed by the type it was declared as in the shader.
/// </para>
/// </remarks>
/// <param name="Heap">Which heap the slot is in.</param>
/// <param name="Index">The slot's index within that heap.</param>
internal readonly record struct VulkanDescriptorHandle(VulkanDescriptorHeap Heap, uint Index)
{
    /// <summary>The index that means "no descriptor". A shader must not index the heap with it.</summary>
    public const uint InvalidIndex = uint.MaxValue;

    /// <summary>Gets a handle that refers to nothing.</summary>
    public static VulkanDescriptorHandle Invalid => new(VulkanDescriptorHeap.Resource, InvalidIndex);

    /// <summary>Gets a value indicating whether the handle refers to a real slot.</summary>
    public bool IsValid => Index != InvalidIndex;
}
