using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Sync;

/// <summary>
/// Collects the barriers a group of passes needs and issues them as one dependency.
/// </summary>
/// <remarks>
/// <para>
/// A pipeline barrier is a synchronisation point for the whole GPU, not for the resource it names: two
/// barriers issued back to back drain the pipeline twice for what could have been one stall. Batching
/// every transition that happens at the same point in the frame into a single call is the difference
/// between one bubble and a dozen.
/// </para>
/// <para>
/// Synchronisation 2 is what makes the batching worth anything. The original barrier form carried one
/// pair of stage masks for the whole call, so batching unrelated transitions widened all of them to the
/// union; the second form puts the stage masks on each barrier, so a texture waiting on compute and a
/// buffer waiting on a copy travel together without either being widened to cover the other.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBarrierBatcher
{
    private readonly Vk _vk;
    private readonly List<ImageMemoryBarrier2> _imageBarriers = [];
    private readonly List<BufferMemoryBarrier2> _bufferBarriers = [];
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
    public bool IsEmpty => !_hasMemoryBarrier && _imageBarriers.Count == 0 && _bufferBarriers.Count == 0;

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

    /// <summary>Adds the barrier that makes an acceleration-structure build visible to what reads it.</summary>
    public void AddAccelerationStructureBuild()
    {
        (PipelineStageFlags2 stages, AccessFlags2 src, AccessFlags2 dst) =
            VulkanAccessMap.AccelerationStructureBuildBarrier();
        AddMemory(stages, src, stages, dst);
    }

    /// <summary>Adds a transition of a buffer from one use to another.</summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="from">What it was used for.</param>
    /// <param name="to">What it will be used for.</param>
    /// <param name="offset">Where the affected range starts.</param>
    /// <param name="size">How long the affected range is; the whole buffer by default.</param>
    public void AddBuffer(
        Buffer buffer,
        VulkanResourceState from,
        VulkanResourceState to,
        ulong offset = 0,
        ulong size = Vk.WholeSize)
    {
        (PipelineStageFlags2 srcStages, AccessFlags2 srcAccess, _) = VulkanAccessMap.Resolve(from);
        (PipelineStageFlags2 dstStages, AccessFlags2 dstAccess, _) = VulkanAccessMap.Resolve(to);

        _bufferBarriers.Add(new BufferMemoryBarrier2
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = srcStages,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStages,
            DstAccessMask = dstAccess,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = buffer,
            Offset = offset,
            Size = size,
        });
    }

    /// <summary>Adds a transition of an image from one use to another.</summary>
    /// <param name="image">The image.</param>
    /// <param name="aspect">Which aspect of the image the transition covers.</param>
    /// <param name="from">What it was used for.</param>
    /// <param name="to">What it will be used for.</param>
    /// <param name="mipLevels">How many mip levels the transition covers.</param>
    /// <param name="arrayLayers">How many array layers the transition covers.</param>
    /// <param name="sampleable">Whether the image can take the shader-read-only layout.</param>
    public void AddImage(
        Image image,
        ImageAspectFlags aspect,
        VulkanResourceState from,
        VulkanResourceState to,
        uint mipLevels = Vk.RemainingMipLevels,
        uint arrayLayers = Vk.RemainingArrayLayers,
        bool sampleable = true)
    {
        bool depthStencil = (aspect & (ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit)) != 0;
        (PipelineStageFlags2 srcStages, AccessFlags2 srcAccess, ImageLayout oldLayout) =
            VulkanAccessMap.Resolve(from, depthStencil, sampleable);
        (PipelineStageFlags2 dstStages, AccessFlags2 dstAccess, ImageLayout newLayout) =
            VulkanAccessMap.Resolve(to, depthStencil, sampleable);

        _imageBarriers.Add(new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStages,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStages,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = mipLevels,
                BaseArrayLayer = 0,
                LayerCount = arrayLayers,
            },
        });
    }

    /// <summary>
    /// Adds an image transition that hands ownership from one queue family to another, as the release
    /// half on the source queue and the acquire half on the destination.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="aspect">Which aspect the transition covers.</param>
    /// <param name="from">What it was used for on the source queue.</param>
    /// <param name="to">What it will be used for on the destination queue.</param>
    /// <param name="sourceFamily">The queue family giving the image up.</param>
    /// <param name="destinationFamily">The queue family taking it on.</param>
    /// <param name="release">
    /// Whether this is the release half. The release names no destination access and the acquire names no
    /// source access, because each half runs on a queue that cannot see the other's work; the ordering
    /// between them comes from the semaphore, not from the barrier.
    /// </param>
    public void AddImageOwnershipTransfer(
        Image image,
        ImageAspectFlags aspect,
        VulkanResourceState from,
        VulkanResourceState to,
        uint sourceFamily,
        uint destinationFamily,
        bool release)
    {
        bool depthStencil = (aspect & (ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit)) != 0;
        (PipelineStageFlags2 srcStages, AccessFlags2 srcAccess, ImageLayout oldLayout) =
            VulkanAccessMap.Resolve(from, depthStencil);
        (PipelineStageFlags2 dstStages, AccessFlags2 dstAccess, ImageLayout newLayout) =
            VulkanAccessMap.Resolve(to, depthStencil);

        _imageBarriers.Add(new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = release ? srcStages : PipelineStageFlags2.None,
            SrcAccessMask = release ? srcAccess : AccessFlags2.None,
            DstStageMask = release ? PipelineStageFlags2.None : dstStages,
            DstAccessMask = release ? AccessFlags2.None : dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = sourceFamily,
            DstQueueFamilyIndex = destinationFamily,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = Vk.RemainingMipLevels,
                BaseArrayLayer = 0,
                LayerCount = Vk.RemainingArrayLayers,
            },
        });
    }

    /// <summary>Issues everything collected as one dependency, and empties the batch.</summary>
    /// <param name="commandBuffer">The command buffer to record into.</param>
    public void Flush(CommandBuffer commandBuffer)
    {
        if (IsEmpty)
        {
            return;
        }

        ImageMemoryBarrier2[] images = _imageBarriers.ToArray();
        BufferMemoryBarrier2[] buffers = _bufferBarriers.ToArray();
        MemoryBarrier2 memory = _memoryBarrier;

        fixed (ImageMemoryBarrier2* firstImage = images)
        fixed (BufferMemoryBarrier2* firstBuffer = buffers)
        {
            DependencyInfo info = new()
            {
                SType = StructureType.DependencyInfo,
                MemoryBarrierCount = _hasMemoryBarrier ? 1u : 0u,
                PMemoryBarriers = _hasMemoryBarrier ? &memory : null,
                BufferMemoryBarrierCount = (uint)buffers.Length,
                PBufferMemoryBarriers = buffers.Length > 0 ? firstBuffer : null,
                ImageMemoryBarrierCount = (uint)images.Length,
                PImageMemoryBarriers = images.Length > 0 ? firstImage : null,
            };

            _vk.CmdPipelineBarrier2(commandBuffer, &info);
        }

        Reset();
    }

    /// <summary>Throws away everything collected without issuing it.</summary>
    public void Reset()
    {
        _imageBarriers.Clear();
        _bufferBarriers.Clear();
        _hasMemoryBarrier = false;
        _memoryBarrier = default;
    }
}
