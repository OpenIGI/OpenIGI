using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>Draws a loaded mission's static objects, uploading each model and texture the first time it is needed.</summary>
/// <remarks>
/// <para>
/// One draw call per sub-mesh, because a sub-mesh names its own texture through the <em>instance</em> —
/// the same model placed twice can wear different textures, so the material cannot be baked into the
/// mesh. Meshes and textures are cached by name, since level 1's 501 placements share about 200 models.
/// </para>
/// <para>
/// Terrain is absent: its format still has open questions, so the world is the placed objects over
/// nothing. That is why the horizon is empty rather than the sky being wrong.
/// </para>
/// </remarks>
public sealed class WorldRenderer : IDisposable
{
    private const string LogCategory = "World";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;
    private MaterialLibrary _materials = MaterialLibrary.Empty;
    private readonly Dictionary<string, MeshGeometry?> _geometry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeshHandle> _meshes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scratch for the selection glow's subset ranges, so outlining allocates nothing.</summary>
    private readonly List<MeshDrawRange> _outlineRanges = [];
    private readonly Dictionary<string, TextureHandle> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureHandle[]> _lightmaps = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, IresEntry>? _lightmapArchive;

    /// <summary>Each placement's mesh and per-subset draw state, resolved once and kept.</summary>
    /// <remarks>
    /// Keyed by reference: <see cref="WorldPlacement"/> is a record, and its value equality would hash
    /// every field of every placement on every lookup.
    /// </remarks>
    private readonly Dictionary<WorldPlacement, ResolvedModel?> _resolved = new(ReferenceEqualityComparer.Instance);

    /// <summary>The destroyed-variant models, for placements whose head has been shot out.</summary>
    private readonly Dictionary<WorldPlacement, ResolvedModel?> _resolvedDestroyed = new(ReferenceEqualityComparer.Instance);

    /// <summary>One frame's per-placement state, shared by every pass drawn from it.</summary>
    /// <remarks>
    /// The shadow cascades and the geometry pass each draw the whole scene, and none of what this
    /// holds — pickup and gate visibility, door and elevator and cutscene transforms — depends on the
    /// camera. Resolving it once per frame instead of once per pass is what took the door registry's
    /// linear scan out of the profile's top ten.
    /// </remarks>
    private FrameEntry[] _frame = [];
    private int[] _drawOrder = [];
    private WorldScene? _frameScene;
    private bool _framePrepared;
    private float _frameAlpha = 1f;
    private bool _frameSmooth;
    private bool _disposed;

    /// <summary>How often a placement the queries call visible is retested, in frames.</summary>
    /// <remarks>The terrain's sparse interval; staggered by query id so retests spread across frames.</remarks>
    private const int VisibleRetestInterval = 16;

    /// <summary>The placements whose occlusion query is (re)issued after this pass's draws.</summary>
    /// <remarks>
    /// Issued after, not during: a query needs the pass's occluders already in the depth buffer, or a
    /// wall drawn later in the same pass could not hide anything tested before it.
    /// </remarks>
    private readonly List<int> _queryQueue = [];
    private long _frameNumber;
    private Camera3D _previousCamera;
    private bool _hasPreviousCamera;

    /// <summary>Initializes a new instance of the <see cref="WorldRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public WorldRenderer(IGraphicsDevice device, ResourceRegistry resources, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);

        _device = device;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets how many sub-meshes the last <see cref="Draw"/> submitted.</summary>
    public int LastDrawCount { get; private set; }

    /// <summary>Gets how many triangles the last <see cref="Draw"/> submitted.</summary>
    public int LastTriangleCount { get; private set; }

    /// <summary>Gets how many of the last <see cref="Draw"/>'s sub-meshes sampled a baked lightmap.</summary>
    /// <remarks>
    /// Worth reporting rather than assuming: a sub-mesh whose bake is missing still draws, just unshaded,
    /// so a broken binding looks like a slightly flat picture rather than like an error.
    /// </remarks>
    public int LastLightmappedCount { get; private set; }

    /// <summary>Gets how many frustum-passing placements the occlusion queries rejected last draw.</summary>
    public int LastOccludedCount { get; private set; }

    /// <summary>Gets or sets the live door transforms applied to otherwise static placements.</summary>
    public DoorRegistry? Doors { get; set; }

    /// <summary>Gets or sets the live elevator transforms applied to lift placements.</summary>
    public ElevatorRegistry? Elevators { get; set; }

    /// <summary>Gets or sets the live destructible alarm objects that select destroyed model variants.</summary>
    public AlarmSpeakerRegistry? AlarmSpeakers { get; set; }

    /// <summary>Gets or sets the live articulated security-camera transforms.</summary>
    public SecurityCameraRegistry? SecurityCameras { get; set; }

    /// <summary>Gets or sets the authored cinematic runtime that supplies live vehicle transforms.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets or sets the live generic pickups whose consumed models no longer draw.</summary>
    public GenericPickupRegistry? GenericPickups { get; set; }

    /// <summary>Sets the shared presentation point used by every tick-driven world transform.</summary>
    /// <remarks>
    /// Also marks the frame's resolved placement state stale, so the first pass drawn after this
    /// re-resolves every live transform and every later pass of the same frame reuses them.
    /// </remarks>
    public void BeginFrame(float tickFraction, bool smooth)
    {
        _frameAlpha = Math.Clamp(tickFraction, 0f, 1f);
        _frameSmooth = smooth;
        _framePrepared = false;
    }

    /// <summary>Resolves one placement's mesh, textures and lightmap pages without drawing it.</summary>
    /// <param name="scene">The scene the placement belongs to.</param>
    /// <param name="index">Which placement to make resident.</param>
    /// <returns><see langword="true"/> when <paramref name="index"/> named a placement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Everything this renderer draws with is resolved the first time it is <em>drawn</em> and cached by
    /// name from then on: the geometry, the mesh upload, each texture, each lightmap page. That is the
    /// right behaviour for a level being walked through, where the cost is spread over the walk and
    /// nothing pays for a room it never enters. It is the wrong behaviour when the camera moves a long
    /// way in one frame — every model on the far side becomes visible at once, and the whole level's
    /// residency is paid for on a single frame.
    /// </para>
    /// <para>
    /// The menu's backdrop makes that concrete: a slow orbit over one corner of the level leaves most of
    /// it unresolved, and pressing Play puts the camera at the player's spawn. Measured on mission 1,
    /// that one frame spent about 260 ms inside <see cref="Draw"/> — not drawing, but uploading.
    /// </para>
    /// <para>
    /// So this exists to pay that cost early and a little at a time, while the menu is idle. It resolves
    /// exactly what <see cref="Draw"/> would resolve for the same placement and in the same order, which
    /// is the property that matters: anything it missed would simply be paid for later, on the frame that
    /// first drew it, and the point is that no frame pays.
    /// </para>
    /// </remarks>
    public bool MakeResident(WorldScene scene, int index)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (index < 0 || index >= scene.Placements.Count)
        {
            return false;
        }

        WorldPlacement placement = DisplayedPlacement(scene.Placements[index]);

        MeshGeometry? geometry = placement.BentGeometry ?? GetGeometry(placement);
        if (geometry is null)
        {
            return true;
        }

        GetMesh(placement.BentGeometry is null ? placement.Instance.Name : placement.TaskName, geometry);
        GetLightmapPages(scene, placement);

        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.IndexCount > 0
                && subset.HasMaterial
                && placement.Pack.TryGetTextureName(placement.Instance, subset.MaterialSlot, out string texture))
            {
                GetTexture(texture);
            }
        }

        return true;
    }

    /// <summary>Draws a scene from a camera.</summary>
    /// <param name="scene">The scene to draw.</param>
    /// <param name="camera">The camera to draw from.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <param name="minimumRadius">
    /// Skips placements whose model bounds are smaller than this radius in metres. Zero draws
    /// everything; the shadow pass raises it per cascade, because an object spanning a fraction of a
    /// texel of a far cascade cannot cast a shadow anyone reads.
    /// </param>
    /// <param name="occlusionCulling">
    /// Whether last frame's occlusion-query results may reject placements the frustum keeps. For the
    /// geometry pass only: indoors, the frustum still holds most of the level behind the walls, and
    /// these queries are what stop it being shaded. The shadow cascades must not pass this — their
    /// camera is the light's, and a query answered from the eye's depth says nothing about it.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        WorldScene scene,
        in Camera3D camera,
        in Matrix4f projection,
        float minimumRadius = 0f,
        bool occlusionCulling = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_framePrepared || !ReferenceEquals(_frameScene, scene))
        {
            PrepareFrame(scene);
        }

        _device.SetProjection(projection);
        _device.SetDepthState(DepthState.TestAndWrite);

        // Both facings are drawn. The original culls with its own winding rule, but the port's
        // flattened index order was measured NOT to wind uniformly — culling either facing loses
        // real walls (tried as an r_world_cull experiment and reverted). Halving the indoor fill
        // cost needs per-model winding repair or portal culling, not this switch.
        _device.SetCullMode(CullMode.None);

        int draws = 0;
        int triangles = 0;
        int lit = 0;
        int occluded = 0;
        bool bypassOcclusion = !occlusionCulling || IsCameraCut(camera);
        _queryQueue.Clear();

        // The pass's planes once, not a projection multiply per placement: this loop used to be a
        // measurable share of Matrix4f.Multiply on its own.
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(projection);

        // In model order rather than authored order: every draw is opaque and depth tested, so the
        // order is free — and taking placements grouped by model keeps the vertex array and usually
        // the textures bound from one draw to the next, which is where the driver's time went.
        foreach (int index in _drawOrder)
        {
            ref FrameEntry entry = ref _frame[index];
            if (entry.Skip || entry.Model is not { } model)
            {
                continue;
            }

            if (model.Geometry.Bounds.Radius < minimumRadius)
            {
                continue;
            }

            Matrix4f baseModelView = camera.CreateModelView(entry.Position, entry.Orientation);
            BoundingSphere bounds = model.Geometry.Bounds;
            float viewX = (baseModelView.M11 * bounds.Centre.X) + (baseModelView.M12 * bounds.Centre.Y)
                + (baseModelView.M13 * bounds.Centre.Z) + baseModelView.M14;
            float viewY = (baseModelView.M21 * bounds.Centre.X) + (baseModelView.M22 * bounds.Centre.Y)
                + (baseModelView.M23 * bounds.Centre.Z) + baseModelView.M24;
            float viewZ = (baseModelView.M31 * bounds.Centre.X) + (baseModelView.M32 * bounds.Centre.Y)
                + (baseModelView.M33 * bounds.Centre.Z) + baseModelView.M34;
            if (!entry.HasRotor && !frustum.SphereVisible(viewX, viewY, viewZ, bounds.Radius))
            {
                continue;
            }

            // A rotor's smear has no honest bounds, so it never joins the query set.
            if (occlusionCulling && !entry.HasRotor)
            {
                // A camera inside the query volume sees the volume's faces only from within — they
                // lie behind the very interior they bound, every sample fails, and the placement
                // flickers off and on as its own absence lets the next query pass. The building the
                // player stands in IS this case, so anything whose volume reaches the camera is
                // force-visible and left unqueried until the camera is clear of it. The envelope is
                // the query cube's circumsphere plus a margin that keeps the near plane out of it.
                float envelope = (bounds.Radius * 1.7320509f) + 0.5f;
                bool cameraInside = ((viewX * viewX) + (viewY * viewY) + (viewZ * viewZ))
                    <= envelope * envelope;

                if (!cameraInside)
                {
                    if (!model.Query.IsValid)
                    {
                        model.Query = _device.CreateOcclusionQuery();
                    }

                    bool visible = bypassOcclusion || _device.OcclusionVisible(model.Query);

                    // Hidden placements retest every frame so they reappear the moment a doorway
                    // opens onto them; visible ones only occasionally, staggered to spread out.
                    if (!visible || ((_frameNumber + model.Query.Id) % VisibleRetestInterval) == 0)
                    {
                        _queryQueue.Add(index);
                    }

                    if (!visible)
                    {
                        occluded++;
                        continue;
                    }
                }
            }

            SubsetDraw[] subsets = model.Subsets ?? ResolveSubsets(scene, model, entry.Displayed!);

            foreach (SubsetDraw subset in subsets)
            {
                _device.SetMaterialOverride(subset.Material);
                int sampleCount = entry.HasRotor ? entry.Rotor.SampleCount : 1;
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    Matrix4f modelView = entry.HasRotor
                        ? camera.CreateModelView(entry.Position, entry.Rotor.GetOrientation(sample))
                        : baseModelView;
                    _device.DrawMesh(
                        model.Mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        subset.Diffuse,
                        subset.Lightmap,
                        BlendMode.Opaque);

                    draws++;
                    triangles += subset.TriangleCount;
                    if (subset.Lightmap.IsValid)
                    {
                        lit++;
                    }
                }
            }
        }

        if (occlusionCulling)
        {
            _previousCamera = camera;
            _hasPreviousCamera = true;
        }

        LastDrawCount = draws;
        LastTriangleCount = triangles;
        LastLightmappedCount = lit;
        LastOccludedCount = occluded;
    }

    /// <summary>Issues the queries the last occlusion-culled <see cref="Draw"/> queued.</summary>
    /// <param name="camera">The same camera that pass drew from.</param>
    /// <remarks>
    /// A separate step so the shell can call it once every opaque occluder of the frame — the world
    /// AND the terrain — is in the depth buffer. Issued inside <see cref="Draw"/> the queries would
    /// miss whichever of the two draws after the world, and hills would stop hiding anything.
    /// Skipping the call costs nothing but culling: an unanswered query reports visible.
    /// </remarks>
    public void IssueOcclusionQueries(in Camera3D camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_queryQueue.Count == 0)
        {
            return;
        }

        _device.SetDepthState(DepthState.TestAndWrite);
        foreach (int index in _queryQueue)
        {
            ref FrameEntry entry = ref _frame[index];
            ResolvedModel model = entry.Model!;
            _device.IssueOcclusionQuery(
                model.Query,
                camera.CreateModelView(entry.Position, entry.Orientation),
                model.QueryBounds);
        }

        _queryQueue.Clear();
    }

    /// <summary>Rejects stale query decisions after a teleport or an abrupt camera cut.</summary>
    private bool IsCameraCut(in Camera3D camera)
    {
        if (!_hasPreviousCamera || (camera.Position - _previousCamera.Position).Length > 4.0)
        {
            return true;
        }

        Matrix3f a = camera.Orientation;
        Matrix3f b = _previousCamera.Orientation;
        float largest = 0f;
        largest = Math.Max(largest, Math.Abs(a.M11 - b.M11));
        largest = Math.Max(largest, Math.Abs(a.M12 - b.M12));
        largest = Math.Max(largest, Math.Abs(a.M13 - b.M13));
        largest = Math.Max(largest, Math.Abs(a.M21 - b.M21));
        largest = Math.Max(largest, Math.Abs(a.M22 - b.M22));
        largest = Math.Max(largest, Math.Abs(a.M23 - b.M23));
        largest = Math.Max(largest, Math.Abs(a.M31 - b.M31));
        largest = Math.Max(largest, Math.Abs(a.M32 - b.M32));
        largest = Math.Max(largest, Math.Abs(a.M33 - b.M33));
        return largest > 0.15f;
    }

    /// <summary>Resolves everything camera-independent about the frame, once, for every pass to share.</summary>
    private void PrepareFrame(WorldScene scene)
    {
        _frameNumber++;

        if (!ReferenceEquals(_frameScene, scene))
        {
            _frameScene = scene;
            _frame = new FrameEntry[scene.Placements.Count];
            _drawOrder = BuildDrawOrder(scene);

            // A new scene means new placement objects; entries keyed by the old ones would never be
            // asked for again and would keep the old scene's geometry alive — and their occlusion
            // queries are device objects, so they are handed back rather than dropped.
            DestroyResolvedQueries();
            _resolved.Clear();
            _resolvedDestroyed.Clear();
            _hasPreviousCamera = false;
        }

        for (int i = 0; i < _frame.Length; i++)
        {
            ref FrameEntry entry = ref _frame[i];
            WorldPlacement authored = scene.Placements[i];

            if (GenericPickups?.IsCollected(authored.Owner) == true
                || !ConditionalContainerGate.IsTaskActive(authored.Owner))
            {
                entry.Skip = true;
                continue;
            }

            entry.Skip = false;

            bool destroyed = authored.DestroyedReplacement is not null
                && AlarmSpeakers?.IsDestroyed(authored.Owner) == true;
            Dictionary<WorldPlacement, ResolvedModel?> cache = destroyed ? _resolvedDestroyed : _resolved;
            if (!cache.TryGetValue(authored, out ResolvedModel? model))
            {
                WorldPlacement displayed = DisplayedPlacement(authored);
                model = ResolveModel(displayed);
                entry.Displayed = displayed;
                cache[authored] = model;
            }
            else if (entry.Displayed is null || destroyed != entry.WasDestroyed)
            {
                entry.Displayed = DisplayedPlacement(authored);
            }

            entry.WasDestroyed = destroyed;
            entry.Model = model;
            if (model is null)
            {
                entry.Skip = true;
                continue;
            }

            // The security camera's swivel changes the orientation but never the model, so the
            // displayed placement cached above stays right; only the transform is per-frame.
            if (SecurityCameras?.TryGetHeadOrientation(
                authored,
                _frameAlpha,
                _frameSmooth,
                out Matrix3f headOrientation) == true)
            {
                entry.Displayed = entry.Displayed! with { Orientation = headOrientation };
            }

            ResolveDrawTransform(
                entry.Displayed!,
                out entry.Position,
                out entry.Orientation,
                out entry.Rotor,
                out entry.HasRotor);
        }

        _framePrepared = true;
    }

    /// <summary>Returns every resolved placement's occlusion query to the device.</summary>
    private void DestroyResolvedQueries()
    {
        foreach (ResolvedModel? model in _resolved.Values)
        {
            if (model is { Query.IsValid: true })
            {
                _device.DestroyOcclusionQuery(model.Query);
                model.Query = OcclusionQueryHandle.None;
            }
        }

        foreach (ResolvedModel? model in _resolvedDestroyed.Values)
        {
            if (model is { Query.IsValid: true })
            {
                _device.DestroyOcclusionQuery(model.Query);
                model.Query = OcclusionQueryHandle.None;
            }
        }
    }

    /// <summary>Orders the placements by model, so consecutive draws share GPU state.</summary>
    private static int[] BuildDrawOrder(WorldScene scene)
    {
        int[] order = new int[scene.Placements.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            int byModel = string.CompareOrdinal(
                scene.Placements[left].Instance.Name,
                scene.Placements[right].Instance.Name);
            return byModel != 0 ? byModel : left.CompareTo(right);
        });
        return order;
    }

    /// <summary>Resolves a displayed placement's geometry and mesh; subsets stay lazy.</summary>
    /// <remarks>
    /// Deliberately in two stages: the mesh is needed before culling, but the textures and lightmaps
    /// are not — resolving those only when a placement first passes a frustum keeps residency
    /// spreading over the walk exactly as it did when the draw loop resolved them inline.
    /// </remarks>
    private ResolvedModel? ResolveModel(WorldPlacement displayed)
    {
        MeshGeometry? geometry = displayed.BentGeometry ?? GetGeometry(displayed);
        if (geometry is null)
        {
            return null;
        }

        MeshHandle mesh = GetMesh(
            displayed.BentGeometry is null ? displayed.Instance.Name : displayed.TaskName,
            geometry);
        return mesh.IsValid ? new ResolvedModel(geometry, mesh) : null;
    }

    /// <summary>Resolves a model's per-subset textures, materials and lightmap pages, once.</summary>
    private SubsetDraw[] ResolveSubsets(WorldScene scene, ResolvedModel model, WorldPlacement displayed)
    {
        TextureHandle[] pages = GetLightmapPages(scene, displayed);

        int drawable = 0;
        foreach (MeshSubset subset in model.Geometry.Subsets)
        {
            if (subset.IndexCount > 0)
            {
                drawable++;
            }
        }

        SubsetDraw[] subsets = new SubsetDraw[drawable];
        int cursor = 0;
        foreach (MeshSubset subset in model.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            SurfaceMaterial material = _materials.Default;

            if (subset.HasMaterial
                && displayed.Pack.TryGetTextureName(displayed.Instance, subset.MaterialSlot, out string texture))
            {
                diffuse = GetTexture(texture);

                // What this surface is made of. A sub-mesh that names no texture takes the default
                // rather than inheriting the previous draw's, exactly as the inline loop did.
                material = _materials.For(texture);
            }

            // Each sub-mesh names the page it samples; a model with none, or one whose bake is
            // absent, draws with the device's white lightmap and is simply unshaded.
            TextureHandle lightmap = subset.LightmapPage < pages.Length
                ? pages[subset.LightmapPage]
                : TextureHandle.None;

            subsets[cursor++] = new SubsetDraw(
                subset.IndexOffset,
                subset.IndexCount,
                subset.TriangleCount,
                diffuse,
                material,
                lightmap);
        }

        model.Subsets = subsets;
        return subsets;
    }

    /// <summary>One placement's camera-independent state for the current frame.</summary>
    private struct FrameEntry
    {
        public bool Skip;
        public bool WasDestroyed;
        public bool HasRotor;
        public ResolvedModel? Model;
        public WorldPlacement? Displayed;
        public Vector3d Position;
        public Matrix3f Orientation;
        public RotorDrawState Rotor;
    }

    /// <summary>A placement's uploaded mesh and, once first drawn, its per-subset draw state.</summary>
    private sealed class ResolvedModel(MeshGeometry geometry, MeshHandle mesh)
    {
        public MeshGeometry Geometry { get; } = geometry;

        public MeshHandle Mesh { get; } = mesh;

        public SubsetDraw[]? Subsets { get; set; }

        /// <summary>Gets or sets the placement's occlusion query, created on first geometry-pass use.</summary>
        public OcclusionQueryHandle Query { get; set; }

        /// <summary>Gets the local box the query rasterises — the bounding sphere's enclosing cube.</summary>
        public OcclusionBounds QueryBounds { get; } = new(
            new Vector3f(
                geometry.Bounds.Centre.X - geometry.Bounds.Radius,
                geometry.Bounds.Centre.Y - geometry.Bounds.Radius,
                geometry.Bounds.Centre.Z - geometry.Bounds.Radius),
            new Vector3f(
                geometry.Bounds.Centre.X + geometry.Bounds.Radius,
                geometry.Bounds.Centre.Y + geometry.Bounds.Radius,
                geometry.Bounds.Centre.Z + geometry.Bounds.Radius));
    }

    /// <summary>One drawable subset: its index range and the states it binds.</summary>
    private readonly record struct SubsetDraw(
        int IndexOffset,
        int IndexCount,
        int TriangleCount,
        TextureHandle Diffuse,
        SurfaceMaterial Material,
        TextureHandle Lightmap);

    private WorldPlacement DisplayedPlacement(WorldPlacement placement)
    {
        WorldPlacement displayed = placement;
        if (placement.DestroyedReplacement is { } replacement
            && AlarmSpeakers?.IsDestroyed(placement.Owner) == true)
        {
            displayed = placement with
            {
                Instance = replacement.Instance,
                Pack = replacement.Pack,
                Model = replacement.Model,
            };
        }

        if (SecurityCameras?.TryGetHeadOrientation(
            placement,
            _frameAlpha,
            _frameSmooth,
            out Matrix3f orientation) == true)
        {
            displayed = displayed with { Orientation = orientation };
        }

        return displayed;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DestroyResolvedQueries();

        foreach (MeshHandle mesh in _meshes.Values)
        {
            _device.DestroyMesh(mesh);
        }

        foreach (TextureHandle[] pages in _lightmaps.Values)
        {
            foreach (TextureHandle page in pages)
            {
                _device.DestroyTexture(page);
            }
        }

        foreach (TextureHandle texture in _textures.Values)
        {
            _device.DestroyTexture(texture);
        }

        _lightmaps.Clear();
        _meshes.Clear();
        _textures.Clear();
        _geometry.Clear();
    }

    private MeshGeometry? GetGeometry(WorldPlacement placement)
    {
        if (_geometry.TryGetValue(placement.Instance.Name, out MeshGeometry? cached))
        {
            return cached;
        }

        MeshGeometry? built = MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre));
        _geometry[placement.Instance.Name] = built;
        return built;
    }


    /// <summary>
    /// Works out where a placement is drawn this frame, after everything that can move it has moved it.
    /// </summary>
    /// <param name="placement">The placement being drawn.</param>
    /// <param name="drawPosition">Receives the position to draw at.</param>
    /// <param name="drawOrientation">Receives the orientation to draw with.</param>
    /// <param name="rotorDraw">Receives the motion-blurred rotor state, when one applies.</param>
    /// <param name="hasRotorDraw">Receives whether a rotor state applies.</param>
    /// <remarks>
    /// A door swings, an elevator carries what stands on it, a cutscene can seize either, and a rotor
    /// smears across the frame. Pulled out of the draw loop so the selection outline resolves the same
    /// transform from the same code: an outline computed a second way would drift off its subject the
    /// moment any of these took hold, which is exactly when a reader is watching it.
    /// </remarks>
    private void ResolveDrawTransform(
        WorldPlacement placement,
        out Vector3d drawPosition,
        out Matrix3f drawOrientation,
        out RotorDrawState rotorDraw,
        out bool hasRotorDraw)
    {
        drawPosition = placement.Position;
        drawOrientation = placement.Orientation;
        rotorDraw = default;
        hasRotorDraw = false;

        if (Doors?.TryGetDrawTransform(
            placement.Owner,
            _frameAlpha,
            _frameSmooth,
            out Vector3d doorPosition,
            out Matrix3f doorOrientation) == true)
        {
            drawPosition = doorPosition;
            drawOrientation = doorOrientation;
            if (Elevators?.TryGetDisplacement(
                placement.Owner,
                _frameAlpha,
                _frameSmooth,
                out Vector3d doorDisplacement) == true)
            {
                drawPosition += doorDisplacement;
            }
        }
        else if (Elevators?.TryGetDrawTransform(
            placement.Owner,
            placement.Position,
            placement.Orientation,
            _frameAlpha,
            _frameSmooth,
            out Vector3d elevatorPosition,
            out Matrix3f elevatorOrientation) == true)
        {
            drawPosition = elevatorPosition;
            drawOrientation = elevatorOrientation;
        }
        else if (Cutscenes?.TryGetRotorDrawState(placement, out rotorDraw) == true)
        {
            drawPosition = rotorDraw.Position;
            hasRotorDraw = true;
        }
        else if (Cutscenes?.TryGetPlacementDrawTransform(
            placement,
            out Vector3d cinematicPosition,
            out Matrix3f cinematicOrientation) == true)
        {
            drawPosition = cinematicPosition;
            drawOrientation = cinematicOrientation;
        }
    }

    /// <summary>Draws a selection glow around one placement.</summary>
    /// <param name="scene">The scene the placement belongs to.</param>
    /// <param name="camera">The eye the frame is drawn from.</param>
    /// <param name="placementIndex">Which placement to outline.</param>
    /// <param name="colour">The glow colour.</param>
    /// <param name="radius">How far the glow reaches past the silhouette, in pixels.</param>
    /// <param name="throughWalls">Whether it ignores the depth buffer.</param>
    /// <returns><see langword="true"/> when something was drawn.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Deliberately not frustum-culled. A marker the reader asked for should keep being submitted
    /// while they turn towards it, and one placement of geometry is not worth the cull.
    /// </para>
    /// <para>
    /// Every subset goes to the device in one call. The glow masks the model's own silhouette out
    /// before drawing, and outlining a piece at a time would light each piece's rim across the pieces
    /// already drawn — a crate with a separate lid would come out with a bright line through it.
    /// </para>
    /// </remarks>
    public bool DrawOutline(
        WorldScene scene,
        Camera3D camera,
        int placementIndex,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (placementIndex < 0 || placementIndex >= scene.Placements.Count)
        {
            return false;
        }

        WorldPlacement placement = DisplayedPlacement(scene.Placements[placementIndex]);
        MeshGeometry? geometry = placement.BentGeometry ?? GetGeometry(placement);
        if (geometry is null)
        {
            return false;
        }

        MeshHandle mesh = GetMesh(
            placement.BentGeometry is null ? placement.Instance.Name : placement.TaskName,
            geometry);
        if (!mesh.IsValid)
        {
            return false;
        }

        _outlineRanges.Clear();
        foreach (MeshSubset subset in geometry.Subsets)
        {
            if (subset.IndexCount > 0)
            {
                _outlineRanges.Add(new MeshDrawRange(subset.IndexOffset, subset.IndexCount));
            }
        }

        if (_outlineRanges.Count == 0)
        {
            return false;
        }

        ResolveDrawTransform(placement, out Vector3d position, out Matrix3f orientation, out _, out _);
        Matrix4f modelView = camera.CreateModelView(position, orientation);

        _device.SetPoseInterpolation(0f, 0f);
        _device.DrawOutline(mesh, CollectionsMarshal.AsSpan(_outlineRanges), modelView, colour, radius, throughWalls);
        _device.ClearPoseInterpolation();
        return true;
    }

    private MeshHandle GetMesh(string name, MeshGeometry geometry)
    {
        if (_meshes.TryGetValue(name, out MeshHandle cached))
        {
            return cached;
        }

        MeshHandle handle = MeshHandle.None;
        try
        {
            handle = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Model '{name}' did not upload: {ex.Message}");
        }

        _meshes[name] = handle;
        return handle;
    }

    /// <summary>Gets the uploaded pages of a placement's baked lightmap, empty when it has none.</summary>
    /// <remarks>
    /// One <c>.olm</c> entry holds every page of one model at one level of detail, so this uploads them
    /// together and caches by entry name. Level 1's archive is 1308 entries, but only the ones actually
    /// drawn are ever touched.
    /// </remarks>
    private TextureHandle[] GetLightmapPages(WorldScene scene, WorldPlacement placement)
    {
        if (!placement.HasLightmap)
        {
            return [];
        }

        string entryName = placement.LightmapEntryName();
        if (_lightmaps.TryGetValue(entryName, out TextureHandle[]? cached))
        {
            return cached;
        }

        TextureHandle[] pages = [];
        if (GetLightmapArchive(scene).TryGetValue(entryName, out IresEntry? entry) && entry is not null)
        {
            try
            {
                ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data);
                pages = new TextureHandle[lightmap.Images.Count];
                for (int i = 0; i < pages.Length; i++)
                {
                    ObjectLightmapImage image = lightmap.Images[i];

                    // A page is an atlas of this model's own surfaces, so it clamps rather than repeats:
                    // wrapping would bleed one surface's light onto another at the edges.
                    pages[i] = _device.CreateTexture(
                        new TextureDescription(
                            image.Width,
                            image.Height,
                            1,
                            TextureFilter.Linear,
                            TextureAddressMode.Clamp),
                        MemoryMarshal.Cast<byte, Bgra32>(image.Pixels.Span));
                }
            }
            catch (Exception ex) when (ex is PackFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Lightmap '{entryName}' did not load: {ex.Message}");
                pages = [];
            }
        }

        _lightmaps[entryName] = pages;
        return pages;
    }

    private Dictionary<string, IresEntry> GetLightmapArchive(WorldScene scene)
    {
        if (_lightmapArchive is not null)
        {
            return _lightmapArchive;
        }

        _lightmapArchive = new Dictionary<string, IresEntry>(StringComparer.OrdinalIgnoreCase);
        if (scene.Mission.TryReadLightmapArchive(_resources, out IresArchive? archive) && archive is not null)
        {
            foreach (IresEntry entry in archive.Entries)
            {
                _lightmapArchive[BareEntryName(entry.Name)] = entry;
            }

            _logger.Information(LogCategory, $"Object lightmaps: {_lightmapArchive.Count} entries.");
        }
        else
        {
            _logger.Warning(LogCategory, "The level has no object-lightmap archive; the world draws unshaded.");
        }

        return _lightmapArchive;
    }

    private static string BareEntryName(string name)
    {
        int slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>Gets or sets what each of the level's textures is made of.</summary>
    /// <remarks>
    /// Settable rather than constructor-injected because it is data the shell loads once and may
    /// reload, and because a renderer with no library still draws -- every surface simply takes the
    /// default, which is the behaviour from before materials existed.
    /// </remarks>
    public MaterialLibrary Materials
    {
        get => _materials;
        set
        {
            _materials = value ?? MaterialLibrary.Empty;

            // The resolved subsets bake each surface's material in, so a reloaded library has to melt
            // them; they re-resolve from the caches on the next draw at dictionary-lookup cost.
            foreach (ResolvedModel? model in _resolved.Values)
            {
                if (model is not null)
                {
                    model.Subsets = null;
                }
            }

            foreach (ResolvedModel? model in _resolvedDestroyed.Values)
            {
                if (model is not null)
                {
                    model.Subsets = null;
                }
            }
        }
    }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>
    /// Settable for the same reason <see cref="Materials"/> is, and owned by the shell rather than by
    /// this renderer: the same pack serves the world, the soldiers and the terrain, and each of them
    /// loads its own textures.
    /// </remarks>
    public MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Captures every distinct model's surface into the card atlas.</summary>
    /// <param name="scene">The level, for the placements that name which models are used.</param>
    /// <param name="cardResolution">How many texels per world unit a card asks for.</param>
    /// <returns>What was captured, or <see langword="null"/> when there was nothing to capture.</returns>
    /// <remarks>
    /// <para>
    /// Here rather than in the shell because everything it needs is already here and private: the
    /// geometry cache, the uploaded meshes, the texture cache and the material library. Reaching in
    /// from outside would mean exposing all four, and the only caller wants none of them.
    /// </para>
    /// <para>
    /// <b>Once per distinct model, not per placement.</b> A level draws the same crate hundreds of
    /// times and it looks the same every time. Spline-bent geometry is skipped entirely: it is unique
    /// to its placement, so it has no shared model whose cards could be reused, and cards for one-off
    /// geometry would be atlas spent on a single object.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public (SurfaceCacheCapture.Result Capture, SurfaceCacheInstances? Instances)? CaptureSurfaceCache(
        WorldScene scene, float cardResolution = 8f)
    {
        ArgumentNullException.ThrowIfNull(scene);

        List<SurfaceCacheCapture.Subject> subjects = [];
        List<MeshHandle> meshes = [];

        // Which captured model each instance name became, so the per-placement lighting layout can join
        // a placement back to the cards its model contributed.
        Dictionary<string, int> modelOfName = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.BentGeometry is not null || !seen.Add(placement.Instance.Name))
            {
                continue;
            }

            if (GetGeometry(placement) is not { } geometry || geometry.Indices.Length < 3)
            {
                continue;
            }

            MeshHandle mesh = GetMesh(placement.Instance.Name, geometry);

            if (!mesh.IsValid)
            {
                continue;
            }

            IReadOnlyList<MeshCard> cards = MeshCardBaker.Bake(geometry, cardResolution);

            if (cards.Count == 0)
            {
                continue;
            }

            // Captured by value so the closure does not depend on which placement the loop is on when
            // the capture actually runs.
            WorldPlacement owner = placement;

            subjects.Add(new SurfaceCacheCapture.Subject(
                geometry,
                cards,
                subset =>
                {
                    TextureHandle diffuse = TextureHandle.None;
                    SurfaceMaterial material = _materials.Default;

                    if (subset.HasMaterial
                        && owner.Pack.TryGetTextureName(owner.Instance, subset.MaterialSlot, out string texture))
                    {
                        diffuse = GetTexture(texture);
                        material = _materials.For(texture);
                    }

                    // No lightmap. A card belongs to a model and a bake belongs to a placement, so
                    // there is no single bake a shared card could carry -- see CardAtlasShaders.
                    return (diffuse, TextureHandle.None, material);
                }));

            modelOfName[placement.Instance.Name] = subjects.Count - 1;
            meshes.Add(mesh);
        }

        if (SurfaceCacheCapture.Capture(_device, subjects, meshes, 8192, _logger) is not { } captured)
        {
            return null;
        }

        // A card's SURFACE belongs to the model and is shared; its LIGHTING belongs to the placement and
        // is not -- one crate stands in sunlight and another in a hangar's shadow. So the lighting gets
        // a second layout, a page per placement of each card.
        SurfaceCacheInstances? instances = SurfaceCacheInstances.Build(
            captured,
            scene.Placements,
            placement => placement.BentGeometry is null
                && modelOfName.TryGetValue(placement.Instance.Name, out int model) ? model : -1,
            8192,
            _logger);

        return (captured, instances);
    }

    private TextureHandle GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = TextureHandle.None;
        string resource = LoadedPack.TextureResourceName(name);

        if (_resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                TextureImage image = TexFile.Load(bytes.Span);
                handle = _device.CreateTexture(
                    new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
                    image.Pixels);

                // The derived normal and roughness this game never shipped, if a pack has been built.
                // Registered against the texture rather than carried through the draw, so nothing below
                // this line knows the maps exist. The top level goes with it so a texture the pack has
                // never seen can derive its own map instead of staying flat forever.
                DetailMaps?.Attach(name, handle, image.GetLevel(0), image.Width, image.Height);
            }
            catch (Exception ex) when (ex is TextureFormatException or ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
            }
        }
        else if (_missingTextures.Add(name))
        {
            _logger.Warning(LogCategory, $"Texture '{resource}' is not registered.");
        }

        _textures[name] = handle;
        return handle;
    }
}
