using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering.Pipeline.Deferred;
using OpenIGI.Rendering.Pipeline.Diagnostics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.RayTracing;
using OpenIGI.Rendering.Pipeline.Targets;
using OpenIGI.Rendering.Pipeline.Textures;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The renderer the game talks to, over the RHI and no graphics API.
/// </summary>
/// <remarks>
/// <para>
/// It draws both of the game's paths. The direct one is the original's: the world drawn forward into the
/// back buffer, the HUD and menus over it, and nothing kept afterwards -- what the vanilla preset asks for.
/// The deferred one is the remaster's, drawn whenever a scene capture is open; it reports
/// <see cref="GraphicsCapabilities.SupportsSceneCapture"/>, and a preset that does not want it declines to
/// open one.
/// </para>
/// <para>
/// <b>Two halves.</b> The direct path is a whole renderer rather than a slice of one: every pass it needs
/// is here, and it can be run and judged on its own. The deferred stack is the larger half, and is drawn
/// whenever a scene capture is open.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice : IGraphicsDevice
{
    /// <summary>How many frames may be in flight, which sizes every per-frame resource.</summary>
    public const int FramesInFlight = 2;

    private readonly IRhiDevice _device;

    // Markers taken while a frame was being recorded, armed once it has been sent. See MarkRecordedWork.
    private readonly List<RecordedWorkMarker> _unarmedMarkers = [];
    private readonly Sprite2DPass _sprites;
    private readonly Mesh3DPass _meshes;
    private readonly Mesh3DPipelineCache _pipelines;
    private readonly MaterialBindGroupCache _materials;
    private readonly IRhiBindGroupLayout _materialLayout;
    private readonly IRhiBindGroupLayout _cubeLayout;
    private readonly StageProfiler _profiler;
    private readonly OcclusionQueries _occlusion;
    private readonly RetiredResources _retired = new(FramesInFlight);
    private readonly IRhiTexture _cube;
    private readonly IRhiBindGroup _cubeGroup;
    private IRhiSampler _cubeSampler = null!;

    private IRhiCommandEncoder? _encoder;

    // The frame's commands already sealed, in the order they were recorded, submitted together with the
    // rest at the swap. See SealEncoder.
    private readonly List<IRhiCommandBuffer> _sealed = [];
    private IRhiRenderPassEncoder? _pass;
    private RhiFrame _frame;
    private bool _inFrame;

    // A present-mode change asked for while a frame was open; see VSyncEnabled.
    private bool _surfaceStale;
    private ulong _frameCounter;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GraphicsDevice"/> class.</summary>
    /// <param name="device">The backend to draw through.</param>
    /// <param name="mode">The mode the surface was configured for.</param>
    /// <param name="captureFrames">
    /// Whether every frame is copied back as it is submitted. A session that intends to capture — a
    /// screenshot run, a flight — says so here, because the copy has to be recorded INSIDE the frame and
    /// a caller asking afterwards is a frame too late.
    /// </param>
    /// <param name="logger">Where the renderer's status lines go, or null to write none.</param>
    public GraphicsDevice(
        IRhiDevice device,
        in DisplayMode mode,
        bool captureFrames = false,
        IOpenIgiLogger? logger = null)
        : this(device, mode, captureFrames, logger, tracedWorld: null)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="GraphicsDevice"/> class over a given traced world.</summary>
    /// <param name="device">The backend to draw through.</param>
    /// <param name="mode">The mode the surface was configured for.</param>
    /// <param name="captureFrames">Whether every frame is copied back as it is submitted.</param>
    /// <param name="logger">Where the renderer's status lines go, or null to write none.</param>
    /// <param name="tracedWorld">
    /// The traced world, which the caller keeps; or <see langword="null"/> for the device's own, built over
    /// its acceleration structures where it has them. A test hands in a stand-in that counts what is done to it.
    /// </param>
    internal GraphicsDevice(
        IRhiDevice device,
        in DisplayMode mode,
        bool captureFrames,
        IOpenIgiLogger? logger,
        ITracedWorld? tracedWorld)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _logger = logger;

        // The traced world, written once above the RHI for every backend that offers acceleration
        // structures. Built here, before anything is drawn: its placeholder scene is what a tracing shader
        // binds until the level has been seen.
        if (tracedWorld is not null)
        {
            _tracedWorld = tracedWorld;
        }
        else if (device.AccelerationStructures is { } structures)
        {
            _ownedTracedWorld = new RayTracing.TracedWorld(
                device, structures, ReflectionCVars.ReflectionTexture.Value, logger ?? NullLogger.Instance);
            _tracedWorld = _ownedTracedWorld;
        }

        CurrentMode = mode;
        PresentFormat = device.SurfaceFormat;
        _captureAlways = captureFrames;

        Capabilities = new GraphicsCapabilities(
            TextureUnits: 8,
            MaxTextureSize: device.Limits.MaxTextureDimension2D,
            SupportsNonPowerOfTwoTextures: true,
            $"RHI over {device.ApiName} — {device.AdapterName}")
        {
            // The G-buffer, the shading over it, the atmosphere and the resolve: a real mission's frame.
            // What it does not draw yet is named in UnimplementedSettings rather than drawn wrongly.
            SupportsSceneCapture = true,
            SupportsFloatRenderTargets = true,

            // The compute seam, where every module of the game's GI stack has this backend's form: saying
            // yes where the stack could not finish building would have the game reach for a shader this
            // device refuses to make. See GiRunsOn.
            SupportsCompute = GiRunsOn(device),

            // Where the backend traces, the GI stack builds its ray-query forms and the compute seam binds
            // them the traced world.
            SupportsRayQuery = GiRunsOn(device) && _tracedWorld is not null,
            SupportsBlockCompression = device.Features.TextureCompressionBc,
            MaxSampleCount = device.Limits.MaxSampleCount,
            MaxComputeGroupsX = device.Limits.MaxComputeWorkgroupsPerDimension,
            UnimplementedSettings = UnimplementedOn(TemporalResolve.RunsOn(device), GiRunsOn(device), _tracedWorld is not null, AutoExposure.RunsOn(device)),
        };

        _materialLayout = Mesh3DPipelineCache.CreateMaterialLayout(device);
        _cubeLayout = Mesh3DPipelineCache.CreateCubeLayout(device);
        _materials = new MaterialBindGroupCache(device, _materialLayout, _retired);

        _pipelines = new Mesh3DPipelineCache(
            device,
            _materialLayout,
            _cubeLayout,
            PresentFormat,
            DepthFormat,
            _retired);

        _textures = new TextureTable(device, _retired);
        _sprites = new Sprite2DPass(device, _textures, PresentFormat, FramesInFlight, DepthFormat, _retired);
        _meshes = new Mesh3DPass(_textures, _materials);
        _profiler = new StageProfiler(device, FramesInFlight);
        _occlusion = new OcclusionQueries(
            device,
            FramesInFlight,
            [.. GBufferTargets.Select(id => SceneTargetSet.FormatOf(id))],
            SceneTargetSet.FormatOf(SceneTargetId.Depth));

        (_cube, _cubeGroup) = CreateCube(device);
    }

    /// <summary>Gets the format the frame is presented in.</summary>
    /// <remarks>
    /// <para>
    /// The device's own, asked once when this is built: every pipeline drawing into a frame has to be
    /// built for the frame's format, and the window system chooses it. A desktop swapchain here hands
    /// out an unsigned-normalised image, so the interface composites and blends at the display encoding;
    /// an offscreen target is whatever it was attached as; a browser canvas may be sRGB.
    /// </para>
    /// <para>
    /// It was a constant, and a constant is what a pipeline built for sRGB and pointed at an
    /// unsigned-normalised swapchain is: refused by the validation layer, and a frame with its gamma
    /// applied twice by anything that let it through.
    /// </para>
    /// </remarks>
    public RhiTextureFormat PresentFormat { get; }

    /// <summary>The format an offscreen frame is taken in when nothing else says.</summary>
    /// <remarks>
    /// Only for a caller attaching its own offscreen target — a headless capture, a test — which has no
    /// window to ask. A device presenting to one reads <see cref="PresentFormat"/> instead.
    /// </remarks>
    public static RhiTextureFormat DefaultPresentFormat => RhiTextureFormat.Bgra8UnormSrgb;

    /// <summary>Gets the format of the depth buffer the direct path draws against.</summary>
    public static RhiTextureFormat DepthFormat => RhiTextureFormat.Depth32Float;

    /// <inheritdoc/>
    public GraphicsCapabilities Capabilities { get; }

    /// <inheritdoc/>
    public DisplayMode CurrentMode { get; private set; }

    /// <inheritdoc/>
    public bool VSyncEnabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            // Not under an open frame: reconfiguring rebuilds the swapchain, which frees the image the
            // frame is drawing into, and the frame's end still copies it for the capture -- toggling vsync
            // from the settings menu was an access violation in that copy. Applied once the frame is
            // presented instead.
            if (_inFrame)
            {
                _surfaceStale = true;
                return;
            }

            _device.ConfigureSurface(CurrentMode.Width, CurrentMode.Height, EffectiveVSync);
        }
    } = true;

    /// <summary>Gets whether presents wait for the display: the setting, unless frame generation cannot allow it.</summary>
    private bool EffectiveVSync => VSyncEnabled && !_frameGenerationForcesVsyncOff;

    /// <summary>Puts the effective vertical sync on the surface, now or once the open frame is presented.</summary>
    private void ApplyEffectiveVSync()
    {
        if (_inFrame)
        {
            _surfaceStale = true;
            return;
        }

        _device.ConfigureSurface(CurrentMode.Width, CurrentMode.Height, EffectiveVSync);
    }

    /// <inheritdoc/>
    public ScenePass CurrentPass { get; private set; } = ScenePass.None;

    /// <summary>Gets the backend this draws through, for a host that has to reach it.</summary>
    public IRhiDevice Backend => _device;

    /// <inheritdoc/>
    public void SetMode(in DisplayMode mode)
    {
        ThrowIfDisposed();

        CurrentMode = mode;
        _device.ConfigureSurface(mode.Width, mode.Height, EffectiveVSync);

        if (_depth is { } depth)
        {
            _retired.Retire(depth);
            _depth = null;
        }
    }

    /// <inheritdoc/>
    public void SetViewport(in Viewport viewport)
    {
        ThrowIfDisposed();

        // While a capture is open the viewport is the scene's, in render coordinates, and the window's
        // own — which the resolve and the interface draw with — is left alone.
        if (TakeSceneViewport(viewport))
        {
            return;
        }

        _viewport = viewport;

        if (_pass is { } pass)
        {
            ApplyViewport(pass);
        }
    }

    /// <inheritdoc/>
    public void Clear(Bgra32 color)
    {
        ThrowIfDisposed();

        // Recorded as the pass's own clear rather than as a command, because that is the only shape the
        // interface has: a pass states what happens to its attachments when it begins.
        _clearColour = new RhiClearColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        _clearPending = true;

        ClosePass();
    }

    /// <inheritdoc/>
    public void ClearDepth()
    {
        ThrowIfDisposed();

        // Inside a capture the depth is what every later pass reads, so the layer that asked for a clear
        // is given the nearest sliver of the range instead.
        if (Capturing)
        {
            SetForegroundLayer(true);
            return;
        }

        _clearDepthPending = true;
        ClosePass();
    }

    // Whether the open frame has drawn anything into the image it will present: a scene, or its own pass.
    private bool _frameDrawn;

    /// <inheritdoc/>
    public void SwapBuffers()
    {
        ThrowIfDisposed();

        ClosePass();

        if (!_inFrame)
        {
            // Nothing drew, so there is no frame to present. Opening one here to close it immediately
            // would be a blank flash rather than the held picture the caller expects.
            return;
        }

        // Presenting on record (NVIDIA's latency layer), a frame is sent the moment its pass has recorded it,
        // and work recorded after that -- the surface cache's relight, at the head of a pass that only ticks --
        // opens the next frame. Presented as it stood, with nothing drawn, it showed whatever the swapchain
        // image last held: a picture several frames old, menus and interface included, on every other frame
        // for as long as the relight ran. Held open instead, it becomes the start of the next frame that does
        // draw, exactly as the present-then-begin cadence would have made it.
        if (PresentsOnRecord && !_frameDrawn)
        {
            return;
        }

        _frameDrawn = false;

        _sprites.EndFrame();

        // The interface layer over the picture, where they were drawn apart; the capture and the present
        // both read the back buffer.
        TagFrameGeneration();
        ComposeLayers();

        // Before the profiler's own resolve and before the submit: the frame's image is the swapchain's,
        // and once it is presented the presentation engine owns it again.
        RecordCapture();

        _profiler.ResolveFrame(Encoder());
        _occlusion.ResolveFrame(Encoder());

        SealEncoder("frame");
        _device.Submit(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_sealed));
        _sealed.Clear();
        _skySubmittedSlot = _skyRecordedSlot;
        _skyRecordedSlot = -1;

        // What was just sent down starts mapping now, a frame before it is asked for.
        if (_skySubmittedSlot >= 0)
        {
            _sky?.RequestReadback(_skySubmittedSlot);
        }

        _autoExposure?.RequestReadback(FrameSlot);
        _profiler.RequestReadback(FrameSlot);
        _occlusion.RequestReadback();

        _device.EndFrame(_frame);
        _inFrame = false;

        // What frame generation did with the frame just presented, read on the presenting thread as its
        // plugin asks.
        ReadFrameGenerationState();

        if (_surfaceStale)
        {
            _surfaceStale = false;
            _device.ConfigureSurface(CurrentMode.Width, CurrentMode.Height, EffectiveVSync);
        }
        _frameCounter++;
        ArmRecordedWorkMarkers();

        _profiler.ReadCompletedFrame(FrameSlot);
        ReleaseStaged();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Whatever is still being recorded is closed and abandoned FIRST. An encoder names everything its
        // commands use, and freeing any of that while it is still open invalidates it, so that ending its
        // pass afterwards is an error in its own right -- which is what every run used to end with.
        CloseShadowPass();
        CloseScenePass();
        ClosePass();
        _encoder?.Dispose();
        _encoder = null;

        foreach (IRhiCommandBuffer unsent in _sealed)
        {
            unsent.Dispose();
        }

        _sealed.Clear();

        _device.WaitIdle();

        // Waited for, so nothing is in flight and everything held back may go.
        _retired.Dispose();
        DisposeCapture();

        DisposeCardAtlas();
        DisposeBackdrop();
        DisposeDeferred();
        DisposeTracers();
        DisposeCompute();
        _ownedTracedWorld?.Dispose();
        _profiler.Dispose();
        _occlusion.Dispose();
        _outlines?.Dispose();
        DisposeCubes();
        _cubeGroup.Dispose();
        _cube.Dispose();
        _cubeSampler.Dispose();
        _sprites.Dispose();
        DisposeWater();
        _pipelines.Dispose();
        _materials.Dispose();
        _cubeLayout.Dispose();
        _materialLayout.Dispose();
        _depth?.Dispose();
        _textures.Dispose();
        DisposeMeshes();
    }

    /// <summary>The environment cube every world draw binds, whether or not it samples one.</summary>
    /// <remarks>
    /// <para>
    /// One white texel on each of six faces. The forward shader declares the cube unconditionally and
    /// only reads it where a draw is reflective, so something has to be bound at that group for every
    /// draw — Vulkan tolerates a set the shader never reads, and WebGPU refuses to record a draw whose
    /// layout has an unbound group at all.
    /// </para>
    /// <para>
    /// White rather than black because it is the identity of the multiply it would take part in: a draw
    /// that reflects nothing and one that reflects this are the same picture.
    /// </para>
    /// </remarks>
    private (IRhiTexture Cube, IRhiBindGroup Group) CreateCube(IRhiDevice device)
    {
        const int Faces = 6;

        IRhiTexture cube = device.CreateTexture(new RhiTextureDescriptor(
            1,
            1,
            RhiTextureFormat.Bgra8UnormSrgb,
            RhiTextureUsage.Sampled | RhiTextureUsage.CopyDestination,
            DepthOrArrayLayers: Faces,
            Dimension: RhiTextureViewDimension.Cube,
            Label: "environment"));

        for (int face = 0; face < Faces; face++)
        {
            device.WriteTexture<byte>(
                new RhiTextureRegion(cube, 0, new RhiOrigin3D(0, 0, face)),
                [255, 255, 255, 255],
                4,
                new RhiExtent3D(1, 1));
        }

        _cubeSampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "environment"));

        IRhiTextureView view = cube.CreateView(new RhiTextureViewDescriptor(
            ArrayLayerCount: Faces,
            Dimension: RhiTextureViewDimension.Cube,
            Label: "environment"));

        IRhiBindGroup group = device.CreateBindGroup(new RhiBindGroupDescriptor(
            _cubeLayout,
            [RhiBindGroupEntry.Of(0, view), RhiBindGroupEntry.Of(1, _cubeSampler)],
            "environment"));

        return (cube, group);
    }

    /// <summary>Which set of per-frame resources the frame being built may write.</summary>
    private int FrameSlot => (int)(_frameCounter % FramesInFlight);

    /// <summary>The encoder this frame records into, opening the frame if it has not begun.</summary>
    private IRhiCommandEncoder Encoder()
    {
        if (_encoder is { } open)
        {
            return open;
        }

        if (!_inFrame)
        {
            BeginFrame();
        }

        _encoder = _device.CreateCommandEncoder("frame");
        return _encoder;
    }

    /// <summary>Finishes what the frame has recorded so far, to be submitted with the rest at the swap.</summary>
    /// <remarks>
    /// <para>
    /// Called after each stretch of the frame: every shadow cascade, the geometry, the shading, the
    /// temporal resolve and the lens chain. Finishing an encoder is where WebGPU validates and translates
    /// everything recorded into it -- five of mission 1's thirteen milliseconds a frame, one encoder for
    /// the whole frame, all of it at the swap and none of it overlapping anything. Sealed in pieces, each
    /// piece is finished while the next is recorded, on a backend that finishes off the recording thread;
    /// what is left to finish at the swap is only what came after the last seal. The compute stretches
    /// matter as much as the draws: a dispatch binds a dozen images, and those cost WebGPU far more to
    /// validate than a draw does.
    /// </para>
    /// <para>
    /// A pass still open is left alone and the seal skipped: an encoder cannot be finished around one.
    /// </para>
    /// <para>
    /// Nothing changes about what runs or in what order: the pieces are submitted together, in the order
    /// they were recorded, exactly where the single encoder was. On Vulkan a finish is next to free, and
    /// a few more command buffers a frame are nothing.
    /// </para>
    /// </remarks>
    /// <param name="label">What the piece holds, for captures and for any error finishing it.</param>
    private void SealEncoder(string label)
    {
        if (_encoder is not { } open || _pass is not null || _scenePass is not null || _shadowPass is not null)
        {
            return;
        }

        _sealed.Add(open.Finish(label));
        open.Dispose();
        _encoder = null;
    }

    private void BeginFrame()
    {
        if (!_device.BeginFrame(out _frame))
        {
            // The surface has been outgrown. Reconfiguring and trying once is what the frame loop would
            // do anyway, and a second refusal is a frame not drawn rather than an exception.
            _device.ConfigureSurface(CurrentMode.Width, CurrentMode.Height, EffectiveVSync);

            if (!_device.BeginFrame(out _frame))
            {
                throw new GraphicsDeviceException("The surface could not be reconfigured for this frame.");
            }
        }

        _inFrame = true;
        ReleaseWorldIfAsked();

        // A frame's depth buffer starts empty. The interface has a ClearDepth, and the game does not
        // call it: the original's backends cleared depth with the frame's own pass, unconditionally, so
        // nothing above ever had to ask. Loading it instead leaves the last frame's depth in place --
        // and with reverse-Z that rejects everything not nearer than what was already there, which is a
        // world that goes black a frame after it appears.
        _clearDepthPending = true;

        // The slot about to be written is the one the frame this many frames ago used, so that frame has
        // finished and whatever it named may now be freed.
        _retired.Frame = _frameCounter;
        _retired.ReleaseBefore(_frameCounter);

        _sprites.BeginFrame(FrameSlot, CurrentMode.Width, CurrentMode.Height);
        _profiler.BeginFrame(FrameSlot);
        _occlusion.BeginFrame(FrameSlot);
        ResetComputeFrame();
    }

    /// <summary>Releases the textures a blit staged for the frame that has just gone out.</summary>
    /// <remarks>
    /// A blit is one-off pixels with no handle of its own, so the texture it needs exists for one frame.
    /// Released after the submit rather than at the draw, because until then the frame still reads it.
    /// </remarks>
    private void ReleaseStaged()
    {
        foreach (TextureHandle staged in _staged)
        {
            _textures.Destroy(staged);
            _sprites.ForgetTexture(staged.Id);
        }

        _staged.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
