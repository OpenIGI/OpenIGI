using OpenIGI.Rendering.Rhi;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Rhi;

/// <summary>
/// Records Vulkan draws, inside a pass begun from its attachments.
/// </summary>
/// <remarks>
/// Dynamic rendering, so there is no render pass object and no framebuffer to keep in step with what is
/// attached. What this still owes the interface is the layouts: an attachment has no General form, so
/// every one is transitioned in on begin and back out on end, which is where WebGPU pays nothing again.
/// </remarks>
internal sealed unsafe class VulkanRhiRenderPassEncoder : IRhiRenderPassEncoder
{
    private readonly VulkanRhiDevice _device;
    private readonly Vk _vk;
    private readonly CommandBuffer _command;
    private readonly List<VulkanRhiTexture> _attachments;
    private readonly VulkanRhiQuerySet? _occlusion;
    private readonly RhiTimestampWrites? _timestamps;

    // What is bound now, so a draw that asks for the same again costs nothing. A frame's draws mostly
    // share a pipeline and their buffers, and each bind is a call through the loader into the driver:
    // with every draw re-binding everything, recording the shadow cascades alone took a quarter of the
    // frame. Only what persists in a command buffer is remembered; push data changes with every draw.
    private Pipeline _boundPipeline;
    private readonly DescriptorSet[] _boundSets = new DescriptorSet[8];
    private readonly (Silk.NET.Vulkan.Buffer Buffer, ulong Offset)[] _boundVertices = new (Silk.NET.Vulkan.Buffer, ulong)[8];
    private (Silk.NET.Vulkan.Buffer Buffer, ulong Offset, IndexType Type) _boundIndices;
    private VulkanRhiPipelineLayout? _layout;
    private bool _ended;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanRhiRenderPassEncoder"/> class.</summary>
    /// <param name="device">The device recording it.</param>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="command">The command buffer.</param>
    /// <param name="attachments">The textures attached, to put back when the pass ends.</param>
    /// <param name="occlusion">The set an occlusion query writes into.</param>
    /// <param name="timestamps">The timestamps the pass writes; the end one is written as it ends.</param>
    public VulkanRhiRenderPassEncoder(
        VulkanRhiDevice device,
        Vk vk,
        CommandBuffer command,
        List<VulkanRhiTexture> attachments,
        VulkanRhiQuerySet? occlusion,
        RhiTimestampWrites? timestamps = null)
    {
        _timestamps = timestamps;
        _device = device;
        _vk = vk;
        _command = command;
        _attachments = attachments;
        _occlusion = occlusion;
    }

    /// <inheritdoc/>
    public void SetPipeline(IRhiRenderPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ThrowIfEnded();

        VulkanRhiRenderPipeline graphics = (VulkanRhiRenderPipeline)pipeline;
        VulkanRhiPipelineLayout layout = (VulkanRhiPipelineLayout)graphics.Layout;

        // A pipeline of another layout may disturb the sets already bound, so they are forgotten and bound
        // again by whatever asks next.
        if (!ReferenceEquals(layout, _layout))
        {
            Array.Clear(_boundSets);
        }

        _layout = layout;

        if (graphics.Handle.Handle == _boundPipeline.Handle)
        {
            return;
        }

        _boundPipeline = graphics.Handle;
        _vk.CmdBindPipeline(_command, PipelineBindPoint.Graphics, graphics.Handle);
    }

    /// <inheritdoc/>
    public void SetBindGroup(int index, IRhiBindGroup group, ReadOnlySpan<uint> dynamicOffsets = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ThrowIfEnded();

        // No transitions here, unlike a compute pass: a layout transition inside a render pass is not
        // allowed, so whatever a draw reads has to be in the right layout before the pass began. Every
        // texture rests in General, which is a layout a shader can read, so it already is.
        DescriptorSet set = ((VulkanRhiBindGroup)group).Set;

        bool remembered = dynamicOffsets.IsEmpty && (uint)index < (uint)_boundSets.Length;
        if (remembered && _boundSets[index].Handle == set.Handle)
        {
            return;
        }

        if ((uint)index < (uint)_boundSets.Length)
        {
            // Dynamic offsets change what the same set reads, so a set bound with them is never matched.
            _boundSets[index] = remembered ? set : default;
        }

        fixed (uint* offsets = dynamicOffsets)
        {
            _vk.CmdBindDescriptorSets(
                _command,
                PipelineBindPoint.Graphics,
                Layout().Handle,
                (uint)index,
                1,
                &set,
                (uint)dynamicOffsets.Length,
                dynamicOffsets.Length > 0 ? offsets : null);
        }
    }

    /// <inheritdoc/>
    public void SetPushData(ReadOnlySpan<byte> data, int offset = 0)
    {
        ThrowIfEnded();

        VulkanRhiPipelineLayout layout = Layout();
        fixed (byte* bytes = data)
        {
            _vk.CmdPushConstants(
                _command,
                layout.Handle,
                layout.PushDataVisibility,
                (uint)offset,
                (uint)data.Length,
                bytes);
        }
    }

    /// <inheritdoc/>
    public void SetVertexBuffer(int slot, IRhiBuffer buffer, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfEnded();

        Silk.NET.Vulkan.Buffer handle = ((VulkanRhiBuffer)buffer).Buffer.Handle;
        ulong at = offset;

        if ((uint)slot < (uint)_boundVertices.Length)
        {
            if (_boundVertices[slot].Buffer.Handle == handle.Handle && _boundVertices[slot].Offset == at)
            {
                return;
            }

            _boundVertices[slot] = (handle, at);
        }

        _vk.CmdBindVertexBuffers(_command, (uint)slot, 1, &handle, &at);
    }

    /// <inheritdoc/>
    public void SetIndexBuffer(IRhiBuffer buffer, RhiIndexFormat format, ulong offset = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ThrowIfEnded();

        Silk.NET.Vulkan.Buffer handle = ((VulkanRhiBuffer)buffer).Buffer.Handle;
        IndexType type = VulkanRhiTranslation.IndexFormat(format);

        if (_boundIndices.Buffer.Handle == handle.Handle && _boundIndices.Offset == offset && _boundIndices.Type == type)
        {
            return;
        }

        _boundIndices = (handle, offset, type);
        _vk.CmdBindIndexBuffer(_command, handle, offset, type);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The height is passed negative with the origin moved to the bottom, which flips the clip space Y
    /// to match what every other API does. Without it a frame drawn by the shared pipeline comes out
    /// upside down on this backend and only on this one.
    /// </remarks>
    public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f)
    {
        ThrowIfEnded();

        // Qualified: OpenIGI.Rendering has a Viewport of its own, and the one meant here is Vulkan's.
        Silk.NET.Vulkan.Viewport viewport = new()
        {
            X = x,
            Y = y + height,
            Width = width,
            Height = -height,
            MinDepth = minDepth,
            MaxDepth = maxDepth,
        };

        _vk.CmdSetViewport(_command, 0, 1, &viewport);
    }

    /// <inheritdoc/>
    public void SetScissor(int x, int y, int width, int height)
    {
        ThrowIfEnded();

        Rect2D scissor = new()
        {
            Offset = new Offset2D(x, y),
            Extent = new Extent2D((uint)width, (uint)height),
        };

        _vk.CmdSetScissor(_command, 0, 1, &scissor);
    }

    /// <inheritdoc/>
    public void SetBlendConstant(in RhiClearColor color)
    {
        ThrowIfEnded();

        float* constants = stackalloc float[4] { color.R, color.G, color.B, color.A };
        _vk.CmdSetBlendConstants(_command, constants);
    }

    /// <inheritdoc/>
    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0)
    {
        ThrowIfEnded();
        _vk.CmdDraw(_command, (uint)vertexCount, (uint)instanceCount, (uint)firstVertex, (uint)firstInstance);
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
        _vk.CmdDrawIndexed(
            _command, (uint)indexCount, (uint)instanceCount, (uint)firstIndex, baseVertex, (uint)firstInstance);
    }

    /// <inheritdoc/>
    public void DrawIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        _vk.CmdDrawIndirect(_command, ((VulkanRhiBuffer)arguments).Buffer.Handle, offset, 1, 0);
    }

    /// <inheritdoc/>
    public void DrawIndexedIndirect(IRhiBuffer arguments, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfEnded();

        _vk.CmdDrawIndexedIndirect(_command, ((VulkanRhiBuffer)arguments).Buffer.Handle, offset, 1, 0);
    }

    /// <inheritdoc/>
    public void BeginOcclusionQuery(int queryIndex)
    {
        ThrowIfEnded();

        VulkanRhiQuerySet set = _occlusion
            ?? throw new GraphicsDeviceException("This pass was begun with no occlusion query set.");

        _vk.CmdBeginQuery(_command, set.Handle, (uint)queryIndex, QueryControlFlags.None);
        _activeQuery = queryIndex;
    }

    /// <inheritdoc/>
    public void EndOcclusionQuery()
    {
        ThrowIfEnded();

        VulkanRhiQuerySet set = _occlusion
            ?? throw new GraphicsDeviceException("This pass was begun with no occlusion query set.");

        // The query begun, not the pool's first: WebGPU's end names none, and Vulkan's has to name the
        // one that is active. Ending query 0 left every other query open to the end of the buffer.
        _vk.CmdEndQuery(_command, set.Handle, (uint)_activeQuery);
    }

    // The occlusion query begun and not yet ended; WebGPU allows one at a time, and so does this.
    private int _activeQuery;

    /// <inheritdoc/>
    public void PushDebugGroup(string label)
    {
    }

    /// <inheritdoc/>
    public void PopDebugGroup()
    {
    }

    /// <inheritdoc/>
    public void EndPass()
    {
        ThrowIfEnded();

        _vk.CmdEndRendering(_command);
        _ended = true;
        VulkanRhiTimestamps.End(_vk, _command, _timestamps);

        // Back to the layout everything rests in, so whatever reads the result next simply reads it.
        foreach (VulkanRhiTexture attachment in _attachments)
        {
            _device.Transition(_command, attachment, ImageLayout.General);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The pass is ended before it is marked disposed, not after. The other order looks harmless and is
    /// not: ending checks that the encoder is still usable, so a pass left to a `using` block would
    /// refuse to end itself and take the frame with it.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_ended)
        {
            EndPass();
        }

        _disposed = true;
    }

    private VulkanRhiPipelineLayout Layout() => _layout
        ?? throw new GraphicsDeviceException("No pipeline is bound, so there is no layout to bind against.");

    private void ThrowIfEnded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_ended)
        {
            throw new GraphicsDeviceException("The render pass has already ended.");
        }
    }
}
