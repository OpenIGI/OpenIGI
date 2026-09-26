namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// Data on its way back from the GPU.
/// </summary>
/// <remarks>
/// <para>
/// A future rather than a value, because the two backends cannot both be blocked on. A desktop can wait
/// on a fence and does; a browser cannot block its one thread at all, and the value arrives on a later
/// turn of the event loop. Every readback in this renderer — the screenshot, the card-atlas dumps, the
/// sky irradiance, the occlusion results — goes through this.
/// </para>
/// <para>
/// The result is owned by the readback and released when it is disposed, so a caller copies out what it
/// means to keep.
/// </para>
/// </remarks>
public interface IRhiReadback : IDisposable
{
    /// <summary>Gets whether the data has arrived.</summary>
    bool IsComplete { get; }

    /// <summary>Gets how many bytes are being read back.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Waits until the data has arrived.</summary>
    /// <param name="timeout">How long to wait, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>.</param>
    /// <returns><see langword="true"/> when it arrived within the timeout.</returns>
    /// <remarks>
    /// Only where the platform allows it. A browser implementation returns whatever
    /// <see cref="IsComplete"/> already says rather than spinning, so a caller that must work there polls
    /// across frames instead of waiting.
    /// </remarks>
    bool Wait(TimeSpan timeout);

    /// <summary>Gets the data, once it has arrived.</summary>
    /// <returns>The bytes, valid until this readback is disposed.</returns>
    /// <exception cref="InvalidOperationException">The data has not arrived.</exception>
    ReadOnlySpan<byte> GetData();
}
