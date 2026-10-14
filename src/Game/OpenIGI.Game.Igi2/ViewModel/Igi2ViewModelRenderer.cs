using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
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
/// mission, a mesh of format 1), so its vertices are pushed through the posed bones whenever the pose moves, as the
/// sequel's renderer weighs them (<see cref="Igi2BoneSkin"/>); the skin is built in the model's own frame and the
/// model's position and orientation are applied in the draw.
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
/// <para>
/// The same models drawn in the world are the weapons lying in the level (<see cref="DrawWorld"/>): rigid, unbaked, and
/// lit the same way, under the world's projection.
/// </para>
/// </remarks>
public sealed class Igi2ViewModelRenderer : ITitleViewModelPresenter, ITitleWorldModelPresenter
{
    private const string LogCategory = "igi2.viewmodel";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly Dictionary<string, Part?> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, Part?> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly List<Part> _skinBatch = [];
    private readonly ParallelOptions _skinWorkers = new() { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
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
                if (part.Tick != view.Tick)
                {
                    if (wanted.PreviousPose is { } previous && !SamePose(part.SkinnedPose, previous)
                        && !Reskin(part, previous, wanted.Model, () => _parts[wanted.Model] = null))
                    {
                        continue;
                    }

                    if (!Reskin(part, wanted.Pose, wanted.Model, () => _parts[wanted.Model] = null))
                    {
                        continue;
                    }
                }

                part.Tick = view.Tick;
                _device.SetPoseInterpolation(1f - view.PoseInterpolation, 1f - view.PoseInterpolation);
                orientation = view.Orientation;
                modelView = camera.CreateModelView(WorldScene.ToMetres(origin), orientation);
            }
            else
            {
                _device.ClearPoseInterpolation();
                Vector3f local = view.Orientation.Transposed().Transform(wanted.Position);
                Vector3d at = new(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z);
                orientation = view.Orientation * wanted.Orientation;
                modelView = camera.CreateModelView(WorldScene.ToMetres(at), orientation);
            }

            DrawPart(part, modelView, orientation);
        }

        _device.SetVertexSun(Vector3f.Zero);
        _device.ClearPoseInterpolation();
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    /// <inheritdoc/>
    public void DrawWorld(IReadOnlyList<TitleWorldModel> models, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(models);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_models is null || models.Count == 0)
        {
            return;
        }

        _device.SetProjection(projection);
        _device.SetCullMode(CullMode.None);
        _device.SetDepthState(DepthState.TestAndWrite);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(projection);
        PrepareWorldSkins(models, camera, frustum);
        foreach (TitleWorldModel wanted in models)
        {
            Matrix4f modelView = camera.CreateModelView(WorldScene.ToMetres(wanted.Position), wanted.Orientation);
            if (_models(wanted.Model) is not { } model || !WorldModelVisible(wanted, model, modelView, frustum))
            {
                continue;
            }

            Part? part;
            if (wanted is { Pose: { } pose, Instance: { } instance })
            {
                // A body of its own: one model may stand many times over, each posed its own way, so each keeps its own mesh.
                if (!_bodies.TryGetValue(instance, out part))
                {
                    part = Build(wanted.Model, skinned: true);
                    _bodies[instance] = part;
                }

                if (part is null || (part.Tick != wanted.Tick && !SamePose(part.SkinnedPose, pose)
                    && !Reskin(part, pose, wanted.Model, () => _bodies[instance] = null)))
                {
                    continue;
                }

                part.Tick = wanted.Tick;
            }
            else
            {
                part = Resolve(wanted.Model, skinned: false);
            }

            if (part is not null)
            {
                DrawPart(part, modelView, wanted.Orientation);
            }
        }

        _device.SetVertexSun(Vector3f.Zero);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    private static bool WorldModelVisible(TitleWorldModel wanted, MefModel model, in Matrix4f modelView, in FrustumCulling.Frustum frustum)
    {
        // pc_direct3drender.c Direct3DRender_DrawBoneMesh excludes before processing vertices;
        // bonedyncubeobj.c BoneDynCubeObj_Exclude moves the authored render sphere by root translation.
        // Keep models with missing bounds visible rather than inventing a bound that could clip them.
        if (!(model.RenderSphereRadius > 0f) || !float.IsFinite(model.RenderSphereRadius))
        {
            return true;
        }

        Vector3f centre = model.RenderSphereCentre;
        if (wanted.Pose is { BoneCount: > 0 } pose)
        {
            Vector3f root = pose.Positions[0];
            centre = new Vector3f(
                ((Fpu)centre.X + root.X).F,
                ((Fpu)centre.Y + root.Y).F,
                ((Fpu)centre.Z + root.Z).F);
        }

        const float scale = (float)(1.0 / WorldScene.UnitsPerMetre);
        return frustum.SphereVisible(modelView, new BoundingSphere(centre * scale, model.RenderSphereRadius * scale));
    }

    /// <summary>Draws one part, lit by the sun turned into its frame.</summary>
    private void DrawPart(Part part, in Matrix4f modelView, Matrix3f orientation)
    {
        // The sun in the model's own frame, as the world pass turns it for an unbaked placement: the orientation takes
        // the model to the world as M * v, so the transpose (the row-vector Transform) takes the world's sun back.
        _device.SetVertexSun(Shading.Sun is { } sun ? orientation.Transform(sun.Direction) : Vector3f.Zero);

        foreach ((MeshSubset subset, TextureHandle texture) in part.Subsets)
        {
            _device.DrawMesh(part.Mesh, subset.IndexOffset, subset.IndexCount, modelView, texture, TextureHandle.None, BlendMode.Opaque);
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

        Igi2BoneSkin? skin = null;
        if (skinned && (skin = Igi2BoneSkin.Create(model)) is null)
        {
            _logger.Warning(LogCategory, $"'{name}' is {model.Format}, not a boned model its hierarchy covers.");
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
        return new Part(skin, geometry, vertices, mesh, [.. subsets]);
    }

    private void PrepareWorldSkins(IReadOnlyList<TitleWorldModel> models, in Camera3D camera, in FrustumCulling.Frustum frustum)
    {
        _skinBatch.Clear();
        foreach (TitleWorldModel wanted in models)
        {
            if (wanted is not { Pose: { } pose, Instance: { } instance }
                || _models!(wanted.Model) is not { } model
                || !WorldModelVisible(wanted, model, camera.CreateModelView(WorldScene.ToMetres(wanted.Position), wanted.Orientation), frustum))
            {
                continue;
            }

            if (!_bodies.TryGetValue(instance, out Part? part))
            {
                part = Build(wanted.Model, skinned: true);
                _bodies[instance] = part;
            }

            if (part is null || part.Tick == wanted.Tick || SamePose(part.SkinnedPose, pose) || _skinBatch.Contains(part))
            {
                continue;
            }

            part.PendingPose = pose;
            part.PendingInstance = instance;
            part.PendingModel = wanted.Model;
            part.PendingTick = wanted.Tick;
            part.SkinError = null;
            _skinBatch.Add(part);
        }

        // pc_direct3drender.c Direct3DRender_ProcessVerticesBone: every model's vertex stream is independent.
        // Only the unchanged CPU calculations run on workers. Resource creation, uploads and draws stay here.
        if (_skinBatch.Count >= 4 && _skinWorkers.MaxDegreeOfParallelism > 1)
        {
            Parallel.For(0, _skinBatch.Count, _skinWorkers, SkinWorldPart);
        }
        else
        {
            for (int i = 0; i < _skinBatch.Count; i++) { SkinWorldPart(i); }
        }

        foreach (Part part in _skinBatch)
        {
            try
            {
                if (part.SkinError is { } error) { throw error; }
                UploadSkin(part, part.PendingPose!);
                part.Tick = part.PendingTick;
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"'{part.PendingModel}' could not be posed: {ex.Message}");
                _bodies[part.PendingInstance!] = null;
            }
            finally
            {
                part.PendingPose = null;
                part.PendingInstance = null;
                part.SkinError = null;
            }
        }
        _skinBatch.Clear();
    }

    private void SkinWorldPart(int index)
    {
        Part part = _skinBatch[index];
        try
        {
            part.Skin!.Skin(part.PendingPose!, (float)(1.0 / WorldScene.UnitsPerMetre), part.Vertices);
        }
        catch (ArgumentException ex)
        {
            part.SkinError = ex;
        }
    }

    private void UploadSkin(Part part, BonePose pose)
    {
        if (part.Mesh.IsValid) { _device.UpdateAnimatedMesh(part.Mesh, part.Vertices); }
        else { part.Mesh = _device.CreateAnimatedMesh(part.Vertices, part.Geometry.Indices); }
        part.SkinnedPose = pose;
    }

    private bool Reskin(Part part, BonePose pose, string model, Action drop)
    {
        try
        {
            part.Skin!.Skin(pose, (float)(1.0 / WorldScene.UnitsPerMetre), part.Vertices);
            UploadSkin(part, pose);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"'{model}' could not be posed: {ex.Message}");
            drop();
            return false;
        }
    }

    private void Release()
    {
        foreach (Part? part in _parts.Values.Concat(_bodies.Values))
        {
            if (part is { Mesh.IsValid: true })
            {
                _device.DestroyMesh(part.Mesh);
            }
        }

        _parts.Clear();
        _bodies.Clear();
        _textures = null;
        _table = null;
        _models = null;
    }

    private static bool SamePose(BonePose? previous, BonePose current) => ReferenceEquals(previous, current)
        || (previous is not null
            && MemoryMarshal.AsBytes(previous.Positions).SequenceEqual(MemoryMarshal.AsBytes(current.Positions))
            && MemoryMarshal.AsBytes(previous.Rotations).SequenceEqual(MemoryMarshal.AsBytes(current.Rotations)));

    private sealed class Part(Igi2BoneSkin? skin, MeshGeometry geometry, Vertex3D[] vertices, MeshHandle mesh, (MeshSubset Subset, TextureHandle Texture)[] subsets)
    {
        public Igi2BoneSkin? Skin { get; } = skin;

        public MeshGeometry Geometry { get; } = geometry;

        public Vertex3D[] Vertices { get; } = vertices;

        public MeshHandle Mesh { get; set; } = mesh;

        public (MeshSubset Subset, TextureHandle Texture)[] Subsets { get; } = subsets;

        public long Tick { get; set; } = long.MinValue;

        public BonePose? SkinnedPose { get; set; }

        public BonePose? PendingPose { get; set; }

        public object? PendingInstance { get; set; }

        public string PendingModel { get; set; } = string.Empty;

        public long PendingTick { get; set; }

        public ArgumentException? SkinError { get; set; }
    }
}
