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
    private readonly WebGpuRhiDevice _device;
    private readonly WebGpuPushRing _pushes;
    private readonly int _pushGroup;
    private WGPURenderPassEncoder _handle;
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

        Wgpu.wgpuRenderPassEncoderSetPipeline(_handle, ((WebGpuRenderPipeline)pipeline).Handle);
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ThrowIfEnded();

        fixed (uint* offsets = dynamicOffsets)
        {
            Wgpu.wgpuRenderPassEncoderSetBindGroup(
                _handle,
                (uint)index,
                ((WebGpuBindGroup)group).Handle,
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

        // The write AND the binding, because nothing above this knows any of the three things the
        // binding takes: the ring, the group index, and where the write landed in it. A pass that only
        // wrote would record a draw whose layout has an unbound group, which is refused outright.
        LastPushOffset = _pushes.Write(data);
        SetPushGroup(_device.PushGroup, LastPushOffset);
    }

    /// <summary>Gets where the last push landed in the ring.</summary>
    public uint LastPushOffset { get; private set; }

    /// <inheritdoc/>
    public void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfEnded();

        Wgpu.wgpuRenderPassEncoderSetVertexBuffer(
            _handle,
            (uint)slot,
            ((WebGpuBuffer)buffer).Handle,
            offset,
            buffer.SizeInBytes - offset);
    }

    /// <inheritdoc/>
    public void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfEnded();

        Wgpu.wgpuRenderPassEncoderSetIndexBuffer(
            _handle,
            ((WebGpuBuffer)buffer).Handle,
            WebGpuTranslation.IndexFormat(format),
            offset,
            buffer.SizeInBytes - offset);
    }

    /// <inheritdoc/>
    public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f)
    {
        ThrowIfEnded();
        Wgpu.wgpuRenderPassEncoderSetViewport(_handle, x, y, width, height, minDepth, maxDepth);
    }

    /// <inheritdoc/>
    public void SetScissor(int x, int y, int width, int height)
    {
        ThrowIfEnded();
        Wgpu.wgpuRenderPassEncoderSetScissorRect(_handle, (uint)x, (uint)y, (uint)width, (uint)height);
    }

    /// <inheritdoc/>
    public void SetBlendConstant(in RhiClearColor color)
    {
        ThrowIfEnded();

        WGPUColor value = new() { r = color.R, g = color.G, b = color.B, a = color.A };
        Wgpu.wgpuRenderPassEncoderSetBlendConstant(_handle, &value);
    }

    /// <inheritdoc/>
    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        ThrowIfEnded();
        Wgpu.wgpuRenderPassEncoderDraw(
            _handle, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance);
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
        Wgpu.wgpuRenderPassEncoderDrawIndexed(
            _handle, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, baseVertex, (uint)firstInstance);
    }

    /// <inheritdoc/>
    public void DrawIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        Wgpu.wgpuRenderPassEncoderDrawIndirect(_handle, ((WebGpuBuffer)arguments).Handle, offset);
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        Wgpu.wgpuRenderPassEncoderDrawIndexedIndirect(_handle, ((WebGpuBuffer)arguments).Handle, offset);
    }

    /// <inheritdoc/>
    public void BeginOcclusionQuery(int queryIndex)
    {
        ThrowIfEnded();
        Wgpu.wgpuRenderPassEncoderBeginOcclusionQuery(_handle, (uint)queryIndex);
    }

    /// <inheritdoc/>
    public void EndOcclusionQuery()
    {
        ThrowIfEnded();
        Wgpu.wgpuRenderPassEncoderEndOcclusionQuery(_handle);
    }

    /// <inheritdoc/>
    public void PushDebugGroup(string label)
    {
        ThrowIfEnded();
        using WebGpuString name = new(label);
        Wgpu.wgpuRenderPassEncoderPushDebugGroup(_handle, name.View);
    }

    /// <inheritdoc/>
    public void PopDebugGroup()
    {
        ThrowIfEnded();
        Wgpu.wgpuRenderPassEncoderPopDebugGroup(_handle);
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfEnded();
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
            Wgpu.wgpuRenderPassEncoderEnd(_handle);
            _ended = true;
        }

        if (_handle.IsValid)
        {
            Wgpu.wgpuRenderPassEncoderRelease(_handle);
            _handle = default;
        }
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
