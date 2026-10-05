// Brings the sparse screen probes up to the frame's own resolution and accumulates them over time.
//
// Two things stop the upsample smearing light across geometry: a probe is weighted by how close its own
// depth is to the pixel's, relative to that depth, and by how squarely the two face the same way. Where
// no probe survives both tests the nearest one is taken outright, because a pixel with no indirect light
// at all reads worse than one with a neighbour's.

#include "../Common/ComputeBindings.slang"
#include "../Common/DepthFilter.slang"

OPENIGI_COMPUTE_IMAGE(0) [[vk::image_format("rgba16f")]] WTexture2D<float4> uAccum;
OPENIGI_COMPUTE_IMAGE(1) [[vk::image_format("rgba16f")]] WTexture2D<float4> uAccumPosition;

// the sparse probe atlas (the gather's filtered output).
OPENIGI_COMPUTE_TEXTURE(0) Texture2D<float4> uProbe;
OPENIGI_COMPUTE_SAMPLER(0) SamplerState uProbeSampler;

// A depth image, declared as one: WGSL refuses one bound as a float texture. See DepthFilter.
OPENIGI_COMPUTE_TEXTURE(1) DepthTexture2D uSceneDepth;
OPENIGI_COMPUTE_SAMPLER(1) SamplerState uSceneDepthSampler;
OPENIGI_COMPUTE_TEXTURE(2) Texture2D<float4> uSceneNormal;
OPENIGI_COMPUTE_SAMPLER(2) SamplerState uSceneNormalSampler;

// last frame's uAccum and uAccumPosition.
OPENIGI_COMPUTE_TEXTURE(3) Texture2D<float4> uHistory;
OPENIGI_COMPUTE_SAMPLER(3) SamplerState uHistorySampler;
OPENIGI_COMPUTE_TEXTURE(4) Texture2D<float4> uHistoryPosition;
OPENIGI_COMPUTE_SAMPLER(4) SamplerState uHistoryPositionSampler;

// Where each probe of the atlas stood when it was traced, relative to that frame's camera; w marks it real.
OPENIGI_COMPUTE_TEXTURE(5) Texture2D<float4> uProbePosition;
OPENIGI_COMPUTE_SAMPLER(5) SamplerState uProbePositionSampler;

// The frame's motion field, moving objects' own stamped over the camera's: unjittered current UV less previous.
// Read only when uHasVelocity says it was made this frame.
OPENIGI_COMPUTE_TEXTURE(6) Texture2D<float4> uVelocity;
OPENIGI_COMPUTE_SAMPLER(6) SamplerState uVelocitySampler;

struct Globals
{
    // x scale, y scale, near, far.
    float4 uUnproject;

    // The same for the frame the history was drawn in. A cinematic zoom moves it every frame.
    float4 uPrevUnproject;

    // This frame's view rotation, as columns. Columns rather than a matrix because the seam sets a
    // parameter at a time and a vector is the widest thing it sets.
    float3 uViewToWorld0;
    float3 uViewToWorld1;
    float3 uViewToWorld2;

    // this frame's camera, relative to the pass origin.
    float3 uCameraToCache;

    // last frame's world-to-view rotation, as columns.
    float3 uPrevWorldToView0;
    float3 uPrevWorldToView1;
    float3 uPrevWorldToView2;

    float3 uPrevCameraToCache;

    // The frame the probe atlas was traced in: its world-to-view rotation, as columns, and its camera. Not
    // this frame's whenever the gather skipped a frame (r_screen_gi_cadence), and the probes are found
    // where the pixel stood THEN.
    float3 uProbeWorldToView0;
    float3 uProbeWorldToView1;
    float3 uProbeWorldToView2;
    float3 uProbeCameraToCache;

    int uScreenWidth;
    int uScreenHeight;
    int uHasHistory;

    // the ceiling on the running average, near ten.
    int uMaxFrames;

    // The texture units the five samplers above are bound through.
    int uProbe;
    int uSceneDepth;
    int uSceneNormal;
    int uHistory;
    int uHistoryPosition;
    int uProbePosition;
    int uVelocity;

    // pixels between probes.
    int uProbeStep;

    // The frame the probe atlas was traced in, which decides where in each tile its probes stand.
    int uProbeFrame;

    // Whether uVelocity holds this frame's motion field.
    int uHasVelocity;
};

OPENIGI_COMPUTE_GLOBALS ConstantBuffer<Globals> g;

bool IsSkyDepth(float depth)
{
    return depth <= 0.0;
}

float3 ViewPositionAt(float2 uv, float depth)
{
    float ndcZ = 1.0 - (2.0 * depth);
    float viewZ = (2.0 * g.uUnproject.z * g.uUnproject.w)
        / ((g.uUnproject.w + g.uUnproject.z) - (ndcZ * (g.uUnproject.w - g.uUnproject.z)));
    float2 ndc = (uv * 2.0) - 1.0;
    float ySign = -1.0;
    return float3(
        (ndc.x * viewZ) / g.uUnproject.x,
        ySign * (ndc.y * viewZ) / g.uUnproject.y,
        viewZ);
}

// Cosine to the fourth, the same weight the filter uses, so the two agree about when two surfaces are
// the same surface.
float FacingWeight(float cosine)
{
    float c = max(cosine, 0.0);
    float c2 = c * c;
    return c2 * c2;
}

float2 ProjectToUv(float3 viewPosition)
{
    float ySign = -1.0;
    float2 ndc = float2(
        (viewPosition.x * g.uUnproject.x) / viewPosition.z,
        ySign * (viewPosition.y * g.uUnproject.y) / viewPosition.z);
    return (ndc * 0.5) + 0.5;
}

// How sharply a probe is rejected for standing off the pixel's plane, measured relative to the pixel's
// view depth: the strict scale first, and a looser one used only when the strict one leaves nothing.
static const float kPlaneSharpness = 200000.0;
static const float kPlaneSharpnessLoose = 20000.0;

// The four probes around the pixel, each weighted by its bilinear share and by how far ITS OWN traced
// position lies off the pixel's plane. It weighted by this frame's depth resampled at each probe's tile
// corner instead, which on a wall or ceiling seen at an angle rejected probes on the very surface being
// lit and swung the weights with every small move of the camera; and with the gather skipping frames it
// laid the probes of the frame before over this frame's picture, four probes out of place during a turn.
// The probes are found by projecting the pixel into the frame they were traced in.
// Where in its tile a frame places each probe: one offset shared by the whole screen, walking eight positions
// spread over the tile (a stratified pattern, the second axis bit-reversed). A probe fixed at its tile's
// centre sampled the same surface points every frame, so the grid's own pattern -- a staircase of cells
// along every thin edge, a fitting, a sill -- was printed into the light and no amount of accumulation could
// average it out. Moved every frame, the pattern differs frame to frame and the per-pixel history averages
// it away. Every pass that finds a probe from a pixel asks this for the frame the probe was traced in.
int2 ProbeTileOffset(int frame, int step)
{
    uint i = uint(frame) & 7u;
    uint reversed = ((i & 1u) << 2) | (i & 2u) | ((i >> 2) & 1u);
    float2 at = (float2(float(i), float(reversed)) + 0.5) / 8.0;
    return min(int2(floor(at * float(step))), int2(step - 1, step - 1));
}

float3 UpsampleProbe(float2 uv, float3 pixelWorld, float pixelViewZ, float3 pixelNormal)
{
    uint width;
    uint height;
    uProbe.GetDimensions(width, height);
    int2 grid = int2(int(width), int(height));

    float3 probeView = (g.uProbeWorldToView0 * (pixelWorld.x - g.uProbeCameraToCache.x))
        + (g.uProbeWorldToView1 * (pixelWorld.y - g.uProbeCameraToCache.y))
        + (g.uProbeWorldToView2 * (pixelWorld.z - g.uProbeCameraToCache.z));
    float2 probeFrameUv = probeView.z > g.uUnproject.z ? ProjectToUv(probeView) : uv;

    // A probe sits at the centre of the pixel step/2 into its tile, as the gather placed it.
    float step = float(max(g.uProbeStep, 1));
    float2 pixelCoord = probeFrameUv * float2(float(g.uScreenWidth), float(g.uScreenHeight));
    float2 probeOffset = float2(ProbeTileOffset(g.uProbeFrame, max(g.uProbeStep, 1)));
    float2 probeCoord = (pixelCoord - probeOffset - 0.5) / step;
    float2 base = floor(probeCoord);
    float2 fraction = probeCoord - base;

    float3 strictSum = float3(0.0, 0.0, 0.0);
    float strictWeight = 0.0;
    float3 looseSum = float3(0.0, 0.0, 0.0);
    float looseWeight = 0.0;
    float3 nearestValue = float3(0.0, 0.0, 0.0);
    float nearestDistance = 3.4e38;
    bool haveNormal = dot(pixelNormal, pixelNormal) > 0.5;

    for (int y = 0; y < 2; y++)
    {
        for (int x = 0; x < 2; x++)
        {
            int2 coord = int2(base) + int2(x, y);
            if (any(coord < int2(0, 0)) || any(coord >= grid))
            {
                continue;
            }

            float4 probePosition = uProbePosition.Load(int3(coord, 0));
            if (probePosition.w <= 0.0)
            {
                continue;
            }

            // How far the probe sits off the pixel's plane (or from the pixel outright, with no normal to
            // define one), as a fraction of the pixel's depth so near and far pixels are judged alike.
            // Stored relative to the camera of the frame the probe was traced in.
            float3 offset = (probePosition.xyz + g.uProbeCameraToCache) - pixelWorld;
            float planeDistance = haveNormal ? abs(dot(offset, pixelNormal)) : length(offset);
            float relative = planeDistance / max(pixelViewZ, 0.001);
            float relative2 = relative * relative;
            float bilinear = (x == 0 ? 1.0 - fraction.x : fraction.x)
                * (y == 0 ? 1.0 - fraction.y : fraction.y);
            float3 value = uProbe.Load(int3(coord, 0)).rgb;

            float strict = bilinear * exp2(-kPlaneSharpness * relative2);
            float loose = bilinear * exp2(-kPlaneSharpnessLoose * relative2);
            strictSum += value * strict;
            strictWeight += strict;
            looseSum += value * loose;
            looseWeight += loose;

            float apart = dot(offset, offset);
            if (apart < nearestDistance)
            {
                nearestDistance = apart;
                nearestValue = value;
            }
        }
    }

    // All four on this surface, or nearly: the plain bilinear answer.
    float bilinearTotal = 0.0;
    for (int b = 0; b < 4; b++)
    {
        bilinearTotal += ((b & 1) == 0 ? 1.0 - fraction.x : fraction.x) * ((b >> 1) == 0 ? 1.0 - fraction.y : fraction.y);
    }

    if (strictWeight > 0.6 * bilinearTotal)
    {
        return strictSum / strictWeight;
    }

    // Some of the four lie on something else -- the lip of a fitting, a window bar, a ledge thinner than
    // the probe spacing. Renormalising what is left hands each pixel a single probe's light across a whole
    // probe cell, and the light round every such edge was drawn as a staircase of cells. The wider search
    // below blends the surface's probes over two cells with a tent instead, which has no cell edges in it.
    // The nearest probe used to be taken outright where none of the four was on the surface, and which
    // probe is nearest changes a whole cell at a time. The ring round the four is searched for the surface's
    // own probes, strictly and then loosely; failing those, all sixteen are blended by how far each stands
    // from the pixel.
    float3 ringSum = float3(0.0, 0.0, 0.0);
    float ringWeight = 0.0;
    float3 ringLooseSum = float3(0.0, 0.0, 0.0);
    float ringLooseWeight = 0.0;
    float3 nearSum = float3(0.0, 0.0, 0.0);
    float nearWeight = 0.0;
    float scale = max(pixelViewZ, 0.001);
    for (int y = -1; y <= 2; y++)
    {
        for (int x = -1; x <= 2; x++)
        {
            int2 coord = int2(base) + int2(x, y);
            if (any(coord < int2(0, 0)) || any(coord >= grid))
            {
                continue;
            }

            float4 probePosition = uProbePosition.Load(int3(coord, 0));
            if (probePosition.w <= 0.0)
            {
                continue;
            }

            float3 offset = (probePosition.xyz + g.uProbeCameraToCache) - pixelWorld;
            float planeDistance = haveNormal ? abs(dot(offset, pixelNormal)) : length(offset);
            float relative = planeDistance / scale;
            float2 apartCells = abs(float2(float(x), float(y)) - fraction);
            float tent = max(1.0 - (apartCells.x * 0.5), 0.0) * max(1.0 - (apartCells.y * 0.5), 0.0);
            float3 value = uProbe.Load(int3(coord, 0)).rgb;

            float onSurface = tent * exp2(-kPlaneSharpness * relative * relative);
            ringSum += value * onSurface;
            ringWeight += onSurface;
            float looseOn = tent * exp2(-kPlaneSharpnessLoose * relative * relative);
            ringLooseSum += value * looseOn;
            ringLooseWeight += looseOn;

            float distance = length(offset) / scale;
            float near = tent / (1.0 + (distance * distance * 400.0));
            nearSum += value * near;
            nearWeight += near;
        }
    }

    if (ringWeight > 1e-4)
    {
        return ringSum / ringWeight;
    }

    if (ringLooseWeight > 1e-4)
    {
        return ringLooseSum / ringLooseWeight;
    }

    return nearWeight > 1e-6 ? nearSum / nearWeight : nearestValue;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 pixel = int2(id.xy);
    if (pixel.x >= g.uScreenWidth || pixel.y >= g.uScreenHeight)
    {
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(float(g.uScreenWidth), float(g.uScreenHeight));
    float depth = DepthComputeSample(uSceneDepth, uSceneDepthSampler, uv);
    if (IsSkyDepth(depth))
    {
        uAccum.Store(pixel, float4(0.0, 0.0, 0.0, 0.0));
        uAccumPosition.Store(pixel, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 viewPosition = ViewPositionAt(uv, depth);
    float3 world = g.uCameraToCache
        + (g.uViewToWorld0 * viewPosition.x)
        + (g.uViewToWorld1 * viewPosition.y)
        + (g.uViewToWorld2 * viewPosition.z);

    float3 storedNormal = (ComputeSample(uSceneNormal, uSceneNormalSampler, uv).xyz * 2.0) - 1.0;
    float storedLength = length(storedNormal);
    float3 pixelNormal = storedLength > 0.001
        ? (storedNormal / storedLength)
        : float3(0.0, 0.0, 0.0);
    float3 worldNormal = pixelNormal;

    float3 current = UpsampleProbe(uv, world, viewPosition.z, pixelNormal);

    // The neighbourhood the history is allowed to sit in, measured a whole PROBE apart rather than a
    // pixel: neighbouring pixels share their probes, so a pixel-wide box describes the upsample's own
    // interpolation instead of the light around it and clamps the history to itself. Each neighbour is
    // upsampled at its own surface, so the box describes the light there rather than this pixel's plane.
    float2 texel = 1.0 / float2(float(g.uScreenWidth), float(g.uScreenHeight));
    float probeSpan = float(max(g.uProbeStep, 2));

    float3 m1 = current;
    float3 m2 = current * current;
    float samples = 1.0;
    for (int s = 0; s < 4; s++)
    {
        float2 off = s == 0 ? float2(1.0, 0.0)
            : s == 1 ? float2(-1.0, 0.0)
            : s == 2 ? float2(0.0, 1.0)
            : float2(0.0, -1.0);
        float2 nuv = clamp(uv + (off * texel * probeSpan), float2(0.0, 0.0), float2(1.0, 1.0));
        float nDepth = DepthComputeSample(uSceneDepth, uSceneDepthSampler, nuv);
        if (IsSkyDepth(nDepth))
        {
            continue;
        }

        float3 nView = ViewPositionAt(nuv, nDepth);
        float3 nWorld = g.uCameraToCache
            + (g.uViewToWorld0 * nView.x)
            + (g.uViewToWorld1 * nView.y)
            + (g.uViewToWorld2 * nView.z);
        float3 nNormal = (ComputeSample(uSceneNormal, uSceneNormalSampler, nuv).xyz * 2.0) - 1.0;
        float nLength = length(nNormal);
        nNormal = nLength > 0.001 ? (nNormal / nLength) : float3(0.0, 0.0, 0.0);

        float3 neighbour = UpsampleProbe(nuv, nWorld, nView.z, nNormal);
        m1 += neighbour;
        m2 += neighbour * neighbour;
        samples += 1.0;
    }

    float3 mean = m1 / samples;
    float3 sigma = sqrt(max((m2 / samples) - (mean * mean), float3(0.0, 0.0, 0.0)));
    float3 clampLo = mean - (1.5 * sigma);
    float3 clampHi = mean + (1.5 * sigma);

    float3 accumulated = current;
    float frames = 0.0;

    if (g.uHasHistory != 0)
    {
        float3 prevView = (g.uPrevWorldToView0 * (world.x - g.uPrevCameraToCache.x))
            + (g.uPrevWorldToView1 * (world.y - g.uPrevCameraToCache.y))
            + (g.uPrevWorldToView2 * (world.z - g.uPrevCameraToCache.z));

        if (prevView.z > 0.0)
        {
            float prevYSign = -1.0;
            float2 prevUv = (float2(
                (prevView.x * g.uPrevUnproject.x) / prevView.z,
                prevYSign * (prevView.y * g.uPrevUnproject.y) / prevView.z) * 0.5) + 0.5;

            // A car or a door moved on its own as well as with the camera: where the motion field disagrees
            // with the camera by more than the jitter could explain, the surface came from where the field
            // says, and the plane test below -- which would only find the road it left -- is not asked.
            bool ownMotion = false;
            if (g.uHasVelocity != 0)
            {
                float2 screen = float2(float(g.uScreenWidth), float(g.uScreenHeight));
                float2 motion = uVelocity.SampleLevel(uVelocitySampler, uv, 0.0).xy;
                float2 apart = (motion - (uv - prevUv)) * screen;
                if (dot(apart, apart) > 2.25)
                {
                    prevUv = uv - motion;
                    ownMotion = true;
                }
            }

            if (all(prevUv >= float2(0.0, 0.0)) && all(prevUv <= float2(1.0, 1.0)))
            {
                float2 sizeF = float2(float(g.uScreenWidth), float(g.uScreenHeight));
                float2 tc = (prevUv * sizeF) - 0.5;
                float2 tcBase = floor(tc);
                float2 tcFrac = tc - tcBase;
                float pixelSpan =
                    (2.0 * viewPosition.z) / (g.uUnproject.y * float(g.uScreenHeight));

                float3 historySum = float3(0.0, 0.0, 0.0);
                float frameSum = 0.0;
                float weightSum = 0.0;

                for (int t = 0; t < 4; t++)
                {
                    int2 tap = int2(tcBase) + int2(t & 1, t >> 1);
                    if (any(tap < int2(0, 0))
                        || any(tap >= int2(g.uScreenWidth, g.uScreenHeight)))
                    {
                        continue;
                    }

                    float4 tapPos = uHistoryPosition.Load(int3(tap, 0));
                    if (tapPos.w <= 0.0)
                    {
                        continue;
                    }

                    // Judged by distance from this surface's PLANE rather than by depth: a wall seen at a
                    // glancing angle has a large depth gradient and no discontinuity at all, and a depth
                    // test rejects every tap on it the moment the camera moves.
                    float3 tapWorld = tapPos.xyz + g.uPrevCameraToCache;
                    float3 offsetFromSurface = tapWorld - world;
                    float planeDistance = abs(dot(offsetFromSurface, worldNormal));
                    if (!ownMotion && planeDistance > max(0.02, 4.0 * pixelSpan))
                    {
                        continue;
                    }

                    float w = ((t & 1) == 0 ? 1.0 - tcFrac.x : tcFrac.x)
                        * ((t >> 1) == 0 ? 1.0 - tcFrac.y : tcFrac.y);
                    float4 tapHistory = uHistory.Load(int3(tap, 0));
                    historySum += tapHistory.rgb * w;
                    frameSum += tapHistory.a * w;
                    weightSum += w;
                }

                if (weightSum > 0.01)
                {
                    float4 history = float4(historySum / weightSum, frameSum / weightSum);
                    float3 clamped = clamp(history.rgb, clampLo, clampHi);
                    frames = min(history.w + 1.0, float(g.uMaxFrames));
                    float alpha = 1.0 / (1.0 + frames);
                    accumulated = lerp(clamped, current, alpha);
                }
            }
        }
    }

    uAccum.Store(pixel, float4(accumulated, frames));
    uAccumPosition.Store(pixel, float4(world - g.uCameraToCache, 1.0));
}
