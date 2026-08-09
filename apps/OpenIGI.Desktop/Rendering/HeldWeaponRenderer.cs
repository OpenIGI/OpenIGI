using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Which of a weapon's parts one <see cref="HeldWeaponRenderer.Draw"/> call covers.</summary>
/// <remarks>
/// The split exists because the two kinds of part belong to different passes on the modern renderer:
/// the gun's body is geometry, but its muzzle flame is LIGHT — additive, depth-read-only — and light
/// drawn into the deferred G-buffer comes out shaded like scenery. That was visible as a one-frame
/// dark square at a firing soldier's muzzle. A soldier now draws its body in the geometry and shadow
/// passes and its flame in the forward pass; the first-person weapon draws whole in the forward pass
/// and keeps <see cref="All"/>.
/// </remarks>
public enum WeaponDrawParts
{
    /// <summary>Every part — the first-person path, which draws entirely in the forward pass.</summary>
    All,

    /// <summary>The rigid parts only, for the geometry and shadow passes.</summary>
    BodyOnly,

    /// <summary>The muzzle flame only, for the forward pass.</summary>
    FlameOnly,
}

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
/// weapon's own <c>RigidDynCubeObj</c> render (<c>0x4A01B0</c>). That render walks the object's child
/// list, so a weapon is not necessarily one mesh: the Glock's body attaches a transient gun-flame
/// effect, its slide and its magazine, while the MP5 attaches two further parts. Persistent children
/// keep the position, orientation and material slots from the same magic-object tree used by placed
/// world objects. Effect tasks are driven separately by the weapon state and are not rigid parts.
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
    private const string GunFlameTaskType = "TASKTYPE_GUNFLAME";

    private static readonly float[] GunFlameRolls = [0f, 4f, 7f];
    private static readonly SurfaceMaterial GunFlameMaterial = new(
        Roughness: 1f,
        Metallic: 0f,
        Emissive: 1f,
        CastsOcclusion: false,
        NoHistory: true);

    /// <summary>How close two flame strengths must be before the mesh is not rebuilt.</summary>
    /// <remarks>
    /// The strength (<c>RenderCVars.MuzzleFlash</c>) is baked into the flame's vertex colours, which
    /// only resolve to 255 steps anyway — rebuilding for a smaller change than one step would churn
    /// the mesh while a settings slider drags.
    /// </remarks>
    private const float FlameStrengthStep = 1f / 255f;

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly MagicObjectRegistry _magic;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Part> _parts = [];

    private string _modelName = string.Empty;
    private MaterialLibrary _materials = MaterialLibrary.Empty;
    private Matrix3f _gunFlameRotation = Matrix3f.Identity;
    private int _lastShotSequence = -1;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="HeldWeaponRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="magic">The retail attachment-name table used to assemble multipart weapons.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public HeldWeaponRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        MagicObjectRegistry magic,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(magic);

        _device = device;
        _resources = resources;
        _magic = magic;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the model currently in hand, or an empty string.</summary>
    public string ModelName => _modelName;

    /// <summary>Gets whether a model is loaded and ready to draw.</summary>
    public bool IsReady => _parts.Count > 0;

    /// <summary>Builds collision vertices in the same attachment-local frame this renderer draws.</summary>
    /// <remarks>
    /// A multipart weapon's raw model vertices are not enough: child nodes apply both a local position
    /// and rotation before the attachment's transform. This is the exact composition used by
    /// <see cref="Draw"/>, without the camera transform, and is therefore safe for a world pickup's
    /// terrain contact solver.
    /// </remarks>
    public DroppedGunCollider? CreatePickupCollider()
    {
        List<Vector3f> vertices = [];
        float units = (float)WorldScene.UnitsPerMetre;
        foreach (Part part in _parts)
        {
            if (part.IsGunFlame)
            {
                continue;
            }

            foreach (Vertex3D vertex in part.Geometry.Vertices)
            {
                Vector3f rotated = part.Orientation.Transposed().Transform(vertex.Position);
                vertices.Add((part.PositionMetres + rotated) * units);
            }

        }

        return vertices.Count == 0 ? null : new DroppedGunCollider(vertices);
    }

    /// <summary>Gets or sets the modern-path surface descriptions used by weapon textures.</summary>
    public MaterialLibrary Materials
    {
        get => _materials;
        set => _materials = value ?? throw new ArgumentNullException(nameof(value));
    }

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

        if (!WorldObject.TryBuild(gunModel, mission.TryGetModel, _magic, out WorldObject? root)
            || root is null)
        {
            _logger.Warning(LogCategory, $"The mission's packs do not hold the gun model '{gunModel}'.");
            return false;
        }

        AddParts(mission, root, Vector3f.Zero, Matrix3f.Identity);
        if (_parts.Count == 0)
        {
            _logger.Warning(LogCategory, $"Gun model '{gunModel}' has nothing to draw.");
            return false;
        }

        _logger.Information(LogCategory, $"Gun model '{gunModel}' draws as {_parts.Count} attached part(s).");
        return true;
    }

    /// <summary>Draws the held weapon at an attachment the player's rig resolved.</summary>
    /// <param name="attachment">Where the bone puts it, in engine units.</param>
    /// <param name="camera">The eye the scene is drawn from.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <param name="firedThisTick">Whether the held gun fired on the current simulation tick.</param>
    /// <param name="shotSequence">The serial of the last round fired.</param>
    /// <param name="parts">Which parts this call draws; see <see cref="WeaponDrawParts"/>.</param>
    public void Draw(
        BoneAttachment attachment,
        in Camera3D camera,
        in Matrix4f projection,
        bool firedThisTick,
        int shotSequence,
        WeaponDrawParts parts = WeaponDrawParts.All)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_parts.Count == 0)
        {
            return;
        }

        Vector3d attachmentMetres = new(
            attachment.Position.X / WorldScene.UnitsPerMetre,
            attachment.Position.Y / WorldScene.UnitsPerMetre,
            attachment.Position.Z / WorldScene.UnitsPerMetre);

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);

        if (firedThisTick && shotSequence != _lastShotSequence)
        {
            _lastShotSequence = shotSequence;
            float degrees = GunFlameRolls[Random.Shared.Next(GunFlameRolls.Length)];
            _gunFlameRotation = Matrix3f.RotationY(degrees * WeaponTaskParameters.DegreesToRadians)
                * _gunFlameRotation;
        }

        float flameStrength = RenderCVars.MuzzleFlash.Value;

        foreach (Part part in _parts)
        {
            if (part.IsGunFlame)
            {
                // A strength of zero is "no flash", not "a black flash" -- skip the draw outright.
                if (!firedThisTick || parts == WeaponDrawParts.BodyOnly || flameStrength <= 0f)
                {
                    continue;
                }
            }
            else if (parts == WeaponDrawParts.FlameOnly)
            {
                continue;
            }

            // The flame's brightness (r_muzzle_flash) is baked into its vertex colours, so its mesh
            // has to follow the setting: a change rebuilds it at the new strength.
            if (part.Mesh.IsValid
                && part.IsGunFlame
                && MathF.Abs(part.FlameStrength - flameStrength) >= FlameStrengthStep)
            {
                _device.DestroyMesh(part.Mesh);
                part.Mesh = MeshHandle.None;
            }

            if (!part.Mesh.IsValid)
            {
                try
                {
                    part.Mesh = _device.CreateMesh(
                        part.IsGunFlame && flameStrength < 1f
                            ? Dimmed(part.Geometry.Vertices, flameStrength)
                            : part.Geometry.Vertices,
                        part.Geometry.Indices);
                    part.FlameStrength = flameStrength;
                }
                catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
                {
                    _logger.Warning(LogCategory, $"Gun part '{part.Name}' did not upload: {ex.Message}");
                    continue;
                }
            }

            Vector3f offset = attachment.Rotation.Transposed().Transform(part.PositionMetres);
            Vector3d position = new(
                attachmentMetres.X + offset.X,
                attachmentMetres.Y + offset.Y,
                attachmentMetres.Z + offset.Z);
            Matrix3f partOrientation = part.IsGunFlame
                ? _gunFlameRotation * part.Orientation
                : part.Orientation;
            Matrix4f modelView = camera.CreateModelView(position, attachment.Rotation * partOrientation);

            _device.SetDepthState(part.IsGunFlame ? DepthState.TestOnly : DepthState.TestAndWrite);

            foreach (MeshSubset subset in part.Geometry.Subsets)
            {
                if (subset.IndexCount == 0)
                {
                    continue;
                }

                TextureHandle diffuse = TextureHandle.None;
                SurfaceMaterial material = part.IsGunFlame ? GunFlameMaterial : _materials.Default;
                if (subset.HasMaterial
                    && part.Pack.TryGetTextureName(part.Instance, subset.MaterialSlot, out string texture))
                {
                    diffuse = GetTexture(texture);
                    if (!part.IsGunFlame)
                    {
                        material = _materials.For(texture);
                    }
                }

                _device.SetMaterialOverride(material);
                _device.DrawMesh(
                    part.Mesh,
                    subset.IndexOffset,
                    subset.IndexCount,
                    modelView,
                    diffuse,
                    TextureHandle.None,
                    part.IsGunFlame ? BlendMode.Additive : BlendMode.Opaque);
            }
        }

        _device.SetDepthState(DepthState.TestAndWrite);
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

    private void AddParts(LoadedMission mission, WorldObject node, Vector3f position, Matrix3f orientation)
    {
        bool isGunFlame = IsGunFlame(node.Name);
        if (FindInstance(mission, node.Name) is { } bound
            && MefMeshBuilder.Build(node.Models.Highest, (float)(1.0 / WorldScene.UnitsPerMetre)) is { } geometry)
        {
            _parts.Add(new Part(
                node.Name,
                geometry,
                bound.Pack,
                bound.Instance,
                position / (float)WorldScene.UnitsPerMetre,
                orientation,
                isGunFlame));
        }

        foreach (WorldObject child in node.Children)
        {
            Vector3f offset = orientation.Transposed().Transform(child.Position);
            AddParts(mission, child, position + offset, orientation * child.Orientation);
        }
    }

    private bool IsGunFlame(string modelName) =>
        _magic.TryGet(modelName, out MagicObjectDefinition definition)
        && string.Equals(
            _magic.TaskTypeName(definition.TaskType),
            GunFlameTaskType,
            StringComparison.Ordinal);

    private static (LoadedPack Pack, MtpInstance Instance)? FindInstance(LoadedMission mission, string modelName)
    {
        foreach (LoadedPack pack in new[] { mission.LevelPack, mission.CommonPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (pack.TryGetModel(instance, out MefModel? _)
                    && string.Equals(instance.Name, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    return (pack, instance);
                }
            }
        }

        return null;
    }

    private void Release()
    {
        foreach (Part part in _parts)
        {
            if (part.Mesh.IsValid)
            {
                _device.DestroyMesh(part.Mesh);
            }
        }

        _parts.Clear();
        _modelName = string.Empty;
        _gunFlameRotation = Matrix3f.Identity;
        _lastShotSequence = -1;
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

    private sealed class Part(
        string name,
        MeshGeometry geometry,
        LoadedPack pack,
        MtpInstance instance,
        Vector3f positionMetres,
        Matrix3f orientation,
        bool isGunFlame)
    {
        public string Name { get; } = name;
        public MeshGeometry Geometry { get; } = geometry;
        public LoadedPack Pack { get; } = pack;
        public MtpInstance Instance { get; } = instance;
        public Vector3f PositionMetres { get; } = positionMetres;
        public Matrix3f Orientation { get; } = orientation;
        public bool IsGunFlame { get; } = isGunFlame;
        public MeshHandle Mesh { get; set; } = MeshHandle.None;
        public float FlameStrength { get; set; } = 1f;
    }

    // A copy of the vertices with the colour scaled down. The additive draw multiplies the texture
    // by the vertex colour, so this is the one per-part brightness control the device offers.
    private static Vertex3D[] Dimmed(Vertex3D[] vertices, float factor)
    {
        static byte Scale(byte value, float f) => (byte)Math.Clamp((int)(value * f), 0, 255);

        Vertex3D[] dimmed = new Vertex3D[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vertex3D v = vertices[i];
            Bgra32 c = v.Colour;
            dimmed[i] = new Vertex3D(
                v.Position,
                v.Normal,
                v.DiffuseU,
                v.DiffuseV,
                v.LightmapU,
                v.LightmapV,
                new Bgra32(Scale(c.B, factor), Scale(c.G, factor), Scale(c.R, factor), c.A));
        }

        return dimmed;
    }
}
