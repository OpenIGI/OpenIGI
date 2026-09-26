using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Sync;

/// <summary>
/// Collects the barriers the traced world's builds need and issues them as one dependency.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline barrier is a synchronisation point for the whole GPU, not for the resource it names: two
/// barriers issued back to back drain the pipeline twice for what could have been one stall. The builds of
/// a frame's acceleration structures are ordered by memory barriers alone -- their output is reachable
/// through device addresses rather than through a bound resource, so there is no per-resource barrier to
/// write -- and every one wanted at the same point is merged into a single call.
/// </para>
/// <para>
/// Synchronisation 2, because it states the stage masks on the barrier itself rather than on the call.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBarrierBatcher
{
    private readonly Vk _vk;
    private MemoryBarrier2 _memoryBarrier;
    private bool _hasMemoryBarrier;

    /// <summary>Initialises a new instance of the <see cref="VulkanBarrierBatcher"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    public VulkanBarrierBatcher(Vk vk)
    {
        ArgumentNullException.ThrowIfNull(vk);
        _vk = vk;
    }

    /// <summary>Gets a value indicating whether anything is waiting to be flushed.</summary>
    public bool IsEmpty => !_hasMemoryBarrier;

    /// <summary>Adds a global memory barrier, merged with any already pending.</summary>
    /// <param name="srcStages">The stages that must finish.</param>
    /// <param name="srcAccess">The writes to make available.</param>
    /// <param name="dstStages">The stages that must wait.</param>
    /// <param name="dstAccess">The accesses to make visible.</param>
    /// <remarks>
    /// There is only ever one memory barrier in a batch: a second would stall the same stages for the
    /// same reason, so the masks are merged instead.
    /// </remarks>
    public void AddMemory(
        PipelineStageFlags2 srcStages,
        AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStages,
        AccessFlags2 dstAccess)
    {
        if (_hasMemoryBarrier)
        {
            _memoryBarrier.SrcStageMask |= srcStages;
            _memoryBarrier.SrcAccessMask |= srcAccess;
            _memoryBarrier.DstStageMask |= dstStages;
            _memoryBarrier.DstAccessMask |= dstAccess;
            return;
        }

        _memoryBarrier = new MemoryBarrier2
        {
            SType = StructureType.MemoryBarrier2,
            SrcStageMask = srcStages,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStages,
            DstAccessMask = dstAccess,
        };
        _hasMemoryBarrier = true;
    }

    /// <summary>Orders a build after the traversals that were reading what it is about to overwrite.</summary>
    /// <remarks>
    /// The other direction to <see cref="AddAccelerationStructureBuild"/>, and the one that is easy to
    /// forget: a structure is rebuilt where it stands, so a build is a write into memory that rays were
    /// reading a moment ago -- and on this queue "a moment ago" includes a frame that has been submitted
    /// and is still running. The two sides are not symmetric: anything may traverse a structure -- the
    /// ray-query dispatches, and a top-level build reading the bottom-level structures under it -- but
    /// only the build stage may write one. No ray-tracing-pipeline stage: this backend traces from
    /// compute, and naming a stage the enabled features do not cover is itself an error.
    /// </remarks>
    public void AddAccelerationStructureReadToBuild() => AddMemory(
        PipelineStageFlags2.ComputeShaderBit | PipelineStageFlags2.AccelerationStructureBuildBitKhr,
        AccessFlags2.AccelerationStructureReadBitKhr,
        PipelineStageFlags2.AccelerationStructureBuildBitKhr,
        AccessFlags2.AccelerationStructureWriteBitKhr);

    /// <summary>Makes everything a build wrote visible to whatever traverses or builds on it next.</summary>
    /// <remarks>
    /// Deliberately coarse: a build that feeds another build -- bottom level into top level -- and a
    /// traversal that reads the result are all covered by one memory barrier over the build and compute
    /// stages.
    /// </remarks>
    public void AddAccelerationStructureBuild()
    {
        const PipelineStageFlags2 stages =
            PipelineStageFlags2.AccelerationStructureBuildBitKhr | PipelineStageFlags2.ComputeShaderBit;

        AddMemory(
            stages,
            AccessFlags2.AccelerationStructureWriteBitKhr,
            stages,
            AccessFlags2.AccelerationStructureReadBitKhr | AccessFlags2.AccelerationStructureWriteBitKhr);
    }

    /// <summary>Issues everything collected as one dependency, and empties the batch.</summary>
    /// <param name="commandBuffer">The command buffer to record into.</param>
    public void Flush(CommandBuffer commandBuffer)
    {
        if (IsEmpty)
        {
            return;
        }

        MemoryBarrier2 memory = _memoryBarrier;
        DependencyInfo info = new()
        {
            SType = StructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &memory,
        };

        _vk.CmdPipelineBarrier2(commandBuffer, &info);
        Reset();
    }

    /// <summary>Throws away everything collected without issuing it.</summary>
    public void Reset()
    {
        _hasMemoryBarrier = false;
        _memoryBarrier = default;
    }
}
