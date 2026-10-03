using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Draws the player's own arms — the skinned model <c>005_01_1</c> on the first-person rig.</summary>
/// <remarks>
/// <para>
/// This is the first half of what <c>0x4644A0</c> draws, and the reason the port could not draw it
/// before: <c>005_01_1</c> is mesh format 1, the <b>skinned</b> format, so its vertices have to be
/// pushed through the bone palette every time the pose changes (<c>0x49B700</c>, ported as
/// <see cref="MefSkinner"/>) rather than handed to the device once.
/// </para>
/// <para>
/// The original skins into a shared dynamic vertex buffer it locks with <c>DISCARD</c>/<c>NOOVERWRITE</c>.
/// This rebuilds the mesh instead, and only when the pose has actually moved — which for a single-key
/// idle is once. That is a port-level difference in bookkeeping, not in what is drawn.
/// </para>
/// </remarks>
internal sealed class ViewModelRenderer : IViewModelPresenter
{
    private const string LogCategory = "ViewModel";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);

    private MefModel? _model;
    private MeshGeometry? _geometry;
    private Vertex3D[] _skinned = [];
    private MeshHandle _mesh = MeshHandle.None;
    private MtpInstance? _instance;
    private LoadedPack? _pack;
    private bool _poseIsStale = true;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ViewModelRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public ViewModelRenderer(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets whether the arms are loaded and can be drawn.</summary>
    public bool IsReady => _geometry is not null && _model is not null;

    /// <summary>Loads the view model a <see cref="HumanView"/> names.</summary>
    /// <param name="mission">The mission whose packs hold it.</param>
    /// <returns><see langword="true"/> when it resolved and is a skinned model with something to draw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mission"/> is <see langword="null"/>.</exception>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();

        if (!mission.TryGetModel(HumanView.ModelName, out MefModel? model) || model is null)
        {
            _logger.Warning(LogCategory, $"The mission's packs do not hold '{HumanView.ModelName}'.");
            return false;
        }

        if (!MefSkinner.IsSkinned(model))
        {
            _logger.Warning(LogCategory, $"'{HumanView.ModelName}' is {model.Format}, not a skinned model.");
            return false;
        }

        _geometry = MefMeshBuilder.Build(model, 1f);
        if (_geometry is null)
        {
            _logger.Warning(LogCategory, $"'{HumanView.ModelName}' has nothing to draw.");
            return false;
        }

        _model = model;
        _skinned = [.. _geometry.Vertices];
        _poseIsStale = true;
        (_pack, _instance) = FindInstance(mission, HumanView.ModelName);

        _logger.Information(
            LogCategory,
            $"Arms '{HumanView.ModelName}': {model.BaseVertexCount} vertices, "
                + $"{model.ExtraInfluenceCount} extra influence(s), highest bone {MefSkinner.HighestBone(model)}.");
        return true;
    }

    /// <summary>Marks the pose as changed, so the next draw re-skins.</summary>
    public void Invalidate() => _poseIsStale = true;

    /// <summary>Draws the arms posed by a rig.</summary>
    /// <param name="view">The first-person rig.</param>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <param name="originUnits">
    /// Where to draw the rig, in engine units, or <see langword="null"/> for the rig's own tick-stepped
    /// <see cref="HumanView.Position"/>. The smooth presentation passes the between-tick origin here so
    /// the arms glide with the interpolated eye instead of stepping against it.
    /// </param>
    /// <param name="previousPoseShare">
    /// How much of the PREVIOUS tick's skin to blend into the drawn one, 0 to 1 — the presentation's
    /// <c>1 - tickFraction</c>. Zero draws this tick's pose exactly, which is the vanilla picture.
    /// </param>
    /// <param name="orientation">
    /// The rig's 3×3, or <see langword="null"/> for its own tick-stepped one. The smooth presentation
    /// passes the sway-blended orientation here — the weapon change animates through the sway angles,
    /// which step on the tick.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    public void Draw(
        HumanView view,
        in Camera3D camera,
        in Matrix4f projection,
        Vector3d? originUnits = null,
        float previousPoseShare = 0f,
        Matrix3f? orientation = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_geometry is null || _model is null)
        {
            return;
        }

        if (_poseIsStale || !_mesh.IsValid)
        {
            if (!Reskin(view))
            {
                return;
            }
        }

        // Drawn at the object's position with its own 3x3. The skin itself is built from a pose whose
        // root rotation is the IDENTITY, and the object's rotation is applied here instead -- which is
        // exactly equivalent, because the root rotation only ever left-multiplies every bone, and it
        // means turning to look does not force a re-skin. The original bakes it into the palette and
        // pays that cost every frame.
        Vector3d origin = originUnits ?? view.Position;
        Vector3d metres = new(
            origin.X / WorldScene.UnitsPerMetre,
            origin.Y / WorldScene.UnitsPerMetre,
            origin.Z / WorldScene.UnitsPerMetre);

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);
        // The rig brings its own surface, as retail's rig draw brings its own state block (0x49D480). The world
        // leaves its last sub-mesh's material in force, so the arms wore whatever the last object in view
        // wore: turning away from the spawn heading made the glove vanish while the knife, which sets its
        // own material, stayed.
        _device.SetMaterialOverride(SurfaceMaterial.Default);

        Matrix4f modelView = camera.CreateModelView(metres, orientation ?? view.Orientation);

        // The mesh is animated (double-buffered), so the previous tick's skin is a vertex stream the
        // shader can blend against -- the same between-tick smoothing the soldiers get.
        _device.SetPoseInterpolation(previousPoseShare, previousPoseShare);

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

        _device.ClearPoseInterpolation();
        _device.SetMaterialOverride(SurfaceMaterial.Default);
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

    private bool Reskin(HumanView view)
    {
        try
        {
            // The unrotated pose: see Draw for why the object's own 3x3 is left out of it.
            BonePose pose = view.Bones.Pose();
            MefSkinner.Skin(_model!, pose, (float)(1.0 / WorldScene.UnitsPerMetre), _skinned);
        }
        catch (ArgumentException ex)
        {
            _logger.Warning(LogCategory, $"The arms could not be posed: {ex.Message}");
            _geometry = null;
            return false;
        }

        try
        {
            // Created animated once, replaced in place from then on: the pose being replaced becomes
            // the mesh's previous-pose stream, which is what the between-tick blend draws against.
            if (_mesh.IsValid)
            {
                _device.UpdateAnimatedMesh(_mesh, _skinned);
            }
            else
            {
                _mesh = _device.CreateAnimatedMesh(_skinned, _geometry!.Indices);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"The arms did not upload: {ex.Message}");
            _geometry = null;
            return false;
        }

        _poseIsStale = false;
        return true;
    }

    private static (LoadedPack? Pack, MtpInstance? Instance) FindInstance(LoadedMission mission, string name)
    {
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase))
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
        _model = null;
        _skinned = [];
        _instance = null;
        _pack = null;
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
