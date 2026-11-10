using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Draws the glows a level's models carry, as the original draws them: a soft sprite blooming over each lamp.
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>Glow_DrawSorted</c>. Every glow of every model drawn is a
/// camera-facing quad of <c>glowsprite.tex</c> in the glow's colour, added to the picture: texture times
/// colour, source alpha and one, no lighting, no fog, depth tested and never written
/// (<c>Glow_SetStates</c>).
/// </para>
/// <para>
/// <b>The sprite is drawn nearer than its lamp.</b> The quad is moved toward the eye by its own radius and
/// shrunk by the same share, so it covers exactly the screen it would have covered and is depth tested a
/// radius in front. That is what lets a bulb's bloom spread over the fitting and the wall behind it rather
/// than being cut off where they meet. It never comes nearer than a tenth of a metre from the eye; nearer
/// than that it is drawn where it stands.
/// </para>
/// <para>
/// The original look, only. The remastered ones light the lamp itself — its glass emits and its light is
/// cast — and a sprite over that would be the same lamp twice.
/// </para>
/// </remarks>
public sealed class ModelGlowRenderer : IDisposable
{
    /// <summary>The sprite every glow is drawn with.</summary>
    public const string TextureName = "LOCAL:common/textures/glowsprite.tex";

    /// <summary>The most glows one frame draws.</summary>
    public const int MaximumGlows = 2048;

    /// <summary>The nearest the eye a pulled-forward sprite is drawn, in metres (409.6 units).</summary>
    public const double NearLimitMetres = 409.6 / WorldScene.UnitsPerMetre;

    private const string LogCategory = "Glow";

    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly GlowQuadBatch _batch = new(MaximumGlows);
    private TextureHandle _texture = TextureHandle.None;
    private bool _asked;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ModelGlowRenderer"/> class.</summary>
    /// <param name="device">The device to draw with.</param>
    /// <param name="resources">Where the sprite is read from.</param>
    /// <param name="logger">Where a missing sprite is reported.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public ModelGlowRenderer(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>How far along the way from the eye a glow's sprite is drawn.</summary>
    /// <param name="offsetMetres">The glow's place, from the eye.</param>
    /// <param name="radiusMetres">The glow's radius.</param>
    /// <returns>The share of the distance the sprite is drawn at, which is also the share of its size it keeps.</returns>
    public static double Pull(Vector3d offsetMetres, double radiusMetres)
    {
        double distance = offsetMetres.Length;

        if (!(distance >= NearLimitMetres))
        {
            return 1.0;
        }

        double pull = radiusMetres;

        if (NearLimitMetres + pull > distance)
        {
            pull = distance - NearLimitMetres;
        }

        return (distance - pull) / distance;
    }

    /// <summary>Draws a frame's glows.</summary>
    /// <param name="glows">The glows of the models the eye's picture drew.</param>
    /// <param name="eye">The eye.</param>
    /// <param name="projection">The projection to draw under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="glows"/> is <see langword="null"/>.</exception>
    public void Draw(IReadOnlyList<WorldGlow> glows, in Camera3D eye, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(glows);

        if (glows.Count == 0 || !Load())
        {
            return;
        }

        int used = 0;

        foreach (WorldGlow glow in glows)
        {
            Vector3d point = glow.PositionUnits / WorldScene.UnitsPerMetre;
            double radius = glow.HalfSizeUnits / WorldScene.UnitsPerMetre;
            Vector3d offset = point - eye.Position;
            double share = Pull(offset, radius);

            used = _batch.Add(used, eye.Position + (offset * share), (float)(radius * share), glow.Colour, 0.0, eye);
        }

        if (used == 0)
        {
            return;
        }

        _batch.Trim(used);
        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        try
        {
            if (!_batch.Mesh.IsValid)
            {
                _batch.Mesh = _device.CreateAnimatedMesh(_batch.Vertices, _batch.BuildIndices());
            }

            _device.UpdateAnimatedMesh(_batch.Mesh, _batch.Vertices.AsSpan(0, used));
            _device.ClearPoseInterpolation();
            _device.DrawMesh(
                _batch.Mesh,
                0,
                (used / GlowQuadBatch.VerticesPerQuad) * GlowQuadBatch.IndicesPerQuad,
                eye.CreateModelView(eye.Position, Matrix3f.Identity),
                _texture,
                TextureHandle.None,
                BlendMode.Additive);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
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

        if (_batch.Mesh.IsValid)
        {
            _device.DestroyMesh(_batch.Mesh);
            _batch.Mesh = MeshHandle.None;
        }

        if (_texture.IsValid)
        {
            _device.DestroyTexture(_texture);
            _texture = TextureHandle.None;
        }
    }

    /// <summary>Reads the sprite the first time a glow is drawn; a game without one draws no glows.</summary>
    private bool Load()
    {
        if (_asked)
        {
            return _texture.IsValid;
        }

        _asked = true;

        if (!_resources.TryLoad(TextureName, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(LogCategory, $"'{TextureName}' is absent; the models' glows will not draw.");
            return false;
        }

        try
        {
            TextureImage image = TexFile.Load(bytes.Span);
            _texture = _device.CreateTexture(
                new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Clamp),
                image.Pixels);
        }
        catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"'{TextureName}' did not load: {ex.Message}");
        }

        return _texture.IsValid;
    }
}
