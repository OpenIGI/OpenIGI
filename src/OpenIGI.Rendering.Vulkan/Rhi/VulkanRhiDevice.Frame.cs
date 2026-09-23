using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>The frame: acquiring an image, submitting what was recorded into it, and presenting.</summary>
/// <remarks>
/// <para>
/// The interface has no swapchain, which is the point: it asks for the texture this frame draws into and
/// says when the frame is over. Everything below — the rotation, the fences, the two semaphores that
/// order an acquire against a present — is this backend's business and has no counterpart on the other.
/// </para>
/// <para>
/// Submissions made during a frame are held rather than sent, and go out as one submit when the frame
/// ends. That is what lets the acquire semaphore be waited on once and the present semaphore be signalled
/// once, which is the ordering a swapchain needs and which the interface's own Submit cannot express.
/// </para>
/// </remarks>
internal sealed unsafe partial class VulkanRhiDevice
{
    private readonly List<CommandBuffer> _pending = [];
    private VulkanSwapchain? _swapchain;
    private VulkanRhiTexture[] _offscreen = [];
    private Semaphore[] _acquired = [];
    private Semaphore[] _presentable = [];
    private Fence[] _inFlight = [];
    private VulkanRhiTexture?[] _frameTextures = [];
    private uint _imageIndex;
    private int _frameSlot;
    private bool _inFrame;

    /// <summary>Gives this device the swapchain it presents through.</summary>
    /// <param name="swapchain">The swapchain, which the caller keeps ownership of.</param>
    /// <remarks>
    /// Handed in rather than made here, because the surface it is built on belongs to the window and the
    /// window belongs to the host. A device with none is headless and says so when asked for a frame.
    /// </remarks>
    public void AttachSwapchain(VulkanSwapchain swapchain)
    {
        ArgumentNullException.ThrowIfNull(swapchain);
        ThrowIfDisposed();

        _swapchain = swapchain;
        CreateFrameSync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The swapchain's own, which is what the window system chose rather than what the renderer would
    /// have liked: this backend's surface prefers an unsigned-normalised image, because the legacy path
    /// encodes its own output. A pipeline built for anything else draws into it invalidly.
    /// </remarks>
    public RhiTextureFormat SurfaceFormat => _offscreen.Length > 0
        ? _offscreen[0].Format
        : _swapchain is { } swapchain
            ? VulkanRhiTranslation.TextureFormat(swapchain.Format)
            : RhiTextureFormat.Bgra8Unorm;

    /// <summary>Gives this device somewhere to draw when there is no window to present to.</summary>
    /// <param name="width">The frame's width in pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="format">What the frame is drawn in.</param>
    /// <remarks>
    /// <para>
    /// A rotation of ordinary textures in place of a swapchain's images. Everything above sees the same
    /// frame — begin, draw, end — and what changes is only that nothing is presented and nothing waits
    /// on a display.
    /// </para>
    /// <para>
    /// This is what a headless capture draws into, and what lets the whole renderer be run and read back
    /// without a window. It is not a test fixture: the game takes frames this way for its own
    /// screenshots.
    /// </para>
    /// </remarks>
    public void AttachOffscreen(int width, int height, RhiTextureFormat format)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        ReleaseOffscreen();
        CreateFrameSync();

        _offscreen = new VulkanRhiTexture[_inFlight.Length];

        for (int i = 0; i < _offscreen.Length; i++)
        {
            _offscreen[i] = (VulkanRhiTexture)CreateTexture(new RhiTextureDescriptor(
                width,
                height,
                format,
                RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.Sampled,
                Label: $"offscreen-{i}"));
        }
    }

    /// <summary>Gets the texture a frame drawn offscreen landed in.</summary>
    /// <param name="index">The frame's index, as it was handed out.</param>
    /// <returns>The texture.</returns>
    public IRhiTexture OffscreenTarget(int index) => _offscreen[index % _offscreen.Length];

    /// <inheritdoc/>
    public bool BeginFrame(out RhiFrame frame)
    {
        ThrowIfDisposed();
        frame = default;

        if (_offscreen.Length > 0)
        {
            return BeginOffscreenFrame(out frame);
        }

        VulkanSwapchain swapchain = _swapchain
            ?? throw new GraphicsDeviceException("This device has no swapchain, so it presents no frames.");

        if (_inFrame)
        {
            throw new GraphicsDeviceException("A frame is already begun; end it before beginning another.");
        }

        Fence fence = _inFlight[_frameSlot];
        VkCheck.Ok(_vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");

        // The slot's previous frame has certainly finished, so whatever it was holding can go now.
        _device.GraphicsQueue.Poll();
        RetireFrameTexture(_frameSlot);
        ReleaseSlot(_frameSlot);

        Result acquired = swapchain.AcquireNextImage(_acquired[_frameSlot], out _imageIndex);
        if (acquired is Result.ErrorOutOfDateKhr)
        {
            NeedsReconfigure = true;
            return false;
        }

        if (acquired is not (Result.Success or Result.SuboptimalKhr))
        {
            VkCheck.Ok(acquired, "vkAcquireNextImageKHR");
        }

        // Suboptimal presents correctly and says this configuration has been outgrown, so the frame is
        // drawn and the swapchain rebuilt after it.
        NeedsReconfigure = acquired == Result.SuboptimalKhr;

        // Reset only now that the frame is certain to be submitted: resetting before a possible
        // out-of-date return would leave the fence unsignalled with nothing to signal it.
        VkCheck.Ok(_vk.ResetFences(_device.Device, 1, &fence), "vkResetFences");

        VulkanRhiTexture target = WrapSwapchainImage(swapchain, _imageIndex);
        _frameTextures[_frameSlot] = target;
        _inFrame = true;

        frame = new RhiFrame(target.View, (int)_imageIndex, _frameSlot);
        return true;
    }

    /// <inheritdoc/>
    public bool EndFrame(in RhiFrame frame)
    {
        ThrowIfDisposed();

        if (_offscreen.Length > 0)
        {
            return EndOffscreenFrame();
        }

        VulkanSwapchain swapchain = _swapchain
            ?? throw new GraphicsDeviceException("This device has no swapchain, so it presents no frames.");

        if (!_inFrame)
        {
            throw new GraphicsDeviceException("No frame is begun, so there is none to end.");
        }

        // The image has to be in the layout a present expects, and nothing above this knows that layout
        // exists. One more barrier, on a command buffer of its own so it runs after everything recorded.
        CommandBuffer transition = _commands.AllocatePrimary(1)[0];
        CommandBufferBeginInfo begin = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        VkCheck.Ok(_vk.BeginCommandBuffer(transition, &begin), "vkBeginCommandBuffer");

        if (_frameTextures[_frameSlot] is { } target)
        {
            Transition(transition, target, ImageLayout.PresentSrcKhr);
        }

        VkCheck.Ok(_vk.EndCommandBuffer(transition), "vkEndCommandBuffer");

        // Writes made since the last submission go ahead of the transition that ends the frame.
        CommandBuffer upload = TakeUpload(_pendingStaging);
        if (upload.Handle != 0)
        {
            _pending.Add(upload);
        }

        _pending.Add(transition);

        CommandBuffer[] buffers = [.. _pending];
        _pending.Clear();
        HoldForSlot(_frameSlot, buffers);

        Semaphore wait = _acquired[_frameSlot];
        Semaphore signal = _presentable[_frameSlot];
        PipelineStageFlags stage = PipelineStageFlags.ColorAttachmentOutputBit;

        // The present's semaphore, and the count a readback of this frame waits for.
        Semaphore* signals = stackalloc Semaphore[2] { signal, _timeline };
        ulong* values = stackalloc ulong[2] { 0, HasTimeline ? NextSignal() : 0 };
        TimelineSemaphoreSubmitInfo counted = new()
        {
            SType = StructureType.TimelineSemaphoreSubmitInfo,
            SignalSemaphoreValueCount = 2,
            PSignalSemaphoreValues = values,
        };

        fixed (CommandBuffer* first = buffers)
        {
            SubmitInfo submit = new()
            {
                SType = StructureType.SubmitInfo,
                PNext = HasTimeline ? &counted : null,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &wait,
                PWaitDstStageMask = &stage,
                CommandBufferCount = (uint)buffers.Length,
                PCommandBuffers = first,
                SignalSemaphoreCount = HasTimeline ? 2u : 1u,
                PSignalSemaphores = signals,
            };

            VkCheck.Ok(
                _vk.QueueSubmit(_device.Graphics, 1, &submit, _inFlight[_frameSlot]),
                "vkQueueSubmit");
        }

        Result presented = swapchain.Present(signal, _imageIndex);
        _inFrame = false;
        _frameSlot = (_frameSlot + 1) % _inFlight.Length;

        if (presented is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
        {
            NeedsReconfigure = true;
            return false;
        }

        VkCheck.Ok(presented, "vkQueuePresentKHR");
        return !NeedsReconfigure;
    }

    /// <inheritdoc/>
    public void ConfigureSurface(int width, int height, bool vsync)
    {
        ThrowIfDisposed();

        if (_offscreen.Length > 0)
        {
            RhiTextureFormat format = _offscreen[0].Format;

            _vk.DeviceWaitIdle(_device.Device);
            AttachOffscreen(width, height, format);
            return;
        }

        VulkanSwapchain swapchain = _swapchain
            ?? throw new GraphicsDeviceException("This device has no swapchain to configure.");

        _vk.DeviceWaitIdle(_device.Device);

        for (int i = 0; i < _frameTextures.Length; i++)
        {
            RetireFrameTexture(i);
        }

        swapchain.Recreate(width, height, vsync);
        NeedsReconfigure = false;
    }

    /// <summary>Gets whether the swapchain has to be rebuilt before the next frame.</summary>
    public bool NeedsReconfigure { get; private set; }

    /// <summary>Holds a submission until the frame ends, or sends it at once outside one.</summary>
    /// <param name="buffers">What was recorded.</param>
    /// <remarks>
    /// Inside a frame there is an acquire to wait on and a present to signal, and both belong to one
    /// submit. Outside a frame — a load-time upload, a test — there is nothing to order against, so it
    /// goes immediately.
    /// </remarks>
    private void SubmitOrHold(CommandBuffer[] buffers)
    {
        if (_inFrame)
        {
            // The writes made before this submission land before it, as the interface promises.
            CommandBuffer upload = TakeUpload(_pendingStaging);
            if (upload.Handle != 0)
            {
                _pending.Add(upload);
            }

            _pending.AddRange(buffers);
            return;
        }

        SubmitLoose(buffers);
    }

    /// <summary>Wraps one swapchain image as a texture, without owning it.</summary>
    /// <remarks>
    /// The image belongs to the swapchain and is destroyed with it, so this one must not free it. Its
    /// view is its own, and is destroyed when the frame that made it comes round again.
    /// </remarks>
    private VulkanRhiTexture WrapSwapchainImage(VulkanSwapchain swapchain, uint index)
    {
        RhiTextureDescriptor descriptor = new(
            (int)swapchain.Extent.Width,
            (int)swapchain.Extent.Height,
            VulkanRhiTranslation.TextureFormat(swapchain.Format),

            // What the swapchain's images are actually created with. The copy source is what a
            // screenshot reads the presented frame back through, and a descriptor that understated it
            // would have the renderer refuse a copy the image supports.
            RhiTextureUsage.RenderTarget | RhiTextureUsage.CopySource | RhiTextureUsage.CopyDestination,
            Label: "swapchain");

        return new VulkanRhiTexture(
            _vk,
            _device.Device,
            _allocator,
            swapchain.ImageAt(index),
            default,
            descriptor,
            owned: false);
    }

    private void RetireFrameTexture(int slot)
    {
        if (slot < _frameTextures.Length && _frameTextures[slot] is { } texture)
        {
            texture.Dispose();
            _frameTextures[slot] = null;
        }
    }

    private void CreateFrameSync()
    {
        const int Frames = 2;

        _acquired = new Semaphore[Frames];
        _presentable = new Semaphore[Frames];
        _inFlight = new Fence[Frames];
        _frameTextures = new VulkanRhiTexture?[Frames];
        CreateSubmissionState(Frames);

        SemaphoreCreateInfo semaphore = new() { SType = StructureType.SemaphoreCreateInfo };

        // Created already signalled, so the first frame does not wait for one that never ran.
        FenceCreateInfo fence = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        for (int i = 0; i < Frames; i++)
        {
            Semaphore acquired;
            Semaphore presentable;
            Fence inFlight;

            VkCheck.Ok(_vk.CreateSemaphore(_device.Device, &semaphore, null, &acquired), "vkCreateSemaphore");
            VkCheck.Ok(_vk.CreateSemaphore(_device.Device, &semaphore, null, &presentable), "vkCreateSemaphore");
            VkCheck.Ok(_vk.CreateFence(_device.Device, &fence, null, &inFlight), "vkCreateFence");

            _acquired[i] = acquired;
            _presentable[i] = presentable;
            _inFlight[i] = inFlight;
        }
    }

    /// <summary>Hands out the next offscreen frame, once the one that used its slot has finished.</summary>
    private bool BeginOffscreenFrame(out RhiFrame frame)
    {
        if (_inFrame)
        {
            throw new GraphicsDeviceException("A frame is already begun; end it before beginning another.");
        }

        Fence fence = _inFlight[_frameSlot];
        VkCheck.Ok(_vk.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue), "vkWaitForFences");
        VkCheck.Ok(_vk.ResetFences(_device.Device, 1, &fence), "vkResetFences");

        _device.GraphicsQueue.Poll();
        ReleaseSlot(_frameSlot);

        _inFrame = true;
        frame = new RhiFrame(_offscreen[_frameSlot].View, _frameSlot, _frameSlot);
        return true;
    }

    /// <summary>Submits what an offscreen frame recorded. Nothing is presented and nothing is waited on.</summary>
    private bool EndOffscreenFrame()
    {
        if (!_inFrame)
        {
            throw new GraphicsDeviceException("No frame is begun, so there is none to end.");
        }

        CommandBuffer upload = TakeUpload(_pendingStaging);
        if (upload.Handle != 0)
        {
            _pending.Add(upload);
        }

        CommandBuffer[] buffers = [.. _pending];
        _pending.Clear();
        HoldForSlot(_frameSlot, buffers);

        // Submitted even with nothing recorded: the fence this slot waits on next time, and the count a
        // readback of this frame waits for, both need something to signal them.
        Semaphore timeline = _timeline;
        ulong value = HasTimeline ? NextSignal() : 0;
        TimelineSemaphoreSubmitInfo counted = new()
        {
            SType = StructureType.TimelineSemaphoreSubmitInfo,
            SignalSemaphoreValueCount = 1,
            PSignalSemaphoreValues = &value,
        };

        fixed (CommandBuffer* first = buffers)
        {
            SubmitInfo submit = new()
            {
                SType = StructureType.SubmitInfo,
                PNext = HasTimeline ? &counted : null,
                CommandBufferCount = (uint)buffers.Length,
                PCommandBuffers = buffers.Length > 0 ? first : null,
                SignalSemaphoreCount = HasTimeline ? 1u : 0u,
                PSignalSemaphores = &timeline,
            };

            VkCheck.Ok(
                _vk.QueueSubmit(_device.Graphics, 1, &submit, _inFlight[_frameSlot]),
                "vkQueueSubmit");
        }

        _inFrame = false;
        _frameSlot = (_frameSlot + 1) % _inFlight.Length;
        return true;
    }

    private void ReleaseOffscreen()
    {
        foreach (VulkanRhiTexture texture in _offscreen)
        {
            texture.Dispose();
        }

        _offscreen = [];
    }

    private void DestroyFrameSync()
    {
        for (int i = 0; i < _inFlight.Length; i++)
        {
            RetireFrameTexture(i);
            _vk.DestroySemaphore(_device.Device, _acquired[i], null);
            _vk.DestroySemaphore(_device.Device, _presentable[i], null);
            _vk.DestroyFence(_device.Device, _inFlight[i], null);
        }

        ReleaseOffscreen();

        _acquired = [];
        _presentable = [];
        _inFlight = [];
        _frameTextures = [];
    }
}
