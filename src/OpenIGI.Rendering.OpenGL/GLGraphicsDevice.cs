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
    private const int MaxTextureUnits = 8;
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

        Capabilities = ReadCapabilities();
        CreatePipeline();
        SetMode(mode);
    }

    /// <inheritdoc/>
    public event EventHandler? DeviceRestored;

    /// <inheritdoc/>
    public GraphicsCapabilities Capabilities { get; }

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

        ApplySampling(description);

        uint id = _nextTextureId++;
        _textures[id] = new GLTexture(name, description.Width, description.Height, description.MipLevels);
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
    public void ClearDetailFade()
    {
        ThrowIfDisposed();
        _detailFade = default;
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

        UseProgram(_meshProgram);

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
            _sentModelView = modelView;
            _sentProjection = _projection;
            _sentDetailFade = _detailFade;
            _sentAlphaTest = alphaTest;
            _meshProgram.SetMatrix4(_meshUniforms.ModelView, modelView);
            _meshProgram.SetMatrix4(_meshUniforms.Projection, _projection);
            _meshProgram.SetVector4(
                _meshUniforms.DetailFade,
                _detailFade.Scale,
                _detailFade.Offset,
                _detailFade.Slope,
                _detailFade.Enabled);
            _meshProgram.SetFloat(_meshUniforms.AlphaTest, alphaTest);
        }
        else
        {
            if (_sentModelView != modelView)
            {
                _sentModelView = modelView;
                _meshProgram.SetMatrix4(_meshUniforms.ModelView, modelView);
            }

            if (_sentProjection != _projection)
            {
                _sentProjection = _projection;
                _meshProgram.SetMatrix4(_meshUniforms.Projection, _projection);
            }

            if (_sentDetailFade != _detailFade)
            {
                _sentDetailFade = _detailFade;
                _meshProgram.SetVector4(
                    _meshUniforms.DetailFade,
                    _detailFade.Scale,
                    _detailFade.Offset,
                    _detailFade.Slope,
                    _detailFade.Enabled);
            }

            if (_sentAlphaTest != alphaTest)
            {
                _sentAlphaTest = alphaTest;
                _meshProgram.SetFloat(_meshUniforms.AlphaTest, alphaTest);
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

        // Which unit each sampler reads is fixed by the pipeline and never varies by draw, so it is set
        // now rather than four times per sub-mesh. A uniform belongs to its program, so both survive the
        // program switches between the 2D and 3D passes.
        UseProgram(_program);
        _program.SetInt("uTexture", 0);
        UseProgram(_meshProgram);
        _meshProgram.SetInt("uDiffuse", 0);
        _meshProgram.SetInt("uLightmap", 1);

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

        return new GraphicsCapabilities(
            Math.Clamp(units, 1, MaxTextureUnits),
            Math.Max(maxSize, 64),
            SupportsNonPowerOfTwoTextures: true,
            $"OpenGL {version} — {renderer} ({vendor})");
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
    private readonly record struct MeshUniforms(int ModelView, int Projection, int DetailFade, int AlphaTest);

    private readonly record struct GLTexture(uint Name, int Width, int Height, int MipLevels);

    private readonly record struct GLMesh(uint VertexArray, uint VertexBuffer, uint IndexBuffer, int IndexCount);
}
