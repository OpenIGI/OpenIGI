using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.ViewModel;

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
public sealed partial class Igi2ViewModelRenderer : ITitleViewModelPresenter, ITitleWorldModelPresenter
{
    private const string LogCategory = "igi2.viewmodel";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IOpenIgiLogger _logger;
    private readonly List<MeshDrawRange> _outlineRanges = [];
    private readonly Dictionary<string, Part?> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, Part?> _bodies = new(ReferenceEqualityComparer.Instance);
    private readonly List<Part> _skinBatch = [];
    private readonly Dictionary<object, Track> _tracks = [];
    private long _worldFrame;
    // The most skinning jobs at once.
    private const int SkinWorkers = 4;

    private readonly Core.Threading.IWorkScheduler? _work;
    private Igi2TextureLoader? _textures;
    private Igi2ModelTable? _table;
    private Func<string, MefModel?>? _models;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="Igi2ViewModelRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report problems.</param>
    /// <param name="work">Where the world models' skinning runs in parallel; none skins them in line.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2ViewModelRenderer(
        IGraphicsDevice device, ResourceRegistry resources, IOpenIgiLogger? logger = null, Core.Threading.IWorkScheduler? work = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
        _work = work;
    }

    /// <inheritdoc/>
    public ObjectShading Shading { get; set; } = ObjectShading.Default;

    /// <inheritdoc/>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Load(mission.PacksInLoadOrder);
    }

    /// <summary>Reads the models it draws from packs, in the order a match is looked for.</summary>
    /// <param name="packs">The packs, the first holding a name shadowing the rest.</param>
    /// <returns><see langword="false"/> when there are none.</returns>
    /// <remarks>For models of the game's own drawn in another game's level, whose packs are not that level's.</remarks>
    public bool Load(IReadOnlyList<LoadedPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Release();
        if (packs.Count == 0)
        {
            return false;
        }

        _table = Igi2ModelTable.Create([.. packs.Select(static pack => pack.Pack)]);
        _models = Igi2DrawableScene.Models(_table, packs);
        _textures = new Igi2TextureLoader(_device, _resources, _logger);
        _spriteResources = new Igi2SpriteResources(_device, _resources.FileSystem, _logger);
        _spriteResources.Get("alarmlight.spr");
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
                QueueFlashes(wanted.MuzzleFlashes, wanted.Model, at, orientation);
            }

            DrawPart(part, modelView, orientation);
        }

        DrawQueuedFlashes(camera, firstPerson: true);
        _device.SetVertexSun(Vector3f.Zero);
        _device.ClearPoseInterpolation();
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A body is skinned once a tick, so the animated mesh's previous stream is the tick before's pose: drawn between the two
    /// when the last two skins are a tick apart, and at the last alone when they are not -- a body that stood still, or was
    /// out of view, has nothing to come from.
    /// </remarks>
    public void DrawWorld(IReadOnlyList<TitleWorldModel> models, in Camera3D camera, in Matrix4f projection, float blend = 1f)
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
        _flashMeshCursor = 0;
        _worldFrame++;
        float between = Math.Clamp(blend, 0f, 1f);
        PrepareWorldSkins(models, camera, frustum);
        foreach (TitleWorldModel wanted in models)
        {
            (Vector3d at, Matrix3f orientation) = Placed(wanted, between);
            Matrix4f modelView = camera.CreateModelView(WorldScene.ToMetres(at), orientation);
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
                PoseBetween(part, wanted.Tick, between);
            }
            else
            {
                part = Resolve(wanted.Model, skinned: false);
                _device.ClearPoseInterpolation();
            }

            if (part is not null)
            {
                DrawPart(part, modelView, orientation);
                QueueFlashes(wanted.MuzzleFlashes, wanted.Model, at, orientation);
            }
        }

        _device.ClearPoseInterpolation();
        ForgetUnseenTracks();
        DrawQueuedFlashes(camera, firstPerson: false);
        _device.SetVertexSun(Vector3f.Zero);
        _device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    // Where a model is drawn this frame: between where it was a tick ago and where it is, when the title names it.
    private (Vector3d At, Matrix3f Orientation) Placed(TitleWorldModel wanted, float blend)
    {
        if (wanted.Instance is not { } instance)
        {
            return (wanted.Position, wanted.Orientation);
        }

        if (!_tracks.TryGetValue(instance, out Track? track))
        {
            track = new Track();
            _tracks[instance] = track;
        }

        if (track.Tick != wanted.Tick)
        {
            bool next = track.Tick == wanted.Tick - 1;
            track.From = next ? track.To : wanted.Position;
            track.FromOrientation = next ? track.ToOrientation : wanted.Orientation;
            track.To = wanted.Position;
            track.ToOrientation = wanted.Orientation;
            track.Tick = wanted.Tick;
        }
        else
        {
            // Drawn again within its tick, as it is now: the tick's latest state is where it goes, from where it was.
            track.To = wanted.Position;
            track.ToOrientation = wanted.Orientation;
        }

        track.SeenFrame = _worldFrame;
        if (blend >= 1f || (track.From == track.To && track.FromOrientation == track.ToOrientation))
        {
            return (track.To, track.ToOrientation);
        }

        return (Vector3d.Lerp(track.From, track.To, blend), Interpolation.InterpolateRotation(track.FromOrientation, track.ToOrientation, blend));
    }

    // A body drawn between its last two skins when they are a tick apart; the blend last frame drew with is the frame's motion.
    private void PoseBetween(Part part, long tick, float blend)
    {
        bool between = blend < 1f && part.SkinTick == tick && part.PreviousSkinTick == tick - 1;
        float poseBlend = between ? 1f - blend : 0f;
        float previous = part.LastDrawnSkinTick == part.SkinTick ? part.LastPoseBlend : 1f;
        part.LastPoseBlend = poseBlend;
        part.LastDrawnSkinTick = part.SkinTick;
        _device.SetPoseInterpolation(poseBlend, between ? previous : 0f);
    }

    // A model no longer drawn for a while is forgotten, so the history holds only what the world still shows.
    private void ForgetUnseenTracks()
    {
        if (_worldFrame % 300 != 0)
        {
            return;
        }

        foreach (object stale in _tracks.Where(entry => _worldFrame - entry.Value.SeenFrame > 300).Select(static entry => entry.Key).ToArray())
        {
            _tracks.Remove(stale);
        }
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
    /// <summary>
    /// Glows through walls the bodies of a world draw that stand for marked things, each in its own colour, where the last
    /// <see cref="DrawWorld"/> drew it and posed as it was posed then.
    /// </summary>
    /// <param name="models">The models the world draw drew.</param>
    /// <param name="marked">What was marked, by the instance its body is drawn for, and the glow each takes.</param>
    /// <param name="camera">The eye.</param>
    /// <param name="radiusPixels">The glow's screen-space radius.</param>
    /// <param name="blend">Where between the last two ticks the world draw stood.</param>
    public void DrawWorldOutlines(
        IReadOnlyList<TitleWorldModel> models, IReadOnlyDictionary<object, Bgra32> marked, in Camera3D camera, float radiusPixels, float blend = 1f)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(marked);
        ObjectDisposedException.ThrowIf(_disposed, this);
        float between = Math.Clamp(blend, 0f, 1f);
        foreach (TitleWorldModel wanted in models)
        {
            if (wanted.Instance is not { } instance || !marked.TryGetValue(instance, out Bgra32 glow)
                || !_bodies.TryGetValue(instance, out Part? part) || part is null || part.Tick != wanted.Tick)
            {
                continue;
            }

            _outlineRanges.Clear();
            foreach ((MeshSubset subset, _) in part.Subsets)
            {
                if (subset.IndexCount > 0)
                {
                    _outlineRanges.Add(new MeshDrawRange(subset.IndexOffset, subset.IndexCount));
                }
            }

            if (_outlineRanges.Count == 0)
            {
                continue;
            }

            (Vector3d at, Matrix3f orientation) = Placed(wanted, between);
            bool poseBetween = between < 1f && part.SkinTick == wanted.Tick && part.PreviousSkinTick == wanted.Tick - 1;
            _device.SetPoseInterpolation(poseBetween ? 1f - between : 0f, poseBetween ? part.LastPoseBlend : 0f);
            _device.DrawOutline(
                part.Mesh,
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_outlineRanges),
                camera.CreateModelView(WorldScene.ToMetres(at), orientation),
                glow,
                radiusPixels,
                throughWalls: true);
        }

        _device.ClearPoseInterpolation();
    }

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
        if (_skinBatch.Count >= SkinWorkers && _work is { } work)
        {
            work.ForRange(0, _skinBatch.Count, SkinWorkers, SkinWorldPart);
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
                UploadSkin(part, part.PendingPose!, part.PendingTick);
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

    private void UploadSkin(Part part, BonePose pose, long tick)
    {
        if (part.Mesh.IsValid) { _device.UpdateAnimatedMesh(part.Mesh, part.Vertices); }
        else { part.Mesh = _device.CreateAnimatedMesh(part.Vertices, part.Geometry.Indices); }
        part.SkinnedPose = pose;
        part.PreviousSkinTick = part.SkinTick;
        part.SkinTick = tick;
    }

    private bool Reskin(Part part, BonePose pose, string model, Action drop, long tick = long.MinValue)
    {
        try
        {
            part.Skin!.Skin(pose, (float)(1.0 / WorldScene.UnitsPerMetre), part.Vertices);
            UploadSkin(part, pose, tick);
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
        foreach (MeshHandle mesh in _spriteMeshes) { _device.DestroyMesh(mesh); }
        _spriteMeshes.Clear();
        _spriteResources?.Dispose();
        _spriteResources = null;
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

        // The ticks of the last two poses uploaded: the previous vertex stream is the earlier's.
        public long SkinTick { get; set; } = long.MinValue;

        public long PreviousSkinTick { get; set; } = long.MinValue;

        // The blend the last frame drew with, and the skin it drew, for the frame's pose motion.
        public float LastPoseBlend { get; set; }

        public long LastDrawnSkinTick { get; set; } = long.MinValue;
    }

    // Where one named model was a tick ago and where it is, to be drawn between.
    private sealed class Track
    {
        public long Tick { get; set; } = long.MinValue;

        public Vector3d From { get; set; }

        public Vector3d To { get; set; }

        public Matrix3f FromOrientation { get; set; }

        public Matrix3f ToOrientation { get; set; }

        public long SeenFrame { get; set; }
    }
}
