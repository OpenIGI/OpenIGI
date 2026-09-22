// Smooths the accumulated screen probes across their neighbours, without smoothing across geometry.
//
// Weighted by distance in WORLD space rather than by distance on screen, and scaled by the local probe
// spacing, so the filter covers the same amount of world at every depth; and by how squarely two probes
// face the same way, so a probe on the floor does not take light from one on the wall beside it.

#include "../Common/ComputeBindings.slang"

OPENIGI_COMPUTE_IMAGE(0) [[vk::image_format("rgba16f")]] WTexture2D<float4> uFiltered;

// accumulated irradiance (rgb), frame count (a).
OPENIGI_COMPUTE_TEXTURE(0) Sampler2D<float4> uAccum;

// probe world position (rgb), valid (a).
OPENIGI_COMPUTE_TEXTURE(1) Sampler2D<float4> uPosition;

// the G-buffer normal, read at each probe's own pixel.
OPENIGI_COMPUTE_TEXTURE(2) Sampler2D<float4> uSceneNormal;

struct Globals
{
    int uWidth;
    int uHeight;
    int uRadius;

    // The texture units the three samplers above are bound through.
    int uAccum;
    int uPosition;
    int uSceneNormal;
};

OPENIGI_COMPUTE_GLOBALS ConstantBuffer<Globals> g;

// Cosine to the fourth: a neighbour half a right angle away is worth a sixteenth, which is enough to keep
// a floor's light off the wall it meets without cutting the filter off at a hard angle.
float FacingWeight(float cosine)
{
    float c = max(cosine, 0.0);
    float c2 = c * c;
    return c2 * c2;
}

float3 ProbeNormal(int2 probe)
{
    float2 uv = (float2(probe) + 0.5) / float2(float(g.uWidth), float(g.uHeight));
    float3 n = (uSceneNormal.Sample(uv).xyz * 2.0) - 1.0;
    float len = length(n);
    return len > 0.001 ? (n / len) : float3(0.0, 0.0, 0.0);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 probe = int2(id.xy);
    if (probe.x >= g.uWidth || probe.y >= g.uHeight)
    {
        return;
    }

    float4 centre = uPosition.Load(int3(probe, 0));
    float4 accum = uAccum.Load(int3(probe, 0));

    if (centre.w <= 0.0)
    {
        uFiltered.Store(probe, accum);
        return;
    }

    float3 c = centre.xyz;
    float3 cn = ProbeNormal(probe);

    // How far apart the probes are HERE, measured from the four neighbours rather than assumed. The
    // probes are laid out on screen, so their world spacing grows with depth; a fixed falloff would
    // smooth a distant wall into one colour and leave a near one unfiltered.
    float spacing = 0.0;
    float spacingCount = 0.0;
    int2 cardinals[4] = { int2(1, 0), int2(-1, 0), int2(0, 1), int2(0, -1) };
    for (int i = 0; i < 4; i++)
    {
        int2 n = clamp(probe + cardinals[i], int2(0, 0), int2(g.uWidth - 1, g.uHeight - 1));
        float4 np = uPosition.Load(int3(n, 0));
        if (np.w > 0.0)
        {
            spacing += length(np.xyz - c);
            spacingCount += 1.0;
        }
    }

    spacing = spacingCount > 0.0 ? spacing / spacingCount : 1.0;
    float denom = (2.0 * spacing * spacing) + 1e-6;

    float3 sum = float3(0.0, 0.0, 0.0);
    float weight = 0.0;
    for (int y = -g.uRadius; y <= g.uRadius; y++)
    {
        for (int x = -g.uRadius; x <= g.uRadius; x++)
        {
            int2 n = clamp(probe + int2(x, y), int2(0, 0), int2(g.uWidth - 1, g.uHeight - 1));
            float4 np = uPosition.Load(int3(n, 0));
            if (np.w <= 0.0)
            {
                continue;
            }

            float4 na = uAccum.Load(int3(n, 0));
            float3 delta = np.xyz - c;
            float3 nn = ProbeNormal(n);

            // A probe whose normal could not be read stands in for itself: refusing it would punch a hole
            // in the filter wherever the G-buffer has nothing to say.
            float facing = (dot(cn, cn) < 0.5 || dot(nn, nn) < 0.5)
                ? 1.0
                : FacingWeight(dot(cn, nn));

            // Weighted by how long each neighbour has been accumulating, so a probe with a dozen frames
            // behind it counts for more than one that has just started.
            float w = exp(-dot(delta, delta) / denom) * (na.a + 1.0) * facing;
            sum += na.rgb * w;
            weight += w;
        }
    }

    float3 filtered = weight > 0.0 ? sum / weight : accum.rgb;
    uFiltered.Store(probe, float4(filtered, accum.a));
}
