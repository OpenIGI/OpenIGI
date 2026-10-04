namespace OpenIGI.Rendering;

/// <summary>What one labelled pass cost the GPU, averaged over the frames measured.</summary>
/// <param name="Label">The pass's label, which for a compute dispatch is its kernel's.</param>
/// <param name="Stage">The stage it is counted under.</param>
/// <param name="Milliseconds">Its average per frame, counting frames where it did not run as nought.</param>
/// <param name="PeakMilliseconds">The worst single frame seen.</param>
/// <param name="FramesRun">How many of the measured frames it ran in.</param>
/// <remarks>
/// A stage sums every pass under it, and one stage -- the global illumination -- is a dozen dispatches,
/// so a stage over budget does not say which of them to look at. This does.
/// </remarks>
public readonly record struct RenderPassTiming(
    string Label,
    RenderStage Stage,
    double Milliseconds,
    double PeakMilliseconds,
    int FramesRun);
