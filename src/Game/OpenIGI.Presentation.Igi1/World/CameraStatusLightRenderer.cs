using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws the pulsing green/red lamp attached to every retail security-camera lens.</summary>
/// <remarks>
/// <c>SCameraLens</c>'s render callback (<c>0x442A70</c>) places <c>alarmlight.spr</c> at head magic
/// vertex 0. It is green while powered and clear, red during both confirmation and detection, and
/// flips size every eight 30 Hz ticks. This renderer preserves those authored positions, colours and
/// half-sizes as emissive camera-facing quads.
/// </remarks>
internal sealed class CameraStatusLightRenderer : ICameraStatusLightPresenter
{
    /// <summary>The global archive used by retail's by-name sprite loader.</summary>
    public const string SpriteArchive = MissionLoader.CommonSpritesArchive;

    /// <summary>The exact sprite requested by <c>SCameraLens</c>'s type registration.</summary>
    public const string SpriteName = "alarmlight.spr";

    private const int MaximumCameras = 128;
    private const float RedSmallHalfSize = 512f / (float)WorldScene.UnitsPerMetre;
    private const float RedLargeHalfSize = 1433.6f / (float)WorldScene.UnitsPerMetre;
    private const float GreenSmallHalfSize = 307.2f / (float)WorldScene.UnitsPerMetre;
    private const float GreenLargeHalfSize = 819.2f / (float)WorldScene.UnitsPerMetre;

    // Retail projects the magic-vertex point into a 2D sprite queue, so the lens housing cannot clip
    // the middle of its own glow. Keep world depth testing (so walls still hide it), but move the quad
    // slightly toward the eye along the identical projection ray. This changes depth only, never its
    // screen position.
    private const double LensSurfaceBiasMetres = 0.04;

    private static readonly SurfaceMaterial GlowMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly GlowQuadBatch _lamps = new(MaximumCameras);
    private readonly GlowQuadBatch _glows = new(MaximumCameras);
    private SpriteResource? _sprite;
    private SpriteFrame _frame;
    private bool _disposed;

    /// <summary>Initializes a camera-lamp renderer.</summary>
    public CameraStatusLightRenderer(IGraphicsDevice device, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether the retail sprite decoded and uploaded successfully.</summary>
    public bool IsReady => _sprite is not null;

    /// <summary>Loads retail <c>alarmlight.spr</c> from the common sprite archive.</summary>
    public bool Load(VirtualFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            if (!fileSystem.Exists(SpriteArchive))
            {
                _logger.Warning("Camera", $"No camera-lamp archive at {SpriteArchive}.");
                return false;
            }

            IresArchive archive = fileSystem.ReadArchive(SpriteArchive);
            IresEntry? entry = archive.FindBody(SpriteName);
            if (entry is null)
            {
                _logger.Warning("Camera", $"'{SpriteName}' is missing from {SpriteArchive}.");
                return false;
            }

            SpriteAsset asset = SpriteLoader.Load(entry.Data, SpriteName);
            if (asset.Frames.Count == 0 || asset.Textures.Count == 0)
            {
                _logger.Warning("Camera", $"'{SpriteName}' has no drawable frame.");
                return false;
            }

            _frame = asset.Frames[0];
            if (_frame.IsEmpty)
            {
                return false;
            }

            // SpriteFrame carries normalized texture coordinates already. alarmlight.spr is a 16x16
            // texture with a 0..1 frame; dividing those values by the dimensions samples only its
            // transparent top-left corner and makes every camera lamp disappear.
            foreach (GlowQuadBatch batch in new[] { _lamps, _glows })
            {
                batch.U0 = _frame.U0;
                batch.V0 = _frame.V0;
                batch.U1 = _frame.U1;
                batch.V1 = _frame.V1;
            }
            _sprite = SpriteResource.Upload(_device, asset, TextureFilter.Linear);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or GraphicsDeviceException)
        {
            _logger.Warning("Camera", $"Could not load '{SpriteName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws all powered, intact lamps in one additive batch.</summary>
    public void Draw(
        IReadOnlyList<SecurityCamera> cameras,
        in Camera3D eye,
        in Matrix4f projection,
        float tickFraction = 1f,
        bool smooth = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cameras);
        if (_sprite is null)
        {
            return;
        }

        int used = 0;
        foreach (SecurityCamera camera in cameras)
        {
            if (camera.Lamp != SecurityCameraLamp.Off)
            {
                used = _lamps.Add(
                    used,
                    camera.PresentedLampPositionUnits(tickFraction, smooth) / WorldScene.UnitsPerMetre,
                    HalfSize(camera),
                    Colour(camera),
                    LensSurfaceBiasMetres,
                    eye);
            }
        }

        Flush(_lamps, used, eye, projection);
    }

    /// <inheritdoc/>
    /// <remarks>An alarm light's reflector flare (<c>0x439F10</c>) is this sprite too, a quad 6144 units each way of its point.</remarks>
    public void DrawGlows(IReadOnlyList<WorldGlow> glows, in Camera3D eye, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(glows);
        if (_sprite is null || glows.Count == 0)
        {
            return;
        }

        int used = 0;
        foreach (WorldGlow glow in glows)
        {
            used = _glows.Add(
                used,
                glow.PositionUnits / WorldScene.UnitsPerMetre,
                glow.HalfSizeUnits / (float)WorldScene.UnitsPerMetre,
                glow.Colour,
                0.0,
                eye);
        }

        Flush(_glows, used, eye, projection);
    }

    private void Flush(GlowQuadBatch batch, int used, in Camera3D eye, in Matrix4f projection)
    {
        if (used == 0)
        {
            return;
        }

        batch.Trim(used);
        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        try
        {
            if (!batch.Mesh.IsValid)
            {
                batch.Mesh = _device.CreateAnimatedMesh(batch.Vertices, batch.BuildIndices());
            }

            _device.UpdateAnimatedMesh(batch.Mesh, batch.Vertices.AsSpan(0, used));
            _device.ClearPoseInterpolation();
            _device.DrawMesh(
                batch.Mesh,
                0,
                (used / GlowQuadBatch.VerticesPerQuad) * GlowQuadBatch.IndicesPerQuad,
                eye.CreateModelView(eye.Position, Matrix3f.Identity),
                _sprite!.Textures[_frame.TextureIndex],
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _sprite?.Dispose();
        _sprite = null;
        foreach (GlowQuadBatch batch in new[] { _lamps, _glows })
        {
            if (batch.Mesh.IsValid)
            {
                _device.DestroyMesh(batch.Mesh);
                batch.Mesh = MeshHandle.None;
            }
        }
    }

    private static float HalfSize(SecurityCamera camera) => (camera.Lamp, camera.IsLargeLampPulse) switch
    {
        (SecurityCameraLamp.Red, true) => RedLargeHalfSize,
        (SecurityCameraLamp.Red, false) => RedSmallHalfSize,
        (SecurityCameraLamp.Green, true) => GreenLargeHalfSize,
        _ => GreenSmallHalfSize,
    };

    private static Bgra32 Colour(SecurityCamera camera) => camera.Lamp switch
    {
        SecurityCameraLamp.Red => new Bgra32(0, 0, 128, 255),
        SecurityCameraLamp.Green when camera.IsLargeLampPulse => new Bgra32(0, 128, 0, 255),
        SecurityCameraLamp.Green => new Bgra32(0, 64, 0, 255),
        _ => default,
    };
}
