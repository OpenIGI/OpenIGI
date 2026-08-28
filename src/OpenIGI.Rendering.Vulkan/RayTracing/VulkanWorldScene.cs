using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Meshes;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

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
/// So placements accumulate rather than being rebuilt. Every draw the geometry pass makes is recorded,
/// keyed by which mesh at which transform, and the set only ever grows. For a world whose geometry does
/// not move — which is all of this one — that set converges on the whole level within the first seconds
/// of play and is then stable and complete. A shadow may be missing a caster the camera has never faced;
/// it is never wrong about one it has.
/// </para>
/// <para>
/// The transforms are stored in absolute world space rather than relative to the camera. Relative would
/// be better conditioned, but it would also mean every placement recorded on a previous frame carried a
/// camera that has since moved, and the whole point is that a placement recorded once stays good.
/// </para>
/// </remarks>
internal sealed class VulkanWorldScene : IDisposable
{
    // Far more than a level of this vintage contains, and small enough that the whole set is well under a
    // megabyte of instance descriptors.
    private const int MaxInstances = 16384;

    // One per geometry of every structure ever built, including the runs left behind by a rebuild. A
    // level settles at a few hundred; this is the ceiling a runaway rebuild loop would hit rather than a
    // size anything is expected to reach.
    private const int MaxRecords = 16384;

    // Two device addresses, a first index and a mask layer.
    private const int RecordSize = 32;

    private readonly Vk _vk;
    private readonly VulkanRayTracingApi _api;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanRayTracingScene _scene;
    private readonly Dictionary<uint, Geometry> _geometries = [];
    private readonly HashSet<Placement> _placed = [];
    private readonly List<VulkanRayTracingInstance> _instances = [];

    // Which mesh each placement came from, parallel to the list above. A structure does not keep the same
    // address for its whole life — compacting it replaces it with a smaller copy at a new one — so the
    // placements that name it have to be able to find their way back to it and be corrected.
    private readonly List<uint> _instanceMeshIds = [];

    // Meshes forgotten since the last scene build, swept out of the lists above in one pass rather than
    // one pass each. Eviction destroys a whole neighbourhood of terrain pages at once, and a per-mesh
    // removal walks every placement in the level for each of them -- quadratic in exactly the moment the
    // player is moving fast enough to trigger it, which is where the freeze came from.
    private readonly HashSet<uint> _forgotten = [];

    // Held rather than written inline, so the sweep below binds no closure and allocates nothing.
    private readonly Predicate<Placement> _forgottenPlacement;

    // Streamed meshes waiting to stop growing, and the ones that have.
    private readonly Dictionary<uint, Streaming> _streaming = [];
    private readonly Dictionary<uint, int> _settled = [];
    private int _frame;

    // Building a structure is not free, and the first frame of a level offers every mesh in it at once.
    // Admitting a few a frame spreads that over the first second of play, which nobody sees, rather than
    // stalling on the frame the level appears.
    private const int MaxNewGeometriesPerFrame = 8;

    // How many frames a streamed mesh's size must hold still before it is built over.
    private const int SettleFrames = 30;

    // Which material ranges of each mesh have been drawn, in the order they were first seen -- which is
    // the order they become geometries in, and therefore the geometry index a hit reports. Accumulated
    // like the placements are: a mesh's ranges are discovered over the first frames it is visible for,
    // and the set only grows.
    private readonly Dictionary<uint, List<RayTracedSurface>> _surfaces = [];

    // Meshes whose range set has grown since their structure was built, and so need building again.
    private readonly HashSet<uint> _stale = [];

    // The material records, one per geometry of every built structure, in the layout the ray shader
    // reads them through a device address. Append-only: a structure rebuilt over a larger range set is
    // given a fresh run of records rather than overwriting the run a frame in flight may still be
    // reading, which is the whole of the synchronisation this needs.
    private readonly VulkanBuffer _records;
    private int _recordCount;
    private bool _recordOverflow;

    private int _newThisFrame;
    private bool _instancesChanged;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanWorldScene"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device to build on.</param>
    /// <param name="allocator">The allocator backing the structures.</param>
    public VulkanWorldScene(Vk vk, VulkanRayTracingApi api, VulkanDevice device, VulkanAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);

        _vk = vk;
        _api = api;
        _device = device;
        _allocator = allocator;
        _scene = new VulkanRayTracingScene(vk, api, device, allocator, MaxInstances);
        _forgottenPlacement = placement => _forgotten.Contains(placement.MeshId);

        // Host visible and written directly. The records are tiny, they change only when a structure is
        // built, and the alternative -- staging into device-local memory -- would need a transfer
        // recorded and fenced for what amounts to thirty-two bytes.
        _records = new VulkanBuffer(
            vk,
            device,
            allocator,
            (ulong)(MaxRecords * RecordSize),
            BufferUsageFlags.ShaderDeviceAddressBit | BufferUsageFlags.StorageBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            persistentlyMapped: true);
    }

    /// <summary>Gets the scene structure, or <see langword="null"/> before anything has been built.</summary>
    public VulkanAccelerationStructure? TopLevel => _scene.TopLevel;

    /// <summary>Gets a value indicating whether the scene holds anything worth tracing against.</summary>
    public bool IsReady => _scene.IsReady;

    /// <summary>Gets how many distinct placements the scene has accumulated.</summary>
    public int PlacementCount => _instances.Count;

    /// <summary>Gets how many meshes the scene holds structures for.</summary>
    public int GeometryCount => _geometries.Count;

    /// <summary>Gets the device address of the material records a ray shader looks a hit up in.</summary>
    public ulong RecordAddress => _records.DeviceAddress;

    /// <summary>Records a mesh being drawn somewhere, adding it to the traceable world if it is new.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh itself.</param>
    /// <param name="worldTransform">Where it is being drawn, in absolute world space.</param>
    /// <param name="surface">Which run of the mesh's indices this draw covered, and what it is made of.</param>
    /// <remarks>
    /// Called for every geometry-pass draw, so it must be cheap when both the placement and the range are
    /// ones already known — which after the first seconds of a level is every one of them. A hash lookup
    /// and a walk of the handful of ranges a mesh has is the whole cost.
    /// </remarks>
    public void Observe(uint meshId, VulkanMesh mesh, in Matrix4f worldTransform, in RayTracedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        // Nothing to trace: an empty mesh, or one whose geometry has not been uploaded yet.
        if (mesh.DrawableIndexCount < 3)
        {
            return;
        }

        // Skinned geometry is rewritten every frame as the body moves, so the triangles a structure was
        // built over are not the ones that are there now.
        if (mesh.Animated)
        {
            return;
        }

        // Streamed geometry -- the terrain, which is the only thing that uses mesh storage -- is appended
        // to as the world loads. Its written range only ever grows and is always contiguous from zero, so
        // a structure built over it is sound; the danger is building one while it is still growing, and
        // then tracing against triangles that have since been replaced by a larger buffer. So it waits
        // until its size has held still for a while, and is taken as it stood then. Terrain that streams
        // in afterwards is not traced: the structure is not rebuilt, which is the next piece of work here
        // and the reason this is still behind a flag.
        if (mesh.Storage && !IsSettled(meshId, mesh.DrawableIndexCount))
        {
            return;
        }

        // Which runs of this mesh have been drawn, and what each is made of. The terrain is the one
        // exception: it is streamed, single-material, and drawn as hundreds of ranges of one buffer, so
        // it stays a single geometry over the whole of itself rather than becoming hundreds of them.
        List<RayTracedSurface> surfaces = SurfacesFor(meshId, mesh, surface, out bool grew);

        // A settled terrain mesh that has since grown is rebuilt over its new, larger extent. Its
        // structure was taken when it first settled; the triangles the world streamed in afterwards are
        // not in it, so a ray passes straight through terrain that is plainly there on screen. Now that
        // it has settled again at the larger size, build a fresh structure over the whole of it, retire
        // the old one once the frames still tracing against it have finished, and let the placements
        // repoint themselves to the new address on the next scene build. Held to the same per-frame
        // budget as a new mesh, and never done while the old structure is still moving under compaction.
        //
        // A mesh whose material ranges have grown -- a part of it drawn for the first time this frame --
        // is rebuilt for the same reason and on the same terms: the structure it has covers fewer runs
        // than the mesh turns out to have.
        if (_geometries.TryGetValue(meshId, out Geometry built)
            && (grew || (mesh.Storage && built.IndexCount != mesh.DrawableIndexCount))
            && !_scene.IsCompacting(built.Structure)
            && _newThisFrame < MaxNewGeometriesPerFrame)
        {
            _geometries[meshId] = BuildGeometry(mesh, surfaces);
            _scene.RetireStructure(built.Structure);
            _stale.Remove(meshId);
            _newThisFrame++;
        }

        Placement placement = Placement.From(meshId, worldTransform);
        if (!_placed.Add(placement))
        {
            return;
        }

        if (_instances.Count >= MaxInstances)
        {
            return;
        }

        if (!_geometries.TryGetValue(meshId, out Geometry geometry))
        {
            if (_newThisFrame >= MaxNewGeometriesPerFrame)
            {
                // Not admitted this frame. The placement is left unrecorded so the next frame offers it
                // again rather than remembering a mesh it never built.
                _placed.Remove(placement);
                return;
            }

            geometry = BuildGeometry(mesh, surfaces);
            _geometries[meshId] = geometry;
            _stale.Remove(meshId);
            _newThisFrame++;
        }

        _instances.Add(VulkanRayTracingInstance.Create(
            worldTransform,

            // Not the mesh: the first of this structure's material records. A hit adds the geometry index
            // traversal reports to it and lands on the record for the run it actually struck.
            customIndex: (uint)geometry.RecordBase & 0x00FFFFFFu,
            VulkanRayTracingInstance.VisibleToAll,
            hitGroupOffset: 0,

            // The port's meshes do not wind consistently — the renderer draws them with culling off for
            // exactly that reason — so traversal must not cull by facing either, or a shadow would be cast
            // by only half the triangles that should cast it.
            GeometryInstanceFlagsKHR.TriangleFacingCullDisableBitKhr,
            geometry.Structure.DeviceAddress));
        _instanceMeshIds.Add(meshId);

        _instancesChanged = true;
    }

    /// <summary>Adds a drawn range to what is known about a mesh, and says whether it was new.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh itself.</param>
    /// <param name="surface">The range this draw covered.</param>
    /// <param name="grew">Receives whether the range set gained an entry, so a built structure is stale.</param>
    /// <returns>Every range known for the mesh, in the order they become geometries in.</returns>
    private List<RayTracedSurface> SurfacesFor(
        uint meshId, VulkanMesh mesh, in RayTracedSurface surface, out bool grew)
    {
        if (!_surfaces.TryGetValue(meshId, out List<RayTracedSurface>? surfaces))
        {
            surfaces = [];
            _surfaces[meshId] = surfaces;
        }

        // The terrain is one geometry over the whole of itself whatever ranges it was drawn as. It is
        // opaque and single-material, so nothing is lost, and it is drawn as hundreds of small ranges of
        // one shared buffer -- as geometries those would cost more to build and traverse than the whole.
        RayTracedSurface entry = mesh.Storage
            ? new RayTracedSurface(0, mesh.DrawableIndexCount, Opaque: true, MaskLayer: -1)
            : surface;

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
        }

        // A terrain mesh that has grown replaces its single entry rather than gaining a second one that
        // overlaps it; every other mesh accumulates.
        if (mesh.Storage && surfaces.Count > 0)
        {
            surfaces[0] = entry;
        }
        else
        {
            surfaces.Add(entry);
        }

        grew = _geometries.ContainsKey(meshId);
        if (grew)
        {
            _stale.Add(meshId);
        }

        return surfaces;
    }

    /// <summary>Whether a streamed mesh has stopped growing for long enough to be worth building over.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="indexCount">How many indices it currently claims.</param>
    /// <returns><see langword="true"/> once the size has held still, and for every frame after.</returns>
    private bool IsSettled(uint meshId, int indexCount)
    {
        if (_settled.TryGetValue(meshId, out int settledCount))
        {
            if (settledCount == indexCount)
            {
                return true;
            }

            // It has grown since the structure was built over it -- the world streamed more of the
            // terrain in as the camera reached it. Treat it as unsettled again, so it is taken afresh
            // at the new size and rebuilt once it has held still there.
            _settled.Remove(meshId);
            _streaming[meshId] = new Streaming(indexCount, _frame);
            return false;
        }

        if (!_streaming.TryGetValue(meshId, out Streaming state) || state.IndexCount != indexCount)
        {
            _streaming[meshId] = new Streaming(indexCount, _frame);
            return false;
        }

        if (_frame - state.Since < SettleFrames)
        {
            return false;
        }

        _settled[meshId] = indexCount;
        _streaming.Remove(meshId);
        return true;
    }

    /// <summary>Records whatever building the scene needs this frame.</summary>
    /// <param name="commandBuffer">The command buffer to record into. Must not be inside a render pass.</param>
    /// <param name="queue">The queue the frame is submitted to.</param>
    public void Record(CommandBuffer commandBuffer, VulkanQueue queue)
    {
        _frame++;
        _newThisFrame = 0;

        // Run from inside the update rather than here: the addresses are only final once this frame's
        // compaction has swapped whatever it is going to swap.
        _scene.BeforeTopLevelBuild = PublishInstances;
        _scene.RecordUpdate(commandBuffer, queue);
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
    /// Compacting a structure replaces it with an exactly-sized copy and frees the original, which moves
    /// it. A placement recorded before that still names the old address, and a scene built over addresses
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

            ulong current = geometry.Structure.DeviceAddress;
            if (_instances[i].AccelerationStructureReference == current)
            {
                continue;
            }

            // The records move with the structure: a rebuild appends a fresh run rather than overwriting
            // the one a frame in flight may still be reading, so a placement that keeps the old custom
            // index would look its material up in records describing the structure it no longer names.
            VulkanRayTracingInstance instance = _instances[i];
            instance.AccelerationStructureReference = current;
            instance.InstanceCustomIndexAndMask =
                ((uint)geometry.RecordBase & 0x00FFFFFFu) | (instance.InstanceCustomIndexAndMask & 0xFF000000u);
            _instances[i] = instance;
            _instancesChanged = true;
        }
    }

    /// <summary>Drops every placement of a forgotten mesh, in one pass over the lists.</summary>
    /// <remarks>
    /// Runs immediately before the placements are handed to the scene, so a forgotten mesh's structure is
    /// never named by a scene that is built -- the same guarantee removing them one at a time gave, at one
    /// walk of the list per frame instead of one per mesh. The order of the survivors is preserved, which
    /// matters only in that the two lists stay parallel.
    /// </remarks>
    private void CompactForgotten()
    {
        if (_forgotten.Count == 0)
        {
            return;
        }

        int kept = 0;
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_forgotten.Contains(_instanceMeshIds[i]))
            {
                continue;
            }

            _instances[kept] = _instances[i];
            _instanceMeshIds[kept] = _instanceMeshIds[i];
            kept++;
        }

        _instances.RemoveRange(kept, _instances.Count - kept);
        _instanceMeshIds.RemoveRange(kept, _instanceMeshIds.Count - kept);

        // The dedupe set has to lose them too, or it holds a placement for every mesh the level ever
        // streamed out. A cached predicate, because RemoveWhere over a capturing lambda allocated a
        // closure per forgotten mesh -- once per evicted page, every time the player moved.
        _placed.RemoveWhere(_forgottenPlacement);
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
        }

        // This mesh's placements have to stop naming its structure before the next scene is built, or
        // every later trace reads geometry that has gone. They are marked here and swept in
        // CompactForgotten, which the same pre-build pass that repoints every placement already runs --
        // so a hundred evicted pages cost one walk of the instance list between them, not a hundred.
        _forgotten.Add(meshId);
        _streaming.Remove(meshId);
        _settled.Remove(meshId);
        _surfaces.Remove(meshId);
        _stale.Remove(meshId);
        _instancesChanged = true;
    }

    /// <summary>Drops the whole accumulated world, for a level change.</summary>
    public void Reset()
    {
        foreach (Geometry geometry in _geometries.Values)
        {
            geometry.Structure.Dispose();
        }

        _geometries.Clear();
        _surfaces.Clear();
        _stale.Clear();
        _recordCount = 0;
        _recordOverflow = false;
        _placed.Clear();
        _instances.Clear();
        _instanceMeshIds.Clear();
        _streaming.Clear();
        _settled.Clear();
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
    /// <returns>The structure, the run of records it was given, and the extent it covered.</returns>
    /// <remarks>
    /// The records are appended rather than written over the run the previous structure used. A frame in
    /// flight may still be tracing that structure and reading those records, and the cost of never
    /// touching them again is a few bytes per rebuild against having to fence a host write behind the
    /// frames that might read it.
    /// </remarks>
    private Geometry BuildGeometry(VulkanMesh mesh, List<RayTracedSurface> surfaces)
    {
        VulkanTriangleGeometry[] triangles = new VulkanTriangleGeometry[surfaces.Count];
        for (int i = 0; i < surfaces.Count; i++)
        {
            RayTracedSurface s = surfaces[i];
            triangles[i] = new VulkanTriangleGeometry(
                mesh.VertexBufferAddress,
                VertexStride: (uint)Vertex3D.Size,
                VertexCount: (uint)mesh.VertexCapacity,
                mesh.IndexBufferAddress,
                PrimitiveCount: (uint)(s.IndexCount / 3),
                FirstIndex: (uint)s.IndexOffset,
                Opaque: s.Opaque);
        }

        int recordBase = WriteRecords(mesh, surfaces);
        return new Geometry(_scene.AddGeometry(triangles), mesh.DrawableIndexCount, recordBase);
    }

    /// <summary>Appends one material record per range and returns where the run starts.</summary>
    /// <param name="mesh">The mesh the ranges belong to.</param>
    /// <param name="surfaces">The ranges, in geometry order.</param>
    /// <returns>The index of the first record, which is what the instance carries as its custom index.</returns>
    private int WriteRecords(VulkanMesh mesh, List<RayTracedSurface> surfaces)
    {
        if (_recordCount + surfaces.Count > MaxRecords)
        {
            // Out of records. The structure is still built and still occludes; what it loses is the
            // per-texel test, so a cut-out run reverts to casting the shadow of its whole triangle. Said
            // once, because silently doing that would look like the alpha test not working.
            if (!_recordOverflow)
            {
                _recordOverflow = true;
                Console.WriteLine(
                    $"[raytracing] material records exhausted at {MaxRecords}; cut-out surfaces admitted "
                    + "after this point cast as solid.");
            }

            return 0;
        }

        int start = _recordCount;
        Span<uint> record = stackalloc uint[RecordSize / sizeof(uint)];

        for (int i = 0; i < surfaces.Count; i++)
        {
            RayTracedSurface s = surfaces[i];
            ulong vertexAddress = mesh.VertexBufferAddress;
            ulong indexAddress = mesh.IndexBufferAddress;

            record[0] = (uint)vertexAddress;
            record[1] = (uint)(vertexAddress >> 32);
            record[2] = (uint)indexAddress;
            record[3] = (uint)(indexAddress >> 32);
            record[4] = (uint)s.IndexOffset;
            record[5] = unchecked((uint)s.MaskLayer);
            record[6] = 0;
            record[7] = 0;

            _records.WriteAt((ulong)((start + i) * RecordSize), (ReadOnlySpan<uint>)record);
        }

        _recordCount = start + surfaces.Count;
        return start;
    }

    /// <summary>One mesh's traceable form, and how much of it was built.</summary>
    /// <param name="Structure">The structure over its triangles.</param>
    /// <param name="IndexCount">How many indices it covered when it was built.</param>
    /// <param name="RecordBase">
    /// Where this structure's run of material records starts. An instance carries it as its custom index,
    /// so a hit reaches its own record by adding the geometry index traversal reports.
    /// </param>
    private readonly record struct Geometry(
        VulkanBottomLevelStructure Structure, int IndexCount, int RecordBase);

    /// <summary>A streamed mesh's size, and the frame it last changed on.</summary>
    /// <param name="IndexCount">How many indices it claimed then.</param>
    /// <param name="Since">The frame the size was last seen to change.</param>
    private readonly record struct Streaming(int IndexCount, int Since);

    /// <summary>A mesh at a place, quantised so that the same placement is recognised as the same one.</summary>
    /// <remarks>
    /// The transforms come back bit-identical frame to frame for static geometry, but quantising costs
    /// nothing and means a placement recomputed through a slightly different route is still recognised
    /// rather than added twice. A millimetre is far below anything a shadow could show.
    /// </remarks>
    private readonly record struct Placement(uint MeshId, long A, long B, long C)
    {
        public static Placement From(uint meshId, in Matrix4f m)
        {
            const float scale = 1000f;
            static long Pack(float x, float y, float z, float w) =>
                ((long)(x * scale) * 73856093)
                ^ ((long)(y * scale) * 19349663)
                ^ ((long)(z * scale) * 83492791)
                ^ ((long)(w * scale) * 32452843);

            return new Placement(
                meshId,
                Pack(m.M11, m.M12, m.M13, m.M14),
                Pack(m.M21, m.M22, m.M23, m.M24),
                Pack(m.M31, m.M32, m.M33, m.M34));
        }
    }
}
