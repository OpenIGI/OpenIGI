// White noise per pixel per frame: what a pass hands DLSS ray reconstruction when it is the denoiser.
//
// The engine's own passes draw their random numbers from low-discrepancy sequences that move a little each
// frame -- interleaved gradient noise walked by the R2 step, a pixel's fixed hash advanced by the golden
// ratio -- because their own temporal filters then converge fastest. Ray reconstruction is trained on the
// opposite: independent samples, uncorrelated from pixel to pixel and from frame to frame. Handed a
// correlated pattern, it reads the pattern as structure in the scene and keeps it.
//
// The hash is PCG3D (Jarzynski and Olano, "Hash Functions for GPU Rendering", JCGT 2020): three 32-bit
// words in, three well-mixed words out, which is enough to key a pixel, a frame and a salt that keeps two
// uses in one pass independent of each other.

#ifndef OPENIGI_PIXEL_NOISE_INCLUDED
#define OPENIGI_PIXEL_NOISE_INCLUDED

uint3 Pcg3d(uint3 v)
{
    v = (v * 1664525u) + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

/// Three independent uniforms in [0, 1) for a pixel, a frame and a salt.
float3 WhiteNoise3(int2 px, uint frame, uint salt)
{
    uint3 h = Pcg3d(uint3(uint2(px), frame ^ (salt * 0x9E3779B9u)));
    return float3(h >> 8u) * (1.0 / 16777216.0);
}

#endif
