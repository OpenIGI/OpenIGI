using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.WebGpu.Native;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>A WebGPU render pipeline.</summary>
public sealed unsafe class WebGpuRenderPipeline : WebGpuResource<WGPURenderPipeline>, IRhiRenderPipeline
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuRenderPipeline"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="layout">The layout it was built against.</param>
    /// <param name="label">The name it was created with.</param>
    public WebGpuRenderPipeline(WGPURenderPipeline handle, IRhiPipelineLayout layout, string label)
        : base(handle, label)
    {
        Layout = layout;
    }

    /// <inheritdoc/>
    public IRhiPipelineLayout Layout { get; }

    /// <inheritdoc/>
    protected override void Release(WGPURenderPipeline handle) => Wgpu.wgpuRenderPipelineRelease(handle);
}

/// <summary>
/// Records WebGPU draws.
/// </summary>
/// <remarks>
/// The viewport and the scissor are set here rather than baked into a pipeline, which is what lets one
/// pipeline survive a window resize. WebGPU defaults both to the whole attachment, so a pass that never
/// sets them still draws.
/// </remarks>
public sealed unsafe class WebGpuRenderPassEncoder : IRhiRenderPassEncoder
{
    // WebGPU's guaranteed minimums, which is as many as any pipeline here uses.
    private const int MaxGroups = 4;
    private const int MaxVertexBuffers = 8;

    private readonly WebGpuRhiDevice _device;
    private readonly WebGpuPushRing _pushes;
    private readonly int _pushGroup;
    private WGPURenderPassEncoder _handle;

    // What is bound now, so a call that would bind it again is not recorded. wgpu validates and translates
    // every recorded command when the encoder is finished, and on mission 1 over half of what the frame
    // recorded restated what was already bound: seventeen thousand draws carried twelve thousand
    // SetPipeline calls, all but twenty of them the same pipeline again, and most vertex and index buffer
    // binds likewise. That work was most of the eight milliseconds finish cost a frame.
    private readonly WGPUBindGroup[] _groups = new WGPUBindGroup[MaxGroups];
    private readonly uint[] _groupOffsets = new uint[MaxGroups];
    private readonly bool[] _groupHasOffset = new bool[MaxGroups];
    private readonly WGPUBuffer[] _vertexBuffers = new WGPUBuffer[MaxVertexBuffers];
    private readonly ulong[] _vertexOffsets = new ulong[MaxVertexBuffers];
    private WGPURenderPipeline _pipeline;

    // The last push's bytes, and where they landed. Two draws in three on mission 1 push exactly what the
    // draw before them pushed -- the parts of one object share its block -- and a push that repeats what
    // is still bound needs neither a slice of the ring nor a rebind.
    private readonly byte[] _lastPush = new byte[256];
    private int _lastPushLength = -1;
    private WebGpuPushSlice _lastSlice;

    // A pipeline whose layout reads its push block per instance (WebGpuInstancePushRing): its pushes go to that
    // ring, the chunk's group stays bound, and each draw names its block by its first instance. The slot is the
    // block the last push landed in, or -1 when the bytes in _lastPush have not been written there yet.
    private WebGpuInstancePushRing? _instanceRing;
    private long _instanceSlot = -1;

    // The indexed draw being held back so that the next one, if it repeats it with the block in the next slot,
    // can be folded into it as one more instance. Anything else recorded in between sends it first.
    private bool _held;
    private uint _heldIndexCount;
    private uint _heldFirstIndex;
    private int _heldBaseVertex;
    private uint _heldFirstSlot;
    private uint _heldInstances;

    private WGPUBuffer _indexBuffer;
    private WGPUIndexFormat _indexFormat;
    private ulong _indexOffset;
    private bool _ended;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuRenderPassEncoder"/> class.</summary>
    /// <param name="handle">The WebGPU handle.</param>
    /// <param name="pushes">The ring push data is written into.</param>
    /// <param name="pushGroup">The bind group the push block is bound as.</param>
    /// <param name="device">The device whose push group this binds.</param>
    public WebGpuRenderPassEncoder(
        WGPURenderPassEncoder handle,
        WebGpuPushRing pushes,
        int pushGroup,
        WebGpuRhiDevice device)
    {
        _handle = handle;
        _pushes = pushes;
        _pushGroup = pushGroup;
        _device = device;
    }

    /// <summary>Gets the group the push block is bound as.</summary>
    public int PushGroup => _pushGroup;

    /// <inheritdoc/>
    public void SetPipeline(IRhiRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ThrowIfEnded();

        WGPURenderPipeline handle = ((WebGpuRenderPipeline)pipeline).Handle;
        if (handle == _pipeline)
        {
            return;
        }

        SendHeld();
        _pipeline = handle;
        WebGpuHotCalls.wgpuRenderPassEncoderSetPipeline(_handle.Handle, handle.Handle);

        WebGpuInstancePushRing? ring = pipeline.Layout is WebGpuPipelineLayout { PushDataPerInstance: true } layout
            ? layout.InstanceRing ??= _device.InstancePushRing(layout.PushDataBytes)
            : null;

        if (ring == _instanceRing)
        {
            return;
        }

        _instanceRing = ring;
        _instanceSlot = -1;

        // A caller that pushes the same bytes again under the new pipeline is told they are already bound, so
        // they are put where this pipeline reads them now rather than left where the last one did.
        if (_lastPushLength >= 0)
        {
            Push(_lastPush.AsSpan(0, _lastPushLength));
        }
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ThrowIfEnded();

        WGPUBindGroup handle = ((WebGpuBindGroup)group).Handle;

        // A group with at most one dynamic offset -- every group here, the push group included -- is
        // remembered with it; one with more is always recorded.
        if ((uint)index < MaxGroups && dynamicOffsets.Length <= 1)
        {
            bool hasOffset = dynamicOffsets.Length == 1;
            uint offset = hasOffset ? dynamicOffsets[0] : 0u;

            if (_groups[index] == handle && _groupHasOffset[index] == hasOffset && _groupOffsets[index] == offset)
            {
                return;
            }

            SendHeld();

            _groups[index] = handle;
            _groupHasOffset[index] = hasOffset;
            _groupOffsets[index] = offset;
        }

        else
        {
            SendHeld();
        }

        fixed (uint* offsets = dynamicOffsets)
        {
            WebGpuHotCalls.wgpuRenderPassEncoderSetBindGroup(
                _handle.Handle,
                (uint)index,
                handle.Handle,
                (nuint)dynamicOffsets.Length,
                offsets);
        }
    }

    /// <summary>Binds the group push data is read through, at the offset the ring gave it.</summary>
    /// <param name="group">The push group, whose one entry is dynamic.</param>
    /// <param name="offset">Where in the ring the data landed.</param>
    public void SetPushGroup(IRhiBindGroup group, uint offset) =>
        SetBindGroup(_pushGroup, group, new ReadOnlySpan<uint>(in offset));

    /// <inheritdoc/>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0)
    {
        ThrowIfEnded();

        if (offset != 0)
        {
            throw new NotSupportedException(
                "A partial push is not something this backend can do: the block is a uniform binding here, "
                + "and a binding is written whole. Set the whole block.");
        }

        if (IsBound(data))
        {
            return;
        }

        Push(data);
    }

    // Writes a push where the bound pipeline reads it, binds it there, and remembers it.
    private void Push(ReadOnlySpan<byte> data)
    {
        if (_instanceRing is { } ring)
        {
            WebGpuInstancePush placed = ring.Write(data);
            SetBindGroup(_pushGroup, placed.Group);
            _instanceSlot = placed.Slot;
            Remember(data, default);
            return;
        }

        // The write AND the binding, because nothing above this knows any of the three things the
        // binding takes: the ring, the group index, and where the write landed in it. A pass that only
        // wrote would record a draw whose layout has an unbound group, which is refused outright.
        WebGpuPushSlice landed = _pushes.Write(data);
        LastPushOffset = landed.Offset;
        SetPushGroup(landed.Group, landed.Offset);
        Remember(data, landed);
    }

    private void Remember(ReadOnlySpan<byte> data, WebGpuPushSlice landed)
    {
        if (data.Length <= _lastPush.Length)
        {
            // Copied through a temporary when it is the remembered push itself, re-placed for a new pipeline.
            if (!data.Overlaps(_lastPush))
            {
                data.CopyTo(_lastPush);
            }

            _lastPushLength = data.Length;
            _lastSlice = landed;
        }
        else
        {
            _lastPushLength = -1;
        }
    }

    /// <summary>Gets where the last push landed in the ring.</summary>
    public uint LastPushOffset { get; private set; }

    /// <inheritdoc/>
    public void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfEnded();

        WGPUBuffer handle = ((WebGpuBuffer)buffer).Handle;

        if ((uint)slot < MaxVertexBuffers)
        {
            if (_vertexBuffers[slot] == handle && _vertexOffsets[slot] == offset)
            {
                return;
            }

            _vertexBuffers[slot] = handle;
            _vertexOffsets[slot] = offset;
        }

        SendHeld();

        WebGpuHotCalls.wgpuRenderPassEncoderSetVertexBuffer(
            _handle.Handle,
            (uint)slot,
            handle.Handle,
            offset,
            buffer.SizeInBytes - offset);
    }

    /// <inheritdoc/>
    public void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfEnded();

        WGPUBuffer handle = ((WebGpuBuffer)buffer).Handle;
        WGPUIndexFormat translated = WebGpuTranslation.IndexFormat(format);

        if (handle == _indexBuffer && translated == _indexFormat && offset == _indexOffset)
        {
            return;
        }

        SendHeld();
        _indexBuffer = handle;
        _indexFormat = translated;
        _indexOffset = offset;

        WebGpuHotCalls.wgpuRenderPassEncoderSetIndexBuffer(
            _handle.Handle,
            handle.Handle,
            (uint)translated,
            offset,
            buffer.SizeInBytes - offset);
    }

    /// <inheritdoc/>
    public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f)
    {
        ThrowIfEnded();
        SendHeld();
        Wgpu.wgpuRenderPassEncoderSetViewport(_handle, x, y, width, height, minDepth, maxDepth);
    }

    /// <inheritdoc/>
    public void SetScissor(int x, int y, int width, int height)
    {
        ThrowIfEnded();
        SendHeld();
        Wgpu.wgpuRenderPassEncoderSetScissorRect(_handle, (uint)x, (uint)y, (uint)width, (uint)height);
    }

    /// <inheritdoc/>
    public void SetBlendConstant(in RhiClearColor color)
    {
        ThrowIfEnded();

        SendHeld();
        WGPUColor value = new() { r = color.R, g = color.G, b = color.B, a = color.A };
        Wgpu.wgpuRenderPassEncoderSetBlendConstant(_handle, &value);
    }

    /// <inheritdoc/>
    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        ThrowIfEnded();
        SendHeld();
        WebGpuHotCalls.wgpuRenderPassEncoderDraw(
            _handle.Handle, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, InstanceFor(instanceCount, firstInstance));
    }

    /// <inheritdoc/>
    public void DrawIndexed(
        int indexCount,
        int instanceCount = 1,
        int firstIndex = 0,
        int baseVertex = 0,
        int firstInstance = 0)
    {
        ThrowIfEnded();

        if (_instanceRing is null)
        {
            WebGpuHotCalls.wgpuRenderPassEncoderDrawIndexed(
                _handle.Handle, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, baseVertex, (uint)firstInstance);
            return;
        }

        uint slot = InstanceFor(instanceCount, firstInstance);

        // The same range again with its block in the next slot is the held draw's next instance: one draw
        // instead of two, and instances are drawn in order, which is the order the draws were issued in.
        if (_held
            && _heldIndexCount == (uint)indexCount
            && _heldFirstIndex == (uint)firstIndex
            && _heldBaseVertex == baseVertex
            && _heldFirstSlot + _heldInstances == slot)
        {
            _heldInstances++;
            return;
        }

        SendHeld();
        _held = true;
        _heldIndexCount = (uint)indexCount;
        _heldFirstIndex = (uint)firstIndex;
        _heldBaseVertex = baseVertex;
        _heldFirstSlot = slot;
        _heldInstances = 1;
    }

    /// <summary>Gets how many draws were folded into the instanced draw before them; for tests and diagnostics.</summary>
    public int FoldedDraws { get; private set; }

    // The draw's first instance under a per-instance pipeline is its block's slot, so such a draw is one
    // instance from zero; any other pipeline's is the caller's own.
    private uint InstanceFor(int instanceCount, int firstInstance)
    {
        if (_instanceRing is null)
        {
            return (uint)firstInstance;
        }

        if (instanceCount != 1 || firstInstance != 0)
        {
            throw new NotSupportedException(
                "A pipeline that reads its push block per instance draws one instance from zero; its first "
                + "instance is where the block landed.");
        }

        if (_instanceSlot < 0)
        {
            throw new InvalidOperationException("A pipeline that reads push data drew before any was pushed.");
        }

        return (uint)_instanceSlot;
    }

    // Records the held draw, if there is one, before anything that would change what it draws with.
    private void SendHeld()
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        FoldedDraws += (int)_heldInstances - 1;
        WebGpuHotCalls.wgpuRenderPassEncoderDrawIndexed(
            _handle.Handle, _heldIndexCount, _heldInstances, _heldFirstIndex, _heldBaseVertex, _heldFirstSlot);
    }

    /// <inheritdoc/>
    public void DrawIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        SendHeld();
        Wgpu.wgpuRenderPassEncoderDrawIndirect(_handle, ((WebGpuBuffer)arguments).Handle, offset);
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        SendHeld();
        Wgpu.wgpuRenderPassEncoderDrawIndexedIndirect(_handle, ((WebGpuBuffer)arguments).Handle, offset);
    }

    /// <inheritdoc/>
    public void BeginOcclusionQuery(int queryIndex)
    {
        ThrowIfEnded();
        SendHeld();
        Wgpu.wgpuRenderPassEncoderBeginOcclusionQuery(_handle, (uint)queryIndex);
    }

    /// <inheritdoc/>
    public void EndOcclusionQuery()
    {
        ThrowIfEnded();
        SendHeld();
        Wgpu.wgpuRenderPassEncoderEndOcclusionQuery(_handle);
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label)
    {
        ThrowIfEnded();
        SendHeld();
        using WebGpuString name = new(label);
        Wgpu.wgpuRenderPassEncoderPushDebugGroup(_handle, name.View);
    }

    /// <inheritdoc/>
    public void PopDebugGroup()
    {
        ThrowIfEnded();
        SendHeld();
        Wgpu.wgpuRenderPassEncoderPopDebugGroup(_handle);
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfEnded();
        SendHeld();
        Wgpu.wgpuRenderPassEncoderEnd(_handle);
        _ended = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_ended && _handle.IsValid)
        {
            SendHeld();
            Wgpu.wgpuRenderPassEncoderEnd(_handle);
            _ended = true;
        }

        if (_handle.IsValid)
        {
            Wgpu.wgpuRenderPassEncoderRelease(_handle);
            _handle = default;
        }
    }

    // Whether these bytes are the last push and its slice is what the push group still has bound; a
    // caller binding some other group there in between must not be answered with a stale slice.
    private bool IsBound(ReadOnlySpan<byte> data)
    {
        if (_lastPushLength != data.Length || (uint)_pushGroup >= MaxGroups)
        {
            return false;
        }

        // Per instance the chunk's group is what stays bound and the slot is the draw's to name, so a push that
        // repeats the last needs only that the last was placed and no offset group bound over its chunk since.
        bool placed = _instanceRing is not null
            ? _instanceSlot >= 0 && !_groupHasOffset[_pushGroup]
            : _lastSlice.Group is WebGpuBindGroup slice
                && _groups[_pushGroup] == slice.Handle
                && _groupHasOffset[_pushGroup]
                && _groupOffsets[_pushGroup] == _lastSlice.Offset;

        return placed && data.SequenceEqual(_lastPush.AsSpan(0, _lastPushLength));
    }

    private void ThrowIfEnded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ended)
        {
            throw new InvalidOperationException("The render pass has already ended.");
        }
    }
}
