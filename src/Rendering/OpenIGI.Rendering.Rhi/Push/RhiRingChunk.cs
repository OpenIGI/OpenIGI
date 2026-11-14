namespace OpenIGI.Rendering.Rhi.Push;

/// <summary>One buffer of a push ring, its bind group, and the host copy pushes are written into first.</summary>
/// <remarks>
/// The written range is tracked as one span, from the lowest byte written to the highest, so a flush is one queue
/// write however many pushes landed in between.
/// </remarks>
internal sealed class RhiRingChunk(IRhiBuffer buffer, IRhiBindGroup group, int bytes) : IDisposable
{
    private readonly byte[] _shadow = new byte[bytes];
    private int _start = int.MaxValue;
    private int _end;

    /// <summary>Gets the chunk's buffer.</summary>
    public IRhiBuffer Buffer { get; } = buffer;

    /// <summary>Gets the group the chunk is read through.</summary>
    public IRhiBindGroup Group { get; } = group;

    /// <summary>Rounds a value up to a multiple of an alignment.</summary>
    public static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    /// <summary>Writes data at an offset, zero-filling the rest of a span of the given length.</summary>
    public void Put(ReadOnlySpan<byte> data, int offset, int length)
    {
        Span<byte> target = _shadow.AsSpan(offset, length);
        data.CopyTo(target);
        target[data.Length..].Clear();

        _start = Math.Min(_start, offset);
        _end = Math.Max(_end, offset + length);
    }

    /// <summary>Uploads what has been written since the last flush, in one queue write.</summary>
    public void Flush(IRhiDevice device)
    {
        if (_start >= _end)
        {
            return;
        }

        device.WriteBuffer(Buffer, (ulong)_start, _shadow.AsSpan(_start, _end - _start));
        _start = int.MaxValue;
        _end = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Group.Dispose();
        Buffer.Dispose();
    }
}
