using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Sync;

/// <summary>
/// Turns a declared use of a resource into the pipeline stages, memory accesses and image layout a
/// barrier needs.
/// </summary>
/// <remarks>
/// <para>
/// This is the single table the whole backend's synchronisation is derived from. A pass says what it
/// does with a resource; this says what that means to the GPU. Keeping it in one place means a barrier
/// is never written by hand and never gets a stage right but an access wrong.
/// </para>
/// <para>
/// A few states are exclusive rather than combinable — presenting, discarding, and being a copy
/// destination each describe the resource completely and cannot be mixed with a shader read — so they
/// answer on their own. The rest accumulate: a resource sampled by both pipelines yields both stages and
/// the union of their accesses.
/// </para>
/// </remarks>
internal static class VulkanAccessMap
{
    // The graphics stages a shader read or write can come from. Vertex and fragment always; the geometry
    // and tessellation stages are not used by this renderer, so naming them would only widen every
    // barrier for work that never happens.
    private const PipelineStageFlags2 GraphicsShaderStages =
        PipelineStageFlags2.VertexShaderBit | PipelineStageFlags2.FragmentShaderBit;

    /// <summary>Works out the stages, accesses and layout a use implies.</summary>
    /// <param name="state">What the resource is being used for.</param>
    /// <param name="depthStencil">Whether the resource is a depth or stencil image.</param>
    /// <param name="sampleable">Whether an image can take the read-only-optimal layout at all.</param>
    /// <returns>The stage mask, access mask and image layout for the barrier.</returns>
    public static (PipelineStageFlags2 Stages, AccessFlags2 Access, ImageLayout Layout) Resolve(
        VulkanResourceState state,
        bool depthStencil = false,
        bool sampleable = true)
    {
        // The layout a sampled image settles into. A depth image read by a shader is not in the ordinary
        // shader-read layout, and an image the adapter cannot sample at all has to sit in the general
        // layout, which is always legal and never optimal.
        ImageLayout readLayout = depthStencil
            ? ImageLayout.DepthStencilReadOnlyOptimal
            : sampleable ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.General;

        switch (state)
        {
            case VulkanResourceState.Unknown:
                // Nothing is known, so everything must be assumed: wait for all commands and make every
                // access visible. Correct and slow, which is the right trade for a state that only
                // appears at a resource's first use or after an aliasing change.
                return (
                    PipelineStageFlags2.AllCommandsBit,
                    AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit,
                    ImageLayout.Undefined);

            case VulkanResourceState.Discard:
                return (
                    PipelineStageFlags2.AllCommandsBit,
                    AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit,
                    ImageLayout.Undefined);

            case VulkanResourceState.HostRead:
                return (PipelineStageFlags2.HostBit, AccessFlags2.HostReadBit, ImageLayout.General);

            case VulkanResourceState.Present:
                // Presentation's visibility is guaranteed by the semaphore the present waits on, so the
                // barrier carries no access mask at all — only the layout change and the execution edge.
                return (
                    PipelineStageFlags2.ColorAttachmentOutputBit,
                    AccessFlags2.None,
                    ImageLayout.PresentSrcKhr);

            case VulkanResourceState.RenderTarget:
                return (
                    PipelineStageFlags2.ColorAttachmentOutputBit,
                    AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit,
                    ImageLayout.ColorAttachmentOptimal);

            case VulkanResourceState.CopyDestination:
                return (
                    PipelineStageFlags2.AllTransferBit,
                    AccessFlags2.TransferWriteBit,
                    ImageLayout.TransferDstOptimal);

            case VulkanResourceState.AccelerationStructureRead:
                return (
                    PipelineStageFlags2.AccelerationStructureBuildBitKhr
                        | PipelineStageFlags2.ComputeShaderBit
                        | PipelineStageFlags2.RayTracingShaderBitKhr,
                    AccessFlags2.AccelerationStructureReadBitKhr,
                    ImageLayout.Undefined);

            case VulkanResourceState.AccelerationStructureWrite:
                return (
                    PipelineStageFlags2.AccelerationStructureBuildBitKhr
                        | PipelineStageFlags2.ComputeShaderBit
                        | PipelineStageFlags2.RayTracingShaderBitKhr,
                    AccessFlags2.AccelerationStructureWriteBitKhr,
                    ImageLayout.Undefined);
        }

        // Writing depth wins outright: an image being written as a depth attachment is in the attachment
        // layout whatever else it is also flagged as, and mixing that with a read layout is meaningless.
        if (state.HasFlag(VulkanResourceState.DepthWrite))
        {
            return (
                PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
                AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit,
                ImageLayout.DepthStencilAttachmentOptimal);
        }

        PipelineStageFlags2 stages = PipelineStageFlags2.None;
        AccessFlags2 access = AccessFlags2.None;
        ImageLayout layout = ImageLayout.Undefined;

        if (state.HasFlag(VulkanResourceState.IndirectArguments))
        {
            stages |= PipelineStageFlags2.DrawIndirectBit;
            access |= AccessFlags2.IndirectCommandReadBit;
        }

        if (state.HasFlag(VulkanResourceState.VertexOrIndexBuffer))
        {
            stages |= PipelineStageFlags2.VertexInputBit;
            access |= AccessFlags2.IndexReadBit | AccessFlags2.VertexAttributeReadBit;
        }

        if (state.HasFlag(VulkanResourceState.DepthRead))
        {
            stages |= PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit;
            access |= AccessFlags2.DepthStencilAttachmentReadBit;
            layout = ImageLayout.DepthStencilReadOnlyOptimal;
        }

        if (state.HasFlag(VulkanResourceState.GraphicsRead))
        {
            stages |= GraphicsShaderStages;
            access |= AccessFlags2.ShaderReadBit | AccessFlags2.UniformReadBit;
            layout = Widen(layout, readLayout);
        }

        if (state.HasFlag(VulkanResourceState.ComputeRead))
        {
            stages |= PipelineStageFlags2.ComputeShaderBit | PipelineStageFlags2.RayTracingShaderBitKhr;
            access |= AccessFlags2.ShaderReadBit | AccessFlags2.UniformReadBit;
            layout = Widen(layout, readLayout);
        }

        // A storage binding is read-write whichever way the shader actually uses it, and only the general
        // layout permits a storage image at all — so any write state settles the layout outright.
        if (state.HasFlag(VulkanResourceState.GraphicsWrite))
        {
            stages |= GraphicsShaderStages;
            access |= AccessFlags2.ShaderReadBit | AccessFlags2.ShaderWriteBit;
            layout = ImageLayout.General;
        }

        if (state.HasFlag(VulkanResourceState.ComputeWrite))
        {
            stages |= PipelineStageFlags2.ComputeShaderBit | PipelineStageFlags2.RayTracingShaderBitKhr;
            access |= AccessFlags2.ShaderReadBit | AccessFlags2.ShaderWriteBit;
            layout = ImageLayout.General;
        }

        if (state.HasFlag(VulkanResourceState.CopySource))
        {
            stages |= PipelineStageFlags2.AllTransferBit;
            access |= AccessFlags2.TransferReadBit;

            // Being a copy source alongside anything else means no single optimal layout serves both, so
            // the image sits in the general layout for as long as both uses overlap.
            layout = layout == ImageLayout.Undefined ? ImageLayout.TransferSrcOptimal : ImageLayout.General;
        }

        if (state.HasFlag(VulkanResourceState.AccelerationStructureRead))
        {
            stages |= PipelineStageFlags2.AccelerationStructureBuildBitKhr
                | PipelineStageFlags2.ComputeShaderBit
                | PipelineStageFlags2.RayTracingShaderBitKhr;
            access |= AccessFlags2.AccelerationStructureReadBitKhr;
        }

        if (stages == PipelineStageFlags2.None)
        {
            stages = PipelineStageFlags2.AllCommandsBit;
        }

        return (stages, access, layout);
    }

    /// <summary>
    /// The barrier the backend puts around an acceleration-structure build: everything a build wrote is
    /// visible to whatever traverses or builds on top of it next.
    /// </summary>
    /// <remarks>
    /// Deliberately coarse. A build's output is reachable through device addresses rather than through a
    /// bound resource, so there is no per-resource barrier to write; a build that feeds another build —
    /// bottom level into top level — and a traversal that reads the result are all covered by one memory
    /// barrier over the build and compute stages.
    /// </remarks>
    /// <returns>The source and destination stage and access masks.</returns>
    public static (PipelineStageFlags2 Stages, AccessFlags2 SrcAccess, AccessFlags2 DstAccess)
        AccelerationStructureBuildBarrier()
    {
        const PipelineStageFlags2 stages =
            PipelineStageFlags2.AccelerationStructureBuildBitKhr | PipelineStageFlags2.ComputeShaderBit;
        return (
            stages,
            AccessFlags2.AccelerationStructureWriteBitKhr,
            AccessFlags2.AccelerationStructureReadBitKhr | AccessFlags2.AccelerationStructureWriteBitKhr);
    }

    private static ImageLayout Widen(ImageLayout current, ImageLayout wanted)
    {
        if (current == ImageLayout.Undefined || current == wanted)
        {
            return wanted;
        }

        // Two different optimal layouts cannot both hold, so the one layout legal for every use wins.
        return ImageLayout.General;
    }
}
