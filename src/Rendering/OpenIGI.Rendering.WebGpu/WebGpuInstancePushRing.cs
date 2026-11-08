using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>Where a per-instance push landed: the group its chunk is read through, and its slot in that chunk.</summary>
/// <param name="Group">The chunk's group, bound at the push group index.</param>
/// <param name="Slot">The block's index in the chunk, which the draw passes as its first instance.</param>
public readonly record struct WebGpuInstancePush(IRhiBindGroup Group, uint Slot);

/// <summary>The frame's push blocks for pipelines that read them per instance, packed one after another.</summary>
/// <remarks>
/// <para>
/// The other half of <see cref="WebGpuPushRing"/>. That ring hands every draw a 256-byte slice of a uniform buffer
/// and binds it by dynamic offset, so every draw costs a bind group call -- eight thousand a frame on mission 3 in a
/// browser, each one a crossing into the page and a validation in the GPU process. A pipeline whose layout says
/// <see cref="RhiPipelineLayoutDescriptor.PushDataPerInstance"/> reads its block from an array by the instance
/// index instead, so its draws share one binding for the whole chunk and each says which block is its own with
/// its first instance. That is also what lets consecutive draws of one range become one instanced draw: their
/// blocks sit in consecutive slots.
/// </para>
/// <para>
/// Blocks are packed at their own size rather than at the uniform offset alignment, so the upload is half what
/// the uniform ring's is for the same draws. Like that ring it is written into host copies and uploaded once a
/// submission, grows a chunk at a time rather than refusing, and starts over when a frame has been presented.
/// </para>
/// </remarks>
public sealed class WebGpuInstancePushRing : IDisposable
{
    private readonly WebGpuRhiDevice _device;
    private readonly List<Chunk> _chunks = [];
    private readonly int _stride;
    private int _next;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGpuInstancePushRing"/> class.</summary>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="blockBytes">The size of the block the shaders reading this ring declare.</param>
    /// <param name="slots">How many blocks one chunk holds.</param>
    public WebGpuInstancePushRing(WebGpuRhiDevice device, int blockBytes, int slots = 16384)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slots);

        _device = device;

        // The array's stride in the shader: an element's size rounded to its alignment, sixteen for a block of
        // vectors and matrices. Not the library's largest block -- the slot is an array index, so a ring is
        // only ever read by blocks of its own size.
        _stride = Align(blockBytes, 16);
        Slots = slots;
        _chunks.Add(MakeChunk(0));
    }

    /// <summary>Gets how many bytes apart consecutive blocks sit.</summary>
    public int Stride => _stride;

    /// <summary>Gets how many blocks one chunk holds.</summary>
    public int Slots { get; }

    /// <summary>Gets how many chunks the ring has grown to.</summary>
    public int Chunks => _chunks.Count;

    /// <summary>Writes one block and says where it landed.</summary>
    /// <param name="data">The bytes, which must fit one stride.</param>
    /// <returns>The chunk's group and the block's slot in it.</returns>
    /// <exception cref="InvalidOperationException">The block is larger than a stride.</exception>
    public WebGpuInstancePush Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.Length > _stride)
        {
            throw new InvalidOperationException(
                $"A push of {data.Length} bytes does not fit the ring's {_stride}-byte stride.");
        }

        int slot = _next++;
        int index = slot / Slots;

        if (index == _chunks.Count)
        {
            _chunks.Add(MakeChunk(index));
        }

        Chunk chunk = _chunks[index];
        int inChunk = slot % Slots;
        int offset = inChunk * _stride;

        // Zero-filled past the data, for the same reason as the uniform ring: nothing may read a value nobody wrote.
        Span<byte> target = chunk.Shadow.AsSpan(offset, _stride);
        data.CopyTo(target);
        target[data.Length..].Clear();
        chunk.MarkWritten(offset, _stride);

        return new WebGpuInstancePush(chunk.Group, (uint)inChunk);
    }

    /// <summary>Uploads what has been written since the last flush, one queue write a chunk.</summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (Chunk chunk in _chunks)
        {
            if (chunk.TakeWritten() is (int start, int length) && length > 0)
            {
                _device.WriteBuffer(chunk.Buffer, (ulong)start, chunk.Shadow.AsSpan(start, length));
            }
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

        foreach (Chunk chunk in _chunks)
        {
            chunk.Group.Dispose();
            chunk.Buffer.Dispose();
        }

        _chunks.Clear();
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private Chunk MakeChunk(int index)
    {
        IRhiBuffer buffer = _device.CreateBuffer(new RhiBufferDescriptor(
            (ulong)(_stride * Slots),
            RhiBufferUsage.Storage | RhiBufferUsage.CopyDestination,
            index == 0 ? "push.instances" : $"push.instances.{index}"));

        return new Chunk(buffer, _device.CreateInstancePushGroup(buffer), new byte[_stride * Slots]);
    }

    /// <summary>One buffer of the ring, its group, and the host copy blocks are written into first.</summary>
    private sealed class Chunk(IRhiBuffer buffer, IRhiBindGroup group, byte[] shadow)
    {
        private int _start = int.MaxValue;
        private int _end;

        public IRhiBuffer Buffer { get; } = buffer;

        public IRhiBindGroup Group { get; } = group;

        public byte[] Shadow { get; } = shadow;

        public void MarkWritten(int offset, int length)
        {
            _start = Math.Min(_start, offset);
            _end = Math.Max(_end, offset + length);
        }

        public (int Start, int Length)? TakeWritten()
        {
            if (_start >= _end)
            {
                return null;
            }

            (int, int) written = (_start, _end - _start);
            _start = int.MaxValue;
            _end = 0;
            return written;
        }
    }
}
