using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws the weapon the player is holding, at the bone the player's own rig puts it on.</summary>
/// <remarks>
/// <para>
/// This is the last link of the chain the original counted three of: the level
/// declares a loadout, every <c>WEAPON_ID_*</c> resolves to a roster entry naming a <c>Gun Model</c>,
/// and the model goes wherever the first-person rig's bone 3 is.
/// </para>
/// <para>
/// The original draws it the same way and in the same order — <c>0x4644A0</c> poses the view, draws
/// <c>005_01_1</c>, then asks the human for the weapon's transform (<c>0x4600C0</c>) and hands it to the
/// weapon's own <c>RigidDynCubeObj</c> render (<c>0x4A01B0</c>), which is an ordinary rigid model draw
/// at a position and a 3×3. The arms are missing here, not the weapon: <c>005_01_1</c> is a
/// <b>skinned</b> model and CPU skinning is not built, whereas a gun model is rigid and draws through
/// the path the world already uses.
/// </para>
/// <para>
/// It draws into the ordinary depth buffer, as <c>0x4644A0</c> does — no clear, no separate near pass.
/// The consequence is that standing face-on to a wall can bury the blade in it, because the original
/// keeps the weapon clear by pushing the <i>eye</i> back with the multi-ray probe in <c>0x464BD0</c>,
/// and that probe is not built. Clearing depth here would hide the missing probe rather than leave it
/// visible.
/// </para>
/// </remarks>
public sealed class HeldWeaponRenderer : IDisposable
{
    private const string LogCategory = "Weapon";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);

    private string _modelName = string.Empty;
    private MeshGeometry? _geometry;
    private MeshHandle _mesh = MeshHandle.None;
    private MtpInstance? _instance;
    private LoadedPack? _pack;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="HeldWeaponRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public HeldWeaponRenderer(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the model currently in hand, or an empty string.</summary>
    public string ModelName => _modelName;

    /// <summary>Gets whether a model is loaded and ready to draw.</summary>
    public bool IsReady => _geometry is not null;

    /// <summary>Loads the model a weapon's roster entry names.</summary>
    /// <param name="mission">The mission whose packs hold the model.</param>
    /// <param name="gunModel">The <c>Gun Model</c> name, for example <c>133_01_1</c> for the knife.</param>
    /// <returns><see langword="true"/> when the model resolved and has something to draw.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public bool Load(LoadedMission mission, string gunModel)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentException.ThrowIfNullOrWhiteSpace(gunModel);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.Equals(_modelName, gunModel, StringComparison.OrdinalIgnoreCase))
        {
            return IsReady;
        }

        Release();
        _modelName = gunModel;

        if (!mission.TryGetModel(gunModel, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"The mission's packs do not hold the gun model '{gunModel}'.");
            return false;
        }

        _geometry = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
        if (_geometry is null)
        {
            _logger.Warning(LogCategory, $"Gun model '{gunModel}' has nothing to draw.");
            return false;
        }

        // A draw record names a material slot in the *instance's* list, not the model's, so the textures
        // come from whichever instance uses this model. A gun model that no instance places draws
        // untextured rather than not at all.
        (_pack, _instance) = FindInstance(mission, gunModel);
        if (_instance is null)
        {
            _logger.Warning(LogCategory, $"No instance names gun model '{gunModel}'; it draws untextured.");
        }

        return true;
    }

    /// <summary>Draws the held weapon at an attachment the player's rig resolved.</summary>
    /// <param name="attachment">Where the bone puts it, in engine units.</param>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    public void Draw(BoneAttachment attachment, in Camera3D camera, in Matrix4f projection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_geometry is null)
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
                _logger.Warning(LogCategory, $"Gun model '{_modelName}' did not upload: {ex.Message}");
                _geometry = null;
                return;
            }
        }

        Vector3d metres = new(
            attachment.Position.X / WorldScene.UnitsPerMetre,
            attachment.Position.Y / WorldScene.UnitsPerMetre,
            attachment.Position.Z / WorldScene.UnitsPerMetre);

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        Matrix4f modelView = camera.CreateModelView(metres, attachment.Rotation);

        foreach (MeshSubset subset in _geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            if (subset.HasMaterial
                && _instance is { } instance
                && _pack is { } pack
                && pack.TryGetTextureName(instance, subset.MaterialSlot, out string texture))
            {
                diffuse = GetTexture(texture);
            }

            _device.DrawMesh(
                _mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                diffuse,
                TextureHandle.None,
                BlendMode.Opaque);
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
        Release();

        foreach (TextureHandle texture in _textures.Values)
        {
            _device.DestroyTexture(texture);
        }

        _textures.Clear();
    }

    private static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string gunModel)
    {
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (pack.TryGetModel(instance, out MefModel? _)
                    && string.Equals(instance.Name, gunModel, StringComparison.OrdinalIgnoreCase))
                {
                    return (pack, instance);
                }
            }
        }

        return (null, null);
    }

    private void Release()
    {
        if (_mesh.IsValid)
        {
            _device.DestroyMesh(_mesh);
        }

        _mesh = MeshHandle.None;
        _geometry = null;
        _instance = null;
        _pack = null;
        _modelName = string.Empty;
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
                    new TextureDescription(
                        image.Width,
                        image.Height,
                        image.MipLevels,
                        TextureFilter.Linear,
                        TextureAddressMode.Repeat),
                    image.Pixels);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }

        _textures[name] = handle;
        return handle;
    }
}
