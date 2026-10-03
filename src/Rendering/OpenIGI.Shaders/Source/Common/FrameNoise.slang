// Interleaved gradient noise (Jimenez 2014), with a per-frame offset that walks the pattern so a pass
// that accumulates over time sees a fresh realisation each frame.
//
// The offset is an additive two-dimensional Weyl sequence on the reciprocals of the plastic number
// (the real root of x^3 = x + 1), the R2 low-discrepancy step, scaled to sixteen pixels a frame. That
// step decorrelates consecutive frames and stratifies the per-pixel values well across a 64-frame
// cycle; it only translates the pattern, so each frame's own spatial character is unchanged.

#ifndef OPENIGI_FRAME_NOISE_INCLUDED
#define OPENIGI_FRAME_NOISE_INCLUDED

// Sixteen pixels times (1/rho, 1/rho^2), rho the plastic number.
static const float2 kFrameNoiseStep = float2(12.0780426600, 9.1174446560);

// The published interleaved gradient noise: a value in [0, 1) whose neighbours differ as much as a
// two-dimensional pattern can make them.
float GradientNoise(float2 q)
{
    return frac(52.9829189 * frac((0.06711056 * q.x) + (0.00583715 * q.y)));
}

// The noise at a pixel for a small frame index (0 to 63, as a float).
float FrameNoise(float2 pos, float frame)
{
    return GradientNoise(pos + (frame * kFrameNoiseStep));
}

#endif
