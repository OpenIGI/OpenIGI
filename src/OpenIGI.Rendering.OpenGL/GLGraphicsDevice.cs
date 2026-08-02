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
public sealed class GLGraphicsDevice : IGraphicsDevice
{
    /// <summary>How many sampling units the backend tracks internally.</summary>
    /// <remarks>
    /// Not the same number as <see cref="GraphicsCapabilities.TextureUnits"/>, which is what the seam
    /// lets a caller address and stays at the original's eight. These are the backend's own: the probe
    /// field alone wants four, and GL 3.3 guarantees sixteen to a fragment shader.
    /// </remarks>
    private const int MaxTextureUnits = 16;
    private const string LogCategory = "OpenGL";

    private readonly GL _gl;
    private readonly Action _present;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<uint, GLTexture> _textures = [];
    private readonly Dictionary<uint, GLMesh> _meshes = [];

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
    private int _lightingProbesEnabled = -1;
    private int _lightingProbeOrigin = -1;
    private int _lightingProbeInverseCount = -1;
    private int _lightingProbeScale = -1;
    private int _lightingProbeSky = -1;
    private int _lightingProbeViewToWorld = -1;
    private int _lightingProbeCamera = -1;
    private int _lightingShowProbes = -1;
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
    private int _occlusionUnproject = -1;
    private int _occlusionSearch = -1;
    private int _occlusionBounce = -1;
    private int _occlusionTexelSize = -1;
    private int _occlusionFrame;
    private int _lightingOcclusionEnabled = -1;

    private Matrix4f? _previousPose;
    private Matrix4f _sentPreviousPose;
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

    /// <summary>GL_TEXTURE_MAX_ANISOTROPY. Core since 4.6 and an EXT with the same value long before.</summary>
    private const GLEnum TextureMaxAnisotropy = (GLEnum)0x84FE;

    /// <summary>GL_MAX_TEXTURE_MAX_ANISOTROPY.</summary>
    private const GLEnum MaxTextureMaxAnisotropy = (GLEnum)0x84FF;

    private int _anisotropy = 1;
    private int _anisotropyLimit = 1;
    private bool _generateMissingMipmaps;

    private GLShaderProgram? _fogProgram;
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
    private int _gradeFrame;

    private GLShaderProgram? _bloomThresholdProgram;
    private GLShaderProgram? _bloomBlurProgram;
    private int _bloomThreshold = -1;
    private int _bloomDirection = -1;
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

        // The original always clears colour and depth together, and depth always to 1.0.
        // glClear honours the depth mask, so a TestOnly draw earlier in the frame would silently stop
        // the depth buffer from clearing; force the mask on across the clear and put it back after.
        bool maskedOff = _depthState == DepthState.TestOnly;
        if (maskedOff)
        {
            _gl.DepthMask(true);
        }

        _gl.ClearColor(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, 1.0f);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

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

        uint array = _gl.GenVertexArray();
        uint vertexBuffer = _gl.GenBuffer();
        uint indexBuffer = _gl.GenBuffer();

        BindVertexArray(array);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * Vertex3D.Size), vertices, BufferUsageARB.StaticDraw);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexBuffer);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(ushort)), indices, BufferUsageARB.StaticDraw);

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

        // Unbound before the buffer bindings are cleared, or clearing the element array would record
        // "no indices" into the array object that was just configured.
        BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);

        MeshHandle handle = new(_nextMeshId++);
        _meshes[handle.Id] = new GLMesh(array, vertexBuffer, indexBuffer, indices.Length);
        return handle;
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

            // Deleting the bound array reverts GL to zero; the shadow has to say so.
            if (_currentVertexArray == entry.VertexArray)
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

        _logger.Information(
            "GI",
            $"Probe field uploaded: {volume.CountX}x{volume.CountY}x{volume.CountZ} at {volume.Spacing:0.#} m.");
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

        _captureActive = true;
        _temporalResolved = false;
        _sceneTarget.BeginFrame();
        _sceneTarget.BindForGeometry();
        return true;
    }

    /// <inheritdoc/>
    public void SetMaterialOverride(in SurfaceMaterial material) => _material = material;

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
        if (CurrentPass == ScenePass.Geometry)
        {
            _sceneTarget!.ResolveMultisample();

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
        ResolveTemporal(target);

        bool bloomed = RunBloom(target, post);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        SetViewport(new Viewport(0, 0, CurrentMode.Width, CurrentMode.Height));

        UseProgram(_resolveProgram);
        _resolveProgram.SetFloat(_resolveExposure, post.Exposure);
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

        BindTexture(0, target.FrontColorTexture);
        _fullScreen.Draw();

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

    /// <summary>Spreads the brightest part of the frame into a half-resolution buffer.</summary>
    /// <param name="target">The scene target holding the finished frame.</param>
    /// <param name="post">The frame's settings, for the threshold and whether to run at all.</param>
    /// <returns><see langword="true"/> when there is a bloom buffer for the resolve to add.</returns>
    /// <remarks>
    /// Bright pass, then a separable Gaussian: a two-dimensional Gaussian is the product of two
    /// one-dimensional ones, so a wide blur costs two passes rather than the square of its radius.
    /// </remarks>
    private bool RunBloom(GLSceneTarget target, in PostProcessSettings post)
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
        _gl.DepthMask(false);
        _depthState = DepthState.Disabled;

        (float texelX, float texelY) = target.BloomTexelSize;

        target.BindForBloom(0);
        UseProgram(_bloomThresholdProgram);

        // The knee is a quarter of the threshold rather than a constant: what counts as "just below
        // the threshold" has to scale with the threshold, or a high one gets a hard edge back.
        _bloomThresholdProgram.SetVector2(
            _bloomThreshold, post.BloomThreshold, MathF.Max(post.BloomThreshold * 0.25f, 0.01f));
        BindTexture(0, target.FrontColorTexture);
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
        BindTexture(3, target.MaterialTexture);
        BindTexture(4, target.DepthTexture);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
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
        _gl.DepthMask(false);
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
        if (!_occlusion.Enabled || _occlusionProgram is null || _fullScreen is null)
        {
            return false;
        }

        if (!target.BindForOcclusion())
        {
            return false;
        }

        UseProgram(_occlusionProgram);
        _occlusionProgram.SetVector4(
            _occlusionUnproject,
            _unprojection.XScale,
            _unprojection.YScale,
            _unprojection.Near,
            _unprojection.Far);
        _occlusionProgram.SetVector4(
            _occlusionSearch,
            Math.Clamp(_occlusion.Quality, 1, 8),
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

        _occlusionProgram.SetVector2(
            _occlusionTexelSize, 1f / target.Description.Width, 1f / target.Description.Height);

        BindTexture(4, target.DepthTexture);
        BindTexture(1, target.NormalTexture);
        BindTexture(2, target.HistoryTexture);

        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _depthState = DepthState.Disabled;

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

        _fullScreen!.Draw();

        // The pass left the bindings and the depth shadow in a state the next draw must not inherit.
        _currentVertexArray = 0;
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;
    }

    /// <summary>Marches the participating medium over the lit scene.</summary>
    /// <param name="target">The scene target holding the lit frame and its depth.</param>
    /// <remarks>
    /// Reads the front colour buffer and writes the back, then swaps. A full-screen pass cannot read
    /// and write one texture at once, which is what the ping-pong pair exists for.
    /// </remarks>
    private void RunVolumetricFog(GLSceneTarget target)
    {

        if (!_fog.Enabled || _fogProgram is null || _fullScreen is null)
        {
            return;
        }

        target.BindForPostProcess();
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

        BindTexture(0, target.FrontColorTexture);
        BindTexture(4, target.DepthTexture);

        if (_shadowMap is { IsReady: true } shadows)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + 5);
            _gl.BindTexture(TextureTarget.Texture2DArray, shadows.Texture);
            _activeTextureUnit = 5;
        }

        _fullScreen.Draw();
        target.Swap();

        _currentVertexArray = 0;
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(true);
        _depthState = DepthState.Disabled;
    }

    /// <inheritdoc/>
    public unsafe void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
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

        ArgumentOutOfRangeException.ThrowIfNegative(indexOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(indexCount);

        if (indexCount % 3 != 0)
        {
            throw new ArgumentException($"A draw must cover whole triangles, got {indexCount} indices.", nameof(indexCount));
        }

        if (indexOffset + indexCount > entry.IndexCount)
        {
            throw new ArgumentException(
                $"Indices {indexOffset}..{indexOffset + indexCount} lie outside the mesh's {entry.IndexCount}.",
                nameof(indexCount));
        }

        if (indexCount == 0)
        {
            return;
        }

        // Which program shades this draw is decided by the pass, not by the caller: a renderer asks
        // for a mesh with a texture and a lightmap and never learns there are two ways to draw it.
        bool shadow = CurrentPass == ScenePass.Shadow;
        bool deferred = !shadow
            && CurrentPass == ScenePass.Geometry
            && _sceneTarget is { IsReady: true } gbuffer
            && gbuffer.Description.Deferred;

        GLShaderProgram program = shadow ? _shadowProgram! : deferred ? _gbufferProgram! : _meshProgram!;
        MeshUniforms uniforms = shadow ? _shadowUniforms : deferred ? _gbufferUniforms : _meshUniforms;

        if (!ReferenceEquals(program, _lastMeshProgram))
        {
            // A shadowed uniform belongs to the program that received it, so switching programs
            // invalidates every one of them. Leaving the cache primed here would draw the whole frame
            // with whatever matrices the other program happened to be holding.
            _lastMeshProgram = program;
            _meshUniformsPrimed = false;
            _sentMaterial = null;
        }

        UseProgram(program);

        if (!shadow && _sentMaterial != _material)
        {
            _sentMaterial = _material;

            if (deferred)
            {
                program.SetVector4(
                    _gbufferMaterial, _material.Roughness, _material.Metallic, _material.Emissive, 0f);
            }
            else
            {
                // The fourth component is the switch. It is only ever set for the forward pass of a
                // modern frame, so the original's path uploads a zero here and its picture is
                // untouched to the bit.
                bool lit = CurrentPass == ScenePass.Forward
                    && _sceneTarget is { IsReady: true } lightingTarget
                    && lightingTarget.Description.Deferred;

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
            program.SetMatrix4(uniforms.ModelView, modelView);

            if (uniforms.PreviousModelView >= 0)
            {
                program.SetMatrix4(uniforms.PreviousModelView, previousPose);
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

        BindTexture(0, NameOf(diffuse, nameof(diffuse)));
        BindTexture(1, NameOf(lightmap, nameof(lightmap)));

        ApplyBlend(blend);

        BindVertexArray(entry.VertexArray);
        _gl.DrawElements(
            PrimitiveType.Triangles,
            (uint)indexCount,
            DrawElementsType.UnsignedShort,
            (void*)(indexOffset * sizeof(ushort)));
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
        _sceneTarget?.Dispose();
        _fogProgram?.Dispose();
        _shadowMap?.Dispose();
        _shadowProgram?.Dispose();
        _gbufferProgram?.Dispose();
        _lightingProgram?.Dispose();
        _fullScreen?.Dispose();
        _resolveProgram?.Dispose();

        foreach (GLMesh mesh in _meshes.Values)
        {
            _gl.DeleteVertexArray(mesh.VertexArray);
            _gl.DeleteBuffer(mesh.VertexBuffer);
            _gl.DeleteBuffer(mesh.IndexBuffer);
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

    private unsafe void CreatePipeline()
    {
        _program = GLShaderProgram.Create(_gl, Sprite2DShaders.Vertex, Sprite2DShaders.Fragment);
        _meshProgram = GLShaderProgram.Create(_gl, Mesh3DShaders.Vertex, Mesh3DShaders.Fragment);

        // Resolved once, here, so no draw ever looks a uniform up by name again.
        _spriteInverseScreen = _program.LocationOf("uInverseScreen");
        _meshUniforms = new MeshUniforms(
            _meshProgram.LocationOf("uModelView"),
            _meshProgram.LocationOf("uProjection"),
            _meshProgram.LocationOf("uDetailFade"),
            _meshProgram.LocationOf("uAlphaTest"));
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

        _resolveProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, PostProcessShaders.Fragment);
        _resolveExposure = _resolveProgram.LocationOf("uExposure");
        _resolveToneMap = _resolveProgram.LocationOf("uToneMap");
        _resolveDecodeInput = _resolveProgram.LocationOf("uDecodeInput");
        _resolveWhitePoint = _resolveProgram.LocationOf("uWhitePoint");
        _resolveDebugView = _resolveProgram.LocationOf("uDebugView");
        _resolveBloomIntensity = _resolveProgram.LocationOf("uBloomIntensity");
        _resolveGrade = _resolveProgram.LocationOf("uGrade");
        _resolveLens = _resolveProgram.LocationOf("uLens");
        _resolveTexelSize = _resolveProgram.LocationOf("uTexelSize");
        UseProgram(_resolveProgram);
        _resolveProgram.SetInt("uScene", 0);
        _resolveProgram.SetInt("uDebugAlbedo", 6);
        _resolveProgram.SetInt("uDebugNormal", 7);
        _resolveProgram.SetInt("uDebugBaked", 1);
        _resolveProgram.SetInt("uDebugMaterial", 2);
        _resolveProgram.SetInt("uDebugMotion", 5);
        _resolveProgram.SetInt("uDebugOcclusion", 3);
        _resolveProgram.SetInt("uBloom", 7);

        _bloomThresholdProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, BloomShaders.Threshold);
        _bloomThreshold = _bloomThresholdProgram.LocationOf("uThreshold");
        UseProgram(_bloomThresholdProgram);
        _bloomThresholdProgram.SetInt("uScene", 0);

        _bloomBlurProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, BloomShaders.Blur);
        _bloomDirection = _bloomBlurProgram.LocationOf("uDirection");
        UseProgram(_bloomBlurProgram);
        _bloomBlurProgram.SetInt("uSource", 0);
        _resolveProgram.SetInt("uDebugDepth", 4);

        _gbufferProgram = GLShaderProgram.Create(_gl, GBufferShaders.Vertex, GBufferShaders.Fragment);
        _gbufferUniforms = new MeshUniforms(
            _gbufferProgram.LocationOf("uModelView"),
            _gbufferProgram.LocationOf("uProjection"),
            _gbufferProgram.LocationOf("uDetailFade"),
            _gbufferProgram.LocationOf("uAlphaTest"),
            _gbufferProgram.LocationOf("uPreviousModelView"));
        _gbufferMaterial = _gbufferProgram.LocationOf("uMaterial");
        _gbufferDecodeAlbedo = _gbufferProgram.LocationOf("uDecodeAlbedo");
        UseProgram(_gbufferProgram);
        _gbufferProgram.SetInt("uDiffuse", 0);
        _gbufferProgram.SetInt("uLightmap", 1);

        _occlusionProgram = GLShaderProgram.Create(
            _gl, PostProcessShaders.Vertex, AmbientOcclusionShaders.Fragment);
        _occlusionUnproject = _occlusionProgram.LocationOf("uUnproject");
        _occlusionSearch = _occlusionProgram.LocationOf("uSearch");
        _occlusionBounce = _occlusionProgram.LocationOf("uBounce");
        _occlusionTexelSize = _occlusionProgram.LocationOf("uTexelSize");
        UseProgram(_occlusionProgram);
        _occlusionProgram.SetInt("uDepth", 4);
        _occlusionProgram.SetInt("uNormal", 1);
        _occlusionProgram.SetInt("uPreviousColour", 2);

        _lightingProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, DeferredLightingShaders.Fragment);
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

        _lightingOcclusionEnabled = _lightingProgram.LocationOf("uOcclusionEnabled");
        _lightingProbesEnabled = _lightingProgram.LocationOf("uProbesEnabled");
        _lightingProbeOrigin = _lightingProgram.LocationOf("uProbeOrigin");
        _lightingProbeInverseCount = _lightingProgram.LocationOf("uProbeInverseCount");
        _lightingProbeScale = _lightingProgram.LocationOf("uProbeScale");
        _lightingProbeSky = _lightingProgram.LocationOf("uProbeSky");
        _lightingProbeViewToWorld = _lightingProgram.LocationOf("uProbeViewToWorld");
        _lightingProbeCamera = _lightingProgram.LocationOf("uProbeCamera");
        _lightingShowProbes = _lightingProgram.LocationOf("uShowProbes");
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

        _shadowProgram = GLShaderProgram.Create(_gl, ShadowDepthShaders.Vertex, ShadowDepthShaders.Fragment);
        _shadowUniforms = new MeshUniforms(
            _shadowProgram.LocationOf("uModelView"),
            _shadowProgram.LocationOf("uProjection"),
            _shadowProgram.LocationOf("uDetailFade"),
            _shadowProgram.LocationOf("uAlphaTest"));
        UseProgram(_shadowProgram);
        _shadowProgram.SetInt("uDiffuse", 0);

        _temporalProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, TemporalAaShaders.Fragment);
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
            _gl, PostProcessShaders.Vertex, ReflectionShaders.Fragment);
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
        _reflectionProgram.SetInt("uMaterial", 3);
        _reflectionProgram.SetInt("uDepth", 4);
        _reflectionProgram.SetInt("uProbeRed", 8);
        _reflectionProgram.SetInt("uProbeGreen", 9);
        _reflectionProgram.SetInt("uProbeBlue", 10);

        _fogProgram = GLShaderProgram.Create(_gl, PostProcessShaders.Vertex, VolumetricFogShaders.Fragment);
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

        _shadowMap = new GLShadowMap(_gl, _logger);
        _fullScreen = new GLFullScreenPass(_gl);
        _sceneTarget = new GLSceneTarget(_gl, _logger);

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
        int PreviousModelView = -1);

    private readonly record struct GLTexture(uint Name, int Width, int Height, int MipLevels);

    private readonly record struct GLMesh(uint VertexArray, uint VertexBuffer, uint IndexBuffer, int IndexCount);
}
