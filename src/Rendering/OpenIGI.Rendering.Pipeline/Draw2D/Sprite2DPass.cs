using System.Runtime.InteropServices;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Passes;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Draw2D;

/// <summary>
/// Everything the frame draws in two dimensions: sprites, distance-field text and the loading
/// backdrop, over the RHI and no graphics API.
/// </summary>
/// <remarks>
/// <para>
/// The three move together because they share everything but a fragment shader: one vertex layout, one
/// geometry ring, one texture bind group per texture. Splitting them would mean three copies of the
/// cache and three rings, and a frame that draws a sprite, a line of text and a backdrop would pay for
/// all three.
/// </para>
/// <para>
/// This records into a pass the caller has already begun rather than beginning one itself. Which
/// attachment the HUD lands on is the frame's business, not this pass's, and on the web the same code
/// has to be able to record into the surface's own pass without knowing it.
/// </para>
/// <para>
/// The text and backdrop pipelines are built on first use. Most sessions draw neither: the backdrop
/// belongs to the loading panel, and building it up front would compile two shaders that a session
/// spent entirely in a level never runs.
/// </para>
/// </remarks>
public sealed class Sprite2DPass : IDisposable
{
    /// <summary>Where the distance-field fragment stage's push block starts: past the projection.</summary>
    private const int SdfPushOffset = 16;

    /// <summary>How many floats the loading backdrop's push block holds.</summary>
    private const int LoadingPushFloats = 16;

    private readonly IRhiDevice _device;
    private readonly ITextureBindingSource _textures;
    private readonly RhiTextureFormat _targetFormat;
    private readonly RhiTextureFormat _depthFormat;
    private readonly SpritePipelineSet _sprites;
    private readonly SpriteGeometryRing _geometry;
    private readonly TextureBindGroupCache _groups;
    private SpritePipelineSet? _text;
    private SpritePipelineSet? _layerSprites;
    private SpritePipelineSet? _layerText;
    private FullscreenPipeline? _backdrop;
    private float _projectionWidth = 1f;
    private float _projectionHeight = 1f;
    private int _frame;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="Sprite2DPass"/> class.</summary>
    /// <param name="device">The device everything here is made on.</param>
    /// <param name="textures">Where a handle is turned into something bindable.</param>
    /// <param name="targetFormat">The format of the attachment the 2D path draws into.</param>
    /// <param name="framesInFlight">How many geometry rings to keep.</param>
    /// <param name="retired">
    /// Where a group whose texture has gone goes, for a caller whose frames are in flight. See
    /// <see cref="RetiredResources"/>.
    /// </param>
    /// <param name="depthFormat">
    /// The depth attachment of the pass it draws into, or undefined for a pass with none. Nothing here
    /// tests depth; a pipeline simply has to declare what its pass carries.
    /// </param>
    public Sprite2DPass(
        IRhiDevice device,
        ITextureBindingSource textures,
        RhiTextureFormat targetFormat,
        int framesInFlight,
        RhiTextureFormat depthFormat = RhiTextureFormat.Undefined,
        RetiredResources? retired = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        _textures = textures;
        _targetFormat = targetFormat;
        _depthFormat = depthFormat;

        TextureLayout = TextureBindGroupCache.CreateLayout(device, "sprite2d");
        _groups = new TextureBindGroupCache(device, TextureLayout, retired);
        _geometry = new SpriteGeometryRing(device, framesInFlight);

        _sprites = new SpritePipelineSet(
            device,
            ShaderModuleId.Sprite2D,
            TextureLayout,
            targetFormat,
            [BlendMode.Opaque, BlendMode.AlphaBlend, BlendMode.Additive, BlendMode.Modulate2X],
            depthFormat);
    }

    /// <summary>Gets the layout every 2D texture group is built against.</summary>
    public IRhiBindGroupLayout TextureLayout { get; }

    /// <summary>Gets or sets a value indicating whether the draws land in an interface layer composited later.</summary>
    /// <remarks>
    /// Such a layer is laid over the picture by its coverage, so its additive draws must not add coverage of
    /// their own. See <see cref="SpritePipelineSet.LayerBlendFor"/>. Built the first time it is asked for.
    /// </remarks>
    public bool DrawsIntoLayer { get; set; }

    private SpritePipelineSet Sprites => DrawsIntoLayer
        ? _layerSprites ??= new SpritePipelineSet(
            _device,
            ShaderModuleId.Sprite2D,
            TextureLayout,
            _targetFormat,
            [BlendMode.Opaque, BlendMode.AlphaBlend, BlendMode.Additive, BlendMode.Modulate2X],
            _depthFormat,
            intoLayer: true)
        : _sprites;

    /// <summary>Starts a frame: empties its ring and fixes what the projection divides by.</summary>
    /// <param name="frameInFlight">Which ring this frame appends into.</param>
    /// <param name="width">The width in pixels the 2D coordinates are given in.</param>
    /// <param name="height">The height.</param>
    public void BeginFrame(int frameInFlight, float width, float height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _frame = frameInFlight;
        _geometry.Reset(frameInFlight);
        SetProjection(width, height);
    }

    /// <summary>Fixes what the projection divides by, without starting a frame.</summary>
    /// <param name="width">The width in pixels the 2D coordinates are given in.</param>
    /// <param name="height">The height.</param>
    /// <remarks>
    /// The game changes it mid-frame — the HUD is authored against the mode and a menu against its own
    /// canvas — so it is not only a frame's property.
    /// </remarks>
    public void SetProjection(float width, float height)
    {
        _projectionWidth = width;
        _projectionHeight = height;
    }

    /// <summary>Uploads what the frame appended. Must run before the frame's commands are submitted.</summary>
    public void EndFrame() => _geometry.Flush(_device, _frame);

    /// <summary>Records a run of textured triangles.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="vertices">The vertices, in pixels.</param>
    /// <param name="indices">The indices, addressing <paramref name="vertices"/> from zero.</param>
    /// <param name="texture">What to sample, or <see cref="TextureHandle.None"/> for flat colour.</param>
    /// <param name="blend">How it is blended over what is already there.</param>
    /// <exception cref="ArgumentException">The index count is not a multiple of three.</exception>
    public void DrawTriangles(
        IRhiRenderPassEncoder pass,
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle texture,
        BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(pass);
        RequireTriangles(indices);

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            return;
        }

        pass.SetPipeline(Sprites.ForBlend(blend));
        BindTexture(pass, texture);
        PushProjection(pass);

        Append(pass, vertices, indices);
    }

    /// <summary>Records a run of distance-field text.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="vertices">The vertices, in pixels.</param>
    /// <param name="indices">The indices, addressing <paramref name="vertices"/> from zero.</param>
    /// <param name="atlas">The glyph atlas.</param>
    /// <param name="parameters">How the glyphs are outlined, glowed and dissolved.</param>
    /// <param name="blend">How it is blended over what is already there.</param>
    /// <exception cref="ArgumentException">The index count is not a multiple of three.</exception>
    /// <remarks>
    /// The sprite path with the distance-field fragment stage in place of the plain one: the same
    /// vertex layout, the same ring, the same per-texture group.
    /// </remarks>
    public void DrawText(
        IRhiRenderPassEncoder pass,
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle atlas,
        in SdfTextParams parameters,
        BlendMode blend)
    {
        ArgumentNullException.ThrowIfNull(pass);
        RequireTriangles(indices);

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            return;
        }

        SpritePipelineSet text = DrawsIntoLayer
            ? _layerText ??= new SpritePipelineSet(
                _device,
                ShaderModuleId.SdfText,
                TextureLayout,
                _targetFormat,
                [BlendMode.Opaque, BlendMode.AlphaBlend, BlendMode.Additive],
                _depthFormat,
                intoLayer: true)
            : _text ??= new SpritePipelineSet(
                _device,
                ShaderModuleId.SdfText,
                TextureLayout,
                _targetFormat,
                [BlendMode.Opaque, BlendMode.AlphaBlend, BlendMode.Additive],
                _depthFormat);

        pass.SetPipeline(text.ForBlend(blend));
        BindTexture(pass, atlas);

        Span<float> push = stackalloc float[SdfPushOffset / sizeof(float) + 16];
        push.Clear();
        push[0] = _projectionWidth;
        push[1] = -_projectionHeight;

        WriteTextParameters(push[(SdfPushOffset / sizeof(float))..], parameters);
        pass.SetPushData(MemoryMarshal.AsBytes(push)[..text.PushDataBytes]);

        Append(pass, vertices, indices);
    }

    /// <summary>Records the loading backdrop over whatever the frame already holds.</summary>
    /// <param name="pass">The pass to record into.</param>
    /// <param name="texture">The picture behind it, when there is one.</param>
    /// <param name="settings">How it is blurred, desaturated, vignetted and swept.</param>
    public void DrawLoadingBackdrop(
        IRhiRenderPassEncoder pass,
        TextureHandle texture,
        in LoadingBackdropSettings settings)
    {
        ArgumentNullException.ThrowIfNull(pass);

        _backdrop ??= new FullscreenPipeline(
            _device,
            ShaderModuleId.Loading,
            TextureLayout,
            _targetFormat,
            RhiBlendState.AlphaBlend,
            depthFormat: _depthFormat,
            fragmentConstants: Mesh3DConstants.DisplayComposite(_targetFormat));

        RhiTextureBinding binding = _textures.Resolve(settings.HasPicture ? texture : TextureHandle.None);
        IRhiBindGroup group = _groups.Get(binding.Id, binding.View, binding.Sampler);

        Span<float> push = stackalloc float[LoadingPushFloats];
        push.Clear();
        push[0] = settings.TimeSeconds;
        push[1] = settings.Blur;
        push[2] = settings.Desaturation;
        push[3] = settings.Vignette;
        push[4] = settings.Zoom;
        push[5] = settings.Grain;
        push[6] = settings.Scanlines;
        push[7] = settings.Opacity;
        push[8] = settings.Letterbox;
        push[9] = settings.HasPicture ? 1f : 0f;
        push[10] = settings.AspectRatio;
        push[11] = settings.Sweep;

        _backdrop.Draw(pass, group, MemoryMarshal.AsBytes(push)[.._backdrop.PushDataBytes]);
    }

    /// <summary>Releases the group of a destroyed texture.</summary>
    /// <param name="textureId">The texture's handle identifier.</param>
    public void ForgetTexture(uint textureId) => _groups.Forget(textureId);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _backdrop?.Dispose();
        _layerText?.Dispose();
        _layerSprites?.Dispose();
        _text?.Dispose();
        _sprites.Dispose();
        _geometry.Dispose();
        _groups.Dispose();
        TextureLayout.Dispose();
    }

    private static void RequireTriangles(ReadOnlySpan<ushort> indices)
    {
        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException("The index count must be a multiple of three.", nameof(indices));
        }
    }

    /// <summary>Lays the text parameters out as the fragment stage's four vectors.</summary>
    private static void WriteTextParameters(Span<float> push, in SdfTextParams parameters)
    {
        push[0] = parameters.PxRange;
        push[1] = parameters.Dissolve;
        push[2] = parameters.DissolveSeed;
        push[3] = 0f;
        push[4] = parameters.OutlineColor.R / 255f;
        push[5] = parameters.OutlineColor.G / 255f;
        push[6] = parameters.OutlineColor.B / 255f;
        push[7] = parameters.OutlineWidth;
        push[8] = parameters.GlowColor.R / 255f;
        push[9] = parameters.GlowColor.G / 255f;
        push[10] = parameters.GlowColor.B / 255f;
        push[11] = parameters.GlowIntensity;
        push[12] = parameters.GlowWidth;
    }

    private void BindTexture(IRhiRenderPassEncoder pass, TextureHandle texture)
    {
        RhiTextureBinding binding = _textures.Resolve(texture);
        pass.SetBindGroup(0, _groups.Get(binding.Id, binding.View, binding.Sampler));
    }

    /// <summary>Hands the vertex stage the projection, with the sign that says which way Y runs.</summary>
    /// <remarks>
    /// Negative, because the RHI states clip space the way WebGPU does — Y upwards — and that is the one
    /// of the two that a viewport cannot change. Vulkan is made to agree by its viewport; the shader is
    /// told by this sign. See Common/Sprite2DVertex.slang.
    /// </remarks>
    private void PushProjection(IRhiRenderPassEncoder pass)
    {
        Span<float> projection = [_projectionWidth, -_projectionHeight];
        pass.SetPushData(MemoryMarshal.AsBytes(projection));
    }

    private void Append(
        IRhiRenderPassEncoder pass,
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices)
    {
        _geometry.Append(_frame, vertices, indices, out int baseVertex, out int firstIndex);

        pass.SetVertexBuffer(0, _geometry.VertexBuffer(_frame));
        pass.SetIndexBuffer(_geometry.IndexBuffer(_frame), RhiIndexFormat.Uint16);
        pass.DrawIndexed(indices.Length, 1, firstIndex, baseVertex);
    }
}
