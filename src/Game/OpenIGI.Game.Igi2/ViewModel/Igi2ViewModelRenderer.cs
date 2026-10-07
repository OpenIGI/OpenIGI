using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <summary>Draws what the player's view holds: the skinned first-person model and the models its clip hangs on it.</summary>
/// <remarks>
/// <para>
/// The view is a boned object drawn after the world under its own projection (<c>HumanView_EnterHandler</c>,
/// <c>igi2 0x4D5DB0</c>, and <c>vObjectFOV</c>, <c>tan 30°</c>). Its model is skinned (<c>arms1_1</c> in the first
/// mission, a mesh of format 1), so its vertices are pushed through the posed bones whenever the pose moves; the
/// skin is built in the model's own frame and the model's position and orientation are applied in the draw.
/// </para>
/// <para>
/// The weapon in hand is not part of that model: each weapon's first-person clips carry an attach object named
/// <c>weapon</c> on the right hand, and the engine draws the held weapon's <c>Fixed Model</c> there
/// (<c>WeaponMagicObj_GetModelName</c>, <c>0x4733B0</c>). The player says which model each part is; this resolves
/// names to meshes and textures through the mission's pack table, as the placed world does.
/// </para>
/// <para>
/// None of the view's models has a bake, so each is lit as an unlightmapped object is: the level's fixed-function sun
/// and object ambient per vertex (<see cref="IGraphicsDevice.SetVertexSun"/>), the sun turned into the model's frame.
/// </para>
/// </remarks>
public sealed class Igi2ViewModelRenderer : ITitleViewModelPresenter
{
    private const string LogCategory = "igi2.viewmodel";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, Part?> _parts = new(StringComparer.OrdinalIgnoreCase);
    private Igi2TextureLoader? _textures;
    private Igi2ModelTable? _table;
    private Func<string, MefModel?>? _models;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="Igi2ViewModelRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2ViewModelRenderer(IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public ObjectShading Shading { get; set; } = ObjectShading.Default;

    /// <inheritdoc/>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();
        IReadOnlyList<LoadedPack> packs = mission.PacksInLoadOrder;
        if (packs.Count == 0)
        {
            return false;
        }

        _table = Igi2ModelTable.Create([.. packs.Select(static pack => pack.Pack)]);
        _models = Igi2DrawableScene.Models(_table, packs);
        _textures = new Igi2TextureLoader(_device, _resources, _logger);
        return true;
    }

    /// <inheritdoc/>
    public void Draw(TitleFirstPersonView view, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_models is null)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);
        _device.SetMaterialOverride(SurfaceMaterial.Default);

        Vector3d origin = view.Origin;
        foreach (TitleViewModelPart wanted in view.Parts)
        {
            if (Resolve(wanted.Model, skinned: wanted.Pose is not null) is not { } part)
            {
                continue;
            }

            Matrix4f modelView;
            Matrix3f orientation;
            if (wanted.Pose is not null)
            {
                if (part.Tick != view.Tick && !Reskin(part, wanted))
                {
                    continue;
                }

                part.Tick = view.Tick;
                orientation = view.Orientation;
                modelView = camera.CreateModelView(WorldScene.ToMetres(origin), orientation);
            }
            else
            {
                Vector3f local = view.Orientation.Transposed().Transform(wanted.Position);
                Vector3d at = new(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
                orientation = view.Orientation * wanted.Orientation;
                modelView = camera.CreateModelView(WorldScene.ToMetres(at), orientation);
            }

            // The sun in the model's own frame, as the world pass turns it for an unbaked placement.
            _device.SetVertexSun(Shading.Sun is { } sun ? orientation.Transposed().Transform(sun.Direction) : Vector3f.Zero);

            foreach ((MeshSubset subset, TextureHandle texture) in part.Subsets)
            {
                _device.DrawMesh(part.Mesh, subset.IndexOffset, subset.IndexCount, modelView, texture, TextureHandle.None, BlendMode.Opaque);
            }
        }

        _device.SetVertexSun(Vector3f.Zero);
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
    }

    private Part? Resolve(string name, bool skinned)
    {
        if (_parts.TryGetValue(name, out Part? cached))
        {
            return cached;
        }

        Part? part = Build(name, skinned);
        _parts[name] = part;
        return part;
    }

    private Part? Build(string name, bool skinned)
    {
        if (_models!(name) is not { } model)
        {
            _logger.Warning(LogCategory, $"The first-person view draws '{name}', which the mission does not hold.");
            return null;
        }

        if (skinned && !MefSkinner.IsSkinned(model))
        {
            _logger.Warning(LogCategory, $"'{name}' is {model.Format}, not a skinned model.");
            return null;
        }

        // A skinned model's positions are written by the skinner, already scaled; a rigid one is scaled here.
        MeshGeometry? geometry = MefMeshBuilder.Build(model, skinned ? 1f : (float)(1.0 / WorldScene.UnitsPerMetre));
        if (geometry is null)
        {
            _logger.Warning(LogCategory, $"'{name}' has nothing to draw.");
            return null;
        }

        List<(MeshSubset, TextureHandle)> subsets = [];
        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle texture = subset.HasMaterial && _table!.TryResolveTexture(name, subset.MaterialSlot, out string? texName)
                ? _textures!.Resolve(Igi2ModelTable.TextureResourceName(texName))
                : TextureHandle.None;
            subsets.Add((subset, texture));
        }

        Vertex3D[] vertices = [.. geometry.Vertices];
        MeshHandle mesh = skinned ? MeshHandle.None : _device.CreateMesh(vertices, geometry.Indices);
        _logger.Information(LogCategory, $"First-person part '{name}': {model.Format}, {subsets.Count} subset(s).");
        return new Part(model, geometry, vertices, mesh, [.. subsets]);
    }

    private bool Reskin(Part part, TitleViewModelPart wanted)
    {
        try
        {
            MefSkinner.Skin(part.Model, wanted.Pose!, (float)(1.0 / WorldScene.UnitsPerMetre), part.Vertices);
            if (part.Mesh.IsValid)
            {
                _device.UpdateAnimatedMesh(part.Mesh, part.Vertices);
            }
            else
            {
                part.Mesh = _device.CreateAnimatedMesh(part.Vertices, part.Geometry.Indices);
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"'{wanted.Model}' could not be posed: {ex.Message}");
            _parts[wanted.Model] = null;
            return false;
        }
    }

    private void Release()
    {
        foreach (Part? part in _parts.Values)
        {
            if (part is { Mesh.IsValid: true })
            {
                _device.DestroyMesh(part.Mesh);
            }
        }

        _parts.Clear();
        _textures = null;
        _table = null;
        _models = null;
    }

    private sealed class Part(MefModel model, MeshGeometry geometry, Vertex3D[] vertices, MeshHandle mesh, (MeshSubset Subset, TextureHandle Texture)[] subsets)
    {
        public MefModel Model { get; } = model;

        public MeshGeometry Geometry { get; } = geometry;

        public Vertex3D[] Vertices { get; } = vertices;

        public MeshHandle Mesh { get; set; } = mesh;

        public (MeshSubset Subset, TextureHandle Texture)[] Subsets { get; } = subsets;

        public long Tick { get; set; } = long.MinValue;
    }
}
