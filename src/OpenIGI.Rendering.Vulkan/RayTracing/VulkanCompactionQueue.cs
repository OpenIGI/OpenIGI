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

    /// <summary>Gets a value indicating whether anything is waiting to be compacted.</summary>
    public bool HasWork => _requested.Count > 0 || _active.Count > 0;

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
        VulkanQueue queue,
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
        _vk.DestroyQueryPool(_device.Device, _queryPool, null);
    }

    private void RecordQueries(CommandBuffer commandBuffer, VulkanQueue queue)
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

            // A zero size means the driver did not write the slot. Compacting to nothing would destroy
            // the structure, so the request is simply dropped.
            if (compacted == 0 || compacted >= structure.Structure.Size)
            {
                continue;
            }

            retire(structure.RecordCompaction(commandBuffer, compacted));
        }

        _active.Clear();
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
