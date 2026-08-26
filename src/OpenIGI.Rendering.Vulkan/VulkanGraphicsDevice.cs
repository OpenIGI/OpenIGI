using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Pipelines;
using OpenIGI.Rendering.Vulkan.Shaders;
using OpenIGI.Rendering.Vulkan.Textures;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// The Vulkan implementation of the renderer seam.
/// </summary>
/// <remarks>
/// <para>
/// This is the counterpart to the OpenGL backend, built on an explicit API: the device owns its
/// swapchain, command buffers, synchronisation, memory and pipelines, and records each frame into a
/// command buffer it submits and presents itself — there is no driver-side present to hand off to.
/// </para>
/// <para>
/// The foundation covers the original's direct path: clear, the two-dimensional triangle primitive
/// every HUD and menu funnels through, textures, and present. It reports no scene capture and no
/// compute, so the engine runs exactly that path while the deferred pipeline, compute, and the
/// hardware-ray-traced GI are layered on behind the same seam. The adapter's ray-tracing support is
/// detected at selection and carried in <see cref="RayTracingAvailable"/> so that switch is a
/// capability check rather than a re-inspection.
/// </para>
/// </remarks>
public sealed partial class VulkanGraphicsDevice : IGraphicsDevice
{
    private readonly IIgiLogger _logger;
    private readonly VulkanInstance _instance;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanCommands _commands;
    private readonly ShaderCompiler _shaderCompiler;
    private readonly Sprite2DPipeline _sprite2D;
    private readonly Sprite2DGeometry _sprite2DGeometry;
    private readonly TextureDescriptorCache _textureDescriptors;
    private readonly Mesh3DPipeline _mesh3D;
    private readonly MeshTextureDescriptorCache _meshTextureDescriptors;
    private readonly Deferred.DeferredRenderer _deferred;
    private readonly VulkanOcclusionQueries _occlusion;

    // The hardware-ray-tracing foundation. Both are null on an adapter that cannot carry them, which is
    // the one question every pass that wants to trace asks before it does.
    private readonly RayTracing.VulkanRayTracingApi? _rayTracing;
    private readonly Bindless.VulkanBindlessDescriptors? _bindless;

    // The traceable copy of the world, accumulated from what the geometry pass is asked to draw. Null on
    // an adapter that cannot trace, which is the one question every pass that wants a ray asks first.
    private readonly RayTracing.VulkanWorldScene? _worldScene;

    // Something legal for a tracing shader to bind before the world's own structure exists.
    private readonly RayTracing.VulkanPlaceholderScene? _placeholderScene;

    // Carries recorded work to the queues and retires it once the GPU is done. Threading is opt-in while
    // the frame path still submits and presents in lock step; the machinery is the same either way, so
    // turning it on is a change of one environment variable rather than of the code that uses it.
    private readonly Queues.VulkanSubmissionQueue _submission;

    private readonly Dictionary<uint, VulkanTexture> _textures = [];
    private uint _nextTextureId = 1;
    private readonly TextureHandle _whiteTexture;

    // Rebuilt in place on resize through their own Recreate methods rather than reassigned, so the
    // references themselves never change.
    private readonly VulkanSwapchain _swapchain;
    private readonly PresentTarget _presentTarget;
    private readonly VulkanFrameLoop _frameLoop;

    // GPU per-stage timing, when the device can carry timestamps. Null on a device that cannot.
    private readonly VulkanStageProfiler? _stageProfiler;

    // A persistent copy of the last presented back buffer, for non-disruptive screenshots. The copy runs
    // only for a few frames after CaptureFrame is last called, not every frame: a full-resolution back
    // buffer read-back on a frame nobody screenshots is pure waste, and it is what made ordinary gameplay
    // slower than it should be. A consumer that wants captures (the menu backdrop, a screenshot loop) calls
    // CaptureFrame each frame and keeps the countdown alive; gameplay that never calls it pays nothing.
    private Memory.VulkanBuffer? _captureStaging;
    private Extent2D _captureExtent;
    private int _captureCountdown;

    // A session that was started to capture keeps the per-frame copy running from the first frame. That
    // matters for correctness, not only cost: a copy recorded into the frame's own command buffer is
    // ordered against the acquire that frame waited on, whereas reading an image that has already been
    // handed to the presentation engine races with the engine still scanning it out.
    private readonly bool _captureAlways;
    private bool _hasPresented;

    // Frame state. A frame's command buffer is opened lazily; no render pass begins until a draw needs
    // one, so the direct path opens the present pass and the deferred path opens its own instead.
    private bool _frameOpen;
    private bool _presentPassActive;
    private CommandBuffer _frameCommandBuffer;
    private uint _frameImageIndex;
    private Bgra32 _clearColour = Bgra32.Black;
    private Viewport _viewport;
    private int _projectionWidth = 1;
    private int _projectionHeight = 1;
    private bool _vsync = true;
    private bool _disposed;

    // Godot's and the reference's rule: a resource is never freed by stalling the GPU to be sure it is idle.
    // A destroyed resource is queued with the frame it was retired on and disposed once that many frames
    // in flight have cycled past — its fence has been waited on by the frame loop by then, so nothing is
    // still reading it. That turns every DestroyMesh / DestroyTexture from a full device stall into an
    // enqueue.
    private readonly Queue<(ulong Frame, IDisposable Resource)> _pendingDeletions = new();
    private ulong _frameCounter;

    /// <summary>Initialises a new instance of the <see cref="VulkanGraphicsDevice"/> class.</summary>
    /// <param name="surfaceProvider">The window's Vulkan surface bridge.</param>
    /// <param name="mode">The initial back-buffer mode.</param>
    /// <param name="enableValidation">Whether to attach the validation layer when it is installed.</param>
    /// <param name="enableDeferred">
    /// Whether to offer the deferred capture path. Off by default while it awaits runtime validation, so
    /// the simpler direct path is what a Vulkan session renders unless it is opted into.
    /// </param>
    /// <param name="enableCapture">
    /// Whether the session will read frames back — a screenshot or a flight. When set, every frame copies
    /// the back buffer as part of its own command buffer, which is both the only correctly synchronised
    /// way to read a frame and a cost an ordinary session should not pay.
    /// </param>
    /// <param name="logger">Where to report backend diagnostics.</param>
    /// <exception cref="GraphicsDeviceException">The device could not be created.</exception>
    public VulkanGraphicsDevice(
        IVulkanSurfaceProvider surfaceProvider,
        in DisplayMode mode,
        bool enableValidation = false,
        bool enableDeferred = false,
        bool enableCapture = false,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(surfaceProvider);
        _logger = logger ?? NullLogger.Instance;
        CurrentMode = mode;
        _vsync = true;
        _deferredEnabled = enableDeferred;
        _captureAlways = enableCapture;

        _instance = new VulkanInstance(surfaceProvider, enableValidation, _logger);
        PhysicalDeviceSelection selection = new PhysicalDeviceSelector(_instance, _logger).Select();
        _device = new VulkanDevice(_instance, selection, _logger);
        _allocator = new VulkanAllocator(_device);
        _commands = new VulkanCommands(_instance.Vk, _device);
        _submission = new Queues.VulkanSubmissionQueue(
            _device.Queues,
            threaded: Environment.GetEnvironmentVariable("OPENIGI_VK_SUBMIT_THREAD") == "1");

        int width = Math.Max(1, mode.Width);
        int height = Math.Max(1, mode.Height);
        _swapchain = new VulkanSwapchain(_instance, _device, width, height, _vsync);
        _presentTarget = new PresentTarget(_instance.Vk, _device, _allocator, _swapchain);
        _frameLoop = new VulkanFrameLoop(_instance.Vk, _device, _commands, _swapchain);

        // GPU stage timing rides in the command stream on timestamp queries. Only stand it up where the
        // device times all graphics and compute work and reports a tick period to scale by; where it does
        // not, the profiler stays null and the overlay simply shows no per-stage figures.
        _instance.Vk.GetPhysicalDeviceProperties(_device.PhysicalDevice, out PhysicalDeviceProperties deviceProperties);
        if (deviceProperties.Limits.TimestampComputeAndGraphics && deviceProperties.Limits.TimestampPeriod > 0f)
        {
            _stageProfiler = new VulkanStageProfiler(
                _instance.Vk, _device, deviceProperties.Limits.TimestampPeriod, VulkanFrameLoop.MaxFramesInFlight);
        }

        _shaderCompiler = new ShaderCompiler();
        _sprite2D = new Sprite2DPipeline(_instance.Vk, _device, _shaderCompiler, _presentTarget.RenderPass);
        _sprite2DGeometry = new Sprite2DGeometry(_instance.Vk, _device, _allocator, VulkanFrameLoop.MaxFramesInFlight);
        _textureDescriptors = new TextureDescriptorCache(_instance.Vk, _device.Device, _sprite2D.DescriptorSetLayout);
        _mesh3D = new Mesh3DPipeline(_instance.Vk, _device, _shaderCompiler, _presentTarget.RenderPass);
        _meshTextureDescriptors = new MeshTextureDescriptorCache(_instance.Vk, _device.Device, _mesh3D.DescriptorSetLayout);
        _deferred = new Deferred.DeferredRenderer(
            _instance.Vk,
            _device,
            _allocator,
            _commands,
            _shaderCompiler,
            _mesh3D.DescriptorSetLayout,
            _presentTarget.RenderPass,
            VulkanFrameLoop.MaxFramesInFlight);

        // The box a query draws tests against the geometry pass's depth, so it shares that pass; it is
        // built here, once the deferred renderer's render passes exist.
        _occlusion = new VulkanOcclusionQueries(
            _instance.Vk, _device, _shaderCompiler, _deferred.GeometryPass, VulkanFrameLoop.MaxFramesInFlight);

        RayTracingAvailable = selection.SupportsRayTracing;

        // The bindless heaps come first: an acceleration structure is reached through them, so a scene
        // that could be built but not reached would be of no use to anything.
        if (_device.Capabilities.DescriptorBuffer)
        {
            _bindless = new Bindless.VulkanBindlessDescriptors(_instance, _device, _allocator);
        }

        if (RayTracingAvailable && _bindless is not null)
        {
            _rayTracing = new RayTracing.VulkanRayTracingApi(_instance, _device);

            // On by default where the hardware traces at all. Building the world costs time on the frames
            // that admit new geometry, but it is what the indirect light is gathered against, and the
            // structures are built once as the level streams rather than per frame. Set the variable to 0
            // to run without it — the distance field is still there to march.
            if (Environment.GetEnvironmentVariable("OPENIGI_VK_RT_SCENE") != "0")
            {
                _worldScene = new RayTracing.VulkanWorldScene(_instance.Vk, _rayTracing, _device, _allocator);
            }

            // Built whenever the adapter can trace, world scene or not: a shader that names a structure
            // needs one written into its descriptor from its very first dispatch.
            if (_device.Capabilities.RayQuery)
            {
                _placeholderScene = new RayTracing.VulkanPlaceholderScene(
                    _instance.Vk, _rayTracing, _device, _allocator, _commands, _device.GraphicsQueue);
            }

            // Opt-in, like the other Vulkan paths. The test builds and traces a scene of its own, which is
            // a few milliseconds and a little memory at start-up — worth paying deliberately when bringing
            // the hardware path up or diagnosing it, and not worth paying on every launch.
            if (Environment.GetEnvironmentVariable("OPENIGI_VK_RT_SELFTEST") == "1")
            {
                RunRayTracingSelfTest();
            }
        }

        // The compute path carries the real-time GI and distance-field passes, and is on by default so a
        // modern preset renders at full quality without an environment variable set. OPENIGI_VK_COMPUTE=0
        // forces it off, back to the deferred path's baked and probe-field GI.
        //
        // Known caveat while it is finished: a few uploads (the irradiance-volume probe field, the surface
        // cache capture) still submit and wait on their own command buffer rather than recording into the
        // frame's, so a burst of them -- the GI rebake a fast camera move triggers -- can stall long enough
        // to trip the driver's timeout and lose the device. Ordinary play amortises the rebake and does not
        // hit it; the fix is to record those uploads into the frame command buffer like every other pass.
        bool enableCompute = Environment.GetEnvironmentVariable("OPENIGI_VK_COMPUTE") != "0";
        // Tracing the indirect light against real geometry rather than marching the distance field, which
        // is the whole point of building the world: the field is a coarse approximation and the geometry
        // is the thing itself. Only where there is a world to trace, and off if the variable says 0.
        bool rayTracedGi = _placeholderScene is not null
            && _worldScene is not null
            && Environment.GetEnvironmentVariable("OPENIGI_VK_RT_GI") != "0";

        Capabilities = BuildCapabilities(selection, enableDeferred, enableCompute, rayTracedGi);
        _viewport = new Viewport(0, 0, width, height);
        _projectionWidth = width;
        _projectionHeight = height;

        _whiteTexture = CreateWhiteTexture();
        _logger.Information("Vulkan", $"Vulkan backend ready on '{selection.Name}'.");
    }

    /// <inheritdoc/>
    public event EventHandler? DeviceRestored;

    /// <inheritdoc/>
    public GraphicsCapabilities Capabilities { get; }

    /// <summary>Gets a value indicating whether the adapter carries the hardware ray-tracing extensions.</summary>
    /// <remarks>
    /// The seam the software-traced GI is meant to cross. It is reported so a later path can build
    /// acceleration structures and trace on the GPU when the hardware allows, and fall back to the
    /// compute tracer when it does not, without inspecting the adapter a second time.
    /// </remarks>
    public bool RayTracingAvailable { get; }

    /// <inheritdoc/>
    public DisplayMode CurrentMode { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Recorded here and applied by rebuilding the swapchain, since the present mode a v-sync change
    /// asks for is fixed at swapchain creation.
    /// </remarks>
    public bool VSyncEnabled
    {
        get => _vsync;
        set
        {
            if (_vsync == value)
            {
                return;
            }

            _vsync = value;
            RecreateSwapchain();
        }
    }

    /// <inheritdoc/>
    /// <remarks>The platform layer owns the display list; the current mode is always a usable entry.</remarks>
    public IReadOnlyList<DisplayMode> EnumerateDisplayModes() => [CurrentMode];

    /// <inheritdoc/>
    public void SetMode(in DisplayMode mode)
    {
        ThrowIfDisposed();
        if (mode.Width <= 0 || mode.Height <= 0)
        {
            throw new GraphicsDeviceException($"Display mode {mode} has a non-positive dimension.");
        }

        CurrentMode = mode;
        RecreateSwapchain();
        _viewport = new Viewport(0, 0, mode.Width, mode.Height);
        Set2DProjection(mode.Width, mode.Height);
    }

    /// <inheritdoc/>
    /// <remarks>The swapchain owns the mode's resources; unsetting waits for it to be idle.</remarks>
    public void UnsetMode() => _device.WaitIdle();

    /// <inheritdoc/>
    /// <remarks>Vulkan does not lose its device the way Direct3D 7 did; recovery is announced for symmetry.</remarks>
    public void Restore()
    {
        ThrowIfDisposed();
        DeviceRestored?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Clear(Bgra32 color)
    {
        ThrowIfDisposed();
        _clearColour = color;
        EnsureFrame();

        // The clear colour is applied when a render pass opens — as the scene clear during capture, or
        // the present pass's clear on the direct path. Only when the present pass is already live does a
        // clear need issuing into it.
        if (_presentPassActive)
        {
            ClearActivePass(color);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Clears only the depth attachment to its far value, so a foreground 3D layer keeps its own depth
    /// ordering without comparing against the scene already drawn. Colour and depth state are untouched.
    /// </remarks>
    public unsafe void ClearDepth()
    {
        ThrowIfDisposed();
        if (!_presentPassActive && !DeferredPassActive)
        {
            return;
        }

        ClearAttachment attachment = new()
        {
            AspectMask = ImageAspectFlags.DepthBit,
            ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(0.0f, 0) },
        };

        ClearRect rect = new()
        {
            BaseArrayLayer = 0,
            LayerCount = 1,
            Rect = new Rect2D(new Offset2D(0, 0), CurrentExtent),
        };

        _instance.Vk.CmdClearAttachments(_frameCommandBuffer, 1, &attachment, 1, &rect);
    }

    /// <inheritdoc/>
    public void SwapBuffers()
    {
        ThrowIfDisposed();
        if (!_frameOpen)
        {
            return;
        }

        // The present pass must run so the swapchain image ends in the presentable layout; a frame that
        // only cleared or only captured still opens and closes it here.
        EnsurePresentPass();
        EndPresentPassIfActive();

        // Copy the finished back buffer into a persistent staging buffer so a screenshot can read the last
        // presented frame — but only while a consumer is actually asking for captures.
        if (_captureAlways || _captureCountdown > 0)
        {
            if (_captureCountdown > 0)
            {
                _captureCountdown--;
            }

            RecordBackbufferCapture();
        }

        bool presented = _frameLoop.EndFrame(_frameImageIndex);
        _frameOpen = false;
        if (presented)
        {
            _hasPresented = true;
        }
        else
        {
            RecreateSwapchain();
        }
    }

    /// <inheritdoc/>
    public void SetViewport(in Viewport viewport)
    {
        ThrowIfDisposed();
        _viewport = viewport;
        if (_presentPassActive || DeferredPassActive)
        {
            ApplyViewportAndScissor(_frameCommandBuffer, viewport);
        }
    }

    /// <inheritdoc/>
    public void Set2DProjection(int width, int height)
    {
        ThrowIfDisposed();
        _projectionWidth = Math.Max(1, width);
        _projectionHeight = Math.Max(1, height);
    }

    /// <summary>
    /// Traces a scene with a known answer and reports whether the hardware path returned it.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every ray landed where it should have; <see langword="false"/> when a
    /// ray was wrong or the adapter cannot trace at all.
    /// </returns>
    /// <remarks>
    /// Worth running whenever the ray-tracing foundation is in doubt. Its parts — device addresses, build
    /// flags, descriptor strides, the barrier between a build and a traversal — fail in ways that surface
    /// downstream as indirect lighting that is subtly wrong rather than as an error, and this turns that
    /// into a plain pass or fail.
    /// </remarks>
    public bool RunRayTracingSelfTest()
    {
        ThrowIfDisposed();

        if (_rayTracing is null || _bindless is null)
        {
            _logger.Warning("Vulkan", "The ray-query self-test was asked for on an adapter that cannot trace.");
            return false;
        }

        RayTracing.VulkanRayQuerySelfTest test = new(
            _instance.Vk,
            _device,
            _allocator,
            _commands,
            _submission,
            _shaderCompiler,
            _bindless,
            _rayTracing);
        return test.Run(_logger);
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

        while (_pendingDeletions.Count > 0)
        {
            _pendingDeletions.Dequeue().Resource.Dispose();
        }

        foreach (Meshes.VulkanMesh mesh in _meshes.Values)
        {
            mesh.Dispose();
        }

        _meshes.Clear();

        foreach (VulkanTexture texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        // Before the compute registry, which owns the atlas images the render pass names.
        _placeholderScene?.Dispose();
        DisposeSceneTextures();
        DisposeCardAtlas();
        DisposeComputeResources();
        _captureStaging?.Dispose();
        _submission.Dispose();
        _worldScene?.Dispose();
        _rayTracing?.Dispose();
        _bindless?.Dispose();
        _occlusion.Dispose();
        _deferred.Dispose();
        _meshTextureDescriptors.Dispose();
        _mesh3D.Dispose();
        _textureDescriptors.Dispose();
        _sprite2DGeometry.Dispose();
        _sprite2D.Dispose();
        _shaderCompiler.Dispose();
        _stageProfiler?.Dispose();
        _frameLoop.Dispose();
        _presentTarget.Dispose();
        _swapchain.Dispose();
        _commands.Dispose();

        // Last of the resource owners: every buffer and image has by now returned its memory to the pool,
        // so this releases what the pool retained while the device is still alive to free it against.
        _allocator.Dispose();
        _device.Dispose();
        _instance.Dispose();
    }

    private static GraphicsCapabilities BuildCapabilities(
        PhysicalDeviceSelection selection,
        bool deferred,
        bool compute,
        bool rayQuery)
    {
        int maxTexture = (int)selection.Properties.Limits.MaxImageDimension2D;
        return new GraphicsCapabilities(
            TextureUnits: 16,
            MaxTextureSize: maxTexture,
            SupportsNonPowerOfTwoTextures: true,
            Renderer: $"Vulkan — {selection.Name}")
        {
            // Both the deferred capture path and compute are offered only when opted into, while they are
            // finished and validated on the live per-frame path; otherwise the engine takes the same
            // fallbacks a bare OpenGL 3.3 context would and runs the fast, stable direct renderer.
            SupportsSceneCapture = deferred,
            SupportsFloatRenderTargets = true,
            SupportsCompute = compute,

            // Only where there is something to trace against as well as the means to trace: a placeholder
            // scene stands in until the world's own is built, so the answer does not change mid-level.
            SupportsRayQuery = compute && rayQuery,
            MaxSampleCount = 1,
            UnimplementedSettings = UnimplementedOnVulkan,
        };
    }

    /// <summary>
    /// The settings this backend still takes and stores without acting on, named so the app can say so
    /// rather than let them look broken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three groups. What is left of the post-process chain — the resolve now runs the lens scatter, the
    /// shutter, the vignette, exposure and the curve, so what remains unread is the grade (contrast,
    /// saturation, the colour lookup) and the effects the reference does not use here. The lighting
    /// features whose device setters record their arguments and return: reflections, the floor sheen, the
    /// detail maps, the alpha hash, the realtime probe volume. And the volumetric fog, which keeps only
    /// the camera it was handed.
    /// </para>
    /// <para>
    /// Keep this list honest in both directions: an entry that has since been implemented is as
    /// misleading as a missing one, so delete the name in the same change that lands the pass. What keeps
    /// it honest is <c>tools/sweep-cvars.ps1</c>, which flips each of these and fails on any that changes
    /// the picture — or that does not change it and is not named here.
    /// </para>
    /// </remarks>
    private static readonly string[] UnimplementedOnVulkan =
    [
        // The grade, and the post-process effects the reference leaves switched off.
        "r_bloom_threshold", "r_grain", "r_chromatic", "r_sharpen",
        "r_contrast", "r_saturation", "r_dof", "r_dof_range", "r_dof_blur",
        "r_camera_taps", "r_lut",

        // Lighting features whose setters record and return.
        "r_ssr", "r_ssr_intensity", "r_ssr_max_roughness", "r_floor_gloss", "r_material_detail",
        "r_alpha_hash",

        // Volumetric fog: the setter keeps the camera and nothing else.
        "r_fog_density", "r_fog_height", "r_fog_anisotropy", "r_fog_quality", "r_fog_distance",
    ];

    private void EnsureFrame()
    {
        if (_frameOpen)
        {
            return;
        }

        while (!_frameLoop.BeginFrame(out _frameCommandBuffer, out _frameImageIndex))
        {
            RecreateSwapchain();
        }

        _sprite2DGeometry.Reset(_frameLoop.CurrentFrame);

        // The frame loop has waited on this slot's fence, so the compute instances it held two frames ago
        // are free to reuse; reset the rings for the slot before any of this frame's dispatches record.
        ResetComputeForFrame(_frameLoop.CurrentFrame);

        _frameCounter++;
        ProcessDeletions();

        _frameOpen = true;
        _presentPassActive = false;

        // Read back the timestamps this slot last carried and reset its block for the frame about to record.
        // Outside any render pass, which is exactly where a fresh frame's command buffer is here.
        _stageProfiler?.BeginFrame(_frameCommandBuffer, _frameLoop.CurrentFrame);

        // The occlusion queries reset and read back the same way, and for the same reason must be outside
        // a render pass — a query pool is reset here before any of this frame's boxes are drawn into it.
        _occlusion.BeginFrame(_frameCommandBuffer, _frameLoop.CurrentFrame);
    }

    /// <summary>Queues a resource to be freed once the frames that might still reference it have cycled.</summary>
    /// <param name="resource">The resource, already unregistered from its lookup.</param>
    private void QueueDeletion(IDisposable resource) => _pendingDeletions.Enqueue((_frameCounter, resource));

    /// <summary>Queues cleanup work — a resource dispose and any descriptor frees — for the same delay.</summary>
    /// <param name="cleanup">The work to run once the retirement frames have cycled.</param>
    private void QueueDeletion(Action cleanup) => _pendingDeletions.Enqueue((_frameCounter, new DeferredAction(cleanup)));

    /// <summary>An <see cref="IDisposable"/> that runs an action when disposed, for the deletion queue.</summary>
    private sealed class DeferredAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    /// <summary>Frees the queued resources whose retirement frame is far enough behind to be idle.</summary>
    private void ProcessDeletions()
    {
        while (_pendingDeletions.Count > 0
            && _pendingDeletions.Peek().Frame + VulkanFrameLoop.MaxFramesInFlight <= _frameCounter)
        {
            _pendingDeletions.Dequeue().Resource.Dispose();
        }
    }

    private unsafe void EnsurePresentPass()
    {
        EnsureFrame();
        if (_presentPassActive)
        {
            return;
        }

        Vk vk = _instance.Vk;
        ClearValue* clears = stackalloc ClearValue[2];
        clears[0] = new ClearValue
        {
            Color = new ClearColorValue(
                _clearColour.R / 255f,
                _clearColour.G / 255f,
                _clearColour.B / 255f,
                1.0f),
        };
        clears[1] = new ClearValue { DepthStencil = new ClearDepthStencilValue(0.0f, 0) };

        RenderPassBeginInfo beginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _presentTarget.RenderPass,
            Framebuffer = _presentTarget.Framebuffer(_frameImageIndex),
            RenderArea = new Rect2D(new Offset2D(0, 0), _presentTarget.Extent),
            ClearValueCount = 2,
            PClearValues = clears,
        };

        FlushComputeBeforePass();
        vk.CmdBeginRenderPass(_frameCommandBuffer, &beginInfo, SubpassContents.Inline);

        // Defensive: nothing draws 3D into the present pass today, but a pipeline belongs to the render
        // pass it was built for, so what the scene passes left bound must not be assumed here.
        InvalidateBoundState();
        _presentPassActive = true;
        ApplyViewportAndScissor(_frameCommandBuffer, _viewport);
    }

    private void EndPresentPassIfActive()
    {
        if (!_presentPassActive)
        {
            return;
        }

        _instance.Vk.CmdEndRenderPass(_frameCommandBuffer);
        _presentPassActive = false;
    }

    private unsafe void ClearActivePass(Bgra32 color)
    {
        Extent2D extent = _presentTarget.Extent;
        ClearAttachment attachment = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue
            {
                Color = new ClearColorValue(color.R / 255f, color.G / 255f, color.B / 255f, 1.0f),
            },
        };

        ClearRect rect = new()
        {
            BaseArrayLayer = 0,
            LayerCount = 1,
            Rect = new Rect2D(new Offset2D(0, 0), extent),
        };

        _instance.Vk.CmdClearAttachments(_frameCommandBuffer, 1, &attachment, 1, &rect);
    }

    private unsafe void ApplyViewportAndScissor(CommandBuffer commandBuffer, in Viewport viewport)
    {
        Extent2D extent = CurrentExtent;
        int width = viewport.Width > 0 ? viewport.Width : (int)extent.Width;
        int height = viewport.Height > 0 ? viewport.Height : (int)extent.Height;

        VkViewport vkViewport = new()
        {
            X = viewport.X,
            Y = viewport.Y,
            Width = width,
            Height = height,
            MinDepth = viewport.MinDepth,
            MaxDepth = viewport.MaxDepth == 0 && viewport.MinDepth == 0 ? 1.0f : viewport.MaxDepth,
        };

        Rect2D scissor = new(
            new Offset2D(Math.Max(0, viewport.X), Math.Max(0, viewport.Y)),
            new Extent2D((uint)Math.Max(0, width), (uint)Math.Max(0, height)));

        Vk vk = _instance.Vk;
        vk.CmdSetViewport(commandBuffer, 0, 1, &vkViewport);
        vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
    }

    private void RecreateSwapchain()
    {
        _device.WaitIdle();
        int width = Math.Max(1, CurrentMode.Width);
        int height = Math.Max(1, CurrentMode.Height);
        _swapchain.Recreate(width, height, _vsync);
        _presentTarget.Recreate(_swapchain);
        _frameLoop.OnSwapchainRecreated(_swapchain);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
