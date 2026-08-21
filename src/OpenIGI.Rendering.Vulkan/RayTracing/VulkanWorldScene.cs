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

    // Streamed meshes waiting to stop growing, and the ones that have.
    private readonly Dictionary<uint, Streaming> _streaming = [];
    private readonly HashSet<uint> _settled = [];
    private int _frame;

    // Building a structure is not free, and the first frame of a level offers every mesh in it at once.
    // Admitting a few a frame spreads that over the first second of play, which nobody sees, rather than
    // stalling on the frame the level appears.
    private const int MaxNewGeometriesPerFrame = 8;

    // How many frames a streamed mesh's size must hold still before it is built over.
    private const int SettleFrames = 30;

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
    }

    /// <summary>Gets the scene structure, or <see langword="null"/> before anything has been built.</summary>
    public VulkanAccelerationStructure? TopLevel => _scene.TopLevel;

    /// <summary>Gets a value indicating whether the scene holds anything worth tracing against.</summary>
    public bool IsReady => _scene.IsReady;

    /// <summary>Gets how many distinct placements the scene has accumulated.</summary>
    public int PlacementCount => _instances.Count;

    /// <summary>Gets how many meshes the scene holds structures for.</summary>
    public int GeometryCount => _geometries.Count;

    /// <summary>Records a mesh being drawn somewhere, adding it to the traceable world if it is new.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="mesh">The mesh itself.</param>
    /// <param name="worldTransform">Where it is being drawn, in absolute world space.</param>
    /// <remarks>
    /// Called for every geometry-pass draw, so it must be cheap when the placement is one already known —
    /// which after the first seconds of a level is every one of them. A hash lookup is the whole cost.
    /// </remarks>
    public void Observe(uint meshId, VulkanMesh mesh, in Matrix4f worldTransform)
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

            geometry = new Geometry(BuildStructure(mesh), mesh.DrawableIndexCount);
            _geometries[meshId] = geometry;
            _newThisFrame++;
        }

        _instances.Add(VulkanRayTracingInstance.Create(
            worldTransform,
            customIndex: meshId & 0x00FFFFFFu,
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

    /// <summary>Whether a streamed mesh has stopped growing for long enough to be worth building over.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <param name="indexCount">How many indices it currently claims.</param>
    /// <returns><see langword="true"/> once the size has held still, and for every frame after.</returns>
    private bool IsSettled(uint meshId, int indexCount)
    {
        if (_settled.Contains(meshId))
        {
            return true;
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

        _settled.Add(meshId);
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
        RefreshStructureAddresses();

        if (_instancesChanged)
        {
            _scene.SetInstances(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_instances));
            _instancesChanged = false;
        }

        _scene.RecordUpdate(commandBuffer, queue);
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

            VulkanRayTracingInstance instance = _instances[i];
            instance.AccelerationStructureReference = current;
            _instances[i] = instance;
            _instancesChanged = true;
        }
    }

    /// <summary>Forgets a mesh that has been destroyed, so its structure is not kept alive by the scene.</summary>
    /// <param name="meshId">The mesh's identifier.</param>
    /// <remarks>
    /// The placements that referred to it stay in the instance list until the scene is next reset. They
    /// point at a structure that has been freed, so a mesh is only ever forgotten together with a reset.
    /// </remarks>
    public void Forget(uint meshId) => _geometries.Remove(meshId);

    /// <summary>Drops the whole accumulated world, for a level change.</summary>
    public void Reset()
    {
        foreach (Geometry geometry in _geometries.Values)
        {
            geometry.Structure.Dispose();
        }

        _geometries.Clear();
        _placed.Clear();
        _instances.Clear();
        _instanceMeshIds.Clear();
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
        _scene.Dispose();
    }

    private VulkanBottomLevelStructure BuildStructure(VulkanMesh mesh)
    {
        uint indexCount = (uint)mesh.DrawableIndexCount;
        VulkanTriangleGeometry triangles = new(
            mesh.VertexBufferAddress,
            VertexStride: (uint)Vertex3D.Size,
            VertexCount: (uint)mesh.VertexCapacity,
            mesh.IndexBufferAddress,
            PrimitiveCount: indexCount / 3);

        return _scene.AddGeometry([triangles]);
    }

    /// <summary>One mesh's traceable form, and how much of it was built.</summary>
    /// <param name="Structure">The structure over its triangles.</param>
    /// <param name="IndexCount">How many indices it covered when it was built.</param>
    private readonly record struct Geometry(VulkanBottomLevelStructure Structure, int IndexCount);

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
