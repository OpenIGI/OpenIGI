using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.RayTracing;

/// <summary>
/// The working memory acceleration-structure builds need, handed out a range at a time.
/// </summary>
/// <remarks>
/// <para>
/// A build wants somewhere to sort and partition while it runs, and it wants that memory only for the
/// duration of the build. Giving each structure its own allocation would mean an allocation per mesh
/// held for a frame; one buffer that a batch of builds carves up between them is a single allocation and
/// no bookkeeping, since every build in the batch finishes before the batch's barrier.
/// </para>
/// <para>
/// There is more than one of them, and which one a batch gets depends on what the GPU has finished. A
/// build reads and writes its scratch for as long as it runs, and a frame is recorded while the previous
/// one is still executing — so handing the next frame the same memory is handing it memory a build is
/// still using. That does not fail every time. It fails when the two overlap, which is why it looked
/// intermittent and why it never happened under the validation layers, which slow the recording side
/// enough that the previous frame is always done.
/// </para>
/// <para>
/// A render graph would get the same result by a different route: build scratch as a transient buffer
/// created inside the graph each frame, which will not hand back memory it knows is still in flight.
/// This is that rule, kept by hand — a buffer is only reused once the clock says the frame that
/// last used it has completed.
/// </para>
/// <para>
/// They grow and never shrink. The largest batch the scene ever builds is the size they settle at, which
/// happens in the first frames and then stops.
/// </para>
/// </remarks>
internal sealed class ScratchBuffer : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly ulong _alignment;

    // Every buffer ever allocated, with the frame that last used it. One of them is current.
    private readonly List<(IRhiBuffer Buffer, ulong LastUsed)> _pool = [];
    private IRhiBuffer? _buffer;
    private ulong _offset;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ScratchBuffer"/> class.</summary>
    /// <param name="device">The device to allocate on.</param>
    /// <param name="alignment">The alignment each handed-out range must start at.</param>
    public ScratchBuffer(IRhiDevice device, ulong alignment)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _alignment = Math.Max(1, alignment);
    }

    /// <summary>Gets how many bytes the buffer currently holds.</summary>
    public ulong Capacity => _buffer?.SizeInBytes ?? 0;

    /// <summary>
    /// Makes sure a buffer can serve a batch of the given total size, allocating one if none can, and
    /// starts handing ranges out from the beginning again.
    /// </summary>
    /// <param name="requiredBytes">The total the batch will ask for, alignment padding included.</param>
    /// <param name="frame">The frame this batch is recorded in.</param>
    /// <param name="completedFrame">The newest frame the GPU has finished.</param>
    public void BeginBatch(ulong requiredBytes, ulong frame, ulong completedFrame)
    {
        _offset = 0;

        // A buffer big enough whose last use the GPU has passed. Anything still in flight is left alone,
        // however much room it has.
        for (int i = 0; i < _pool.Count; i++)
        {
            (IRhiBuffer buffer, ulong lastUsed) = _pool[i];
            if (buffer.SizeInBytes >= requiredBytes && lastUsed <= completedFrame)
            {
                _pool[i] = (buffer, frame);
                _buffer = buffer;
                return;
            }
        }

        // Nothing free and large enough, so another one. Grown generously rather than exactly, so a batch
        // that creeps up by one mesh at a time does not allocate every frame.
        ulong largest = 0;
        foreach ((IRhiBuffer buffer, ulong _) in _pool)
        {
            largest = Math.Max(largest, buffer.SizeInBytes);
        }

        ulong size = Math.Max(requiredBytes, Math.Max(64 * 1024, largest * 2));
        IRhiBuffer fresh = _device.CreateBuffer(new RhiBufferDescriptor(
            Align(size, _alignment),
            RhiBufferUsage.AccelerationStructureScratch,
            "traced world scratch"));

        _pool.Add((fresh, frame));
        _buffer = fresh;
    }

    /// <summary>Hands out an aligned range and advances past it.</summary>
    /// <param name="bytes">How many bytes the build needs.</param>
    /// <returns>The buffer, and where in it the range starts.</returns>
    /// <exception cref="InvalidOperationException">
    /// The batch asked for more than <see cref="BeginBatch"/> was told it would.
    /// </exception>
    public (IRhiBuffer Buffer, ulong Offset) Allocate(ulong bytes)
    {
        if (_buffer is null)
        {
            throw new InvalidOperationException("BeginBatch must be called before a range is allocated.");
        }

        ulong start = Align(_offset, _alignment);
        if (start + bytes > _buffer.SizeInBytes)
        {
            throw new InvalidOperationException(
                $"A build asked for {bytes} scratch bytes at {start}, past the {_buffer.SizeInBytes}-byte buffer.");
        }

        _offset = start + bytes;
        return (_buffer, start);
    }

    /// <summary>Rounds a size or an offset up to an alignment.</summary>
    /// <param name="value">The value.</param>
    /// <param name="alignment">The alignment, which need not be a power of two.</param>
    /// <returns>The value rounded up.</returns>
    public static ulong Align(ulong value, ulong alignment) =>
        alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach ((IRhiBuffer buffer, ulong _) in _pool)
        {
            buffer.Dispose();
        }

        _pool.Clear();
        _buffer = null;
    }
}
