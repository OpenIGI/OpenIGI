using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Interop;

/// <summary>
/// <c>VkCheckpointDataNV</c>: one marker the GPU passed on a queue before it stopped, as read back by
/// <c>vkGetQueueCheckpointDataNV</c> from a device that has just been lost.
/// </summary>
/// <remarks>
/// Declared here rather than taken from a binding package because it is four fields, and the extension
/// is reached through <c>vkGetDeviceProcAddr</c> for the same reason: <c>VK_NV_device_diagnostic_checkpoints</c>
/// is the only piece of the NV namespace the backend touches, and it is a diagnostic, not a dependency.
/// </remarks>
internal unsafe struct VkCheckpointDataNV
{
    /// <summary>Must be <see cref="StructureType.CheckpointDataNV"/>.</summary>
    public StructureType SType;

    /// <summary>Extension chain, unused.</summary>
    public void* PNext;

    /// <summary>The pipeline stage the marker was reached at.</summary>
    public PipelineStageFlags Stage;

    /// <summary>The marker handed to <c>vkCmdSetCheckpointNV</c>, which the backend uses as a small integer.</summary>
    public void* PCheckpointMarker;
}
