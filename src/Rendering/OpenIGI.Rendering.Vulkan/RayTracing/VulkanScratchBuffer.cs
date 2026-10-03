using OpenIGI.Rendering.Vulkan.Memory;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

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
/// This is that rule, kept by hand — a buffer is only reused once the queue says the submission that
/// last used it has completed.
/// </para>
/// <para>
/// They grow and never shrink. The largest batch the scene ever builds is the size they settle at, which
/// happens in the first frames and then stops.
/// </para>
/// </remarks>
internal sealed class VulkanScratchBuffer : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly VulkanAllocator _allocator;
    private readonly ulong _alignment;

    // Every buffer ever allocated, with the submission that last used it. One of them is current.
    private readonly List<(VulkanBuffer Buffer, ulong LastUsed)> _pool = [];
    private VulkanBuffer? _buffer;
    private ulong _offset;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="VulkanScratchBuffer"/> class.</summary>
    /// <param name="vk">The core Vulkan API.</param>
    /// <param name="device">The device to allocate on.</param>
    /// <param name="allocator">The allocator backing the buffer.</param>
    /// <param name="alignment">The alignment each handed-out range must start at.</param>
    public VulkanScratchBuffer(Vk vk, VulkanDevice device, VulkanAllocator allocator, ulong alignment)
    {
        ArgumentNullException.ThrowIfNull(vk);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(allocator);
        _vk = vk;
        _device = device;
        _allocator = allocator;
        _alignment = Math.Max(1, alignment);
    }

    /// <summary>Gets how many bytes the buffer currently holds.</summary>
    public ulong Capacity => _buffer?.Size ?? 0;

    /// <summary>
    /// Makes sure the buffer can serve a batch of the given total size, reallocating if it cannot, and
    /// starts handing ranges out from the beginning again.
    /// </summary>
    /// <param name="requiredBytes">The total the batch will ask for, alignment padding included.</param>
    /// <param name="submissionValue">The queue value this batch will be signalled by.</param>
    /// <param name="completedValue">The queue value the GPU has already reached.</param>
    /// <returns>Always null; nothing is handed back for the caller to retire any more.</returns>
    public VulkanBuffer? BeginBatch(ulong requiredBytes, ulong submissionValue, ulong completedValue)
    {
        _offset = 0;

        // A buffer big enough whose last use the GPU has passed. Anything still in flight is left alone,
        // however much room it has.
        for (int i = 0; i < _pool.Count; i++)
        {
            (VulkanBuffer buffer, ulong lastUsed) = _pool[i];
            if (buffer.Size >= requiredBytes && lastUsed <= completedValue)
            {
                _pool[i] = (buffer, submissionValue);
                _buffer = buffer;
                return null;
            }
        }

        // Nothing free and large enough, so another one. Grown generously rather than exactly, so a batch
        // that creeps up by one mesh at a time does not allocate every frame.
        ulong largest = 0;
        foreach ((VulkanBuffer buffer, ulong _) in _pool)
        {
            largest = Math.Max(largest, buffer.Size);
        }

        ulong size = Math.Max(requiredBytes, Math.Max(64 * 1024, largest * 2));
        VulkanBuffer fresh = new(
            _vk,
            _device,
            _allocator,
            VulkanRayTracingApi.Align(size, _alignment),
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit,
            MemoryPropertyFlags.DeviceLocalBit);

        _pool.Add((fresh, submissionValue));
        _buffer = fresh;

        // Nothing is replaced any more: a buffer the GPU may still be reading stays in the pool until it
        // says otherwise, rather than being handed back for someone else to time.
        return null;
    }

    /// <summary>Hands out an aligned range and advances past it.</summary>
    /// <param name="bytes">How many bytes the build needs.</param>
    /// <returns>The device address of the range.</returns>
    /// <exception cref="InvalidOperationException">
    /// The batch asked for more than <see cref="BeginBatch"/> was told it would.
    /// </exception>
    public ulong Allocate(ulong bytes)
    {
        if (_buffer is null)
        {
            throw new InvalidOperationException("BeginBatch must be called before a range is allocated.");
        }

        ulong start = VulkanRayTracingApi.Align(_offset, _alignment);
        if (start + bytes > _buffer.Size)
        {
            throw new InvalidOperationException(
                $"A build asked for {bytes} scratch bytes at {start}, past the {_buffer.Size}-byte buffer.");
        }

        _offset = start + bytes;
        return _buffer.DeviceAddress + start;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach ((VulkanBuffer buffer, ulong _) in _pool)
        {
            buffer.Dispose();
        }

        _pool.Clear();
        _buffer = null;
    }
}
