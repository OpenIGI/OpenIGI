using OpenIGI.Core.Diagnostics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// The sun's cascaded shadow map: one depth texture array, one layer per cascade.
/// </summary>
/// <remarks>
/// <para>
/// An array rather than several textures, because the lighting pass has to pick a cascade per pixel
/// and choosing between separate samplers would mean branching into four texture units. With an array
/// the cascade is just the third coordinate.
/// </para>
/// <para>
/// The texture is set up for <em>hardware comparison</em>: sampling returns how much of the
/// neighbourhood passes the depth test rather than the stored depth, so a single bilinear fetch is
/// already four shadow tests filtered together. That is what makes 3×3 percentage-closer filtering
/// cost nine fetches instead of thirty-six, and it is why the sampler is declared
/// <c>sampler2DArrayShadow</c> in the shader.
/// </para>
/// </remarks>
internal sealed class GLShadowMap : IDisposable
{
    private const string LogCategory = "ShadowMap";

    private readonly GL _gl;
    private readonly IIgiLogger _logger;

    private uint _texture;
    private uint _framebuffer;
    private int _resolution;
    private int _layers;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GLShadowMap"/> class.</summary>
    /// <param name="gl">The GL binding.</param>
    /// <param name="logger">Where to report a map that could not be created.</param>
    public GLShadowMap(GL gl, IIgiLogger logger)
    {
        _gl = gl;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the map exists and can be rendered into.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Gets the depth texture array.</summary>
    public uint Texture => _texture;

    /// <summary>Gets the side length in texels of each cascade.</summary>
    public int Resolution => _resolution;

    /// <summary>Gets how many cascades the map holds.</summary>
    public int Layers => _layers;

    /// <summary>Builds or rebuilds the map.</summary>
    /// <param name="resolution">The side length in texels of each cascade.</param>
    /// <param name="layers">How many cascades.</param>
    /// <returns><see langword="true"/> when the map is ready.</returns>
    public bool Configure(int resolution, int layers)
    {
        if (resolution <= 0 || layers <= 0)
        {
            Release();
            return false;
        }

        if (IsReady && _resolution == resolution && _layers == layers)
        {
            return true;
        }

        Release();

        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, _texture);

        unsafe
        {
            _gl.TexImage3D(
                TextureTarget.Texture2DArray,
                0,
                (int)InternalFormat.DepthComponent32f,
                (uint)resolution,
                (uint)resolution,
                (uint)layers,
                0,
                PixelFormat.DepthComponent,
                PixelType.Float,
                null);
        }

        // Comparison mode: a fetch returns the fraction of the neighbourhood that passes, not the
        // depth. Linear filtering on top of that is what gives four filtered tests per fetch.
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // Clamped to a border of "fully lit". Anything outside a cascade's box must not be shadowed by
        // the edge texel smeared outwards, which is what clamp-to-edge would do and what produces the
        // long streaks trailing off the side of a badly set up cascade.
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        Span<float> border = [1f, 1f, 1f, 1f];
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureBorderColor, border);

        _framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, _texture, 0, 0);

        // A depth-only framebuffer has to say it draws no colour, or it is incomplete.
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);

        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (status != GLEnum.FramebufferComplete)
        {
            _logger.Warning(LogCategory, $"The shadow map is incomplete ({status}); shadows are off.");
            Release();
            return false;
        }

        _resolution = resolution;
        _layers = layers;
        IsReady = true;
        _logger.Information(LogCategory, $"Shadow map: {layers} cascade(s) at {resolution}x{resolution}.");
        return true;
    }

    /// <summary>Makes one cascade the render target and clears it.</summary>
    /// <param name="cascade">The zero-based cascade index.</param>
    public void BindCascade(int cascade)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTextureLayer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            _texture,
            0,
            Math.Clamp(cascade, 0, _layers - 1));

        _gl.Viewport(0, 0, (uint)_resolution, (uint)_resolution);
        _gl.DepthMask(true);
        _gl.ClearDepth(1.0);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
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
        IsReady = false;
        _resolution = 0;
        _layers = 0;

        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }

        if (_texture != 0)
        {
            _gl.DeleteTexture(_texture);
            _texture = 0;
        }
    }
}
