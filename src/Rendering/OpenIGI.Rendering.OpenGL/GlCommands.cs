using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>What one recorded command does.</summary>
internal enum GlOp : byte
{
    BeginRenderPass,
    EndRenderPass,
    BeginComputePass,
    EndComputePass,
    SetRenderPipeline,
    SetComputePipeline,
    SetBindGroup,
    SetVertexBuffer,
    SetIndexBuffer,
    SetViewport,
    SetScissor,
    SetBlendConstant,
    Draw,
    DrawIndexed,
    DrawIndirect,
    DrawIndexedIndirect,
    Dispatch,
    DispatchIndirect,
    BeginOcclusion,
    EndOcclusion,
    CopyBufferToBuffer,
    CopyBufferToTexture,
    CopyTextureToBuffer,
    CopyTextureToTexture,
    ClearBuffer,
    ResolveQuerySet,
    PushDebugGroup,
    PopDebugGroup,
}

/// <summary>One recorded command: its operation, the objects it names and its numbers.</summary>
/// <remarks>
/// One shape for every command, so a frame's thousands of draws are a list of values rather than of allocations.
/// Which field means what is the operation's to say; <see cref="GlExecutor"/> reads them back in the same terms
/// <see cref="GlCommandEncoder"/> wrote them.
/// </remarks>
internal struct GlCommand
{
    public GlOp Op;
    public object? A;
    public object? B;
    public long I0;
    public long I1;
    public long I2;
    public long I3;
    public long I4;
    public float F0;
    public float F1;
    public float F2;
    public float F3;
    public float F4;
    public float F5;
}

/// <summary>A copy between a buffer and a texture, or two textures, as recorded.</summary>
/// <param name="Buffer">The buffer side, for a copy that has one.</param>
/// <param name="Source">The texture read, for a copy from a texture.</param>
/// <param name="Destination">The texture written, for a copy into one.</param>
/// <param name="Size">How much.</param>
internal sealed record GlTextureCopy(
    RhiBufferTextureCopy? Buffer,
    RhiTextureRegion? Source,
    RhiTextureRegion? Destination,
    RhiExtent3D Size);

/// <summary>The commands one encoder recorded, in order.</summary>
internal sealed class GlCommandList
{
    private GlCommand[] _items = new GlCommand[64];

    /// <summary>Gets how many commands there are.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the commands, of which the first <see cref="Count"/> are recorded.</summary>
    public ReadOnlySpan<GlCommand> Items => _items.AsSpan(0, Count);

    /// <summary>Empties the list for reuse, keeping its capacity and letting go of what its commands referred to.</summary>
    public void Reset()
    {
        Array.Clear(_items, 0, Count);
        Count = 0;
    }

    /// <summary>Appends a command.</summary>
    /// <param name="command">The command.</param>
    public void Add(in GlCommand command)
    {
        if (Count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        _items[Count++] = command;
    }
}
