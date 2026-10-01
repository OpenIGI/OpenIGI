namespace OpenIGI.Engine.Flow;

/// <summary>
/// The four things the frame arbiter tells the renderer, and the whole of what the engine kernel knows
/// about rendering.
/// </summary>
/// <remarks>
/// Keeping it to these four is what lets M4 run headless and keeps the renderer swappable: the arbiter
/// never names a device, a surface or a backend. The calls correspond to <c>Render_Flush</c>
/// (<c>0x4E7BB0</c>), <c>Render_Reset</c> (<c>0x4E7B80</c>), <c>Render_SetFirstFrame</c>
/// (<c>0x4E7340</c>) and <c>Render_SetTickRate</c> (<c>0x4E6030</c>).
/// </remarks>
public interface IRenderScheduler
{
    /// <summary>Submits whatever the previous pass queued.</summary>
    void Flush();

    /// <summary>Clears the queue and its counters, ready for the next pass.</summary>
    void Reset();

    /// <summary>Tells the renderer whether the next pass is one of the opening frames.</summary>
    /// <param name="isFirstFrame">Whether this is a first frame.</param>
    /// <remarks>The original sets this on the first two ticks after a tick-rate change and clears it after.</remarks>
    void SetFirstFrame(bool isFirstFrame);

    /// <summary>Tells the renderer the simulation rate changed.</summary>
    /// <param name="tickRate">The new rate in hertz.</param>
    /// <remarks>
    /// Paired with every <see cref="OpenIGI.Engine.Time.GameClock.SetTickRate"/>. The clock itself does
    /// not make this call — it knows nothing about rendering — so the flow task makes both.
    /// </remarks>
    void SetTickRate(int tickRate);
}
