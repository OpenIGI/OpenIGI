using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Replays recorded commands into the context, on the owning thread.</summary>
/// <remarks>
/// <para>
/// Bind groups are applied when a draw or dispatch needs them, through the bound pipeline's plan, because the units
/// an entry lands on are the pipeline's to say: a group bound before its pipeline, or kept across a change of
/// pipeline, lands wherever the pipeline that draws with it reads it.
/// </para>
/// <para>
/// <b>Barriers.</b> OpenGL orders everything except what a shader writes through a storage buffer or image, which a
/// later read sees only after <c>glMemoryBarrier</c>. A pipeline that can write either marks the work it did, and the
/// next command that could read anything issues one barrier for all of it.
/// </para>
/// </remarks>
internal sealed partial class GlExecutor(GlRhiDevice device)
{
    private const int MaxGroups = 8;
    private const int MaxVertexBuffers = 16;

    private readonly GL _gl = device.Gl;
    private readonly GlState _state = device.State;
    private readonly GlBindGroup?[] _groups = new GlBindGroup?[MaxGroups];
    private readonly uint[][] _offsets = [.. Enumerable.Range(0, MaxGroups).Select(_ => new uint[4])];
    private readonly bool[] _dirty = new bool[MaxGroups];
    private readonly (GlBuffer? Buffer, long Offset)[] _vertexBuffers = new (GlBuffer?, long)[MaxVertexBuffers];

    private GlRenderPipeline? _render;
    private GlComputePipeline? _compute;
    private GlBuffer? _indexBuffer;
    private GLEnum _indexType = GLEnum.UnsignedInt;
    private int _indexSize = 4;
    private long _indexOffset;
    private bool _vertexStateDirty;
    private bool _pendingWrites;
    private bool _depthReadOnly;
    private int _passWidth;
    private int _passHeight;
    private uint _occlusionQuery;

    /// <summary>Replays one command buffer.</summary>
    /// <param name="commands">The commands.</param>
    public void Execute(GlCommandList commands)
    {
        foreach (ref readonly GlCommand command in commands.Items)
        {
            Execute(command);
        }
    }

    private void Execute(in GlCommand c)
    {
        switch (c.Op)
        {
            case GlOp.BeginRenderPass:
                BeginRenderPass((RhiRenderPassDescriptor)c.A!);
                break;
            case GlOp.EndRenderPass:
                EndRenderPass((RhiRenderPassDescriptor)c.A!);
                break;
            case GlOp.BeginComputePass:
                BeginComputePass(c);
                break;
            case GlOp.EndComputePass:
                EndComputePass(c);
                break;
            case GlOp.SetRenderPipeline:
                SetRenderPipeline((GlRenderPipeline)c.A!);
                break;
            case GlOp.SetComputePipeline:
                _compute = (GlComputePipeline)c.A!;
                _state.UseProgram(_compute.Program.Name);
                ClearUnsampledUnits(_compute.Plan);
                MarkAllGroupsDirty();
                break;
            case GlOp.SetBindGroup:
                SetBindGroup(c);
                break;
            case GlOp.SetVertexBuffer:
                _vertexBuffers[c.I0] = ((GlBuffer)c.A!, c.I1);
                _vertexStateDirty = true;
                break;
            case GlOp.SetIndexBuffer:
                _indexBuffer = (GlBuffer)c.A!;
                (_indexType, _indexSize) = GlTranslation.Index((RhiIndexFormat)c.I0);
                _indexOffset = c.I1;
                _vertexStateDirty = true;
                break;
            case GlOp.SetViewport:
                _gl.ViewportIndexed(0, c.F0, c.F1, c.F2, c.F3);
                _gl.DepthRange(c.F4, c.F5);
                break;
            case GlOp.SetScissor:
                _gl.Scissor((int)c.I0, (int)c.I1, (uint)c.I2, (uint)c.I3);
                break;
            case GlOp.SetBlendConstant:
                _gl.BlendColor(c.F0, c.F1, c.F2, c.F3);
                break;
            case GlOp.Draw:
            case GlOp.DrawIndexed:
            case GlOp.DrawIndirect:
            case GlOp.DrawIndexedIndirect:
                Draw(c);
                break;
            case GlOp.Dispatch:
            case GlOp.DispatchIndirect:
                Dispatch(c);
                break;
            case GlOp.BeginOcclusion:
                _occlusionQuery = BeginOcclusion((GlQuerySet?)c.A, (int)c.I0);
                break;
            case GlOp.EndOcclusion:
                if (_occlusionQuery != 0)
                {
                    _gl.EndQuery(GLEnum.SamplesPassed);
                    _occlusionQuery = 0;
                }

                break;
            case GlOp.PushDebugGroup:
                Mark((string?)c.A);
                break;
            case GlOp.PopDebugGroup:
                Unmark();
                break;
            default:
                ExecuteTransfer(c);
                break;
        }
    }

    private void BeginRenderPass(RhiRenderPassDescriptor descriptor)
    {
        Mark(descriptor.Label);
        Barrier();

        GlTextureView[] colors = [.. descriptor.ColorAttachments.Select(a => (GlTextureView)a.View)];
        GlTextureView? depth = descriptor.DepthAttachment is { } d ? (GlTextureView)d.View : null;
        device.Framebuffers.Bind(colors, depth);

        (_passWidth, _passHeight) = colors.Length > 0 ? colors[0].Size : depth?.Size ?? (1, 1);
        _depthReadOnly = descriptor.DepthAttachment?.DepthReadOnly ?? false;

        bool clears = descriptor.ColorAttachments.Any(a => a.Load == RhiLoadOp.Clear)
            || descriptor.DepthAttachment is { DepthLoad: RhiLoadOp.Clear };
        if (clears)
        {
            _state.OpenMasksForClear();
            for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
            {
                RhiColorAttachment attachment = descriptor.ColorAttachments[i];
                if (attachment.Load == RhiLoadOp.Clear)
                {
                    ClearColor(i, attachment);
                }
            }

            if (descriptor.DepthAttachment is { DepthLoad: RhiLoadOp.Clear } clear)
            {
                float value = clear.DepthClearValue;
                _gl.ClearBuffer(GLEnum.Depth, 0, in value);
            }
        }

        // Every pass starts with the whole target as its viewport and scissor, as the RHI's other backends do. The
        // scissor is switched on only after the clears, which are of the whole target whatever a pass scissors to.
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(0, 0, (uint)_passWidth, (uint)_passHeight);
        _gl.Viewport(0, 0, (uint)_passWidth, (uint)_passHeight);
        _gl.DepthRange(0, 1);

        if (descriptor.Timestamps is { } timestamps)
        {
            Timestamp((GlQuerySet)timestamps.QuerySet, timestamps.BeginIndex);
        }

        _render = null;
        MarkAllGroupsDirty();
    }

    private void ClearColor(int index, in RhiColorAttachment attachment)
    {
        RhiClearColor value = attachment.ClearValue;
        if (attachment.View.Format == RhiTextureFormat.R32Uint)
        {
            ReadOnlySpan<uint> bits = [(uint)value.R, (uint)value.G, (uint)value.B, (uint)value.A];
            _gl.ClearBuffer(GLEnum.Color, index, bits);
            return;
        }

        ReadOnlySpan<float> color = [value.R, value.G, value.B, value.A];
        _gl.ClearBuffer(GLEnum.Color, index, color);
    }

    private void EndRenderPass(RhiRenderPassDescriptor descriptor)
    {
        // Multisampled attachments resolve into their targets as the pass ends.
        for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
        {
            if (descriptor.ColorAttachments[i].ResolveTarget is GlTextureView resolve)
            {
                Resolve(descriptor, i, resolve);
            }
        }

        List<GLEnum> discarded = [];
        for (int i = 0; i < descriptor.ColorAttachments.Count; i++)
        {
            if (descriptor.ColorAttachments[i].Store == RhiStoreOp.Discard)
            {
                discarded.Add(GLEnum.ColorAttachment0 + i);
            }
        }

        if (descriptor.DepthAttachment is { DepthStore: RhiStoreOp.Discard } depth)
        {
            discarded.Add(depth.View.Format.HasStencil() ? GLEnum.DepthStencilAttachment : GLEnum.DepthAttachment);
        }

        if (discarded.Count > 0)
        {
            GlTextureView[] colors = [.. descriptor.ColorAttachments.Select(a => (GlTextureView)a.View)];
            device.Framebuffers.Bind(colors, (GlTextureView?)descriptor.DepthAttachment?.View);
            _gl.InvalidateFramebuffer(GLEnum.DrawFramebuffer, [.. discarded]);
        }

        if (descriptor.Timestamps is { } timestamps)
        {
            Timestamp((GlQuerySet)timestamps.QuerySet, timestamps.EndIndex);
        }

        _gl.Disable(EnableCap.ScissorTest);
        NoteWrites(_render?.Plan);
        _render = null;
        Unmark();
    }

    private void Resolve(RhiRenderPassDescriptor descriptor, int index, GlTextureView resolve)
    {
        GlTextureView[] colors = [.. descriptor.ColorAttachments.Select(a => (GlTextureView)a.View)];
        uint source = device.Framebuffers.Bind(colors, (GlTextureView?)descriptor.DepthAttachment?.View);
        uint target = device.Framebuffers.Single(resolve);

        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(GLEnum.ReadFramebuffer, source);
        _gl.ReadBuffer(GLEnum.ColorAttachment0 + index);
        _gl.BindFramebuffer(GLEnum.DrawFramebuffer, target);
        (int width, int height) = resolve.Size;
        _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, (uint)GLEnum.ColorBufferBit, GLEnum.Nearest);
        _gl.BindFramebuffer(GLEnum.ReadFramebuffer, 0);
    }

    private void BeginComputePass(in GlCommand c)
    {
        Mark(c.B as string);
        if (c.A is GlQuerySet set && c.I0 >= 0)
        {
            Timestamp(set, (int)c.I0);
        }

        _compute = null;
        MarkAllGroupsDirty();
    }

    private void EndComputePass(in GlCommand c)
    {
        if (c.A is GlQuerySet set && c.I0 >= 0)
        {
            Timestamp(set, (int)c.I0);
        }

        _compute = null;
        Unmark();
    }

    // A named group a frame debugger shows the pass's calls under; nothing unless the device is marking.
    private void Mark(string? label)
    {
        if (device.Marking)
        {
            _gl.PushDebugGroup(GLEnum.DebugSourceApplication, 0, uint.MaxValue, RhiPassMarkers.NameOf(label));
        }
    }

    private void Unmark()
    {
        if (device.Marking)
        {
            _gl.PopDebugGroup();
        }
    }

    private void SetRenderPipeline(GlRenderPipeline pipeline)
    {
        if (ReferenceEquals(pipeline, _render))
        {
            return;
        }

        NoteWrites(_render?.Plan);
        _render = pipeline;
        _state.UseProgram(pipeline.Program.Name);
        _state.ApplyRaster(pipeline.State, _depthReadOnly);

        if (_state.BindVertexArray(pipeline.VertexArray))
        {
            _vertexStateDirty = true;
        }

        ClearUnsampledUnits(pipeline.Plan);
        MarkAllGroupsDirty();
    }

    // A texture fetched without a sampler reads through whatever sampler object its unit last had, and a depth
    // comparison one left there by another pass makes the fetch undefined. Cleared for render and compute alike.
    private void ClearUnsampledUnits(GlBindingPlan plan)
    {
        foreach (int unit in plan.UnsampledUnits)
        {
            _state.BindSampler(unit, 0);
        }
    }

    private void SetBindGroup(in GlCommand c)
    {
        int index = (int)c.I0;
        if ((uint)index >= MaxGroups)
        {
            return;
        }

        _groups[index] = (GlBindGroup)c.A!;
        uint[] offsets = _offsets[index];
        int count = (int)c.I1;
        if (count > 0)
        {
            if (c.B is uint[] all)
            {
                if (all.Length > offsets.Length)
                {
                    _offsets[index] = offsets = new uint[all.Length];
                }

                all.CopyTo(offsets, 0);
            }
            else
            {
                offsets[0] = (uint)c.I2;
            }
        }

        _dirty[index] = true;
    }

    private void MarkAllGroupsDirty() => Array.Fill(_dirty, true);

    /// <summary>Binds whatever groups changed through the plan of the pipeline about to draw or dispatch.</summary>
    private void ApplyGroups(GlBindingPlan plan)
    {
        for (int g = 0; g < plan.Groups.Length && g < MaxGroups; g++)
        {
            if (!_dirty[g] || _groups[g] is not { } group)
            {
                continue;
            }

            _dirty[g] = false;
            uint[] offsets = _offsets[g];

            foreach (GlUnit unit in plan.Groups[g])
            {
                if ((uint)unit.Binding >= (uint)group.Entries.Length || group.Entries[unit.Binding] is not { } entry)
                {
                    continue;
                }

                long offset = (long)entry.Offset + (entry.DynamicIndex >= 0 ? offsets[entry.DynamicIndex] : 0);
                switch (unit.Kind)
                {
                    case GlUnitKind.UniformBuffer when entry.Buffer is { } buffer:
                        _state.BindUniform(unit.Unit, buffer.Name, offset, (long)entry.Size);
                        break;
                    case GlUnitKind.StorageBuffer when entry.Buffer is { } buffer:
                        _state.BindStorage(unit.Unit, buffer.Name, offset, (long)entry.Size);
                        break;
                    case GlUnitKind.Image when entry.View is { } view:
                        _state.BindImage(unit.Unit, view, entry.Access);
                        break;
                    case GlUnitKind.Texture when entry.View is { } view:
                        _state.BindTexture(unit.Unit, view.Target, view.Name);
                        break;
                    case GlUnitKind.Sampler when entry.Sampler is { } sampler:
                        _state.BindSampler(unit.Unit, sampler.Name);
                        break;
                }
            }
        }
    }

    private void ApplyVertexState(GlRenderPipeline pipeline)
    {
        if (!_vertexStateDirty)
        {
            return;
        }

        _vertexStateDirty = false;
        for (int slot = 0; slot < pipeline.Slots.Length; slot++)
        {
            (GlBuffer? buffer, long offset) = _vertexBuffers[slot];
            _state.BindVertexBuffer(slot, buffer?.Name ?? 0, offset, pipeline.Slots[slot].Stride);
        }

        // The element buffer is vertex array state, so it is bound again whenever the vertex array changes.
        _state.BindElementBuffer(_indexBuffer?.Name ?? 0);
    }

    private unsafe void Draw(in GlCommand c)
    {
        if (_render is not { } pipeline)
        {
            return;
        }

        Barrier();
        ApplyGroups(pipeline.Plan);
        ApplyVertexState(pipeline);
        GLEnum mode = pipeline.State.Topology;

        switch (c.Op)
        {
            case GlOp.Draw:
                SetDrawParameters(pipeline, (int)c.I2, (int)c.I3);
                _gl.DrawArraysInstancedBaseInstance(mode, (int)c.I2, (uint)c.I0, (uint)c.I1, (uint)c.I3);
                break;
            case GlOp.DrawIndexed:
                SetDrawParameters(pipeline, (int)c.I3, (int)c.I4);
                _gl.DrawElementsInstancedBaseVertexBaseInstance(
                    mode,
                    (uint)c.I0,
                    _indexType,
                    (void*)(_indexOffset + (c.I2 * _indexSize)),
                    (uint)c.I1,
                    (int)c.I3,
                    (uint)c.I4);
                break;
            case GlOp.DrawIndirect:
                SetDrawParameters(pipeline, 0, 0);
                BindIndirectPush(pipeline, c.I2, (int)c.I3);
                _gl.BindBuffer(GLEnum.DrawIndirectBuffer, ((GlBuffer)c.A!).Name);
                _gl.DrawArraysIndirect(mode, (void*)c.I0);
                break;
            default:
                SetDrawParameters(pipeline, 0, 0);
                BindIndirectPush(pipeline, c.I2, (int)c.I3);
                _gl.BindBuffer(GLEnum.DrawIndirectBuffer, ((GlBuffer)c.A!).Name);
                _gl.MultiDrawElementsIndirect(mode, _indexType, (void*)c.I0, (uint)c.I1, (uint)RhiDrawIndexedArguments.Size);
                break;
        }

        NoteWrites(pipeline.Plan);
    }

    // Where the driver has no gl_BaseVertexARB, the GLSL reads these uniforms instead. An indirect draw's come from
    // its arguments, which the host cannot see, so they are left at nought there -- the only case that differs.
    private void SetDrawParameters(GlRenderPipeline pipeline, int baseVertex, int baseInstance)
    {
        if (pipeline.Program.BaseVertex >= 0)
        {
            _gl.Uniform1(pipeline.Program.BaseVertex, baseVertex);
        }

        if (pipeline.Program.BaseInstance >= 0)
        {
            _gl.Uniform1(pipeline.Program.BaseInstance, baseInstance);
        }
    }

    private void Dispatch(in GlCommand c)
    {
        if (_compute is not { } pipeline)
        {
            return;
        }

        Barrier();
        ApplyGroups(pipeline.Plan);

        // Each dispatch is its own group under a frame debugger, which otherwise shows a pass's dispatches as one.
        Mark(pipeline.Label);
        if (c.Op == GlOp.Dispatch)
        {
            _gl.DispatchCompute((uint)c.I0, (uint)c.I1, (uint)c.I2);
        }
        else
        {
            _gl.BindBuffer(GLEnum.DispatchIndirectBuffer, ((GlBuffer)c.A!).Name);
            _gl.DispatchComputeIndirect((nint)c.I0);
        }

        Unmark();

        NoteWrites(pipeline.Plan);
    }

    // A pipeline that can write a storage buffer or image leaves writes a later read sees only after a barrier.
    private void NoteWrites(GlBindingPlan? plan)
    {
        if (plan is { Writes: true })
        {
            _pendingWrites = true;
        }
    }

    /// <summary>Makes every shader write so far visible to whatever reads next.</summary>
    private void Barrier()
    {
        if (_pendingWrites)
        {
            _pendingWrites = false;
            // GL_ALL_BARRIER_BITS is every bit set, which the enum holds as -1.
            _gl.MemoryBarrier(uint.MaxValue);
        }
    }

    /// <summary>Forgets everything bound, at the start of a submission, since another may have run in between.</summary>
    public void Reset()
    {
        Array.Clear(_groups);
        Array.Clear(_vertexBuffers);
        _render = null;
        _compute = null;
        _indexBuffer = null;
        _vertexStateDirty = true;
        MarkAllGroupsDirty();
    }
}
