using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Records one submission's work, to be replayed on the owning thread when it is submitted.</summary>
/// <remarks>
/// Recorded rather than issued, because the RHI's order is the queue's: a buffer written after a pass is recorded
/// and before it is submitted is read by that pass, and push data reaches its ring only when the device flushes the
/// ring at submission. Issuing as recorded would run the pass before either.
/// </remarks>
internal sealed class GlCommandEncoder(GlRhiDevice device, string label) : IRhiCommandEncoder
{
    // From the device's pool and back to it once replayed: a frame records tens of thousands of commands, and a
    // list grown afresh each time is megabytes of large-object garbage a frame, which the collector pays for in full.
    private readonly GlCommandList _commands = device.RentCommands();
    private bool _finished;

    public string Label { get; } = label;

    internal GlRhiDevice Device { get; } = device;

    internal void Add(in GlCommand command)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _commands.Add(command);
    }

    public IRhiRenderPassEncoder BeginRenderPass(RhiRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Add(new GlCommand { Op = GlOp.BeginRenderPass, A = descriptor });
        return new GlRenderPassEncoder(this, descriptor);
    }

    public IRhiComputePassEncoder BeginComputePass(in RhiComputePassDescriptor descriptor = default)
    {
        Add(new GlCommand
        {
            Op = GlOp.BeginComputePass,
            A = descriptor.Timestamps?.QuerySet,
            I0 = descriptor.Timestamps?.BeginIndex ?? -1,
            B = descriptor.Label,
        });

        return new GlComputePassEncoder(this, descriptor.Timestamps);
    }

    public void CopyBufferToBuffer(IRhiBuffer source, ulong sourceOffset, IRhiBuffer destination, ulong destinationOffset, ulong sizeInBytes) =>
        Add(new GlCommand
        {
            Op = GlOp.CopyBufferToBuffer,
            A = (GlBuffer)source,
            I0 = (long)sourceOffset,
            B = (GlBuffer)destination,
            I1 = (long)destinationOffset,
            I2 = (long)sizeInBytes,
        });

    public void CopyBufferToTexture(in RhiBufferTextureCopy source, in RhiTextureRegion destination, in RhiExtent3D size) =>
        Add(new GlCommand { Op = GlOp.CopyBufferToTexture, A = new GlTextureCopy(source, null, destination, size) });

    public void CopyTextureToBuffer(in RhiTextureRegion source, in RhiBufferTextureCopy destination, in RhiExtent3D size) =>
        Add(new GlCommand { Op = GlOp.CopyTextureToBuffer, A = new GlTextureCopy(destination, source, null, size) });

    public void CopyTextureToTexture(in RhiTextureRegion source, in RhiTextureRegion destination, in RhiExtent3D size) =>
        Add(new GlCommand { Op = GlOp.CopyTextureToTexture, A = new GlTextureCopy(null, source, destination, size) });

    public void ClearBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0) =>
        Add(new GlCommand
        {
            Op = GlOp.ClearBuffer,
            A = (GlBuffer)buffer,
            I0 = (long)offset,
            I1 = (long)(sizeInBytes > 0 ? sizeInBytes : buffer.SizeInBytes - offset),
        });

    public void ResolveQuerySet(IRhiQuerySet querySet, int firstQuery, int queryCount, IRhiBuffer destination, ulong destinationOffset) =>
        Add(new GlCommand
        {
            Op = GlOp.ResolveQuerySet,
            A = (GlQuerySet)querySet,
            I0 = firstQuery,
            I1 = queryCount,
            B = (GlBuffer)destination,
            I2 = (long)destinationOffset,
        });

    public IRhiCommandBuffer Finish(string label = "")
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _finished = true;
        return new GlCommandBuffer(Device, _commands, string.IsNullOrEmpty(label) ? Label : label);
    }

    public void Dispose()
    {
        if (!_finished)
        {
            _finished = true;
            Device.ReturnCommands(_commands);
        }
    }
}

/// <summary>What recording inside either kind of pass shares: groups, push data and debug labels.</summary>
internal abstract class GlPassEncoder(GlCommandEncoder encoder) : IRhiPassEncoder
{
    private readonly byte[] _lastPush = new byte[256];
    private int _lastPushLength = -1;

    protected GlCommandEncoder Encoder { get; } = encoder;

    protected bool Ended { get; private set; }

    /// <summary>Gets the ring the bound pipeline reads its push blocks per instance from, or null.</summary>
    protected Rhi.Push.RhiInstancePushRing? InstanceRing { get; private set; }

    /// <summary>Gets the slot the last per-instance push landed in, or -1 before any.</summary>
    protected long InstanceSlot { get; private set; } = -1;

    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        GlCommand command = new()
        {
            Op = GlOp.SetBindGroup,
            I0 = index,
            A = (GlBindGroup)group,
            I1 = dynamicOffsets.Length,
        };

        if (dynamicOffsets.Length > 0)
        {
            command.I2 = dynamicOffsets[0];
        }

        if (dynamicOffsets.Length > 1)
        {
            command.B = dynamicOffsets.ToArray();
        }

        Encoder.Add(command);
    }

    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset != 0)
        {
            throw new NotSupportedException(
                "A partial push is not something this backend can do: the block is a uniform binding here, and a "
                + "binding is written whole. Set the whole block.");
        }

        if (data.Length <= _lastPush.Length)
        {
            if (!data.Overlaps(_lastPush))
            {
                data.CopyTo(_lastPush);
            }

            _lastPushLength = data.Length;
        }
        else
        {
            _lastPushLength = -1;
        }

        Push(data);
    }

    public void PushDebugGroup(string label) => Encoder.Add(new GlCommand { Op = GlOp.PushDebugGroup, A = label });

    public void PopDebugGroup() => Encoder.Add(new GlCommand { Op = GlOp.PopDebugGroup });

    public abstract void EndPass();

    public void Dispose()
    {
        if (!Ended)
        {
            EndPass();
        }
    }

    protected void MarkEnded() => Ended = true;

    /// <summary>Points push data at a pipeline's ring, putting the last push where the new pipeline reads it.</summary>
    /// <remarks>
    /// Push data outlives a change of pipeline, as a push constant does; a pipeline reading it from another ring
    /// than the last one would otherwise read a block nobody wrote there.
    /// </remarks>
    protected void UseInstanceRing(Rhi.Push.RhiInstancePushRing? ring)
    {
        if (ring == InstanceRing)
        {
            return;
        }

        InstanceRing = ring;
        InstanceSlot = -1;

        if (_lastPushLength >= 0)
        {
            Push(_lastPush.AsSpan(0, _lastPushLength));
        }
    }

    private void Push(ReadOnlySpan<byte> data)
    {
        int group = Encoder.Device.PushGroup;

        if (InstanceRing is { } ring)
        {
            Rhi.Push.RhiInstancePush placed = ring.Write(data);
            SetBindGroup(group, placed.Group);
            InstanceSlot = placed.Slot;
            return;
        }

        Rhi.Push.RhiPushSlice slice = Encoder.Device.PushRing.Write(data);
        uint offset = slice.Offset;
        SetBindGroup(group, slice.Group, new ReadOnlySpan<uint>(in offset));
    }
}

/// <summary>Records draws.</summary>
internal sealed class GlRenderPassEncoder(GlCommandEncoder encoder, RhiRenderPassDescriptor descriptor)
    : GlPassEncoder(encoder), IRhiRenderPassEncoder
{
    public void SetPipeline(IRhiRenderPipeline pipeline)
    {
        GlRenderPipeline gl = (GlRenderPipeline)pipeline;
        Encoder.Add(new GlCommand { Op = GlOp.SetRenderPipeline, A = gl });
        UseInstanceRing(gl.InstanceRing);
    }

    public void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0) =>
        Encoder.Add(new GlCommand { Op = GlOp.SetVertexBuffer, I0 = slot, A = (GlBuffer)buffer, I1 = (long)offset });

    public void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0) =>
        Encoder.Add(new GlCommand { Op = GlOp.SetIndexBuffer, A = (GlBuffer)buffer, I0 = (long)format, I1 = (long)offset });

    public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f) =>
        Encoder.Add(new GlCommand { Op = GlOp.SetViewport, F0 = x, F1 = y, F2 = width, F3 = height, F4 = minDepth, F5 = maxDepth });

    public void SetScissor(int x, int y, int width, int height) =>
        Encoder.Add(new GlCommand { Op = GlOp.SetScissor, I0 = x, I1 = y, I2 = width, I3 = height });

    public void SetBlendConstant(in RhiClearColor color) =>
        Encoder.Add(new GlCommand { Op = GlOp.SetBlendConstant, F0 = color.R, F1 = color.G, F2 = color.B, F3 = color.A });

    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0) =>
        Encoder.Add(new GlCommand
        {
            Op = GlOp.Draw,
            I0 = vertexCount,
            I1 = instanceCount,
            I2 = firstVertex,
            I3 = InstanceFor(instanceCount, firstInstance),
        });

    public void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0, int baseVertex = 0, int firstInstance = 0) =>
        Encoder.Add(new GlCommand
        {
            Op = GlOp.DrawIndexed,
            I0 = indexCount,
            I1 = instanceCount,
            I2 = firstIndex,
            I3 = baseVertex,
            I4 = InstanceFor(instanceCount, firstInstance),
        });

    public void DrawIndirect(IRhiBuffer arguments, ulong offset) => Indirect(GlOp.DrawIndirect, arguments, offset, 1);

    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset) =>
        Indirect(GlOp.DrawIndexedIndirect, arguments, offset, 1);

    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset, int drawCount) =>
        Indirect(GlOp.DrawIndexedIndirect, arguments, offset, drawCount);

    public void BeginOcclusionQuery(int queryIndex) =>
        Encoder.Add(new GlCommand { Op = GlOp.BeginOcclusion, A = descriptor.OcclusionQuerySet, I0 = queryIndex });

    public void EndOcclusionQuery() => Encoder.Add(new GlCommand { Op = GlOp.EndOcclusion });

    public override void EndPass()
    {
        if (Ended)
        {
            return;
        }

        MarkEnded();
        Encoder.Add(new GlCommand { Op = GlOp.EndRenderPass, A = descriptor });
    }

    // An indirect draw's first instance is in a buffer the encoder never sees, so it cannot be pointed at the block as a
    // direct draw's is. The draw carries the block's slot instead, and the executor binds the push array from that block
    // on, where an instance of nought finds it. Every draw of the batch reads the one block, as they share one push.
    private void Indirect(GlOp op, IRhiBuffer arguments, ulong offset, int drawCount) =>
        Encoder.Add(new GlCommand
        {
            Op = op,
            A = (GlBuffer)arguments,
            I0 = (long)offset,
            I1 = drawCount,
            I2 = InstanceRing is null ? -1 : InstanceFor(1, 0),
            I3 = InstanceRing?.Stride ?? 0,
        });

    // A pipeline reading its push block per instance draws one instance from nought; its first instance is where
    // the block landed.
    private long InstanceFor(int instanceCount, int firstInstance)
    {
        if (InstanceRing is null)
        {
            return firstInstance;
        }

        if (instanceCount != 1 || firstInstance != 0)
        {
            throw new NotSupportedException(
                "A pipeline that reads its push block per instance draws one instance from zero; its first instance "
                + "is where the block landed.");
        }

        return InstanceSlot >= 0
            ? InstanceSlot
            : throw new InvalidOperationException("A pipeline that reads push data drew before any was pushed.");
    }
}

/// <summary>Records dispatches.</summary>
internal sealed class GlComputePassEncoder(GlCommandEncoder encoder, RhiTimestampWrites? timestamps)
    : GlPassEncoder(encoder), IRhiComputePassEncoder
{
    public void SetPipeline(IRhiComputePipeline pipeline) =>
        Encoder.Add(new GlCommand { Op = GlOp.SetComputePipeline, A = (GlComputePipeline)pipeline });

    public void Dispatch(int x, int y = 1, int z = 1) =>
        Encoder.Add(new GlCommand { Op = GlOp.Dispatch, I0 = x, I1 = y, I2 = z });

    public void DispatchIndirect(IRhiBuffer arguments, ulong offset) =>
        Encoder.Add(new GlCommand { Op = GlOp.DispatchIndirect, A = (GlBuffer)arguments, I0 = (long)offset });

    public override void EndPass()
    {
        if (Ended)
        {
            return;
        }

        MarkEnded();
        Encoder.Add(new GlCommand
        {
            Op = GlOp.EndComputePass,
            A = timestamps?.QuerySet,
            I0 = timestamps?.EndIndex ?? -1,
        });
    }
}
