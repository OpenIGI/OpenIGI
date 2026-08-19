using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The per-frame synchronisation ring: it hands out a command buffer to record into, and turns the
/// recorded frame into a submission and a present without the CPU stalling on the GPU.
/// </summary>
/// <remarks>
/// <para>
/// Two frames are kept in flight. Each has its own command buffer, a semaphore the acquire signals when
/// its image is ready, and a fence the submission signals when the GPU is done — so while the GPU draws
/// frame N the CPU records frame N+1, and the fence is what stops it from lapping the GPU and
/// overwriting a command buffer still in use.
/// </para>
/// <para>
/// The render-finished semaphores are per <em>image</em>, not per frame in flight: a present waits on
/// one, and it must not be reused until that present has consumed it, which is tied to how many images
/// the swapchain has rather than to how many frames are in flight. They are rebuilt with the swapchain.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanFrameLoop : IDisposable
{
    /// <summary>How many frames the CPU may record ahead of the GPU.</summary>
    public const int MaxFramesInFlight = 2;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly CommandBuffer[] _commandBuffers;
    private readonly Semaphore[] _imageAvailable = new Semaphore[MaxFramesInFlight];
    private readonly Fence[] _inFlight = new Fence[MaxFramesInFlight];

    private VulkanSwapchain _swapchain;
    private Semaphore[] _renderFinished = [];
    private int _currentFrame;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanFrameLoop"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to synchronise against.</param>
    /// <param name="commands">The pool the per-frame command buffers come from.</param>
    /// <param name="swapchain">The swapchain being presented to.</param>
    public VulkanFrameLoop(Vk vk, VulkanDevice device, VulkanCommands commands, VulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(swapchain);
        _vk = vk;
        _device = device;
        _swapchain = swapchain;
        _commandBuffers = commands.AllocatePrimary(MaxFramesInFlight);

        SemaphoreCreateInfo semaphoreInfo = new() { SType = StructureType.SemaphoreCreateInfo };

        // The fences start signalled so the first wait on each returns at once — otherwise frame zero
        // would wait forever on a fence nothing has submitted.
        FenceCreateInfo fenceInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            Semaphore semaphore;
            VkCheck.Ok(_vk.CreateSemaphore(device.Device, &semaphoreInfo, null, &semaphore), "vkCreateSemaphore");
            _imageAvailable[i] = semaphore;

            Fence fence;
            VkCheck.Ok(_vk.CreateFence(device.Device, &fenceInfo, null, &fence), "vkCreateFence");
            _inFlight[i] = fence;
        }

        RebuildImageSemaphores();
    }

    /// <summary>Gets the index of the frame in flight currently being recorded, for per-frame resources.</summary>
    public int CurrentFrame => _currentFrame;

    /// <summary>Points the frame loop at a rebuilt swapchain and refreshes the per-image semaphores.</summary>
    /// <param name="swapchain">The new swapchain, which may hold a different number of images.</param>
    public void OnSwapchainRecreated(VulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        _swapchain = swapchain;
        RebuildImageSemaphores();
    }

    /// <summary>Waits for this frame's slot to be free, then acquires an image and opens its buffer.</summary>
    /// <param name="commandBuffer">Receives the command buffer to record the frame into.</param>
    /// <param name="imageIndex">Receives the acquired swapchain image index.</param>
    /// <returns>
    /// <see langword="true"/> when a frame was opened. <see langword="false"/> means the swapchain is out
    /// of date and the caller must rebuild it and try again; nothing was recorded.
    /// </returns>
    public bool BeginFrame(out CommandBuffer commandBuffer, out uint imageIndex)
    {
        commandBuffer = default;
        imageIndex = 0;

        Fence fence = _inFlight[_currentFrame];
        VkCheck.Ok(_vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");

        Result acquire = _swapchain.AcquireNextImage(_imageAvailable[_currentFrame], out imageIndex);
        if (acquire == Result.ErrorOutOfDateKhr)
        {
            return false;
        }

        // The fence is reset only now that the frame is certain to be submitted; resetting it before a
        // possible out-of-date return would leave it unsignalled with nothing to signal it.
        VkCheck.Ok(_vk.ResetFences(_device.Device, 1, &fence), "vkResetFences");

        commandBuffer = _commandBuffers[_currentFrame];
        VkCheck.Ok(_vk.ResetCommandBuffer(commandBuffer, 0), "vkResetCommandBuffer");

        CommandBufferBeginInfo beginInfo = new() { SType = StructureType.CommandBufferBeginInfo };
        VkCheck.Ok(_vk.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        return true;
    }

    /// <summary>Closes the frame's buffer, submits it, and presents the image.</summary>
    /// <param name="imageIndex">The image index returned by <see cref="BeginFrame"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the frame presented cleanly; <see langword="false"/> when the
    /// swapchain went out of date and must be rebuilt before the next frame.
    /// </returns>
    public bool EndFrame(uint imageIndex)
    {
        CommandBuffer commandBuffer = _commandBuffers[_currentFrame];
        VkCheck.Ok(_vk.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

        Semaphore wait = _imageAvailable[_currentFrame];
        Semaphore signal = _renderFinished[imageIndex];
        PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &wait,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signal,
        };

        VkCheck.Ok(
            _vk.QueueSubmit(_device.Graphics, 1, &submitInfo, _inFlight[_currentFrame]),
            "vkQueueSubmit");

        Result present = _swapchain.Present(signal, imageIndex);
        _currentFrame = (_currentFrame + 1) % MaxFramesInFlight;
        return present == Result.Success;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.WaitIdle();

        foreach (Semaphore semaphore in _renderFinished)
        {
            _vk.DestroySemaphore(_device.Device, semaphore, null);
        }

        for (int i = 0; i < MaxFramesInFlight; i++)
        {
            _vk.DestroySemaphore(_device.Device, _imageAvailable[i], null);
            _vk.DestroyFence(_device.Device, _inFlight[i], null);
        }
    }

    private void RebuildImageSemaphores()
    {
        foreach (Semaphore semaphore in _renderFinished)
        {
            _vk.DestroySemaphore(_device.Device, semaphore, null);
        }

        _renderFinished = new Semaphore[_swapchain.ImageCount];
        SemaphoreCreateInfo createInfo = new() { SType = StructureType.SemaphoreCreateInfo };
        for (int i = 0; i < _renderFinished.Length; i++)
        {
            Semaphore semaphore;
            VkCheck.Ok(_vk.CreateSemaphore(_device.Device, &createInfo, null, &semaphore), "vkCreateSemaphore");
            _renderFinished[i] = semaphore;
        }
    }
}
