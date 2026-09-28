using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using System.Runtime.InteropServices;

namespace OpenIGI.Presentation.Rendering;

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
public sealed class WorldRenderer : IWorldPresenter
{
    /// <summary>The alpha a cut-out surface has to reach to be drawn, matching what the shaders test.</summary>
    /// <remarks>
    /// Kept beside the texture load because it is what the mip chain's coverage is preserved AGAINST: the
    /// scale chosen per level is the one that keeps the same fraction of texels above this number, so a
    /// value here that disagrees with the shaders would preserve the wrong quantity and thin the fence
    /// anyway. Change both together.
    /// </remarks>
    private const string LogCategory = "World";

    /// <summary>How far a lamp placement may stand from the light it houses, in metres.</summary>
    /// <remarks>See where it is used; the extractor's clustering is what sets the scale.</remarks>
    private const double LampBindingRadius = 4.0;

    private readonly IGraphicsDevice _device;
    private LampSwitchboard? _switchboard;
    private readonly ResourceRegistry _resources;
    private readonly AssetFormatProfile _formats;
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

    // Path-traced lightmap pages that override the retail .olm ones, keyed by entry name then page index.
    // Empty unless a bake has been applied; GetLightmapPages substitutes these pixels for the shipped ones.
    private readonly Dictionary<string, Dictionary<int, Bgra32[]>> _bakedPages = new(StringComparer.OrdinalIgnoreCase);

    // When true, a lightmapped page the bake did not cover reads black instead of falling back to the
    // shipped .olm -- so the frame shows only our own lighting, with its gaps visible rather than papered
    // over by the artists' light.
    private bool _discardRetailLightmaps;

    // The headroom the baked pages are divided by before their sRGB encode when the bake is exclusive, so a
    // sunlit or sky-lit surface's radiance keeps its value above one in an eight-bit page instead of clipping;
    // the shader multiplies back up. One when the shipped LDR pages are in play (they have no such range).
    private const float HdrHeadroom = 4f;
    private float _lightmapScale = 1f;

    /// <inheritdoc/>
    public ObjectShading Shading { get; set; } = ObjectShading.Default;

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

    // The glass pass's own order, rebuilt per frame. See the comment where it is filled.
    private readonly List<int> _glassOrder = [];
    private WorldScene? _frameScene;
    private bool _framePrepared;
    private float _frameAlpha = 1f;
    private bool _frameSmooth;
    private PlacementPose? _drawPoses;
    private bool _disposed;

    /// <summary>How often a placement the queries call visible is retested, in frames.</summary>
    /// <remarks>The terrain's sparse interval; staggered by query id so retests spread across frames.</remarks>
    private const int VisibleRetestInterval = 16;

    /// <summary>How many frames the view must hold still before a visibility verdict is trusted again.</summary>
    /// <remarks>
    /// A verdict is not measured against the frame that acts on it. The box is drawn, the result is read
    /// once the slot's fence comes round, and the renderer skips the page a frame or two after the depth
    /// that judged it was written. Standing still that is exactly right. Turning it is a statement about
    /// where the camera was looking, not where it is: a page that has just come out from behind a hill is
    /// still carrying the verdict it earned while hidden, so it is skipped -- the hole -- until its retest
    /// lands and it is drawn again. Long enough to cover the deepest frame queue the backends run.
    /// </remarks>
    private const int OcclusionSettleFrames = 3;

    /// <summary>How far the camera may drift per frame and still count as holding still, in metres.</summary>
    private const double OcclusionSettlePosition = 0.05;

    /// <summary>How far any orientation element may move per frame and still count as holding still.</summary>
    /// <remarks>About a fifth of a degree: below what a hand on a mouse holds, above sensor noise.</remarks>
    private const float OcclusionSettleOrientation = 0.003f;

    /// <summary>The placements whose occlusion query is (re)issued after this pass's draws.</summary>
    /// <remarks>
    /// Issued after, not during: a query needs the pass's occluders already in the depth buffer, or a
    /// wall drawn later in the same pass could not hide anything tested before it.
    /// </remarks>
    private readonly List<int> _queryQueue = [];
    private long _frameNumber;
    private Camera3D _previousCamera;
    private bool _hasPreviousCamera;
    private int _occlusionSettle;

    /// <summary>Initializes a new instance of the <see cref="WorldRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="formats">The title's format policy; a baked lightmap's descriptor stride differs.</param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The profile is the only title-shaped thing this renderer takes, and it takes it because a
    /// <c>.olm</c> entry cannot be read without knowing which game wrote it — the descriptor is 44
    /// bytes in Project I.G.I. and 52 in the sequel, and the stride decides where the pixels start.
    /// It is a parameter rather than a default so that a title cannot forget to say.
    /// </remarks>
    public WorldRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        AssetFormatProfile formats,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(formats);

        _device = device;
        _resources = resources;
        _formats = formats;
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

    /// <summary>Gets or sets the live retail glass and explosive-object presentation state.</summary>
    public DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Gets or sets the live articulated security-camera transforms.</summary>
    public SecurityCameraRegistry? SecurityCameras { get; set; }

    /// <inheritdoc/>
    public IPlacementSpin? Spins { get; set; }

    /// <summary>Gets or sets the authored cinematic runtime that supplies live vehicle transforms.</summary>
    public CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets or sets the live generic pickups whose consumed models no longer draw.</summary>
    public GenericPickupRegistry? GenericPickups { get; set; }

    /// <summary>Gets or sets GenericTBA destruction state for authored interaction placements.</summary>
    public WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the live rotating world objects (radar dishes and rotating tasks).</summary>
    public RotatingObjectRegistry? RotatingObjects { get; set; }

    /// <inheritdoc/>
    void IWorldPresenter.Draw(
        WorldScene scene,
        in Camera3D camera,
        in Matrix4f projection,
        in WorldDrawOptions options) =>
        Draw(
            scene,
            camera,
            projection,
            options.MinimumRadius,
            options.OcclusionCulling,
            options.RotorsOnly,
            options.GlassOnly);

    /// <inheritdoc/>
    /// <remarks>
    /// Sub-meshes rather than objects, because that is the unit the device is handed and so the number
    /// that decides the frame. A title whose world is not built from sub-meshes reports whatever its own
    /// unit is, which is why these are lines rather than a fixed set of named counters.
    /// </remarks>
    public void Describe(ICollection<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Add(FormattableString.Invariant(
            $"sub-meshes {LastDrawCount}   lightmapped {LastLightmappedCount}   triangles {LastTriangleCount}"));
        into.Add(FormattableString.Invariant($"placements occlusion-culled {LastOccludedCount}"));
    }

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
    /// <param name="rotorsOnly">
    /// Draws the translucent rotor attachments in place of the opaque world. The two are separate
    /// passes over the same placements: a rotor blends against what is already in the frame, so it
    /// has to follow every opaque draw and test depth without writing it. Its tallies accumulate
    /// onto the opaque pass's rather than replacing them, so the overlay still reads one frame.
    /// </param>
    /// <param name="glassOnly">
    /// Draws the level's glazing in place of the opaque world, over a picture the lighting has already
    /// finished. A SECOND pass over the same subsets rather than a diversion of them: a window texture
    /// holds the frame and the pane together, the opaque pass keeps the frame as it always did, and this
    /// keeps what that pass discarded. Like the rotors it tests depth without writing it, and its tallies
    /// accumulate onto the opaque pass's rather than replacing them.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        WorldScene scene,
        in Camera3D camera,
        in Matrix4f projection,
        float minimumRadius = 0f,
        bool occlusionCulling = false,
        bool rotorsOnly = false,
        bool glassOnly = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_framePrepared || !ReferenceEquals(_frameScene, scene))
        {
            PrepareFrame(scene);
        }

        _device.SetProjection(projection);
        _device.SetDepthState(rotorsOnly || glassOnly ? DepthState.TestOnly : DepthState.TestAndWrite);

        // Back faces are culled once the mesh builder has repaired winding (r_world_cull): every triangle
        // is rewound to agree with its outward vertex normals there, so CCW is reliably the front and
        // culling the back removes a thin object's inward face rather than letting the two fight over a
        // pixel -- and halves the indoor fill. Off falls back to drawing both facings, which is what the
        // port did before the repair existed and does not wind uniformly on its own.
        _device.SetCullMode(RenderCVars.WorldCull.Value != 0 ? CullMode.CounterClockwise : CullMode.None);

        // The lightmap headroom for the objects' pages -- one unless an exclusive HDR bake is applied.
        // Reset to one at the end of the pass so the terrain, which keeps its shipped LDR lightmaps, is not
        // scaled by it.
        _device.SetLightmapScale(_lightmapScale);

        // What this pass composites with, read once: the title's lightmap gain and, if it has one, the
        // sun its unlightmapped objects are lit by. Both are applied per sub-mesh below and tracked so
        // the device is told only when something changes.
        ObjectShading shading = Shading;
        float appliedScale = _lightmapScale;
        Vector3f appliedSun = Vector3f.Zero;

        int draws = 0;
        int triangles = 0;
        int lit = 0;
        int occluded = 0;
        bool bypassOcclusion = !occlusionCulling || !OcclusionVerdictsUsable(camera);
        _queryQueue.Clear();

        // The pass's planes once, not a projection multiply per placement: this loop used to be a
        // measurable share of Matrix4f.Multiply on its own.
        FrustumCulling.Frustum frustum = FrustumCulling.Frustum.FromProjection(projection);

        // In model order rather than authored order: every draw is opaque and depth tested, so the
        // order is free — and taking placements grouped by model keeps the vertex array and usually
        // the textures bound from one draw to the next, which is where the driver's time went.
        // GLASS IS DRAWN FARTHEST FIRST, and the opaque pass's model order will not do.
        //
        // Two reasons, and the second is the one that shows. The panes blend, so back to front is simply
        // the right order to composite them in. But the glazing also RECORDS each pane pixel into a layer
        // the reflection pass reads, and that layer holds one pane per pixel: where two panes overlap --
        // a window seen through another window, which a building with glass on both faces is full of --
        // the last write wins. In model order which one that is, is arbitrary. So a pixel showing the
        // near pane could be carrying the FAR pane's normal and distance, and the reflection pass would
        // dutifully reflect about a plane that is not the one you are looking at. That reads as a patch
        // of window reflecting something at a plainly wrong angle.
        //
        // Sorted farthest first, the last writer is the nearest pane, which is the one whose surface the
        // pixel mostly is. Cheap: almost nothing in a level is a window.
        // The opaque pass keeps its own array untouched -- it holds every placement in the level and
        // copying it per frame to share one loop would be a real cost for no gain.
        // Whether this pass is the one that feeds the traced world. Exactly one of the three must be,
        // or every placement behind the camera would be submitted three times a frame.
        bool tracedOnly = !glassOnly && !rotorsOnly && RenderCVars.TracedWorldRadius.Value > 0f;

        ReadOnlySpan<int> order = _drawOrder;
        if (glassOnly)
        {
            _glassOrder.Clear();
            for (int i = 0; i < _drawOrder.Length; i++)
            {
                int candidate = _drawOrder[i];
                ref FrameEntry glass = ref _frame[candidate];
                if (!glass.Skip && glass.Model is { HasGlass: true })
                {
                    _glassOrder.Add(candidate);
                }
            }

            Vector3d eye = camera.Position;
            _glassOrder.Sort((left, right) =>
                (_frame[right].Position - eye).LengthSquared.CompareTo(
                    (_frame[left].Position - eye).LengthSquared));

            order = CollectionsMarshal.AsSpan(_glassOrder);
        }

        foreach (int index in order)
        {
            ref FrameEntry entry = ref _frame[index];
            if (entry.Skip || entry.Model is not { } model)
            {
                continue;
            }

            // Each pass takes only the half of the placements it is for: the rotors blend against the
            // opaque world, so they cannot be drawn in the middle of building it.
            if (entry.IsRotor != rotorsOnly)
            {
                continue;
            }

            // Almost nothing is a window, so the glass pass gets out before the frustum test rather than
            // after it.
            if (glassOnly && !model.HasGlass)
            {
                continue;
            }

            if (model.Geometry.Bounds.Radius < minimumRadius)
            {
                continue;
            }

            Matrix4f baseModelView = camera.CreateModelView(entry.Position, entry.Orientation);
            BoundingSphere bounds = model.Geometry.Bounds;

            // A ROTOR IS CULLED TOO, and against a bound that holds for every phase of its spin. The
            // blur turns the mesh about its own local origin, so an off-origin bound centre travels a
            // circle and a sphere taken at one blade sample is wrong at every other -- which is why a
            // rotor used to be exempt from the frustum test altogether, and why a helicopter behind the
            // camera was still drawn. Centred at that origin and grown by how far the real centre sits
            // from it, the sphere encloses every orientation, so the test can be made without choosing a
            // sample and the retail blur is untouched when it is visible.
            bool rotorCull = entry.HasRotor;
            Matrix4f cullModelView = rotorCull ? camera.CreateModelView(entry.Position) : baseModelView;
            BoundingSphere cullBounds = rotorCull ? RotorCullBounds(bounds) : bounds;

            float viewX = (cullModelView.M11 * cullBounds.Centre.X) + (cullModelView.M12 * cullBounds.Centre.Y)
                + (cullModelView.M13 * cullBounds.Centre.Z) + cullModelView.M14;
            float viewY = (cullModelView.M21 * cullBounds.Centre.X) + (cullModelView.M22 * cullBounds.Centre.Y)
                + (cullModelView.M23 * cullBounds.Centre.Z) + cullModelView.M24;
            float viewZ = (cullModelView.M31 * cullBounds.Centre.X) + (cullModelView.M32 * cullBounds.Centre.Y)
                + (cullModelView.M33 * cullBounds.Centre.Z) + cullModelView.M34;
            if (!frustum.SphereVisible(viewX, viewY, viewZ, cullBounds.Radius))
            {
                // DRAWN BY VISIBILITY, TRACED BY DISTANCE, and the two must not be the same question.
                // See SubmitTracedOnly: a reflection shows what is BEHIND the camera, so a structure
                // built only from what the frustum passed can never hold it.
                SubmitTracedOnly(
                    scene, model, entry.Displayed!, baseModelView, viewX, viewY, viewZ, cullBounds.Radius,
                    tracedOnly);
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

                        // Hidden from the camera is not hidden from a ray. A wall standing between the
                        // two is exactly what a window reflects around.
                        SubmitTracedOnly(
                            scene, model, entry.Displayed!, baseModelView,
                            viewX, viewY, viewZ, bounds.Radius, tracedOnly);
                        continue;
                    }
                }
            }

            SubsetDraw[] subsets = model.Subsets ?? ResolveSubsets(scene, model, entry.Displayed!);

            // How much of its rated output the lamp this placement IS is currently giving out. One for
            // everything that is not a lamp, and for every lamp nobody has touched, so the common case
            // is a comparison against a constant.
            float glow = model.LampFixture >= 0 && Switchboard is { } board
                ? board.LevelOf(model.LampFixture)
                : 1f;

            foreach (SubsetDraw subset in subsets)
            {
                // The glass pass takes only the glazing; every other pass takes everything, the glazing
                // included. That asymmetry is the point: a window's frame belongs in the G-buffer and its
                // pane does not, and the two are the same subset, so the opaque pass must still draw it
                // and let the alpha cut decide which half of it survives.
                if (glassOnly && !subset.Material.Glass)
                {
                    continue;
                }

                _device.SetMaterialOverride(subset.IsLamp && glow < 1f ? Dimmed(subset.Material, glow) : subset.Material);

                // A bake is multiplied by the title's gain on top of whatever headroom it was encoded
                // with; a sub-mesh with none is drawn at the headroom alone and lit -- if the title
                // lights such things at all -- by its fixed-function sun. I.G.I. 2's lightmap stage is
                // MODULATE2X and its unlightmapped objects are lit by Direct3D's vertex pipeline
                // (ObjectShading cites the state blocks); Project I.G.I. answers Default at the seam, and
                // both of these are then the calls this pass always made.
                bool baked = subset.Lightmap.IsValid;
                float scale = baked ? _lightmapScale * shading.LightmapGain : _lightmapScale;
                if (scale != appliedScale)
                {
                    _device.SetLightmapScale(scale);
                    appliedScale = scale;
                }

                int sampleCount = entry.HasRotor ? entry.Rotor.SampleCount : 1;
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    Matrix3f orientation = entry.HasRotor ? entry.Rotor.GetOrientation(sample) : entry.Orientation;
                    Matrix4f modelView = entry.HasRotor
                        ? camera.CreateModelView(entry.Position, orientation)
                        : baseModelView;

                    // The sun in the placement's own frame. An orientation's rows are the model's axes
                    // in the world, so dotting a world vector against them -- the transpose, in the
                    // engine's row-vector convention -- is what takes it into model space.
                    Vector3f sun = !baked && shading.Sun is { } sunlight
                        ? orientation.Transposed().Transform(sunlight.Direction)
                        : Vector3f.Zero;
                    if (sun != appliedSun)
                    {
                        _device.SetVertexSun(sun);
                        appliedSun = sun;
                    }

                    _device.DrawMesh(
                        model.Mesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        subset.Diffuse,
                        subset.Lightmap,
                        rotorsOnly || glassOnly ? BlendMode.AlphaBlend : BlendMode.Opaque);

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

        // The rotor pass adds to the opaque pass's tallies rather than replacing them: the two are
        // halves of one frame's world draw, and the overlay is reading the frame.
        LastDrawCount = (rotorsOnly || glassOnly ? LastDrawCount : 0) + draws;
        LastTriangleCount = (rotorsOnly || glassOnly ? LastTriangleCount : 0) + triangles;
        LastLightmappedCount = (rotorsOnly || glassOnly ? LastLightmappedCount : 0) + lit;
        LastOccludedCount = (rotorsOnly || glassOnly ? LastOccludedCount : 0) + occluded;

        // Back to one, and to no sun, for whatever draws next -- the terrain keeps its shipped LDR
        // lightmaps and lights itself.
        if (appliedScale != 1f)
        {
            _device.SetLightmapScale(1f);
        }

        if (appliedSun != Vector3f.Zero)
        {
            _device.SetVertexSun(Vector3f.Zero);
        }
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
    /// <summary>Gets whether visibility verdicts may be acted on: the view has held still long enough.</summary>
    /// <remarks>
    /// Not a test of this frame alone. A view that has just stopped still has verdicts in flight that were
    /// measured while it moved, so the count runs on for <see cref="OcclusionSettleFrames"/> frames after
    /// the last movement and only then lets the culler skip anything again.
    /// </remarks>
    private bool OcclusionVerdictsUsable(in Camera3D camera)
    {
        if (ViewMoved(camera))
        {
            _occlusionSettle = OcclusionSettleFrames;
        }
        else if (_occlusionSettle > 0)
        {
            _occlusionSettle--;
        }

        return _occlusionSettle == 0;
    }

    private bool ViewMoved(in Camera3D camera)
    {
        if (!_hasPreviousCamera)
        {
            return true;
        }

        if ((camera.Position - _previousCamera.Position).Length > OcclusionSettlePosition)
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
        return largest > OcclusionSettleOrientation;
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

            // Blown up with nothing authored to stand in its place: there is no rubble model to
            // swap to, so the placement leaves the frame entirely rather than drawing intact.
            if (Destructibles?.IsDestroyed(authored) == true && authored.DestroyedReplacement is null)
            {
                entry.Skip = true;
                continue;
            }

            entry.Skip = false;
            entry.IsRotor = authored.IsRotor;

            bool destroyed = UsesDestroyedReplacement(authored);
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

            // A fan or a radar head, which turns for ever and never leaves its spot. Same reasoning as
            // the camera above: the model and the position are settled, only the transform is per-frame.
            if (Spins?.TryGetOrientation(authored, _frameAlpha, out Matrix3f spun) == true)
            {
                entry.Displayed = entry.Displayed! with { Orientation = spun };
            }

            if (TryGetStandOrientation(authored, out Matrix3f stand))
            {
                entry.Displayed = entry.Displayed! with { Orientation = stand };
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

    /// <summary>Orders the placements by render group, then by model so consecutive draws share GPU state.</summary>
    /// <remarks>
    /// <para>
    /// <b>The group has to be the outer key.</b> The engine draws nothing directly: it enqueues each
    /// visible object into one of sixteen call queues by its group and empties them in order, so two
    /// objects in different groups are separated by everything the groups between them draw. Batching by
    /// model across that boundary would put a pane of glass before the wall behind it because the two
    /// happen to share a mesh name, which is the shape of every sorting complaint an I.G.I. 2 world can
    /// produce.
    /// </para>
    /// <para>
    /// Model stays the inner key, so the state batching survives inside each group — which is where it
    /// was doing its work anyway, since a group is a run of objects rather than a scattering.
    /// </para>
    /// <para>
    /// <b>Project I.G.I. is unaffected to the bit.</b> No shipped level of that game authors a group, so
    /// every placement carries <see cref="WorldPlacement.DefaultRenderGroup"/>, the first comparison is
    /// always nought, and the order is the one this produced before the key existed.
    /// </para>
    /// </remarks>
    internal static int[] BuildDrawOrder(WorldScene scene)
    {
        int[] order = new int[scene.Placements.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            int byGroup = scene.Placements[left].RenderGroup.CompareTo(scene.Placements[right].RenderGroup);
            if (byGroup != 0)
            {
                return byGroup;
            }

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

    /// <summary>The same surface with its lamp turned down, or turned off altogether.</summary>
    /// <remarks>
    /// <para>
    /// Emission is not a brightness on this path — it is a SHADING MODEL. A surface with any emission at
    /// all is written to the G-buffer as unlit and comes out of the lighting pass as its own albedo
    /// times that number, so scaling it towards zero does not fade a panel back into the room: it fades
    /// it to black, and a switched-off lamp in a lit corridor ends up a hole in the wall.
    /// </para>
    /// <para>
    /// So the last part of the fade is a handover rather than a ramp. Below the threshold the emission
    /// is dropped outright and the panel becomes an ordinary lit surface, which is what a dead fitting
    /// is. The threshold is low — under a third of albedo, dimmer than the same panel would be lit in
    /// any room worth lighting — so the step it costs is smaller than the step of leaving it out, and it
    /// lands two hundred milliseconds into a fade nobody is looking at the panel during.
    /// </para>
    /// <para>
    /// It also has to clear <see cref="SurfaceMaterial.ExcludedFromOcclusion"/>, which is a quarter and
    /// is not a brightness at all: the G-buffer has no flags channel, so that one value in the emissive
    /// slot means "leave this surface out of the ambient occlusion". A lamp allowed to fade through it
    /// would spend a frame claiming to be that instead.
    /// </para>
    /// </remarks>
    private static SurfaceMaterial Dimmed(SurfaceMaterial material, float glow)
    {
        float emissive = material.Emissive * glow;

        return material with { Emissive = emissive < 0.3f ? 0f : emissive };
    }

    /// <summary>Offers a placement the camera will not draw to the traced world anyway.</summary>
    /// <param name="scene">The level, for resolving the model's subsets.</param>
    /// <param name="model">The model that was culled.</param>
    /// <param name="displayed">Its placement as displayed.</param>
    /// <param name="modelView">Where it stands, relative to the camera.</param>
    /// <param name="viewX">Its bounding sphere's centre in view space.</param>
    /// <param name="viewY">Its bounding sphere's centre in view space.</param>
    /// <param name="viewZ">Its bounding sphere's centre in view space.</param>
    /// <param name="radius">That sphere's radius, in metres.</param>
    /// <param name="wanted">Whether this pass is the one that feeds the traced world at all.</param>
    /// <remarks>
    /// <para>
    /// <b>The bug this exists for.</b> The acceleration structure was filled from the DRAW path, so it
    /// held exactly what the camera had at some point had in frame. A window reflects what is behind the
    /// camera, which by definition is the geometry the frustum just threw away — so a pane could only
    /// ever show a place the player had already stood and looked at. Measured at one window on mission
    /// one: 82 geometries and 587 placements in the structure facing the glass, against 150 and 1800
    /// after sweeping the camera once across the yard behind it. The same window therefore reflected
    /// different things in two sessions, and more of the level the longer one played — which is not a
    /// tuning problem, it is the reflection being a function of where the player has looked.
    /// </para>
    /// <para>
    /// <b>The rule.</b> Visibility decides what is DRAWN; the whole level is TRACED. Every placement is
    /// offered every frame whether the camera can see it or not, so the traced world holds the level's
    /// static geometry entire and keeps each placement exactly as long as it is still there.
    /// </para>
    /// <para>
    /// Cheap: the backend's submission returns at once when there is no traced world, and for a placement
    /// it already holds the whole cost is a hash lookup per subset.
    /// </para>
    /// </remarks>
    private void SubmitTracedOnly(
        WorldScene scene,
        ResolvedModel model,
        WorldPlacement displayed,
        in Matrix4f modelView,
        float viewX,
        float viewY,
        float viewZ,
        float radius,
        bool wanted)
    {
        if (!wanted)
        {
            return;
        }

        // EVERY placement, however far or small, every frame. The traced world keeps a static placement
        // only for as long as it goes on being offered (VulkanWorldScene), which is what lets an object
        // that moves leave the structure at once instead of leaving a trail -- so anything not offered
        // here disappears from every ray. It used to be culled at three hundred metres and a degree of
        // arc, the reference's own culling; but the reference caches every static instance up front
        // and culls per frame against that, whereas here what was not offered was never admitted: the
        // props, fittings and shells around the player's start were judged from the loading-screen
        // camera, arrived only once play began, and the lighting crept across the room as they did.

        SubsetDraw[] subsets = model.Subsets ?? ResolveSubsets(scene, model, displayed);
        foreach (SubsetDraw subset in subsets)
        {
            _device.SubmitTracedMesh(
                model.Mesh, subset.IndexOffset, subset.IndexCount, modelView, subset.Diffuse);
        }
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
        bool anyLamp = false;
        foreach (MeshSubset subset in model.Geometry.Subsets)
        {
            if (subset.IndexCount == 0)
            {
                continue;
            }

            TextureHandle diffuse = TextureHandle.None;
            SurfaceMaterial material = _materials.Default;
            bool lamp = false;

            if (subset.HasMaterial
                && displayed.Pack.TryGetTextureName(displayed.Instance, subset.MaterialSlot, out string texture))
            {
                diffuse = GetTexture(texture);

                // What this surface is made of. A sub-mesh that names no texture takes the default
                // rather than inheriting the previous draw's, exactly as the inline loop did.
                material = _materials.For(texture);

                // ...and whether this particular sub-mesh is a lamp, which is not a property of the
                // texture. A texture is shared -- the page that skins a ceiling tube also skins the panel
                // it hangs from and, in level 1, the side of an office cabinet -- so making a texture
                // emissive glowed a whole garage soffit and put a flat white slab down that cabinet.
                // Size alone does not save it either: the cabinet's panel is smaller than a fluorescent
                // batten. Only the model and material slot the detector or the manifest named will do.
                //
                // The strength comes from there too, because one texture is not one brightness: a
                // floodlight and the lamp over a door draw with the same page and are not the same light.
                //
                // Never over an authored material. A materials.json entry saying a surface glows is
                // somebody having looked at it, and nothing worked out here overrules that.
                if (material.Emissive <= 0f
                    && Lamps.TryGetStrength(displayed.Instance.Name, subset.MaterialSlot, out float glow))
                {
                    material = material with { Emissive = glow };

                    // ...and this, and only this, is what a wall switch may turn down. An authored
                    // emissive above -- a screen, a sign, the sky dome -- is somebody having decided that
                    // surface gives off light, and it is not a lamp anybody can switch off.
                    lamp = true;
                    anyLamp = true;
                }
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
                lightmap,
                lamp);
        }

        // Which of the level's lights this placement is the housing of, so that switching that light off
        // stops this geometry glowing. Resolved here rather than in the switchboard because here is where
        // the placement is already known to draw a lamp at all -- that is a fact about the model's
        // sub-meshes, and asking every placement in the level how near a fixture it stands would ask it
        // of five hundred crates as well.
        //
        // A generous radius: a fixture is the merged centroid of a whole cluster of emitting triangles,
        // and a long batten's centroid can sit a couple of metres from the origin of the placement that
        // draws part of it. Wrong only where two unrelated lamps stand within a few metres of each other
        // and were still not merged, which is a lamp switching its neighbour off -- visible, harmless,
        // and rarer than the alternative of a switch that does nothing.
        model.LampFixture = anyLamp && Switchboard is { } board
            ? board.NearestFixture(displayed.Position, LampBindingRadius)
            : -1;

        model.Subsets = subsets;

        bool glass = false;
        foreach (SubsetDraw drawn in subsets)
        {
            glass |= drawn.Material.Glass;
        }

        model.HasGlass = glass;
        return subsets;
    }

    /// <summary>One placement's camera-independent state for the current frame.</summary>
    private struct FrameEntry
    {
        public bool Skip;
        public bool WasDestroyed;
        public bool HasRotor;
        public bool IsRotor;
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

        /// <summary>Gets or sets whether any of those subsets is glazing.</summary>
        /// <remarks>
        /// So the glass pass can reject a placement before it frustum-tests it. Almost nothing in a level
        /// is a window, and without this the pass would walk every crate, tree and oil drum in the draw
        /// order to find the handful of models that have a pane in them.
        /// </remarks>
        public bool HasGlass { get; set; }

        /// <summary>The light this placement is the fixture of, or -1 when it is not one.</summary>
        /// <remarks>Resolved with the subsets, and melted with them whenever the switchboard changes.</remarks>
        public int LampFixture { get; set; } = -1;

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
    /// <remarks>
    /// <c>IsLamp</c> says the sub-mesh glows because the lamp detection called it a fixture, as opposed
    /// to because a material file said the surface emits. Only the first kind answers to a light switch:
    /// an authored emissive is a screen or a sign or the sky, and nobody is switching those off.
    /// </remarks>
    private readonly record struct SubsetDraw(
        int IndexOffset,
        int IndexCount,
        int TriangleCount,
        TextureHandle Diffuse,
        SurfaceMaterial Material,
        TextureHandle Lightmap,
        bool IsLamp = false);

    // A StationaryGun's stand turns with its gun: 0x474420 writes the traverse into the task's own
    // orientation (+112, the authored one times Z(traverse)), and that is what the stand model is drawn
    // with. Only the traverse -- the elevation belongs to the gun alone -- so the yoke stays square to
    // the gun it holds.
    private bool TryGetStandOrientation(WorldPlacement placement, out Matrix3f orientation)
    {
        // The type test first: TryGet walks the level's interactions, and this runs per placement.
        if (placement.Owner is { } owner
            && string.Equals(owner.Type.Name, "StationaryGun", StringComparison.Ordinal)
            && Interactions?.TryGet(owner, out WorldInteraction? interaction) == true
            && interaction?.StationaryGun is { } gun)
        {
            orientation = gun.TurnedOrientation;
            return true;
        }

        orientation = default;
        return false;
    }

    private WorldPlacement DisplayedPlacement(WorldPlacement placement)
    {
        WorldPlacement displayed = placement;
        if (placement.DestroyedReplacement is { } replacement && UsesDestroyedReplacement(placement))
        {
            displayed = placement with
            {
                Instance = replacement.Instance,
                Pack = replacement.Pack,
                Model = replacement.Model,
            };
        }

        if (Interactions?.TryGet(placement.Owner, out WorldInteraction? interaction) == true
            && interaction is { IsDestroyed: true })
        {
            displayed = placement.DestroyedReplacement is { } genericReplacement
                ? placement with { Instance = genericReplacement.Instance, Pack = genericReplacement.Pack, Model = genericReplacement.Model }
                : placement with { IsSuppressed = true };
        }

        if (SecurityCameras?.TryGetHeadOrientation(
            placement,
            _frameAlpha,
            _frameSmooth,
            out Matrix3f orientation) == true)
        {
            displayed = displayed with { Orientation = orientation };
        }

        if (Spins?.TryGetOrientation(placement, _frameAlpha, out Matrix3f spun) == true)
        {
            displayed = displayed with { Orientation = spun };
        }

        if (TryGetStandOrientation(placement, out Matrix3f stand))
        {
            displayed = displayed with { Orientation = stand };
        }

        return displayed;
    }

    /// <summary>Whether the renderer must swap an authored placement for its destroyed-model binding.</summary>
    private bool UsesDestroyedReplacement(WorldPlacement placement) =>
        placement.DestroyedReplacement is not null
        && (AlarmSpeakers?.IsDestroyed(placement.Owner) == true
            || Destructibles?.IsDestroyed(placement) == true);

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
        _bakedPages.Clear();
        _meshes.Clear();
        _textures.Clear();
        _geometry.Clear();
        _resolved.Clear();
        _resolvedDestroyed.Clear();
    }

    /// <summary>Overrides the retail object lightmaps with a path-traced bake, applied on the next draw.</summary>
    /// <remarks>
    /// The pages are already composited and encoded (<see cref="BakedLightmaps"/>); this only takes their
    /// pixels and drops any lightmap textures already uploaded from the shipped archive, so the next draw
    /// rebuilds them from the bake. A page whose size disagrees with the shipped one is ignored at upload.
    /// <see langword="null"/> clears any override, restoring the shipped lightmaps.
    /// </remarks>
    /// <param name="baked">The composited pages, or <see langword="null"/> to restore the shipped lightmaps.</param>
    /// <param name="discardRetail">
    /// When <see langword="true"/>, a lightmapped page with no bake reads black rather than the shipped one,
    /// so only our own lighting shows.
    /// </param>
    public void SetBakedLightmaps(BakedLightmaps? baked, bool discardRetail = false)
    {
        _bakedPages.Clear();

        // Independent of whether a bake has landed yet: under the exclusive mode a surface with no bake
        // reads black even while the worker is still tracing, so the shipped lightmaps never flash up first.
        _discardRetailLightmaps = discardRetail;

        // The exclusive path carries our own high-dynamic-range lighting, so its pages are encoded with the
        // headroom and the shader scales them back up; the fallback path mixes with the shipped LDR pages,
        // so it clamps to one and scales by one.
        _lightmapScale = discardRetail ? HdrHeadroom : 1f;

        if (baked is not null)
        {
            foreach ((string entryName, Dictionary<int, BakedLightmaps.Page> pages) in baked.Pages)
            {
                Dictionary<int, Bgra32[]> entry = new(pages.Count);
                foreach ((int index, BakedLightmaps.Page page) in pages)
                {
                    Vector3f[] radiance = page.Radiance;
                    Bgra32[] pixels = new Bgra32[radiance.Length];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Vector3f r = radiance[i];
                        pixels[i] = new Bgra32(
                            r: LinearToSrgb(r.X / _lightmapScale),
                            g: LinearToSrgb(r.Y / _lightmapScale),
                            b: LinearToSrgb(r.Z / _lightmapScale),
                            a: 255);
                    }

                    entry[index] = pixels;
                }

                _bakedPages[entryName] = entry;
            }
        }

        foreach (TextureHandle[] uploaded in _lightmaps.Values)
        {
            foreach (TextureHandle page in uploaded)
            {
                if (page.IsValid)
                {
                    _device.DestroyTexture(page);
                }
            }
        }

        _lightmaps.Clear();

        // Each placement caches the lightmap handles it resolved into its per-subset draw state, and those
        // now point at textures just destroyed. Drop the resolutions so the next draw rebuilds them against
        // the swapped-in pages -- without this the frame the async bake lands on draws a dangling handle and
        // the device rejects it.
        DestroyResolvedQueries();
        _resolved.Clear();
        _resolvedDestroyed.Clear();

        _logger.Information(
            LogCategory,
            baked is null
                ? "Restored the shipped object lightmaps."
                : $"Applied a path-traced lightmap bake over {_bakedPages.Count} object(s).");
    }

    // Encodes one linear channel to sRGB, clamped to the displayable range. The page is decoded back to
    // linear in the G-buffer, so this is only the storage encoding; the HDR headroom is carried separately.
    private static byte LinearToSrgb(float linear)
    {
        float c = Math.Clamp(linear, 0f, 1f);
        float s = c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(s * 255f), 0, 255);
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

    /// <summary>Returns a rotor's bound across every phase and aerodynamic-tilt sample.</summary>
    /// <remarks>
    /// Rotor blur rotates the mesh around its local origin, so its usual off-origin bound centre moves
    /// over a circle. Centering the cull sphere at that origin and extending it by the centre's radius
    /// encloses every orientation without selecting a particular blade sample. That retains the full
    /// retail blur when visible and lets ordinary frustum culling reject a helicopter behind the camera.
    /// </remarks>
    private static BoundingSphere RotorCullBounds(in BoundingSphere bounds) =>
        new(Vector3f.Zero, bounds.Centre.Length + bounds.Radius);


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
        else if (RotatingObjects?.TryGetDrawTransform(
            placement,
            _frameAlpha,
            _frameSmooth,
            out Vector3d rotatingPosition,
            out Matrix3f rotatingOrientation) == true)
        {
            drawPosition = rotatingPosition;
            drawOrientation = rotatingOrientation;
        }
    }

    /// <inheritdoc/>
    public PlacementPose DrawPoses => _drawPoses ??= TryGetDrawTransform;

    // The transform a placement is drawn with this frame, through the same precedence the draw loop
    // and the selection outline use. A rotor has no single orientation mid-blur; nothing on one takes a
    // mark (0x42D416), so its authored orientation stands.
    private bool TryGetDrawTransform(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ResolveDrawTransform(DisplayedPlacement(placement), out positionMetres, out orientation, out _, out _);
        return true;
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
                ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data, _formats);
                pages = new TextureHandle[lightmap.Images.Count];
                for (int i = 0; i < pages.Length; i++)
                {
                    ObjectLightmapImage image = lightmap.Images[i];

                    // A path-traced bake, if one was applied, replaces the shipped page's pixels here --
                    // same dimensions, same upload, so nothing downstream knows the difference. Falls back
                    // to the retail page when this entry or page was not baked, or the sizes disagree --
                    // unless the bake is exclusive, in which case an uncovered page reads black so only our
                    // own lighting shows.
                    ReadOnlySpan<Bgra32> pixels = MemoryMarshal.Cast<byte, Bgra32>(image.Pixels.Span);
                    if (_bakedPages.TryGetValue(entryName, out Dictionary<int, Bgra32[]>? bakedEntry)
                        && bakedEntry.TryGetValue(i, out Bgra32[]? bakedPage)
                        && bakedPage.Length == image.Width * image.Height)
                    {
                        pixels = bakedPage;
                    }
                    else if (_discardRetailLightmaps)
                    {
                        pixels = new Bgra32[image.Width * image.Height];
                    }

                    // A page is an atlas of this model's own surfaces, so it clamps rather than repeats:
                    // wrapping would bleed one surface's light onto another at the edges.
                    pages[i] = _device.CreateTexture(
                        new TextureDescription(
                            image.Width,
                            image.Height,
                            1,
                            TextureFilter.Linear,
                            TextureAddressMode.Clamp),
                        pixels);
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

    /// <summary>Gets or sets which of the level's sub-meshes are light fixtures.</summary>
    /// <remarks>
    /// Set per level beside <see cref="Materials"/>, and for the same reason: it is the shell's
    /// conclusion about the level, not something a renderer can work out from a draw. It decides where a
    /// DETECTED emitter is allowed to glow -- see the resolve, and <see cref="LampDetector"/> for why the
    /// texture alone cannot answer that. Empty is the honest default: nothing detected glows.
    /// </remarks>
    public LampDetector.Lamps Lamps { get; set; } = LampDetector.Lamps.None;

    /// <summary>Gets or sets which of the level's lamps are currently lit, or <see langword="null"/> for all of them.</summary>
    /// <remarks>
    /// Set per level beside <see cref="Lamps"/>, which decides where a lamp is; this decides whether it
    /// is on. Null is the level as it ships — every fixture at full output, and not one comparison per
    /// draw spent asking.
    /// </remarks>
    public LampSwitchboard? Switchboard
    {
        get => _switchboard;
        set
        {
            if (ReferenceEquals(_switchboard, value))
            {
                return;
            }

            _switchboard = value;

            // Each placement's binding to the light it houses is resolved with its subsets, so a new
            // board has to melt them for the same reason a reloaded material library does. They
            // re-resolve on the next draw at the cost of a dictionary lookup each.
            MeltResolvedSubsets();
        }
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
            MeltResolvedSubsets();
        }
    }

    /// <summary>Drops every placement's resolved per-subset state, so the next draw resolves it again.</summary>
    private void MeltResolvedSubsets()
    {
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

    /// <summary>Drops every uploaded diffuse texture so the next draw loads them all again.</summary>
    /// <remarks>
    /// <para>
    /// For swapping the upscaled pack in and out of a level that is already running, which is a
    /// comparison tool rather than a feature: the two pictures have to be the same frame, and a reload
    /// between them is the only way to be sure nothing else moved.
    /// </para>
    /// <para>
    /// <b>Clearing the dictionary is not enough, and that is the whole subtlety.</b> A resolved model
    /// caches its subsets, and a <c>SubsetDraw</c> carries its diffuse <i>by value</i> — so a texture
    /// dropped here would still be drawn from the cached draw list, by a handle that no longer exists.
    /// <see cref="MeltResolvedSubsets"/> is the existing answer to exactly that, already used when the
    /// material library or the switchboard is replaced; the subsets re-resolve on next use at the cost of
    /// a dictionary lookup.
    /// </para>
    /// <para>
    /// <b>And the decode cache has to go with them.</b> It holds images decoded on workers during the
    /// load, and <see cref="GetTexture"/> prefers it over the archive — so leaving it would hand back the
    /// very pixels this is trying to replace. It is an optimisation with no semantics of its own, so
    /// dropping it costs a slower reload and nothing else.
    /// </para>
    /// <para>
    /// Safe mid-frame: <c>DestroyTexture</c> queues the image and its descriptor sets for deletion once
    /// the frames that may still name them have cycled, rather than freeing under a frame in flight.
    /// </para>
    /// </remarks>
    public void ReloadTextures()
    {
        foreach (TextureHandle texture in _textures.Values)
        {
            if (texture.IsValid)
            {
                _device.DestroyTexture(texture);
            }
        }

        _textures.Clear();

        // A name that had no texture before may well have one now, and the other way round.
        _missingTextures.Clear();

        Decoded = null;
        MeltResolvedSubsets();
    }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>
    /// Settable for the same reason <see cref="Materials"/> is, and owned by the shell rather than by
    /// this renderer: the same pack serves the world, the soldiers and the terrain, and each of them
    /// loads its own textures.
    /// </remarks>
    public MaterialDetailPack? DetailMaps { get; set; }

    /// <summary>Gets or sets the level's textures, decoded ahead of this renderer on worker threads.</summary>
    /// <remarks>
    /// Optional and purely a speed-up: with none set every texture is decoded on the thread that first
    /// draws it, which is what this renderer always did. See <see cref="TextureDecodeCache"/>.
    /// </remarks>
    public TextureDecodeCache? Decoded { get; set; }


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

        // Decoded on a worker while the backdrop was being built, if one was offered. Everything from
        // the file down to the corrected mip chain has then already happened, and what is left here is
        // the upload -- which is the only part that had to be on this thread in the first place.
        if (Decoded is not null && Decoded.TryTake(name, out TextureImage? ready, out TextureImage? readyDetail))
        {
            TextureHandle prepared = TextureHandle.None;
            if (ready is not null)
            {
                try
                {
                    prepared = CreateFromDecoded(name, ready, readyDetail);
                }
                catch (GraphicsDeviceException ex)
                {
                    _logger.Warning(LogCategory, $"Texture '{name}' did not load: {ex.Message}");
                }
            }

            _textures[name] = prepared;
            return prepared;
        }

        TextureHandle handle = TextureHandle.None;
        string resource = LoadedPack.TextureResourceName(name);

        if (_resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            try
            {
                // An upscaled replacement if the player built one, the game's own texture otherwise. Asked
                // only for names the registry HAS -- a name the level never declared is still one the level
                // never declared, and letting a stray PNG conjure one would let a pack add textures the game
                // does not ask for. Normally the decode cache has already done this on a worker; this is the
                // same substitution for whatever the cache never saw, so the two paths cannot disagree.
                SurfaceAsset asset = SurfaceAssetReader.Read(bytes.Span);
                TextureImage image = asset.Diffuse;

                // The wire of a fence, the slats of a grille, the leaves of a canopy: everything drawn as
                // a cut-out quad loses its coverage as the chain averages down, so it thins with distance
                // and flickers as the level crosses over. Restored before the chain is uploaded, against
                // the same cutoff the shaders test. Textures that are opaque, or that are entirely holes,
                // are left alone -- there is no coverage to preserve and scaling would corrupt a channel
                // nothing reads.
                // Most of this game's textures carry a single level, and this backend generates none --
                // so without this the whole world was sampled at full resolution however far away it was,
                // which sparkles on every surface and makes thin things crawl.
                // A cut-out's partly-covered edge texels are drawn by the reference and discarded by us,
                // which is why its barbed wire is visibly thicker than ours against the same sky. Widened
                // first, so the chain below filters the alpha we actually intend to keep. Real
                // transparencies -- window glass above all -- are classified out and left alone.
                // Classified from level zero before the alpha is touched. A cut-out needs its chain rebuilt
                // even when it shipped with one: a chain imported by the original tool carries the black of
                // the transparent gaps in every level, so the wire goes dark at distance -- the far fences
                // that read pure black under a render scale. Rebuilding from level zero runs the alpha-
                // weighted filter that keeps the wire's colour; an opaque surface keeps whatever chain it
                // has and only generates one if it shipped without.
                bool cutout = AlphaCoverage.IsCutout(image.GetLevel(0));

                image.WidenCutoutAlpha(TextureDecodeCache.AlphaTestCutoff);

                image = cutout ? image.RebuildMipChain() : image.WithMipChain();
                image.PreserveAlphaCoverage(TextureDecodeCache.AlphaTestCutoff);

                handle = CreateFromDecoded(name, image, asset.Detail);
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

    /// <summary>Uploads an already-decoded image and registers whatever derives from it.</summary>
    /// <param name="name">The texture's name, which the detail pack keys its maps by.</param>
    /// <param name="image">The decoded chain, from the cache or from this thread.</param>
    /// <param name="detail">
    /// The packed surface map the asset carried, or <see langword="null"/> when it carried none — which
    /// is every texture the game itself ships, and every replacement built without them.
    /// </param>
    /// <returns>The uploaded texture.</returns>
    /// <remarks>
    /// The half of <see cref="GetTexture"/> that has to be on the render thread, split out so the
    /// decoded-ahead path and the decode-here path upload through exactly the same code.
    /// </remarks>
    private TextureHandle CreateFromDecoded(string name, TextureImage image, TextureImage? detail = null)
    {
        TextureHandle handle = _device.CreateTexture(
            new TextureDescription(image.Width, image.Height, image.MipLevels, TextureFilter.Linear, TextureAddressMode.Repeat),
            image.Pixels);

        // The derived normal, roughness and occlusion this game never shipped. Registered against the
        // texture rather than carried through the draw, so nothing below this line knows the maps exist.
        //
        // The asset's OWN map wins when it carries one: a replacement in the overrides folder holds the
        // map derived from the picture it replaces, at the same resolution and by a better estimator than
        // the one this process can run between frames. Without one, the top level goes to the pack so a
        // texture nothing has derived for can derive its own instead of staying flat forever.
        if (detail is not null)
        {
            DetailMaps?.AttachPacked(handle, detail.GetLevel(0), detail.Width, detail.Height);
        }
        else
        {
            DetailMaps?.Attach(name, handle, image.GetLevel(0), image.Width, image.Height);
        }

        return handle;
    }
}
