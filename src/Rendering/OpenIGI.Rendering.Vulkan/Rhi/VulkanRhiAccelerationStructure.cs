using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.RayTracing;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>An acceleration structure as the interface hands it out.</summary>
/// <remarks>
/// Owned by whoever created it, which keeps it alive until no frame in flight traces against it -- the traced
/// world above the RHI decides that, for every backend at once. Disposing this frees the structure.
/// </remarks>
internal sealed class VulkanRhiAccelerationStructure : IRhiAccelerationStructure
{
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiAccelerationStructure"/> class.</summary>
    /// <param name="structure">The structure, which this now owns.</param>
    /// <param name="kind">Which level it is.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    public VulkanRhiAccelerationStructure(VulkanAccelerationStructure structure, RhiAccelerationStructureKind kind, string label)
    {
        Structure = structure;
        Kind = kind;
        Label = label;
    }

    /// <summary>Gets the structure.</summary>
    public VulkanAccelerationStructure Structure { get; }

    /// <summary>Gets its handle.</summary>
    public AccelerationStructureKHR Handle => Structure.Handle;

    /// <inheritdoc/>
    public RhiAccelerationStructureKind Kind { get; }

    /// <inheritdoc/>
    public ulong Size => Structure.Size;

    /// <inheritdoc/>
    /// <remarks>The structure's device address.</remarks>
    public ulong InstanceReference => Structure.DeviceAddress;

    /// <inheritdoc/>
    public string Label { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Structure.Dispose();
    }
}
