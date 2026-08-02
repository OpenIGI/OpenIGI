using OpenIGI.Core.Diagnostics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// The offscreen framebuffer the scene is rendered into.
/// </summary>
/// <remarks>
/// <para>
/// The first framebuffer object in the project. Colour is an <c>RGBA16F</c> texture when high dynamic
/// range is asked for and <c>RGBA8</c> otherwise, and depth is a <em>texture</em> rather than a
/// renderbuffer — which is the whole point, because every effect from here on reads depth back.
/// </para>
/// <para>
/// Colour is a ping-pong pair. A post-process cannot read and write one texture at once, so a pass
/// reads the front and writes the back and the two swap; the depth attachment is shared, so geometry
/// drawn after a full-screen pass still depth-tests against the scene it was drawn over.
/// </para>
/// <para>
/// Multisampling, when asked for, rasterises into a separate multisampled framebuffer that is
/// resolved into the front colour texture and the depth texture with <c>glBlitFramebuffer</c> before
/// anything samples them: a shader cannot usefully read a multisampled depth buffer, and every effect
/// the remaster track adds needs to.
/// </para>
/// </remarks>
internal sealed class GLSceneTarget : IDisposable
{
    private const string LogCategory = "SceneTarget";

    private readonly GL _gl;
    private readonly IIgiLogger _logger;

    private readonly uint[] _colorTextures = [0, 0];
    private readonly uint[] _colorFramebuffers = [0, 0];
    private readonly uint[] _gbufferTextures = [0, 0, 0, 0];
    private readonly uint[] _historyTextures = [0, 0];
    private readonly uint[] _historyFramebuffers = [0, 0];
    private int _historyWrite;
    private bool _historyValid;
    private uint _gbufferFramebuffer;
    private uint _depthTexture;
    private uint _msaaFramebuffer;
    private uint _msaaColor;
    private uint _msaaDepth;
    private int _front;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GLSceneTarget"/> class.</summary>
    /// <param name="gl">The GL binding.</param>
    /// <param name="logger">Where to report a target that could not be created.</param>
    public GLSceneTarget(GL gl, IIgiLogger logger)
    {
        _gl = gl;
        _logger = logger;
    }

    /// <summary>Gets the description the target was last built for.</summary>
    public SceneTargetDescription Description { get; private set; }

    /// <summary>Gets a value indicating whether the target exists and can be rendered into.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Gets the texture holding the scene as most recently written.</summary>
    public uint FrontColorTexture => _colorTextures[_front];

    /// <summary>Gets the scene's depth, readable by a shader.</summary>
    public uint DepthTexture => _depthTexture;

    /// <summary>Gets the accumulated frames the temporal resolve reads.</summary>
    /// <remarks>
    /// A pair rather than one texture: the resolve reads the frames accumulated so far and writes the
    /// new accumulation, and no pass may read and write one texture at once. Alternating between two
    /// means the result is ready to be sampled where it stands, with no copy back.
    /// </remarks>
    public uint HistoryTexture => _historyTextures[1 - _historyWrite];

    /// <summary>Gets the texture the temporal resolve last wrote, which is then the scene.</summary>
    public uint ResolvedHistoryTexture => _historyTextures[_historyWrite];

    /// <summary>Gets a value indicating whether the history holds a frame worth blending.</summary>
    /// <remarks>
    /// False for the first frame after the target is built, when the history is uninitialised memory
    /// rather than a picture. The alternative — clearing it to black — would fade the first frames up
    /// from nothing, which reads as a fault rather than as anti-aliasing.
    /// </remarks>
    public bool HasHistory => _historyValid;

    /// <summary>Gets the albedo target, or zero when the target is not deferred.</summary>
    public uint AlbedoTexture => _gbufferTextures[0];

    /// <summary>Gets the view-space normal target, or zero when the target is not deferred.</summary>
    public uint NormalTexture => _gbufferTextures[1];

    /// <summary>Gets the baked-irradiance target, or zero when the target is not deferred.</summary>
    public uint BakedLightTexture => _gbufferTextures[2];

    /// <summary>Gets the roughness/metallic/emissive target, or zero when the target is not deferred.</summary>
    public uint MaterialTexture => _gbufferTextures[3];

    /// <summary>Gets the framebuffer geometry should be rasterised into.</summary>
    /// <remarks>
    /// The G-buffer when the target is deferred, the multisampled framebuffer when multisampling is
    /// on, and otherwise the front colour target.
    /// </remarks>
    public uint GeometryFramebuffer =>
        Description.Deferred ? _gbufferFramebuffer
        : Description.IsMultisampled ? _msaaFramebuffer
        : _colorFramebuffers[_front];

    /// <summary>Builds or rebuilds the target for a description.</summary>
    /// <param name="description">The target to build.</param>
    /// <returns><see langword="true"/> when the target is ready to render into.</returns>
    /// <remarks>A description matching the current one is a no-op, so this is safe to call every frame.</remarks>
    public bool Configure(in SceneTargetDescription description)
    {
        if (!description.IsValid)
        {
            return false;
        }

        if (IsReady && Description == description)
        {
            return true;
        }

        Release();
        Description = description;

        InternalFormat colorFormat = description.HighDynamicRange ? InternalFormat.Rgba16f : InternalFormat.Rgba8;

        for (int i = 0; i < 2; i++)
        {
            _colorTextures[i] = CreateTexture(colorFormat, PixelFormat.Rgba, PixelType.Float, description);
        }

        _depthTexture = CreateTexture(
            InternalFormat.DepthComponent32f,
            PixelFormat.DepthComponent,
            PixelType.Float,
            description);

        for (int i = 0; i < 2; i++)
        {
            _colorFramebuffers[i] = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _colorFramebuffers[i]);
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                _colorTextures[i],
                0);
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D,
                _depthTexture,
                0);

            if (!IsComplete($"colour target {i}"))
            {
                Release();
                return false;
            }
        }

        if (description.Deferred && !CreateGBuffer(description))
        {
            Release();
            return false;
        }

        if (!CreateHistory(colorFormat, description))
        {
            // No history means no accumulation, and the rest of the frame is unaffected -- so this is
            // a feature that quietly does not happen rather than a target that failed to build.
            _logger.Warning(LogCategory, "The temporal history could not be created; accumulation is off.");
        }

        if (description.IsMultisampled && !CreateMultisampleTargets(colorFormat, description))
        {
            // Falling back to a single-sampled target is far better than refusing to render: the
            // picture is the same picture, just with harder edges.
            _logger.Warning(LogCategory, $"{description.SampleCount}x multisampling is unavailable; continuing without it.");
            Description = description with { SampleCount = 1 };
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        IsReady = true;
        return true;
    }

    /// <summary>Makes the geometry framebuffer current and sets the viewport to the whole target.</summary>
    public void BindForGeometry()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, GeometryFramebuffer);
        _gl.Viewport(0, 0, (uint)Description.Width, (uint)Description.Height);
    }

    /// <summary>
    /// Resolves multisampled rasterisation into the sampleable colour and depth textures.
    /// </summary>
    /// <remarks>
    /// A no-op when multisampling is off. Depth is blitted as well as colour, because the effects that
    /// follow read depth and a multisampled depth buffer is not something a shader can sample.
    /// </remarks>
    public void ResolveMultisample()
    {
        if (!Description.IsMultisampled)
        {
            return;
        }

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _msaaFramebuffer);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _colorFramebuffers[_front]);
        _gl.BlitFramebuffer(
            0, 0, Description.Width, Description.Height,
            0, 0, Description.Width, Description.Height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        _gl.BlitFramebuffer(
            0, 0, Description.Width, Description.Height,
            0, 0, Description.Width, Description.Height,
            ClearBufferMask.DepthBufferBit,
            BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Makes the front colour buffer current, for the lighting pass and the forward draws.</summary>
    /// <remarks>
    /// Its depth attachment is the one the G-buffer wrote, so a forward draw over the lit scene still
    /// depth-tests against the geometry that was recorded before it.
    /// </remarks>
    public void BindForColour()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _colorFramebuffers[_front]);
        _gl.Viewport(0, 0, (uint)Description.Width, (uint)Description.Height);
    }

    /// <summary>Makes the back colour buffer current so a full-screen pass can write into it.</summary>
    /// <remarks>The front buffer stays readable as the pass's input; <see cref="Swap"/> exchanges them.</remarks>
    public void BindForPostProcess()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _colorFramebuffers[1 - _front]);
        _gl.Viewport(0, 0, (uint)Description.Width, (uint)Description.Height);
    }

    /// <summary>Makes what was just written the scene.</summary>
    public void Swap() => _front = 1 - _front;

    /// <summary>Makes the history buffer being written current, for the temporal resolve.</summary>
    /// <returns><see langword="true"/> when there is a history to write into.</returns>
    public bool BindForTemporalResolve()
    {
        if (_historyFramebuffers[_historyWrite] == 0)
        {
            return false;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _historyFramebuffers[_historyWrite]);
        _gl.Viewport(0, 0, (uint)Description.Width, (uint)Description.Height);
        return true;
    }

    /// <summary>Copies what the temporal resolve wrote back over the scene, and makes it the front.</summary>
    /// <remarks>
    /// So that the forward pass draws onto the accumulated image without drawing <em>into</em> the
    /// history. Anything composited after the resolve — the view model above all — must not become part
    /// of what the next frame blends against, or it accumulates against itself.
    /// </remarks>
    public void CopyTemporalResultToScene()
    {
        if (_historyFramebuffers[_historyWrite] == 0)
        {
            return;
        }

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _historyFramebuffers[_historyWrite]);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _colorFramebuffers[1 - _front]);
        _gl.BlitFramebuffer(
            0, 0, Description.Width, Description.Height,
            0, 0, Description.Width, Description.Height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);
        Swap();
    }

    /// <summary>Makes what the temporal resolve just wrote the history the next frame reads.</summary>
    public void AdvanceHistory()
    {
        _historyWrite = 1 - _historyWrite;
        _historyValid = true;
    }

    /// <summary>Forgets the accumulated frames, so the next one starts over.</summary>
    /// <remarks>
    /// For a cut, a teleport, or a resolution change — anything after which the previous frame is not a
    /// slightly different view of this one but a different picture entirely.
    /// </remarks>
    public void DiscardHistory() => _historyValid = false;

    /// <summary>Clears the target's colour and depth.</summary>
    /// <param name="r">The red component in linear light.</param>
    /// <param name="g">The green component in linear light.</param>
    /// <param name="b">The blue component in linear light.</param>
    public void Clear(float r, float g, float b)
    {
        BindForGeometry();

        // The depth mask has to be on for glClear to touch depth at all, and a translucent pass may
        // have left it off -- the same trap the direct path documents on Clear.
        _gl.DepthMask(true);
        _gl.ClearColor(r, g, b, 1.0f);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
    }

    /// <summary>Creates the pair of accumulation buffers the temporal resolve alternates between.</summary>
    /// <param name="colorFormat">The scene's colour format, which the history has to match.</param>
    /// <param name="description">The target being built.</param>
    /// <returns><see langword="true"/> when both buffers exist.</returns>
    private bool CreateHistory(InternalFormat colorFormat, in SceneTargetDescription description)
    {
        _historyWrite = 0;
        _historyValid = false;

        for (int i = 0; i < 2; i++)
        {
            _historyTextures[i] = CreateTexture(colorFormat, PixelFormat.Rgba, PixelType.Float, description);
            _historyFramebuffers[i] = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _historyFramebuffers[i]);

            // Colour only. The resolve is a full-screen pass over a finished frame, so there is nothing
            // for it to depth-test against and sharing the scene's depth would only risk writing it.
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                _historyTextures[i],
                0);

            if (!IsComplete($"history {i}"))
            {
                return false;
            }
        }

        return true;
    }


    /// <summary>Builds the three G-buffer targets and the framebuffer that writes all of them at once.</summary>
    /// <param name="description">The target being built.</param>
    /// <returns><see langword="true"/> when the G-buffer is complete.</returns>
    /// <remarks>
    /// The formats are chosen for what each channel has to survive. Albedo is ordinary 8-bit colour.
    /// The normal gets <c>RGB10_A2</c>: ten bits a channel is what stops a slowly-curving surface from
    /// banding once a specular highlight runs across it, and the two-bit alpha is plenty for a
    /// metallic value that is physically almost always nought or one. Baked irradiance is 8-bit
    /// because that is the precision the original's lightmaps have in the first place.
    /// </remarks>
    private bool CreateGBuffer(in SceneTargetDescription description)
    {
        // The normal target is 16-bit float rather than RGB10_A2: it has to carry a usable alpha for
        // blending, and RGB10_A2's two-bit alpha gives four coverage levels, which is visible banding
        // on every blended edge.
        InternalFormat[] formats =
        [
            InternalFormat.Rgba8,
            InternalFormat.Rgba16f,
            InternalFormat.Rgba8,
            InternalFormat.Rgba8,
        ];

        for (int i = 0; i < _gbufferTextures.Length; i++)
        {
            _gbufferTextures[i] = CreateTexture(formats[i], PixelFormat.Rgba, PixelType.Float, description);
        }

        _gbufferFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferFramebuffer);

        for (int i = 0; i < _gbufferTextures.Length; i++)
        {
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0 + i,
                TextureTarget.Texture2D,
                _gbufferTextures[i],
                0);
        }

        // Depth is shared with the colour targets, which is what lets the lighting pass write into a
        // colour buffer that still depth-tests correctly for the forward draws after it.
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D,
            _depthTexture,
            0);

        // Without this only attachment 0 is written, and the normal and baked targets stay empty.
        Span<GLEnum> buffers =
        [
            GLEnum.ColorAttachment0,
            GLEnum.ColorAttachment1,
            GLEnum.ColorAttachment2,
            GLEnum.ColorAttachment3,
        ];

        _gl.DrawBuffers(buffers);
        return IsComplete("G-buffer");
    }

    private bool CreateMultisampleTargets(InternalFormat colorFormat, in SceneTargetDescription description)
    {
        uint samples = (uint)description.SampleCount;

        _msaaColor = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaColor);
        _gl.RenderbufferStorageMultisample(
            RenderbufferTarget.Renderbuffer, samples, colorFormat, (uint)description.Width, (uint)description.Height);

        _msaaDepth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _msaaDepth);
        _gl.RenderbufferStorageMultisample(
            RenderbufferTarget.Renderbuffer,
            samples,
            InternalFormat.DepthComponent32f,
            (uint)description.Width,
            (uint)description.Height);

        _msaaFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _msaaFramebuffer);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _msaaColor);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _msaaDepth);

        if (IsComplete("multisample target"))
        {
            return true;
        }

        ReleaseMultisample();
        return false;
    }

    private uint CreateTexture(
        InternalFormat internalFormat,
        PixelFormat format,
        PixelType type,
        in SceneTargetDescription description)
    {
        uint name = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, name);

        unsafe
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)internalFormat,
                (uint)description.Width,
                (uint)description.Height,
                0,
                format,
                type,
                null);
        }

        // Linear sampling and clamping, because every consumer is a full-screen pass reading a
        // neighbourhood: a wrapped sample would pull the opposite edge of the screen into a blur.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return name;
    }

    private bool IsComplete(string what)
    {
        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status == GLEnum.FramebufferComplete)
        {
            return true;
        }

        _logger.Warning(LogCategory, $"The {what} is incomplete ({status}).");
        return false;
    }

    private void Release()
    {
        IsReady = false;
        _front = 0;

        for (int i = 0; i < 2; i++)
        {
            if (_colorFramebuffers[i] != 0)
            {
                _gl.DeleteFramebuffer(_colorFramebuffers[i]);
                _colorFramebuffers[i] = 0;
            }

            if (_colorTextures[i] != 0)
            {
                _gl.DeleteTexture(_colorTextures[i]);
                _colorTextures[i] = 0;
            }
        }

        for (int i = 0; i < 2; i++)
        {
            if (_historyFramebuffers[i] != 0)
            {
                _gl.DeleteFramebuffer(_historyFramebuffers[i]);
                _historyFramebuffers[i] = 0;
            }

            if (_historyTextures[i] != 0)
            {
                _gl.DeleteTexture(_historyTextures[i]);
                _historyTextures[i] = 0;
            }
        }

        _historyWrite = 0;
        _historyValid = false;

        if (_depthTexture != 0)
        {
            _gl.DeleteTexture(_depthTexture);
            _depthTexture = 0;
        }

        for (int i = 0; i < _gbufferTextures.Length; i++)
        {
            if (_gbufferTextures[i] != 0)
            {
                _gl.DeleteTexture(_gbufferTextures[i]);
                _gbufferTextures[i] = 0;
            }
        }

        if (_gbufferFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_gbufferFramebuffer);
            _gbufferFramebuffer = 0;
        }

        ReleaseMultisample();
    }

    private void ReleaseMultisample()
    {
        if (_msaaFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_msaaFramebuffer);
            _msaaFramebuffer = 0;
        }

        if (_msaaColor != 0)
        {
            _gl.DeleteRenderbuffer(_msaaColor);
            _msaaColor = 0;
        }

        if (_msaaDepth != 0)
        {
            _gl.DeleteRenderbuffer(_msaaDepth);
            _msaaDepth = 0;
        }
    }
}
