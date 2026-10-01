using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Compute;

/// <summary>
/// The uniform block one compute pass reads, written per frame.
/// </summary>
/// <remarks>
/// <para>
/// Every compute pass here takes its parameters as a constant buffer rather than as push data: the
/// blocks run to two hundred bytes and the guaranteed push room is a hundred and twenty-eight, so there
/// was never a choice.
/// </para>
/// <para>
/// One buffer per frame in flight, because a frame still being read cannot have its parameters
/// overwritten by the one being recorded — the same reason the geometry rings and the pose slots are
/// per frame. A single buffer would give the older frame the newer frame's camera, which reprojects
/// everything to the wrong place for exactly one frame and looks like a flicker.
/// </para>
/// </remarks>
public sealed class ComputeParameters : IDisposable
{
    private readonly IRhiDevice _device;
    private readonly IRhiBuffer[] _buffers;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ComputeParameters"/> class.</summary>
    /// <param name="device">The device the buffers live on.</param>
    /// <param name="sizeInBytes">How large the block is.</param>
    /// <param name="framesInFlight">How many buffers to keep.</param>
    /// <param name="label">A name for captures and diagnostics.</param>
    public ComputeParameters(IRhiDevice device, int sizeInBytes, int framesInFlight, string label)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeInBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);

        _device = device;
        _buffers = new IRhiBuffer[framesInFlight];

        for (int i = 0; i < framesInFlight; i++)
        {
            _buffers[i] = device.CreateBuffer(new RhiBufferDescriptor(
                (ulong)sizeInBytes,
                RhiBufferUsage.Uniform | RhiBufferUsage.CopyDestination,
                $"{label}.params{i}"));
        }
    }

    /// <summary>Gets the buffer one frame's parameters live in.</summary>
    /// <param name="frameInFlight">Which frame.</param>
    /// <returns>The buffer.</returns>
    public IRhiBuffer this[int frameInFlight] => _buffers[frameInFlight % _buffers.Length];

    /// <summary>Writes one frame's parameters.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="frameInFlight">Which frame.</param>
    /// <param name="data">The block.</param>
    /// <returns>The buffer it went into, ready to bind.</returns>
    public IRhiBuffer Write<T>(int frameInFlight, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IRhiBuffer buffer = this[frameInFlight];

        if (MemoryMarshal.AsBytes(data).Length > (int)buffer.SizeInBytes)
        {
            throw new ArgumentException(
                $"The block is larger than the {buffer.SizeInBytes} bytes '{buffer.Label}' was made for.",
                nameof(data));
        }

        _device.WriteBuffer(buffer, 0, data);
        return buffer;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IRhiBuffer buffer in _buffers)
        {
            buffer.Dispose();
        }
    }
}
