using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline.Deferred;

/// <summary>
/// Asynchronous occlusion queries: which objects were hidden behind nearer geometry last time they were
/// probed, so the game can skip drawing them.
/// </summary>
/// <remarks>
/// <para>
/// An object registers a query; while the geometry pass is open its bounding box is drawn against the depth
/// the real geometry wrote, colour and depth writes off, inside an occlusion query. No sample passing means
/// the box was wholly behind something nearer.
/// </para>
/// <para>
/// <b>Results lag, and that is safe.</b> Each frame in flight owns a query set. Its results are resolved
/// as the frame ends and mapped without waiting; they are read when that slot comes round again. Until a
/// result is known an object is <em>visible</em>, and one empty result is not trusted until a second
/// confirms it -- a camera-edge race -- so the failure mode is an extra draw, never a one-frame hole.
/// </para>
/// <para>
/// A slot whose last results have not come back yet (a browser's maps can take longer than a frame)
/// probes nothing that frame rather than overwrite a buffer still being read: the objects keep what they
/// last knew.
/// </para>
/// </remarks>
public sealed class OcclusionQueries : IDisposable
{
    /// <summary>How many boxes one frame can probe. A box that does not fit stays as it was.</summary>
    /// <remarks>WebGPU's ceiling on a query set.</remarks>
    public const int PerFrameCapacity = 4096;

    /// <summary>How many bytes of push data the box shader reads: a matrix and two corners.</summary>
    public const int PushDataBytes = sizeof(float) * 24;

    private readonly IRhiDevice _device;
    private readonly IRhiShaderModule _module;
    private readonly IRhiPipelineLayout _layout;
    private readonly IRhiRenderPipeline _pipeline;
    private readonly IRhiQuerySet[] _sets;
    private readonly IRhiBuffer[] _resolved;
    private readonly IRhiBuffer[] _readback;
    private readonly IRhiReadback?[] _pending;
    private readonly List<(uint Handle, int Local)>[] _issued;
    private readonly int[] _next;
    private readonly bool[] _blocked;
    private readonly Dictionary<uint, State> _queries = [];
    private uint _nextId = 1;

    /// <summary>How many frames a hidden verdict stands without a fresh answer before the object is drawn again.</summary>
    /// <remarks>
    /// A hidden object is re-probed every frame, so on a path that probes, its answer is never more than
    /// a couple of frames old. A path that does not probe at all -- the direct path the vanilla preset
    /// draws through, which has no geometry pass to draw a box into -- would otherwise hold whatever was
    /// hidden when it took over hidden for good: switch from a deferred preset to vanilla and walk, and
    /// the terrain and buildings behind a fence stayed missing, the frame's black showing through it.
    /// </remarks>
    private const long StaleFrames = 8;

    private long _frame;
    private int _slot;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="OcclusionQueries"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="framesInFlight">How many frames may be in flight, each with a set of its own.</param>
    /// <param name="colourFormats">The geometry pass's colour targets, which the box pipeline must match.</param>
    /// <param name="depthFormat">Its depth target.</param>
    public OcclusionQueries(
        IRhiDevice device,
        int framesInFlight,
        IReadOnlyList<RhiTextureFormat> colourFormats,
        RhiTextureFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(colourFormats);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _device = device;

        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.Occlusion);
        _module = device.CreateShaderModule(new RhiShaderModuleDescriptor(ShaderModuleId.Occlusion, Label: "Occlusion"));
        _layout = device.CreatePipelineLayout(new RhiPipelineLayoutDescriptor(
            [],
            PushDataBytes,
            RhiShaderStages.Vertex,
            "Occlusion"));

        _pipeline = device.CreateRenderPipeline(new RhiRenderPipelineDescriptor(
            _layout,
            // Clip space the RHI's way, as the geometry pass draws it.
            new RhiShaderStage(
                _module,
                reflection.EntryPointFor(ShaderStage.Vertex).Name,
                ShaderConstants.Declared(reflection, Mesh3DConstants.TargetConstants)),
            new RhiShaderStage(_module, reflection.EntryPointFor(ShaderStage.Fragment).Name),
            [],

            // Every target the pass has, none of them written: the box only exercises the depth test.
            [.. colourFormats.Select(format => new RhiColorTargetState(format, Write: RhiColorWrite.None))],

            // Reverse-Z, as the geometry was drawn, and read-only: a probe must not occlude anything.
            new RhiDepthStencilState(depthFormat, DepthWrite: false, RhiCompare.GreaterEqual),

            // No culling, so a box the camera is inside still rasterises -- and counts as visible.
            new RhiPrimitiveState(RhiPrimitiveTopology.TriangleList, RhiCullMode.None),
            SampleCount: 1,
            "Occlusion"));

        _sets = new IRhiQuerySet[framesInFlight];
        _resolved = new IRhiBuffer[framesInFlight];
        _readback = new IRhiBuffer[framesInFlight];
        _pending = new IRhiReadback?[framesInFlight];
        _issued = new List<(uint, int)>[framesInFlight];
        _next = new int[framesInFlight];
        _blocked = new bool[framesInFlight];

        const ulong Bytes = PerFrameCapacity * sizeof(ulong);

        for (int i = 0; i < framesInFlight; i++)
        {
            _sets[i] = device.CreateQuerySet(new RhiQuerySetDescriptor(RhiQueryType.Occlusion, PerFrameCapacity, $"occlusion{i}"));
            _resolved[i] = device.CreateBuffer(new RhiBufferDescriptor(
                Bytes,
                RhiBufferUsage.QueryResolve | RhiBufferUsage.CopySource,
                $"occlusion{i}.resolved"));
            _readback[i] = device.CreateBuffer(new RhiBufferDescriptor(
                Bytes,
                RhiBufferUsage.CopyDestination | RhiBufferUsage.MapRead,
                $"occlusion{i}.readback"));
            _issued[i] = [];
        }
    }

    /// <summary>Gets the set the frame being recorded probes into, for its geometry pass to be opened with.</summary>
    public IRhiQuerySet CurrentSet => _sets[_slot];

    /// <summary>Registers a new query, visible until its box has been probed.</summary>
    /// <returns>The handle.</returns>
    public OcclusionQueryHandle Create()
    {
        uint id = _nextId++;

        // The zero state is the safe one -- not hidden, not pending -- so a query that is never probed
        // keeps its object drawn.
        _queries[id] = default;
        return new OcclusionQueryHandle(id);
    }

    /// <summary>Forgets a query. A result still in flight for it is ignored when read.</summary>
    /// <param name="query">The query. An invalid or unknown one is ignored.</param>
    public void Destroy(OcclusionQueryHandle query)
    {
        if (query.IsValid)
        {
            _queries.Remove(query.Id);
        }
    }

    /// <summary>Gets whether an object should be drawn: its last known visibility.</summary>
    /// <param name="query">The query. An invalid, unknown or never-probed one is visible.</param>
    /// <returns><see langword="true"/> to draw the object.</returns>
    public bool Visible(OcclusionQueryHandle query) =>
        !query.IsValid
        || !_queries.TryGetValue(query.Id, out State state)
        || !state.Hidden
        || _frame - state.AnsweredFrame > StaleFrames;

    /// <summary>Opens a frame: reads what its slot last probed, and starts the slot afresh.</summary>
    /// <param name="slot">The frame-in-flight slot the frame records into, whose last frame has finished.</param>
    public void BeginFrame(int slot)
    {
        if (_disposed)
        {
            return;
        }

        _frame++;
        _slot = slot;
        _blocked[slot] = false;

        if (_issued[slot].Count > 0 && !ReadSlot(slot))
        {
            // Not back yet: this slot probes nothing this frame, and its results are read next time.
            _blocked[slot] = true;
            return;
        }

        _issued[slot].Clear();
        _next[slot] = 0;
    }

    /// <summary>Probes one object's box against the depth the geometry pass has drawn so far.</summary>
    /// <param name="pass">The geometry pass, opened with <see cref="CurrentSet"/>.</param>
    /// <param name="query">The object's query.</param>
    /// <param name="modelViewProjection">The object-to-clip transform its box is drawn with.</param>
    /// <param name="bounds">The box, in object space.</param>
    /// <remarks>
    /// Does nothing when the query is unknown, still waiting on an earlier result, or the slot is full or
    /// still being read -- in every case the object keeps what it last knew.
    /// </remarks>
    public void Issue(IRhiRenderPassEncoder pass, OcclusionQueryHandle query, in Matrix4f modelViewProjection, in OcclusionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(pass);

        int slot = _slot;

        if (_disposed
            || _blocked[slot]
            || !query.IsValid
            || !_queries.TryGetValue(query.Id, out State state)
            || state.Pending
            || _next[slot] >= PerFrameCapacity)
        {
            return;
        }

        int local = _next[slot]++;

        Span<float> push = stackalloc float[PushDataBytes / sizeof(float)];
        modelViewProjection.CopyColumnMajorTo(push[..16]);
        push[16] = bounds.Minimum.X;
        push[17] = bounds.Minimum.Y;
        push[18] = bounds.Minimum.Z;
        push[19] = 0f;
        push[20] = bounds.Maximum.X;
        push[21] = bounds.Maximum.Y;
        push[22] = bounds.Maximum.Z;
        push[23] = 0f;

        // Bound before every box: the world interleaves its probes with its draws, so another pipeline
        // is likely bound since the last one.
        pass.SetPipeline(_pipeline);
        pass.SetPushData(MemoryMarshal.AsBytes(push));
        pass.BeginOcclusionQuery(local);
        pass.Draw(36);
        pass.EndOcclusionQuery();

        _queries[query.Id] = state with { Pending = true };
        _issued[slot].Add((query.Id, local));
    }

    /// <summary>Records the resolve of this frame's probes, outside any pass, before the frame is submitted.</summary>
    /// <param name="encoder">The frame's encoder.</param>
    public void ResolveFrame(IRhiCommandEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);

        int slot = _slot;
        int count = _next[slot];

        if (_disposed || _blocked[slot] || count <= 0)
        {
            return;
        }

        ulong bytes = (ulong)count * sizeof(ulong);
        encoder.ResolveQuerySet(_sets[slot], 0, count, _resolved[slot], 0);
        encoder.CopyBufferToBuffer(_resolved[slot], 0, _readback[slot], 0, bytes);
    }

    /// <summary>Starts mapping what the frame just submitted resolved, so it is there when the slot comes round.</summary>
    public void RequestReadback()
    {
        int slot = _slot;
        int count = _next[slot];

        if (_disposed || _blocked[slot] || count <= 0 || _pending[slot] is not null)
        {
            return;
        }

        _pending[slot] = _device.ReadBuffer(_readback[slot], 0, (ulong)count * sizeof(ulong));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        for (int i = 0; i < _sets.Length; i++)
        {
            _pending[i]?.Dispose();
            _readback[i].Dispose();
            _resolved[i].Dispose();
            _sets[i].Dispose();
        }

        _pipeline.Dispose();
        _layout.Dispose();
        _module.Dispose();
    }

    /// <summary>Folds a slot's results into the objects that probed, once they have come back.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns><see langword="false"/> while they are still on their way.</returns>
    private bool ReadSlot(int slot)
    {
        if (_pending[slot] is not { } data)
        {
            // Probed and never resolved -- the frame was abandoned. Nothing will come back: let the
            // objects probe again.
            ReleaseIssued(slot);
            return true;
        }

        if (!data.Wait(TimeSpan.Zero) && !data.IsComplete)
        {
            return false;
        }

        ReadOnlySpan<ulong> samples = MemoryMarshal.Cast<byte, ulong>(data.GetData());

        foreach ((uint handle, int local) in _issued[slot])
        {
            if (!_queries.TryGetValue(handle, out State state))
            {
                continue;
            }

            if (local >= samples.Length)
            {
                _queries[handle] = state with { Pending = false };
                continue;
            }

            bool passed = samples[local] != 0;

            // One empty result can be a camera-edge race; two in a row is the object being hidden.
            int empty = passed ? 0 : state.EmptyFrames + 1;
            _queries[handle] = new State(Pending: false, Hidden: !passed && empty >= 2, EmptyFrames: empty, AnsweredFrame: _frame);
        }

        data.Dispose();
        _pending[slot] = null;
        return true;
    }

    private void ReleaseIssued(int slot)
    {
        foreach ((uint handle, _) in _issued[slot])
        {
            if (_queries.TryGetValue(handle, out State state))
            {
                _queries[handle] = state with { Pending = false };
            }
        }
    }

    /// <summary>One object's visibility, carried across frames.</summary>
    /// <remarks>Phrased so the zero value -- what an unprobed query holds -- means "draw it".</remarks>
    /// <param name="Pending">Whether a probe is in flight, so the object is not probed again yet.</param>
    /// <param name="Hidden">Whether the last probes proved it hidden.</param>
    /// <param name="EmptyFrames">Consecutive probes that found nothing, for the two-result hysteresis.</param>
    /// <param name="AnsweredFrame">The frame its last answer was read in; see <see cref="StaleFrames"/>.</param>
    private readonly record struct State(bool Pending, bool Hidden, int EmptyFrames, long AnsweredFrame);
}
