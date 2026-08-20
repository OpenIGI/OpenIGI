using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The graphics command pool, and the one-off submissions that stand outside the frame loop.
/// </summary>
/// <remarks>
/// <para>
/// Every command the backend issues is recorded into a command buffer allocated from a pool tied to the
/// graphics family. The per-frame buffers the frame loop owns come from here; so do the short-lived
/// buffers that upload a texture or transition an image, which do not belong to any frame and are
/// submitted on their own through <see cref="SingleTime"/>.
/// </para>
/// <para>
/// <see cref="SingleTime"/> submits and then waits: it is for load-time work — staging a mesh, building
/// a mip chain, moving an image to its first layout — where the simplicity of a synchronous copy is
/// worth more than the pipelining a fence-and-poll would keep. Nothing on the frame path uses it.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanCommands : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly CommandPool _pool;
    private readonly CommandPool _singleTimePool;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanCommands"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool is created on.</param>
    /// <exception cref="GraphicsDeviceException">The command pool could not be created.</exception>
    public VulkanCommands(Vk vk, VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device;

        CommandPoolCreateInfo createInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = device.Selection.Queues.Graphics.Family,

            // The frame buffers are re-recorded every frame, so the pool must let a buffer be reset on
            // its own rather than only by resetting the whole pool.
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };

        CommandPool pool;
        VkCheck.Ok(_vk.CreateCommandPool(device.Device, &createInfo, null, &pool), "vkCreateCommandPool");
        _pool = pool;

        // A separate pool for one-off uploads. A storage mesh uploads mid-frame, while a frame command
        // buffer from the pool above is still being recorded; sharing a pool would mean allocating and
        // freeing from it while another of its buffers is recording, which is a host-synchronisation
        // hazard. Its own pool keeps the two apart.
        CommandPoolCreateInfo singleTimeInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = device.Selection.Queues.Graphics.Family,
            Flags = CommandPoolCreateFlags.TransientBit,
        };
        CommandPool singleTimePool;
        VkCheck.Ok(_vk.CreateCommandPool(device.Device, &singleTimeInfo, null, &singleTimePool), "vkCreateCommandPool");
        _singleTimePool = singleTimePool;
    }

    /// <summary>Allocates primary command buffers from the pool.</summary>
    /// <param name="count">How many to allocate.</param>
    /// <returns>The allocated buffers.</returns>
    /// <exception cref="GraphicsDeviceException">The buffers could not be allocated.</exception>
    public CommandBuffer[] AllocatePrimary(int count)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = (uint)count,
        };

        CommandBuffer[] buffers = new CommandBuffer[count];
        fixed (CommandBuffer* first = buffers)
        {
            VkCheck.Ok(_vk.AllocateCommandBuffers(_device.Device, &allocInfo, first), "vkAllocateCommandBuffers");
        }

        return buffers;
    }

    /// <summary>Records and submits a one-off command buffer, then waits for it to finish.</summary>
    /// <param name="record">Records the commands into the buffer.</param>
    /// <exception cref="GraphicsDeviceException">The submission failed.</exception>
    public void SingleTime(Action<CommandBuffer> record)
    {
        ArgumentNullException.ThrowIfNull(record);

        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _singleTimePool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        CommandBuffer commandBuffer;
        VkCheck.Ok(_vk.AllocateCommandBuffers(_device.Device, &allocInfo, &commandBuffer), "vkAllocateCommandBuffers");

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkCheck.Ok(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        record(commandBuffer);
        VkCheck.Ok(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };

        // A short-lived fence rather than a queue-wait-idle, so the wait is scoped to this submission
        // and not to everything the queue happens to be doing.
        FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
        Fence fence;
        VkCheck.Ok(_vk.CreateFence(_device.Device, &fenceInfo, null, &fence), "vkCreateFence");

        try
        {
            VkCheck.Ok(_vk.QueueSubmit(_device.Graphics, 1, &submitInfo, fence), "vkQueueSubmit");
            VkCheck.Ok(_vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
        }
        finally
        {
            _vk.DestroyFence(_device.Device, fence, null);
            _vk.FreeCommandBuffers(_device.Device, _singleTimePool, 1, &commandBuffer);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyCommandPool(_device.Device, _singleTimePool, null);
        _vk.DestroyCommandPool(_device.Device, _pool, null);
    }
}
