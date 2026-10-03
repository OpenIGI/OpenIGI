// The lamp-shadow filter: a single edge-aware disk blur over the accumulated visibility.
//
// The accumulation leaves two kinds of pixel that still need help. One sits in a penumbra, where the
// right answer really is in between and the remaining noise is the spread of a few dozen binary rays;
// blurring across the penumbra's own width costs nothing there, because the true shadow is that soft
// anyway. The other has too little history to be trusted -- just revealed, or just rejected -- and is
// still close to raw rays. Pixels that are neither are left exactly as they are, so a crisp contact
// shadow stays crisp.
//
// The blur only gathers from the same surface: a tap off the centre's tangent plane, or facing another
// way, counts for nothing, in the spirit of the edge-stopping functions of Dammertz et al. (2010) and
// Schied et al. (2017). Where the history is mature and the pixel is not in a penumbra, a tap whose
// value is far from the centre's also counts for little, so that a filter called in to remove a stray
// speck does not round off a real edge doing it.

#include "../Common/LampDenoiseCommon.slang"
#include "../Common/DepthFilter.slang"
#include "../Common/Unroll.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uAccumulated;
[[vk::binding(1, 0)]] SamplerState uAccumulatedSampler;
[[vk::binding(2, 0)]] DepthTexture2D uDepth;
[[vk::binding(3, 0)]] SamplerState uDepthSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uNormal;
[[vk::binding(5, 0)]] SamplerState uNormalSampler;
[[vk::binding(6, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uOutput;

struct Params
{
    // Rotation only: turns a view-space offset into a world-space one around the camera.
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height of the lamp images.
    float4 resolution;

    // xy this frame's jitter in lamp pixels, z the frame index, w above a half when filtering is on.
    float4 jitter;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

// The local statistics' Gaussian falloff: a tap's weight is 2^(-kStatsFalloff * squared pixel distance).
static const float kStatsFalloff = 2.0 / 9.0;

// Below this mean the relative spread is measured against this instead, so near-black pixels do not
// look infinitely noisy.
static const float kMinRelativeMean = 0.1;

// How far the filter may reach to cover a penumbra, in lamp pixels, and the least that counts as one.
static const float kMinPenumbraRadius = 1.0;
static const float kMaxPenumbraRadius = 24.0;
static const float kPenumbraRadiusThreshold = 1.5;

// A mean within this of either end is fully lit or fully dark, not a penumbra.
static const float kPenumbraMargin = 0.02;

// How noisy, relative to its mean, a pixel outside a penumbra must be before it is filtered: the
// threshold drops as the history thins out.
static const float kNoisyRelativeMature = 0.15;
static const float kNoisyRelativeFresh = 0.10;

// The filter's footprint outside a penumbra, in lamp pixels, and its tap counts.
static const float kNoisyRadius = 8.0;
static const int kPenumbraTaps = 16;
static const float kMatureTaps = 12.0;
static const float kFreshTaps = 24.0;
static const int kMaxTaps = 24;

// How sharply a tap off the centre's tangent plane is refused: 2^(-k d^2), d relative to view depth.
static const float kPlaneSharpness = 10000.0;

// Below this many frames of history the value test is skipped: the centre is too noisy to judge by.
static const float kValueTestFrames = 6.0;

// The value test's tolerance never falls below this, so a flat neighbourhood does not refuse every tap.
static const float kMinValueSigma = 0.001;

// The golden angle, pi (3 - sqrt 5): successive taps turned by it fill a disk evenly (Vogel 1979).
static const float kGoldenAngle = 2.39996323;
static const float kTwoPi = 6.28318531;

float2 JitteredDepthUv(int2 px)
{
    return ((float2(px) + 0.5) - p.jitter.xy) * p.resolution.zw;
}

float4 AccumulatedAt(int2 px)
{
    return uAccumulated.Load(int3(px, 0));
}

// The normal of the full-resolution texel a coordinate falls in. Read unfiltered: a blend of two faces'
// normals at a crease is a direction neither face has.
float3 NormalAt(float2 uv)
{
    uint width;
    uint height;
    uNormal.GetDimensions(width, height);
    int2 last = int2(int(width) - 1, int(height) - 1);
    int2 texel = clamp(int2(floor(uv * float2(float(width), float(height)))), int2(0, 0), last);
    float3 encoded = uNormal.Load(int3(texel, 0)).xyz;
    return normalize((encoded * 2.0) - 1.0);
}

// An integer avalanche mix (Wellons' "lowbias32"), used to scatter the disk's rotation between pixels
// and frames.
uint MixBits(uint x)
{
    x ^= x >> 16;
    x *= 0x7FEB352Du;
    x ^= x >> 15;
    x *= 0x846CA68Bu;
    x ^= x >> 16;
    return x;
}

float UnitFloat(uint bits)
{
    return float(bits >> 8) * (1.0 / 16777216.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    int2 size = int2(p.resolution.xy);
    if (px.x >= size.x || px.y >= size.y)
    {
        return;
    }

    float4 centre = AccumulatedAt(px);

    // No lamp here: unshadowed, and nothing for the shading to upsample against.
    if (centre.y <= 0.0)
    {
        uOutput.Store(px, float4(1.0, 0.0, 0.0, 0.0));
        return;
    }

    // The accumulation had no surface here; pass it on untouched.
    if (centre.z <= 0.0)
    {
        uOutput.Store(px, float4(centre.x, centre.y, 0.0, 0.0));
        return;
    }

    float2 depthUv = JitteredDepthUv(px);
    float depth = DepthBilinear(uDepth, uDepthSampler, depthUv);
    if (depth <= 0.0)
    {
        uOutput.Store(px, float4(centre.x, centre.y, 0.0, 0.0));
        return;
    }

    float3 viewPos = ViewPositionAt(depthUv, depth, p.unproject);
    float3 world = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz;
    float3 normal = NormalAt(depthUv);

    // One for a pixel whose history was just started, falling to nothing by its third frame.
    float disocclusion = 1.0 - saturate((centre.z - 1.0) * 0.5);

    // A small Gaussian-weighted neighbourhood: how bright, how noisy, and how wide a penumbra any lamp
    // here casts.
    float weightSum = 0.0;
    float visSum = 0.0;
    float visSquares = 0.0;
    float penumbraWidth = centre.y;
    int2 last = size - 1;

    OPENIGI_UNROLL
    for (int dy = -2; dy <= 2; dy++)
    {
        OPENIGI_UNROLL
        for (int dx = -2; dx <= 2; dx++)
        {
            float2 tap = AccumulatedAt(clamp(px + int2(dx, dy), int2(0, 0), last)).xy;
            penumbraWidth = max(penumbraWidth, tap.y);
            if (tap.y <= 0.0)
            {
                continue;
            }

            float weight = exp2(-kStatsFalloff * float((dx * dx) + (dy * dy)));
            weightSum += weight;
            visSum += tap.x * weight;
            visSquares += tap.x * tap.x * weight;
        }
    }

    float mean = visSum / max(weightSum, 1e-6);
    float sigma = sqrt(max((visSquares / max(weightSum, 1e-6)) - (mean * mean), 0.0));

    // A young history's spread understates its error: the frames it would need to show it are missing.
    sigma *= lerp(1.0, 2.0, disocclusion);

    float relative = sigma / max(mean, kMinRelativeMean);

    // The penumbra's width in metres, projected to lamp pixels at this depth; half of it either side.
    float pixelsPerMetre = (p.unproject.x * 0.5 * p.resolution.x) / max(viewPos.z, 1e-3);
    float penumbraRadius =
        clamp(0.5 * penumbraWidth * pixelsPerMetre, kMinPenumbraRadius, kMaxPenumbraRadius);

    bool inPenumbra = (mean > kPenumbraMargin) && (mean < (1.0 - kPenumbraMargin))
        && (penumbraRadius > kPenumbraRadiusThreshold);
    bool noisy = relative > lerp(kNoisyRelativeMature, kNoisyRelativeFresh, disocclusion);

    float2 result = centre.xy;

    if (p.jitter.w > 0.5 && (inPenumbra || noisy))
    {
        int tapCount = inPenumbra ? kPenumbraTaps : int(round(lerp(kMatureTaps, kFreshTaps, disocclusion)));
        float radius = inPenumbra ? penumbraRadius : kNoisyRadius;
        bool valueTest = !inPenumbra && centre.z >= kValueTestFrames;

        // The disk is turned by a value that differs between the four pixels of a 2x2 block and changes
        // every frame: neighbours sample different directions, and the frames the image is resolved
        // over see the pattern move rather than stand still.
        uint seed = uint(px.x & 1) | (uint(px.y & 1) << 1) | (uint(p.jitter.z) << 2);
        float rotation = UnitFloat(MixBits(seed)) * kTwoPi;

        float2 sum = centre.xy;
        float total = 1.0;

        for (int i = 0; i < kMaxTaps; i++)
        {
            if (i >= tapCount)
            {
                break;
            }

            // Equal-area spacing: the square root keeps the density even from centre to rim.
            float r = radius * sqrt((float(i) + 0.5) / float(tapCount));
            float angle = rotation + (float(i) * kGoldenAngle);
            float2 offset = float2(cos(angle), sin(angle)) * r;

            int2 t = int2(floor((float2(px) + offset) + 0.5));
            if (any(t < int2(0, 0)) || any(t >= size))
            {
                continue;
            }

            float2 tap = AccumulatedAt(t).xy;
            if (tap.y <= 0.0)
            {
                continue;
            }

            float2 tapUv = JitteredDepthUv(t);
            float tapDepth = DepthBilinear(uDepth, uDepthSampler, tapUv);
            if (tapDepth <= 0.0)
            {
                continue;
            }

            float3 tapView = ViewPositionAt(tapUv, tapDepth, p.unproject);
            float3 tapWorld = mul(p.viewToWorld, float4(tapView, 0.0)).xyz;

            // Distance off the centre's tangent plane, relative to how far away the centre is.
            float planeDistance = abs(dot(tapWorld - world, normal)) / max(viewPos.z, 1e-4);
            float planeWeight = exp2(-kPlaneSharpness * planeDistance * planeDistance);

            // Falls to nothing once the two normals are a radian apart.
            float3 tapNormal = NormalAt(tapUv);
            float normalWeight = 1.0 - saturate(acos(clamp(dot(normal, tapNormal), -1.0, 1.0)));

            float valueWeight = 1.0;
            if (valueTest)
            {
                valueWeight = exp2(-abs(centre.x - tap.x) / max(sigma, kMinValueSigma));
            }

            float weight = planeWeight * normalWeight * valueWeight;
            sum += tap * weight;
            total += weight;
        }

        result = sum / total;
    }

    uOutput.Store(px, float4(saturate(result.x), saturate(result.y), viewPos.z, 0.0));
}
