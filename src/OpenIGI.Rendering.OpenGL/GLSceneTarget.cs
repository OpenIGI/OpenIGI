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
    private readonly uint[] _gbufferTextures = [0, 0, 0, 0, 0];
    private readonly uint[] _historyTextures = [0, 0];
    private readonly uint[] _historyFramebuffers = [0, 0];
    private int _historyWrite;
    private bool _historyValid;
    private uint _outputTexture;
    private uint _outputFramebuffer;
    private uint _outputDepth;
    private uint _foregroundDepth;
    private bool _presentingOutput;
    private uint _occlusionTexture;
    private uint _occlusionFramebuffer;
    private uint _occlusionSearchTexture;
    private uint _occlusionSearchFramebuffer;
    private int _occlusionWidth;
    private int _occlusionHeight;
    private uint _fogTexture;
    private uint _fogFramebuffer;
    private int _fogWidth;
    private int _fogHeight;
    private readonly uint[] _bloomTextures = [0, 0];
    private readonly uint[] _bloomFramebuffers = [0, 0];
    private int _bloomWidth;
    private int _bloomHeight;
    private uint _scratchTexture;
    private uint _scratchFramebuffer;
    private uint _resolvedTexture;
    private uint _resolvedFramebuffer;
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
    /// <remarks>
    /// The upsampled buffer once the temporal resolve has run at a higher output resolution, and the
    /// render-resolution buffer otherwise. Callers after the resolve — the forward pass and the final
    /// tone map — want whichever of the two is the frame, and never want to know which it was.
    /// </remarks>
    public uint FrontColorTexture => _presentingOutput ? _outputTexture : _colorTextures[_front];

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

    /// <summary>Gets the per-surface screen motion target, or zero when the target is not deferred.</summary>
    /// <remarks>
    /// How far each surface slid across the screen by moving itself, with the camera's own contribution
    /// deliberately excluded — the temporal resolve reprojects that exactly from depth, and what it
    /// cannot recover is which surfaces moved. Zero for everything static, which is nearly the whole
    /// frame.
    /// </remarks>
    public uint MotionTexture => _gbufferTextures[4];

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

        if (description.Deferred && !CreateFog(description))
        {
            Release();
            return false;
        }

        if (!CreateBloom(colorFormat, description))
        {
            _logger.Warning(LogCategory, "The bloom buffers could not be created; bloom is off.");
        }

        _scratchTexture = CreateTexture(
            colorFormat,
            PixelFormat.Rgba,
            PixelType.Float,
            description with { Width = description.PresentWidth, Height = description.PresentHeight });

        _scratchFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _scratchTexture,
            0);

        if (!IsComplete("scratch"))
        {
            _logger.Warning(LogCategory, "The scratch buffer could not be created; the camera effects are off.");
        }

        // Eight-bit, not float: this holds the frame AFTER the tone map and the sRGB encode, which is
        // the space spatial anti-aliasing has to work in. Edges are a property of the displayed
        // picture, and detecting them in linear light finds them in the wrong places.
        _resolvedTexture = CreateTexture(
            InternalFormat.Rgba8,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            description with { Width = description.PresentWidth, Height = description.PresentHeight });

        _resolvedFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolvedFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _resolvedTexture,
            0);

        if (!IsComplete("resolved"))
        {
            _logger.Warning(LogCategory, "The resolved buffer could not be created; spatial anti-aliasing is off.");
        }

        if (description.Deferred && !CreateOcclusion(description))
        {
            // Nothing else depends on it: the lighting pass reads full visibility when it is absent, so
            // a target without one is the same picture with flat ambient.
            _logger.Warning(LogCategory, "The occlusion buffer could not be created; ambient occlusion is off.");
        }

        if (!CreateHistory(colorFormat, description))
        {
            // No history means no accumulation, and the rest of the frame is unaffected -- so this is
            // a feature that quietly does not happen rather than a target that failed to build.
            _logger.Warning(LogCategory, "The temporal history could not be created; accumulation is off.");
        }

        // The first-person rig is a foreground layer, not part of the world's depth. Giving it a
        // private attachment lets the hands and held object occlude one another without clearing the
        // scene depth texture that depth-of-field and the other screen-space passes still have to read.
        _foregroundDepth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _foregroundDepth);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer,
            InternalFormat.DepthComponent32f,
            (uint)description.PresentWidth,
            (uint)description.PresentHeight);

        if (description.IsMultisampled && !CreateMultisampleTargets(colorFormat, description))
        {
            // Falling back to a single-sampled target is far better than refusing to render: the
            // picture is the same picture, just with harder edges.
            _logger.Warning(LogCategory, $"{description.SampleCount}x multisampling is unavailable; continuing without it.");
            Description = description with { SampleCount = 1 };
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        IsReady = true;
        _logger.Information(
            LogCategory,
            $"Configured {description.Width}x{description.Height} -> {description.PresentWidth}x{description.PresentHeight}, "
                + $"hdr={description.HighDynamicRange}, deferred={description.Deferred}, msaa={description.SampleCount}, "
                + $"scratch={_scratchFramebuffer != 0}, resolved={_resolvedFramebuffer != 0}, history={_historyFramebuffers[0] != 0}.");
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
        if (_presentingOutput)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _outputFramebuffer);
            _gl.Viewport(0, 0, (uint)Description.PresentWidth, (uint)Description.PresentHeight);
            return;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _colorFramebuffers[_front]);
        _gl.Viewport(0, 0, (uint)Description.Width, (uint)Description.Height);
    }

    /// <summary>Replaces the colour target's world depth with the private first-person depth layer.</summary>
    /// <remarks>
    /// The colour attachment is deliberately unchanged: the rig draws over the finished world. Only
    /// depth is isolated, so clearing it cannot turn the scene depth into the far-plane/sky value.
    /// Pair with <see cref="RestoreWorldDepth"/> before post-processing reads scene depth.
    /// </remarks>
    public void BindForForegroundDepth()
    {
        uint framebuffer = _presentingOutput ? _outputFramebuffer : _colorFramebuffers[_front];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer,
            _foregroundDepth);
        _gl.Viewport(0, 0, (uint)Description.PresentWidth, (uint)Description.PresentHeight);
    }

    /// <summary>Restores the world's depth attachment after the first-person foreground layer.</summary>
    public void RestoreWorldDepth()
    {
        uint framebuffer = _presentingOutput ? _outputFramebuffer : _colorFramebuffers[_front];
        uint depth = _presentingOutput ? _outputDepth : _depthTexture;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D,
            depth,
            0);
        _gl.Viewport(0, 0, (uint)Description.PresentWidth, (uint)Description.PresentHeight);
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
        _gl.Viewport(0, 0, (uint)Description.PresentWidth, (uint)Description.PresentHeight);
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

        if (_outputFramebuffer == 0)
        {
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _historyFramebuffers[_historyWrite]);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _colorFramebuffers[1 - _front]);
            _gl.BlitFramebuffer(
                0, 0, Description.Width, Description.Height,
                0, 0, Description.Width, Description.Height,
                ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Nearest);
            Swap();
            return;
        }

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _historyFramebuffers[_historyWrite]);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _outputFramebuffer);
        _gl.BlitFramebuffer(
            0, 0, Description.PresentWidth, Description.PresentHeight,
            0, 0, Description.PresentWidth, Description.PresentHeight,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // The world's depth, stretched to the output. The view model tests against it, so it has to
        // exist at the resolution the view model is now drawn at; nearest, because a depth halfway
        // between a wall and what is behind it is a surface neither of them has.
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _colorFramebuffers[_front]);
        _gl.BlitFramebuffer(
            0, 0, Description.Width, Description.Height,
            0, 0, Description.PresentWidth, Description.PresentHeight,
            ClearBufferMask.DepthBufferBit,
            BlitFramebufferFilter.Nearest);

        _presentingOutput = true;
    }

    /// <summary>Makes what the temporal resolve just wrote the history the next frame reads.</summary>
    public void AdvanceHistory()
    {
        _historyWrite = 1 - _historyWrite;
        _historyValid = true;
    }

    /// <summary>Gets the ambient visibility and bounced colour, or zero when the target is not deferred.</summary>
    /// <remarks>
    /// Alpha is how much of the sky the surface can see, and the colour is what its neighbours bounce
    /// onto it. They travel together because the same horizon search produces both.
    /// </remarks>
    public uint OcclusionTexture => _occlusionTexture;

    /// <summary>Gets the half-resolution raw horizon-search result.</summary>
    public uint OcclusionSearchTexture => _occlusionSearchTexture;

    /// <summary>Gets the texel size of the half-resolution occlusion buffer.</summary>
    public (float X, float Y) OcclusionSearchTexelSize =>
        (1f / Math.Max(1, _occlusionWidth), 1f / Math.Max(1, _occlusionHeight));

    /// <summary>Makes the occlusion buffer current, for the horizon search to write into.</summary>
    /// <returns><see langword="true"/> when there is a buffer to write into.</returns>
    public bool BindForOcclusion()
    {
        if (_occlusionFramebuffer == 0)
        {
            return false;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _occlusionFramebuffer);
        _gl.Viewport(0, 0, (uint)Description.Width, (uint)Description.Height);
        return true;
    }

    /// <summary>Makes the half-resolution horizon-search buffer current.</summary>
    public bool BindForOcclusionSearch()
    {
        if (_occlusionSearchFramebuffer == 0)
        {
            return false;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _occlusionSearchFramebuffer);
        _gl.Viewport(0, 0, (uint)_occlusionWidth, (uint)_occlusionHeight);
        return true;
    }

    /// <summary>Gets the half-resolution integrated fog scattering and transmittance.</summary>
    public uint FogTexture => _fogTexture;

    public (float X, float Y) FogTexelSize =>
        (1f / Math.Max(1, _fogWidth), 1f / Math.Max(1, _fogHeight));

    /// <summary>Makes the half-resolution fog integration buffer current.</summary>
    public bool BindForFogIntegration()
    {
        if (_fogFramebuffer == 0)
        {
            return false;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fogFramebuffer);
        _gl.Viewport(0, 0, (uint)_fogWidth, (uint)_fogHeight);
        return true;
    }

    /// <summary>Gets the presentation-sized scratch buffer the camera effects write into.</summary>
    /// <remarks>
    /// Its own buffer rather than the scene ping-pong, because by the time these run the frame may be
    /// at the <em>output</em> resolution — the temporal upsample has already happened — and the scene
    /// pair is sized for the render resolution.
    /// </remarks>
    public uint ScratchTexture => _scratchTexture;

    /// <summary>Gets a value indicating whether the scratch buffer exists.</summary>
    public bool HasScratch => _scratchFramebuffer != 0;

    /// <summary>Gets the tone-mapped frame, for a pass that has to run after the resolve.</summary>
    public uint ResolvedTexture => _resolvedTexture;

    /// <summary>Gets a value indicating whether the tone-mapped frame can be captured.</summary>
    public bool HasResolved => _resolvedFramebuffer != 0;

    /// <summary>Makes the tone-mapped buffer current, at the presentation size.</summary>
    public void BindForResolved()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolvedFramebuffer);
        _gl.Viewport(0, 0, (uint)Description.PresentWidth, (uint)Description.PresentHeight);
    }

    /// <summary>Makes the scratch buffer current, at the presentation size.</summary>
    public void BindForScratch()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchFramebuffer);
        _gl.Viewport(0, 0, (uint)Description.PresentWidth, (uint)Description.PresentHeight);
    }

    /// <summary>Gets the blurred bright parts of the frame, or zero when there is no bloom buffer.</summary>
    public uint BloomTexture => _bloomTextures[0];

    /// <summary>Gets a value indicating whether the bloom buffers exist.</summary>
    public bool HasBloom => _bloomFramebuffers[0] != 0;

    /// <summary>Makes one of the two half-resolution bloom buffers current.</summary>
    /// <param name="index">Which of the pair, 0 or 1.</param>
    public void BindForBloom(int index)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFramebuffers[index]);
        _gl.Viewport(0, 0, (uint)_bloomWidth, (uint)_bloomHeight);
    }

    /// <summary>Gets one of the two half-resolution bloom textures.</summary>
    /// <param name="index">Which of the pair, 0 or 1.</param>
    /// <returns>The texture name.</returns>
    public uint BloomTextureAt(int index) => _bloomTextures[index];

    /// <summary>Gets the size of one texel of the bloom buffers, for the blur's step.</summary>
    public (float X, float Y) BloomTexelSize => (1f / MathF.Max(_bloomWidth, 1), 1f / MathF.Max(_bloomHeight, 1));

    /// <summary>Gets a value indicating whether an upsampled output buffer exists to accumulate into.</summary>
    public bool HasOutput => _outputFramebuffer != 0;

    /// <summary>Puts the target back to drawing at the render resolution, for a new frame.</summary>
    /// <remarks>
    /// The upsampled output is where the frame lives only from the temporal resolve onwards. Every
    /// frame starts again at the render resolution, and forgetting to say so leaves the next frame's
    /// lighting writing into the previous frame's finished picture at the wrong size.
    /// </remarks>
    public void BeginFrame() => _presentingOutput = false;

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

    /// <summary>Creates the pair of half-resolution buffers the bloom blur ping-pongs between.</summary>
    /// <param name="colorFormat">The scene's colour format, which the bloom has to match.</param>
    /// <param name="description">The target being built.</param>
    /// <returns><see langword="true"/> when both buffers exist.</returns>
    /// <remarks>
    /// Half resolution costs nothing in quality here, and the reason is worth stating: the pass is a
    /// low-pass filter, so the detail lost to halving is exactly the detail the blur was about to
    /// remove anyway.
    /// </remarks>
    private bool CreateBloom(InternalFormat colorFormat, in SceneTargetDescription description)
    {
        _bloomWidth = Math.Max(1, description.Width / 2);
        _bloomHeight = Math.Max(1, description.Height / 2);

        SceneTargetDescription half = description with { Width = _bloomWidth, Height = _bloomHeight };

        for (int i = 0; i < 2; i++)
        {
            _bloomTextures[i] = CreateTexture(colorFormat, PixelFormat.Rgba, PixelType.Float, half);
            _bloomFramebuffers[i] = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _bloomFramebuffers[i]);
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                _bloomTextures[i],
                0);

            if (!IsComplete($"bloom {i}"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Creates the buffer the horizon search writes visibility and bounced colour into.</summary>
    /// <param name="description">The target being built.</param>
    /// <returns><see langword="true"/> when the buffer exists.</returns>
    /// <remarks>
    /// Sixteen-bit float rather than eight-bit: the bounced colour shares it, and bounced light is
    /// linear radiance that a byte cannot hold the low end of without banding the very gradients the
    /// effect exists to produce.
    /// </remarks>
    private bool CreateOcclusion(in SceneTargetDescription description)
    {
        _occlusionWidth = description.HalfWidth;
        _occlusionHeight = description.HalfHeight;
        SceneTargetDescription half = description with { Width = _occlusionWidth, Height = _occlusionHeight };

        _occlusionSearchTexture = CreateTexture(
            InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float, half);
        _occlusionSearchFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _occlusionSearchFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _occlusionSearchTexture,
            0);

        if (!IsComplete("occlusion search"))
        {
            return false;
        }

        _occlusionTexture = CreateTexture(
            InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float, description);

        _occlusionFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _occlusionFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _occlusionTexture,
            0);

        return IsComplete("occlusion");
    }

    private bool CreateFog(in SceneTargetDescription description)
    {
        _fogWidth = description.HalfWidth;
        _fogHeight = description.HalfHeight;
        SceneTargetDescription half = description with { Width = _fogWidth, Height = _fogHeight };
        _fogTexture = CreateTexture(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float, half);
        _fogFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fogFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _fogTexture,
            0);
        return IsComplete("fog integration");
    }

    /// <summary>Creates the pair of accumulation buffers the temporal resolve alternates between.</summary>
    /// <param name="colorFormat">The scene's colour format, which the history has to match.</param>
    /// <param name="description">The target being built.</param>
    /// <returns><see langword="true"/> when both buffers exist.</returns>
    private bool CreateHistory(InternalFormat colorFormat, in SceneTargetDescription description)
    {
        _historyWrite = 0;
        _historyValid = false;
        _presentingOutput = false;

        // At the OUTPUT resolution, which is the whole of what makes this an upsample rather than a
        // stretch: the accumulation has to have somewhere to put the detail the render resolution
        // cannot hold, or there is nowhere for the extra samples to go.
        SceneTargetDescription output = description with
        {
            Width = description.PresentWidth,
            Height = description.PresentHeight,
        };

        for (int i = 0; i < 2; i++)
        {
            _historyTextures[i] = CreateTexture(colorFormat, PixelFormat.Rgba, PixelType.Float, output);
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

        if (!description.IsUpsampling)
        {
            return true;
        }

        // The forward pass draws the view model over the accumulated frame, and once that frame is
        // bigger than the one the geometry went into it needs a target of its own -- with a depth
        // buffer, because the view model still tests against the world it is standing in.
        _outputTexture = CreateTexture(colorFormat, PixelFormat.Rgba, PixelType.Float, output);
        _outputDepth = CreateTexture(
            InternalFormat.DepthComponent32f,
            PixelFormat.DepthComponent,
            PixelType.Float,
            output);

        _outputFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _outputFramebuffer);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _outputTexture,
            0);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D,
            _outputDepth,
            0);

        return IsComplete("upsampled output");
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
            InternalFormat.Rgb10A2,
            InternalFormat.Rgba8,
            InternalFormat.Rgba8,

            // Signed, and a fraction of a screen per frame -- so it needs a float format, and half
            // precision is ample for a quantity that is compared against a texel.
            InternalFormat.RG16f,
        ];

        for (int i = 0; i < _gbufferTextures.Length; i++)
        {
            if ((i == 2 && !description.HasBakedLighting)
                || (i == 4 && !description.HasMotionVectors))
            {
                continue;
            }

            PixelFormat pixelFormat = i == 4 ? PixelFormat.RG : PixelFormat.Rgba;
            PixelType pixelType = i == 1 ? PixelType.UnsignedInt2101010Rev : PixelType.Float;
            _gbufferTextures[i] = CreateTexture(formats[i], pixelFormat, pixelType, description);
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
            description.HasBakedLighting ? GLEnum.ColorAttachment2 : GLEnum.None,
            GLEnum.ColorAttachment3,
            description.HasMotionVectors ? GLEnum.ColorAttachment4 : GLEnum.None,
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
        _presentingOutput = false;

        if (_resolvedFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_resolvedFramebuffer);
            _resolvedFramebuffer = 0;
        }

        if (_resolvedTexture != 0)
        {
            _gl.DeleteTexture(_resolvedTexture);
            _resolvedTexture = 0;
        }

        if (_scratchFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_scratchFramebuffer);
            _scratchFramebuffer = 0;
        }

        if (_scratchTexture != 0)
        {
            _gl.DeleteTexture(_scratchTexture);
            _scratchTexture = 0;
        }

        for (int i = 0; i < 2; i++)
        {
            if (_bloomFramebuffers[i] != 0)
            {
                _gl.DeleteFramebuffer(_bloomFramebuffers[i]);
                _bloomFramebuffers[i] = 0;
            }

            if (_bloomTextures[i] != 0)
            {
                _gl.DeleteTexture(_bloomTextures[i]);
                _bloomTextures[i] = 0;
            }
        }

        if (_occlusionFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_occlusionFramebuffer);
            _occlusionFramebuffer = 0;
        }

        if (_occlusionSearchFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_occlusionSearchFramebuffer);
            _occlusionSearchFramebuffer = 0;
        }

        if (_occlusionTexture != 0)
        {
            _gl.DeleteTexture(_occlusionTexture);
            _occlusionTexture = 0;
        }

        if (_occlusionSearchTexture != 0)
        {
            _gl.DeleteTexture(_occlusionSearchTexture);
            _occlusionSearchTexture = 0;
        }

        _occlusionWidth = 0;
        _occlusionHeight = 0;

        if (_fogFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_fogFramebuffer);
            _fogFramebuffer = 0;
        }

        if (_fogTexture != 0)
        {
            _gl.DeleteTexture(_fogTexture);
            _fogTexture = 0;
        }

        _fogWidth = 0;
        _fogHeight = 0;

        if (_outputFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_outputFramebuffer);
            _outputFramebuffer = 0;
        }

        if (_outputTexture != 0)
        {
            _gl.DeleteTexture(_outputTexture);
            _outputTexture = 0;
        }

        if (_outputDepth != 0)
        {
            _gl.DeleteTexture(_outputDepth);
            _outputDepth = 0;
        }

        if (_foregroundDepth != 0)
        {
            _gl.DeleteRenderbuffer(_foregroundDepth);
            _foregroundDepth = 0;
        }

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
