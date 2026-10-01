using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.RayTracing;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>A structure the traced world built, as the interface hands it to a bind group.</summary>
/// <remarks>
/// Owned by the world, which keeps it alive until no frame in flight traces against it; disposing this
/// view of it frees nothing.
/// </remarks>
internal sealed class VulkanRhiAccelerationStructure : IRhiAccelerationStructure
{
    /// <summary>Initialises a new instance of the <see cref="VulkanRhiAccelerationStructure"/> class.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    public VulkanRhiAccelerationStructure(VulkanAccelerationStructure structure, string label)
    {
        Structure = structure;
        Label = label;
    }

    /// <summary>Gets the structure.</summary>
    public VulkanAccelerationStructure Structure { get; }

    /// <summary>Gets its handle.</summary>
    public AccelerationStructureKHR Handle => Structure.Handle;

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
