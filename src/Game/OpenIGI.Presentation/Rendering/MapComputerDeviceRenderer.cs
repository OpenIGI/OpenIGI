using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
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
public sealed class MapComputerDeviceRenderer : IDisposable
{
    private const string LogCategory = "Computer";
    private const string ModelName = "mapcomputer";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private TexTextureCache? _textureCache;

    private MeshGeometry? _geometry;
    private MeshHandle _mesh = MeshHandle.None;
    private LoadedPack? _pack;
    private MtpInstance _instance;
    private bool _hasInstance;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MapComputerDeviceRenderer"/> class.</summary>
    /// <param name="device">The device the model is uploaded to and drawn with.</param>
    /// <param name="resources">The resources the common pack's device model is read from.</param>
    /// <param name="logger">Told when the model does not load.</param>
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

    /// <summary>How far ahead of the eye the device is held, metres (<c>0x46BD50</c>: 532.48 units).</summary>
    public const double HeldForward = 532.48 / WorldScene.UnitsPerMetre;

    /// <summary>How far below the eye, metres (40.96 units).</summary>
    public const double HeldDown = 40.96 / WorldScene.UnitsPerMetre;

    /// <summary>Draws the device where the player holds it (<c>0x46BD50</c>): ahead of the eye and a little below, facing it.</summary>
    /// <param name="camera">The player's eye.</param>
    /// <param name="projection">The eye's projection.</param>
    /// <param name="sun">The level's fixed-function light, which lights the model as it lights any without a bake; or none.</param>
    public void DrawHeld(in Camera3D camera, in Matrix4f projection, FixedFunctionSun? sun)
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

        // The eye's matrix times RotX(pi/2), as 0x46BD50 builds it: the camera's rows are its right, up and forward, so the
        // model's X goes to the right, its Y up and its Z back toward the eye.
        Matrix3f o = camera.Orientation;
        Matrix3f orientation = new(
            o.M11, o.M21, -o.M31,
            o.M12, o.M22, -o.M32,
            o.M13, o.M23, -o.M33);
        Vector3d position = camera.Position + camera.Orientation.Transform(new Vector3d(0d, -HeldDown, HeldForward));

        // The light in the model's own frame, which is what the vertex stage lights the normals with.
        _device.SetVertexSun(sun is { } light ? orientation.Transform(light.Direction) : Vector3f.Zero);
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
                diffuse = Textures.Get(texture);
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
        _device.SetVertexSun(Vector3f.Zero);
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
        _textureCache?.Clear();
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


    // The textures the models name, loaded on first use.
    private TexTextureCache Textures => _textureCache ??= new(_device, _resources, _logger, LogCategory) { ReportMissing = true };
}
