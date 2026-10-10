using OpenIGI.Rendering.D3D12.Sync;
using OpenIGI.Rendering.Rhi;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Rhi;

/// <summary>What a pass remembers it has bound, so a root signature change can bind it all again.</summary>
/// <remarks>
/// Direct3D 12 forgets every root argument when the root signature changes, where Vulkan keeps the sets a
/// compatible layout leaves alone. Two pipelines of one layout can have different root signatures here --
/// their shaders' registers differ -- so the pass keeps its groups and push data and binds them again into
/// whichever signature the next pipeline brings.
/// </remarks>
internal unsafe class D3D12PassState
{
    private const int MaxGroups = 8;

    private readonly (D3D12RhiBindGroup? Group, uint[] Offsets)[] _groups = new (D3D12RhiBindGroup?, uint[])[MaxGroups];
    private readonly byte[] _push = new byte[256];
    private int _pushEnd;
    private int _dirtyGroups;

    /// <summary>Initialises a new instance of the <see cref="D3D12PassState"/> class.</summary>
    /// <param name="owner">The encoder the pass records into.</param>
    /// <param name="graphics">Whether it binds the graphics root, rather than the compute one.</param>
    protected D3D12PassState(D3D12RhiCommandEncoder owner, bool graphics)
    {
        Owner = owner;
        Graphics = graphics;
    }

    /// <summary>Gets the encoder.</summary>
    protected D3D12RhiCommandEncoder Owner { get; }

    /// <summary>Gets the list.</summary>
    protected ID3D12GraphicsCommandList7* List => Owner.List;

    /// <summary>Gets a value indicating whether the graphics root is the one bound.</summary>
    protected bool Graphics { get; }

    /// <summary>Gets the root signature bound now, or null before a pipeline.</summary>
    protected D3D12RootSignature? Signature { get; private set; }

    /// <summary>Binds a pipeline's root signature and constants, binding the remembered state again if it changed.</summary>
    /// <param name="pipeline">The pipeline.</param>
    protected void BindRoot(D3D12RhiPipeline pipeline)
    {
        if (!ReferenceEquals(pipeline.RootSignature, Signature))
        {
            Signature = pipeline.RootSignature;
            if (Graphics)
            {
                List->SetGraphicsRootSignature(Signature.Native);
            }
            else
            {
                List->SetComputeRootSignature(Signature.Native);
            }

            for (int i = 0; i < MaxGroups; i++)
            {
                ApplyGroup(i);
            }

            ApplyPush(0, _pushEnd);
        }

        if (Signature.Constants >= 0 && pipeline.Constants is { } constants)
        {
            if (Graphics)
            {
                List->SetGraphicsRootConstantBufferView((uint)Signature.Constants, constants.GpuAddress);
            }
            else
            {
                List->SetComputeRootConstantBufferView((uint)Signature.Constants, constants.GpuAddress);
            }
        }
    }

    /// <summary>Remembers a group and binds it, if a pipeline is bound.</summary>
    /// <param name="index">The group index.</param>
    /// <param name="group">The group.</param>
    /// <param name="dynamicOffsets">Its dynamic offsets, in binding order.</param>
    protected void BindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets)
    {
        ArgumentNullException.ThrowIfNull(group);
        if ((uint)index >= MaxGroups)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"A pass binds at most {MaxGroups} groups.");
        }

        _groups[index] = ((D3D12RhiBindGroup)group, dynamicOffsets.ToArray());
        _dirtyGroups |= 1 << index;
        ApplyGroup(index);
    }

    /// <summary>Tells the tracker what the bound groups are used for by the next draw or dispatch.</summary>
    /// <param name="all">
    /// Every group, rather than only those bound since the last call. A dispatch reads what the one before
    /// it wrote through the very same group; a draw's groups are read the same way by every draw.
    /// </param>
    protected void UseGroups(bool all)
    {
        for (int i = 0; i < MaxGroups; i++)
        {
            if ((all || (_dirtyGroups & (1 << i)) != 0) && _groups[i].Group is { } group)
            {
                Owner.Use(group);
            }
        }

        _dirtyGroups = 0;
    }

    /// <summary>Remembers push data and sets it, if a pipeline is bound.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="offset">Where in the block they start.</param>
    protected void Push(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + data.Length > _push.Length || offset % 4 != 0 || data.Length % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Push data is whole words within 256 bytes.");
        }

        data.CopyTo(_push.AsSpan(offset));
        _pushEnd = Math.Max(_pushEnd, offset + data.Length);
        ApplyPush(offset, data.Length);
    }

    private void ApplyGroup(int index)
    {
        if (Signature is null || _groups[index].Group is not { } group || index >= Signature.Groups.Length)
        {
            return;
        }

        D3D12RhiDevice device = Owner.Device;
        D3D12GroupParameters parameters = Signature.Groups[index];

        if (parameters.Table >= 0 && group.Table >= 0)
        {
            GpuDescriptorHandle handle = device.ShaderHeap.Gpu(group.Table);
            if (Graphics)
            {
                List->SetGraphicsRootDescriptorTable((uint)parameters.Table, handle);
            }
            else
            {
                List->SetComputeRootDescriptorTable((uint)parameters.Table, handle);
            }
        }

        if (parameters.Samplers >= 0 && group.Samplers is { } samplers)
        {
            GpuDescriptorHandle handle = device.SamplerHeap.Gpu(samplers.Start);
            if (Graphics)
            {
                List->SetGraphicsRootDescriptorTable((uint)parameters.Samplers, handle);
            }
            else
            {
                List->SetComputeRootDescriptorTable((uint)parameters.Samplers, handle);
            }
        }

        uint[] offsets = _groups[index].Offsets;
        for (int i = 0; i < parameters.Dynamic.Length; i++)
        {
            (int parameter, RootParameterType type) = parameters.Dynamic[i];
            if (parameter < 0)
            {
                continue;
            }

            ulong address = group.DynamicAddresses[i] + (i < offsets.Length ? offsets[i] : 0u);
            switch (type, Graphics)
            {
                case (RootParameterType.TypeCbv, true):
                    List->SetGraphicsRootConstantBufferView((uint)parameter, address);
                    break;
                case (RootParameterType.TypeCbv, false):
                    List->SetComputeRootConstantBufferView((uint)parameter, address);
                    break;
                case (RootParameterType.TypeUav, true):
                    List->SetGraphicsRootUnorderedAccessView((uint)parameter, address);
                    break;
                case (RootParameterType.TypeUav, false):
                    List->SetComputeRootUnorderedAccessView((uint)parameter, address);
                    break;
                case (_, true):
                    List->SetGraphicsRootShaderResourceView((uint)parameter, address);
                    break;
                default:
                    List->SetComputeRootShaderResourceView((uint)parameter, address);
                    break;
            }
        }
    }

    private void ApplyPush(int offset, int length)
    {
        if (Signature is null || Signature.Push < 0 || length == 0)
        {
            return;
        }

        int words = Math.Min(length / 4, Signature.PushWords - (offset / 4));
        if (words <= 0)
        {
            return;
        }

        fixed (byte* bytes = &_push[offset])
        {
            if (Graphics)
            {
                List->SetGraphicsRoot32BitConstants((uint)Signature.Push, (uint)words, bytes, (uint)(offset / 4));
            }
            else
            {
                List->SetComputeRoot32BitConstants((uint)Signature.Push, (uint)words, bytes, (uint)(offset / 4));
            }
        }
    }
}

/// <summary>A render pass being recorded.</summary>
internal sealed unsafe class D3D12RhiRenderPassEncoder : D3D12PassState, IRhiRenderPassEncoder
{
    private readonly RhiRenderPassDescriptor _descriptor;
    private readonly (D3D12RhiBuffer? Buffer, ulong Offset)[] _vertexBuffers = new (D3D12RhiBuffer?, ulong)[8];
    private D3D12RhiRenderPipeline? _pipeline;
    private D3D12RhiBuffer? _indexBuffer;
    private bool _vertexBuffersDirty;
    private bool _inputsUsed;
    private int _occlusionQuery = -1;
    private bool _ended;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiRenderPassEncoder"/> class.</summary>
    /// <param name="owner">The encoder.</param>
    /// <param name="descriptor">The pass.</param>
    public D3D12RhiRenderPassEncoder(D3D12RhiCommandEncoder owner, RhiRenderPassDescriptor descriptor)
        : base(owner, graphics: true)
    {
        _descriptor = descriptor;
    }

    /// <inheritdoc/>
    public void SetPipeline(IRhiRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        D3D12RhiRenderPipeline graphics = (D3D12RhiRenderPipeline)pipeline;
        if (ReferenceEquals(graphics, _pipeline))
        {
            return;
        }

        BindRoot(graphics);
        List->SetPipelineState(graphics.Native);
        List->IASetPrimitiveTopology(graphics.Topology);

        // The strides come with the pipeline here, so a new pipeline means the buffers are set again.
        _vertexBuffersDirty = true;
        _pipeline = graphics;
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default) =>
        BindGroup(index, group, dynamicOffsets);

    /// <inheritdoc/>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0) => Push(data, offset);

    /// <inheritdoc/>
    public void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _vertexBuffers[slot] = ((D3D12RhiBuffer)buffer, offset);
        _vertexBuffersDirty = true;
        _inputsUsed = false;
    }

    /// <inheritdoc/>
    public void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        D3D12RhiBuffer index = (D3D12RhiBuffer)buffer;
        _indexBuffer = index;
        _inputsUsed = false;
        IndexBufferView view = new()
        {
            BufferLocation = index.GpuAddress + offset,
            SizeInBytes = (uint)(index.SizeInBytes - offset),
            Format = D3D12Translation.IndexFormat(format),
        };
        List->IASetIndexBuffer(&view);
    }

    /// <inheritdoc/>
    public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f)
    {
        Silk.NET.Direct3D12.Viewport viewport = new(x, y, width, height, minDepth, maxDepth);
        List->RSSetViewports(1, &viewport);
    }

    /// <inheritdoc/>
    public void SetScissor(int x, int y, int width, int height)
    {
        Silk.NET.Maths.Box2D<int> rectangle = new(x, y, x + width, y + height);
        List->RSSetScissorRects(1, &rectangle);
    }

    /// <inheritdoc/>
    public void SetBlendConstant(in RhiClearColor color)
    {
        float* factor = stackalloc float[4] { color.R, color.G, color.B, color.A };
        List->OMSetBlendFactor(factor);
    }

    /// <inheritdoc/>
    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        BeforeDraw(null);
        List->DrawInstanced((uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance);
    }

    /// <inheritdoc/>
    public void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0, int baseVertex = 0, int firstInstance = 0)
    {
        BeforeDraw(null);
        List->DrawIndexedInstanced((uint)indexCount, (uint)instanceCount, (uint)firstIndex, baseVertex, (uint)firstInstance);
    }

    /// <inheritdoc/>
    public void DrawIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        D3D12RhiBuffer buffer = (D3D12RhiBuffer)arguments;
        BeforeDraw(buffer);
        List->ExecuteIndirect(Owner.Device.DrawSignature, 1, buffer.Resource, buffer.Offset + offset, null, 0);
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        D3D12RhiBuffer buffer = (D3D12RhiBuffer)arguments;
        BeforeDraw(buffer);
        List->ExecuteIndirect(Owner.Device.DrawIndexedSignature, 1, buffer.Resource, buffer.Offset + offset, null, 0);
    }

    /// <inheritdoc/>
    public void BeginOcclusionQuery(int queryIndex)
    {
        if (_descriptor.OcclusionQuerySet is not D3D12RhiQuerySet set)
        {
            throw new InvalidOperationException("The pass was begun with no occlusion query set.");
        }

        _occlusionQuery = queryIndex;
        List->BeginQuery(set.Heap, QueryType.Occlusion, (uint)queryIndex);
    }

    /// <inheritdoc/>
    public void EndOcclusionQuery()
    {
        if (_descriptor.OcclusionQuerySet is D3D12RhiQuerySet set && _occlusionQuery >= 0)
        {
            List->EndQuery(set.Heap, QueryType.Occlusion, (uint)_occlusionQuery);
            _occlusionQuery = -1;
        }
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label) => Owner.BeginEvent(label);

    /// <inheritdoc/>
    public void PopDebugGroup() => Owner.EndEvent();

    /// <inheritdoc/>
    public void EndPass()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        foreach (RhiColorAttachment attachment in _descriptor.ColorAttachments)
        {
            D3D12RhiTexture texture = ((D3D12RhiTextureView)attachment.View).Owner;
            if (attachment.ResolveTarget is D3D12RhiTextureView resolve)
            {
                Owner.Use(texture, D3D12Use.ResolveSource);
                Owner.Use(resolve.Owner, D3D12Use.ResolveDest);
                Owner.FlushBarriers();
                List->ResolveSubresource(
                    resolve.Owner.Resource,
                    0,
                    texture.Resource,
                    0,
                    D3D12Translation.ViewFormat(resolve.Format));
            }
        }

        Owner.EndPass(_descriptor.Timestamps);
    }

    /// <inheritdoc/>
    public void Dispose() => EndPass();

    /// <summary>Everything a draw does before it is recorded: its inputs' uses, their barriers, the vertex buffers.</summary>
    /// <param name="arguments">The indirect arguments, if it has any.</param>
    private void BeforeDraw(D3D12RhiBuffer? arguments)
    {
        UseGroups(all: false);
        if (!_inputsUsed)
        {
            foreach ((D3D12RhiBuffer? buffer, _) in _vertexBuffers)
            {
                if (buffer is not null)
                {
                    Owner.Use(buffer, D3D12Use.Vertex);
                }
            }

            if (_indexBuffer is not null)
            {
                Owner.Use(_indexBuffer, D3D12Use.Index);
            }

            _inputsUsed = true;
        }

        if (arguments is not null)
        {
            Owner.Use(arguments, D3D12Use.Indirect);
        }

        Owner.FlushBarriers();
        FlushVertexBuffers();
    }

    private void FlushVertexBuffers()
    {
        if (!_vertexBuffersDirty || _pipeline is null)
        {
            return;
        }

        _vertexBuffersDirty = false;
        int[] strides = _pipeline.Strides;
        int count = Math.Min(strides.Length, _vertexBuffers.Length);
        if (count == 0)
        {
            return;
        }

        VertexBufferView* views = stackalloc VertexBufferView[count];
        for (int slot = 0; slot < count; slot++)
        {
            (D3D12RhiBuffer? buffer, ulong offset) = _vertexBuffers[slot];
            views[slot] = buffer is null
                ? default
                : new VertexBufferView
                {
                    BufferLocation = buffer.GpuAddress + offset,
                    SizeInBytes = (uint)(buffer.SizeInBytes - offset),
                    StrideInBytes = (uint)strides[slot],
                };
        }

        List->IASetVertexBuffers(0, (uint)count, views);
    }
}

/// <summary>A compute pass being recorded.</summary>
internal sealed unsafe class D3D12RhiComputePassEncoder : D3D12PassState, IRhiComputePassEncoder
{
    private readonly RhiComputePassDescriptor _descriptor;
    private D3D12RhiComputePipeline? _pipeline;
    private bool _ended;

    /// <summary>Initialises a new instance of the <see cref="D3D12RhiComputePassEncoder"/> class.</summary>
    /// <param name="owner">The encoder.</param>
    /// <param name="descriptor">The pass.</param>
    public D3D12RhiComputePassEncoder(D3D12RhiCommandEncoder owner, in RhiComputePassDescriptor descriptor)
        : base(owner, graphics: false)
    {
        _descriptor = descriptor;
    }

    /// <inheritdoc/>
    public void SetPipeline(IRhiComputePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        D3D12RhiComputePipeline compute = (D3D12RhiComputePipeline)pipeline;
        if (ReferenceEquals(compute, _pipeline))
        {
            return;
        }

        BindRoot(compute);
        List->SetPipelineState(compute.Native);
        _pipeline = compute;
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default) =>
        BindGroup(index, group, dynamicOffsets);

    /// <inheritdoc/>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0) => Push(data, offset);

    /// <inheritdoc/>
    public void Dispatch(int x, int y = 1, int z = 1)
    {
        BeforeDispatch(null);
        List->Dispatch((uint)x, (uint)y, (uint)z);
    }

    /// <inheritdoc/>
    public void DispatchIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        D3D12RhiBuffer buffer = (D3D12RhiBuffer)arguments;
        BeforeDispatch(buffer);
        List->ExecuteIndirect(Owner.Device.DispatchSignature, 1, buffer.Resource, buffer.Offset + offset, null, 0);
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label) => Owner.BeginEvent(label);

    /// <inheritdoc/>
    public void PopDebugGroup() => Owner.EndEvent();

    /// <inheritdoc/>
    public void EndPass()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        Owner.EndPass(_descriptor.Timestamps);
    }

    /// <inheritdoc/>
    public void Dispose() => EndPass();

    /// <summary>
    /// Everything a dispatch uses, told to the tracker: every bound group, every time, because a dispatch may
    /// read what the one before it wrote through the very same group. The tracker puts a barrier between them
    /// only where one wrote something the next touches.
    /// </summary>
    private void BeforeDispatch(D3D12RhiBuffer? arguments)
    {
        UseGroups(all: true);
        if (arguments is not null)
        {
            Owner.Use(arguments, D3D12Use.Indirect);
        }

        Owner.FlushBarriers();
    }
}
