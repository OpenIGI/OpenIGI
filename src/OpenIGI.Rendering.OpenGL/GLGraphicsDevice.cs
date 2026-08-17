using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// An <see cref="IGraphicsDevice"/> on OpenGL 3.3 core.
/// </summary>
/// <remarks>
/// <para>
/// The device owns the GL objects for the 2D pipeline and the texture table; it does not own the
/// context or the window. Presentation is delegated to the callback supplied at construction, which
/// keeps this project free of any dependency on <c>OpenIGI.Platform</c> — the composition root wires
/// the window's buffer swap in.
/// </para>
/// <para>
/// <b>Frame order.</b> <see cref="SwapBuffers"/> presents first and then prepares the next frame,
/// matching the original's <c>DisplaySwapBuffers</c>; see the original.
/// </para>
/// <para>
/// Textures are uploaded as <c>GL_RGBA</c> with the red and blue channels swizzled at sample time
/// rather than converted on the CPU. Texture swizzle is core in both GL 3.3 and GLES 3.0, so the same
/// upload path survives the GLES backend; converting instead would cost a full copy of every texture
/// for no visible difference.
/// </para>
/// </remarks>
public sealed partial class GLGraphicsDevice : IGraphicsDevice
{
    /// <summary>How many sampling units the backend tracks internally.</summary>
    /// <remarks>
    /// Not the same number as <see cref="GraphicsCapabilities.TextureUnits"/>, which is what the seam
    /// lets a caller address and stays at the original's eight. These are the backend's own: the probe
    /// field alone wants four, and GL 3.3 guarantees sixteen to a fragment shader.
    /// </remarks>
    private const int MaxTextureUnits = 16;

    /// <summary>How many concentric shells make up the selection glow's falloff.</summary>
    /// <remarks>
    /// Eight is where the banding stops being visible at the widths a selection mark uses. Fewer reads
    /// as a set of rings; more costs another draw of the model for a difference nobody can see.
    /// </remarks>
    private const int GlowShells = 8;

    /// <summary>The depth the glow's mask is pinned to, in normalised device coordinates.</summary>
    /// <remarks>
    /// Just <em>inside</em> the near plane, never on it. Clip space keeps what satisfies
    /// <c>-w &lt;= z &lt;= w</c>, so a triangle pinned to exactly <c>-w</c> sits on the boundary and the
    /// clipper's own rounding decides whether it survives — which showed up as a mask full of holes and
    /// a glow washing across the middle of the model wherever one appeared.
    /// </remarks>
    private const float MaskDepth = -1f + 1e-5f;

    /// <summary>The depth a see-through shell is pinned to.</summary>
    /// <remarks>
    /// A hair behind the mask, and that gap is the whole mechanism: the shells pass the depth test
    /// against the world, which is further back still, and fail it against the mask, which is the
    /// model's own silhouette. Sharing the mask's depth would compare equal and reject everything.
    /// </remarks>
    private const float GlowDepth = -1f + 1e-4f;

    /// <summary>How sharply the glow fades from the silhouette outwards.</summary>
    /// <remarks>
    /// Above 1 the light hugs the model and trails off, which is what a glow looks like; at 1 it is a
    /// linear ramp and reads as a flat band with a visible outer edge.
    /// </remarks>
    private const float GlowFalloff = 2.5f;

    private const string LogCategory = "OpenGL";

    private readonly GL _gl;
    private readonly Action _present;
    private readonly IIgiLogger _logger;
    private readonly GLProgramBinaryCache _programCache;
    private readonly HandleTable<GLTexture> _textures = new();
    private readonly HandleTable<GLMesh> _meshes = new();

    // The GL names currently bound to each texture unit. Zero is GL's own initial state, so this starts
    // out truthful; every bind in this class goes through BindTexture, so it stays that way.
    private readonly uint[] _textureNames = new uint[MaxTextureUnits];

    private GLShaderProgram _program = null!;
    private GLShaderProgram _meshProgram = null!;
    private MeshUniforms _meshUniforms;
    private int _spriteInverseScreen = -1;

    // Shadows of the bindings and of the mesh program's uniform values. A level is drawn as thousands of
    // sub-meshes, most of them sharing a transform, a texture and a blend with the one before -- so the
    // redundant call is the common case rather than the exception, and each one is a driver round trip.
    private uint _currentProgram;
    private uint _currentVertexArray;
    private int _activeTextureUnit = -1;
    private bool _meshUniformsPrimed;
    private Matrix4f _sentModelView;
    private Matrix4f _sentProjection;
    private (float Scale, float Offset, float Slope, float Enabled) _sentDetailFade;
    private float _sentAlphaTest;

    private uint _nextMeshId = 1;
    private Matrix4f _projection = Matrix4f.Identity;
    private (float Scale, float Offset, float Slope, float Enabled) _detailFade;
    private IrradianceVolumeSettings _volumeSettings;
    private readonly uint[] _probeTextures = [0, 0, 0, 0];
    private Vector3f _probeOrigin;
    private Vector3f _probeInverseCount;
    private float _probeSpacing;
    private bool _hasProbes;

    // The last probe layout logged as uploaded, so re-uploads of the same field (the interactive bake
    // easing light in frame by frame) do not each print a line.
    private (int X, int Y, int Z, float Spacing) _loggedProbeLayout;
    private int _lightingProbesEnabled = -1;
    private int _lightingProbeOrigin = -1;
    private int _lightingProbeInverseCount = -1;
    private int _lightingProbeScale = -1;
    private int _lightingProbeSky = -1;
    private int _lightingProbeBlend = -1;
    private int _lightingProbeViewToWorld = -1;
    private int _lightingProbeCamera = -1;
    private int _lightingShowProbes = -1;
    private int _lightingScale = -1;

    // The screen probe gather's indirect atlas, fed into the deferred lighting's own diffuse irradiance
    // rather than composited as a post-pass. Set per frame by the game layer while the G-buffer is live;
    // read by RunDeferredLighting and then cleared, so a frame that does not gather adds nothing.
    private int _lightingGiStrength = -1;
    private TextureHandle _screenIndirect;
    private float _screenIndirectStrength;

    // The level's real-time local lights -- the lamp fixtures, in view space. Set per frame by the game
    // layer before the pass closes and cleared after, the same lifetime as the screen indirect: a frame
    // that submits none lights from none. Packed for one glUniform4fv each: position.xyz + reach, then
    // colour.rgb + intensity, per light, up to the shader's array size.
    private int _lightingLocalLightCount = -1;
    private int _lightingLocalLightPositionRadius = -1;
    private int _lightingLocalLightColourIntensity = -1;
    private readonly float[] _localLightPositionRadius = new float[DeferredLocalLight.Maximum * 4];
    private readonly float[] _localLightColourIntensity = new float[DeferredLocalLight.Maximum * 4];
    private int _localLightCount;

    private DebugView _debugView;
    private ReflectionSettings _reflections;
    private GLShaderProgram? _reflectionProgram;
    private int _reflectionUnproject = -1;
    private int _reflectionMarch = -1;
    private int _reflectionTexelSize = -1;
    private int _reflectionJitter = -1;
    private int _reflectionProbesEnabled = -1;
    private int _reflectionProbeOrigin = -1;
    private int _reflectionProbeInverseCount = -1;
    private int _reflectionProbeSpacing = -1;
    private int _reflectionProbeSky = -1;
    private int _reflectionViewToWorld = -1;
    private int _reflectionCamera = -1;
    private int _reflectionShow = -1;
    private int _reflectionFrame;

    private AmbientOcclusionSettings _occlusion;
    private GLShaderProgram? _occlusionProgram;
    private GLShaderProgram? _occlusionReconstructProgram;
    private int _occlusionUnproject = -1;
    private int _occlusionSearch = -1;
    private int _occlusionBounce = -1;
    private int _occlusionTexelSize = -1;
    private int _occlusionReconstructTexelSize = -1;
    private int _occlusionFrame;
    private int _lightingOcclusionEnabled = -1;

    private Matrix4f? _previousPose;
    private Matrix4f _sentPreviousPose;
    private float _poseBlend;
    private float _previousPoseBlend;
    private float _sentPoseBlend;
    private float _sentPreviousPoseBlend;
    private DepthState _depthState = DepthState.Disabled;
    private CullMode _cullMode = CullMode.None;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _whiteTexture;
    private uint _blitTexture;
    private int _blitWidth;
    private int _blitHeight;
    private uint _nextTextureId = 1;
    private int _projectionWidth;
    private int _projectionHeight;
    private BlendMode _blend = BlendMode.Opaque;
    private GLShaderProgram? _gbufferProgram;
    private readonly Dictionary<GBufferLayout, GBufferPipeline> _gbufferPipelines = [];
    private GLShaderProgram? _cardProgram;
    private MeshUniforms _cardUniforms;
    private int _cardMaterial = -1;
    private int _cardDecodeAlbedo = -1;
    private GLCardAtlas? _cardAtlas;
    private bool _cardCaptureActive;
    private GLShaderProgram? _lightingProgram;
    private GLShaderProgram? _lastMeshProgram;
    private MeshUniforms _gbufferUniforms;
    private int _gbufferMaterial = -1;
    private int _meshMaterial = -1;
    private int _meshKeyColour = -1;
    private int _meshAmbient = -1;
    private int _gbufferDecodeAlbedo = -1;
    private int _lightingModelLocation = -1;
    private int _lightingSunDirection = -1;
    private int _lightingSunColour = -1;
    private int _lightingAmbient = -1;
    private SurfaceMaterial? _sentMaterial;
    private SurfaceMaterial _material = SurfaceMaterial.Default;
    private SceneLighting _lighting = SceneLighting.Baked;
    private float _materialDetail;
    private int _gbufferDetail = -1;
    private float _sentDetailStrength = float.NaN;

    /// <summary>GL_TEXTURE_MAX_ANISOTROPY. Core since 4.6 and an EXT with the same value long before.</summary>
    private const GLEnum TextureMaxAnisotropy = (GLEnum)0x84FE;

    /// <summary>GL_MAX_TEXTURE_MAX_ANISOTROPY.</summary>
    private const GLEnum MaxTextureMaxAnisotropy = (GLEnum)0x84FF;

    private int _anisotropy = 1;
    private int _anisotropyLimit = 1;
    private bool _generateMissingMipmaps;

    private GLShaderProgram? _fogProgram;
    private GLShaderProgram? _fogCompositeProgram;
    private int _fogCompositeTexelSize = -1;
    private VolumetricFogSettings _fog = VolumetricFogSettings.Off;
    private Vector3f _cameraPosition;
    private readonly LocalFogVolume[] _fogVolumes = new LocalFogVolume[LocalFogVolume.MaxPerLevel];
    private int _fogVolumeCount;
    private int _fogFrame;
    private int _fogSunDirection = -1;
    private int _fogSunColour = -1;
    private int _fogAmbient = -1;
    private int _fogCascadeCount = -1;
    private int _fogCascadeSplits = -1;
    private int _fogCascadeMatrix = -1;
    private int _fogUnproject = -1;
    private int _fogViewToWorld = -1;
    private int _fogMedium = -1;
    private int _fogMarch = -1;
    private int _fogAlbedo = -1;
    private int _fogJitter = -1;
    private int _fogMultiScatter = -1;
    private int _fogUniformFraction = -1;
    private int _fogVolumeCountLocation = -1;
    private int _fogVolumeCentre = -1;
    private int _fogVolumeExtent = -1;

    private GLShadowMap? _shadowMap;
    private GLShaderProgram? _shadowProgram;
    private MeshUniforms _shadowUniforms;
    private ShadowSettings _shadowSettings = ShadowSettings.Off;
    private readonly Matrix4f[] _cascadeMatrices = new Matrix4f[ShadowSettings.MaxCascades];
    private readonly Matrix4f[] _cascadeProjections = new Matrix4f[ShadowSettings.MaxCascades];
    private readonly float[] _cascadeSplits = new float[ShadowSettings.MaxCascades];
    private int _cascadeCount;
    private int _activeShadowCascade = -1;
    private TemporalAaSettings _temporal;
    private Matrix4f _temporalProjection = Matrix4f.Identity;
    private Vector3f _temporalCamera;
    private Matrix3f _temporalViewToWorldValue = Matrix3f.Identity;
    private Matrix4f _previousViewProjection = Matrix4f.Identity;

    // The same three, frozen at the head of the frame for the camera gather. The temporal resolve
    // advances the fields above from inside the forward pass, so by the time the gather runs they
    // describe the frame being drawn rather than the one before it.
    private Matrix4f _motionPreviousViewProjection = Matrix4f.Identity;
    private Vector3f _motionPreviousCamera;
    private bool _motionHasPreviousFrame;
    private Vector3f _previousCamera;
    private Matrix3f _previousViewToWorld = Matrix3f.Identity;
    private bool _hasPreviousFrame;

    /// <summary>How far the camera may travel in one frame before the history is treated as a cut.</summary>
    /// <remarks>
    /// A mission load, a death camera taking over, the ladder view pulling back: all of them move the
    /// eye tens of metres between one frame and the next, and reprojecting across that finds a
    /// different room rather than a different view of the same one. Ten metres is comfortably beyond
    /// what running or a fast free camera covers in a frame and comfortably below any of those cuts.
    /// Discarding when in doubt costs a few frames of convergence; keeping when in doubt costs a smear.
    /// </remarks>
    private const float CutTravelMetres = 10f;
    private bool _temporalResolved;

    private GLShaderProgram? _temporalProgram;
    private int _temporalUnproject = -1;
    private int _temporalViewToWorld = -1;
    private int _temporalPrevViewProjection = -1;
    private int _temporalCameraDelta = -1;
    private int _temporalRenderSize = -1;
    private int _temporalJitter = -1;
    private int _temporalUpsampling = -1;
    private int _temporalFeedback = -1;
    private int _temporalSharpening = -1;

    private DepthUnprojection _unprojection;
    private Matrix3f _viewToWorld = Matrix3f.Identity;
    private int _lightingCascadeCount = -1;
    private int _lightingCascadeSplits = -1;
    private int _lightingCascadeMatrix = -1;
    private int _lightingUnproject = -1;
    private int _lightingViewToWorld = -1;
    private int _lightingShadowBias = -1;
    private int _lightingShadowTexel = -1;
    private int _lightingShadowDetail = -1;

    private GLSceneTarget? _sceneTarget;
    private GLStageProfiler? _stageProfiler;
    private readonly HandleTable<VisibilityQuery> _visibilityQueries = new();
    private uint _nextVisibilityQuery = 1;
    private GLShaderProgram? _visibilityProgram;
    private int _visibilityModelView = -1;
    private int _visibilityProjection = -1;
    private int _visibilityMinimum = -1;
    private int _visibilityMaximum = -1;
    private uint _visibilityArray;
    private uint _visibilityVertexBuffer;
    private uint _visibilityIndexBuffer;
    private GLFullScreenPass? _fullScreen;
    private GLShaderProgram? _resolveProgram;
    private int _resolveExposure = -1;
    private int _resolveToneMap = -1;
    private int _resolveDecodeInput = -1;
    private int _resolveWhitePoint = -1;
    private int _resolveDebugView = -1;
    private int _resolveBloomIntensity = -1;
    private int _resolveGrade = -1;
    private int _resolveLens = -1;
    private int _resolveTexelSize = -1;
    private int _resolveLut = -1;
    private int _resolveGreenPhosphor = -1;
    private int _resolveTactical = -1;
    private int _resolveTacticalTime = -1;
    private int _resolveBinocular = -1;
    private int _resolveBinocularTime = -1;
    private uint _lutTexture;
    private int _lutSize;
    private int _gradeFrame;

    // Auto-exposure. Two compute passes reduce the scene to an average brightness and ease a running
    // exposure towards it, kept in a small buffer between frames; the resolve reads it back a frame late.
    private ComputeShaderHandle _autoExposureReduce;
    private ComputeShaderHandle _autoExposureAdapt;
    private StorageBufferHandle _autoExposureAccum;

    private GLShaderProgram? _bloomThresholdProgram;
    private GLShaderProgram? _bloomBlurProgram;
    private int _bloomThreshold = -1;
    private int _bloomDirection = -1;

    private GLShaderProgram? _spatialAaProgram;
    private int _spatialAaTexelSize = -1;
    private int _spatialAaEdge = -1;

    private GLShaderProgram? _outlineProgram;
    private int _outlineModelView;
    private int _outlineProjection;
    private int _outlineWidth;
    private int _outlineColour;
    private int _viewportWidth = 1;
    private int _viewportHeight = 1;
    private int _outlineViewport;
    private int _outlinePoseBlend;
    private int _outlinePin;
    private GLShaderProgram? _cameraProgram;
    private int _cameraUnproject = -1;
    private int _cameraFocus = -1;
    private int _cameraMotionBlur = -1;
    private int _cameraPrevViewProjection = -1;
    private int _cameraDelta = -1;
    private int _cameraViewToWorld = -1;
    private int _cameraTexelSize = -1;
    private int _cameraFrame;
    private bool _captureActive;

    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="GLGraphicsDevice"/> class.</summary>
    /// <param name="gl">The GL binding for the current context.</param>
    /// <param name="mode">The initial back-buffer mode.</param>
    /// <param name="present">Called to present the back buffer. Normally the window's buffer swap.</param>
    /// <param name="logger">Where to report backend diagnostics.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="GraphicsDeviceException">The 2D pipeline could not be created.</exception>
    public GLGraphicsDevice(GL gl, in DisplayMode mode, Action present, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(present);

        _gl = gl;
        _present = present;
        _logger = logger ?? NullLogger.Instance;
        CurrentMode = mode;

        Features = GLFeatureLevel.Detect(gl);
        Capabilities = ReadCapabilities();
        _programCache = new(gl, Features);
        CreatePipeline();
        SetMode(mode);
    }

    /// <inheritdoc/>
    public event EventHandler? DeviceRestored;

    /// <inheritdoc/>
    public GraphicsCapabilities Capabilities { get; }

    /// <summary>Gets what the context that was actually created can do.</summary>
    internal GLFeatureLevel Features { get; }

    /// <inheritdoc/>
    public DisplayMode CurrentMode { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// The swap interval belongs to the platform's context, not to GL itself, so this only records the
    /// request. The host reads it back and applies it to the window.
    /// </remarks>
    public bool VSyncEnabled { get; set; } = true;

    /// <inheritdoc/>
    /// <remarks>
    /// GL has no mode enumeration of its own — the platform layer owns the display list. The current
    /// mode is reported so callers always see at least one usable entry.
    /// </remarks>
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
        SetViewport(new Viewport(0, 0, mode.Width, mode.Height));
        Set2DProjection(mode.Width, mode.Height);
    }

    /// <inheritdoc/>
    /// <remarks>GL owns no mode-specific resources here, so there is nothing to release.</remarks>
    public void UnsetMode()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A GL context is not lost in the way a Direct3D 7 device is; the state below is re-applied anyway
    /// so a host that recreates its context can call this and get a device in a known state.
    /// </remarks>
    public void Restore()
    {
        ThrowIfDisposed();

        // Dithering is on by default in GL and is a relic: it perturbs the low bit of an 8-bit target
        // to hide banding. Turned off for predictable output, not as a fix -- it was measured against
        // the scattered one-bit differences between the direct and offscreen paths and changed none of
        // them, so those come from somewhere else (see docs/remaster/reference/README.md).
        _gl.Disable(EnableCap.Dither);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _depthState = DepthState.Disabled;
        _cullMode = CullMode.None;
        _blend = BlendMode.Opaque;
        ApplyBlend(BlendMode.AlphaBlend);
        SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));

        // The shadows below describe a context this call cannot vouch for, so they are dropped rather
        // than trusted: the next draw re-binds and re-uploads everything it needs.
        _currentProgram = 0;
        _currentVertexArray = 0;
        _activeTextureUnit = -1;
        _meshUniformsPrimed = false;
        Array.Clear(_textureNames);

        DeviceRestored?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Clear(Bgra32 color)
    {
        ThrowIfDisposed();
        using GLStageProfiler.Scope timing = Measure(RenderStage.Setup);

        // The original always clears colour and depth together, and depth always to 1.0.
        // glClear honours the real GL depth mask. Do not trust only the cached depth state here: a
        // backend-owned full-screen pass can have touched the mask without going through SetDepthState.
        // Force writes for the clear, then restore TestOnly's masked state. Disabled deliberately keeps
        // the mask on because disabling the depth test prevents draws from writing depth by itself.
        bool maskedOff = _depthState == DepthState.TestOnly;
        _gl.DepthMask(true);

        _gl.ClearColor(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, 1.0f);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (maskedOff)
        {
            _gl.DepthMask(false);
        }
    }

    /// <inheritdoc/>
    public void ClearDepth()
    {
        ThrowIfDisposed();

        // The forward rig must start with empty depth so its own pieces can occlude one another, but
        // clearing the colour target's shared world-depth texture destroys information still consumed
        // by post-processing. This distinction was invisible with temporal upsampling because its
        // forward target already has separate depth; Spatial AA renders the rig straight into the scene
        // colour target and exposed the regression as sky-coloured holes. Swap in a private attachment
        // before clearing it, then EndPass restores world depth for the post chain.
        if (_captureActive && CurrentPass == ScenePass.Forward)
        {
            _sceneTarget!.BindForForegroundDepth();
        }

        // This clear is especially sensitive to stale backend state: the foreground renderbuffer is
        // private to the hands/weapon and persists between frames. If a full-screen pass left the real
        // GL mask off while the cache said Disabled, the clear used to do nothing and old-frame depth
        // progressively cut holes out of the moving rig. Always enable writes before clearing it.
        bool maskedOff = _depthState == DepthState.TestOnly;
        _gl.DepthMask(true);

        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.DepthBufferBit);

        if (maskedOff)
        {
            _gl.DepthMask(false);
        }
    }

    /// <inheritdoc/>
    public void SwapBuffers()
    {
        ThrowIfDisposed();

        _present();

        // Whatever the driver has finished, gathered here because this is the one point that happens
        // exactly once a frame -- which is what tells one frame's stages from the next's. It never
        // waits: a query with no result yet is simply left for the next frame to ask about.
        _stageProfiler?.Collect();

        // The original unbinds both texture stages immediately after presenting, so the new frame starts
        // from a known state rather than inheriting the last one's. It really unbinds now: the old code
        // only cleared a table nothing read, which left the claim in this comment untrue.
        for (int unit = 0; unit < _textureNames.Length; unit++)
        {
            BindTexture(unit, 0);
        }
    }

    /// <inheritdoc/>
    public void SetViewport(in Viewport viewport)
    {
        ThrowIfDisposed();
        _gl.Viewport(viewport.X, viewport.Y, (uint)Math.Max(0, viewport.Width), (uint)Math.Max(0, viewport.Height));
        _gl.DepthRange(viewport.MinDepth, viewport.MaxDepth);

        // Remembered for the outline, which measures its glow in pixels and so has to know how many
        // there are across the target it is drawing into.
        _viewportWidth = Math.Max(1, viewport.Width);
        _viewportHeight = Math.Max(1, viewport.Height);
    }

    /// <inheritdoc/>
    public void BlitPixels(int x, int y, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length < width * height)
        {
            throw new ArgumentException(
                $"A {width}x{height} blit needs {width * height} pixels, got {pixels.Length}.",
                nameof(pixels));
        }

        EnsureBlitTexture(width, height, pixels);

        // Drawn as a quad rather than through glDrawPixels, which 3.3 core removed. The visible result
        // is the same: a 1:1 copy into the render target at (x, y).
        SpriteBatch batch = new(this, capacityInQuads: 1);
        TextureHandle handle = new(uint.MaxValue);
        _textures[handle.Id] = new GLTexture(_blitTexture, width, height, 1);
        try
        {
            batch.Begin();
            batch.Draw(
                handle,
                new RectF(x, y, width, height),
                RectF.Unit,
                Bgra32.White,
                BlendMode.Opaque);
            batch.End();
        }
        finally
        {
            _textures.Remove(handle.Id);
        }
    }

    /// <inheritdoc/>
    public void CaptureFrame(int width, int height, Span<Bgra32> destination)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (destination.Length < width * height)
        {
            throw new ArgumentException(
                $"A {width}x{height} capture needs {width * height} pixels, got {destination.Length}.",
                nameof(destination));
        }

        _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        _gl.ReadBuffer(ReadBufferMode.Back);
        _gl.ReadPixels(
            0,
            0,
            (uint)width,
            (uint)height,
            PixelFormat.Bgra,
            PixelType.UnsignedByte,
            destination);

        // GL reads bottom-up; callers want the top row first, so flip in place.
        Span<Bgra32> row = new Bgra32[width];
        for (int y = 0; y < height / 2; y++)
        {
            Span<Bgra32> top = destination.Slice(y * width, width);
            Span<Bgra32> bottom = destination.Slice((height - 1 - y) * width, width);
            top.CopyTo(row);
            bottom.CopyTo(top);
            row.CopyTo(bottom);
        }
    }

    /// <inheritdoc/>
    public void CopyFrameToTexture(TextureHandle texture, int width, int height)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0 || !_textures.TryGetValue(texture.Id, out GLTexture entry))
        {
            return;
        }

        // Straight from the back buffer into the texture's level 0 — no read back, no upload. The copy
        // keeps GL's bottom-up row order, so whoever draws it flips the source rectangle rather than the
        // pixels; that costs nothing and this is a per-swap operation, not a per-frame one.
        _gl.ReadBuffer(ReadBufferMode.Back);
        BindTextureForUpload(entry.Name);
        _gl.CopyTexSubImage2D(
            TextureTarget.Texture2D,
            0,
            0,
            0,
            0,
            0,
            (uint)Math.Min(width, entry.Width),
            (uint)Math.Min(height, entry.Height));
    }

    /// <inheritdoc/>
    public TextureHandle CreateTexture(in TextureDescription description, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();

        if (description.Width <= 0 || description.Height <= 0)
        {
            throw new ArgumentException($"Texture size {description.Width}x{description.Height} is not positive.", nameof(description));
        }

        if (description.MipLevels < 1)
        {
            throw new ArgumentException($"A texture needs at least one mip level, got {description.MipLevels}.", nameof(description));
        }

        int required = 0;
        for (int level = 0; level < description.MipLevels; level++)
        {
            (int w, int h) = description.GetMipSize(level);
            if (w <= 0 || h <= 0)
            {
                throw new ArgumentException(
                    $"Mip level {level} of a {description.Width}x{description.Height} texture is empty; the chain is too long.",
                    nameof(description));
            }

            required += w * h;
        }

        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"A {description.Width}x{description.Height} texture with {description.MipLevels} mip levels needs {required} pixels, got {pixels.Length}.",
                nameof(pixels));
        }

        uint name = _gl.GenTexture();
        BindTextureForUpload(name);

        int offset = 0;
        for (int level = 0; level < description.MipLevels; level++)
        {
            (int w, int h) = description.GetMipSize(level);
            UploadLevel(level, w, h, pixels.Slice(offset, w * h));
            offset += w * h;
        }

        TextureDescription sampled = description;

        // A texture that arrived without a mip chain gets one built. The terrain's ground tiles are
        // the case that matters: the atlas's layer descriptors carry no mip count, so every tile is a
        // single 128x128 level, and minifying that with no mips aliases into a crawling sparkle
        // wherever the ground recedes -- which is most of the screen in this game.
        //
        // Only for power-of-two sizes: glGenerateMipmap accepts others, but the halving is then not
        // exact and the chain drifts off the source.
        if (_generateMissingMipmaps
            && description.MipLevels == 1
            && IsPowerOfTwo(description.Width)
            && IsPowerOfTwo(description.Height))
        {
            _gl.GenerateMipmap(TextureTarget.Texture2D);

            int levels = 1;
            for (int size = Math.Max(description.Width, description.Height); size > 1; size >>= 1)
            {
                levels++;
            }

            sampled = description with { MipLevels = levels };
        }

        ApplySampling(sampled);

        uint id = _nextTextureId++;
        _textures[id] = new GLTexture(name, description.Width, description.Height, sampled.MipLevels);
        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public void UpdateTexture(TextureHandle texture, int mipLevel, ReadOnlySpan<Bgra32> pixels)
    {
        ThrowIfDisposed();

        GLTexture entry = Resolve(texture, nameof(texture));
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipLevel, entry.MipLevels);

        int w = entry.Width >> mipLevel;
        int h = entry.Height >> mipLevel;
        if (pixels.Length < w * h)
        {
            throw new ArgumentException(
                $"Mip level {mipLevel} is {w}x{h} and needs {w * h} pixels, got {pixels.Length}.",
                nameof(pixels));
        }

        BindTextureForUpload(entry.Name);
        UploadLevel(mipLevel, w, h, pixels[..(w * h)]);
    }

    /// <inheritdoc/>
    public void DestroyTexture(TextureHandle texture)
    {
        if (_disposed || !texture.IsValid)
        {
            return;
        }

        if (_textures.Remove(texture.Id, out GLTexture entry))
        {
            _gl.DeleteTexture(entry.Name);

            // Deleting a bound texture unbinds it, so the shadow has to follow or the next draw that
            // wanted white on that unit would skip the bind and sample a deleted name.
            for (int unit = 0; unit < _textureNames.Length; unit++)
            {
                if (_textureNames[unit] == entry.Name)
                {
                    _textureNames[unit] = 0;
                }
            }
        }
    }

    /// <inheritdoc/>
    public void SetTexture(int unit, TextureHandle texture)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(unit);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(unit, Capabilities.TextureUnits);

        BindTexture(unit, NameOf(texture, nameof(texture)));
    }

    /// <inheritdoc/>
    public unsafe void DrawTriangles2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException($"An index buffer must be a multiple of three, got {indices.Length}.", nameof(indices));
        }

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            return;
        }

        if (vertices.Length > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertices.Length}.",
                nameof(vertices));
        }

        UseProgram(_program);

        // The 2D pass owes nothing to whichever 3D renderer ran last: a sprite quad is authored in
        // screen space with one fixed winding, and inheriting a live cull mode from the scene made
        // the whole interface vanish whenever the last 3D draw happened to cull that winding.
        SetCullMode(CullMode.None);

        // uTexture is a constant and is set once at link time; the screen size changes only on a resize,
        // but it is one uniform on a path that draws a handful of batches per frame rather than
        // thousands, so it is not worth shadowing.
        _program.SetVector2(_spriteInverseScreen, 2.0f / _projectionWidth, 2.0f / _projectionHeight);

        BindTexture(0, NameOf(texture, nameof(texture)));

        ApplyBlend(blend);

        BindVertexArray(_vertexArray);
        UploadStream(BufferTargetARB.ArrayBuffer, _vertexBuffer, vertices, Vertex2D.Size);
        UploadStream(BufferTargetARB.ElementArrayBuffer, _indexBuffer, indices, sizeof(ushort));

        _gl.DrawElements(PrimitiveType.Triangles, (uint)indices.Length, DrawElementsType.UnsignedShort, (void*)0);
    }

    /// <inheritdoc/>
    public void Set2DProjection(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _projectionWidth = width;
        _projectionHeight = height;
    }

    /// <inheritdoc/>
    public unsafe MeshHandle CreateMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException($"An index buffer must be a multiple of three, got {indices.Length}.", nameof(indices));
        }

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            throw new ArgumentException("A mesh needs at least one triangle.", nameof(indices));
        }

        if (vertices.Length > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertices.Length}.",
                nameof(vertices));
        }

        int highest = HighestIndex(indices);
        if (highest >= vertices.Length)
        {
            throw new ArgumentException($"Index {highest} is past the {vertices.Length} vertices supplied.", nameof(indices));
        }

        uint vertexBuffer = _gl.GenBuffer();
        uint indexBuffer = _gl.GenBuffer();

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * Vertex3D.Size), vertices, BufferUsageARB.StaticDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        // The previous-pose stream is the mesh's own vertices: a static mesh's previous pose IS its
        // current one, so aliasing the streams makes its pose delta exactly zero at no memory cost.
        uint array = BuildMeshVertexArray(vertexBuffer, vertexBuffer, indexBuffer, indices);

        MeshHandle handle = new(_nextMeshId++);
        _meshes[handle.Id] = new GLMesh(array, vertexBuffer, indexBuffer, indices.Length);
        return handle;
    }

    /// <inheritdoc/>
    public unsafe MeshHandle CreateMeshStorage(int vertexCapacity, int indexCapacity)
    {
        ThrowIfDisposed();

        if (vertexCapacity <= 0 || indexCapacity <= 0)
        {
            throw new ArgumentException(
                $"A storage mesh needs positive capacities, got {vertexCapacity} vertices and {indexCapacity} indices.",
                nameof(vertexCapacity));
        }

        if (vertexCapacity > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertexCapacity}.",
                nameof(vertexCapacity));
        }

        uint vertexBuffer = _gl.GenBuffer();
        uint indexBuffer = _gl.GenBuffer();

        // Buffers are typeless: allocating the index storage through the array-buffer target avoids
        // touching any vertex array's element-buffer binding. DynamicDraw, because ranges keep landing
        // in these buffers for as long as their page keeps growing.
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(vertexCapacity * Vertex3D.Size),
            null,
            BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, indexBuffer);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(indexCapacity * sizeof(ushort)),
            null,
            BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        uint array = BuildMeshVertexArray(vertexBuffer, vertexBuffer, indexBuffer, uploadIndices: default);

        MeshHandle handle = new(_nextMeshId++);
        _meshes[handle.Id] = new GLMesh(
            array,
            vertexBuffer,
            indexBuffer,
            IndexCount: 0,
            VertexCapacity: vertexCapacity,
            IndexCapacity: indexCapacity);
        return handle;
    }

    /// <inheritdoc/>
    public unsafe void UploadMeshRange(
        MeshHandle mesh,
        int vertexOffset,
        ReadOnlySpan<Vertex3D> vertices,
        int indexOffset,
        ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();

        if (!_meshes.TryGetValue(mesh.Id, out GLMesh entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (!entry.IsStorage)
        {
            throw new ArgumentException($"{mesh} was not created with {nameof(CreateMeshStorage)}.", nameof(mesh));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(vertexOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(indexOffset);

        if (vertexOffset + vertices.Length > entry.VertexCapacity)
        {
            throw new ArgumentException(
                $"Vertices {vertexOffset}..{vertexOffset + vertices.Length} lie outside the mesh's capacity of {entry.VertexCapacity}.",
                nameof(vertices));
        }

        if (indexOffset + indices.Length > entry.IndexCapacity)
        {
            throw new ArgumentException(
                $"Indices {indexOffset}..{indexOffset + indices.Length} lie outside the mesh's capacity of {entry.IndexCapacity}.",
                nameof(indices));
        }

        if (!indices.IsEmpty)
        {
            int highest = HighestIndex(indices);
            if (highest >= entry.VertexCapacity)
            {
                throw new ArgumentException(
                    $"Index {highest} is past the mesh's capacity of {entry.VertexCapacity} vertices.",
                    nameof(indices));
            }
        }

        if (!vertices.IsEmpty)
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, entry.VertexBuffer);
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                (nint)(vertexOffset * Vertex3D.Size),
                (nuint)(vertices.Length * Vertex3D.Size),
                vertices);
        }

        if (!indices.IsEmpty)
        {
            // Through the array-buffer target for the same reason CreateMeshStorage allocates there.
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, entry.IndexBuffer);
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                (nint)(indexOffset * sizeof(ushort)),
                (nuint)(indices.Length * sizeof(ushort)),
                indices);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        int drawable = Math.Max(entry.IndexCount, indexOffset + indices.Length);
        if (drawable != entry.IndexCount)
        {
            _meshes[mesh.Id] = entry with { IndexCount = drawable };
        }
    }

    /// <inheritdoc/>
    public unsafe MeshHandle CreateAnimatedMesh(ReadOnlySpan<Vertex3D> vertices, ReadOnlySpan<ushort> indices)
    {
        ThrowIfDisposed();

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException($"An index buffer must be a multiple of three, got {indices.Length}.", nameof(indices));
        }

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            throw new ArgumentException("A mesh needs at least one triangle.", nameof(indices));
        }

        if (vertices.Length > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertices.Length}.",
                nameof(vertices));
        }

        int highest = HighestIndex(indices);
        if (highest >= vertices.Length)
        {
            throw new ArgumentException($"Index {highest} is past the {vertices.Length} vertices supplied.", nameof(indices));
        }

        // Two whole vertex buffers and a vertex array over each pairing. An update writes the buffer
        // holding the STALE pose and flips which array draws, so the pose being replaced becomes the
        // previous-pose stream without a byte being copied.
        uint bufferA = _gl.GenBuffer();
        uint bufferB = _gl.GenBuffer();
        uint indexBuffer = _gl.GenBuffer();

        foreach (uint buffer in (ReadOnlySpan<uint>)[bufferA, bufferB])
        {
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
            _gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * Vertex3D.Size),
                vertices,
                BufferUsageARB.DynamicDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        uint arrayA = BuildMeshVertexArray(bufferA, bufferB, indexBuffer, indices);
        uint arrayB = BuildMeshVertexArray(bufferB, bufferA, indexBuffer, uploadIndices: default);

        MeshHandle handle = new(_nextMeshId++);
        _meshes[handle.Id] = new GLMesh(
            arrayA, bufferA, indexBuffer, indices.Length, arrayB, bufferB, vertices.Length);
        return handle;
    }

    /// <inheritdoc/>
    public void UpdateAnimatedMesh(MeshHandle mesh, ReadOnlySpan<Vertex3D> vertices)
    {
        ThrowIfDisposed();

        if (!_meshes.TryGetValue(mesh.Id, out GLMesh entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (!entry.IsAnimated)
        {
            throw new ArgumentException($"{mesh} was not created with {nameof(CreateAnimatedMesh)}.", nameof(mesh));
        }

        if (vertices.Length != entry.VertexCount)
        {
            throw new ArgumentException(
                $"An animated mesh's vertex count is fixed: created with {entry.VertexCount}, got {vertices.Length}.",
                nameof(vertices));
        }

        // Into the buffer holding the OLDER of the two poses; the flip then makes what was current the
        // previous-pose stream. Orphan the old storage before uploading. A direct BufferSubData can
        // block until the GPU has finished the previous draw using this buffer; that stall becomes
        // especially visible when a ladder/death/interaction animation updates many bodies together.
        // BufferData with DynamicDraw lets the driver retire the old storage asynchronously.
        uint stale = entry.FrontIsFirst ? entry.SecondVertexBuffer : entry.VertexBuffer;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, stale);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(vertices.Length * Vertex3D.Size),
            vertices,
            BufferUsageARB.DynamicDraw);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        _meshes[mesh.Id] = entry with { FrontIsFirst = !entry.FrontIsFirst };
    }

    /// <summary>Builds a vertex array reading a mesh's layout from one buffer and its previous pose from another.</summary>
    /// <param name="currentBuffer">The buffer attributes 0–4 read.</param>
    /// <param name="previousBuffer">The buffer the previous-pose position and normal (5, 6) read.</param>
    /// <param name="indexBuffer">The element buffer recorded into the array.</param>
    /// <param name="uploadIndices">Index data to upload while bound, or empty when already uploaded.</param>
    /// <returns>The vertex array's name.</returns>
    private unsafe uint BuildMeshVertexArray(
        uint currentBuffer, uint previousBuffer, uint indexBuffer, ReadOnlySpan<ushort> uploadIndices)
    {
        uint array = _gl.GenVertexArray();
        BindVertexArray(array);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexBuffer);
        if (!uploadIndices.IsEmpty)
        {
            _gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(uploadIndices.Length * sizeof(ushort)),
                uploadIndices,
                BufferUsageARB.StaticDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, currentBuffer);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vertex3D.Size, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Vertex3D.Size, (void*)12);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Vertex3D.Size, (void*)24);
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, Vertex3D.Size, (void*)32);
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(
            4,
            4,
            VertexAttribPointerType.UnsignedByte,
            true,
            Vertex3D.Size,
            (void*)Vertex3D.ColourOffset);

        // The previous pose's position and normal, from whichever buffer holds it.
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, previousBuffer);
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, Vertex3D.Size, (void*)0);
        _gl.EnableVertexAttribArray(6);
        _gl.VertexAttribPointer(6, 3, VertexAttribPointerType.Float, false, Vertex3D.Size, (void*)12);

        // Unbound before the buffer bindings are cleared, or clearing the element array would record
        // "no indices" into the array object that was just configured.
        BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
        return array;
    }

    /// <inheritdoc/>
    public void DestroyMesh(MeshHandle mesh)
    {
        ThrowIfDisposed();

        if (mesh.IsValid && _meshes.Remove(mesh.Id, out GLMesh entry))
        {
            _gl.DeleteVertexArray(entry.VertexArray);
            _gl.DeleteBuffer(entry.VertexBuffer);
            _gl.DeleteBuffer(entry.IndexBuffer);

            if (entry.IsAnimated)
            {
                _gl.DeleteVertexArray(entry.SecondVertexArray);
                _gl.DeleteBuffer(entry.SecondVertexBuffer);
            }

            // Deleting the bound array reverts GL to zero; the shadow has to say so.
            if (_currentVertexArray == entry.VertexArray
                || (entry.IsAnimated && _currentVertexArray == entry.SecondVertexArray))
            {
                _currentVertexArray = 0;
            }
        }
    }

    /// <inheritdoc/>
    public void SetDepthState(DepthState state)
    {
        ThrowIfDisposed();

        if (_depthState == state)
        {
            return;
        }

        _depthState = state;
        switch (state)
        {
            case DepthState.Disabled:
                // The mask stays on: with the test disabled GL writes no depth anyway, and leaving it
                // off would also stop Clear from clearing the depth buffer.
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(true);
                break;
            case DepthState.TestAndWrite:
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Lequal);
                _gl.DepthMask(true);
                break;
            case DepthState.TestOnly:
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Lequal);
                _gl.DepthMask(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown depth state.");
        }
    }

    /// <inheritdoc/>
    public void SetCullMode(CullMode mode)
    {
        ThrowIfDisposed();

        if (_cullMode == mode)
        {
            return;
        }

        _cullMode = mode;
        if (mode == CullMode.None)
        {
            _gl.Disable(EnableCap.CullFace);
            return;
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.CullFace(mode == CullMode.Clockwise ? TriangleFace.Front : TriangleFace.Back);
    }

    /// <inheritdoc/>
    public void SetProjection(in Matrix4f projection)
    {
        ThrowIfDisposed();
        _projection = projection;
    }

    /// <inheritdoc/>
    public void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha)
    {
        ThrowIfDisposed();
        _detailFade = (scale, offset, slope, fadeAlpha ? 2f : 1f);
    }

    /// <inheritdoc/>
    public void SetReflections(in ReflectionSettings settings)
    {
        ThrowIfDisposed();
        _reflections = settings;
    }

    /// <inheritdoc/>
    public void SetStageTiming(bool enabled)
    {
        ThrowIfDisposed();

        if (_stageProfiler is { } profiler)
        {
            profiler.Enabled = enabled;
        }
    }

    /// <inheritdoc/>
    public void BeginStageTiming(RenderStage stage)
    {
        ThrowIfDisposed();
        _stageProfiler?.Begin(stage);
    }

    /// <inheritdoc/>
    public void EndStageTiming()
    {
        ThrowIfDisposed();
        _stageProfiler?.End();
    }

    /// <inheritdoc/>
    public IReadOnlyList<RenderStageTiming> StageTimings() =>
        _stageProfiler?.Timings() ?? [];

    /// <inheritdoc/>
    public GpuFrameTiming GpuTiming() => _stageProfiler?.TotalTiming() ?? default;

    /// <inheritdoc/>
    public OcclusionQueryHandle CreateOcclusionQuery()
    {
        ThrowIfDisposed();
        uint id = _nextVisibilityQuery++;
        _visibilityQueries[id] = new VisibilityQuery(_gl.GenQuery());
        return new OcclusionQueryHandle(id);
    }

    /// <inheritdoc/>
    public bool OcclusionVisible(OcclusionQueryHandle query)
    {
        ThrowIfDisposed();
        if (!query.IsValid || !_visibilityQueries.TryGetValue(query.Id, out VisibilityQuery state))
        {
            return true;
        }

        if (state.Pending)
        {
            _gl.GetQueryObject(state.Name, QueryObjectParameterName.ResultAvailable, out int ready);
            if (ready != 0)
            {
                _gl.GetQueryObject(state.Name, QueryObjectParameterName.Result, out uint passed);
                int emptyFrames = passed != 0 ? 0 : state.EmptyFrames + 1;
                state = state with
                {
                    Pending = false,
                    EmptyFrames = emptyFrames,

                    // A single empty result can be a camera-edge race. Requiring two retains the
                    // conservative failure mode: extra drawing, never a one-frame hole.
                    Visible = passed != 0 || emptyFrames < 2,
                };
                _visibilityQueries[query.Id] = state;
            }
        }

        return state.Visible;
    }

    /// <inheritdoc/>
    public unsafe void IssueOcclusionQuery(
        OcclusionQueryHandle query,
        in Matrix4f modelView,
        in OcclusionBounds bounds)
    {
        ThrowIfDisposed();
        if (_visibilityProgram is null
            || !query.IsValid
            || !_visibilityQueries.TryGetValue(query.Id, out VisibilityQuery state)
            || state.Pending)
        {
            return;
        }

        UseProgram(_visibilityProgram);
        _visibilityProgram.SetMatrix4(_visibilityModelView, modelView);
        _visibilityProgram.SetMatrix4(_visibilityProjection, _projection);
        _visibilityProgram.SetVector3(
            _visibilityMinimum, bounds.Minimum.X, bounds.Minimum.Y, bounds.Minimum.Z);
        _visibilityProgram.SetVector3(
            _visibilityMaximum, bounds.Maximum.X, bounds.Maximum.Y, bounds.Maximum.Z);

        _gl.ColorMask(false, false, false, false);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        BindVertexArray(_visibilityArray);
        _gl.BeginQuery(QueryTarget.AnySamplesPassed, state.Name);
        _gl.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedShort, null);
        _gl.EndQuery(QueryTarget.AnySamplesPassed);

        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(_depthState != DepthState.TestOnly);
        if (_cullMode != CullMode.None)
        {
            _gl.Enable(EnableCap.CullFace);
        }

        _visibilityQueries[query.Id] = state with { Pending = true };
    }

    /// <inheritdoc/>
    public void DestroyOcclusionQuery(OcclusionQueryHandle query)
    {
        ThrowIfDisposed();
        if (query.IsValid && _visibilityQueries.Remove(query.Id, out VisibilityQuery state))
        {
            _gl.DeleteQuery(state.Name);
        }
    }

    /// <summary>Times a stage until the returned scope is disposed.</summary>
    /// <param name="stage">The stage about to run.</param>
    /// <returns>The scope to keep for the stage's duration.</returns>
    /// <remarks>Costs nothing when timing is off, and nothing at all before the device is initialised.</remarks>
    private GLStageProfiler.Scope Measure(RenderStage stage) => new(_stageProfiler, stage);

    private GLStageProfiler.Scope Measure(RenderStage stage, bool enabled) =>
        new(enabled ? _stageProfiler : null, stage);

    /// <inheritdoc/>
    public void SetDebugView(DebugView view)
    {
        ThrowIfDisposed();
        _debugView = view;
    }

    /// <inheritdoc/>
    public void SetIrradianceVolumeSettings(in IrradianceVolumeSettings settings)
    {
        ThrowIfDisposed();
        _volumeSettings = settings;
    }

    /// <inheritdoc/>
    public unsafe void SetColourLookup(ColourLookupTable? table)
    {
        ThrowIfDisposed();

        if (_lutTexture != 0)
        {
            _gl.DeleteTexture(_lutTexture);
            _lutTexture = 0;
            _lutSize = 0;
        }

        if (table is null)
        {
            return;
        }

        _lutTexture = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture3D, _lutTexture);

        // RGB16F rather than RGB8. The table is sampled in DISPLAY space, where the eye's sensitivity
        // to a step is greatest in the darks -- and eight bits per channel across a grade that also
        // lifts the blacks is where banding in a night sky comes from. It is 32^3 entries either way,
        // which is 786 KB at half float.
        fixed (float* entries = table.Entries)
        {
            _gl.TexImage3D(
                TextureTarget.Texture3D,
                0,
                (int)InternalFormat.Rgb16f,
                (uint)table.Size,
                (uint)table.Size,
                (uint)table.Size,
                0,
                PixelFormat.Rgb,
                PixelType.Float,
                entries);
        }

        // Linear on all three axes IS the interpolation: a 32-entry cube resolves a smooth grade only
        // because the hardware blends between the eight samples around each colour. Clamped, so a
        // colour that overshoots the domain takes the nearest authored answer rather than wrapping to
        // the opposite corner of the cube, which would turn a clipped highlight into a hue inversion.
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);

        _textureNames[0] = 0;
        _lutSize = table.Size;
        _logger.Information("Grade", $"Colour table '{table.Name}' uploaded, {table.Size}³.");
    }

    /// <inheritdoc/>
    public void SetIrradianceVolume(IrradianceVolume? volume)
    {
        ThrowIfDisposed();
        ReleaseProbeTextures();

        if (volume is null || volume.ProbeCount == 0)
        {
            return;
        }

        int count = volume.ProbeCount;
        float[] red = new float[count * 4];
        float[] green = new float[count * 4];
        float[] blue = new float[count * 4];
        float[] distance = new float[count * 4];

        ReadOnlySpan<float> source = volume.Coefficients;
        ReadOnlySpan<float> moments = volume.Distances;

        for (int probe = 0; probe < count; probe++)
        {
            int from = probe * IrradianceVolume.CoefficientsPerProbe;
            int to = probe * 4;

            for (int i = 0; i < 4; i++)
            {
                red[to + i] = source[from + i];
                green[to + i] = source[from + 4 + i];
                blue[to + i] = source[from + 8 + i];
            }

            distance[to] = moments[probe * 2];
            distance[to + 1] = moments[(probe * 2) + 1];
        }

        _probeTextures[0] = CreateProbeTexture(volume, red);
        _probeTextures[1] = CreateProbeTexture(volume, green);
        _probeTextures[2] = CreateProbeTexture(volume, blue);
        _probeTextures[3] = CreateProbeTexture(volume, distance);

        _probeOrigin = volume.Origin;
        _probeSpacing = volume.Spacing;
        _probeInverseCount = new Vector3f(1f / volume.CountX, 1f / volume.CountY, 1f / volume.CountZ);
        _hasProbes = true;

        // Only when the field's shape changes, not on every upload. An interactive bake re-uploads this
        // volume every frame for a second or so as the light eases in -- same layout each time -- and
        // logging that per frame would bury the rest of the frame's diagnostics.
        var layout = (volume.CountX, volume.CountY, volume.CountZ, volume.Spacing);
        if (layout != _loggedProbeLayout)
        {
            _loggedProbeLayout = layout;
            _logger.Information(
                "GI",
                $"Probe field uploaded: {volume.CountX}x{volume.CountY}x{volume.CountZ} at {volume.Spacing:0.#} m.");
        }
    }

    /// <summary>Uploads one channel of the probe field as a filtered volume texture.</summary>
    private unsafe uint CreateProbeTexture(IrradianceVolume volume, float[] data)
    {
        uint name = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture3D, name);

        fixed (float* pixels = data)
        {
            _gl.TexImage3D(
                TextureTarget.Texture3D,
                0,
                (int)InternalFormat.Rgba16f,
                (uint)volume.CountX,
                (uint)volume.CountY,
                (uint)volume.CountZ,
                0,
                PixelFormat.Rgba,
                PixelType.Float,
                pixels);
        }

        // Linear, because the blend between eight neighbouring probes is exactly what a probe field
        // wants and the hardware does it for nothing. Clamped, because a surface at the edge of the
        // volume should take the nearest probe's answer rather than one from the opposite corner.
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);

        _textureNames[0] = 0;
        return name;
    }

    /// <summary>Drops the probe field, for a level change or shutdown.</summary>
    private void ReleaseProbeTextures()
    {
        for (int i = 0; i < _probeTextures.Length; i++)
        {
            if (_probeTextures[i] != 0)
            {
                _gl.DeleteTexture(_probeTextures[i]);
                _probeTextures[i] = 0;
            }
        }

        _hasProbes = false;
    }

    /// <inheritdoc/>
    public void SetAmbientOcclusion(in AmbientOcclusionSettings settings)
    {
        ThrowIfDisposed();
        _occlusion = settings;
    }

    /// <inheritdoc/>
    public void SetPreviousPose(in Matrix4f previousModelView)
    {
        ThrowIfDisposed();
        _previousPose = previousModelView;
    }

    /// <inheritdoc/>
    public void ClearPreviousPose()
    {
        ThrowIfDisposed();
        _previousPose = null;
    }

    /// <inheritdoc/>
    public void SetPoseInterpolation(float currentBlend, float previousBlend)
    {
        ThrowIfDisposed();
        _poseBlend = Math.Clamp(currentBlend, 0f, 1f);
        _previousPoseBlend = Math.Clamp(previousBlend, 0f, 1f);
    }

    /// <inheritdoc/>
    public void ClearPoseInterpolation()
    {
        ThrowIfDisposed();
        _poseBlend = 0f;
        _previousPoseBlend = 0f;
    }

    /// <inheritdoc/>
    public void ClearDetailFade()
    {
        ThrowIfDisposed();
        _detailFade = default;
    }

    /// <inheritdoc/>
    public ScenePass CurrentPass { get; private set; } = ScenePass.None;

    /// <inheritdoc/>
    public bool BeginSceneCapture(in SceneTargetDescription description)
    {
        ThrowIfDisposed();

        if (_captureActive)
        {
            throw new GraphicsDeviceException("A scene capture is already in progress.");
        }

        if (_sceneTarget is null || !_sceneTarget.Configure(description))
        {
            return false;
        }

        if (description.Deferred)
        {
            SelectGBufferPipeline(description.GBuffer);
        }

        _captureActive = true;
        _temporalResolved = false;

        // Where the camera was on the frame BEFORE this one, taken here because this is the only moment
        // it is still true. The temporal resolve ends by calling RememberFrame, and it runs from the
        // forward pass -- long before the camera gather that needs the previous frame -- so by the time
        // the gather asks, the "previous" state is the current one. It reprojected every pixel onto
        // itself and produced a velocity of exactly zero, which is why r_motion_blur has never done
        // anything at any value or any preset.
        _motionPreviousViewProjection = _previousViewProjection;
        _motionPreviousCamera = _previousCamera;
        _motionHasPreviousFrame = _hasPreviousFrame;

        _sceneTarget.BeginFrame();
        _sceneTarget.BindForGeometry();
        return true;
    }

    /// <inheritdoc/>
    public bool BeginCardCapture(int width, int height)
    {
        ThrowIfDisposed();

        if (_cardCaptureActive)
        {
            throw new GraphicsDeviceException("A card capture is already in progress.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new GraphicsDeviceException($"A card atlas cannot be {width}x{height}.");
        }

        _cardAtlas ??= new GLCardAtlas(_gl, _logger);

        // Any handle handed out for the previous atlas is about to name a deleted texture, because
        // Configure releases what it held. Forgetting them here means a caller that kept one gets a
        // clean "does not belong to this device" rather than a silent sample of freed memory.
        ForgetRegistration(_cardAtlas.AlbedoTexture);
        ForgetRegistration(_cardAtlas.NormalTexture);
        ForgetRegistration(_cardAtlas.MaterialTexture);
        ForgetRegistration(_cardAtlas.DepthTexture);

        if (!_cardAtlas.Configure(width, height))
        {
            return false;
        }

        _cardAtlas.BindForCapture();
        _cardCaptureActive = true;
        CurrentPass = ScenePass.Card;

        // A card is an orthographic view of one model from outside it, so the near surface is the one
        // that matters and back faces are never wanted. Both are set here rather than left to the
        // caller, because a capture is not a frame and has no state of its own to inherit.
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _depthState = DepthState.TestAndWrite;

        ApplyBlend(BlendMode.Opaque);
        return true;
    }

    /// <inheritdoc/>
    public void SetCardPage(int x, int y, int width, int height)
    {
        ThrowIfDisposed();

        if (!_cardCaptureActive || _cardAtlas is null)
        {
            throw new GraphicsDeviceException("No card capture is in progress.");
        }

        _cardAtlas.BindPage(x, y, width, height);
    }

    /// <inheritdoc/>
    public void EndCardCapture()
    {
        ThrowIfDisposed();

        if (!_cardCaptureActive || _cardAtlas is null)
        {
            throw new GraphicsDeviceException("No card capture is in progress.");
        }

        _cardAtlas.EndCapture();
        _cardCaptureActive = false;
        CurrentPass = ScenePass.None;

        // The drawing path caches which program is bound and which mesh uniforms it holds, and both
        // belong to whichever program the capture used.
        _lastMeshProgram = null;
        _meshUniformsPrimed = false;
        _sentMaterial = null;

        SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));
    }

    /// <inheritdoc/>
    public void ReadCardAtlas(int attachment, Span<Bgra32> destination)
    {
        ThrowIfDisposed();

        if (_cardAtlas is not { IsReady: true } atlas)
        {
            throw new GraphicsDeviceException("There is no card atlas to read.");
        }

        int needed = atlas.Width * atlas.Height;

        if (destination.Length < needed)
        {
            throw new GraphicsDeviceException(
                $"The card atlas is {atlas.Width}x{atlas.Height}; {needed} texels were needed, "
                + $"{destination.Length} offered.");
        }

        atlas.ReadAttachment(Math.Clamp(attachment, 0, 3), destination);
    }

    /// <inheritdoc/>
    public TextureHandle CardAlbedo => Register(_cardAtlas?.AlbedoTexture ?? 0);

    /// <inheritdoc/>
    public TextureHandle CardNormal => Register(_cardAtlas?.NormalTexture ?? 0);

    /// <inheritdoc/>
    public TextureHandle CardMaterial => Register(_cardAtlas?.MaterialTexture ?? 0);

    /// <inheritdoc/>
    public TextureHandle CardDepth => Register(_cardAtlas?.DepthTexture ?? 0);

    /// <inheritdoc/>
    public TextureHandle SceneDepth => Register(_sceneTarget?.DepthTexture ?? 0);

    /// <inheritdoc/>
    public TextureHandle SceneNormal => Register(_sceneTarget?.NormalTexture ?? 0);

    /// <inheritdoc/>
    public TextureHandle SceneColour => Register(_sceneTarget?.FrontColorTexture ?? 0);

    /// <summary>Drops the handle registered for a backend-owned texture, if there is one.</summary>
    private void ForgetRegistration(uint name)
    {
        if (name == 0)
        {
            return;
        }

        foreach ((uint id, GLTexture entry) in _textures)
        {
            // Only ever the borrowed registrations, which are the ones this made: they carry no
            // dimensions because the backend owns the texture and this holds nothing but its name.
            //
            // Matching on the name alone deleted textures it does not own. GL recycles names, so when
            // the card atlas is torn down and rebuilt on load, a freed name is handed straight back to
            // the next CreateStorageTexture -- and forgetting the atlas then unregistered a live
            // storage texture instead. It presented as the surface cache's own bounce atlases not
            // belonging to the device that had just created them, while the one created first, before
            // any name had been freed, read back perfectly.
            if (entry.Name == name && entry is { Width: 0, Height: 0 })
            {
                _textures.Remove(id);
                return;
            }
        }
    }

    /// <summary>Hands out a handle for a texture the backend owns, so callers can bind it.</summary>
    /// <remarks>
    /// The atlas textures are created by the framebuffer rather than by CreateTexture, so they have no
    /// handle until something asks for one. Registered on demand and remembered, so repeated asks give
    /// the same handle rather than leaking a new entry per frame.
    /// </remarks>
    private TextureHandle Register(uint name)
    {
        if (name == 0)
        {
            return TextureHandle.None;
        }

        foreach ((uint id, GLTexture entry) in _textures)
        {
            // Borrowed registrations only, for the same reason forgetting one is fussy: without this a
            // storage texture that happened to share a recycled name would be handed back here as
            // though it were the atlas being asked for.
            if (entry.Name == name && entry is { Width: 0, Height: 0 })
            {
                return new TextureHandle(id);
            }
        }

        uint handle = _nextTextureId++;
        _textures[handle] = new GLTexture(name, 0, 0, 1);
        return new TextureHandle(handle);
    }

    /// <inheritdoc/>
    public void SetMaterialOverride(in SurfaceMaterial material) => _material = material;

    /// <inheritdoc/>
    public void SetTextureDetail(TextureHandle diffuse, TextureHandle detail)
    {
        ThrowIfDisposed();

        if (!_textures.TryGetValue(diffuse.Id, out GLTexture entry))
        {
            throw new GraphicsDeviceException($"{diffuse} does not belong to this device.");
        }

        uint name = 0;

        if (detail.IsValid)
        {
            if (!_textures.TryGetValue(detail.Id, out GLTexture map))
            {
                throw new GraphicsDeviceException($"{detail} does not belong to this device.");
            }

            name = map.Name;
        }

        _textures[diffuse.Id] = entry with { Detail = name };
    }

    /// <inheritdoc/>
    public void SetMaterialDetail(float strength) => _materialDetail = MathF.Max(strength, 0f);

    /// <inheritdoc/>
    public void SetLighting(in SceneLighting lighting) => _lighting = lighting;

    /// <inheritdoc/>
    public void SetTextureQuality(int maxAnisotropy, bool generateMissingMipmaps)
    {
        ThrowIfDisposed();

        int clamped = Math.Clamp(maxAnisotropy, 1, Math.Max(1, _anisotropyLimit));
        if (clamped == _anisotropy && generateMissingMipmaps == _generateMissingMipmaps)
        {
            return;
        }

        _anisotropy = clamped;

        // Mip generation happens at upload, so a change only reaches textures loaded from here on --
        // in practice the next level. The anisotropy is sampler state and can be re-applied now.
        _generateMissingMipmaps = generateMissingMipmaps;

        foreach (GLTexture entry in _textures.Values)
        {
            BindTextureForUpload(entry.Name);
            _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)TextureMaxAnisotropy, (float)_anisotropy);
        }
    }

    /// <inheritdoc/>
    public void SetVolumetricFog(
        in VolumetricFogSettings fog,
        Vector3f cameraPosition,
        ReadOnlySpan<LocalFogVolume> volumes)
    {
        _fog = fog;
        _cameraPosition = cameraPosition;
        _fogVolumeCount = Math.Min(volumes.Length, LocalFogVolume.MaxPerLevel);

        for (int i = 0; i < _fogVolumeCount; i++)
        {
            _fogVolumes[i] = volumes[i];
        }
    }

    /// <inheritdoc/>
    public void SetTemporalAntiAliasing(
        in TemporalAaSettings settings,
        in Matrix4f projection,
        Vector3f cameraPosition,
        Matrix3f viewToWorld)
    {
        ThrowIfDisposed();

        _temporal = settings;
        _temporalProjection = projection;
        _temporalCamera = cameraPosition;
        _temporalViewToWorldValue = viewToWorld;
    }

    /// <inheritdoc/>
    public void SetShadows(
        in ShadowSettings settings,
        ReadOnlySpan<ShadowCascade> cascades,
        in DepthUnprojection unprojection,
        Matrix3f viewToWorld)
    {
        ThrowIfDisposed();

        _shadowSettings = settings;
        _unprojection = unprojection;
        _viewToWorld = viewToWorld;
        _cascadeCount = 0;

        if (!settings.Enabled || cascades.Length == 0 || _shadowMap is null)
        {
            return;
        }

        if (!_shadowMap.Configure(settings.Resolution, settings.EffectiveCascades))
        {
            return;
        }

        _cascadeCount = Math.Min(cascades.Length, settings.EffectiveCascades);

        for (int i = 0; i < _cascadeCount; i++)
        {
            // Camera-relative world into the cascade clip space, in one matrix: the light rotation and
            // then its box. The rotation carries no translation because the cascades are fitted in a
            // frame whose origin IS the camera.
            _cascadeProjections[i] = cascades[i].Projection;
            _cascadeMatrices[i] = cascades[i].Projection * Matrix4f.CreateRigid(cascades[i].LightRotation, default);
            _cascadeSplits[i] = cascades[i].SplitDistance;
        }
    }

    /// <inheritdoc/>
    public bool BeginShadowCascade(int cascade)
    {
        ThrowIfDisposed();

        if (_shadowMap is not { IsReady: true } map || cascade < 0 || cascade >= _cascadeCount)
        {
            return false;
        }

        map.BindCascade(cascade);
        _activeShadowCascade = cascade;
        CurrentPass = ScenePass.Shadow;

        // Per cascade, and the results add up rather than replace each other, because the budget is for
        // the shadow pass as a whole -- four cascades is a choice about how to spend one number.
        _stageProfiler?.Begin(RenderStage.Shadows);

        // Depth only, and front faces culled. Casting from the far side of a solid object puts the
        // depth it writes inside the object rather than on the lit surface, which is the cheapest way
        // to stop a bias from having to cover the whole thickness of a wall.
        SetDepthState(DepthState.TestAndWrite);
        SetCullMode(CullMode.Clockwise);
        SetProjection(_cascadeProjections[cascade]);
        return true;
    }

    /// <inheritdoc/>
    public void EndShadowCascade()
    {
        ThrowIfDisposed();

        if (_activeShadowCascade < 0)
        {
            return;
        }

        _stageProfiler?.End();
        _activeShadowCascade = -1;
        CurrentPass = ScenePass.None;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        SetCullMode(CullMode.CounterClockwise);
        SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));
    }

    /// <inheritdoc/>
    public void BeginPass(ScenePass pass)
    {
        ThrowIfDisposed();

        if (!_captureActive)
        {
            throw new GraphicsDeviceException($"Cannot begin the {pass} pass outside a scene capture.");
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        CurrentPass = pass;

        // The forward pass draws over the lit scene, not into the G-buffer: it is for the surfaces a
        // G-buffer cannot hold -- anything translucent -- and for the view model, which is drawn with
        // its own narrower projection and whose depth must not reach the effects that read depth.
        if (pass == ScenePass.Forward && _sceneTarget!.Description.Deferred)
        {
            // The accumulation closes here, before anything forward is drawn. The view model is the
            // reason: it is locked to the camera and writes no depth, so a depth-based reprojection
            // places it wherever the world behind it was and blends it with ten frames of its own sway.
            // Resolving first puts it in front of the accumulated image instead of inside it.
            ResolveTemporal(_sceneTarget);
            _sceneTarget.BindForColour();
        }
        else
        {
            _sceneTarget!.BindForGeometry();
        }

        // Started after the bind and, for the forward pass, after the temporal resolve it triggers --
        // both belong to the stages that own them rather than to this one. Shadow cascades have their
        // own bracket in BeginShadowCascade.
        if (pass is ScenePass.Geometry or ScenePass.Forward)
        {
            _stageProfiler?.Begin(pass == ScenePass.Geometry ? RenderStage.Geometry : RenderStage.Forward);
        }
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfDisposed();

        if (CurrentPass == ScenePass.None)
        {
            throw new GraphicsDeviceException("No pass is open.");
        }

        // Multisampled rasterisation is resolved as the geometry pass closes rather than at the end of
        // the capture: everything after it -- fog, reflections, the translucent pass -- reads the depth
        // texture, and a multisampled depth buffer is not something a shader can sample.
        // The stage closes before the multisample resolve, not after: the resolve belongs to the target
        // rather than to the geometry, and the deferred stages that follow are each timed in their own
        // right. Only one TIME_ELAPSED query may be open at a time, so they cannot be nested anyway.
        if (CurrentPass == ScenePass.Geometry)
        {
            _stageProfiler?.End();
            if (_sceneTarget!.Description.IsMultisampled)
            {
                using GLStageProfiler.Scope resolveTiming = Measure(RenderStage.GeometryResolve);
                _sceneTarget.ResolveMultisample();
            }
            else
            {
                _sceneTarget.ResolveMultisample();
            }

            if (_sceneTarget.Description.Deferred)
            {
                bool occluded = RunAmbientOcclusion(_sceneTarget);
                RunDeferredLighting(_sceneTarget, occluded);
                RunReflections(_sceneTarget);
                RunVolumetricFog(_sceneTarget);
            }
            else
            {
                _sceneTarget.BindForGeometry();
            }
        }
        else if (CurrentPass == ScenePass.Forward)
        {
            _stageProfiler?.End();
            _sceneTarget!.RestoreWorldDepth();
        }

        CurrentPass = ScenePass.None;
    }

    /// <inheritdoc/>
    public void EndSceneCapture()
    {
        ThrowIfDisposed();

        if (!_captureActive)
        {
            throw new GraphicsDeviceException("No scene capture is in progress.");
        }

        if (CurrentPass != ScenePass.None)
        {
            throw new GraphicsDeviceException($"The {CurrentPass} pass is still open.");
        }

        _sceneTarget!.ResolveMultisample();
        _captureActive = false;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));
    }

    /// <inheritdoc/>
    public void ResolveScene(in PostProcessSettings post)
    {
        ThrowIfDisposed();

        if (_captureActive)
        {
            throw new GraphicsDeviceException("The scene capture has not been ended.");
        }

        if (_sceneTarget is not { IsReady: true } target || _resolveProgram is null || _fullScreen is null)
        {
            throw new GraphicsDeviceException("Nothing has been captured this frame.");
        }

        // A frame with no forward pass -- the free camera draws no view model -- has not resolved yet.
        // Outside the post bracket: the temporal resolve is its own stage and has already been timed
        // when the forward pass triggered it.
        ResolveTemporal(target);

        // Before the bloom, so that a defocused highlight blooms as the wide soft thing it has become
        // rather than as the point it was: the lens spreads what actually reaches it.
        uint scene;
        using (GLStageProfiler.Scope timing = Measure(RenderStage.CameraEffects, post.WantsCameraEffects))
        {
            scene = RunCameraEffects(
                target, post, _motionPreviousViewProjection, _motionPreviousCamera, _motionHasPreviousFrame);
        }

        bool bloomed;
        using (GLStageProfiler.Scope timing = Measure(RenderStage.Bloom, post.WantsBloom))
        {
            bloomed = RunBloom(target, post, scene);
        }

        // With anti-aliasing on, the tone map lands in a buffer rather than on the screen, so the
        // edge filter has a finished picture to read. Edges are a property of the DISPLAYED image --
        // detecting them in linear light finds them in the wrong places -- which is why this sits after
        // the encode rather than anywhere earlier.
        bool antiAliased = post.WantsSpatialAa
            && target.HasResolved
            && _spatialAaProgram is not null;

        if (antiAliased)
        {
            target.BindForResolved();
        }
        else
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));
        }

        // Auto-exposure runs before the resolve uses its result: it reads last frame's adapted value and
        // measures this frame for the next. Off, or without a tone map, it returns the cvar's exposure
        // unchanged -- and the vanilla copy path never reaches here at all, so it is untouched either way.
        float exposure;
        using (GLStageProfiler.Scope timing = Measure(
            RenderStage.Exposure,
            post.AutoExposure && post.ToneMap != ToneMapOperator.None))
        {
            exposure = RunAutoExposure(scene, post);
        }

        using GLStageProfiler.Scope toneTiming = Measure(RenderStage.ToneMapGrade);
        UseProgram(_resolveProgram);
        _resolveProgram.SetFloat(_resolveExposure, exposure);
        _resolveProgram.SetInt(_resolveToneMap, (int)post.ToneMap);
        _resolveProgram.SetInt(_resolveDecodeInput, post.SceneIsDisplayEncoded ? 1 : 0);
        _resolveProgram.SetFloat(_resolveWhitePoint, post.WhitePoint);
        _resolveProgram.SetFloat(_resolveBloomIntensity, bloomed ? post.BloomIntensity : 0f);
        _resolveProgram.SetVector4(
            _resolveGrade,
            MathF.Max(post.Contrast, 0f),
            MathF.Max(post.Saturation, 0f),
            Math.Clamp(post.Vignette, 0f, 1f),
            Math.Clamp(post.Grain, 0f, 1f));

        _gradeFrame = (_gradeFrame + 1) & 1023;
        _resolveProgram.SetVector3(
            _resolveLens,
            MathF.Max(post.ChromaticAberration, 0f),
            MathF.Max(post.Sharpen, 0f),
            _gradeFrame * 0.61803399f);
        _resolveProgram.SetVector2(
            _resolveTexelSize, 1f / target.Description.PresentWidth, 1f / target.Description.PresentHeight);

        // The strength carries the "is there one" as well: with no table uploaded the shader's branch
        // is never taken and the sampler is never read, which is what keeps an unbound sampler3D from
        // being sampled at all.
        bool graded = post.WantsColourLookup && _lutTexture != 0;
        _resolveProgram.SetVector2(_resolveLut, graded ? Math.Clamp(post.ColourLookup, 0f, 1f) : 0f, _lutSize);
        _resolveProgram.SetInt(_resolveGreenPhosphor, post.GreenPhosphor ? 1 : 0);

        // Clamped here rather than trusted from the caller: the shader indexes an exponential and a
        // handful of step edges off these, and a value outside the unit range does not degrade, it
        // produces a black or a white screen.
        TacticalDisplaySettings tactical = post.Tactical;
        _resolveProgram.SetVector4(
            _resolveTactical,
            Math.Clamp(tactical.Amount, 0f, 1f),
            Math.Clamp(tactical.Boot, 0f, 1f),
            Math.Clamp(tactical.Sweep, 0f, 1f),
            tactical.SweepPosition);
        _resolveProgram.SetFloat(_resolveTacticalTime, tactical.Seconds);

        // The binocular glass, clamped for the same reason: its bezel port and grain index step edges
        // that produce a black frame outside the unit range rather than degrading gracefully.
        BinocularDisplaySettings binocular = post.Binocular;
        _resolveProgram.SetVector3(
            _resolveBinocular,
            Math.Clamp(binocular.Amount, 0f, 1f),
            Math.Max(binocular.Aberration, 0f),
            0f);
        _resolveProgram.SetFloat(_resolveBinocularTime, binocular.Seconds);

        if (graded)
        {
            BindProbeTexture(8, _lutTexture);
        }

        if (bloomed)
        {
            BindTexture(7, target.BloomTexture);
        }

        // Only bound when asked for. There are eight units in all, and holding six of them every frame
        // for a view nobody has switched on would leave the passes after this two.
        // The probe view is answered by the lighting pass, which has already run and written it into
        // the scene, so the resolve must pass that through rather than overwrite it with a G-buffer.
        // The probe and reflection views are answered by the passes that own them and are already in
        // the scene, so the resolve passes those through rather than overwriting with a G-buffer.
        int debug = target.Description.Deferred
            && _debugView != DebugView.Probes
            && _debugView != DebugView.Ssr
                ? (int)_debugView
                : 0;
        _resolveProgram.SetInt(_resolveDebugView, debug);

        if (debug != 0)
        {
            BindTexture(4, target.DepthTexture);
            BindTexture(3, target.OcclusionTexture);
            BindTexture(5, target.MotionTexture);
            BindTexture(6, target.AlbedoTexture);
            BindTexture(7, target.NormalTexture);
            BindTexture(1, target.BakedLightTexture);
            BindTexture(2, target.MaterialTexture);
        }

        BindTexture(0, scene);
        _fullScreen.Draw();
        toneTiming.Dispose();

        if (antiAliased)
        {
            using GLStageProfiler.Scope aaTiming = Measure(RenderStage.SpatialAntiAliasing);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));

            UseProgram(_spatialAaProgram!);
            _spatialAaProgram!.SetVector2(
                _spatialAaTexelSize,
                1f / target.Description.PresentWidth,
                1f / target.Description.PresentHeight);
            _spatialAaProgram.SetVector2(
                _spatialAaEdge,
                MathF.Max(post.SpatialAaThreshold, 0.001f),
                Math.Clamp(post.SpatialAa, 0f, 1f));

            BindTexture(0, target.ResolvedTexture);
            _fullScreen.Draw();
        }

        // Re-apply the depth state for real rather than just recording it. Writing the shadow alone was
        // a lie: the full-screen pass restores whatever depth state the last 3D draw left, so the next
        // frame's SetDepthState(Disabled) saw "already disabled" and did nothing, the sky dome was then
        // drawn WITH depth testing and writing, and every piece of geometry beyond the dome's radius --
        // the horizon, the far buildings, the top of the water tower -- failed the test and showed sky.
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;

        _currentVertexArray = 0;
    }

    /// <summary>Defocuses what is off the focal plane and smears what moved.</summary>
    /// <param name="target">The scene target holding the finished frame, its depth and its motion.</param>
    /// <param name="post">The frame's settings.</param>
    /// <param name="previousViewProjection">
    /// The view-projection of the frame BEFORE this one. Passed in rather than read from the field,
    /// because the temporal resolve that runs immediately before this has already advanced the field to
    /// the current frame — reading it here reprojected every pixel onto itself and produced no motion at
    /// all, at any <c>r_motion_blur</c> setting.
    /// </param>
    /// <param name="previousCamera">Where the camera was on that frame, for the same reason.</param>
    /// <param name="hasPreviousFrame">Whether there is a previous frame to reproject against at all.</param>
    /// <returns>The texture holding the frame afterwards, which is unchanged when nothing ran.</returns>
    /// <remarks>
    /// Into a buffer of its own rather than the scene ping-pong, because by this point the frame may
    /// be at the output resolution — the temporal upsample has already happened — while the scene pair
    /// is sized for the render resolution.
    /// </remarks>
    private uint RunCameraEffects(
        GLSceneTarget target,
        in PostProcessSettings post,
        in Matrix4f previousViewProjection,
        Vector3f previousCamera,
        bool hasPreviousFrame)
    {
        if (!post.WantsCameraEffects
            || !target.HasScratch
            || _cameraProgram is null
            || _fullScreen is null)
        {
            return target.FrontColorTexture;
        }

        target.BindForScratch();
        UseProgram(_cameraProgram);

        _cameraProgram.SetVector4(
            _cameraUnproject,
            _unprojection.XScale,
            _unprojection.YScale,
            _unprojection.Near,
            _unprojection.Far);
        _cameraProgram.SetVector4(
            _cameraFocus,
            MathF.Max(post.FocalDistance, 0f),
            MathF.Max(post.FocalRange, 0.01f),
            post.FocalDistance > 0f ? MathF.Max(post.FocalBlur, 0f) : 0f,
            Math.Clamp(post.CameraTaps, 1, 16));

        _cameraFrame = (_cameraFrame + 1) & 63;
        // The motion attachment belongs to the deferred G-buffer. In the non-deferred offscreen path
        // the handle is zero, so binding it would make the shader read the backend's fallback texture
        // as a full-screen velocity field. That turns a small motion-blur setting into an unstable smear
        // across the whole level. Depth-of-field still works from the shared depth attachment; only the
        // motion component must be gated by the buffer that actually exists.
        float motionBlur = target.Description.Deferred ? post.MotionBlur : 0f;
        // The camera gather uses this phase to decorrelate its defocus taps. That is useful when TAA
        // accumulates the samples, but with the user's non-deferred/non-temporal path it turns a static
        // depth-of-field image into fresh noise every frame. Keep the gather stable unless history is
        // actually blending it; otherwise the result reads as screen flicker rather than bokeh.
        float cameraJitter = _temporal.Accumulates ? _cameraFrame * 0.61803399f : 0f;
        _cameraProgram.SetVector3(
            _cameraMotionBlur,

            // Nothing to reproject against on the very first frame, and a reprojection against an
            // identity matrix would smear the whole picture in one direction.
            hasPreviousFrame ? Math.Clamp(motionBlur, 0f, 1f) : 0f,

            // A hard ceiling in pixels, so one very fast object cannot smear across the frame -- and
            // cannot cost a thousand taps doing it.
            48f,
            cameraJitter);

        _cameraProgram.SetMatrix4(_cameraPrevViewProjection, previousViewProjection);
        _cameraProgram.SetVector3(
            _cameraDelta,
            _temporalCamera.X - previousCamera.X,
            _temporalCamera.Y - previousCamera.Y,
            _temporalCamera.Z - previousCamera.Z);
        _cameraProgram.SetMatrix3(_cameraViewToWorld, _temporalViewToWorldValue);
        _cameraProgram.SetVector2(
            _cameraTexelSize,
            1f / target.Description.PresentWidth,
            1f / target.Description.PresentHeight);

        BindTexture(0, target.FrontColorTexture);
        BindTexture(4, target.DepthTexture);
        BindTexture(5, target.MotionTexture);

        _gl.Disable(EnableCap.DepthTest);
        // Keep the real mask consistent with DepthState.Disabled. With depth testing disabled the
        // full-screen draw cannot write depth, while a following glClear must still be able to do so.
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;

        _fullScreen.Draw();
        return target.ScratchTexture;
    }

    // Measures the scene's brightness and eases a running exposure towards it, returning the exposure to
    // resolve with. Off, or on a context without compute, or before a tone map, it returns the cvar's own
    // exposure and does nothing else.
    private float RunAutoExposure(uint scene, in PostProcessSettings post)
    {
        if (!post.AutoExposure
            || post.ToneMap == ToneMapOperator.None
            || !_autoExposureReduce.IsValid
            || !_autoExposureAdapt.IsValid)
        {
            return post.Exposure;
        }

        // Last frame's adapted exposure, read before this frame overwrites it. A frame of latency, which
        // eye adaptation does not notice, and no stall waiting on work only just submitted.
        uint[] state = new uint[3];
        ReadStorageBuffer<uint>(_autoExposureAccum, state);
        float adapted = BitConverter.UInt32BitsToSingle(state[2]);

        // Measure this frame into the accumulator and ease the running exposure towards it, for the next
        // frame to read. The scene is bound to unit zero, which the resolve rebinds for itself afterwards.
        BindTexture(0, scene);
        SetComputeUniform(_autoExposureReduce, "uScene", 0);
        SetComputeUniform(_autoExposureReduce, "uDecode", post.SceneIsDisplayEncoded ? 1 : 0);
        SetComputeUniform(_autoExposureReduce, "uGrid", AutoExposureShaders.Grid);
        SetComputeUniform(_autoExposureReduce, "uFixedScale", AutoExposureShaders.FixedScale);
        SetComputeUniform(_autoExposureReduce, "uFixedBias", AutoExposureShaders.FixedBias);
        BindStorageBuffer(0, _autoExposureAccum);

        int groups = (AutoExposureShaders.Grid + 7) / 8;
        Dispatch(_autoExposureReduce, groups, groups);
        MemoryBarrier(MemoryBarrierScope.StorageBuffer);

        // Middle grey as the target, a small step per frame so the eye adapts over a moment rather than
        // snapping, and a range that keeps a black or blown scene from driving the exposure to an extreme.
        SetComputeUniform(_autoExposureAdapt, "uKey", 0.18f);
        SetComputeUniform(_autoExposureAdapt, "uRate", 0.04f);
        SetComputeUniform(_autoExposureAdapt, "uMin", 0.05f);
        SetComputeUniform(_autoExposureAdapt, "uMax", 16.0f);
        SetComputeUniform(_autoExposureAdapt, "uFixedScale", AutoExposureShaders.FixedScale);
        SetComputeUniform(_autoExposureAdapt, "uFixedBias", AutoExposureShaders.FixedBias);
        BindStorageBuffer(0, _autoExposureAccum);
        Dispatch(_autoExposureAdapt, 1);
        MemoryBarrier(MemoryBarrierScope.StorageBuffer);

        // Until the first adapt has run the buffer still holds zero; the cvar's exposure stands in.
        return adapted > 0f ? adapted * post.Exposure : post.Exposure;
    }

    /// <summary>Spreads the brightest part of the frame into a half-resolution buffer.</summary>
    /// <param name="target">The scene target holding the finished frame.</param>
    /// <param name="post">The frame's settings, for the threshold and whether to run at all.</param>
    /// <param name="scene">The finished frame, which may be the camera pass's output rather than the target's own.</param>
    /// <returns><see langword="true"/> when there is a bloom buffer for the resolve to add.</returns>
    /// <remarks>
    /// Bright pass, then a separable Gaussian: a two-dimensional Gaussian is the product of two
    /// one-dimensional ones, so a wide blur costs two passes rather than the square of its radius.
    /// </remarks>
    private bool RunBloom(GLSceneTarget target, in PostProcessSettings post, uint scene)
    {
        if (!post.WantsBloom
            || !target.HasBloom
            || _bloomThresholdProgram is null
            || _bloomBlurProgram is null
            || _fullScreen is null)
        {
            return false;
        }

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;

        (float texelX, float texelY) = target.BloomTexelSize;

        target.BindForBloom(0);
        UseProgram(_bloomThresholdProgram);

        // The knee is a quarter of the threshold rather than a constant: what counts as "just below
        // the threshold" has to scale with the threshold, or a high one gets a hard edge back.
        _bloomThresholdProgram.SetVector2(
            _bloomThreshold, post.BloomThreshold, MathF.Max(post.BloomThreshold * 0.25f, 0.01f));
        BindTexture(0, scene);
        _fullScreen.Draw();

        UseProgram(_bloomBlurProgram);

        // Two passes per widening, run twice: the second pair samples an already-blurred image, so the
        // effective radius grows far faster than the tap count does.
        for (int pass = 0; pass < 2; pass++)
        {
            float spread = pass + 1;

            target.BindForBloom(1);
            _bloomBlurProgram.SetVector2(_bloomDirection, texelX * spread, 0f);
            BindTexture(0, target.BloomTextureAt(0));
            _fullScreen.Draw();

            target.BindForBloom(0);
            _bloomBlurProgram.SetVector2(_bloomDirection, 0f, texelY * spread);
            BindTexture(0, target.BloomTextureAt(1));
            _fullScreen.Draw();
        }

        return true;
    }

    /// <summary>Reflects the lit frame in the surfaces smooth enough to show it.</summary>
    /// <param name="target">The scene target holding the lit colour, its depth and the G-buffer.</param>
    /// <remarks>
    /// After the lighting because a reflection is of the lit world, and before the fog because a
    /// reflection belongs behind the air rather than in front of it.
    /// </remarks>
    private void RunReflections(GLSceneTarget target)
    {
        if (!_reflections.Enabled || _reflectionProgram is null || _fullScreen is null)
        {
            return;
        }

        using GLStageProfiler.Scope timing = Measure(RenderStage.Reflections);

        target.BindForPostProcess();
        UseProgram(_reflectionProgram);

        _reflectionProgram.SetVector4(
            _reflectionUnproject,
            _unprojection.XScale,
            _unprojection.YScale,
            _unprojection.Near,
            _unprojection.Far);
        _reflectionProgram.SetVector4(
            _reflectionMarch,
            _reflections.Marches ? _reflections.Quality : 0,
            MathF.Max(_reflections.Intensity, 0f),
            MathF.Max(_reflections.MaxRoughness, 0.01f),
            MathF.Max(_reflections.Thickness, 0.01f));
        _reflectionProgram.SetVector2(
            _reflectionTexelSize, 1f / target.Description.Width, 1f / target.Description.Height);

        _reflectionFrame = (_reflectionFrame + 1) & 63;
        _reflectionProgram.SetFloat(_reflectionJitter, _reflectionFrame * 0.61803399f);

        bool probes = _hasProbes && _volumeSettings.Enabled;
        _reflectionProgram.SetInt(_reflectionProbesEnabled, probes ? 1 : 0);

        if (probes)
        {
            BindProbeTexture(8, _probeTextures[0]);
            BindProbeTexture(9, _probeTextures[1]);
            BindProbeTexture(10, _probeTextures[2]);
            _reflectionProgram.SetVector3(
                _reflectionProbeOrigin, _probeOrigin.X, _probeOrigin.Y, _probeOrigin.Z);
            _reflectionProgram.SetVector3(
                _reflectionProbeInverseCount,
                _probeInverseCount.X,
                _probeInverseCount.Y,
                _probeInverseCount.Z);
            _reflectionProgram.SetFloat(_reflectionProbeSpacing, _probeSpacing);
        }

        // What a ray that finds nothing at all falls back to. The sky is what most of a reflected ray
        // in this game would have hit, and the ambient term is what the level says its sky is worth.
        float intensity = probes ? MathF.Max(_volumeSettings.Intensity, 0f) : 1f;
        _reflectionProgram.SetVector3(
            _reflectionProbeSky,
            _lighting.AmbientColour.X * intensity,
            _lighting.AmbientColour.Y * intensity,
            _lighting.AmbientColour.Z * intensity);

        _reflectionProgram.SetMatrix3(_reflectionViewToWorld, _viewToWorld);
        _reflectionProgram.SetVector3(
            _reflectionCamera, _cameraPosition.X, _cameraPosition.Y, _cameraPosition.Z);
        _reflectionProgram.SetInt(_reflectionShow, _debugView == DebugView.Ssr ? 1 : 0);

        BindTexture(0, target.FrontColorTexture);
        BindTexture(1, target.NormalTexture);
        BindTexture(2, target.AlbedoTexture);
        BindTexture(3, target.MaterialTexture);
        BindTexture(4, target.DepthTexture);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;

        _fullScreen.Draw();
        target.Swap();
    }

    /// <summary>Blends this frame into the accumulated ones.</summary>
    /// <param name="target">The scene target holding the frame, its depth, and the history.</param>
    /// <remarks>
    /// Runs once per frame, at the first of the forward pass opening or the frame resolving, and is a
    /// no-op thereafter. The accumulated image is copied back over the scene colour, so everything after
    /// this point draws onto it and reads it exactly as it would have without any accumulation at all.
    /// </remarks>
    private void ResolveTemporal(GLSceneTarget target)
    {
        if (_temporalResolved)
        {
            return;
        }

        _temporalResolved = true;

        if (!_temporal.Accumulates || _temporalProgram is null || _fullScreen is null)
        {
            // Still worth remembering where the camera was: a frame with the accumulation off is not a
            // discontinuity, and forgetting would make every toggle of the setting cost a full
            // convergence.
            RememberFrame();
            return;
        }

        if (!target.BindForTemporalResolve())
        {
            RememberFrame();
            return;
        }

        using GLStageProfiler.Scope timing = Measure(RenderStage.TemporalResolve);

        // The first frame after a rebuild has nothing behind it, and the second has a previous camera
        // but no accumulated picture. Both resolve to the current frame alone, which is exactly what a
        // feedback of one does -- so the pass runs either way and the history is seeded rather than
        // special-cased.
        bool hasHistory = target.HasHistory && _hasPreviousFrame && !IsCut();
        float feedback = hasHistory ? Math.Clamp(_temporal.Feedback, 0f, 1f) : 1f;

        UseProgram(_temporalProgram);
        _temporalProgram.SetVector4(
            _temporalUnproject, _unprojection.XScale, _unprojection.YScale, _unprojection.Near, _unprojection.Far);
        _temporalProgram.SetMatrix3(_temporalViewToWorld, _temporalViewToWorldValue);
        _temporalProgram.SetMatrix4(_temporalPrevViewProjection, _previousViewProjection);
        _temporalProgram.SetVector3(
            _temporalCameraDelta,
            _temporalCamera.X - _previousCamera.X,
            _temporalCamera.Y - _previousCamera.Y,
            _temporalCamera.Z - _previousCamera.Z);
        _temporalProgram.SetVector2(
            _temporalRenderSize, target.Description.Width, target.Description.Height);
        _temporalProgram.SetVector2(_temporalJitter, _temporal.JitterX, _temporal.JitterY);
        _temporalProgram.SetFloat(
            _temporalUpsampling, target.Description.IsUpsampling && target.HasOutput ? 1f : 0f);
        _temporalProgram.SetFloat(_temporalFeedback, feedback);
        // Scaled by the feedback, and this is not a fudge. The sharpening is applied to the history and
        // the result becomes the next frame's history, so it compounds: at a feedback of 0.1 a pixel is
        // sharpened some ten times before it is replaced, and a gain of 0.25 asks for two and a half
        // times the contrast the setting names. Measured, an unscaled 0.25 put 38% MORE high-frequency
        // energy into the frame than having no anti-aliasing at all -- it was not sharpening the image,
        // it was ringing it. Scaling by the share of the pixel that is replaced each frame makes the
        // number mean the same thing at every feedback.
        _temporalProgram.SetFloat(
            _temporalSharpening, hasHistory ? Math.Clamp(_temporal.MotionSharpening, 0f, 1f) * feedback : 0f);

        BindTexture(0, target.FrontColorTexture);
        BindTexture(1, target.HistoryTexture);
        BindTexture(4, target.DepthTexture);
        BindTexture(5, target.MotionTexture);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;

        _fullScreen.Draw();

        target.CopyTemporalResultToScene();
        target.AdvanceHistory();
        RememberFrame();
    }

    /// <summary>Tells whether the camera jumped rather than moved since the previous frame.</summary>
    private bool IsCut()
    {
        float dx = _temporalCamera.X - _previousCamera.X;
        float dy = _temporalCamera.Y - _previousCamera.Y;
        float dz = _temporalCamera.Z - _previousCamera.Z;

        if ((dx * dx) + (dy * dy) + (dz * dz) > CutTravelMetres * CutTravelMetres)
        {
            return true;
        }

        // A turn can be a cut without the eye moving at all -- the death camera swinging behind the
        // body, or a level starting the player facing the other way. The third row is where the view
        // looks; two frames of ordinary turning agree closely, and anything past a right angle in one
        // frame is not turning.
        float facing = (_temporalViewToWorldValue.M31 * _previousViewToWorld.M31)
            + (_temporalViewToWorldValue.M32 * _previousViewToWorld.M32)
            + (_temporalViewToWorldValue.M33 * _previousViewToWorld.M33);

        return facing < 0f;
    }

    /// <summary>Keeps this frame's camera so the next one can reproject into it.</summary>
    private void RememberFrame()
    {
        // The world-to-view rotation is the transpose of the one handed in, because a rotation's
        // inverse is its transpose -- and it is folded into the projection here, once, rather than in
        // the shader once per pixel.
        _previousViewProjection =
            _temporalProjection * Matrix4f.CreateRigid(_temporalViewToWorldValue.Transposed(), default);
        _previousCamera = _temporalCamera;
        _previousViewToWorld = _temporalViewToWorldValue;
        _hasPreviousFrame = true;
    }

    /// <summary>Searches the depth buffer's horizons for how open each surface is.</summary>
    /// <param name="target">The scene target holding the G-buffer and the occlusion buffer.</param>
    /// <returns><see langword="true"/> when the lighting pass has an occlusion buffer to read.</returns>
    /// <remarks>
    /// Before the lighting rather than after it, because its result is an input to the ambient term
    /// rather than a filter over the finished picture. Darkening a lit frame afterwards would darken
    /// the sunlight too, and sunlight in a corner is the shadow map's business.
    /// </remarks>
    private bool RunAmbientOcclusion(GLSceneTarget target)
    {
        if (!_occlusion.Enabled
            || _occlusionProgram is null
            || _occlusionReconstructProgram is null
            || _fullScreen is null)
        {
            return false;
        }

        if (!target.BindForOcclusionSearch())
        {
            return false;
        }

        using GLStageProfiler.Scope timing = Measure(RenderStage.AmbientOcclusionSearch);

        UseProgram(_occlusionProgram);
        _occlusionProgram.SetVector4(
            _occlusionUnproject,
            _unprojection.XScale,
            _unprojection.YScale,
            _unprojection.Near,
            _unprojection.Far);
        int directions = Math.Clamp(_occlusion.Quality, 1, 8);
        if (_temporal.Accumulates)
        {
            // The slice rotation already advances every frame. High and Ultra therefore cover their
            // configured angular set over two accumulated frames instead of tracing it redundantly in
            // each one. Enhanced has no temporal history and deliberately keeps every direction now.
            directions = (directions + 1) / 2;
        }

        _occlusionProgram.SetVector4(
            _occlusionSearch,
            directions,
            Math.Clamp(_occlusion.BounceQuality > 0 ? _occlusion.BounceQuality * 4 : _occlusion.Quality * 4, 1, 32),
            MathF.Max(_occlusion.Radius, 0.01f),
            MathF.Max(_occlusion.Intensity, 0f));

        // Rotated by a different amount each frame as well as each pixel, so that the directions a
        // pixel has not searched this frame are ones it searches next -- which is what turns a handful
        // of slices into a converged hemisphere once the temporal accumulation has it.
        _occlusionFrame = (_occlusionFrame + 1) & 63;
        _occlusionProgram.SetVector4(
            _occlusionBounce,
            _occlusion.WantsBounce ? _occlusion.BounceIntensity : 0f,
            _occlusion.WantsBounce ? 1f : 0f,
            _occlusionFrame * 0.61803399f,
            0f);

        (float lowTexelX, float lowTexelY) = target.OcclusionSearchTexelSize;
        _occlusionProgram.SetVector2(_occlusionTexelSize, lowTexelX, lowTexelY);

        BindTexture(4, target.DepthTexture);
        BindTexture(1, target.NormalTexture);
        BindTexture(2, target.HistoryTexture);
        BindTexture(3, target.MaterialTexture);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;

        _fullScreen.Draw();

        timing.Dispose();
        if (!target.BindForOcclusion())
        {
            return false;
        }

        using GLStageProfiler.Scope reconstructTiming = Measure(RenderStage.AmbientOcclusionReconstruct);
        UseProgram(_occlusionReconstructProgram);
        _occlusionReconstructProgram.SetVector2(_occlusionReconstructTexelSize, lowTexelX, lowTexelY);
        BindTexture(6, target.OcclusionSearchTexture);
        BindTexture(4, target.DepthTexture);
        BindTexture(1, target.NormalTexture);
        _fullScreen.Draw();
        return true;
    }

    /// <summary>Lights the G-buffer into the scene's colour buffer.</summary>
    /// <param name="target">The scene target holding the G-buffer.</param>
    /// <param name="occluded">Whether the occlusion buffer holds this frame's horizon search.</param>
    /// <remarks>
    /// One full-screen pass. Under <see cref="LightingModel.Baked"/> it computes <c>albedo × baked</c>,
    /// which is the same product the original's forward shader writes — so the deferred path
    /// reproduces the forward one rather than approximating it.
    /// </remarks>
    private void RunDeferredLighting(GLSceneTarget target, bool occluded)
    {
        using GLStageProfiler.Scope timing = Measure(RenderStage.Lighting);

        target.BindForColour();

        UseProgram(_lightingProgram!);
        _lightingProgram!.SetInt(_lightingModelLocation, (int)_lighting.Model);
        _lightingProgram.SetInt(_lightingOcclusionEnabled, occluded ? 1 : 0);

        if (occluded)
        {
            BindTexture(6, target.OcclusionTexture);
        }

        bool probes = _hasProbes && _volumeSettings.Enabled;
        _lightingProgram.SetInt(_lightingProbesEnabled, probes ? 1 : 0);
        _lightingProgram.SetInt(_lightingShowProbes, _debugView == DebugView.Probes ? 1 : 0);

        // Never scales the baked model: that path has to reproduce the original exactly, and the
        // original multiplies the texture by the lightmap and stops.
        _lightingProgram.SetFloat(
            _lightingScale, _lighting.Model == LightingModel.Baked ? 1f : MathF.Max(_lighting.Scale, 0f));

        if (probes)
        {
            BindProbeTexture(8, _probeTextures[0]);
            BindProbeTexture(9, _probeTextures[1]);
            BindProbeTexture(10, _probeTextures[2]);
            BindProbeTexture(11, _probeTextures[3]);

            _lightingProgram.SetVector3(
                _lightingProbeOrigin, _probeOrigin.X, _probeOrigin.Y, _probeOrigin.Z);
            _lightingProgram.SetVector3(
                _lightingProbeInverseCount,
                _probeInverseCount.X,
                _probeInverseCount.Y,
                _probeInverseCount.Z);

            // The normal offset is half a probe spacing: far enough to leave the surface it shades,
            // near enough not to cross the next wall along.
            _lightingProgram.SetVector2(_lightingProbeScale, _probeSpacing, _probeSpacing * 0.5f);

            Vector3f sky = _volumeSettings.Sky;
            float intensity = MathF.Max(_volumeSettings.Intensity, 0f);
            _lightingProgram.SetFloat(
                _lightingProbeBlend, Math.Clamp(_volumeSettings.Blend, 0f, 1f));
            _lightingProgram.SetVector3(
                _lightingProbeSky, sky.X * intensity, sky.Y * intensity, sky.Z * intensity);
            _lightingProgram.SetMatrix3(_lightingProbeViewToWorld, _viewToWorld);
            _lightingProgram.SetVector3(
                _lightingProbeCamera, _cameraPosition.X, _cameraPosition.Y, _cameraPosition.Z);
        }
        _lightingProgram.SetVector3(
            _lightingSunDirection, _lighting.SunDirection.X, _lighting.SunDirection.Y, _lighting.SunDirection.Z);
        _lightingProgram.SetVector3(
            _lightingSunColour, _lighting.SunColour.X, _lighting.SunColour.Y, _lighting.SunColour.Z);
        _lightingProgram.SetVector3(
            _lightingAmbient, _lighting.AmbientColour.X, _lighting.AmbientColour.Y, _lighting.AmbientColour.Z);

        _lightingProgram.SetInt(_lightingCascadeCount, _cascadeCount);
        _lightingProgram.SetVector4(
            _lightingCascadeSplits, _cascadeSplits[0], _cascadeSplits[1], _cascadeSplits[2], _cascadeSplits[3]);
        _lightingProgram.SetVector4(
            _lightingUnproject, _unprojection.XScale, _unprojection.YScale, _unprojection.Near, _unprojection.Far);
        _lightingProgram.SetMatrix3(_lightingViewToWorld, _viewToWorld);
        _lightingProgram.SetVector2(_lightingShadowBias, _shadowSettings.DepthBias, _shadowSettings.NormalBias);
        _lightingProgram.SetFloat(
            _lightingShadowTexel, _shadowSettings.Resolution > 0 ? 1f / _shadowSettings.Resolution : 0f);
        _lightingProgram.SetVector3(
            _lightingShadowDetail,
            _shadowSettings.ContactReach,
            _shadowSettings.ContactThickness,
            _shadowSettings.FilterRadius);

        for (int i = 0; i < _cascadeCount; i++)
        {
            _lightingProgram.SetMatrix4(_lightingCascadeMatrix, i, _cascadeMatrices[i]);
        }

        BindTexture(4, target.DepthTexture);

        if (_shadowMap is { IsReady: true } shadows)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + 5);
            _gl.BindTexture(TextureTarget.Texture2DArray, shadows.Texture);
            _activeTextureUnit = 5;
        }

        BindTexture(0, target.AlbedoTexture);
        BindTexture(1, target.NormalTexture);
        BindTexture(2, target.BakedLightTexture);
        BindTexture(3, target.MaterialTexture);

        // The screen probe gather's indirect, if the game layer traced one this frame. It arrives as
        // irradiance E and becomes the diffuse fill in place of the flat ambient: fed into the shader's
        // own diffuseIrradiance, where albedo * E * uLightScale applies it in this renderer's convention
        // -- no one-over-pi, metals excluded by diffuseAlbedo -- rather than as a post-pass. Cleared
        // after, so the next frame starts from nothing.
        bool screenGi = _screenIndirectStrength > 0f && _screenIndirect.IsValid;
        _lightingProgram.SetFloat(_lightingGiStrength, screenGi ? _screenIndirectStrength : 0f);

        if (screenGi)
        {
            SetTexture(7, _screenIndirect);
        }

        // The level's local lights (lamps), if the game set any this frame. The baked model returns before
        // it ever reaches them, and the game only submits under a shading model, so the count is enough of
        // a guard here -- an empty set is a count of zero and the shader's loop does not run.
        _lightingProgram.SetInt(_lightingLocalLightCount, _localLightCount);

        if (_localLightCount > 0)
        {
            _lightingProgram.SetVector4Array(
                _lightingLocalLightPositionRadius, _localLightPositionRadius.AsSpan(0, _localLightCount * 4));
            _lightingProgram.SetVector4Array(
                _lightingLocalLightColourIntensity, _localLightColourIntensity.AsSpan(0, _localLightCount * 4));
        }

        _fullScreen!.Draw();

        _screenIndirect = default;
        _screenIndirectStrength = 0f;
        _localLightCount = 0;

        // The pass left the bindings and the depth shadow in a state the next draw must not inherit.
        _currentVertexArray = 0;
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;
    }

    /// <inheritdoc/>
    public void SetScreenIndirect(TextureHandle indirect, float strength)
    {
        // Held, not acted on: the deferred lighting reads it when the geometry pass closes, so this only
        // records what the next RunDeferredLighting should fold in. Called before EndPass, while the
        // G-buffer the gather traced from is still live.
        _screenIndirect = indirect;
        _screenIndirectStrength = MathF.Max(strength, 0f);
    }

    /// <summary>Sets the level's real-time local lights for the next deferred lighting pass.</summary>
    /// <param name="lights">The lights, in view space; a set longer than <see cref="DeferredLocalLight.Maximum"/> is truncated.</param>
    /// <remarks>
    /// Held, not acted on: like <see cref="SetScreenIndirect"/> this only records what the next
    /// <see cref="RunDeferredLighting"/> folds in, and the pass clears it after, so a frame that sets none
    /// lights from none. Called each frame while the G-buffer is live, before the geometry pass closes.
    /// The positions are already in view space -- the game layer transforms them there in double precision,
    /// because the level sits at large world coordinates and a float subtract of the camera loses too much.
    /// </remarks>
    public void SetLocalLights(ReadOnlySpan<DeferredLocalLight> lights)
    {
        int count = Math.Min(lights.Length, DeferredLocalLight.Maximum);

        for (int i = 0; i < count; i++)
        {
            DeferredLocalLight light = lights[i];
            int at = i * 4;

            _localLightPositionRadius[at + 0] = light.ViewPosition.X;
            _localLightPositionRadius[at + 1] = light.ViewPosition.Y;
            _localLightPositionRadius[at + 2] = light.ViewPosition.Z;
            _localLightPositionRadius[at + 3] = light.Radius;

            _localLightColourIntensity[at + 0] = light.Colour.X;
            _localLightColourIntensity[at + 1] = light.Colour.Y;
            _localLightColourIntensity[at + 2] = light.Colour.Z;
            _localLightColourIntensity[at + 3] = light.Intensity;
        }

        _localLightCount = count;
    }

    /// <summary>Marches the participating medium over the lit scene.</summary>
    /// <param name="target">The scene target holding the lit frame and its depth.</param>
    /// <remarks>
    /// Reads the front colour buffer and writes the back, then swaps. A full-screen pass cannot read
    /// and write one texture at once, which is what the ping-pong pair exists for.
    /// </remarks>
    private void RunVolumetricFog(GLSceneTarget target)
    {

        if (!_fog.Enabled || _fogProgram is null || _fogCompositeProgram is null || _fullScreen is null)
        {
            return;
        }

        if (!target.BindForFogIntegration())
        {
            return;
        }

        using GLStageProfiler.Scope timing = Measure(RenderStage.FogIntegrate);
        UseProgram(_fogProgram);

        _fogProgram.SetVector3(
            _fogSunDirection, _lighting.SunDirection.X, _lighting.SunDirection.Y, _lighting.SunDirection.Z);
        _fogProgram.SetVector3(
            _fogSunColour, _lighting.SunColour.X, _lighting.SunColour.Y, _lighting.SunColour.Z);
        _fogProgram.SetVector3(
            _fogAmbient, _lighting.AmbientColour.X, _lighting.AmbientColour.Y, _lighting.AmbientColour.Z);
        _fogProgram.SetInt(_fogCascadeCount, _cascadeCount);
        _fogProgram.SetVector4(
            _fogCascadeSplits, _cascadeSplits[0], _cascadeSplits[1], _cascadeSplits[2], _cascadeSplits[3]);
        _fogProgram.SetVector4(
            _fogUnproject, _unprojection.XScale, _unprojection.YScale, _unprojection.Near, _unprojection.Far);
        _fogProgram.SetMatrix3(_fogViewToWorld, _viewToWorld);

        for (int i = 0; i < _cascadeCount; i++)
        {
            _fogProgram.SetMatrix4(_fogCascadeMatrix, i, _cascadeMatrices[i]);
        }

        _fogProgram.SetVector4(
            _fogMedium,
            _fog.Density,
            _fog.HeightFalloff > 0f ? 1f / _fog.HeightFalloff : 0f,

            // The base height arrives in world terms and the march works camera-relative, so it is
            // rebased here rather than in the shader, where it would be recomputed per sample.
            _fog.BaseHeight - _cameraPosition.Z,
            Math.Clamp(_fog.Anisotropy, -0.95f, 0.95f));

        _fogProgram.SetVector4(
            _fogMarch, _fog.Distance, _fog.Steps, _fog.SunIntensity, _fog.AmbientIntensity);

        Vector3f albedo = _fog.ScatteringAlbedo;
        _fogProgram.SetVector3(_fogAlbedo, albedo.X, albedo.Y, albedo.Z);

        // Varied per frame so the dither pattern moves; with temporal filtering in stage R5 this is
        // what turns the remaining noise into detail rather than into a static grain.
        _fogProgram.SetFloat(_fogMultiScatter, Math.Clamp(_fog.MultipleScattering, 0f, 1f));
        _fogProgram.SetFloat(_fogUniformFraction, Math.Clamp(_fog.UniformFraction, 0f, 1f));
        _fogProgram.SetInt(_fogVolumeCountLocation, _fogVolumeCount);

        for (int i = 0; i < _fogVolumeCount; i++)
        {
            LocalFogVolume volume = _fogVolumes[i];

            // Rebased against the camera here, because the march works in camera-relative world space
            // and doing it per sample would be the same subtraction a few million more times.
            _fogProgram.SetVector4(
                _fogVolumeCentre + i,
                volume.Centre.X - _cameraPosition.X,
                volume.Centre.Y - _cameraPosition.Y,
                volume.Centre.Z - _cameraPosition.Z,
                volume.Shape == FogVolumeShape.Sphere ? 1f : 0f);

            _fogProgram.SetVector4(
                _fogVolumeExtent + i,
                MathF.Max(volume.Extent.X, 0.01f),
                MathF.Max(volume.Extent.Y, 0.01f),
                MathF.Max(volume.Extent.Z, 0.01f),
                volume.DensityScale);
        }

        _fogFrame = (_fogFrame + 1) & 63;
        _fogProgram.SetFloat(_fogJitter, _fogFrame * 0.61803399f);

        BindTexture(4, target.DepthTexture);

        if (_shadowMap is { IsReady: true } shadows)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + 5);
            _gl.BindTexture(TextureTarget.Texture2DArray, shadows.Texture);
            _activeTextureUnit = 5;
        }

        _fullScreen.Draw();

        timing.Dispose();
        target.BindForPostProcess();
        using GLStageProfiler.Scope compositeTiming = Measure(RenderStage.FogComposite);
        UseProgram(_fogCompositeProgram);
        (float fogTexelX, float fogTexelY) = target.FogTexelSize;
        _fogCompositeProgram.SetVector2(_fogCompositeTexelSize, fogTexelX, fogTexelY);
        BindTexture(0, target.FrontColorTexture);
        BindTexture(4, target.DepthTexture);
        BindTexture(6, target.FogTexture);
        _fullScreen.Draw();
        target.Swap();

        _currentVertexArray = 0;
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;
    }

    /// <inheritdoc/>
    public void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        Span<MeshDrawRange> range = stackalloc MeshDrawRange[1];
        range[0] = new(indexOffset, indexCount);
        DrawMeshRanges(mesh, range, modelView, diffuse, lightmap, blend);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>The interior is carved out with the depth buffer, not a stencil.</b> The scene target has no
    /// stencil attachment — its depth is a plain 32-bit float texture the deferred lighting samples —
    /// so the mask goes where there is room for it: a first pass draws the model unexpanded, colour
    /// writes off, depth test always, pinning its silhouette to the near plane. Every shell afterwards
    /// tests <c>LESS</c> against that, which rejects exactly the pixels the model covers and leaves the
    /// rim. Because the mask ignores the depth test on the way in, it works with the model behind a
    /// wall, which is what makes the x-ray mode possible at all.
    /// </para>
    /// <para>
    /// <b>The glow is layered, not blurred.</b> There is no spare render target to blur a mask in, so
    /// the falloff is built from concentric shells drawn widest first and brightest last. They are
    /// alpha blended rather than added: a model whose limbs overlap has several shells covering the
    /// same pixel, and blending converges on the colour where adding would pile up into a white smear
    /// wherever the mesh happens to be thick.
    /// </para>
    /// <para>
    /// Depth writing is off for every shell. Occluded or not, an outline must never stop the geometry
    /// drawn after it from appearing — it is a mark on the frame, not a part of the scene. The mask
    /// pass is the one exception, and it is the reason this belongs late in the frame: it leaves the
    /// near plane written across the selection's silhouette.
    /// </para>
    /// </remarks>
    public unsafe void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
        ThrowIfDisposed();

        if (_outlineProgram is null)
        {
            return;
        }

        if (!_meshes.TryGetValue(mesh.Id, out GLMesh entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (ranges.IsEmpty || radius <= 0f)
        {
            return;
        }

        foreach (MeshDrawRange range in ranges)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexCount);

            if (range.IndexOffset + range.IndexCount > entry.IndexCount)
            {
                throw new ArgumentException(
                    $"Indices {range.IndexOffset}..{range.IndexOffset + range.IndexCount} lie outside the mesh's {entry.IndexCount}.",
                    nameof(ranges));
            }
        }

        UseProgram(_outlineProgram);

        // The mesh programs cache their uniforms per program; switching away invalidates that cache,
        // so the next mesh draw must re-send its own.
        _lastMeshProgram = null;
        _meshUniformsPrimed = false;
        _sentMaterial = null;

        _outlineProgram.SetMatrix4(_outlineModelView, modelView);
        _outlineProgram.SetMatrix4(_outlineProjection, _projection);
        _outlineProgram.SetVector2(_outlineViewport, _viewportWidth, _viewportHeight);
        _outlineProgram.SetFloat(_outlinePoseBlend, _poseBlend);

        BindVertexArray(entry.ActiveVertexArray);

        // The mask. Both facings, because a model with inconsistent winding must still stop the glow
        // getting inside it, and a hole in the mask reads as a bright patch across the middle.
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Always);
        _gl.DepthMask(true);
        _gl.ColorMask(false, false, false, false);

        _outlineProgram.SetFloat(_outlineWidth, 0f);
        _outlineProgram.SetVector2(_outlinePin, 1f, MaskDepth);
        DrawOutlineRanges(entry, ranges);

        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(false);
        _gl.DepthFunc(DepthFunction.Less);

        // Cull whichever facing the frame is currently KEEPING, so each shell contributes one layer.
        // Asking for a fixed facing would double the coverage wherever the pass in force had already
        // inverted the winding, and the glow would come out twice as strong on those models.
        _gl.Enable(EnableCap.CullFace);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.CullFace(_cullMode == CullMode.Clockwise ? TriangleFace.Back : TriangleFace.Front);

        // Pinned to the near plane the shells clear any wall between them and the camera; left at their
        // own depth the world occludes them, which is the "only when visible" mode.
        _outlineProgram.SetVector2(_outlinePin, throughWalls ? 1f : 0f, GlowDepth);

        ApplyBlend(BlendMode.AlphaBlend);

        // Widest and faintest first, so each shell blends over the ones outside it. What that stack has
        // to add up to is a curve, not a set of even steps: full strength against the model and nothing
        // at all at the far edge. So the loop tracks how opaque the glow already is at each ring and
        // asks the next shell only for the difference -- solving a * (1 - covered) = wanted - covered.
        // Setting every shell to the same fraction instead piles opacity up in the first two rings and
        // reads as a solid band with a hard edge, which is what a stack of rings looks like.
        float covered = 0f;

        for (int shell = GlowShells; shell >= 1; shell--)
        {
            float fraction = shell / (float)GlowShells;
            float wanted = MathF.Pow(1f - fraction + (1f / GlowShells), GlowFalloff);
            float step = Math.Clamp((wanted - covered) / MathF.Max(1e-3f, 1f - covered), 0f, 1f);
            covered = wanted;

            _outlineProgram.SetFloat(_outlineWidth, radius * fraction);
            _outlineProgram.SetVector4(
                _outlineColour,
                colour.R / 255f,
                colour.G / 255f,
                colour.B / 255f,
                step * (colour.A / 255f));

            DrawOutlineRanges(entry, ranges);
        }

        // Put back exactly what the frame was using, so a pass that outlines something mid-stream is
        // indistinguishable from one that did not. Both are restored by replaying the setters' own
        // logic rather than by guessing, because a state left wrong here corrupts the rest of the frame.
        RestoreCullMode();
        RestoreDepthState();
    }

    /// <summary>Issues one outline pass over every range of the mesh.</summary>
    private unsafe void DrawOutlineRanges(GLMesh entry, ReadOnlySpan<MeshDrawRange> ranges)
    {
        _ = entry;

        foreach (MeshDrawRange range in ranges)
        {
            if (range.IndexCount > 0)
            {
                _gl.DrawElements(
                    PrimitiveType.Triangles,
                    (uint)range.IndexCount,
                    DrawElementsType.UnsignedShort,
                    (void*)(nint)(range.IndexOffset * sizeof(ushort)));
            }
        }
    }

    private void RestoreCullMode()
    {
        if (_cullMode == CullMode.None)
        {
            _gl.Disable(EnableCap.CullFace);
            return;
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.CullFace(_cullMode == CullMode.Clockwise ? TriangleFace.Front : TriangleFace.Back);
    }

    private void RestoreDepthState()
    {
        switch (_depthState)
        {
            case DepthState.Disabled:
                _gl.Disable(EnableCap.DepthTest);
                _gl.DepthMask(true);
                break;
            case DepthState.TestOnly:
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Lequal);
                _gl.DepthMask(false);
                break;
            default:
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Lequal);
                _gl.DepthMask(true);
                break;
        }
    }

    /// <inheritdoc/>
    public unsafe void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (!_meshes.TryGetValue(mesh.Id, out GLMesh entry))
        {
            throw new ArgumentException($"{mesh} does not belong to this device.", nameof(mesh));
        }

        if (ranges.IsEmpty)
        {
            return;
        }

        bool anyIndices = false;
        foreach (MeshDrawRange range in ranges)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexOffset);
            ArgumentOutOfRangeException.ThrowIfNegative(range.IndexCount);

            if (range.IndexCount % 3 != 0)
            {
                throw new ArgumentException(
                    $"A draw must cover whole triangles, got {range.IndexCount} indices.",
                    nameof(ranges));
            }

            if (range.IndexOffset + range.IndexCount > entry.IndexCount)
            {
                throw new ArgumentException(
                    $"Indices {range.IndexOffset}..{range.IndexOffset + range.IndexCount} lie outside the mesh's {entry.IndexCount}.",
                    nameof(ranges));
            }

            anyIndices |= range.IndexCount > 0;
        }

        if (!anyIndices)
        {
            return;
        }

        // Which program shades this draw is decided by the pass, not by the caller: a renderer asks
        // for a mesh with a texture and a lightmap and never learns there are two ways to draw it.
        bool card = CurrentPass == ScenePass.Card;
        bool shadow = CurrentPass == ScenePass.Shadow;
        bool deferred = !shadow
            && CurrentPass == ScenePass.Geometry
            && _sceneTarget is { IsReady: true } gbuffer
            && gbuffer.Description.Deferred;

        GLShaderProgram program = card ? _cardProgram!
            : shadow ? _shadowProgram!
            : deferred ? _gbufferProgram!
            : _meshProgram!;

        MeshUniforms uniforms = card ? _cardUniforms
            : shadow ? _shadowUniforms
            : deferred ? _gbufferUniforms
            : _meshUniforms;

        if (!ReferenceEquals(program, _lastMeshProgram))
        {
            // A shadowed uniform belongs to the program that received it, so switching programs
            // invalidates every one of them. Leaving the cache primed here would draw the whole frame
            // with whatever matrices the other program happened to be holding.
            _lastMeshProgram = program;
            _meshUniformsPrimed = false;
            _sentMaterial = null;
            _sentDetailStrength = float.NaN;
        }

        UseProgram(program);

        if (!shadow && _sentMaterial != _material)
        {
            _sentMaterial = _material;

            if (card)
            {
                program.SetVector4(
                    _cardMaterial,
                    _material.Roughness,
                    _material.Metallic,
                    _material.PackedEmissive,
                    0f);
            }
            else if (deferred)
            {
                // The fourth component is the per-draw "no history" flag, carried to the motion
                // target's spare channel so the temporal resolve can refuse to blend where the
                // engine KNOWS the previous frame shows something else -- a fresh spawn, a teleport.
                program.SetVector4(
                    _gbufferMaterial,
                    _material.Roughness,
                    _material.Metallic,
                    _material.PackedEmissive,
                    _material.NoHistory ? 1f : 0f);
            }
            else
            {
                // The fourth component is the switch. It is only ever set for the forward pass of a
                // modern frame, so the original's path uploads a zero here and its picture is
                // untouched to the bit.
                // Emissive forward geometry (rain, particles, muzzle light and the sky's forward path)
                // carries its final colour in the vertex/texture data, like the reference renderer. It
                // must bypass the scene-lighting branch or an upward-facing streak can receive only the
                // ambient term and disappear into the fog.
                bool lit = CurrentPass == ScenePass.Forward
                    && _sceneTarget is { IsReady: true } lightingTarget
                    && lightingTarget.Description.Deferred
                    && _material.Emissive <= 0.5f;

                program.SetVector4(
                    _meshMaterial,
                    _material.Roughness,
                    _material.Metallic,
                    _material.Emissive,
                    lit ? 1f : 0f);
                program.SetVector3(
                    _meshKeyColour, _lighting.SunColour.X, _lighting.SunColour.Y, _lighting.SunColour.Z);
                program.SetVector3(
                    _meshAmbient, _lighting.AmbientColour.X, _lighting.AmbientColour.Y, _lighting.AmbientColour.Z);
            }
        }

        // Every uniform below used to be re-uploaded on every sub-mesh. The projection is fixed for the
        // whole pass, the fade and the alpha test change a handful of times, and consecutive sub-meshes
        // of one object share a transform -- so almost all of that traffic was the driver being told
        // what it already knew. The sampler units are constant and are set once, at link time.
        // The cut-out threshold belongs to opaque draws only. A blended draw is asking for a gradient,
        // and testing it at a half would punch out exactly the half of the gradient it wants to keep.
        float alphaTest = blend == BlendMode.Opaque ? Mesh3DShaders.AlphaTestThreshold : 0f;

        if (!_meshUniformsPrimed)
        {
            _meshUniformsPrimed = true;
            Matrix4f previousPose = _previousPose ?? modelView;
            _sentModelView = modelView;
            _sentPreviousPose = previousPose;
            _sentProjection = _projection;
            _sentDetailFade = _detailFade;
            _sentAlphaTest = alphaTest;
            _sentPoseBlend = _poseBlend;
            _sentPreviousPoseBlend = _previousPoseBlend;
            program.SetMatrix4(uniforms.ModelView, modelView);

            if (uniforms.PreviousModelView >= 0)
            {
                program.SetMatrix4(uniforms.PreviousModelView, previousPose);
            }

            if (uniforms.PoseBlend >= 0)
            {
                program.SetFloat(uniforms.PoseBlend, _poseBlend);
            }

            if (uniforms.PreviousPoseBlend >= 0)
            {
                program.SetFloat(uniforms.PreviousPoseBlend, _previousPoseBlend);
            }
            program.SetMatrix4(uniforms.Projection, _projection);
            program.SetVector4(
                uniforms.DetailFade,
                _detailFade.Scale,
                _detailFade.Offset,
                _detailFade.Slope,
                _detailFade.Enabled);
            program.SetFloat(uniforms.AlphaTest, alphaTest);
        }
        else
        {
            if (_sentModelView != modelView)
            {
                _sentModelView = modelView;
                program.SetMatrix4(uniforms.ModelView, modelView);
            }

            // Only the G-buffer program has this; the forward and shadow programs do not, and asking
            // them to set uniform -1 is a GL error per draw rather than a no-op.
            if (uniforms.PreviousModelView >= 0)
            {
                Matrix4f previousPose = _previousPose ?? modelView;

                if (_sentPreviousPose != previousPose)
                {
                    _sentPreviousPose = previousPose;
                    program.SetMatrix4(uniforms.PreviousModelView, previousPose);
                }
            }

            if (uniforms.PoseBlend >= 0 && _sentPoseBlend != _poseBlend)
            {
                _sentPoseBlend = _poseBlend;
                program.SetFloat(uniforms.PoseBlend, _poseBlend);
            }

            if (uniforms.PreviousPoseBlend >= 0 && _sentPreviousPoseBlend != _previousPoseBlend)
            {
                _sentPreviousPoseBlend = _previousPoseBlend;
                program.SetFloat(uniforms.PreviousPoseBlend, _previousPoseBlend);
            }

            if (_sentProjection != _projection)
            {
                _sentProjection = _projection;
                program.SetMatrix4(uniforms.Projection, _projection);
            }

            if (_sentDetailFade != _detailFade)
            {
                _sentDetailFade = _detailFade;
                program.SetVector4(
                    uniforms.DetailFade,
                    _detailFade.Scale,
                    _detailFade.Offset,
                    _detailFade.Slope,
                    _detailFade.Enabled);
            }

            if (_sentAlphaTest != alphaTest)
            {
                _sentAlphaTest = alphaTest;
                program.SetFloat(uniforms.AlphaTest, alphaTest);
            }
        }

        // An untextured draw is legitimate and common -- it takes the white texture, as NameOf does --
        // so the entry is only resolved when there is one to resolve. Resolving unconditionally is what
        // the first version did, and it threw on the first untextured sub-mesh in the level.
        GLTexture surface = diffuse.IsValid
            ? Resolve(diffuse, nameof(diffuse))
            : new GLTexture(_whiteTexture, 1, 1, 1);

        BindTexture(0, surface.Name);

        // The shadow program's fragment stage is nothing but the diffuse cut-out test — it never
        // samples the lightmap, and the cascades redraw the whole world several times a frame, so
        // binding one per draw there was thousands of driver calls for a sampler nothing reads.
        if (!shadow)
        {
            BindTexture(1, NameOf(lightmap, nameof(lightmap)));
        }

        // The derived surface detail: the normal and roughness this game's textures never carried. A
        // texture with no map registered, or a preset that asked for none, sends a strength of zero
        // rather than a second branch -- the shader's multiply by it is cheaper than the divergence,
        // and the alternative is two G-buffer programs differing by four lines.
        if (deferred && _gbufferDetail >= 0)
        {
            float strength = surface.Detail != 0 ? _materialDetail : 0f;

            if (_sentDetailStrength != strength)
            {
                _sentDetailStrength = strength;
                program.SetFloat(_gbufferDetail, strength);
            }

            if (strength > 0f)
            {
                BindTexture(2, surface.Detail);
            }
        }

        ApplyBlend(blend);

        BindVertexArray(entry.ActiveVertexArray);
        if (ranges.Length == 1)
        {
            MeshDrawRange range = ranges[0];
            _gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)range.IndexCount,
                DrawElementsType.UnsignedShort,
                (void*)(range.IndexOffset * sizeof(ushort)));
        }
        else
        {
            uint* counts = stackalloc uint[ranges.Length];
            void** offsets = stackalloc void*[ranges.Length];
            for (int i = 0; i < ranges.Length; i++)
            {
                counts[i] = (uint)ranges[i].IndexCount;
                offsets[i] = (void*)(ranges[i].IndexOffset * sizeof(ushort));
            }

            _gl.MultiDrawElements(
                PrimitiveType.Triangles,
                counts,
                DrawElementsType.UnsignedShort,
                offsets,
                (uint)ranges.Length);
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

        ReleaseProbeTextures();

        if (_lutTexture != 0)
        {
            _gl.DeleteTexture(_lutTexture);
            _lutTexture = 0;
        }

        // The auto-exposure compute programs and their buffer are tracked like any other compute
        // resource, so DisposeComputeResources deletes them; destroying them explicitly here would trip
        // the disposed guard, since Dispose has already marked the device.
        DisposeComputeResources();
        DisposeTextResources();
        _cardAtlas?.Dispose();
        _cardProgram?.Dispose();
        _stageProfiler?.Dispose();
        foreach (VisibilityQuery query in _visibilityQueries.Values)
        {
            _gl.DeleteQuery(query.Name);
        }
        _visibilityQueries.Clear();
        _visibilityProgram?.Dispose();
        if (_visibilityArray != 0)
        {
            _gl.DeleteVertexArray(_visibilityArray);
        }
        if (_visibilityVertexBuffer != 0)
        {
            _gl.DeleteBuffer(_visibilityVertexBuffer);
        }
        if (_visibilityIndexBuffer != 0)
        {
            _gl.DeleteBuffer(_visibilityIndexBuffer);
        }
        _sceneTarget?.Dispose();
        _fogProgram?.Dispose();
        _fogCompositeProgram?.Dispose();
        _occlusionProgram?.Dispose();
        _occlusionReconstructProgram?.Dispose();
        _shadowMap?.Dispose();
        _shadowProgram?.Dispose();
        foreach (GBufferPipeline pipeline in _gbufferPipelines.Values)
        {
            pipeline.Program.Dispose();
        }
        _gbufferPipelines.Clear();
        _gbufferProgram = null;
        _lightingProgram?.Dispose();
        _fullScreen?.Dispose();
        _resolveProgram?.Dispose();

        foreach (GLMesh mesh in _meshes.Values)
        {
            _gl.DeleteVertexArray(mesh.VertexArray);
            _gl.DeleteBuffer(mesh.VertexBuffer);
            _gl.DeleteBuffer(mesh.IndexBuffer);

            if (mesh.IsAnimated)
            {
                _gl.DeleteVertexArray(mesh.SecondVertexArray);
                _gl.DeleteBuffer(mesh.SecondVertexBuffer);
            }
        }

        _meshes.Clear();
        _meshProgram?.Dispose();

        foreach (GLTexture entry in _textures.Values)
        {
            _gl.DeleteTexture(entry.Name);
        }

        _textures.Clear();

        if (_whiteTexture != 0)
        {
            _gl.DeleteTexture(_whiteTexture);
        }

        if (_blitTexture != 0)
        {
            _gl.DeleteTexture(_blitTexture);
        }

        if (_vertexBuffer != 0)
        {
            _gl.DeleteBuffer(_vertexBuffer);
        }

        if (_indexBuffer != 0)
        {
            _gl.DeleteBuffer(_indexBuffer);
        }

        if (_vertexArray != 0)
        {
            _gl.DeleteVertexArray(_vertexArray);
        }

        _program?.Dispose();
    }

    /// <summary>Selects the exact G-buffer program matching the attachments allocated by a preset.</summary>
    /// <remarks>
    /// Variants are linked on first use and cached thereafter. This keeps a missing attachment out of
    /// the shader interface itself, so the driver can eliminate its varyings, texture reads and export
    /// work instead of relying on a per-fragment uniform branch.
    /// </remarks>
    private void SelectGBufferPipeline(GBufferLayout layout)
    {
        if (!_gbufferPipelines.TryGetValue(layout, out GBufferPipeline? pipeline))
        {
            GLShaderProgram program = GLShaderProgram.Create(
                _gl,
                GBufferShaders.VertexFor(layout),
                GBufferShaders.FragmentFor(layout),
                _programCache);
            MeshUniforms uniforms = new(
                program.LocationOf("uModelView"),
                program.LocationOf("uProjection"),
                program.LocationOf("uDetailFade"),
                program.LocationOf("uAlphaTest"),
                program.LocationOf("uPreviousModelView"),
                program.LocationOf("uPoseBlend"),
                program.LocationOf("uPreviousPoseBlend"));
            pipeline = new(
                program,
                uniforms,
                program.LocationOf("uMaterial"),
                program.LocationOf("uDecodeAlbedo"),
                program.LocationOf("uDetailStrength"));
            _gbufferPipelines.Add(layout, pipeline);

            UseProgram(program);
            program.SetInt("uDiffuse", 0);
            program.SetInt("uLightmap", 1);
            program.SetInt("uDetail", 2);
        }

        _gbufferProgram = pipeline.Program;
        _gbufferUniforms = pipeline.Uniforms;
        _gbufferMaterial = pipeline.Material;
        _gbufferDecodeAlbedo = pipeline.DecodeAlbedo;
        _gbufferDetail = pipeline.Detail;
    }

    private unsafe void CreatePipeline()
    {
        _program = GLShaderProgram.Create(_gl, Sprite2DShaders.Vertex, Sprite2DShaders.Fragment, _programCache);
        _meshProgram = GLShaderProgram.Create(_gl, Mesh3DShaders.Vertex, Mesh3DShaders.Fragment, _programCache);

        // Resolved once, here, so no draw ever looks a uniform up by name again.
        _spriteInverseScreen = _program.LocationOf("uInverseScreen");
        _meshUniforms = new MeshUniforms(
            _meshProgram.LocationOf("uModelView"),
            _meshProgram.LocationOf("uProjection"),
            _meshProgram.LocationOf("uDetailFade"),
            _meshProgram.LocationOf("uAlphaTest"),
            PoseBlend: _meshProgram.LocationOf("uPoseBlend"));
        _outlineProgram = GLShaderProgram.Create(_gl, OutlineShaders.Vertex, OutlineShaders.Fragment, _programCache);
        _outlineModelView = _outlineProgram.LocationOf("uModelView");
        _outlineProjection = _outlineProgram.LocationOf("uProjection");
        _outlineWidth = _outlineProgram.LocationOf("uWidth");
        _outlineColour = _outlineProgram.LocationOf("uColour");
        _outlineViewport = _outlineProgram.LocationOf("uViewport");
        _outlinePoseBlend = _outlineProgram.LocationOf("uPoseBlend");
        _outlinePin = _outlineProgram.LocationOf("uPin");

        _meshMaterial = _meshProgram.LocationOf("uMaterial");
        _meshKeyColour = _meshProgram.LocationOf("uKeyColour");
        _meshAmbient = _meshProgram.LocationOf("uAmbientColour");

        // Which unit each sampler reads is fixed by the pipeline and never varies by draw, so it is set
        // now rather than four times per sub-mesh. A uniform belongs to its program, so both survive the
        // program switches between the 2D and 3D passes.
        UseProgram(_program);
        _program.SetInt("uTexture", 0);
        UseProgram(_meshProgram);
        _meshProgram.SetInt("uDiffuse", 0);
        _meshProgram.SetInt("uLightmap", 1);

        _resolveProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, PostProcessShaders.Fragment, _programCache);
        _resolveExposure = _resolveProgram.LocationOf("uExposure");
        _resolveToneMap = _resolveProgram.LocationOf("uToneMap");
        _resolveDecodeInput = _resolveProgram.LocationOf("uDecodeInput");
        _resolveWhitePoint = _resolveProgram.LocationOf("uWhitePoint");
        _resolveDebugView = _resolveProgram.LocationOf("uDebugView");
        _resolveBloomIntensity = _resolveProgram.LocationOf("uBloomIntensity");
        _resolveGrade = _resolveProgram.LocationOf("uGrade");
        _resolveLens = _resolveProgram.LocationOf("uLens");
        _resolveTexelSize = _resolveProgram.LocationOf("uTexelSize");
        _resolveLut = _resolveProgram.LocationOf("uLut");
        _resolveGreenPhosphor = _resolveProgram.LocationOf("uGreenPhosphor");
        _resolveTactical = _resolveProgram.LocationOf("uTactical");
        _resolveTacticalTime = _resolveProgram.LocationOf("uTacticalTime");
        _resolveBinocular = _resolveProgram.LocationOf("uBinocular");
        _resolveBinocularTime = _resolveProgram.LocationOf("uBinocularTime");
        UseProgram(_resolveProgram);
        _resolveProgram.SetInt("uScene", 0);
        _resolveProgram.SetInt("uDebugAlbedo", 6);
        _resolveProgram.SetInt("uDebugNormal", 7);
        _resolveProgram.SetInt("uDebugBaked", 1);
        _resolveProgram.SetInt("uDebugMaterial", 2);
        _resolveProgram.SetInt("uDebugMotion", 5);
        _resolveProgram.SetInt("uDebugOcclusion", 3);
        _resolveProgram.SetInt("uBloom", 7);

        // Unit 8, past every unit the debug views claim. The grade and a debug view are never both
        // wanted, but the grade and the BLOOM are -- and bloom is on 7 because the debug set had taken
        // everything below it.
        _resolveProgram.SetInt("uColourLut", 8);

        // Auto-exposure's two compute passes and the tiny buffer they share. Compute only, and never
        // fatal: a context without it, or a shader that will not build, simply leaves the feature off and
        // the exposure the cvar's own value. The buffer is three uints -- log-luminance sum, count, and
        // the running exposure as float bits -- zeroed so the first frame starts adapted.
        if (Capabilities.SupportsCompute)
        {
            try
            {
                _autoExposureReduce = CreateComputeShader(AutoExposureShaders.Reduce, "autoexposure.reduce");
                _autoExposureAdapt = CreateComputeShader(AutoExposureShaders.Adapt, "autoexposure.adapt");
                _autoExposureAccum = CreateStorageBuffer(3 * sizeof(uint), "autoexposure.accum");
                UpdateStorageBuffer<uint>(_autoExposureAccum, new uint[3]);
            }
            catch (GraphicsDeviceException)
            {
                _autoExposureReduce = default;
                _autoExposureAdapt = default;
            }
        }

        _bloomThresholdProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, BloomShaders.Threshold, _programCache);
        _bloomThreshold = _bloomThresholdProgram.LocationOf("uThreshold");
        UseProgram(_bloomThresholdProgram);
        _bloomThresholdProgram.SetInt("uScene", 0);

        _cameraProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, CameraEffectShaders.Fragment, _programCache);
        _cameraUnproject = _cameraProgram.LocationOf("uUnproject");
        _cameraFocus = _cameraProgram.LocationOf("uFocus");
        _cameraMotionBlur = _cameraProgram.LocationOf("uMotionBlur");
        _cameraPrevViewProjection = _cameraProgram.LocationOf("uPrevViewProjection");
        _cameraDelta = _cameraProgram.LocationOf("uCameraDelta");
        _cameraViewToWorld = _cameraProgram.LocationOf("uViewToWorld");
        _cameraTexelSize = _cameraProgram.LocationOf("uTexelSize");
        UseProgram(_cameraProgram);
        _cameraProgram.SetInt("uScene", 0);
        _cameraProgram.SetInt("uDepth", 4);
        _cameraProgram.SetInt("uMotion", 5);

        _spatialAaProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, SpatialAaShaders.Fragment, _programCache);
        _spatialAaTexelSize = _spatialAaProgram.LocationOf("uTexelSize");
        _spatialAaEdge = _spatialAaProgram.LocationOf("uEdge");
        UseProgram(_spatialAaProgram);
        _spatialAaProgram.SetInt("uScene", 0);

        _bloomBlurProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, BloomShaders.Blur, _programCache);
        _bloomDirection = _bloomBlurProgram.LocationOf("uDirection");
        UseProgram(_bloomBlurProgram);
        _bloomBlurProgram.SetInt("uSource", 0);
        _resolveProgram.SetInt("uDebugDepth", 4);

        _cardProgram = GLShaderProgram.Create(_gl, CardAtlasShaders.Vertex, CardAtlasShaders.Fragment, _programCache);
        _cardUniforms = new MeshUniforms(
            _cardProgram.LocationOf("uModelView"),
            _cardProgram.LocationOf("uProjection"),
            -1,
            _cardProgram.LocationOf("uAlphaTest"));
        _cardMaterial = _cardProgram.LocationOf("uMaterial");
        _cardDecodeAlbedo = _cardProgram.LocationOf("uDecodeAlbedo");
        UseProgram(_cardProgram);
        _cardProgram.SetInt("uDiffuse", 0);


        _occlusionProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, AmbientOcclusionShaders.Fragment, _programCache);
        _occlusionUnproject = _occlusionProgram.LocationOf("uUnproject");
        _occlusionSearch = _occlusionProgram.LocationOf("uSearch");
        _occlusionBounce = _occlusionProgram.LocationOf("uBounce");
        _occlusionTexelSize = _occlusionProgram.LocationOf("uTexelSize");
        UseProgram(_occlusionProgram);
        _occlusionProgram.SetInt("uDepth", 4);
        _occlusionProgram.SetInt("uNormal", 1);
        _occlusionProgram.SetInt("uPreviousColour", 2);
        _occlusionProgram.SetInt("uMaterial", 3);

        _occlusionReconstructProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, AmbientOcclusionShaders.ReconstructFragment, _programCache);
        _occlusionReconstructTexelSize = _occlusionReconstructProgram.LocationOf("uLowTexelSize");
        UseProgram(_occlusionReconstructProgram);
        _occlusionReconstructProgram.SetInt("uOcclusion", 6);
        _occlusionReconstructProgram.SetInt("uDepth", 4);
        _occlusionReconstructProgram.SetInt("uNormal", 1);

        _lightingProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, DeferredLightingShaders.Fragment, _programCache);
        _lightingModelLocation = _lightingProgram.LocationOf("uLightingModel");
        _lightingSunDirection = _lightingProgram.LocationOf("uSunDirection");
        _lightingSunColour = _lightingProgram.LocationOf("uSunColour");
        _lightingAmbient = _lightingProgram.LocationOf("uAmbientColour");
        UseProgram(_lightingProgram);
        _lightingProgram.SetInt("uAlbedo", 0);
        _lightingProgram.SetInt("uNormal", 1);
        _lightingProgram.SetInt("uBakedLight", 2);
        _lightingProgram.SetInt("uMaterial", 3);
        _lightingProgram.SetInt("uOcclusion", 6);
        _lightingProgram.SetInt("uProbeRed", 8);
        _lightingProgram.SetInt("uProbeGreen", 9);
        _lightingProgram.SetInt("uProbeBlue", 10);
        _lightingProgram.SetInt("uProbeDistance", 11);
        _lightingProgram.SetInt("uGI", 7);

        _lightingOcclusionEnabled = _lightingProgram.LocationOf("uOcclusionEnabled");
        _lightingProbesEnabled = _lightingProgram.LocationOf("uProbesEnabled");
        _lightingProbeOrigin = _lightingProgram.LocationOf("uProbeOrigin");
        _lightingProbeInverseCount = _lightingProgram.LocationOf("uProbeInverseCount");
        _lightingProbeScale = _lightingProgram.LocationOf("uProbeScale");
        _lightingProbeSky = _lightingProgram.LocationOf("uProbeSky");
        _lightingProbeBlend = _lightingProgram.LocationOf("uProbeBlend");
        _lightingProbeViewToWorld = _lightingProgram.LocationOf("uProbeViewToWorld");
        _lightingProbeCamera = _lightingProgram.LocationOf("uProbeCamera");
        _lightingShowProbes = _lightingProgram.LocationOf("uShowProbes");
        _lightingScale = _lightingProgram.LocationOf("uLightScale");
        _lightingGiStrength = _lightingProgram.LocationOf("uGiStrength");
        _lightingLocalLightCount = _lightingProgram.LocationOf("uLocalLightCount");
        _lightingLocalLightPositionRadius = _lightingProgram.LocationOf("uLocalLightPositionRadius");
        _lightingLocalLightColourIntensity = _lightingProgram.LocationOf("uLocalLightColourIntensity");
        _lightingCascadeCount = _lightingProgram.LocationOf("uCascadeCount");
        _lightingCascadeSplits = _lightingProgram.LocationOf("uCascadeSplits");
        _lightingCascadeMatrix = _lightingProgram.LocationOf("uCascadeMatrix");
        _lightingUnproject = _lightingProgram.LocationOf("uUnproject");
        _lightingViewToWorld = _lightingProgram.LocationOf("uViewToWorld");
        _lightingShadowBias = _lightingProgram.LocationOf("uShadowBias");
        _lightingShadowTexel = _lightingProgram.LocationOf("uShadowTexel");
        _lightingShadowDetail = _lightingProgram.LocationOf("uShadowDetail");
        _lightingProgram.SetInt("uDepth", 4);
        _lightingProgram.SetInt("uShadowMap", 5);

        _shadowProgram = GLShaderProgram.Create(_gl, ShadowDepthShaders.Vertex, ShadowDepthShaders.Fragment, _programCache);
        _shadowUniforms = new MeshUniforms(
            _shadowProgram.LocationOf("uModelView"),
            _shadowProgram.LocationOf("uProjection"),
            _shadowProgram.LocationOf("uDetailFade"),
            _shadowProgram.LocationOf("uAlphaTest"),
            PoseBlend: _shadowProgram.LocationOf("uPoseBlend"));
        UseProgram(_shadowProgram);
        _shadowProgram.SetInt("uDiffuse", 0);

        _temporalProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, TemporalAaShaders.Fragment, _programCache);
        _temporalUnproject = _temporalProgram.LocationOf("uUnproject");
        _temporalViewToWorld = _temporalProgram.LocationOf("uViewToWorld");
        _temporalPrevViewProjection = _temporalProgram.LocationOf("uPrevViewProjection");
        _temporalCameraDelta = _temporalProgram.LocationOf("uCameraDelta");
        _temporalRenderSize = _temporalProgram.LocationOf("uRenderSize");
        _temporalJitter = _temporalProgram.LocationOf("uJitter");
        _temporalUpsampling = _temporalProgram.LocationOf("uUpsampling");
        _temporalFeedback = _temporalProgram.LocationOf("uFeedback");
        _temporalSharpening = _temporalProgram.LocationOf("uSharpening");
        UseProgram(_temporalProgram);
        _temporalProgram.SetInt("uCurrent", 0);
        _temporalProgram.SetInt("uHistory", 1);
        _temporalProgram.SetInt("uDepth", 4);
        _temporalProgram.SetInt("uMotion", 5);

        _reflectionProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, ReflectionShaders.Fragment, _programCache);
        _reflectionUnproject = _reflectionProgram.LocationOf("uUnproject");
        _reflectionMarch = _reflectionProgram.LocationOf("uMarch");
        _reflectionTexelSize = _reflectionProgram.LocationOf("uTexelSize");
        _reflectionJitter = _reflectionProgram.LocationOf("uFrameJitter");
        _reflectionProbesEnabled = _reflectionProgram.LocationOf("uProbesEnabled");
        _reflectionProbeOrigin = _reflectionProgram.LocationOf("uProbeOrigin");
        _reflectionProbeInverseCount = _reflectionProgram.LocationOf("uProbeInverseCount");
        _reflectionProbeSpacing = _reflectionProgram.LocationOf("uProbeSpacing");
        _reflectionProbeSky = _reflectionProgram.LocationOf("uProbeSky");
        _reflectionViewToWorld = _reflectionProgram.LocationOf("uViewToWorld");
        _reflectionCamera = _reflectionProgram.LocationOf("uCameraPosition");
        _reflectionShow = _reflectionProgram.LocationOf("uShowReflections");
        UseProgram(_reflectionProgram);
        _reflectionProgram.SetInt("uScene", 0);
        _reflectionProgram.SetInt("uNormal", 1);
        _reflectionProgram.SetInt("uAlbedo", 2);
        _reflectionProgram.SetInt("uMaterial", 3);
        _reflectionProgram.SetInt("uDepth", 4);
        _reflectionProgram.SetInt("uProbeRed", 8);
        _reflectionProgram.SetInt("uProbeGreen", 9);
        _reflectionProgram.SetInt("uProbeBlue", 10);

        _fogProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, VolumetricFogShaders.Fragment, _programCache);
        _fogSunDirection = _fogProgram.LocationOf("uSunDirection");
        _fogSunColour = _fogProgram.LocationOf("uSunColour");
        _fogAmbient = _fogProgram.LocationOf("uAmbientColour");
        _fogCascadeCount = _fogProgram.LocationOf("uCascadeCount");
        _fogCascadeSplits = _fogProgram.LocationOf("uCascadeSplits");
        _fogCascadeMatrix = _fogProgram.LocationOf("uCascadeMatrix");
        _fogUnproject = _fogProgram.LocationOf("uUnproject");
        _fogViewToWorld = _fogProgram.LocationOf("uViewToWorld");
        _fogMedium = _fogProgram.LocationOf("uMedium");
        _fogMarch = _fogProgram.LocationOf("uMarch");
        _fogAlbedo = _fogProgram.LocationOf("uAlbedo");
        _fogJitter = _fogProgram.LocationOf("uFrameJitter");
        _fogMultiScatter = _fogProgram.LocationOf("uMultiScatter");
        _fogUniformFraction = _fogProgram.LocationOf("uUniformFraction");
        _fogVolumeCountLocation = _fogProgram.LocationOf("uVolumeCount");
        _fogVolumeCentre = _fogProgram.LocationOf("uVolumeCentre");
        _fogVolumeExtent = _fogProgram.LocationOf("uVolumeExtent");
        UseProgram(_fogProgram);
        _fogProgram.SetInt("uScene", 0);
        _fogProgram.SetInt("uDepth", 4);
        _fogProgram.SetInt("uShadowMap", 5);

        _fogCompositeProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, VolumetricFogShaders.CompositeFragment, _programCache);
        _fogCompositeTexelSize = _fogCompositeProgram.LocationOf("uLowTexelSize");
        UseProgram(_fogCompositeProgram);
        _fogCompositeProgram.SetInt("uScene", 0);
        _fogCompositeProgram.SetInt("uDepth", 4);
        _fogCompositeProgram.SetInt("uFog", 6);

        _shadowMap = new GLShadowMap(_gl, _logger);
        _fullScreen = new GLFullScreenPass(_gl);
        _sceneTarget = new GLSceneTarget(_gl, _logger);
        _stageProfiler = new GLStageProfiler(_gl);

        _visibilityProgram = GLShaderProgram.Create(
            _gl, OcclusionCullingShaders.Vertex, OcclusionCullingShaders.Fragment, _programCache);
        _visibilityModelView = _visibilityProgram.LocationOf("uModelView");
        _visibilityProjection = _visibilityProgram.LocationOf("uProjection");
        _visibilityMinimum = _visibilityProgram.LocationOf("uMinimum");
        _visibilityMaximum = _visibilityProgram.LocationOf("uMaximum");

        ReadOnlySpan<Vector3f> visibilityCorners =
        [
            new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f),
            new(0f, 0f, 1f), new(1f, 0f, 1f), new(1f, 1f, 1f), new(0f, 1f, 1f),
        ];
        ReadOnlySpan<ushort> visibilityIndices =
        [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        ];
        _visibilityArray = _gl.GenVertexArray();
        _visibilityVertexBuffer = _gl.GenBuffer();
        _visibilityIndexBuffer = _gl.GenBuffer();
        BindVertexArray(_visibilityArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _visibilityVertexBuffer);
        _gl.BufferData(
            BufferTargetARB.ArrayBuffer,
            (nuint)(visibilityCorners.Length * sizeof(float) * 3),
            visibilityCorners,
            BufferUsageARB.StaticDraw);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _visibilityIndexBuffer);
        _gl.BufferData(
            BufferTargetARB.ElementArrayBuffer,
            (nuint)(visibilityIndices.Length * sizeof(ushort)),
            visibilityIndices,
            BufferUsageARB.StaticDraw);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, null);
        BindVertexArray(0);

        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        _indexBuffer = _gl.GenBuffer();

        BindVertexArray(_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Vertex2D.Size, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Vertex2D.Size, (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, Vertex2D.Size, (void*)16);

        BindVertexArray(0);

        Span<Bgra32> white = [Bgra32.White];
        _whiteTexture = _gl.GenTexture();
        BindTextureForUpload(_whiteTexture);
        UploadLevel(0, 1, 1, white);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _depthState = DepthState.Disabled;
        _cullMode = CullMode.None;
        ApplyBlend(BlendMode.AlphaBlend);

        _logger.Information(
            LogCategory,
            $"Device ready: {Capabilities.Renderer}, {Capabilities.TextureUnits} texture units, max texture {Capabilities.MaxTextureSize}.");
    }

    /// <summary>Uploads one mip level as RGBA with a red/blue swizzle applied at sample time.</summary>
    private void UploadLevel(int level, int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        _gl.TexImage2D<Bgra32>(
            TextureTarget.Texture2D,
            level,
            InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            pixels);
    }

    private void ApplySampling(in TextureDescription description)
    {
        // The engine's pixels are blue-first; sampling reverses R and B instead of a CPU conversion.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleR, (int)GLEnum.Blue);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureSwizzleB, (int)GLEnum.Red);

        // The original's mip chain stops when the smaller axis reaches one, so the maximum level must
        // be pinned; leaving it at the default marks a non-square texture as incomplete and it samples
        // black.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, description.MipLevels - 1);

        bool linear = description.Filter == TextureFilter.Linear;
        int magFilter = (int)(linear ? GLEnum.Linear : GLEnum.Nearest);
        int minFilter = description.MipLevels > 1
            ? (int)(linear ? GLEnum.LinearMipmapLinear : GLEnum.NearestMipmapNearest)
            : magFilter;

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minFilter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magFilter);

        if (_anisotropy > 1)
        {
            _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)TextureMaxAnisotropy, (float)_anisotropy);
        }

        int wrap = (int)(description.AddressMode == TextureAddressMode.Repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
    }

    private void EnsureBlitTexture(int width, int height, ReadOnlySpan<Bgra32> pixels)
    {
        if (_blitTexture == 0)
        {
            _blitTexture = _gl.GenTexture();
            _blitWidth = 0;
            _blitHeight = 0;
        }

        BindTextureForUpload(_blitTexture);
        UploadLevel(0, width, height, pixels[..(width * height)]);

        if (_blitWidth != width || _blitHeight != height)
        {
            ApplySampling(new TextureDescription(width, height, 1, TextureFilter.Point, TextureAddressMode.Clamp));
            _blitWidth = width;
            _blitHeight = height;
        }
    }

    private void ApplyBlend(BlendMode blend)
    {
        if (_blend == blend)
        {
            return;
        }

        _blend = blend;
        switch (blend)
        {
            case BlendMode.Opaque:
                _gl.Disable(EnableCap.Blend);
                break;
            case BlendMode.AlphaBlend:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
            case BlendMode.Additive:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(blend), blend, "Unknown blend mode.");
        }
    }

    /// <summary>
    /// Replaces a stream buffer's contents in one call, which also orphans the previous storage so the
    /// driver never has to stall on a frame still in flight.
    /// </summary>
    private void UploadStream<T>(BufferTargetARB target, uint buffer, ReadOnlySpan<T> data, int elementSize)
        where T : unmanaged
    {
        _gl.BindBuffer(target, buffer);
        _gl.BufferData(target, (nuint)(data.Length * elementSize), data, BufferUsageARB.StreamDraw);
    }

    private GraphicsCapabilities ReadCapabilities()
    {
        _gl.GetInteger(GetPName.MaxTextureImageUnits, out int units);
        _gl.GetInteger(GetPName.MaxTextureSize, out int maxSize);

        string vendor = _gl.GetStringS(StringName.Vendor) ?? "unknown";
        string renderer = _gl.GetStringS(StringName.Renderer) ?? "unknown";
        string version = _gl.GetStringS(StringName.Version) ?? "unknown";

        int samples = 1;
        if (Features.AtLeast(3, 3))
        {
            _gl.GetInteger((GetPName)GLEnum.MaxSamples, out samples);
        }

        _gl.GetInteger((GetPName)MaxTextureMaxAnisotropy, out int anisotropyLimit);
        _anisotropyLimit = Math.Clamp(anisotropyLimit, 1, 16);

        return new GraphicsCapabilities(
            Math.Clamp(units, 1, MaxTextureUnits),
            Math.Max(maxSize, 64),
            SupportsNonPowerOfTwoTextures: true,
            $"OpenGL {version} — {renderer} ({vendor})")
        {
            // Every context this backend will accept can render offscreen; 3.0 is where framebuffer
            // objects became core, and the floor here is 3.3.
            SupportsSceneCapture = true,
            SupportsFloatRenderTargets = true,
            SupportsCompute = Features.SupportsCompute,
            MaxSampleCount = Math.Clamp(samples, 1, 16),
        };
    }

    private GLTexture Resolve(TextureHandle handle, string parameterName)
    {
        if (!_textures.TryGetValue(handle.Id, out GLTexture entry))
        {
            throw new ArgumentException($"{handle} does not belong to this device.", parameterName);
        }

        return entry;
    }

    /// <summary>Finds the largest value in an index buffer.</summary>
    /// <remarks>
    /// A running maximum rather than a bounds test per index, so the whole check is a handful of SIMD
    /// maxima instead of a compare-and-branch for every index of every sub-mesh in a level. The reported
    /// index is the largest rather than the first offending one, which localises the fault just as well.
    /// </remarks>
    private static int HighestIndex(ReadOnlySpan<ushort> indices)
    {
        ushort highest = 0;
        int i = 0;

        if (System.Numerics.Vector.IsHardwareAccelerated && indices.Length >= System.Numerics.Vector<ushort>.Count)
        {
            System.Numerics.Vector<ushort> lanes = System.Numerics.Vector<ushort>.Zero;
            for (; i <= indices.Length - System.Numerics.Vector<ushort>.Count; i += System.Numerics.Vector<ushort>.Count)
            {
                lanes = System.Numerics.Vector.Max(lanes, new System.Numerics.Vector<ushort>(indices[i..]));
            }

            for (int lane = 0; lane < System.Numerics.Vector<ushort>.Count; lane++)
            {
                highest = Math.Max(highest, lanes[lane]);
            }
        }

        for (; i < indices.Length; i++)
        {
            highest = Math.Max(highest, indices[i]);
        }

        return highest;
    }

    /// <summary>Makes a program current, skipping the call when it already is.</summary>
    private void UseProgram(GLShaderProgram program)
    {
        if (_currentProgram == program.Handle)
        {
            return;
        }

        program.Use();
        _currentProgram = program.Handle;

        // A graphics program and a compute program share GL's single current-program binding, but each
        // path caches it separately. Binding a graphics program here changes what GL has bound out from
        // under the compute cache, so the next UseCompute must not believe its program is still current --
        // otherwise it skips its own bind and the dispatch runs with a graphics program (or none) active,
        // which the driver rejects as "no active compute shader". This is the mirror of UseCompute
        // clearing _currentProgram, and without it a compute pass that follows any draw in the same frame
        // silently does nothing -- which is exactly how the screen gather produced no atlas whenever a
        // draw had run since the previous dispatch.
        _boundComputeProgram = 0;
    }

    /// <summary>Binds a vertex array, skipping the call when it is already bound.</summary>
    /// <remarks>
    /// Nothing here unbinds to zero when it is done. The old code did, which meant every draw paid for
    /// two binds instead of one and the next draw could never hit the shadow; GL has no state that a
    /// left-bound array corrupts, because everything that configures one binds it first.
    /// </remarks>
    private void BindVertexArray(uint array)
    {
        if (_currentVertexArray == array)
        {
            return;
        }

        _gl.BindVertexArray(array);
        _currentVertexArray = array;
    }

    /// <summary>Binds a volume texture to a unit.</summary>
    /// <remarks>
    /// Separate from the two-dimensional path because the unit cache records a name without recording
    /// which target it was bound to, and a volume left in a unit that a later pass binds a flat texture
    /// into is a sampler reading nothing.
    /// </remarks>
    private void BindProbeTexture(int unit, uint name)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture3D, name);
        _activeTextureUnit = unit;
        _textureNames[unit] = 0;
    }

    /// <summary>Binds a texture name to a unit, skipping the call when it is already there.</summary>
    private void BindTexture(int unit, uint name)
    {
        if (_textureNames[unit] == name)
        {
            return;
        }

        if (_activeTextureUnit != unit)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _activeTextureUnit = unit;
        }

        _gl.BindTexture(TextureTarget.Texture2D, name);
        _textureNames[unit] = name;
    }

    /// <summary>Binds a texture so it can be uploaded to or have its sampling set.</summary>
    /// <remarks>
    /// Unit zero, and left bound afterwards: an upload has no unit of its own, and unbinding to zero on
    /// the way out only guarantees that the next draw has to bind again. The shadow records it either
    /// way, so nothing downstream can be misled.
    /// <para>
    /// Unlike <see cref="BindTexture"/> this always leaves unit zero <em>active</em>, even when the
    /// binding itself can be skipped. <c>glTexImage2D</c> and <c>glTexParameter</c> act on whichever unit
    /// is active, so a skipped switch would send the upload to a texture bound on some other unit.
    /// </para>
    /// </remarks>
    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private void BindTextureForUpload(uint name)
    {
        if (_activeTextureUnit != 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _activeTextureUnit = 0;
        }

        if (_textureNames[0] != name)
        {
            _gl.BindTexture(TextureTarget.Texture2D, name);
            _textureNames[0] = name;
        }
    }

    /// <summary>Resolves a handle to the GL name a draw should bind for it.</summary>
    /// <remarks>An absent texture binds white, so a modulate becomes the identity and the shader needs no branch.</remarks>
    private uint NameOf(TextureHandle handle, string parameterName) =>
        handle.IsValid ? Resolve(handle, parameterName).Name : _whiteTexture;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>The mesh program's uniform locations, resolved once when it is linked.</summary>
    private readonly record struct MeshUniforms(
        int ModelView,
        int Projection,
        int DetailFade,
        int AlphaTest,
        int PreviousModelView = -1,
        int PoseBlend = -1,
        int PreviousPoseBlend = -1);

    /// <summary>A linked G-buffer layout and its resolved per-draw uniform locations.</summary>
    private sealed record GBufferPipeline(
        GLShaderProgram Program,
        MeshUniforms Uniforms,
        int Material,
        int DecodeAlbedo,
        int Detail);

    /// <summary>One non-blocking any-samples query and its last trustworthy result.</summary>
    private readonly record struct VisibilityQuery(
        uint Name,
        bool Pending = false,
        bool Visible = true,
        int EmptyFrames = 0);

    /// <summary>A texture this device owns, and the derived detail map attached to it, if any.</summary>
    /// <remarks>
    /// The detail map lives on the texture entry rather than in a table of its own because every draw
    /// already resolves its diffuse through this dictionary. Attaching it here makes the lookup free;
    /// a second dictionary would be a second hash of the same key, per sub-mesh, per frame.
    /// </remarks>
    private readonly record struct GLTexture(
        uint Name,
        int Width,
        int Height,
        int MipLevels,
        uint Detail = 0,
        int Depth = 1,
        TextureTarget Target = TextureTarget.Texture2D,
        InternalFormat Format = InternalFormat.Rgba8);

    /// <summary>One uploaded mesh.</summary>
    /// <remarks>
    /// A static mesh is the first four fields, its vertex array reading the previous-pose stream from
    /// its own buffer. An animated mesh carries a second buffer and a second array with the buffer
    /// roles exchanged; <see cref="FrontIsFirst"/> says which pairing is current, and an update flips
    /// it rather than copying anything.
    /// </remarks>
    private readonly record struct GLMesh(
        uint VertexArray,
        uint VertexBuffer,
        uint IndexBuffer,
        int IndexCount,
        uint SecondVertexArray = 0,
        uint SecondVertexBuffer = 0,
        int VertexCount = 0,
        bool FrontIsFirst = true,
        int VertexCapacity = 0,
        int IndexCapacity = 0)
    {
        public bool IsAnimated => SecondVertexBuffer != 0;

        public bool IsStorage => VertexCapacity > 0;

        public uint ActiveVertexArray => FrontIsFirst ? VertexArray : SecondVertexArray;
    }
}
