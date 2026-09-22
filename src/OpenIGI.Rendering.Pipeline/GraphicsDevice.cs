using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Diagnostics;
using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Meshes;
using OpenIGI.Rendering.Pipeline.Textures;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// The renderer the game talks to, over the RHI and no graphics API.
/// </summary>
/// <remarks>
/// <para>
/// This is the original's direct path: the world drawn forward into the back buffer, the HUD and menus
/// over it, and nothing kept afterwards. It reports <see cref="GraphicsCapabilities.SupportsSceneCapture"/>
/// false, which is what tells everything above to run that path and not the remaster's — so the
/// settings the deferred stack owns are recorded and named rather than silently ignored.
/// </para>
/// <para>
/// <b>Why vanilla first.</b> It is a whole renderer rather than a slice of one: every pass it needs has
/// moved, so it can be run and compared against what the Vulkan device draws today. The deferred stack
/// is the larger half and follows behind it, pass by pass, against that comparison.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice : IGraphicsDevice
{
    /// <summary>How many frames may be in flight, which sizes every per-frame resource.</summary>
    public const int FramesInFlight = 2;

    private readonly IRhiDevice _device;
    private readonly Sprite2DPass _sprites;
    private readonly Mesh3DPass _meshes;
    private readonly Mesh3DPipelineCache _pipelines;
    private readonly MaterialBindGroupCache _materials;
    private readonly IRhiBindGroupLayout _materialLayout;
    private readonly IRhiBindGroupLayout _cubeLayout;
    private readonly StageProfiler _profiler;
    private readonly RetiredResources _retired = new(FramesInFlight);
    private readonly IRhiTexture _cube;
    private readonly IRhiBindGroup _cubeGroup;
    private IRhiSampler _cubeSampler = null!;

    private IRhiCommandEncoder? _encoder;
    private IRhiRenderPassEncoder? _pass;
    private RhiFrame _frame;
    private bool _inFrame;
    private ulong _frameCounter;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GraphicsDevice"/> class.</summary>
    /// <param name="device">The backend to draw through.</param>
    /// <param name="mode">The mode the surface was configured for.</param>
    /// <param name="timestampPeriodNanoseconds">Nanoseconds per timestamp tick, from the device.</param>
    /// <param name="captureFrames">
    /// Whether every frame is copied back as it is submitted. A session that intends to capture — a
    /// screenshot run, a flight — says so here, because the copy has to be recorded INSIDE the frame and
    /// a caller asking afterwards is a frame too late.
    /// </param>
    /// <param name="deferredFrame">
    /// Whether this device offers the deferred frame. It is built and agrees with the Vulkan renderer on
    /// a frame made by hand; a real mission's geometry does not reach its G-buffer yet, so it is offered
    /// to somebody working on it rather than to a player, who gets the direct path, which draws.
    /// </param>
    public GraphicsDevice(
        IRhiDevice device,
        in DisplayMode mode,
        double timestampPeriodNanoseconds = 1.0,
        bool captureFrames = false,
        bool deferredFrame = false)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        CurrentMode = mode;
        PresentFormat = device.SurfaceFormat;
        _captureAlways = captureFrames;

        Capabilities = new GraphicsCapabilities(
            TextureUnits: 8,
            MaxTextureSize: device.Limits.MaxTextureDimension2D,
            SupportsNonPowerOfTwoTextures: true,
            $"{device.AdapterName} (RHI)")
        {
            // The G-buffer, the shading over it and the resolve are here and agree with the Vulkan
            // renderer on a frame built by hand — but a real mission's geometry does not reach the
            // G-buffer yet, and a device that claimed otherwise would hand the game a frame it cannot
            // finish. Opt in with OPENIGI_RHI_DEFERRED=1 to work on it; everything else gets the direct
            // path, which draws.
            SupportsSceneCapture = deferredFrame,
            SupportsFloatRenderTargets = true,

            // The compute seam is the deferred stack's, and none of it has moved onto this path. Saying
            // yes here would have the game reach for a shader handle this device refuses to hand out.
            SupportsCompute = false,
            SupportsRayQuery = false,
            MaxSampleCount = device.Limits.MaxSampleCount,
            UnimplementedSettings = UnimplementedSettingNames,
        };

        _materialLayout = Mesh3DPipelineCache.CreateMaterialLayout(device);
        _cubeLayout = Mesh3DPipelineCache.CreateCubeLayout(device);
        _materials = new MaterialBindGroupCache(device, _materialLayout, _retired);

        _pipelines = new Mesh3DPipelineCache(
            device,
            _materialLayout,
            _cubeLayout,
            PresentFormat,
            DepthFormat);

        _textures = new TextureTable(device, _retired);
        _sprites = new Sprite2DPass(device, _textures, PresentFormat, FramesInFlight, DepthFormat, _retired);
        _meshes = new Mesh3DPass(_textures, _materials);
        _profiler = new StageProfiler(device, FramesInFlight, timestampPeriodNanoseconds);

        (_cube, _cubeGroup) = CreateCube(device);
    }

    /// <summary>Gets the format the frame is presented in.</summary>
    /// <remarks>
    /// <para>
    /// The device's own, asked once when this is built: every pipeline drawing into a frame has to be
    /// built for the frame's format, and the window system chooses it. A desktop swapchain here hands
    /// out an unsigned-normalised image because the legacy path encodes its own output; an offscreen
    /// target is whatever it was attached as; a browser canvas may be sRGB.
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
            _device.ConfigureSurface(CurrentMode.Width, CurrentMode.Height, value);
        }
    } = true;

    /// <inheritdoc/>
    public ScenePass CurrentPass { get; private set; } = ScenePass.None;

    /// <summary>Gets the backend this draws through, for a host that has to reach it.</summary>
    public IRhiDevice Backend => _device;

    /// <inheritdoc/>
    public void SetMode(in DisplayMode mode)
    {
        ThrowIfDisposed();

        CurrentMode = mode;
        _device.ConfigureSurface(mode.Width, mode.Height, VSyncEnabled);

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
        _clearDepthPending = true;
        ClosePass();
    }

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

        _sprites.EndFrame();

        // Before the profiler's own resolve and before the submit: the frame's image is the swapchain's,
        // and once it is presented the presentation engine owns it again.
        RecordCapture();

        _profiler.ResolveFrame(Encoder());

        _device.Submit([Encoder().Finish("frame")]);

        _encoder?.Dispose();
        _encoder = null;

        _device.EndFrame(_frame);
        _inFrame = false;
        _frameCounter++;

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
        _device.WaitIdle();

        // Waited for, so nothing is in flight and everything held back may go.
        _retired.Dispose();
        DisposeCapture();

        _pass?.EndPass();
        _encoder?.Dispose();

        DisposeCardAtlas();
        DisposeDeferred();
        _profiler.Dispose();
        _cubeGroup.Dispose();
        _cube.Dispose();
        _cubeSampler.Dispose();
        _sprites.Dispose();
        _pipelines.Dispose();
        _materials.Dispose();
        _cubeLayout.Dispose();
        _materialLayout.Dispose();
        _depth?.Dispose();
        _textures.Dispose();
        DisposeMeshes();
        DisposeQueries();
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

    private void BeginFrame()
    {
        if (!_device.BeginFrame(out _frame))
        {
            // The surface has been outgrown. Reconfiguring and trying once is what the frame loop would
            // do anyway, and a second refusal is a frame not drawn rather than an exception.
            _device.ConfigureSurface(CurrentMode.Width, CurrentMode.Height, VSyncEnabled);

            if (!_device.BeginFrame(out _frame))
            {
                throw new GraphicsDeviceException("The surface could not be reconfigured for this frame.");
            }
        }

        _inFrame = true;

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
