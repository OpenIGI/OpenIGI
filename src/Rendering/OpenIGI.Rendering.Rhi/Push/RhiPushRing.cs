namespace OpenIGI.Rendering.Rhi.Push;

/// <summary>Where one push landed: the group to bind and the dynamic offset to bind it at.</summary>
/// <param name="Group">The bind group over the chunk of the ring the push was written to.</param>
/// <param name="Offset">Where in that chunk it landed.</param>
public readonly record struct RhiPushSlice(IRhiBindGroup Group, uint Offset);

/// <summary>
/// A ring of uniform memory that push data is written into and bound from, for a backend with no push constants.
/// </summary>
/// <remarks>
/// <para>
/// Uniform buffers written a slice at a time and bound by dynamic offset, because that is the only shape
/// WebGPU offers for what a push constant does, and the shape OpenGL binds a uniform range in too. A slice is
/// aligned to what the device requires of a dynamic uniform offset, which is 256 bytes on every adapter this
/// has met, so a block of 112 bytes still costs 256 -- the price of not having push constants.
/// </para>
/// <para>
/// The ring is written through the device's queue write rather than through an encoder, which is what makes it
/// correct: a queue write is ordered against the submissions around it, so everything written while a frame is
/// being recorded lands before that frame runs. It is also why the ring has to hold a whole frame's worth of
/// pushes -- nothing is reused until the frame that read it has finished.
/// </para>
/// <para>
/// <b>Written once per submission, not once per push.</b> A push lands in a host copy of its chunk and the device
/// flushes each chunk's written range in one queue write just before it submits (<see cref="Flush"/>). A queue
/// write a push, which is a draw, cost more than the draws themselves: mission 1 spent four in ten of its frame
/// there in a browser, thousands of calls a frame.
/// </para>
/// <para>
/// So it grows rather than refusing: a frame that needs more than it holds gets another chunk, its own buffer
/// behind its own bind group, and keeps it. A frame's worth is not a number anything can know in advance --
/// capturing the surface cache draws the level into every card in one go, thousands of pushes where an ordinary
/// frame makes hundreds -- and a Vulkan push constant has no such ceiling to hit.
/// </para>
/// </remarks>
public sealed class RhiPushRing : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly Func<IRhiBuffer, int, IRhiBindGroup> _makeGroup;
    private readonly List<RhiRingChunk> _chunks = [];
    private readonly int _slice;
    private int _next;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="RhiPushRing"/> class.</summary>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="makeGroup">
    /// Makes the bind group a chunk is read through: its one entry the chunk's buffer, a slice long, with a dynamic
    /// offset. The backend's, because only it knows the layout the push group is bound against.
    /// </param>
    /// <param name="largestBlock">The largest push block any module declares; see <see cref="RhiPushBlocks"/>.</param>
    /// <param name="slices">How many pushes one chunk holds.</param>
    /// <param name="alignment">What the device requires of a dynamic uniform offset.</param>
    public RhiPushRing(
        IRhiDevice device,
        Func<IRhiBuffer, int, IRhiBindGroup> makeGroup,
        int largestBlock,
        int slices = 4096,
        int alignment = 256)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(makeGroup);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);

        _device = device;
        _makeGroup = makeGroup;
        _slice = Math.Max(alignment, RhiRingChunk.Align(Math.Max(largestBlock, 1), alignment));

        Slices = slices;
        _chunks.Add(MakeChunk(0));
    }

    /// <summary>Gets the buffer of the ring's first chunk, where a frame's first pushes land.</summary>
    public IRhiBuffer Buffer => _chunks[0].Buffer;

    /// <summary>Gets how many bytes one slice occupies.</summary>
    public int SliceBytes => _slice;

    /// <summary>Gets how many slices one chunk holds.</summary>
    public int Slices { get; }

    /// <summary>Gets how many chunks the ring has grown to.</summary>
    public int Chunks => _chunks.Count;

    /// <summary>Writes one push block and says where it landed.</summary>
    /// <param name="data">The bytes, which must fit in one slice.</param>
    /// <returns>The group and dynamic offset to bind it at.</returns>
    /// <exception cref="InvalidOperationException">The block is larger than a slice.</exception>
    public RhiPushSlice Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.Length > _slice)
        {
            throw new InvalidOperationException(
                $"A push of {data.Length} bytes does not fit the ring's {_slice}-byte slice.");
        }

        int slice = _next++;
        int index = slice / Slices;

        if (index == _chunks.Count)
        {
            _chunks.Add(MakeChunk(index));
        }

        RhiRingChunk chunk = _chunks[index];
        int offset = (slice % Slices) * _slice;

        // Written a whole slice at a time, zero-filled past the data: a uniform binding is sized to the block, and
        // leaving the tail at whatever the last frame put there would hand a shader values nobody wrote.
        chunk.Put(data, offset, _slice);
        return new RhiPushSlice(chunk.Group, (uint)offset);
    }

    /// <summary>Uploads what has been pushed since the last flush, one queue write a chunk.</summary>
    /// <remarks>Called by the device before every submission, which is what orders it ahead of the draws.</remarks>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (RhiRingChunk chunk in _chunks)
        {
            chunk.Flush(_device);
        }
    }

    /// <summary>Starts the ring over, once the frame that read it has finished.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _next = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (RhiRingChunk chunk in _chunks)
        {
            chunk.Dispose();
        }

        _chunks.Clear();
    }

    private RhiRingChunk MakeChunk(int index)
    {
        IRhiBuffer buffer = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(_slice * Slices),
            RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
            index == 0 ? "push.ring" : $"push.ring.{index}"));

        return new RhiRingChunk(buffer, _makeGroup(buffer, _slice), _slice * Slices);
    }
}
