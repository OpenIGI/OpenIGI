namespace OpenIGI.Rendering;

/// <summary>What the resolve is told beyond the settings themselves.</summary>
/// <param name="AdaptedExposure">What the frame's own metering settled on, multiplying the setting.</param>
/// <param name="Aspect">The render height over its width, which the vignette is shaped by.</param>
/// <param name="BloomRan">Whether there is a bloom image worth compositing.</param>
/// <param name="GrainPhase">Where in the film's hash this frame sits.</param>
/// <param name="GradeSize">The authored grade cube's edge, or nought for no grade.</param>
/// <param name="PresentWidth">The width the frame is presented at, which the lens steps by.</param>
/// <param name="PresentHeight">Its height.</param>
/// <param name="MaskView">
/// Whether what is being resolved is a channel being read rather than a picture being looked at. A mask
/// view keeps neither the curve, the film, the grade nor the lens — every one of those assumes radiance,
/// and what a mask holds is a code value.
/// </param>
public readonly record struct ResolveFrameState(
    float AdaptedExposure = 1f,
    float Aspect = 1f,
    bool BloomRan = false,
    float GrainPhase = 0f,
    float GradeSize = 0f,
    int PresentWidth = 1,
    int PresentHeight = 1,
    bool MaskView = false);

/// <summary>The eighty bytes the resolve is handed: the curve, the film, the grade and the lens.</summary>
/// <remarks>
/// Lifted out of the backends for the reason the other blocks were: the layout belongs to the shader,
/// and both paths fill it. Nearly every field here is switched off by a mask view, which is the one
/// rule in the block worth stating once rather than repeating at each slot.
/// </remarks>
public static class ResolvePushBlock
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = 20;

    /// <summary>How far the grain's phase steps each frame.</summary>
    /// <remarks>
    /// The golden ratio, so successive frames land far apart in the hash rather than walking through it
    /// — which is what stops the grain crawling in one direction.
    /// </remarks>
    public const float GrainStep = 0.61803399f;

    /// <summary>How many frames the grain's phase counts before it wraps.</summary>
    /// <remarks>
    /// Short, so the counter cannot drift into a magnitude where a float has no fractional bits left to
    /// give the shader.
    /// </remarks>
    public const int GrainCycle = 1024;

    /// <summary>Writes the block.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="post">What the frame asked for.</param>
    /// <param name="state">What the frame arrived at.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void Write(Span<float> block, in PostProcessSettings post, in ResolveFrameState state)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The resolve block is {Floats} floats, not {block.Length}.", nameof(block));
        }

        block.Clear();

        bool mask = state.MaskView;

        block[0] = post.Exposure * state.AdaptedExposure;
        block[1] = (int)post.ToneMap;
        block[2] = post.WhitePoint;
        block[3] = mask ? 1f : 0f;

        block[4] = mask ? 0f : post.Vignette;
        block[5] = state.Aspect;
        block[6] = state.BloomRan ? 1f : 0f;

        block[8] = mask ? 0f : Math.Clamp(post.Grain, 0f, 1f);
        block[9] = state.GrainPhase;

        block[10] = state.GradeSize > 0f && !mask ? Math.Clamp(post.ColourLookup, 0f, 1f) : 0f;
        block[11] = mask ? 0f : state.GradeSize;

        block[12] = mask ? 0f : MathF.Max(post.ChromaticAberration, 0f);
        block[13] = mask ? 0f : MathF.Max(post.Sharpen, 0f);
        block[14] = 1f / Math.Max(1, state.PresentWidth);
        block[15] = 1f / Math.Max(1, state.PresentHeight);

        block[16] = mask ? 1f : post.Contrast;
        block[17] = mask ? 1f : post.Saturation;
        block[18] = !mask && post.GreenPhosphor ? 1f : 0f;
        block[19] = mask ? 0f : Math.Clamp(post.NightShift, 0f, 1f);
    }
}
