using System.Runtime.InteropServices;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// Where a frame's indirect draw arguments are written: one host-visible buffer per frame in flight, filled
/// front to back as the frame records and read by the GPU straight from there.
/// </summary>
/// <remarks>
/// <para>
/// The terrain draws its ground as pages of ranges, a draw call for every visible chunk's layer: thousands of
/// draws a frame, each one a trip through the backend for what is the same mesh, the same material and the
/// same push block as the draw before it. With the ranges written here, a page is one indirect call that makes
/// all of them in order -- Vulkan's <c>multiDrawIndirect</c>, Direct3D 12's <c>ExecuteIndirect</c> with a count
/// -- which is the picture the separate draws made, for a fraction of the submission.
/// </para>
/// <para>
/// A slot is written only once the GPU has finished the frame that last used it: there is one more slot than
/// frames in flight. A slot that runs out of room mid-frame starts a larger buffer and keeps the full one alive
/// until the slot comes round again, because the draws already recorded this frame still read it.
/// </para>
/// </remarks>
internal sealed class IndirectDrawRing : IDisposable
{
    private const int InitialRecords = 8192;

    private readonly IRhiDevice _device;
    private readonly Slot[] _slots;
    private Slot _current;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="IndirectDrawRing"/> class.</summary>
    /// <param name="device">The device the buffers are made on.</param>
    /// <param name="framesInFlight">How many frames the device may have in flight at once.</param>
    public IndirectDrawRing(IRhiDevice device, int framesInFlight)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _slots = new Slot[Math.Max(1, framesInFlight) + 1];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new Slot();
        }

        _current = _slots[0];
    }

    /// <summary>Starts the slot a frame writes into.</summary>
    /// <param name="frame">The frame about to be recorded.</param>
    public void BeginFrame(ulong frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current = _slots[(int)(frame % (ulong)_slots.Length)];

        // Its last frame is finished: the buffers it outgrew then can go, and its own can be written again.
        foreach (IRhiBuffer retired in _current.Retired)
        {
            retired.Dispose();
        }

        _current.Retired.Clear();
        _current.Used = 0;
    }

    /// <summary>Writes a run of draws' arguments for this frame.</summary>
    /// <param name="draws">The arguments, in the order they are to be drawn.</param>
    /// <returns>The buffer and the byte offset the run starts at.</returns>
    public (IRhiBuffer Buffer, ulong Offset) Write(ReadOnlySpan<RhiDrawIndexedArguments> draws)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Slot slot = _current;
        if (slot.Buffer is null || slot.Used + draws.Length > slot.Capacity)
        {
            if (slot.Buffer is { } full)
            {
                slot.Retired.Add(full);
            }

            slot.Capacity = Math.Max(Math.Max(InitialRecords, slot.Capacity * 2), draws.Length);
            slot.Buffer = _device.CreateBuffer(new RhiBufferDescriptor(
                (ulong)slot.Capacity * RhiDrawIndexedArguments.Size,
                RhiBufferUsage.Indirect | RhiBufferUsage.MapWrite,
                "indirect draw arguments"));
            slot.Used = 0;
        }

        ulong offset = (ulong)slot.Used * RhiDrawIndexedArguments.Size;
        _device.WriteBuffer(slot.Buffer, offset, MemoryMarshal.AsBytes(draws));
        slot.Used += draws.Length;
        return (slot.Buffer, offset);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Slot slot in _slots)
        {
            slot.Buffer?.Dispose();
            foreach (IRhiBuffer retired in slot.Retired)
            {
                retired.Dispose();
            }

            slot.Retired.Clear();
        }
    }

    private sealed class Slot
    {
        public IRhiBuffer? Buffer { get; set; }

        public int Capacity { get; set; }

        public int Used { get; set; }

        public List<IRhiBuffer> Retired { get; } = [];
    }
}
