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

        // The scratch buffer is carved up afresh every frame, so the batch is opened exactly once here —
        // sized for the mesh builds and the scene build together — and every range below comes out of it.
        BeginScratchBatch(submissionValue, queue.CompletedValue);

        RecordGeometryBuilds(commandBuffer, submissionValue);
        _compaction.Record(
            commandBuffer,
            queue,
            replaced => _retiring.Add((submissionValue, replaced)));

        // Built even with nothing in it. An empty structure is legal and every ray against it simply
        // misses, which is what a shader binding needs: a descriptor that is always valid to name, rather
        // than one that is only written once the world has been seen.
        // Everything above wrote acceleration structures the scene build is about to read.
        _barriers.AddAccelerationStructureBuild();
        _barriers.Flush(commandBuffer);

        _topLevel.RecordBuild(commandBuffer, _scratch.Allocate(_topLevel.BuildScratchSize));

        _barriers.AddAccelerationStructureBuild();
        _barriers.Flush(commandBuffer);
    }

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
