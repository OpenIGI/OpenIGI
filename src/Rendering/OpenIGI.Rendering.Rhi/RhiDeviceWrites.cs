using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Rhi;

/// <summary>The typed buffer write, over the device's byte write.</summary>
public static class RhiDeviceWrites
{
    /// <summary>Writes host data into a buffer.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="device">The device.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="offset">Where the write starts, in bytes.</param>
    /// <param name="data">The data.</param>
    public static void WriteBuffer<T>(this IRhiDevice device, IRhiBuffer buffer, ulong offset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(device);
        device.WriteBuffer(buffer, offset, MemoryMarshal.AsBytes(data));
    }
}
