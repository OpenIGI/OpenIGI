using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Draws the retail <c>mapcomputer.mef</c> as the computer presentation.</summary>
/// <remarks>
/// The model is a thin 4:3 panel. Its authored texture supplies the SIMOTECH casing and controls while
/// the computer HUD, rendered afterwards, supplies the live picture inside the display aperture. It is
/// camera-relative rather than a weapon-bone model: treating it as a gun is what produced the diagonal
/// black slab in the first implementation.
/// </remarks>
internal sealed class MapComputerDeviceRenderer : IDisposable
{
    private const string LogCategory = "Computer";
    private const string ModelName = "mapcomputer";
    private const float DefaultWidthMetres = 0.5537f;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);

    private MeshGeometry? _geometry;
    private MeshHandle _mesh = MeshHandle.None;
    private LoadedPack? _pack;
    private MtpInstance _instance;
    private bool _hasInstance;
    private bool _disposed;
    private float _widthMetres = DefaultWidthMetres;

    public MapComputerDeviceRenderer(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger logger)
    {
        _device = device;
        _resources = resources;
        _logger = logger;
    }

    /// <summary>Gets whether the common pack supplied the retail device model.</summary>
    public bool IsReady => _geometry is not null && _hasInstance;

    /// <summary>Loads the model and material binding authored for the map computer.</summary>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (!string.Equals(instance.Name, ModelName, StringComparison.OrdinalIgnoreCase)
                    || !pack.TryGetModel(instance, out MefModel? model)
                    || model is null)
                {
                    continue;
                }

                _geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
                if (_geometry is null)
                {
                    _logger.Warning(LogCategory, $"'{ModelName}' has no drawable geometry.");
                    return false;
                }

                _widthMetres = Math.Max(
                    _geometry.Vertices.Max(vertex => vertex.Position.X) - _geometry.Vertices.Min(vertex => vertex.Position.X),
                    float.Epsilon);

                _pack = pack;
                _instance = instance;
                _hasInstance = true;
                _logger.Information(LogCategory, $"Loaded retail device '{ModelName}' ({_geometry.Subsets.Length} subset(s)).");
                return true;
            }
        }

        _logger.Warning(LogCategory, $"The mission's packs do not bind the retail device '{ModelName}'.");
        return false;
    }

    /// <summary>Draws the device square to the camera at the requested horizontal screen coverage.</summary>
    /// <param name="camera">The active first-person camera.</param>
    /// <param name="projection">The narrow presentation projection.</param>
    /// <param name="horizontalHalfTangent">The half-width of the view frustum at one metre.</param>
    /// <param name="horizontalCoverage">The fraction of the presentation occupied by the 4:3 device.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float horizontalHalfTangent, float horizontalCoverage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_geometry is null || _pack is null || !_hasInstance)
        {
            return;
        }

        if (!_mesh.IsValid)
        {
            try
            {
                _mesh = _device.CreateMesh(_geometry.Vertices, _geometry.Indices);
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"The map computer did not upload: {ex.Message}");
                return;
            }
        }

        // The mesh's outside housing deliberately overhangs the display: at retail's 4:3 presentation
        // its outer side and bottom are cropped while the aperture fills the 91% × 85.25% HUD viewport.
        // Coverage is consequently allowed beyond the display width rather than being capped at one.
        float coverage = Math.Clamp(horizontalCoverage, 0.01f, 4f);
        float halfTangent = Math.Max(horizontalHalfTangent, 0.001f);
        float distance = _widthMetres / (2f * coverage * halfTangent);

        // CreateModelView multiplies the camera orientation by the model orientation. Its inverse
        // therefore gives this flat asset a stable, front-on view-space orientation at any player yaw.
        // The position conversion goes in the other direction: Camera3D transforms a world offset into
        // view space with Orientation.Transposed(), so Orientation maps the requested view offset back
        // to the world. Using the transposed matrix here placed the panel beside or behind the player.
        Matrix3f orientation = camera.Orientation.Transposed();
        Vector3d position = camera.Position + camera.Orientation.Transform(new Vector3d(0d, 0d, distance));

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);
        Matrix4f modelView = camera.CreateModelView(position, orientation);

        foreach (MeshSubset subset in _geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial && _pack.TryGetTextureName(_instance, subset.MaterialSlot, out string texture))
            {
                diffuse = GetTexture(texture);
            }

            _device.SetMaterialOverride(SurfaceMaterial.Default);
            _device.DrawMesh(
                _mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
        }

        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
        foreach (TextureHandle texture in _textures.Values)
        {
            _device.DestroyTexture(texture);
        }

        _textures.Clear();
    }

    private void Release()
    {
        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
        }

        _mesh = MeshHandle.None;
        _geometry = null;
        _pack = null;
        _instance = default;
        _hasInstance = false;
    }

    private TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        if (_resources.TryLoad(LoadedPack.TextureResourceName(name), out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
                    image.Pixels);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"The device texture '{name}' did not load: {ex.Message}");
            }
        }
        else
        {
            _logger.Warning(LogCategory, $"The retail device texture '{name}' is not registered.");
        }

        _textures[name] = handle;
        return handle;
    }
}
