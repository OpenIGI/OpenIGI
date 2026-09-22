using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Vulkan.Deferred;
using OpenIGI.Rendering.Vulkan.Interop;
using OpenIGI.Rendering.Vulkan.Shaders;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan;

/// <summary>
/// Asynchronous hardware occlusion queries: the Vulkan counterpart to the OpenGL backend's visibility
/// queries, deciding each frame which objects were hidden last frame so the renderer can skip them.
/// </summary>
/// <remarks>
/// <para>
/// An object registers a query, and each frame its bounding box is drawn into the geometry pass wrapped
/// in <c>vkCmdBeginQuery</c>/<c>vkCmdEndQuery</c>. The query counts how many of the box's fragments
/// passed the depth test the real geometry wrote; zero means the box was entirely behind nearer
/// geometry, so the object is skipped next frame. The result is read a frame or two later rather than
/// waited on, so the query never stalls the pipeline.
/// </para>
/// <para>
/// <b>Why the results lag, and why that is safe.</b> A query's result is not ready when the box is drawn;
/// it is ready once the GPU has drained that frame. The frame loop already waits a slot's fence before
/// handing the slot back out, so this reads a slot's results the next time the slot comes round — by when
/// they are guaranteed to have landed — and resets the slot's block for the frame about to record. The
/// conservative default until a result is known is <em>visible</em>: a query never hides geometry it has
/// not yet proven hidden, and a single empty result (a camera-edge race) is not trusted until a second
/// confirms it, so the failure mode is an extra draw, never a one-frame hole.
/// </para>
/// <para>
/// <b>The pool is partitioned by in-flight slot and compacted per frame.</b> Each slot owns a fixed block
/// of the pool; the queries a frame issues are packed into the front of its slot's block in issue order,
/// and the object-to-block mapping is remembered so the results can be matched back to their objects when
/// the slot is read. An object whose box could not fit the block that frame simply stays conservatively
/// visible.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanOcclusionQueries : IDisposable
{
    /// <summary>How many boxes one frame can probe. A box that does not fit stays conservatively visible.</summary>
    private const int PerFrameCapacity = 4096;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly VisibilityPipeline _pipeline;
    private readonly QueryPool _pool;
    private readonly int _slots;

    private readonly Dictionary<uint, State> _queries = [];
    private uint _nextId = 1;

    // Per in-flight slot: the (object, local index) pairs it issued, read back when the slot recycles.
    private readonly List<(uint Handle, int Local)>[] _slotIssued;
    private readonly int[] _slotNext;
    private readonly ulong[] _readback = new ulong[PerFrameCapacity];
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanOcclusionQueries"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device the pool and pipeline live on.</param>
    /// <param name="geometryPass">The deferred geometry pass the boxes are drawn into.</param>
    /// <param name="framesInFlight">How many slots the frame loop cycles through.</param>
    /// <exception cref="GraphicsDeviceException">The pool or pipeline could not be created.</exception>
    public VulkanOcclusionQueries(
        Vk vk,
        VulkanDevice device,
        RenderPass geometryPass,
        int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        _vk = vk;
        _device = device.Device;
        _slots = framesInFlight;
        _pipeline = new VisibilityPipeline(vk, device, geometryPass);

        _slotIssued = new List<(uint, int)>[framesInFlight];
        _slotNext = new int[framesInFlight];
        for (int i = 0; i < framesInFlight; i++)
        {
            _slotIssued[i] = [];
        }

        QueryPoolCreateInfo createInfo = new()
        {
            SType = StructureType.QueryPoolCreateInfo,
            QueryType = QueryType.Occlusion,
            QueryCount = (uint)(PerFrameCapacity * framesInFlight),
        };

        QueryPool pool;
        VkCheck.Ok(_vk.CreateQueryPool(_device, &createInfo, null, &pool), "vkCreateQueryPool (occlusion)");
        _pool = pool;
    }

    /// <summary>Registers a new query, conservatively visible until its box has been probed.</summary>
    /// <returns>The handle to test with <see cref="Visible"/> and probe with <see cref="Issue"/>.</returns>
    public OcclusionQueryHandle Create()
    {
        uint id = _nextId++;

        // The all-zero state is the conservative one -- not hidden, not pending, no empty probes -- so a
        // query that is never probed (the direct render path has no geometry pass to draw a box into)
        // keeps its object drawn. See the note on State for the constructor trap this replaced.
        _queries[id] = default;
        return new OcclusionQueryHandle(id);
    }

    /// <summary>Forgets a query. Any result still in flight for it is ignored when read.</summary>
    /// <param name="query">The query to forget. An invalid or unknown handle is ignored.</param>
    public void Destroy(OcclusionQueryHandle query)
    {
        if (query.IsValid)
        {
            _queries.Remove(query.Id);
        }
    }

    /// <summary>Gets whether an object should be drawn — its last known visibility.</summary>
    /// <param name="query">The query. An invalid, unknown or never-probed handle is treated as visible.</param>
    /// <returns><see langword="true"/> to draw the object.</returns>
    public bool Visible(OcclusionQueryHandle query)
        => !query.IsValid || !_queries.TryGetValue(query.Id, out State state) || !state.Hidden;

    /// <summary>
    /// Opens the frame: reads back the results this slot last recorded, then resets its block so this
    /// frame can write into it. Must run outside a render pass, at the top of the frame.
    /// </summary>
    /// <param name="commandBuffer">The frame's command buffer, freshly begun.</param>
    /// <param name="slot">The in-flight slot this frame owns.</param>
    public void BeginFrame(CommandBuffer commandBuffer, int slot)
    {
        if (_disposed)
        {
            return;
        }

        if (_slotIssued[slot].Count > 0)
        {
            ReadSlot(slot);
        }

        _slotIssued[slot].Clear();
        _slotNext[slot] = 0;

        _vk.CmdResetQueryPool(commandBuffer, _pool, (uint)(slot * PerFrameCapacity), (uint)PerFrameCapacity);
    }

    /// <summary>Probes one object's box against the depth already in the geometry pass.</summary>
    /// <param name="commandBuffer">The command buffer being recorded, inside the geometry pass.</param>
    /// <param name="slot">The in-flight slot this frame owns.</param>
    /// <param name="query">The object's query.</param>
    /// <param name="modelViewProjection">The object-to-clip transform for its box.</param>
    /// <param name="bounds">The box, in object space.</param>
    /// <remarks>
    /// Does nothing when the query is unknown, when it is still waiting on a prior result, or when the
    /// slot's block is full — in every case the object stays conservatively visible. The box inherits the
    /// viewport and scissor the world and terrain draws already set in this pass, so it rasterises exactly
    /// where they did.
    /// </remarks>
    public void Issue(
        CommandBuffer commandBuffer,
        int slot,
        OcclusionQueryHandle query,
        in Matrix4f modelViewProjection,
        in OcclusionBounds bounds)
    {
        if (_disposed
            || !query.IsValid
            || !_queries.TryGetValue(query.Id, out State state)
            || state.Pending
            || _slotNext[slot] >= PerFrameCapacity)
        {
            return;
        }

        int local = _slotNext[slot]++;
        uint index = (uint)((slot * PerFrameCapacity) + local);

        // Bound before every box, not once a frame: the terrain interleaves its query boxes with mesh
        // draws, so another pipeline may have been bound since the last box. The pipeline uses dynamic
        // viewport and scissor, which persist across a bind, so the ones the scene draws set still stand.
        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline.Pipeline);

        float* push = stackalloc float[(int)VisibilityPipeline.PushConstantSize / sizeof(float)];
        modelViewProjection.CopyColumnMajorTo(new Span<float>(push, 16));
        push[16] = bounds.Minimum.X; push[17] = bounds.Minimum.Y; push[18] = bounds.Minimum.Z; push[19] = 0f;
        push[20] = bounds.Maximum.X; push[21] = bounds.Maximum.Y; push[22] = bounds.Maximum.Z; push[23] = 0f;
        _vk.CmdPushConstants(
            commandBuffer,
            _pipeline.PipelineLayout,
            ShaderStageFlags.VertexBit,
            0,
            VisibilityPipeline.PushConstantSize,
            push);

        _vk.CmdBeginQuery(commandBuffer, _pool, index, 0);
        _vk.CmdDraw(commandBuffer, 36, 1, 0, 0);
        _vk.CmdEndQuery(commandBuffer, _pool, index);

        _queries[query.Id] = state with { Pending = true };
        _slotIssued[slot].Add((query.Id, local));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _vk.DestroyQueryPool(_device, _pool, null);
        _pipeline.Dispose();
    }

    private void ReadSlot(int slot)
    {
        int count = _slotNext[slot];
        if (count <= 0)
        {
            return;
        }

        uint first = (uint)(slot * PerFrameCapacity);
        fixed (ulong* data = _readback)
        {
            // The slot's fence has been waited, so every query in the block has landed; ask for 64-bit
            // results, which return at once on already-ready data.
            Result result = _vk.GetQueryPoolResults(
                _device,
                _pool,
                first,
                (uint)count,
                (nuint)(count * sizeof(ulong)),
                data,
                sizeof(ulong),
                QueryResultFlags.Result64Bit | QueryResultFlags.ResultWaitBit);

            if (result != Result.Success)
            {
                return;
            }
        }

        foreach ((uint handle, int local) in _slotIssued[slot])
        {
            if (local < 0 || local >= count || !_queries.TryGetValue(handle, out State state))
            {
                continue;
            }

            bool passed = _readback[local] != 0;

            // A single empty result can be a camera-edge race; requiring two keeps the conservative
            // failure mode of an extra draw rather than a one-frame hole.
            int emptyFrames = passed ? 0 : state.EmptyFrames + 1;
            _queries[handle] = state with
            {
                Pending = false,
                EmptyFrames = emptyFrames,
                Hidden = !passed && emptyFrames >= 2,
            };
        }
    }

    /// <summary>One object's visibility state, carried across frames.</summary>
    /// <remarks>
    /// Phrased so that <see langword="default"/> is the safe state: a struct's zero value is what an
    /// unprobed query holds, and it must mean "draw it". This used to carry a <c>Visible</c> flag defaulted
    /// to true in the primary constructor, and <see cref="Create"/> wrote <c>new State()</c> -- which on a
    /// struct binds to the implicit zero-initialised constructor, not the primary one, so every query was
    /// born hidden. The deferred presets probed and corrected that within two frames; the vanilla preset's
    /// direct path never probes, and every placement vanished as soon as the camera held still long enough
    /// for its verdict to be trusted.
    /// </remarks>
    /// <param name="Pending">Whether a probe is in flight and the object must not be probed again yet.</param>
    /// <param name="Hidden">Whether the last probes proved the object hidden — the inverse of <see cref="Visible"/>.</param>
    /// <param name="EmptyFrames">Consecutive probes that found nothing, for the two-frame hysteresis.</param>
    private readonly record struct State(bool Pending, bool Hidden, int EmptyFrames);
}
