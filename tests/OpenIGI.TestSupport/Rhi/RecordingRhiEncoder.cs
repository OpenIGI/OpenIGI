using OpenIGI.Rendering.Rhi;

namespace OpenIGI.TestSupport.Rhi;

/// <summary>An encoder that writes down what it was asked to record rather than recording it.</summary>
/// <param name="label">Its name.</param>
public sealed class RecordingRhiEncoder(string label) : IRhiCommandEncoder
{
    private readonly List<RecordedRhiCommand> _commands = [];

    /// <inheritdoc/>
    public string Label { get; } = label;

    /// <summary>Gets what has been recorded so far.</summary>
    public IReadOnlyList<RecordedRhiCommand> Commands => _commands;

    /// <summary>Gets whether a pass is open, which is what forbids recording anything else.</summary>
    public bool InPass { get; private set; }

    /// <inheritdoc/>
    public IRhiRenderPassEncoder BeginRenderPass(RhiRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ThrowIfInPass();

        List<string> targets = [];
        List<bool> clears = [];

        foreach (RhiColorAttachment attachment in descriptor.ColorAttachments)
        {
            targets.Add(attachment.View.Label);
            clears.Add(attachment.Load is RhiLoadOp.Clear);
        }

        Add(new RecordedRhiCommand.BeginRenderPass(
            descriptor.Label,
            targets,
            descriptor.DepthAttachment?.View.Label,
            clears));

        InPass = true;
        return new RecordingRhiRenderPass(this, descriptor);
    }

    /// <inheritdoc/>
    public IRhiComputePassEncoder BeginComputePass(in RhiComputePassDescriptor descriptor = default)
    {
        ThrowIfInPass();
        Add(new RecordedRhiCommand.BeginComputePass(descriptor.Label));

        InPass = true;
        return new RecordingRhiComputePass(this, descriptor.Label);
    }

    /// <inheritdoc/>
    public void CopyBufferToBuffer(
        IRhiBuffer source,
        ulong sourceOffset,
        IRhiBuffer destination,
        ulong destinationOffset,
        ulong sizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfInPass();

        // The bytes move as well as being recorded, so a test can drive a staged upload end to end.
        if (source is RecordedBuffer from && destination is RecordedBuffer to)
        {
            from.Contents.AsSpan((int)sourceOffset, (int)sizeInBytes)
                .CopyTo(to.Contents.AsSpan((int)destinationOffset));
        }

        Add(new RecordedRhiCommand.CopyBufferToBuffer(source.Label, destination.Label, sizeInBytes));
    }

    /// <inheritdoc/>
    public void CopyBufferToTexture(
        in RhiBufferTextureCopy source,
        in RhiTextureRegion destination,
        in RhiExtent3D size)
    {
        ThrowIfInPass();
        Add(new RecordedRhiCommand.CopyBufferToTexture(source.Buffer.Label, destination.Texture.Label));
    }

    /// <inheritdoc/>
    public void CopyTextureToBuffer(
        in RhiTextureRegion source,
        in RhiBufferTextureCopy destination,
        in RhiExtent3D size)
    {
        ThrowIfInPass();
        Add(new RecordedRhiCommand.CopyTextureToBuffer(source.Texture.Label, destination.Buffer.Label));
    }

    /// <inheritdoc/>
    public void CopyTextureToTexture(
        in RhiTextureRegion source,
        in RhiTextureRegion destination,
        in RhiExtent3D size)
    {
        ThrowIfInPass();
        Add(new RecordedRhiCommand.CopyTextureToTexture(source.Texture.Label, destination.Texture.Label));
    }

    /// <inheritdoc/>
    public void BuildAccelerationStructures(
        ReadOnlySpan<RhiBottomLevelBuild> bottomLevel,
        RhiTopLevelBuild? topLevel,
        RhiTimestampWrites? timestamps = null)
    {
        ThrowIfInPass();

        List<(string, bool)> bottom = [];
        foreach (RhiBottomLevelBuild build in bottomLevel)
        {
            bottom.Add((build.Destination.Label, build.Update));
        }

        Add(new RecordedRhiCommand.BuildAccelerationStructures(
            bottom, topLevel?.Destination.Label, topLevel?.InstanceCount ?? 0));
    }

    /// <inheritdoc/>
    public void ClearBuffer(IRhiBuffer buffer, ulong offset = 0, ulong sizeInBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfInPass();

        ulong length = sizeInBytes == 0 ? buffer.SizeInBytes - offset : sizeInBytes;

        if (buffer is RecordedBuffer recorded)
        {
            recorded.Contents.AsSpan((int)offset, (int)length).Clear();
        }

        Add(new RecordedRhiCommand.WriteBuffer(buffer.Label, offset, (int)length));
    }

    /// <inheritdoc/>
    public void ResolveQuerySet(
        IRhiQuerySet querySet,
        int firstQuery,
        int queryCount,
        IRhiBuffer destination,
        ulong destinationOffset)
    {
        ArgumentNullException.ThrowIfNull(querySet);
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfInPass();

        Add(new RecordedRhiCommand.CopyBufferToBuffer(
            querySet.Label,
            destination.Label,
            (ulong)queryCount * sizeof(ulong)));
    }

    /// <inheritdoc/>
    public IRhiCommandBuffer Finish(string label = "")
    {
        ThrowIfInPass();
        return new RecordedCommandBuffer(label.Length > 0 ? label : Label, [.. _commands]);
    }

    /// <inheritdoc/>
    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>Writes one command down.</summary>
    /// <param name="command">What was asked for.</param>
    internal void Add(RecordedRhiCommand command) => _commands.Add(command);

    /// <summary>Notes that the open pass has ended.</summary>
    internal void PassEnded() => InPass = false;

    private void ThrowIfInPass()
    {
        if (InPass)
        {
            throw new InvalidOperationException(
                $"The encoder '{Label}' has a pass open. End it before recording anything else — both "
                + "backends forbid this, and only one of them says so.");
        }
    }
}

/// <summary>What is common to recording inside a recorded pass.</summary>
/// <param name="encoder">The encoder the commands go to.</param>
/// <param name="label">The pass's name.</param>
public abstract class RecordingRhiPass(RecordingRhiEncoder encoder, string label) : IRhiPassEncoder
{
    private readonly List<string> _debugGroups = [];

    /// <summary>Gets the pass's name.</summary>
    public string Label { get; } = label;

    /// <summary>Gets whether the pass has ended.</summary>
    public bool Ended { get; private set; }

    /// <summary>Gets the encoder the commands go to.</summary>
    protected RecordingRhiEncoder Encoder { get; } = encoder;

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ThrowIfEnded();

        Encoder.Add(new RecordedRhiCommand.SetBindGroup(index, group.Label, dynamicOffsets.ToArray()));
    }

    /// <inheritdoc/>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.SetPushData(data.ToArray(), offset));
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label)
    {
        ThrowIfEnded();
        _debugGroups.Add(label);
    }

    /// <inheritdoc/>
    public void PopDebugGroup()
    {
        ThrowIfEnded();

        if (_debugGroups.Count == 0)
        {
            throw new InvalidOperationException(
                $"The pass '{Label}' popped a debug group it never pushed, which a capture would show as "
                + "a frame that ends inside a region.");
        }

        _debugGroups.RemoveAt(_debugGroups.Count - 1);
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfEnded();

        if (_debugGroups.Count > 0)
        {
            throw new InvalidOperationException(
                $"The pass '{Label}' ended inside the debug group '{_debugGroups[^1]}'.");
        }

        Ended = true;
        Encoder.PassEnded();
        Encoder.Add(new RecordedRhiCommand.EndPass(Label));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!Ended)
        {
            EndPass();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Throws when the pass has already ended.</summary>
    protected void ThrowIfEnded()
    {
        if (Ended)
        {
            throw new InvalidOperationException($"The pass '{Label}' has ended; nothing more goes into it.");
        }
    }
}

/// <summary>A render pass that writes its draws down.</summary>
/// <param name="encoder">The encoder the commands go to.</param>
/// <param name="descriptor">What the pass draws into.</param>
public sealed class RecordingRhiRenderPass(RecordingRhiEncoder encoder, RhiRenderPassDescriptor descriptor)
    : RecordingRhiPass(encoder, descriptor.Label), IRhiRenderPassEncoder
{
    /// <summary>Gets what the pass draws into.</summary>
    public RhiRenderPassDescriptor Descriptor { get; } = descriptor;

    /// <inheritdoc/>
    public void SetPipeline(IRhiRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ThrowIfEnded();

        Encoder.Add(new RecordedRhiCommand.SetPipeline(pipeline.Label));
    }

    /// <inheritdoc/>
    public void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0) => ThrowIfEnded();

    /// <inheritdoc/>
    public void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0) => ThrowIfEnded();

    /// <inheritdoc/>
    public void SetViewport(
        float x,
        float y,
        float width,
        float height,
        float minDepth = 0f,
        float maxDepth = 1f)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.SetViewport(x, y, width, height, minDepth, maxDepth));
    }

    /// <inheritdoc/>
    public void SetScissor(int x, int y, int width, int height)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.SetScissor(x, y, width, height));
    }

    /// <inheritdoc/>
    public void SetBlendConstant(in RhiClearColor color) => ThrowIfEnded();

    /// <inheritdoc/>
    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.Draw(vertexCount, instanceCount, Indexed: false));
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
        Encoder.Add(new RecordedRhiCommand.Draw(indexCount, instanceCount, Indexed: true));
    }

    /// <inheritdoc/>
    public void DrawIndirect(IRhiBuffer arguments, ulong offset)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.Draw(VertexCount: 0, InstanceCount: 0, Indexed: false));
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.Draw(VertexCount: 0, InstanceCount: 0, Indexed: true));
    }

    /// <inheritdoc/>
    public void BeginOcclusionQuery(int queryIndex) => ThrowIfEnded();

    /// <inheritdoc/>
    public void EndOcclusionQuery() => ThrowIfEnded();
}

/// <summary>A compute pass that writes its dispatches down.</summary>
/// <param name="encoder">The encoder the commands go to.</param>
/// <param name="label">The pass's name.</param>
public sealed class RecordingRhiComputePass(RecordingRhiEncoder encoder, string label)
    : RecordingRhiPass(encoder, label), IRhiComputePassEncoder
{
    /// <inheritdoc/>
    public void SetPipeline(IRhiComputePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ThrowIfEnded();

        Encoder.Add(new RecordedRhiCommand.SetPipeline(pipeline.Label));
    }

    /// <inheritdoc/>
    public void Dispatch(int x, int y = 1, int z = 1)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.Dispatch(x, y, z));
    }

    /// <inheritdoc/>
    public void DispatchIndirect(IRhiBuffer arguments, ulong offset)
    {
        ThrowIfEnded();
        Encoder.Add(new RecordedRhiCommand.Dispatch(X: 0, Y: 0, Z: 0));
    }
}
