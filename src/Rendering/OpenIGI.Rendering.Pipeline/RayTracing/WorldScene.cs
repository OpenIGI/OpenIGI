using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>
/// Builds the traceable copy of the world out of what the renderer is asked to draw.
/// </summary>
/// <remarks>
/// <para>
/// A ray needs to be able to hit geometry that is not on screen — the wall casting the shadow is very
/// often behind the camera — so the structure it traces against cannot be built from one frame's visible
/// set. But the backend has no scene graph: it is handed meshes to create and told to draw them at
/// transforms, and that is the whole of what it knows about the world.
/// </para>
/// <para>
/// So placements are remembered rather than rebuilt. Every draw the geometry pass makes -- and every
/// placement the world renderer offers without drawing, which is the whole level every frame -- is
/// recorded, keyed by which mesh at which transform, and kept for as long as it goes on being offered.
/// For a world whose geometry does not move, that set is the whole level from the loading screen on,
/// stable and complete; a placement that stops being offered (an object that moved) leaves a frame
/// later rather than staying behind as a ghost.
/// </para>
/// <para>
/// The transforms are stored in absolute world space rather than relative to the camera. Relative would
/// be better conditioned, but it would also mean every placement recorded on a previous frame carried a
/// camera that has since moved, and the whole point is that a placement recorded once stays good.
/// </para>
/// </remarks>
internal sealed class WorldScene : IDisposable
{
    // Far more than a level of this vintage contains, and small enough that the whole set is well under a
    // megabyte of instance descriptors.
    private const int MaxInstances = 16384;

    // One per geometry of every live structure. A run a rebuild or an eviction leaves behind goes back
    // to be reused once no frame in flight can still read it; see ReleaseRecords.
    private const int MaxRecords = 16384;

    // How many frames a released run of records is held before it is handed out again: past the frames
    // in flight and the scene's own retirement slack, so no trace still running can read a record that
    // has since been rewritten for another mesh.
    private const int RecordRetireFrames = 8;

    // Two shader handles (vertices, indices), a first index, a mask layer, an albedo layer and the flags.
    private const int RecordSize = 32;

    private readonly IRhiDevice _device;
    private readonly IOpenIgiLogger _logger;
    private readonly RayTracingScene _scene;
    private readonly Dictionary<uint, Geometry> _geometries = [];

    // Every static placement, with the frame it was last offered on. A placement the renderer stops
    // offering -- an object that has moved, or one the level no longer shows -- is dropped a frame later.
    private readonly Dictionary<Placement, int> _placed = [];
    private readonly List<RhiAccelerationInstance> _instances = [];

    // The placement behind each STATIC instance, parallel to the head of the list above, so a placement
    // that has stopped being offered can be found and dropped.
    private readonly List<Placement> _instancePlacements = [];

    // How many placements the pass being recorded has offered, and how many the last one did. A pass that
    // offered none drew no world (a menu over it, a frame skipped), and expires nothing.
    private int _observedThisPass;
    private int _observedLastPass;

    // Which mesh each placement came from, parallel to the list above. A mesh does not keep the same
    // structure for its whole life — rebuilding it over a larger range set replaces it — so the
    // placements that name it have to be able to find their way back to it and be corrected.
    private readonly List<uint> _instanceMeshIds = [];

    // Meshes forgotten since the last scene build, swept out of the lists above in one pass rather than
    // one pass each. Eviction destroys a whole neighbourhood of terrain pages at once, and a per-mesh
    // removal walks every placement in the level for each of them -- quadratic in exactly the moment the
    // player is moving fast enough to trigger it, which is where the freeze came from.
    private readonly HashSet<uint> _forgotten = [];

    // Which meshes are skinned, so the count can be reported. See SkinnedGeometryCount.
    private readonly HashSet<uint> _skinned = [];

    // Meshes that turned out to move. A gun in a walking soldier's hands, a door on its hinge, the
    // player's own view model: rigid geometry -- so a structure built over it is sound -- drawn at a
    // transform that is different every frame. Remembering where such a thing WAS is what this set
    // exists to stop. Accumulating a placement per frame for each of them filled the instance list to
    // its sixteen-thousand ceiling within ten seconds of mission three, at which point nothing new
    // could enter the traced world at all, and left the dedupe set holding a hundred thousand
    // transforms that would never be matched again -- twenty-seven megabytes of large-object heap, and
    // a sweep over every one of them each time a terrain page streamed out.
    //
    // They are not dropped from the scene: they are placed from the frame that is drawing them and
    // forgotten again at the start of the next, which is the only way a moving object's traced shadow
    // can be in the right place. See the static/dynamic split over the instance list below.
    private readonly HashSet<uint> _movers = [];

    // The movers already placed this frame. A model is drawn once per material subset, and every one of
    // those draws arrives here; a static placement is deduplicated by _placed, but a mover used to be
    // added once per draw -- a soldier stood in the structure nine to fourteen times over, every copy the
    // whole body, and every ray that passed near one tested all of them. Emptied with the moving tail.
    private readonly HashSet<Placement> _movedThisFrame = [];

    // Rigid movers, found by what they do rather than by a guess. A mesh that, in the same scene build,
    // gains a new static placement AND loses one it was no longer offered at has moved: a static prop
    // turning up for the first time only gains, and one destroyed only loses. Seen on enough consecutive
    // builds, the mesh is filed with the movers -- see RigidMoverBuilds. Until then a helicopter or a held
    // weapon was a new "static" placement every frame, which re-sorted the instance list and kept the
    // world from ever counting as settled.
    private readonly HashSet<uint> _gainedStatic = [];
    private readonly HashSet<uint> _lostStatic = [];
    private readonly Dictionary<uint, int> _movingBuilds = [];

    /// <summary>How many consecutive scene builds a mesh must move on before it is filed as a mover.</summary>
    private const int RigidMoverBuilds = 3;

    // How many of the leading instances are accumulated static placements. Everything past it is this
    // frame's movers, cleared and refilled every frame -- so a moving object is traced where it is now
    // rather than everywhere it has ever been.
    private int _staticCount;

    // The shadow casters offered for one frame at a time -- the terrain -- by the version of their
    // contents their structure was built over, and the material record each was given once.
    private readonly Dictionary<uint, int> _casterVersions = [];
    private readonly Dictionary<uint, int> _casterRecords = [];
    private int _frame;

    // Building a structure is not free, and the first frame of a level offers every mesh in it at once.
    // Admitting a few a frame spreads that over the first second of play, which nobody sees, rather than
    // stalling on the frame the level appears.
    private const int MaxNewGeometriesPerFrame = 8;

    // Which material ranges of each mesh have been drawn, in the order they were first seen -- which is
    // the order they become geometries in, and therefore the geometry index a hit reports. Accumulated
    // like the placements are: a mesh's ranges are discovered over the first frames it is visible for,
    // and the set only grows.
    private readonly Dictionary<uint, List<RayTracedSurface>> _surfaces = [];

    // Meshes whose range set has grown since their structure was built, and so need building again.
    private readonly HashSet<uint> _stale = [];

    // The material records, one per geometry of every built structure, in the layout the ray shader
    // reads them through the buffer's shader handle. Append-only: a structure rebuilt over a larger range set is
    // given a fresh run of records rather than overwriting the run a frame in flight may still be
    // reading, which is the whole of the synchronisation this needs.
    private readonly IRhiBuffer _records;
    private int _recordCount;
    private bool _recordOverflow;

    // Runs of records free to hand out again, in address order with neighbours merged, and the runs
    // released but not yet old enough to reuse, each with the frame it may be reused from. Without these
    // the records were append-only: every terrain page streamed in took one for good, so flying across a
    // large level ran them out, and every structure built after that was pointed at record nought -- some
    // other mesh's vertices, indices and cut-out mask. Its rays read the wrong mask (a tree's whole card
    // cast a shadow) and indexed past the end of the wrong index buffer (a device fault).
    private readonly List<(int Start, int Count)> _freeRecords = [];
    private readonly List<(int Frame, int Start, int Count)> _releasedRecords = [];

    private int _newThisFrame;

    // Whether this frame grew the scene or turned something away for want of budget, and how many frames
    // in a row have done neither. See IsSettled.
    private bool _grewThisFrame;
    private int _quietFrames;

    /// <summary>How many frames in a row must add nothing before the scene counts as settled.</summary>
    private const int SettledFrames = 30;
    private bool _instancesChanged;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WorldScene"/> class.</summary>
    /// <param name="device">The device to build on.</param>
    /// <param name="structures">Its acceleration structures.</param>
    /// <param name="logger">Where running out of records is said.</param>
    public WorldScene(IRhiDevice device, IRhiAccelerationStructures structures, IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(logger);

        _device = device;
        _logger = logger;
        _scene = new RayTracingScene(device, structures, Math.Min(MaxInstances, structures.MaxInstances));

        // Host visible and written directly. The records are tiny, they change only when a structure is
        // built, and the alternative -- staging into device-local memory -- would need a transfer
        // recorded and fenced for what amounts to thirty-two bytes.
        _records = device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(MaxRecords * RecordSize),
            RhiBufferUsage.RayTracingInput | RhiBufferUsage.MapWrite,
            "traced world hit records"));
    }

    /// <summary>Gets the scene structure, or <see langword="null"/> before anything has been built.</summary>
    public IRhiAccelerationStructure? TopLevel => _scene.TopLevel;

    /// <summary>Gets a value indicating whether the scene holds anything worth tracing against.</summary>
    public bool IsReady => _scene.IsReady;

    /// <summary>Gets or sets whether new meshes are admitted without the per-frame budget.</summary>
    /// <remarks>
    /// The budget exists to keep a structure build from landing in a frame the player sees. Behind the
    /// loading screen there is no such frame, and the budget only stretches the time the level spends
    /// arriving in the structure -- during which anything lit once, the surface cache above all, is lit
    /// against a world with walls missing. So the shell lifts it for as long as the load holds the picture.
    /// </remarks>
    public bool UnlimitedAdmission { get; set; }

    /// <summary>Gets a value indicating whether the scene has stopped growing.</summary>
    /// <remarks>
    /// Ready says the scene holds SOMETHING, which is true from the first eight meshes admitted -- the
    /// per-frame budget -- while a level has hundreds to come. Anything that lights the level once and
    /// keeps the result has to wait for this instead: a frame count of nothing new admitted and nothing
    /// turned away for want of budget. Lit against a scene still filling in, every wall not yet admitted
    /// is missing from its shadows.
    /// </remarks>
    public bool IsSettled => IsReady && _quietFrames >= SettledFrames;

    /// <summary>Gets how many distinct placements the scene has accumulated.</summary>
    public int PlacementCount => _instances.Count;

    /// <summary>Gets how many meshes the scene holds structures for.</summary>
    public int GeometryCount => _geometries.Count;

    /// <summary>Gets how many of the traced geometries are skinned, and so refitted every frame.</summary>
    /// <remarks>
    /// Reported rather than merely counted. Skinned geometry was excluded from the traced world for most
    /// of this renderer's life, and the symptom of it being absent is not an error but a silence: rays
    /// pass through every soldier and through the player, so a reflection shows an empty room and a
    /// traced shadow falls where a body is standing. Nothing downstream can tell that apart from "the
    /// reflection is working and there is simply nobody there", which is exactly the confusion this
    /// number ends.
    /// </remarks>
    public int SkinnedGeometryCount => _skinned.Count;

    /// <summary>How many CASTER geometries the world holds -- which in practice is the terrain.</summary>
    /// <remarks>
    /// Separate from the rest because the ground enters the traced world by a different door
    /// (<see cref="ObserveCaster"/>) and can be absent while everything else is present. When it is, a
    /// reflected ray pointing at the floor hits nothing at all and the surface reflects sky, which looks
    /// like a reflection that is too bright rather than like missing ground.
    /// </remarks>
    public int CasterGeometryCount => _casterVersions.Count;

    /// <summary>Gets the handle of the material records a ray shader looks a hit up in.</summary>
    public ulong RecordHandle => _records.ShaderHandle;

    /// <summary>Records a mesh being drawn somewhere, adding it to the traceable world if it is new.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh itself.</param>
    /// <param name="worldTransform">Where it is being drawn, in absolute world space.</param>
    /// <param name="surface">Which run of the mesh's indices this draw covered, and what it is made of.</param>
    /// <param name="mask">
    /// Which rays may find it. The world takes <see cref="RayMask.All"/>; the first-person rig takes
    /// <see cref="RayMask.ViewModel"/>, which only a reflection asks for.
    /// </param>
    /// <remarks>
    /// Called for every geometry-pass draw, so it must be cheap when both the placement and the range are
    /// ones already known — which after the first seconds of a level is every one of them. A hash lookup
    /// and a walk of the handful of ranges a mesh has is the whole cost.
    /// </remarks>
    public void Observe(
        uint meshId,
        IRayTracedMesh mesh,
        in Matrix4f worldTransform,
        in RayTracedSurface surface,
        byte mask = (byte)RayMask.All)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        // Nothing to trace: an empty mesh, or one whose geometry has not been uploaded yet.
        if (mesh.DrawableIndexCount < 3)
        {
            return;
        }

        // Skinned geometry -- soldiers, and the player's own body -- is REFITTED rather than excluded.
        //
        // It used to return here, because the triangles a structure was built over are not the ones that
        // are there a frame later. The consequence was that no character was in the traced world at all:
        // a window or a puddle could reflect the room and the weapon and never a person, and the sun's
        // traced shadow passed straight through everyone in the level. Nothing downstream could fix that,
        // because a ray has to be able to HIT a body before anything can ask what colour it is.
        //
        // A refit is what this case is for. The topology never changes -- the same vertices, the same
        // indices, the same counts, moved by a skeleton -- which is exactly the shape of problem an
        // acceleration structure update is defined to handle, and it costs a fraction of a rebuild. The
        // structure is created asking for updates.
        bool skinned = mesh.Animated;

        // Streamed geometry -- the terrain's batch pages, the only thing that uses mesh storage -- is not
        // traced as drawn. A page holds every chunk ever streamed into it at its level of detail, the
        // same ground is held again by the pages of the other levels, and the vertex stage slides what
        // it draws towards the next level as the camera moves; a structure over a page therefore casts
        // from a surface that is not the one on screen, and the ground shadowed itself in patches. The
        // terrain offers what it drew, where it drew it, through ObserveCaster instead.
        if (mesh.Storage)
        {
            return;
        }

        // Which runs of this mesh have been drawn, and what each is made of.
        List<RayTracedSurface> surfaces = SurfacesFor(meshId, mesh, surface, out bool grew);

        // A mesh whose material ranges have grown -- a part of it drawn for the first time this frame --
        // is rebuilt: the structure it has covers fewer runs than the mesh turns out to have. Retire the
        // old one once the frames still tracing against it have finished, and let the placements repoint
        // themselves to the new address on the next scene build. Held to the same per-frame budget as a
        // new mesh.
        //
        // Or rebuilt later, if it could not be then: a range found on a frame whose budget was spent is
        // marked stale -- and until this retried it, nothing ever
        // read the mark, so the missing part stayed out of the traced world for good, a hole every ray
        // passed through.
        if (_geometries.TryGetValue(meshId, out Geometry built)
            && (grew || _stale.Contains(meshId))
            && (UnlimitedAdmission || _newThisFrame < MaxNewGeometriesPerFrame))
        {
            if (BuildGeometry(mesh, surfaces, allowUpdate: skinned) is { } rebuilt)
            {
                _geometries[meshId] = rebuilt;
                _scene.RetireStructure(built.Structure);
                ReleaseRecords(built.RecordBase, built.RecordCount);
                _stale.Remove(meshId);
            }
            else
            {
                // No records for the larger run: the old structure stands, and the mark stays for a
                // later frame that has some.
                _stale.Add(meshId);
            }

            _newThisFrame++;
        }

        // A mover is placed from the frame drawing it and nowhere else: no dedupe, because the whole
        // point is that this transform is not the last one, and no accumulation, because the tail of the
        // list it goes into is emptied at the start of every frame.
        if (skinned)
        {
            _skinned.Add(meshId);
        }

        if (skinned && _movers.Add(meshId))
        {
            // A mover from the first frame rather than after sixty-four sightings. The promotion the
            // counter below performs is a guess about geometry that keeps turning up somewhere new; a
            // skinned mesh is known to move, and letting it accumulate a static placement per frame in
            // the meantime would fill the instance list with a soldier's whole walk.
            _instancesChanged = true;
        }

        bool moving = _movers.Contains(meshId);
        Placement placement = default;
        if (moving && !_movedThisFrame.Add(Placement.From(meshId, worldTransform)))
        {
            // This mover's other subsets: the structure already holds every run, so one instance is the
            // whole body.
            return;
        }

        if (!moving)
        {
            placement = Placement.From(meshId, worldTransform);
            if (KnownPlacement(placement) is { } known)
            {
                // A place this mesh already stands at, offered again: kept for another frame, at the
                // transform it was admitted with.
                _placed[known] = _frame;
                _observedThisPass++;
                return;
            }
        }

        if (_instances.Count >= MaxInstances)
        {
            return;
        }

        if (!_geometries.TryGetValue(meshId, out Geometry geometry))
        {
            if (!UnlimitedAdmission && _newThisFrame >= MaxNewGeometriesPerFrame)
            {
                // Not admitted this frame. The placement is left unrecorded so the next frame offers it
                // again rather than remembering a mesh it never built.
                _grewThisFrame = true;
                return;
            }

            if (BuildGeometry(mesh, surfaces, allowUpdate: skinned) is not { } admitted)
            {
                // No records left to give it: left out of the traced world rather than built against
                // another mesh's records, and offered again next frame.
                _grewThisFrame = true;
                return;
            }

            geometry = admitted;
            _geometries[meshId] = geometry;
            _stale.Remove(meshId);
            _newThisFrame++;
        }
        else if (skinned)
        {
            // This frame's pose, and a refit to go with it.
            //
            // The address matters as much as the request: the mesh keeps a RING of pose buffers so that a
            // frame still in flight never reads the one being written, so the buffer the structure was
            // built against is not the one holding the pose now. Refitting without re-pointing fits the
            // structure to a pose two frames old, which reads as a body whose reflection lags its own
            // movement.
            geometry.Structure.RepointVertices(mesh.Vertices);
            _scene.Invalidate(geometry.Structure);
        }

        if (!moving)
        {
            // Kept for as long as the renderer goes on offering it. There used to be a guess here instead:
            // a mesh seen at sixty-four new places within a few seconds was judged to be moving, and every
            // place it had been was swept out of the static scene. It condemned every heavily instanced
            // static prop -- a light fitting placed ninety-three times, a fence panel, a floor tile, all
            // discovered at once behind the loading screen or as the camera swept across them -- which then
            // existed only where a frame drew them. Every GI and shadow ray met a world that gained and lost
            // walls, fittings and fences with the view, and the bounce light crept across walls and ceilings
            // in patches that took seconds to settle, moving and at rest. Nothing is guessed any more: each
            // PLACEMENT answers for itself: re-offered, it stays; not offered, it goes (see ExpireUnoffered) --
            // one copy moving no longer condemns the copies that stand still, nor leaves a trail of where it
            // has been.
            _placed[placement] = _frame;
            _observedThisPass++;
            _grewThisFrame = true;
            _gainedStatic.Add(meshId);
        }

        AddInstance(moving, meshId, placement, RhiAccelerationInstance.Create(
            worldTransform,

            // Not the mesh: the first of this structure's material records. A hit adds the geometry index
            // traversal reports to it and lands on the record for the run it actually struck.
            customIndex: (uint)geometry.RecordBase & 0x00FFFFFFu,
            mask,

            // The port's meshes do not wind consistently — the renderer draws them with culling off for
            // exactly that reason — so traversal must not cull by facing either, or a shadow would be cast
            // by only half the triangles that should cast it.
            RhiAccelerationInstanceOptions.TriangleCullDisable,
            geometry.Structure.InstanceReference));

        _instancesChanged = true;
    }

    /// <summary>The placement already standing where this one is, if any: the same cell or one beside it.</summary>
    /// <remarks>
    /// A draw's world transform is recovered from its CAMERA-RELATIVE model-view and the frame's rotation, in
    /// single precision, at an absolute position thousands of metres out -- so its last bits depend on which
    /// way the camera is pointing, by about half a millimetre. Matched exactly, a placement turning the
    /// camera nudged across a key boundary was a NEW placement: added, the old one expired a frame later,
    /// and a mesh doing that three builds running was filed as a mover. The traced world was rebuilt
    /// differently on every frame of a mouse movement, and every reflection, shadow and bounce read off it
    /// flickered -- a pane's reflection switching patches between the cache's light and the material's as
    /// the view shook, with the camera standing still. A key a cell either side of an existing one is that
    /// placement; nothing else of the same mesh stands within two centimetres at the same rotation.
    /// </remarks>
    private Placement? KnownPlacement(Placement placement)
    {
        if (_placed.ContainsKey(placement))
        {
            return placement;
        }

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx | dy | dz) == 0)
                    {
                        continue;
                    }

                    Placement beside = placement with
                    {
                        X = placement.X + dx,
                        Y = placement.Y + dy,
                        Z = placement.Z + dz,
                    };
                    if (_placed.ContainsKey(beside))
                    {
                        return beside;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Files an instance in the static head of the list or the frame's moving tail.</summary>
    /// <param name="moving">Whether the mesh's transform is one this frame supplies rather than remembers.</param>
    /// <param name="meshId">The mesh the instance names, kept parallel to the instance itself.</param>
    /// <param name="placement">The static placement it stands for; ignored for a moving one.</param>
    /// <param name="instance">The instance to file.</param>
    /// <remarks>
    /// A static placement is inserted at the boundary rather than appended, so the two runs stay
    /// contiguous and every pass over the list -- the address refresh, the forgotten sweep -- goes on
    /// working on one array. The shift it costs is over the moving tail alone, which is a handful of
    /// entries, and statics are discovered only over a level's first seconds.
    /// </remarks>
    private void AddInstance(bool moving, uint meshId, in Placement placement, in RhiAccelerationInstance instance)
    {
        if (moving)
        {
            _instances.Add(instance);
            _instanceMeshIds.Add(meshId);
            return;
        }

        _instances.Insert(_staticCount, instance);
        _instanceMeshIds.Insert(_staticCount, meshId);
        _instancePlacements.Insert(_staticCount, placement);
        _staticCount++;
    }

    /// <summary>Adds a drawn range to what is known about a mesh, and says whether it was new.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh itself.</param>
    /// <param name="surface">The range this draw covered.</param>
    /// <param name="grew">Receives whether the range set gained an entry, so a built structure is stale.</param>
    /// <returns>Every range known for the mesh, in the order they become geometries in.</returns>
    private List<RayTracedSurface> SurfacesFor(
        uint meshId, IRayTracedMesh mesh, in RayTracedSurface surface, out bool grew)
    {
        if (!_surfaces.TryGetValue(meshId, out List<RayTracedSurface>? surfaces))
        {
            surfaces = [];
            _surfaces[meshId] = surfaces;
        }

        RayTracedSurface entry = surface;
        if (entry.IndexCount < 3 || entry.IndexOffset < 0 || entry.IndexOffset + entry.IndexCount > mesh.IndexCapacity)
        {
            grew = false;
            return surfaces;
        }

        // A linear walk, because a mesh has a handful of ranges and this runs once per draw: a set would
        // cost a hash of the whole record to save comparing four fields a few times.
        for (int i = 0; i < surfaces.Count; i++)
        {
            if (surfaces[i] == entry)
            {
                grew = false;
                return surfaces;
            }

            // The same run with only its glow changed -- a lamp switched, faded, or first seen while its
            // photocell had it off. Not a new run: its record is rewritten where it stands, or the tube
            // would reflect the state it happened to be in the first frame it was drawn.
            if (surfaces[i] with { Emissive = entry.Emissive } == entry)
            {
                surfaces[i] = entry;
                if (_geometries.TryGetValue(meshId, out Geometry built) && i < built.RecordCount)
                {
                    ReadOnlySpan<uint> flag = [entry.FlagWord];
                    _device.WriteBuffer(_records, (ulong)(((built.RecordBase + i) * RecordSize) + (7 * sizeof(uint))), flag);
                }

                grew = false;
                return surfaces;
            }
        }

        surfaces.Add(entry);

        grew = _geometries.ContainsKey(meshId);
        if (grew)
        {
            _stale.Add(meshId);
        }

        return surfaces;
    }

    /// <summary>Places a caster mesh for this frame, building its structure over its current contents.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh itself, a storage mesh whose contents its owner rewrites.</param>
    /// <param name="worldTransform">Where it is placed, in absolute world space.</param>
    /// <param name="indexCount">How many indices, from the start of the mesh, make up the caster.</param>
    /// <remarks>
    /// <para>
    /// This is how the terrain is traced. Its owner writes the chunks it drew this frame, at the
    /// positions the vertex stage drew them, into a mesh it hands over here; the placement goes into
    /// the moving tail, so it lasts exactly one frame like any other mover's, and the structure is
    /// built again whenever the mesh's <see cref="IRayTracedMesh.ContentVersion"/> says the contents
    /// changed — a few thousand triangles, so a rebuild is cheaper than the bookkeeping a refit would
    /// need. A caster that has not changed keeps its structure and costs a placement.
    /// </para>
    /// <para>
    /// The material record is written once and reused: it names the mesh's buffers and a first index
    /// of zero, which a rebuild does not change, so nothing a frame in flight reads is overwritten.
    /// </para>
    /// </remarks>
    public void ObserveCaster(uint meshId, IRayTracedMesh mesh, in Matrix4f worldTransform, int indexCount)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (indexCount < 3 || indexCount > mesh.IndexCapacity)
        {
            return;
        }

        RayTracedSurface surface = new(0, indexCount, Opaque: true, MaskLayer: -1);
        bool built = _geometries.TryGetValue(meshId, out Geometry geometry);
        bool current = built
            && _casterVersions.TryGetValue(meshId, out int version)
            && version == mesh.ContentVersion
            && geometry.IndexCount == indexCount;

        // Rebuilt whenever its contents changed. It used to wait out a compaction and place the OLD
        // structure meanwhile -- the page's previous ground, metres off the surface the frame
        // drew -- so every ray off a far hillside struck it and the hill went into shadow for a frame each
        // time its level of detail moved (a moving camera: every few frames).
        if (!current)
        {
            if (!_casterRecords.TryGetValue(meshId, out int recordBase))
            {
                recordBase = WriteRecords(mesh, [surface]);
                if (recordBase < 0)
                {
                    // None to give it; whatever this page had stays until a later frame has one.
                    return;
                }

                _casterRecords[meshId] = recordBase;
            }

            if (built)
            {
                _scene.RetireStructure(geometry.Structure);
            }

            RhiTriangleGeometry[] triangles =
            [
                new RhiTriangleGeometry(
                    mesh.Vertices,
                    VertexStride: (uint)Vertex3D.Size,
                    VertexCount: (uint)mesh.VertexCapacity,
                    mesh.Indices,
                    FirstIndex: 0,
                    PrimitiveCount: (uint)(indexCount / 3),
                    Opaque: true),
            ];

            // The record is the page's own, held in _casterRecords across rebuilds, so the geometry
            // claims none of it: it goes back when the page is forgotten, not when it is rebuilt.
            geometry = new Geometry(_scene.AddGeometry(triangles), indexCount, recordBase, RecordCount: 0);
            _geometries[meshId] = geometry;
            _casterVersions[meshId] = mesh.ContentVersion;
            _surfaces[meshId] = [surface];
        }
        else if (!built)
        {
            return;
        }

        if (_instances.Count >= MaxInstances)
        {
            return;
        }

        AddInstance(moving: true, meshId, default, RhiAccelerationInstance.Create(
            worldTransform,
            customIndex: (uint)geometry.RecordBase & 0x00FFFFFFu,
            (byte)RayMask.All,
            RhiAccelerationInstanceOptions.TriangleCullDisable,
            geometry.Structure.InstanceReference));

        _instancesChanged = true;
    }

    /// <summary>Records this frame's builds, and empties the moving tail for the next frame to refill.</summary>
    /// <param name="encoder">The frame's encoder, outside any pass.</param>
    /// <param name="clock">The frame clock, already advanced to this frame.</param>
    /// <param name="timestamps">Where to record when the builds began and ended, or <see langword="null"/>.</param>
    public void Record(IRhiCommandEncoder encoder, SubmissionClock clock, RhiTimestampWrites? timestamps = null)
    {
        _frame++;
        ReclaimRecords();
        _newThisFrame = 0;
        _quietFrames = _grewThisFrame ? 0 : _quietFrames + 1;
        _grewThisFrame = false;

        // What the pass just finished offered, for the sweep below: placements it did not offer again are
        // dropped, unless it offered nothing at all -- a frame that drew no world says nothing about it.
        _observedLastPass = _observedThisPass;
        _observedThisPass = 0;

        // Run from inside the update rather than here: the placements are only final once this frame's
        // geometry has been described.
        _scene.BeforeTopLevelBuild = PublishInstances;
        _scene.RecordUpdate(encoder, clock, timestamps);

        // This runs once the frame's geometry pass is complete, so the moving tail published just now is
        // where this frame drew its objects -- the rays the frame fires next meet them there, not a frame
        // behind, which put the back of a moving truck inside its old self. Empty it, and the next frame's
        // pass refills it from where its objects are then.
        if (_instances.Count > _staticCount)
        {
            _instances.RemoveRange(_staticCount, _instances.Count - _staticCount);
            _instanceMeshIds.RemoveRange(_staticCount, _instanceMeshIds.Count - _staticCount);
            _instancesChanged = true;
        }

        _movedThisFrame.Clear();
        FileRigidMovers();
    }

    /// <summary>Moves a mesh that has moved on several builds running from the static placements to the movers.</summary>
    /// <remarks>
    /// Its static placements are left to expire on their own: no longer offered as statics, they go at the
    /// next build like any placement nothing offers.
    /// </remarks>
    private void FileRigidMovers()
    {
        foreach (uint meshId in _gainedStatic)
        {
            if (!_lostStatic.Contains(meshId))
            {
                continue;
            }

            int builds = _movingBuilds.GetValueOrDefault(meshId) + 1;
            if (builds >= RigidMoverBuilds)
            {
                _movers.Add(meshId);
                _movingBuilds.Remove(meshId);
                _instancesChanged = true;
                continue;
            }

            _movingBuilds[meshId] = builds;
        }

        // A run broken is a run restarted: a door that swung once and stopped is not a mover.
        if (_movingBuilds.Count > 0)
        {
            foreach (uint meshId in _movingBuilds.Keys)
            {
                if (!_gainedStatic.Contains(meshId) || !_lostStatic.Contains(meshId))
                {
                    _movingBuilds.Remove(meshId);
                }
            }
        }

        _gainedStatic.Clear();
        _lostStatic.Clear();
    }

    /// <summary>Repoints every placement at its structure and hands the list to the scene.</summary>
    private void PublishInstances()
    {
        RefreshStructureAddresses();

        if (!_instancesChanged)
        {
            return;
        }

        _scene.SetInstances(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_instances));
        _instancesChanged = false;
    }

    /// <summary>
    /// Points every placement at where its mesh's structure actually is now.
    /// </summary>
    /// <remarks>
    /// Rebuilding a mesh replaces its structure and retires the original. A placement recorded before that
    /// still names the old one, and a scene built over addresses
    /// that have been freed is a scene the hardware faults on — so every placement is checked against its
    /// mesh's current structure before the scene is built.
    /// </remarks>
    private void RefreshStructureAddresses()
    {
        CompactForgotten();

        for (int i = 0; i < _instances.Count; i++)
        {
            if (!_geometries.TryGetValue(_instanceMeshIds[i], out Geometry geometry))
            {
                continue;
            }

            ulong current = geometry.Structure.InstanceReference;
            if (_instances[i].StructureReference == current)
            {
                continue;
            }

            // The records move with the structure: a rebuild appends a fresh run rather than overwriting
            // the one a frame in flight may still be reading, so a placement that keeps the old custom
            // index would look its material up in records describing the structure it no longer names.
            _instances[i] = _instances[i].Repointed(current, (uint)geometry.RecordBase);
            _instancesChanged = true;
        }
    }

    /// <summary>Drops every placement of a forgotten mesh, and every static one no longer offered.</summary>
    /// <remarks>
    /// <para>
    /// Runs immediately before the placements are handed to the scene, so a forgotten mesh's structure is
    /// never named by a scene that is built -- the same guarantee removing them one at a time gave, at one
    /// walk of the list per frame instead of one per mesh. The order of the survivors is preserved, which
    /// matters only in that the lists stay parallel.
    /// </para>
    /// <para>
    /// A static placement the last pass did not offer again is dropped with them: the object has moved on,
    /// or the level no longer shows it. The world renderer offers every placement of the level every frame,
    /// drawn or not, so a still object is always re-offered and a moved one leaves at once rather than as
    /// a trail. Nothing expires after a pass that offered nothing -- a frame that drew no world.
    /// </para>
    /// </remarks>
    private void CompactForgotten()
    {
        bool expire = _observedLastPass > 0;
        int stale = _frame - 1;

        if (_forgotten.Count == 0 && !expire)
        {
            return;
        }

        int kept = 0;
        int keptStatic = 0;
        for (int i = 0; i < _instances.Count; i++)
        {
            uint meshId = _instanceMeshIds[i];
            bool isStatic = i < _staticCount;

            if (_forgotten.Contains(meshId))
            {
                if (isStatic)
                {
                    _placed.Remove(_instancePlacements[i]);
                }

                continue;
            }

            // Past the boundary is this frame's moving tail, kept whoever it belongs to.
            if (isStatic && expire
                && _placed.TryGetValue(_instancePlacements[i], out int lastOffered)
                && lastOffered < stale)
            {
                _placed.Remove(_instancePlacements[i]);
                _lostStatic.Add(meshId);
                continue;
            }

            _instances[kept] = _instances[i];
            _instanceMeshIds[kept] = meshId;
            if (isStatic)
            {
                _instancePlacements[keptStatic] = _instancePlacements[i];
                keptStatic++;
            }

            kept++;
        }

        if (kept != _instances.Count)
        {
            _instancesChanged = true;
        }

        _instances.RemoveRange(kept, _instances.Count - kept);
        _instanceMeshIds.RemoveRange(kept, _instanceMeshIds.Count - kept);
        _instancePlacements.RemoveRange(keptStatic, _instancePlacements.Count - keptStatic);
        _staticCount = keptStatic;
        _forgotten.Clear();
    }

    /// <summary>Forgets a mesh that has been destroyed, so its structure is not kept alive by the scene.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <remarks>
    /// The placements that referred to it stay in the instance list until the scene is next reset. They
    /// point at a structure that has been freed, so a mesh is only ever forgotten together with a reset.
    /// </remarks>
    public void Forget(uint meshId)
    {
        // The structure and every placement that named it both leave the scene -- but the structure is
        // retired rather than freed here. A frame in flight is still tracing the scene it was last built
        // into, and freeing its geometry on the spot is exactly the read of freed memory the next trace
        // faults on -- the device loss a streamed-out terrain chunk produced, reported by the fault
        // extension as an invalid read. Retired, it lives until the frames that traced it have finished.
        if (_geometries.Remove(meshId, out Geometry geometry))
        {
            _scene.RetireStructure(geometry.Structure);
            ReleaseRecords(geometry.RecordBase, geometry.RecordCount);
        }

        if (_casterRecords.TryGetValue(meshId, out int casterRecord))
        {
            ReleaseRecords(casterRecord, 1);
        }

        // This mesh's placements have to stop naming its structure before the next scene is built, or
        // every later trace reads geometry that has gone. They are marked here and swept in
        // CompactForgotten, which the same pre-build pass that repoints every placement already runs --
        // so a hundred evicted pages cost one walk of the instance list between them, not a hundred.
        _forgotten.Add(meshId);
        _casterVersions.Remove(meshId);
        _casterRecords.Remove(meshId);
        _surfaces.Remove(meshId);
        _stale.Remove(meshId);
        _movers.Remove(meshId);
        _movingBuilds.Remove(meshId);
        _instancesChanged = true;
    }

    /// <summary>Drops the whole accumulated world, for a level change.</summary>
    /// <remarks>
    /// Through the scene, not around it. Disposing the structures here would leave the scene still holding
    /// them -- in its live set and its pending builds -- and the next frame would
    /// measure and build memory that had been freed. The scene's own <c>Clear</c> frees them and forgets them
    /// together, and empties the top-level structure so no trace names them either. Requires the device to be
    /// idle; see <c>TracedWorld.Reset</c>, its only caller, which a level change reaches.
    /// </remarks>
    public void Reset()
    {
        _scene.Clear();
        _geometries.Clear();
        _surfaces.Clear();
        _stale.Clear();
        _recordCount = 0;
        _recordOverflow = false;
        _freeRecords.Clear();
        _releasedRecords.Clear();
        _placed.Clear();
        _instances.Clear();
        _instanceMeshIds.Clear();
        _instancePlacements.Clear();
        _observedThisPass = 0;
        _observedLastPass = 0;
        _casterVersions.Clear();
        _casterRecords.Clear();
        _movers.Clear();
        _movedThisFrame.Clear();
        _gainedStatic.Clear();
        _lostStatic.Clear();
        _movingBuilds.Clear();
        _forgotten.Clear();
        _staticCount = 0;
        _instancesChanged = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Geometry geometry in _geometries.Values)
        {
            geometry.Structure.Dispose();
        }

        _geometries.Clear();
        _records.Dispose();
        _scene.Dispose();
    }

    /// <summary>Builds a structure over a mesh's material ranges, and the records a hit reads.</summary>
    /// <param name="mesh">The mesh to build over.</param>
    /// <param name="surfaces">Its ranges, in the order they become geometries in.</param>
    /// <param name="allowUpdate">
    /// Whether the structure is to be refittable, which is what skinned geometry needs.
    /// </param>
    /// <returns>The structure, the run of records it was given, and the extent it covered.</returns>
    /// <remarks>
    /// The records are appended rather than written over the run the previous structure used. A frame in
    /// flight may still be tracing that structure and reading those records, and the cost of never
    /// touching them again is a few bytes per rebuild against having to fence a host write behind the
    /// frames that might read it.
    /// </remarks>
    private Geometry? BuildGeometry(IRayTracedMesh mesh, List<RayTracedSurface> surfaces, bool allowUpdate = false)
    {
        // The records first: a structure with none of its own would read another mesh's.
        int recordBase = WriteRecords(mesh, surfaces);
        if (recordBase < 0)
        {
            return null;
        }

        RhiTriangleGeometry[] triangles = new RhiTriangleGeometry[surfaces.Count];
        for (int i = 0; i < surfaces.Count; i++)
        {
            RayTracedSurface s = surfaces[i];
            triangles[i] = new RhiTriangleGeometry(
                mesh.Vertices,
                VertexStride: (uint)Vertex3D.Size,
                VertexCount: (uint)mesh.VertexCapacity,
                mesh.Indices,
                FirstIndex: (uint)s.IndexOffset,
                PrimitiveCount: (uint)(s.IndexCount / 3),
                Opaque: s.Opaque);
        }

        return new Geometry(
            _scene.AddGeometry(triangles, allowUpdate), mesh.DrawableIndexCount, recordBase, surfaces.Count);
    }

    /// <summary>Appends one material record per range and returns where the run starts.</summary>
    /// <param name="mesh">The mesh the ranges belong to.</param>
    /// <param name="surfaces">The ranges, in geometry order.</param>
    /// <returns>The index of the first record, which is what the instance carries as its custom index.</returns>
    private int WriteRecords(IRayTracedMesh mesh, List<RayTracedSurface> surfaces)
    {
        int start = AllocateRecords(surfaces.Count);
        if (start < 0)
        {
            // Out of records. The caller leaves the mesh out of the traced world: built against records
            // that are not its own, its rays would read another mesh's buffers. Said once, because a
            // missing shadow would otherwise look like a different bug.
            if (!_recordOverflow)
            {
                _recordOverflow = true;
                _logger.Warning(
                    "RayTracing",
                    $"Material records exhausted at {MaxRecords}; meshes admitted after this point are left out of the traced world.");
            }

            return -1;
        }

        Span<uint> record = stackalloc uint[RecordSize / sizeof(uint)];

        for (int i = 0; i < surfaces.Count; i++)
        {
            RayTracedSurface s = surfaces[i];
            ulong vertices = mesh.Vertices.ShaderHandle;
            ulong indices = mesh.Indices.ShaderHandle;

            record[0] = (uint)vertices;
            record[1] = (uint)(vertices >> 32);
            record[2] = (uint)indices;
            record[3] = (uint)(indices >> 32);
            record[4] = (uint)s.IndexOffset;
            record[5] = unchecked((uint)s.MaskLayer);
            record[6] = unchecked((uint)s.AlbedoLayer);
            record[7] = s.FlagWord;

            _device.WriteBuffer(_records, (ulong)((start + i) * RecordSize), (ReadOnlySpan<uint>)record);
        }

        return start;
    }

    /// <summary>Takes a run of records: the first free run long enough, else fresh ones at the end.</summary>
    /// <returns>Where the run starts, or -1 where there is no room.</returns>
    private int AllocateRecords(int count)
    {
        for (int i = 0; i < _freeRecords.Count; i++)
        {
            (int start, int length) = _freeRecords[i];
            if (length < count)
            {
                continue;
            }

            if (length == count)
            {
                _freeRecords.RemoveAt(i);
            }
            else
            {
                _freeRecords[i] = (start + count, length - count);
            }

            return start;
        }

        if (_recordCount + count > MaxRecords)
        {
            return -1;
        }

        int fresh = _recordCount;
        _recordCount += count;
        return fresh;
    }

    /// <summary>Gives a run of records back, to be handed out again once no frame in flight can read it.</summary>
    private void ReleaseRecords(int start, int count)
    {
        if (count > 0)
        {
            _releasedRecords.Add((_frame + RecordRetireFrames, start, count));
        }
    }

    /// <summary>Moves the released runs old enough to reuse onto the free list, merging neighbours.</summary>
    private void ReclaimRecords()
    {
        for (int i = _releasedRecords.Count - 1; i >= 0; i--)
        {
            (int frame, int start, int count) = _releasedRecords[i];
            if (frame > _frame)
            {
                continue;
            }

            _releasedRecords.RemoveAt(i);

            int at = 0;
            while (at < _freeRecords.Count && _freeRecords[at].Start < start)
            {
                at++;
            }

            _freeRecords.Insert(at, (start, count));

            if (at + 1 < _freeRecords.Count && start + count == _freeRecords[at + 1].Start)
            {
                _freeRecords[at] = (start, count + _freeRecords[at + 1].Count);
                _freeRecords.RemoveAt(at + 1);
            }

            if (at > 0 && _freeRecords[at - 1].Start + _freeRecords[at - 1].Count == start)
            {
                _freeRecords[at - 1] = (_freeRecords[at - 1].Start, _freeRecords[at - 1].Count + _freeRecords[at].Count);
                _freeRecords.RemoveAt(at);
            }
        }

        // A free run that ends where the used span does goes back to the span, so fresh allocations at
        // the end stay as cheap as they were before anything was released.
        if (_freeRecords.Count > 0 && _freeRecords[^1].Start + _freeRecords[^1].Count == _recordCount)
        {
            _recordCount = _freeRecords[^1].Start;
            _freeRecords.RemoveAt(_freeRecords.Count - 1);
        }
    }

    /// <summary>One mesh's traceable form, and how much of it was built.</summary>
    /// <param name="Structure">The structure over its triangles.</param>
    /// <param name="IndexCount">How many indices it covered when it was built.</param>
    /// <param name="RecordBase">
    /// Where this structure's run of material records starts. An instance carries it as its custom index,
    /// so a hit reaches its own record by adding the geometry index traversal reports.
    /// </param>
    /// <param name="RecordCount">
    /// How many of those records the structure owns and gives back when it goes; nought for a terrain
    /// page, whose one record outlives its rebuilds.
    /// </param>
    private readonly record struct Geometry(
        BottomLevelStructure Structure, int IndexCount, int RecordBase, int RecordCount);

    /// <summary>A mesh at a place, quantised so that the same placement is recognised as the same one.</summary>
    /// <remarks>
    /// <para>
    /// NOT bit-identical frame to frame, which is what this once assumed: the transform is recovered from a
    /// camera-relative model-view through the frame's own rotation, in single precision, so its translation
    /// wanders by about half a millimetre as the camera turns. See <see cref="KnownPlacement"/>, which
    /// matches a key a cell either side of an existing one for exactly that reason.
    /// </para>
    /// <para>
    /// The rotation is rounded to a twentieth: its own noise is around a ten-millionth, and the values
    /// placements actually take -- nought, a half, one, and the arbitrary angles between -- sit nowhere near
    /// a boundary that fine noise could cross. The position is rounded to two centimetres, coarse enough
    /// that the neighbour search covers any wander and fine enough that two copies of one mesh never share.
    /// </para>
    /// </remarks>
    private readonly record struct Placement(uint MeshId, long Rotation, long X, long Y, long Z)
    {
        private const float RotationSteps = 20f;
        private const float CellsPerMetre = 50f;

        public static Placement From(uint meshId, in Matrix4f m)
        {
            static long Step(float value) => (long)MathF.Round(value * RotationSteps);

            long rotation = (Step(m.M11) * 73856093) ^ (Step(m.M12) * 19349663) ^ (Step(m.M13) * 83492791)
                ^ (Step(m.M21) * 32452843) ^ (Step(m.M22) * 49979687) ^ (Step(m.M23) * 86028121)
                ^ (Step(m.M31) * 15485863) ^ (Step(m.M32) * 32452867) ^ (Step(m.M33) * 67867967);

            return new Placement(
                meshId,
                rotation,
                (long)MathF.Floor(m.M14 * CellsPerMetre),
                (long)MathF.Floor(m.M24 * CellsPerMetre),
                (long)MathF.Floor(m.M34 * CellsPerMetre));
        }
    }

}
