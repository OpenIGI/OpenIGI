using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Where an indirect draw under a per-instance push pipeline finds its block, and what the executor keeps.</summary>
/// <remarks>
/// A direct draw names its block by its first instance. An indirect draw's first instance sits in the arguments buffer,
/// nought for every draw the renderer batches, so the push array is bound from the block on instead: the instance the
/// shader counts from then lands on it. That is what lets a pass's thousands of ranges go to the driver as one
/// multi-draw, as they do on every other backend, rather than one draw and one round of binding each.
/// </remarks>
internal sealed partial class GlExecutor : IDisposable
{
    // Blocks copied aside for a driver whose storage offsets cannot land on a block; reused in turn, which OpenGL keeps
    // ordered, since every write to them is the GPU's own copy.
    private const int ScratchSlots = 64;

    private uint _pushScratch;
    private int _scratchStride;
    private int _scratchNext;

    /// <summary>Frees what the executor made for itself.</summary>
    public void Dispose()
    {
        if (_readFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_readFramebuffer);
            _readFramebuffer = 0;
        }

        DeleteScratch();
    }

    /// <summary>Binds the push array of a per-instance pipeline from one block on, for an indirect draw.</summary>
    /// <param name="pipeline">The pipeline drawing.</param>
    /// <param name="slot">The block's slot in its chunk, or below nought for a pipeline with an ordinary push block.</param>
    /// <param name="stride">The bytes from one block to the next.</param>
    private void BindIndirectPush(GlRenderPipeline pipeline, long slot, int stride)
    {
        int group = device.PushGroup;
        if (slot < 0 || group >= pipeline.Plan.Groups.Length || _groups[group] is not { } bound)
        {
            return;
        }

        foreach (GlUnit unit in pipeline.Plan.Groups[group])
        {
            if (unit.Kind != GlUnitKind.StorageBuffer
                || (uint)unit.Binding >= (uint)bound.Entries.Length
                || bound.Entries[unit.Binding] is not { Buffer: { } buffer } entry)
            {
                continue;
            }

            long offset = (long)entry.Offset + (slot * stride);
            if (offset % device.Caps.StorageOffsetAlignment == 0)
            {
                _state.BindStorage(unit.Unit, buffer.Name, offset, stride);
            }
            else
            {
                long copied = CopyAside(buffer.Name, offset, stride);
                _state.BindStorage(unit.Unit, _pushScratch, copied, stride);
            }
        }

        // The next draw to apply groups binds the whole chunk again.
        _dirty[group] = true;
    }

    // Copies a block to an offset the driver will bind storage at, and says where.
    private unsafe long CopyAside(uint source, long offset, int stride)
    {
        int alignment = device.Caps.StorageOffsetAlignment;
        int aligned = (stride + alignment - 1) / alignment * alignment;

        if (_pushScratch == 0 || _scratchStride != aligned)
        {
            DeleteScratch();
            _pushScratch = _gl.GenBuffer();
            _scratchStride = aligned;
            _gl.BindBuffer(GLEnum.CopyWriteBuffer, _pushScratch);
            _gl.BufferData(GLEnum.CopyWriteBuffer, (nuint)((long)aligned * ScratchSlots), null, GLEnum.DynamicCopy);
        }

        long target = (long)(_scratchNext++ % ScratchSlots) * aligned;
        _gl.BindBuffer(GLEnum.CopyReadBuffer, source);
        _gl.BindBuffer(GLEnum.CopyWriteBuffer, _pushScratch);
        _gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, (nint)offset, (nint)target, (nuint)stride);
        return target;
    }

    private void DeleteScratch()
    {
        if (_pushScratch == 0)
        {
            return;
        }

        _state.ForgetBuffer(_pushScratch);
        _gl.DeleteBuffer(_pushScratch);
        _pushScratch = 0;
    }
}
