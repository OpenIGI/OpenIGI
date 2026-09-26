using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Queues;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// Shrinks built acceleration structures to the size they actually need, a batch at a time.
/// </summary>
/// <remarks>
/// <para>
/// A build has to allocate for the worst case before it knows how the tree will come out, so a finished
/// structure typically occupies a good deal more memory than its nodes need — often half again. Asking
/// the driver afterwards how much it really needs, and copying into a buffer of exactly that size, gives
/// that memory back. For a world of static geometry, built once and traced for the rest of the session,
/// it is worth doing for everything large enough to matter.
/// </para>
/// <para>
/// The catch is that the size is a property of the <em>finished</em> structure, so the query cannot be
/// recorded in the same command buffer as the build — the build has not run yet. And the query's result
/// cannot be read until the query itself has run. So compaction is a three-frame handshake: build, then
/// query once the build has completed, then copy once the query has been read. All three steps are
/// driven off the queue's timeline, and nothing anywhere waits.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanCompactionQueue : IDisposable
{
    // How many structures are compacted at once. One query pool serves the batch, and a batch is in
    // flight for a few frames, so the number trades memory reclaimed per frame against pool size.
    private const uint BatchSize = 64;

    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly QueryPool _queryPool;
    private readonly List<Pending> _requested = [];
    private readonly List<VulkanBottomLevelStructure> _active = [];

    // Structures that were freed while the handshake still named them. They keep their place in _active
    // -- the query results come back by slot, so removing one would pair every later size with the wrong
    // structure -- and are skipped when the copies are recorded.
    private readonly HashSet<VulkanBottomLevelStructure> _abandoned = [];

    private ulong _queryCompletedAt;
    private bool _queriesRecorded;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanCompactionQueue"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the query pool lives on.</param>
    public VulkanCompactionQueue(Vk vk, VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device;

        QueryPoolCreateInfo createInfo = new()
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.AccelerationStructureCompactedSizeKhr,
            QueryCount = BatchSize,
        };

        QueryPool pool;
        VkCheck.Ok(_vk.CreateQueryPool(device.Device, &createInfo, null, &pool), "vkCreateQueryPool");
        _queryPool = pool;
        ResetPool();
    }

    /// <summary>Whether a structure is still somewhere in the compaction handshake.</summary>
    /// <param name="structure">The structure to check for.</param>
    /// <returns>
    /// <see langword="true"/> while the structure is queued for a size query or has one in flight -- the
    /// compactor is about to read it and copy from it, so it must not be freed. <see langword="false"/>
    /// once it has left the handshake, whether it was compacted or dropped for not shrinking, after which
    /// it can be replaced.
    /// </returns>
    public bool IsQueued(VulkanBottomLevelStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        foreach (Pending pending in _requested)
        {
            if (ReferenceEquals(pending.Structure, structure))
            {
                return true;
            }
        }

        return _active.Contains(structure);
    }

    /// <summary>Asks for a structure to be compacted once its build has completed.</summary>
    /// <param name="structure">The structure to compact.</param>
    /// <param name="buildCompletesAt">
    /// The timeline value of the submission that builds it. The query is not recorded until the queue has
    /// passed this.
    /// </param>
    public void Request(VulkanBottomLevelStructure structure, ulong buildCompletesAt)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!structure.AllowCompaction || structure.IsCompacted)
        {
            return;
        }

        _requested.Add(new Pending(structure, buildCompletesAt));
    }

    /// <summary>Advances the handshake by one step, recording whatever this frame can do.</summary>
    /// <param name="commandBuffer">The command buffer this frame is recording into.</param>
    /// <param name="queue">The queue the work is submitted to, whose timeline drives the handshake.</param>
    /// <param name="retire">
    /// Called with each structure a compaction replaced. The caller must keep it alive until the
    /// recorded copy has run, then dispose it.
    /// </param>
    /// <remarks>
    /// At most one of the two steps happens per call, and only when the GPU has actually got far enough
    /// for it to be legal. A frame where neither is ready does nothing at all.
    /// </remarks>
    public void Record(
        CommandBuffer commandBuffer,
        ISubmissionClock queue,
        Action<VulkanAccelerationStructure> retire)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(retire);

        if (_queriesRecorded)
        {
            if (queue.CompletedValue >= _queryCompletedAt)
            {
                RecordCompactions(commandBuffer, retire);
            }

            return;
        }

        RecordQueries(commandBuffer, queue);
    }

    /// <summary>Drops one structure from the handshake, because it is about to be freed.</summary>
    /// <param name="structure">The structure being retired.</param>
    /// <remarks>
    /// The handshake spans three frames and holds a reference across all of them, so a structure retired
    /// in the middle of one is still named by a copy the compactor has yet to record -- and the copy is
    /// recorded from the <em>source</em> structure, which by then has been freed. That is a read of freed
    /// acceleration-structure memory, which is what the device-fault report calls an invalid read: the
    /// device loss a menu backdrop swap produced, where a level's worth of meshes are destroyed a few
    /// frames after the last of them was built and requested. A structure that is going away has nothing
    /// to gain from being shrunk, so it simply leaves the queue.
    /// </remarks>
    public void Forget(VulkanBottomLevelStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        for (int i = _requested.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_requested[i].Structure, structure))
            {
                _requested.RemoveAt(i);
            }
        }

        if (_active.Contains(structure))
        {
            _abandoned.Add(structure);
        }
    }

    /// <summary>Forgets every structure queued or being measured, for a level change.</summary>
    /// <remarks>
    /// The structures themselves belong to the scene and are freed there. What this drops is the queue's
    /// references to them and the half-finished round of queries over them -- so the next round starts
    /// from an empty pool rather than reading sizes back for structures that no longer exist.
    /// Only safe with the device idle, which is the one condition a level change already meets.
    /// </remarks>
    public void Clear()
    {
        _requested.Clear();
        _active.Clear();
        _abandoned.Clear();
        _queriesRecorded = false;
        _queryCompletedAt = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requested.Clear();
        _active.Clear();
        _abandoned.Clear();
        _vk.DestroyQueryPool(_device.Device, _queryPool, null);
    }

    private void RecordQueries(CommandBuffer commandBuffer, ISubmissionClock queue)
    {
        ulong completed = queue.CompletedValue;

        // Only structures whose build has actually finished can be measured; the rest wait for a later
        // frame, keeping their place in the list.
        int taken = 0;
        for (int i = _requested.Count - 1; i >= 0 && _active.Count < BatchSize; i--)
        {
            Pending pending = _requested[i];
            if (pending.BuildCompletesAt > completed)
            {
                continue;
            }

            _active.Add(pending.Structure);
            _requested.RemoveAt(i);
            taken++;
        }

        if (taken == 0)
        {
            return;
        }

        ResetPool(commandBuffer);

        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].RecordCompactedSizeQuery(commandBuffer, _queryPool, (uint)i);
        }

        // The queries are part of the submission this frame is being recorded into, which will signal the
        // queue's next timeline value.
        _queryCompletedAt = queue.LastSubmittedValue + 1;
        _queriesRecorded = true;
    }

    private void RecordCompactions(CommandBuffer commandBuffer, Action<VulkanAccelerationStructure> retire)
    {
        ulong[] sizes = new ulong[_active.Count];
        fixed (ulong* first = sizes)
        {
            Result result = _vk.GetQueryPoolResults(
                _device.Device,
                _queryPool,
                0,
                (uint)_active.Count,
                (nuint)(sizes.Length * sizeof(ulong)),
                first,
                sizeof(ulong),
                QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit);

            if (result != Result.Success)
            {
                // The results are not ready after all. Leave the batch in place and try again next frame
                // rather than compacting to a size that was never written.
                return;
            }
        }

        for (int i = 0; i < _active.Count; i++)
        {
            ulong compacted = sizes[i];
            VulkanBottomLevelStructure structure = _active[i];

            // Retired under the compactor since the query was recorded. Its memory is gone or going, so
            // the copy would read what has been freed.
            if (_abandoned.Contains(structure))
            {
                continue;
            }

            // A zero size means the driver did not write the slot. Compacting to nothing would destroy
            // the structure, so the request is simply dropped.
            if (compacted == 0 || compacted >= structure.Structure.Size)
            {
                continue;
            }

            retire(structure.RecordCompaction(commandBuffer, compacted));
        }

        _active.Clear();
        _abandoned.Clear();
        _queriesRecorded = false;
    }

    private void ResetPool(CommandBuffer commandBuffer = default)
    {
        // Resetting from the host is free and needs no command buffer, which matters because the reset
        // has to happen before the queries are written into the same buffer.
        if (_device.Capabilities.HostQueryReset)
        {
            _vk.ResetQueryPool(_device.Device, _queryPool, 0, BatchSize);
            return;
        }

        if (commandBuffer.Handle != 0)
        {
            _vk.CmdResetQueryPool(commandBuffer, _queryPool, 0, BatchSize);
        }
    }

    private readonly record struct Pending(VulkanBottomLevelStructure Structure, ulong BuildCompletesAt);
}
