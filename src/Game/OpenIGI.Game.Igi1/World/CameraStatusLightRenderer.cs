using OpenIGI.Presentation;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi1.World;

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
    public const string SpriteArchive = "LOCAL:common/sprites/sprites.res";

    /// <summary>The exact sprite requested by <c>SCameraLens</c>'s type registration.</summary>
    public const string SpriteName = "alarmlight.spr";

    private const int MaximumCameras = 128;
    private const int VerticesPerCamera = 4;
    private const int IndicesPerCamera = 6;
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
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumCameras * VerticesPerCamera];
    private SpriteResource? _sprite;
    private SpriteFrame _frame;
    private float _u0;
    private float _v0;
    private float _u1;
    private float _v1;
    private MeshHandle _mesh = MeshHandle.None;
    private int _lastUsed;
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

            IresArchive archive = IresArchive.Read(fileSystem.ReadAllBytes(SpriteArchive));
            IresEntry? entry = archive.Entries.FirstOrDefault(
                candidate => candidate.Kind == IresEntryKind.Body
                    && Path.GetFileName(candidate.Name).Equals(SpriteName, StringComparison.OrdinalIgnoreCase));
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
            _u0 = _frame.U0;
            _v0 = _frame.V0;
            _u1 = _frame.U1;
            _v1 = _frame.V1;
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
            if (used + VerticesPerCamera > _vertices.Length || camera.Lamp == SecurityCameraLamp.Off)
            {
                continue;
            }

            Vector3d centre = (camera.PresentedLampPositionUnits(tickFraction, smooth)
                / WorldScene.UnitsPerMetre) - eye.Position;
            double distance = centre.Length;
            if (!(distance > 1e-6))
            {
                continue;
            }

            Vector3d toEye = centre / -distance;
            Vector3d reference = Math.Abs(toEye.Z) < 0.9
                ? new Vector3d(0, 0, 1)
                : new Vector3d(1, 0, 0);
            Vector3d rightD = Cross(reference, toEye);
            double rightLength = rightD.Length;
            if (!(rightLength > 1e-9))
            {
                continue;
            }

            rightD /= rightLength;
            Vector3d upD = Cross(toEye, rightD);
            centre += toEye * LensSurfaceBiasMetres;
            float half = HalfSize(camera);
            Vector3f right = new(
                (float)(rightD.X * half),
                (float)(rightD.Y * half),
                (float)(rightD.Z * half));
            Vector3f up = new(
                (float)(upD.X * half),
                (float)(upD.Y * half),
                (float)(upD.Z * half));
            Vector3f centreF = new((float)centre.X, (float)centre.Y, (float)centre.Z);
            Vector3f normal = new((float)toEye.X, (float)toEye.Y, (float)toEye.Z);
            Bgra32 colour = Colour(camera);

            _vertices[used] = new Vertex3D(centreF - right - up, normal, _u0, _v0, colour: colour);
            _vertices[used + 1] = new Vertex3D(centreF + right - up, normal, _u1, _v0, colour: colour);
            _vertices[used + 2] = new Vertex3D(centreF + right + up, normal, _u1, _v1, colour: colour);
            _vertices[used + 3] = new Vertex3D(centreF - right + up, normal, _u0, _v1, colour: colour);
            used += VerticesPerCamera;
        }

        if (used == 0)
        {
            return;
        }
        if (_lastUsed > used)
        {
            Array.Clear(_vertices, used, _lastUsed - used);
        }
        _lastUsed = used;

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestOnly);
        _device.SetMaterialOverride(GlowMaterial);

        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = _device.CreateAnimatedMesh(_vertices, BuildIndices());
            }
            _device.UpdateAnimatedMesh(_mesh, _vertices);
            _device.ClearPoseInterpolation();
            _device.DrawMesh(
                _mesh,
                0,
                (used / VerticesPerCamera) * IndicesPerCamera,
                eye.CreateModelView(eye.Position, Matrix3f.Identity),
                _sprite.Textures[_frame.TextureIndex],
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
        _sprite?.Dispose();
        _sprite = null;
        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
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

    private static Vector3d Cross(Vector3d left, Vector3d right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static ushort[] BuildIndices()
    {
        ushort[] indices = new ushort[MaximumCameras * IndicesPerCamera];
        for (int camera = 0; camera < MaximumCameras; camera++)
        {
            int vertex = camera * VerticesPerCamera;
            int index = camera * IndicesPerCamera;
            indices[index] = (ushort)vertex;
            indices[index + 1] = (ushort)(vertex + 1);
            indices[index + 2] = (ushort)(vertex + 2);
            indices[index + 3] = (ushort)vertex;
            indices[index + 4] = (ushort)(vertex + 2);
            indices[index + 5] = (ushort)(vertex + 3);
        }
        return indices;
    }
}
