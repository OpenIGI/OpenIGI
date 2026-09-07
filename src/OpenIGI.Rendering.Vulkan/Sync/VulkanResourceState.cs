namespace OpenIGI.Rendering.Vulkan.Sync;

/// <summary>What a resource is being used for at a point in the frame.</summary>
/// <remarks>
/// <para>
/// Vulkan asks a barrier for three separate things — which pipeline stages must finish, which memory
/// accesses must be made visible, and (for an image) which layout it should be in. Those three always
/// move together in practice: a texture being sampled by a compute shader implies the compute stage, a
/// shader read, and the shader-read-only layout, and getting one of the three wrong is a class of bug
/// that neither compiles differently nor usually renders differently until it does.
/// </para>
/// <para>
/// So passes declare the <em>use</em>, and the mapping to stages, accesses and layout happens in one
/// place. That is what makes barriers derivable from a pass's declared reads and writes rather than
/// hand-placed at each call site.
/// </para>
/// <para>
/// The values are flags because a resource genuinely can be two things at once — sampled by the fragment
/// shader while also being a copy source, say — and the barrier then has to cover both.
/// </para>
/// </remarks>
[Flags]
internal enum VulkanResourceState : uint
{
    /// <summary>No known use. Treated as "anything may have touched it", which is always safe and never fast.</summary>
    Unknown = 0,

    /// <summary>The contents are not needed; the image may be left in an undefined layout.</summary>
    Discard = 1u << 0,

    /// <summary>Read by a compute shader.</summary>
    ComputeRead = 1u << 1,

    /// <summary>Read by a graphics-stage shader.</summary>
    GraphicsRead = 1u << 2,

    /// <summary>Read and written by a compute shader through a storage binding.</summary>
    ComputeWrite = 1u << 3,

    /// <summary>Read and written by a graphics-stage shader through a storage binding.</summary>
    GraphicsWrite = 1u << 4,

    /// <summary>Written as a colour attachment.</summary>
    RenderTarget = 1u << 5,

    /// <summary>Written as a depth or stencil attachment.</summary>
    DepthWrite = 1u << 6,

    /// <summary>Tested against as a depth or stencil attachment without being written.</summary>
    DepthRead = 1u << 7,

    /// <summary>The source of a copy.</summary>
    CopySource = 1u << 8,

    /// <summary>The destination of a copy.</summary>
    CopyDestination = 1u << 9,

    /// <summary>Read as the argument buffer of an indirect draw or dispatch.</summary>
    IndirectArguments = 1u << 10,

    /// <summary>Read by the fixed-function vertex or index fetch.</summary>
    VertexOrIndexBuffer = 1u << 11,

    /// <summary>Traversed as an acceleration structure, or read as one of its build inputs.</summary>
    AccelerationStructureRead = 1u << 12,

    /// <summary>Written by an acceleration-structure build or copy.</summary>
    AccelerationStructureWrite = 1u << 13,

    /// <summary>Read back by the CPU.</summary>
    HostRead = 1u << 14,

    /// <summary>Handed to the presentation engine.</summary>
    Present = 1u << 15,

    /// <summary>Read by a shader on either pipeline.</summary>
    ShaderRead = ComputeRead | GraphicsRead,

    /// <summary>Written by a shader on either pipeline.</summary>
    ShaderWrite = ComputeWrite | GraphicsWrite,
}
