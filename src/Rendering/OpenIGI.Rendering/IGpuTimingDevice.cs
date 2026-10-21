namespace OpenIGI.Rendering;

/// <summary>What the GPU's work cost: per stage, per labelled pass, and the whole frame measured end to end.</summary>
/// <remarks>Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IGpuTimingDevice
{
    /// <summary>Turns per-stage GPU timing on or off.</summary>
    /// <param name="enabled">Whether the backend should time each <see cref="RenderStage"/>.</param>
    /// <remarks>
    /// <para>
    /// Off by default and worth keeping that way when nobody is reading it. A timer query is a
    /// synchronisation point: the driver is free to reorder and batch work between draw calls, and
    /// asking when a stage began and ended narrows what it may do. The measurement is close enough to
    /// judge a budget by and is not free.
    /// </para>
    /// <para>
    /// Switching it off discards the history, so the peaks reported after switching it back on belong
    /// to the run being watched rather than to some earlier camera angle.
    /// </para>
    /// </remarks>
    void SetStageTiming(bool enabled);

    /// <summary>Begins a GPU timing range owned by the desktop compositor.</summary>
    /// <remarks>Ranges must not overlap backend-owned rendering ranges.</remarks>
    void BeginStageTiming(RenderStage stage);

    /// <summary>Ends the desktop-owned GPU timing range.</summary>
    void EndStageTiming();

    /// <summary>Gets what each stage of the frame last cost the GPU.</summary>
    /// <returns>
    /// One entry per stage that ran, in frame order; empty when timing is off or when no result has
    /// come back yet.
    /// </returns>
    /// <remarks>
    /// The results lag the frame being drawn by a frame or two, deliberately. A query's result is not
    /// ready when its stage ends, and waiting for it would stall the very pipeline being measured — so
    /// the answers are collected whenever the driver has them and reported a little late instead.
    /// </remarks>
    IReadOnlyList<RenderStageTiming> StageTimings();

    /// <summary>Gets the rolling distribution of total timed GPU work per completed frame.</summary>
    GpuFrameTiming GpuTiming();

    /// <summary>Gets every labelled pass timed since timing began, the most expensive first.</summary>
    /// <returns>The breakdown within the stages; empty where nothing labels its passes.</returns>
    IReadOnlyList<RenderPassTiming> PassTimings() => [];

    /// <summary>Names the compute work recorded from here on in the timings, or stops naming it.</summary>
    /// <param name="label">The name, or <see langword="null"/> to go back to each kernel's own.</param>
    /// <remarks>
    /// One kernel can serve several callers -- the surface cache's relight, bounce and far refresh are one
    /// program -- and timed under the kernel's name they are one figure that cannot say which of them
    /// spiked. Costs nothing when nothing is being timed.
    /// </remarks>
    void SetComputeTimingLabel(string? label)
    {
        _ = label;
    }

    /// <summary>Gets what each labelled pass cost in the slowest share of the frames timed.</summary>
    /// <param name="fraction">The share: 0.01 is the frames behind the 1% low.</param>
    /// <param name="frames">How many frames that was.</param>
    /// <param name="averageMilliseconds">Their average summed GPU time.</param>
    /// <returns>The breakdown, the most expensive first; empty where nothing labels its passes.</returns>
    IReadOnlyList<RenderPassTiming> WorstFramePassTimings(double fraction, out int frames, out double averageMilliseconds)
    {
        frames = 0;
        averageMilliseconds = 0.0;
        return [];
    }

    /// <summary>Gets the whole GPU frame measured end to end, rather than summed from its stages.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="GpuTiming"/> adds up the stages, so it can only report work somebody remembered to
    /// bracket. A pass recorded outside every stage scope contributes to nothing and costs real time, and
    /// the totals stay quietly wrong -- which is how a millisecond of lamp-shadow tracing read as free
    /// here for an entire session. This is a single timestamp pair around the frame's whole command
    /// buffer, so the difference between the two is precisely the work no stage claims.
    /// </para>
    /// <para>
    /// Zero on a backend that does not measure it, which is how a caller should tell "no untimed work"
    /// (a small difference) from "not measured" (zero).
    /// </para>
    /// </remarks>
    GpuFrameTiming GpuMeasuredTiming();
}
