namespace OpenIGI.Rendering;

/// <summary>
/// What one <see cref="RenderStage"/> cost the GPU, in milliseconds.
/// </summary>
/// <param name="Stage">The stage measured.</param>
/// <param name="Milliseconds">
/// Its smoothed GPU time. Smoothed rather than instantaneous because a single frame's query is far
/// noisier than the thing being judged: the driver batches, the clock boosts, and a stage costing a
/// steady 0.8 ms reports anywhere from 0.4 to 2 ms frame to frame. A budget is a claim about the
/// steady state, so the steady state is what is shown.
/// </param>
/// <param name="PeakMilliseconds">
/// The worst single frame seen since measuring began. The budget is what a stage costs; this is what
/// it costs when the camera turns to face the whole level, which is the frame that actually drops.
/// </param>
public readonly record struct RenderStageTiming(RenderStage Stage, double Milliseconds, double PeakMilliseconds);
