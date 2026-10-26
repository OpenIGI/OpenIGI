using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;

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
public sealed partial class WorldRenderer : IWorldPresenter
{
    /// <summary>The alpha a cut-out surface has to reach to be drawn, matching what the shaders test.</summary>
    /// <remarks>
    /// Kept beside the texture load because it is what the mip chain's coverage is preserved AGAINST: the
    /// scale chosen per level is the one that keeps the same fraction of texels above this number, so a
    /// value here that disagrees with the shaders would preserve the wrong quantity and thin the fence
    /// anyway. Change both together.
    /// </remarks>
    internal const string LogCategory = "World";

    private readonly IGraphicsDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly WorldTextureCache _textures;
    private readonly WorldLightmapPages _lightmapPages;
    private readonly WorldPlacementTransforms _transforms = new();
    private readonly WorldSubsetResolver _subsets;
    private readonly WorldFrameState _frameState;

    /// <summary>Scratch for the selection glow's subset ranges, so outlining allocates nothing.</summary>
    private readonly List<MeshDrawRange> _outlineRanges = [];

    /// <inheritdoc/>
    public ObjectShading Shading { get; set; } = ObjectShading.Default;

    /// <inheritdoc/>
    public ModelLodPolicy? Lod { get; set; }

    // The glass pass's own order, rebuilt per frame. See the comment where it is filled.
    private readonly List<int> _glassOrder = [];
    private PlacementPose? _drawPoses;
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
    private readonly OcclusionSettle _occlusionSettle = new(windowed: false);

    /// <summary>Initializes a new instance of the <see cref="WorldRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="resources">Where texture bytes are read from.</param>
    /// <param name="formats">The title's format policy; a baked lightmap's descriptor stride differs.</param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <param name="additiveFaceGroups">
    /// How an additive face group is added under <see cref="FaceGroupBlending"/>: Project I.G.I. adds its whole colour
    /// (<c>ONE, ONE</c>, <c>0x49EC70</c>), I.G.I. 2 weights it by alpha (<c>Direct3DRender_SetBlendStateAdditive</c>).
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The profile is the only title-shaped format this renderer takes, and it takes it because a
    /// <c>.olm</c> entry cannot be read without knowing which game wrote it — the descriptor is 44
    /// bytes in Project I.G.I. and 52 in the sequel, and the stride decides where the pixels start.
    /// It is a parameter rather than a default so that a title cannot forget to say.
    /// </remarks>
    public WorldRenderer(
        IGraphicsDevice device,
        ResourceRegistry resources,
        AssetFormatProfile formats,
        IOpenIgiLogger? logger = null,
        BlendMode additiveFaceGroups = BlendMode.Additive)
    {
        _additiveFaceGroups = additiveFaceGroups;
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(formats);

        _device = device;
        _logger = logger ?? NullLogger.Instance;
        _textures = new WorldTextureCache(device, resources, _logger);
        _lightmapPages = new WorldLightmapPages(device, resources, formats, _logger);
        _subsets = new WorldSubsetResolver(device, _logger, _transforms, _textures, _lightmapPages);
        _frameState = new WorldFrameState(_transforms, _subsets, _occlusionSettle);
    }

    // How the title adds an additive face group: Project I.G.I. at ONE, ONE (0x49EC70), I.G.I. 2 at SRCALPHA, ONE.
    private readonly BlendMode _additiveFaceGroups;

    /// <inheritdoc/>
    public bool FaceGroupBlending { get; set; }

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
    public DoorRegistry? Doors { get => _transforms.Doors; set => _transforms.Doors = value; }

    /// <summary>Gets or sets the live elevator transforms applied to lift placements.</summary>
    public ElevatorRegistry? Elevators { get => _transforms.Elevators; set => _transforms.Elevators = value; }

    /// <summary>Gets or sets the live destructible alarm objects that select destroyed model variants.</summary>
    public AlarmSpeakerRegistry? AlarmSpeakers { get => _transforms.AlarmSpeakers; set => _transforms.AlarmSpeakers = value; }

    /// <summary>Gets or sets the live retail glass and explosive-object presentation state.</summary>
    public DestructibleObjectRegistry? Destructibles { get => _transforms.Destructibles; set => _transforms.Destructibles = value; }

    /// <summary>Gets or sets the live articulated security-camera transforms.</summary>
    public SecurityCameraRegistry? SecurityCameras { get => _transforms.SecurityCameras; set => _transforms.SecurityCameras = value; }

    /// <inheritdoc/>
    public IPlacementSpin? Spins { get => _transforms.Spins; set => _transforms.Spins = value; }

    /// <inheritdoc/>
    public IPlacementMotion? Motions { get => _transforms.Motions; set => _transforms.Motions = value; }

    /// <inheritdoc/>
    public IPlacementDestruction? Destruction { get => _transforms.Destruction; set => _transforms.Destruction = value; }

    /// <inheritdoc/>
    public IPlacementVariant? Variants { get => _transforms.Variants; set => _transforms.Variants = value; }

    /// <summary>Gets or sets the authored cinematic runtime that supplies live vehicle transforms.</summary>
    public CutsceneRuntime? Cutscenes { get => _transforms.Cutscenes; set => _transforms.Cutscenes = value; }

    /// <summary>Gets or sets the live generic pickups whose consumed models no longer draw.</summary>
    public GenericPickupRegistry? GenericPickups { get => _frameState.GenericPickups; set => _frameState.GenericPickups = value; }

    /// <summary>Gets or sets the level's interactions: turning gun stands and the charges a plant has moved.</summary>
    public WorldInteractionRegistry? Interactions { get => _transforms.Interactions; set => _transforms.Interactions = value; }

    /// <summary>Gets or sets the live rotating world objects (radar dishes and rotating tasks).</summary>
    public RotatingObjectRegistry? RotatingObjects { get => _transforms.RotatingObjects; set => _transforms.RotatingObjects = value; }

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
            options.GlassOnly,
            options.FromEye,
            options.BlendedOnly);

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
        _transforms.FrameAlpha = Math.Clamp(tickFraction, 0f, 1f);
        _transforms.FrameSmooth = smooth;
        _frameState.Invalidate();
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

        _subsets.MakeResident(scene, scene.Placements[index]);
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
    /// <param name="fromEye">
    /// Whether the pass is drawn from the frame's eye, so a placement the title's culling sees through a window is scissored to it
    /// (<see cref="IPlacementVisibility.ClipOf"/>). A light's pass is never narrowed.
    /// </param>
    /// <param name="blendedOnly">Draws only the face groups the original sorts and blends, over the opaque world.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        WorldScene scene,
        in Camera3D camera,
        in Matrix4f projection,
        float minimumRadius = 0f,
        bool occlusionCulling = false,
        bool rotorsOnly = false,
        bool glassOnly = false,
        bool fromEye = false,
        bool blendedOnly = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _frameState.EnsurePrepared(scene);
        WorldFrameEntry[] frame = _frameState.Entries;
        int[] drawOrder = _frameState.DrawOrder;

        _device.SetProjection(projection);
        _device.SetDepthState(rotorsOnly || glassOnly || blendedOnly ? DepthState.TestOnly : DepthState.TestAndWrite);

        // The original's face group states, when they are asked for: no alpha test on an opaque group, alpha over 64 on an
        // alpha-tested one (Direct3DRender_SetBlendStateAlphaTest), and alpha over one on the groups the blended pass draws
        // (ALPHAREF 1: 0x49EC70, Direct3DRender_SetBlendStateTransparent). The test is GREATER and the device's is at least, so
        // each reference is one more.
        bool faceGroups = FaceGroupBlending && !rotorsOnly && !glassOnly;
        byte appliedReference = IGraphicsDevice.DefaultAlphaReference;

        // Interior sectors and meshes without authored normals have no reliable outward facing.
        // Choose culling per mesh below, including when an archived r_world_cull enables it.
        bool cullBackFaces = GeometryCVars.WorldCull.Value != 0;
        CullMode appliedCull = CullMode.None;
        _device.SetCullMode(appliedCull);

        // The lightmap headroom for the objects' pages -- one unless an exclusive HDR bake is applied.
        // Reset to one at the end of the pass so the terrain, which keeps its shipped LDR lightmaps, is not
        // scaled by it.
        _device.SetLightmapScale(_lightmapPages.Scale);

        // What this pass composites with, read once: the title's lightmap gain and, if it has one, the
        // sun its unlightmapped objects are lit by. Both are applied per sub-mesh below and tracked so
        // the device is told only when something changes.
        ObjectShading shading = Shading;
        float appliedScale = _lightmapPages.Scale;
        Vector3f appliedSun = Vector3f.Zero;
        bool appliedMotion = false;
        ViewClip? appliedClip = null;

        int draws = 0;
        int triangles = 0;
        int lit = 0;
        int occluded = 0;
        bool bypassOcclusion = !occlusionCulling || !_occlusionSettle.VerdictsUsable(camera);
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
        bool tracedOnly = !glassOnly && !rotorsOnly && !blendedOnly && RayTracingCVars.TracedWorldRadius.Value > 0f;

        ReadOnlySpan<int> order = drawOrder;
        if (glassOnly)
        {
            _glassOrder.Clear();
            for (int i = 0; i < drawOrder.Length; i++)
            {
                int candidate = drawOrder[i];
                ref WorldFrameEntry glass = ref frame[candidate];
                if (!glass.Skip && glass.Model is { HasGlass: true })
                {
                    _glassOrder.Add(candidate);
                }
            }

            Vector3d eye = camera.Position;
            _glassOrder.Sort((left, right) =>
                (frame[right].Position - eye).LengthSquared.CompareTo(
                    (frame[left].Position - eye).LengthSquared));

            order = CollectionsMarshal.AsSpan(_glassOrder);
        }

        // The view's horizontal half-angle tangent, which a level of detail is picked against.
        float lodFieldOfView = projection.M11 != 0f ? 1f / projection.M11 : 1f;
        foreach (int index in order)
        {
            ref WorldFrameEntry entry = ref frame[index];
            if (entry.Skip || entry.Model is not { } model)
            {
                continue;
            }

            // The level this view draws the model at, as the original picks it for each view (0x4CFCB0); beyond its cutoff
            // it is not drawn at all.
            WorldPlacement displayed = entry.Displayed!;
            if (Lod is { } lod && !entry.HasRotor && displayed.BentGeometry is null && entry.Variant < 0 && !entry.WasDestroyed)
            {
                int level = lod.Select(scene.Placements[index], entry.Position, camera, lodFieldOfView);
                if (level < 0)
                {
                    continue;
                }

                if (level > 0 && lod.Level(displayed, level) is { } lower && _subsets.ResolveLevel(lower) is { } lowerModel)
                {
                    displayed = lower;
                    model = lowerModel;
                }
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

            // The original's look fogs each object along its own sphere (Fog_SetRange).
            if (FaceGroupBlending)
            {
                float depth = baseModelView.TransformPoint(bounds.Centre).Z;
                _device.SetObjectFogRange(depth - bounds.Radius, depth + bounds.Radius);
            }

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
                    scene, model, displayed, baseModelView, viewX, viewY, viewZ, cullBounds.Radius,
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
                    if (!visible || ((_frameState.FrameNumber + model.Query.Id) % VisibleRetestInterval) == 0)
                    {
                        _queryQueue.Add(index);
                    }

                    if (!visible)
                    {
                        occluded++;

                        // Hidden from the camera is not hidden from a ray. A wall standing between the
                        // two is exactly what a window reflects around.
                        SubmitTracedOnly(
                            scene, model, displayed, baseModelView,
                            viewX, viewY, viewZ, bounds.Radius, tracedOnly);
                        continue;
                    }
                }
            }

            if (!ApplyClip(entry, fromEye, ref appliedClip))
            {
                continue;
            }

            WorldSubsetDraw[] subsets = model.Subsets ?? _subsets.ResolveSubsets(scene, model, displayed);

            CullMode cull = cullBackFaces && model.CanCullBackFaces ? CullMode.CounterClockwise : CullMode.None;
            if (cull != appliedCull)
            {
                _device.SetCullMode(cull);
                appliedCull = cull;
            }

            // Named by its place in the scene, so its draws pair with its own from the frame before whatever
            // the occlusion and the frustum dropped ahead of it this time.
            if (entry.Moving)
            {
                _device.SetObjectMotion(true, index + 1L);
                appliedMotion = true;
            }
            else if (appliedMotion)
            {
                _device.SetObjectMotion(false);
                appliedMotion = false;
            }

            // How much of its rated output the lamp this placement IS is currently giving out. One for
            // everything that is not a lamp, and for every lamp nobody has touched, so the common case
            // is a comparison against a constant.
            float glow = model.LampFixture >= 0 && Switchboard is { } board
                ? board.LevelOf(model.LampFixture)
                : 1f;

            foreach (WorldSubsetDraw subset in subsets)
            {
                // The glass pass takes only the glazing; every other pass takes everything, the glazing
                // included. That asymmetry is the point: a window's frame belongs in the G-buffer and its
                // pane does not, and the two are the same subset, so the opaque pass must still draw it
                // and let the alpha cut decide which half of it survives.
                if (glassOnly && !subset.Material.Glass)
                {
                    continue;
                }

                // A sorted group is the blended pass's alone; the opaque pass leaves it out.
                bool blended = subset.Blend is SubsetBlend.Translucent or SubsetBlend.Additive;
                if (faceGroups ? blended != blendedOnly : blendedOnly)
                {
                    continue;
                }

                if (faceGroups)
                {
                    byte reference = subset.Blend switch
                    {
                        SubsetBlend.Opaque => 0,
                        SubsetBlend.AlphaTested => 65,
                        _ => 2,
                    };
                    if (reference != appliedReference)
                    {
                        _device.SetAlphaReference(reference);
                        appliedReference = reference;
                    }
                }

                _device.SetMaterialOverride(subset.IsLamp && glow < 1f ? WorldSubsetResolver.Dimmed(subset.Material, glow) : subset.Material);

                // A bake is multiplied by the title's gain on top of whatever headroom it was encoded
                // with; a sub-mesh with none is drawn at the headroom alone and lit -- if the title
                // lights such things at all -- by its fixed-function sun. I.G.I. 2's lightmap stage is
                // MODULATE2X and its unlightmapped objects are lit by Direct3D's vertex pipeline
                // (ObjectShading cites the state blocks); Project I.G.I. answers Default at the seam, and
                // both of these are then the calls this pass always made.
                bool baked = subset.Lightmap.IsValid;
                float scale = baked ? _lightmapPages.Scale * shading.LightmapGain : _lightmapPages.Scale;
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

                    // The sun in the placement's own frame. The orientation takes the model's frame to the
                    // world's as M * v (the model view above is built that way, so its COLUMNS are the
                    // model's axes in the world), and the inverse is its transpose: dotting the world
                    // vector against each column, which is what the engine's row-vector Transform does.
                    Vector3f sun = !baked && shading.Sun is { } sunlight
                        ? orientation.Transform(sunlight.Direction)
                        : Vector3f.Zero;
                    if (sun != appliedSun)
                    {
                        _device.SetVertexSun(sun);
                        appliedSun = sun;
                    }

                    BlendMode blend = blendedOnly
                        ? subset.Blend == SubsetBlend.Additive ? _additiveFaceGroups : BlendMode.AlphaBlend
                        : rotorsOnly || glassOnly ? BlendMode.AlphaBlend : BlendMode.Opaque;
                    DrawSubset(model.Mesh, subset, modelView, blend, appliedCull);

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
            _occlusionSettle.Observed(camera);
        }

        // The rotor pass adds to the opaque pass's tallies rather than replacing them: the two are
        // halves of one frame's world draw, and the overlay is reading the frame.
        bool overlay = rotorsOnly || glassOnly || blendedOnly;
        LastDrawCount = (overlay ? LastDrawCount : 0) + draws;
        LastTriangleCount = (overlay ? LastTriangleCount : 0) + triangles;
        LastLightmappedCount = (overlay ? LastLightmappedCount : 0) + lit;
        LastOccludedCount = (overlay ? LastOccludedCount : 0) + occluded;
        if (appliedReference != IGraphicsDevice.DefaultAlphaReference)
        {
            _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);
        }

        if (FaceGroupBlending)
        {
            _device.SetObjectFogRange(0f, 0f);
        }

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

        if (appliedMotion)
        {
            _device.SetObjectMotion(false);
        }

        if (appliedClip is not null)
        {
            _device.SetDrawClip(null);
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
            ref WorldFrameEntry entry = ref _frameState.Entries[index];
            ResolvedWorldModel model = entry.Model!;
            _device.IssueOcclusionQuery(
                model.Query,
                camera.CreateModelView(entry.Position, entry.Orientation),
                model.QueryBounds);
        }

        _queryQueue.Clear();
    }

    /// <summary>What the last prepared frame decided for every placement a task owns, for scenario runs.</summary>
    /// <param name="owner">The task whose placements, its own and its attachments', are described.</param>
    /// <returns>One line per placement: skipped or drawn, the model shown, and where, in engine units.</returns>
    internal IEnumerable<string> DescribeFrameForTesting(Engine.Tasks.GameTask owner) =>
        _frameState.DescribeForTesting(owner);

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
        ResolvedWorldModel model,
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
        // only for as long as it goes on being offered (the traced WorldScene), which is what lets an object
        // that moves leave the structure at once instead of leaving a trail -- so anything not offered
        // here disappears from every ray. It used to be culled at three hundred metres and a degree of
        // arc; that rule only works for a scene that caches every static instance up front and culls
        // per frame against the cache, whereas here what was not offered was never admitted: the
        // props, fittings and shells around the player's start were judged from the loading-screen
        // camera, arrived only once play began, and the lighting crept across the room as they did.

        WorldSubsetDraw[] subsets = model.Subsets ?? _subsets.ResolveSubsets(scene, model, displayed);

        // Each run with the material it would be drawn with -- lamp, switch and all -- because nothing is
        // drawn here to take one from.
        float glow = model.LampFixture >= 0 && Switchboard is { } board
            ? board.LevelOf(model.LampFixture)
            : 1f;
        foreach (WorldSubsetDraw subset in subsets)
        {
            _device.SubmitTracedMesh(
                model.Mesh,
                subset.IndexOffset,
                subset.IndexCount,
                modelView,
                subset.Diffuse,
                material: subset.IsLamp && glow < 1f ? WorldSubsetResolver.Dimmed(subset.Material, glow) : subset.Material);
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

        _subsets.DestroyResolvedQueries();
        _subsets.DestroyMeshes();
        _lightmapPages.Release();
        _textures.Release();
        _subsets.Clear();
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
        _lightmapPages.ApplyBake(baked, discardRetail);

        // Each placement caches the lightmap handles it resolved into its per-subset draw state, and those
        // now point at textures just destroyed. Drop the resolutions so the next draw rebuilds them against
        // the swapped-in pages -- without this the frame the async bake lands on draws a dangling handle and
        // the device rejects it.
        _subsets.ForgetResolved();

        _logger.Information(
            LogCategory,
            baked is null
                ? "Restored the shipped object lightmaps."
                : $"Applied a path-traced lightmap bake over {_lightmapPages.BakedEntryCount} object(s).");
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

    /// <inheritdoc/>
    public PlacementPose DrawPoses => _drawPoses ??= _transforms.TryGetDrawTransform;

    /// <inheritdoc/>
    public IReadOnlyCollection<WorldPlacement> Movers => _frameState.Movers;

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

        WorldPlacement placement = _transforms.DisplayedPlacement(scene.Placements[placementIndex]);
        MeshGeometry? geometry = placement.BentGeometry ?? _subsets.GetGeometry(placement);
        if (geometry is null)
        {
            return false;
        }

        MeshHandle mesh = _subsets.GetMesh(
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

        _transforms.ResolveDrawTransform(placement, out Vector3d position, out Matrix3f orientation, out _, out _);
        Matrix4f modelView = camera.CreateModelView(position, orientation);

        _device.SetPoseInterpolation(0f, 0f);
        _device.DrawOutline(mesh, CollectionsMarshal.AsSpan(_outlineRanges), modelView, colour, radius, throughWalls);
        _device.ClearPoseInterpolation();
        return true;
    }

    /// <summary>Gets or sets which of the level's sub-meshes are light fixtures.</summary>
    /// <remarks>
    /// Set per level beside <see cref="Materials"/>, and for the same reason: it is the shell's
    /// conclusion about the level, not something a renderer can work out from a draw. It decides where a
    /// DETECTED emitter is allowed to glow -- see the resolve, and <see cref="LampDetector"/> for why the
    /// texture alone cannot answer that. Empty is the honest default: nothing detected glows.
    /// </remarks>
    public LampDetector.Lamps Lamps { get => _subsets.Lamps; set => _subsets.Lamps = value; }

    /// <summary>Gets or sets which of the level's lamps are currently lit, or <see langword="null"/> for all of them.</summary>
    /// <remarks>
    /// Set per level beside <see cref="Lamps"/>, which decides where a lamp is; this decides whether it
    /// is on. Null is the level as it ships — every fixture at full output, and not one comparison per
    /// draw spent asking.
    /// </remarks>
    public LampSwitchboard? Switchboard { get => _subsets.Switchboard; set => _subsets.Switchboard = value; }

    /// <summary>Gets or sets what each of the level's textures is made of.</summary>
    /// <remarks>
    /// Settable rather than constructor-injected because it is data the shell loads once and may
    /// reload, and because a renderer with no library still draws -- every surface simply takes the
    /// default, which is the behaviour from before materials existed.
    /// </remarks>
    public MaterialLibrary Materials { get => _subsets.Materials; set => _subsets.Materials = value; }

    /// <summary>Drops every uploaded diffuse texture so the next draw loads them all again.</summary>
    /// <remarks>
    /// <para>
    /// For swapping the upscaled pack in and out of a level that is already running, which is a
    /// comparison tool rather than a feature: the two pictures have to be the same frame, and a reload
    /// between them is the only way to be sure nothing else moved.
    /// </para>
    /// <para>
    /// <b>Clearing the dictionary is not enough, and that is the whole subtlety.</b> A resolved model
    /// caches its subsets, and a <c>WorldSubsetDraw</c> carries its diffuse <i>by value</i> — so a texture
    /// dropped here would still be drawn from the cached draw list, by a handle that no longer exists.
    /// <see cref="WorldSubsetResolver.MeltResolvedSubsets"/> is the existing answer to exactly that, already used when the
    /// material library or the switchboard is replaced; the subsets re-resolve on next use at the cost of
    /// a dictionary lookup.
    /// </para>
    /// <para>
    /// <b>And the decode cache has to go with them.</b> It holds images decoded on workers during the
    /// load, and <see cref="WorldTextureCache.GetTexture"/> prefers it over the archive — so leaving it would hand back the
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
        _textures.Reload();
        _subsets.MeltResolvedSubsets();
    }

    /// <summary>Gets or sets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>
    /// Settable for the same reason <see cref="Materials"/> is, and owned by the shell rather than by
    /// this renderer: the same pack serves the world, the soldiers and the terrain, and each of them
    /// loads its own textures.
    /// </remarks>
    public MaterialDetailPack? DetailMaps { get => _textures.DetailMaps; set => _textures.DetailMaps = value; }

    /// <summary>Gets or sets the level's textures, decoded ahead of this renderer on worker threads.</summary>
    /// <remarks>
    /// Optional and purely a speed-up: with none set every texture is decoded on the thread that first
    /// draws it, which is what this renderer always did. See <see cref="TextureDecodeCache"/>.
    /// </remarks>
    public TextureDecodeCache? Decoded { get => _textures.Decoded; set => _textures.Decoded = value; }

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

        return _subsets.CaptureSurfaceCache(scene, cardResolution);
    }
}
