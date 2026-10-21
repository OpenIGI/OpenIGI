using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Which bone of the first-person view rig one of a weapon's parts hangs off.</summary>
/// <remarks>
/// <para>
/// A weapon is not one rigid model on one bone. <c>0x477220</c>, <c>0x472670</c> and <c>0x46DD00</c>
/// register five part classes, and each one's render handler asks the human holding the gun for a
/// <b>different</b> attachment and then overwrites its own position and 3×3 with the answer — the ATTA
/// offset it was spawned with is not added back. Two handlers cover all five classes:
/// </para>
/// <code>
/// 0x472880  GunClip / GunCABarrel / Gunx2      Gun message 4 (0x477C30)  ->  0x4601A0  bone 52
/// 0x477B10  GunMovingPart / GunCAClip          Gun message 5 (0x477C40)  ->  0x460210  bone 55
/// </code>
/// <para>
/// That is the whole mechanism behind an animated reload: the magazine and the slide are driven by bones
/// the reload clip moves independently of bone 3, which carries the gun body. Measured in the gun's own
/// frame over the Glock's reload, the magazine swings 0.22 m clear of the body and back while the slide
/// racks about 0.01 m; the slide racks again on the firing clip, where bone 3 barely moves.
/// </para>
/// </remarks>
public enum WeaponAttachSlot
{
    /// <summary>The gun body — bone 3, <c>0x4600C0</c>. Everything without a part class of its own.</summary>
    Primary,

    /// <summary>The magazine — bone 52, <c>0x4601A0</c>.</summary>
    Magazine,

    /// <summary>The slide, bolt or lever — bone 55, <c>0x460210</c>.</summary>
    MovingPart,
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
/// effect, its slide and its magazine, while the MP5 attaches two further parts. Effect tasks are driven
/// separately by the weapon state and are not rigid parts.
/// </para>
/// <para>
/// A child is <b>not</b> necessarily fixed to the body. Five of the part classes have a render handler
/// that asks the human for an attachment of its own and replaces its transform with the answer, so the
/// magazine rides bone 52 and the slide bone 55 while the body rides bone 3 — see
/// <see cref="WeaponAttachSlot"/>. That is what animates a reload: those bones are moved by the reload
/// clip independently of the one carrying the gun. Children that name no such class keep the position,
/// orientation and material slots from the same magic-object tree used by placed world objects.
/// </para>
/// <para>
/// A caller that has no view rig to ask — a dropped pickup, a soldier's weapon, a missile — supplies no
/// bone-driven attachments, and every part then falls back to its authored magic-object placement on the
/// primary attachment. That is the whole difference between the two paths: retail gates the same three
/// handlers on the first-person view being the one drawing (<c>0x4E81F0</c> + 92).
/// </para>
/// <para>
/// It remains in the ordinary depth buffer, but not in the world's depth range. The retail view-model
/// renderer (<c>0x49F700</c>) selects transform layer zero; <c>0x493580</c> gives it a 204.8-unit near
/// plane instead of the world's 409.6. <see cref="ViewModelProjection"/> reproduces that 0.05 m foreground
/// range, while <see cref="ViewModelDepth"/> starts the rig with fresh depth. The weapon therefore keeps
/// normal arm-and-weapon self-occlusion without a close wall cutting through it. The physical wall pullback
/// still determines where the model rests.
/// </para>
/// </remarks>
internal sealed class HeldWeaponRenderer : IHeldWeaponPresenter
{
    private const string LogCategory = "Weapon";
    private const string GunFlameTaskType = "TASKTYPE_GUNFLAME";

    // The five part classes of 0x477220 / 0x472670 / 0x46DD00, grouped by the attachment their render
    // handler asks the human for. See WeaponAttachSlot.
    private static readonly Dictionary<string, WeaponAttachSlot> SlotByTaskType = new(StringComparer.Ordinal)
    {
        ["TASKTYPE_GUNCLIP"] = WeaponAttachSlot.Magazine,
        ["TASKTYPE_CABARREL"] = WeaponAttachSlot.Magazine,
        ["TASKTYPE_GUNX2"] = WeaponAttachSlot.Magazine,
        ["TASKTYPE_GUNMOVINGPART"] = WeaponAttachSlot.MovingPart,
        ["TASKTYPE_CACLIP"] = WeaponAttachSlot.MovingPart,
    };

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
    private readonly IOpenIgiLogger _logger;
    private TexTextureCache? _textureCache;
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
        IOpenIgiLogger? logger = null)
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

    /// <inheritdoc/>
    public Vector3f? MuzzleMetres
    {
        get
        {
            foreach (Part part in _parts)
            {
                if (part.IsGunFlame)
                {
                    return part.PositionMetres;
                }
            }

            return null;
        }
    }

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

        AddParts(mission, root, PartFrame.Root, PartFrame.Root, WeaponAttachSlot.Primary);
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
    /// <param name="magazineAttachment">
    /// The view rig's bone-52 attachment (<c>0x4601A0</c>), which drives the magazine and the second
    /// Uzi. When omitted, those parts keep their authored placement on <paramref name="attachment"/>,
    /// which is what a world pickup, a soldier's weapon or a missile wants.
    /// </param>
    /// <param name="movingPartAttachment">
    /// The view rig's bone-55 attachment (<c>0x460210</c>), which drives the slide, bolt or lever.
    /// Omitted by the same callers and with the same fallback.
    /// </param>
    public void Draw(
        BoneAttachment attachment,
        in Camera3D camera,
        in Matrix4f projection,
        bool firedThisTick,
        int shotSequence,
        WeaponDrawParts parts = WeaponDrawParts.All,
        BoneAttachment? magazineAttachment = null,
        BoneAttachment? movingPartAttachment = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_parts.Count == 0)
        {
            return;
        }

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

            // The bone this part's class asks for, when the caller had one to give. A part driven by a
            // bone drops the offset the magic-object tree gave it -- that is what the retail handler's
            // wholesale overwrite of position and 3x3 amounts to -- and only what hangs BELOW it keeps
            // an offset. Without the bone the whole tree stays as authored, on the primary attachment.
            BoneAttachment partAttachment = attachment;
            Vector3f partPosition = part.PositionMetres;
            Matrix3f partOrientation = part.Orientation;
            BoneAttachment? slotAttachment = part.Slot switch
            {
                WeaponAttachSlot.Magazine => magazineAttachment,
                WeaponAttachSlot.MovingPart => movingPartAttachment,
                _ => null,
            };

            if (slotAttachment is { } anchored)
            {
                partAttachment = anchored;
                partPosition = part.AnchoredPositionMetres;
                partOrientation = part.AnchoredOrientation;
            }

            Vector3d attachmentMetres = new(
                partAttachment.Position.X / WorldScene.UnitsPerMetre,
                partAttachment.Position.Y / WorldScene.UnitsPerMetre,
                partAttachment.Position.Z / WorldScene.UnitsPerMetre);
            Vector3f offset = partAttachment.Rotation.Transposed().Transform(partPosition);
            Vector3d position = new(
                attachmentMetres.X + offset.X,
                attachmentMetres.Y + offset.Y,
                attachmentMetres.Z + offset.Z);
            if (part.IsGunFlame)
            {
                partOrientation = _gunFlameRotation * partOrientation;
            }
            Matrix4f modelView = camera.CreateModelView(position, partAttachment.Rotation * partOrientation);

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
                    diffuse = Textures.Get(texture);
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

        _textureCache?.Clear();
    }

    private void AddParts(
        LoadedMission mission,
        WorldObject node,
        PartFrame model,
        PartFrame anchored,
        WeaponAttachSlot slot)
    {
        // A part class with an attachment of its own is not a static ATTA child: its handler overwrites
        // the whole transform with the bone's, so the offset accumulated down to here is dropped and its
        // own descendants -- the second Uzi's muzzle flame, say -- continue in this reset local frame.
        // Both frames are carried on: the model one is what a caller with no view rig falls back to.
        if (SlotOf(node.Name) is { } ownSlot)
        {
            slot = ownSlot;
            anchored = PartFrame.Root;
        }

        bool isGunFlame = string.Equals(TaskTypeOf(node.Name), GunFlameTaskType, StringComparison.Ordinal);
        if (FindInstance(mission, node.Name) is { } bound
            && MefMeshBuilder.Build(node.Models.Highest, (float)(1.0 / WorldScene.UnitsPerMetre)) is { } geometry)
        {
            _parts.Add(new Part(
                node.Name,
                geometry,
                bound.Pack,
                bound.Instance,
                model.PositionMetres,
                model.Task,
                anchored.PositionMetres,
                anchored.Task,
                isGunFlame,
                slot));
        }

        foreach (WorldObject child in node.Children)
        {
            AddParts(mission, child, model.Descend(child), anchored.Descend(child), slot);
        }
    }

    private string TaskTypeOf(string modelName) =>
        _magic.TryGet(modelName, out MagicObjectDefinition definition)
            ? _magic.TaskTypeName(definition.TaskType)
            : string.Empty;

    private WeaponAttachSlot? SlotOf(string modelName) =>
        SlotByTaskType.TryGetValue(TaskTypeOf(modelName), out WeaponAttachSlot slot) ? slot : null;

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

    /// <summary>One node's place in the attachment tree, accumulated on the way down.</summary>
    /// <remarks>
    /// ATTA positions are transformed in the raw parent DRAW frame, but the model's right-handed
    /// attachment basis is the transpose of that row-vector matrix, so both are carried. This is the
    /// same split <see cref="WorldScene"/>'s attachment walk uses; keeping both frames matters for the
    /// UZIX2 child that carries the second Uzi, otherwise it comes out mirrored away from the hand.
    /// </remarks>
    private readonly record struct PartFrame(Vector3f Position, Matrix3f Draw, Matrix3f Task)
    {
        /// <summary>The frame an attachment tree is walked from.</summary>
        public static PartFrame Root { get; } = new(Vector3f.Zero, Matrix3f.Identity, Matrix3f.Identity);

        /// <summary>Gets the accumulated position in metres, which is what a draw wants.</summary>
        public Vector3f PositionMetres => Position / (float)WorldScene.UnitsPerMetre;

        /// <summary>Composes this frame with one attachment step.</summary>
        /// <param name="child">The attached node.</param>
        /// <returns>The child's frame.</returns>
        public PartFrame Descend(WorldObject child) => new(
            Position + Draw.Transposed().Transform(child.Position),
            Draw * child.Orientation,
            Task * child.Orientation.Transposed());
    }

    private sealed class Part(
        string name,
        MeshGeometry geometry,
        LoadedPack pack,
        MtpInstance instance,
        Vector3f positionMetres,
        Matrix3f orientation,
        Vector3f anchoredPositionMetres,
        Matrix3f anchoredOrientation,
        bool isGunFlame,
        WeaponAttachSlot slot)
    {
        public string Name { get; } = name;
        public MeshGeometry Geometry { get; } = geometry;
        public LoadedPack Pack { get; } = pack;
        public MtpInstance Instance { get; } = instance;

        /// <summary>Where the magic-object tree puts this part, relative to the gun body.</summary>
        public Vector3f PositionMetres { get; } = positionMetres;

        /// <summary>The magic-object tree's orientation for this part, relative to the gun body.</summary>
        public Matrix3f Orientation { get; } = orientation;

        /// <summary>Where this part sits relative to the bone its <see cref="Slot"/> names.</summary>
        /// <remarks>
        /// Zero for the part that owns the slot, because its handler drops the accumulated offset; a
        /// descendant of one keeps whatever the tree gives it below that reset.
        /// </remarks>
        public Vector3f AnchoredPositionMetres { get; } = anchoredPositionMetres;

        /// <summary>This part's orientation relative to the bone its <see cref="Slot"/> names.</summary>
        public Matrix3f AnchoredOrientation { get; } = anchoredOrientation;

        public bool IsGunFlame { get; } = isGunFlame;

        /// <summary>Which view-rig bone drives this part when the caller has one to offer.</summary>
        public WeaponAttachSlot Slot { get; } = slot;

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

    // The textures the models name, loaded on first use.
    private TexTextureCache Textures => _textureCache ??= new(_device, _resources, _logger, LogCategory);
}
