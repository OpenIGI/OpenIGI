using OpenIGI.Core.Diagnostics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// The textures a model's captured surfaces live in, and the framebuffer they are rendered through.
/// </summary>
/// <remarks>
/// <para>
/// Three targets and a depth buffer, all one size. Every card in the level is rendered into its own
/// rectangle of these, once, at load — so the atlas is written for a fraction of a second and read for
/// the rest of the level.
/// </para>
/// <para>
/// <b>The depth buffer is a renderbuffer rather than a texture.</b> Nothing reads it: it exists so that
/// a card capturing a model from one side gets the near surface rather than whichever triangle happened
/// to be drawn last. A texture would be readable and would cost the same memory for a capability no
/// consumer wants.
/// </para>
/// <para>
/// <b>Sampled with a linear filter and clamped.</b> Linear because a ray lands between texels and the
/// hardware's blend is what a card lookup wants; clamped because a page is a window onto a surface and
/// the space past its edge is the gutter, not the far side of the model.
/// </para>
/// </remarks>
internal sealed class GLCardAtlas : IDisposable
{
    private const string LogCategory = "SurfaceCache";

    private readonly GL _gl;
    private readonly IIgiLogger _logger;
    private readonly uint[] _textures = [0, 0, 0];
    private uint _framebuffer;
    private uint _depth;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GLCardAtlas"/> class.</summary>
    /// <param name="gl">The context to allocate through.</param>
    /// <param name="logger">Where to report what was allocated and anything incomplete.</param>
    public GLCardAtlas(GL gl, IIgiLogger logger)
    {
        _gl = gl;
        _logger = logger;
    }

    /// <summary>Gets the atlas width in texels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the atlas height in texels.</summary>
    public int Height { get; private set; }

    /// <summary>Gets a value indicating whether the atlas is allocated and complete.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Gets the texture holding each card's surface colour.</summary>
    public uint AlbedoTexture => _textures[0];

    /// <summary>Gets the texture holding each card's model-space normal.</summary>
    public uint NormalTexture => _textures[1];

    /// <summary>Gets the texture holding each card's roughness, metallic and emissive.</summary>
    public uint MaterialTexture => _textures[2];

    /// <summary>Allocates the atlas, replacing anything already held.</summary>
    /// <param name="width">The atlas width in texels.</param>
    /// <param name="height">The atlas height in texels.</param>
    /// <returns><see langword="true"/> when the atlas is complete and can be rendered into.</returns>
    public bool Configure(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (IsReady && Width == width && Height == height)
        {
            return true;
        }

        Release();

        Width = width;
        Height = height;

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        // Albedo is eight bits a channel because it is a colour; so is the material target, whose three
        // values are all fractions. The normal gets ten bits a channel, and that is not generosity: a
        // card's normal is read as a DIRECTION by rays arriving from anywhere, and eight bits quantises
        // a hemisphere coarsely enough that a flat wall bounces light in visible bands.
        InternalFormat[] formats =
        [
            InternalFormat.Rgba8,
            InternalFormat.Rgb10A2,
            InternalFormat.Rgba8,
        ];

        for (int i = 0; i < _textures.Length; i++)
        {
            _textures[i] = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _textures[i]);

            unsafe
            {
                _gl.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    (int)formats[i],
                    (uint)width,
                    (uint)height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    null);
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0 + i,
                TextureTarget.Texture2D,
                _textures[i],
                0);
        }

        _depth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer,
            _depth);

        Span<GLEnum> buffers =
        [
            GLEnum.ColorAttachment0,
            GLEnum.ColorAttachment1,
            GLEnum.ColorAttachment2,
        ];

        _gl.DrawBuffers(buffers);

        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);

        if (status != GLEnum.FramebufferComplete)
        {
            _logger.Error(LogCategory, $"The card atlas is incomplete: {status}.");
            Release();
            return false;
        }

        IsReady = true;
        _logger.Information(
            LogCategory, $"Card atlas: {width}x{height}, albedo, normal and material.");

        return true;
    }

    /// <summary>Binds the atlas as the render target and clears it.</summary>
    /// <remarks>
    /// Cleared once for the whole capture rather than per card. The gutters between pages are never
    /// drawn into, so without this they hold whatever the driver allocated — and a linear fetch near a
    /// page's edge reaches into them.
    /// </remarks>
    public void BindForCapture()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.Disable(EnableCap.ScissorTest);

        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>Restricts drawing to one card's page.</summary>
    /// <param name="x">The page's left edge in texels.</param>
    /// <param name="y">The page's top edge in texels.</param>
    /// <param name="width">The page's width in texels.</param>
    /// <param name="height">The page's height in texels.</param>
    /// <remarks>
    /// The scissor is set as well as the viewport, and both are needed. The viewport maps clip space
    /// onto the page; the scissor is what stops a triangle that reaches past the card's own rectangle
    /// from being drawn into the page beside it — geometry outside the orthographic box is clipped in
    /// depth, but geometry outside it sideways is not.
    /// </remarks>
    public void BindPage(int x, int y, int width, int height)
    {
        _gl.Viewport(x, y, (uint)width, (uint)height);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x, y, (uint)width, (uint)height);
    }

    /// <summary>Finishes the capture and returns to the back buffer.</summary>
    public void EndCapture()
    {
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
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

    private void Release()
    {
        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        if (_depth != 0)
        {
            _gl.DeleteRenderbuffer(_depth);
            _depth = 0;
        }

        for (int i = 0; i < _textures.Length; i++)
        {
            if (_textures[i] != 0)
            {
                _gl.DeleteTexture(_textures[i]);
                _textures[i] = 0;
            }
        }

        IsReady = false;
        Width = 0;
        Height = 0;
    }
}
