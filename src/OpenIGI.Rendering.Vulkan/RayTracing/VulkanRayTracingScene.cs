using OpenIGI.Rendering.Vulkan.Memory;
using OpenIGI.Rendering.Vulkan.Queues;
using OpenIGI.Rendering.Vulkan.Sync;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// The scene as the ray tracer sees it: a structure per mesh, one structure over all their placements,
/// and the machinery that keeps both built.
/// </summary>
/// <remarks>
/// <para>
/// Everything that traces — indirect diffuse, reflections, shadows — traces against the one top-level
/// structure this owns. Meshes register once and are built in the frames that follow; placements are
/// handed over each frame and the scene is rebuilt over them. Nothing here waits on the GPU: builds are
/// recorded into whatever command buffer the frame is using, and compaction advances a step per frame
/// off the queue's timeline.
/// </para>
/// <para>
/// The ordering within a frame is fixed and matters. Pending bottom-level builds go first, then a
/// barrier, then the top-level build that reads them, then a second barrier before anything traces. Both
/// barriers are coarse memory barriers rather than per-resource ones, because a structure is reached by
/// device address and there is no resource to name.
/// </para>
/// </remarks>
internal sealed class VulkanRayTracingScene : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanRayTracingApi _api;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly VulkanScratchBuffer _scratch;
    private readonly VulkanCompactionQueue _compaction;
    private readonly VulkanTopLevelStructure _topLevel;
    private readonly VulkanBarrierBatcher _barriers;
    private readonly List<VulkanBottomLevelStructure> _geometries = [];
    private readonly List<VulkanBottomLevelStructure> _pendingBuilds = [];
    private readonly List<(ulong RetireAfter, IDisposable Resource)> _retiring = [];
    private readonly List<VulkanBottomLevelStructure> _pendingRetire = [];

    // Frames beyond the retiring one to hold a replaced or compacted structure before freeing it: the
    // frame-in-flight depth (two) plus a frame of slack, so no frame still tracing it reads freed memory.
    private const ulong RetireSafetyFrames = 3;

    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRayTracingScene"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="api">The ray-tracing entry points.</param>
    /// <param name="device">The device to build on.</param>
    /// <param name="allocator">The allocator backing every structure.</param>
    /// <param name="instanceCapacity">The most placements the scene will ever hold.</param>
    public VulkanRayTracingScene(
        Vk vk,
        VulkanRayTracingApi api,
        VulkanDevice device,
        VulkanAllocator allocator,
        uint instanceCapacity)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);

        _vk = vk;
        _api = api;
        _device = device;
        _allocator = allocator;
        _barriers = new VulkanBarrierBatcher(vk);
        _scratch = new VulkanScratchBuffer(vk, device, allocator, api.ScratchAlignment);
        _compaction = new VulkanCompactionQueue(vk, device);
        _topLevel = new VulkanTopLevelStructure(vk, api, device, allocator, instanceCapacity);
    }

    /// <summary>Gets the scene structure, or <see langword="null"/> before the first build.</summary>
    public VulkanAccelerationStructure? TopLevel => _topLevel.Structure;

    /// <summary>Gets a value indicating whether the scene has been built and can be traced against.</summary>
    public bool IsReady => _topLevel.Structure is not null && _topLevel.InstanceCount > 0;

    /// <summary>Gets how many meshes the scene holds structures for.</summary>
    public int GeometryCount => _geometries.Count;

    /// <summary>Gets how many placements the last build covered.</summary>
    public uint InstanceCount => _topLevel.InstanceCount;

    /// <summary>Adds a mesh to the scene, to be built in a following frame.</summary>
    /// <param name="geometries">The triangle runs the mesh is made of.</param>
    /// <param name="allowUpdate">Whether the mesh's vertices will move.</param>
    /// <returns>The structure, whose device address instances refer to.</returns>
    public VulkanBottomLevelStructure AddGeometry(
        IReadOnlyList<VulkanTriangleGeometry> geometries,
        bool allowUpdate = false)
    {
        VulkanBottomLevelStructure structure =
            new(_vk, _api, _device, _allocator, geometries, allowUpdate);
        _geometries.Add(structure);
        _pendingBuilds.Add(structure);
        return structure;
    }

    /// <summary>Marks a mesh as needing to be rebuilt or refitted.</summary>
    /// <param name="structure">The structure to rebuild.</param>
    public void Invalidate(VulkanBottomLevelStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!_pendingBuilds.Contains(structure))
        {
            _pendingBuilds.Add(structure);
        }
    }

    /// <summary>Whether a structure is still moving under compaction and cannot yet be retired.</summary>
    /// <param name="structure">The structure a rebuild wants to replace.</param>
    /// <returns><see langword="true"/> while the compactor still names it.</returns>
    public bool IsCompacting(VulkanBottomLevelStructure structure) => _compaction.IsQueued(structure);

    /// <summary>Retires a structure a rebuild has replaced.</summary>
    /// <param name="structure">The structure no new build will name any more.</param>
    /// <remarks>
    /// It is dropped from the live set at once -- nothing built after this frame refers to it -- but its
    /// memory is only freed once the queue has passed this frame, because the scenes already in flight are
    /// still tracing rays against it. The freeing is dated in <see cref="RecordUpdate"/>, where the frame's
    /// submission value is known.
    /// </remarks>
    public void RetireStructure(VulkanBottomLevelStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _geometries.Remove(structure);
        _pendingRetire.Add(structure);
    }

    /// <summary>Sets where every mesh sits this frame.</summary>
    /// <param name="instances">The placements.</param>
    public void SetInstances(ReadOnlySpan<VulkanRayTracingInstance> instances) =>
        _topLevel.SetInstances(instances);

    /// <summary>
    /// Records everything the scene needs this frame: outstanding mesh builds, the scene build over the
    /// current placements, and one step of compaction.
    /// </summary>
    /// <param name="commandBuffer">The frame's command buffer.</param>
    /// <param name="queue">The queue the frame is submitted to.</param>
    /// <remarks>
    /// Leaves the scene ready to trace against by the end of the command buffer: the closing barrier
    /// makes the build visible to the compute stage, which is where every ray query runs.
    /// </remarks>
    public void RecordUpdate(CommandBuffer commandBuffer, VulkanQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        RetireCompleted(queue.CompletedValue);

        // This frame's work will be signalled by the queue's next value, which is what everything
        // deferred off this frame is dated against.
        ulong submissionValue = queue.LastSubmittedValue + 1;

        // A structure a build replaced or a compaction shrank is freed only after this many frames beyond
        // the one retiring it. The tight bound is the frame that retires it, but a structure can still be
        // named by the instances of a frame already in flight -- an outer bottom-level whose compacted copy
        // moved it, its old copy still traced by the frame the move has not reached yet. Freeing it on the
        // retiring frame's own value raced that trace and read freed acceleration-structure memory, which
        // the device-fault report caught as an invalid read under a fast-moving camera. Holding it past the
        // frames-in-flight depth (with a frame of slack) closes the window; the cost is a few structures
        // kept a little longer, which the compaction they came from more than pays back.
        ulong retireAfter = submissionValue + RetireSafetyFrames;

        // Structures a rebuild replaced this frame: freed once the queue passes this frame, by when the
        // scenes still tracing against them have all completed.
        for (int i = 0; i < _pendingRetire.Count; i++)
        {
            _retiring.Add((retireAfter, _pendingRetire[i]));
        }

        _pendingRetire.Clear();

        // The scratch buffer is carved up afresh every frame, so the batch is opened exactly once here —
        // sized for the mesh builds and the scene build together — and every range below comes out of it.
        BeginScratchBatch(submissionValue, queue.CompletedValue);

        // Before anything is written: the structures about to be rebuilt are the ones the frames already
        // in flight are tracing rays against, and a build is a write over what those rays are reading.
        _barriers.AddAccelerationStructureReadToBuild();
        _barriers.Flush(commandBuffer);

        RecordGeometryBuilds(commandBuffer, submissionValue);
        _compaction.Record(
            commandBuffer,
            queue,
            replaced => _retiring.Add((retireAfter, replaced)));

        // Only now are the geometry addresses settled. Compacting one moves it, and the placements that
        // name it have to be repointed before the scene is built over them — do it any earlier and this
        // frame's scene is built over the address of a structure this same frame has just retired.
        BeforeTopLevelBuild?.Invoke();

        // Built even with nothing in it. An empty structure is legal and every ray against it simply
        // misses, which is what a shader binding needs: a descriptor that is always valid to name, rather
        // than one that is only written once the world has been seen.
        // Everything above wrote acceleration structures the scene build is about to read.
        _barriers.AddAccelerationStructureBuild();
        _barriers.Flush(commandBuffer);

        _topLevel.RecordBuild(
            commandBuffer,
            _scratch.Allocate(_topLevel.BuildScratchSize),
            submissionValue,
            queue.CompletedValue);

        _barriers.AddAccelerationStructureBuild();
        _barriers.Flush(commandBuffer);
    }

    /// <summary>Gets or sets what to run once the geometry is built and compacted, before the scene is.</summary>
    /// <remarks>
    /// The window a caller needs if its instances name geometry by device address: compaction replaces a
    /// structure with a smaller copy and frees the original, so an address read before this point may be
    /// one that no longer exists by the time the scene is built over it.
    /// </remarks>
    public Action? BeforeTopLevelBuild { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach ((_, IDisposable resource) in _retiring)
        {
            resource.Dispose();
        }

        _retiring.Clear();

        foreach (VulkanBottomLevelStructure structure in _pendingRetire)
        {
            structure.Dispose();
        }

        _pendingRetire.Clear();

        foreach (VulkanBottomLevelStructure structure in _geometries)
        {
            structure.Dispose();
        }

        _geometries.Clear();
        _pendingBuilds.Clear();
        _topLevel.Dispose();
        _compaction.Dispose();
        _scratch.Dispose();
    }

    private void BeginScratchBatch(ulong submissionValue, ulong completedValue)
    {
        ulong total = VulkanRayTracingApi.Align(_topLevel.BuildScratchSize, _api.ScratchAlignment);
        foreach (VulkanBottomLevelStructure structure in _pendingBuilds)
        {
            total += VulkanRayTracingApi.Align(structure.BuildScratchSize, _api.ScratchAlignment);
        }

        // The scratch handed back is one no build still in flight is using, which is why the completed
        // value goes in: a batch recorded now must not write memory the previous frame is still building
        // out of.
        _scratch.BeginBatch(total, submissionValue, completedValue);
    }

    private void RecordGeometryBuilds(CommandBuffer commandBuffer, ulong submissionValue)
    {
        if (_pendingBuilds.Count == 0)
        {
            return;
        }

        foreach (VulkanBottomLevelStructure structure in _pendingBuilds)
        {
            ulong scratch = _scratch.Allocate(structure.BuildScratchSize);
            structure.RecordBuild(commandBuffer, scratch, update: structure.AllowUpdate && structure.IsBuilt);
            _compaction.Request(structure, submissionValue);
        }

        _pendingBuilds.Clear();
    }

    private void RetireCompleted(ulong completedValue)
    {
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            (ulong retireAfter, IDisposable resource) = _retiring[i];
            if (retireAfter > completedValue)
            {
                continue;
            }

            resource.Dispose();
            _retiring.RemoveAt(i);
        }
    }
}
