namespace OpenIGI.Rendering;

/// <summary>
/// How this frame is blended into the ones before it.
/// </summary>
/// <remarks>
/// <para>
/// The scene is sampled at a different sub-pixel offset each frame (see <see cref="TemporalJitter"/>)
/// and every frame is mixed into an accumulated history, so a pixel ends up holding an average over
/// many samples of its own area. That is supersampling paid for in time instead of in bandwidth, and it
/// is the only anti-aliasing here that does anything about the edges a deferred renderer cannot
/// multisample cheaply — the lighting discontinuities, the specular glints, the fog's dither.
/// </para>
/// <para>
/// The whole difficulty is that the history is only valid where the previous frame drew the same
/// surface. Where it did not — behind something that moved, or outside the previous frame's view — the
/// history is another surface entirely, and blending it in is a smear that follows the camera around.
/// The backend therefore reprojects each pixel into the previous frame and constrains what it finds
/// there to the range of colours around the pixel now; a history outside that range is a disocclusion
/// rather than a sample, and is discarded.
/// </para>
/// </remarks>
/// <param name="Enabled">Whether to accumulate at all. When false the frame is passed straight through.</param>
/// <param name="Feedback">
/// The share of the resolved pixel taken from the current frame, from 0 to 1. Low values converge to a
/// cleaner image and hold more samples, at the cost of responding to change more slowly; the usual
/// range is 0.05 to 0.15. One disables the accumulation without disabling the jitter, which is the
/// useful setting for seeing what the jitter alone is doing.
/// </param>
/// <param name="JitterX">This frame's horizontal sub-pixel offset, in pixels.</param>
/// <param name="JitterY">This frame's vertical sub-pixel offset, in pixels.</param>
/// <param name="MotionSharpening">
/// How much of the history's own blur is subtracted back out, from 0 to 1. Resampling the history with
/// bilinear filtering every frame is a low-pass filter applied over and over, and a scene that is
/// merely panning slowly can go visibly soft; sharpening the resampled result counteracts exactly that.
/// Too much of it rings around high-contrast edges. The backend scales this by <paramref name="Feedback"/>
/// before applying it, because the sharpening lands on the history and the history feeds back into
/// itself — so without the scaling the same number would mean a gentle touch at a high feedback and a
/// tenfold overshoot at a low one.
/// </param>
public readonly record struct TemporalAaSettings(
    bool Enabled = false,
    float Feedback = 0.1f,
    float JitterX = 0f,
    float JitterY = 0f,
    float MotionSharpening = 0.25f)
{
    /// <summary>Gets settings with no accumulation and no jitter.</summary>
    public static TemporalAaSettings Off { get; }

    /// <summary>Gets a value indicating whether the pass would do anything.</summary>
    public bool Accumulates => Enabled && Feedback < 1f;
}
