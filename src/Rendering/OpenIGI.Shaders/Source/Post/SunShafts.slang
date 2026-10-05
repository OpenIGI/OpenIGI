// Light shafts from the sun, drawn in screen space: the bright sky around the sun's disc, blurred outward
// along lines from the sun's place on screen, so whatever stands between the eye and the sun -- a tower's
// lattice, a fence, the edge of a roof -- cuts dark rays into the glow and the gaps between it bright ones.
//
// What this adds is the look of thin air seen against the sun, which the volume cannot give: on a clear
// morning the medium is far too thin for its own shafts to show, and a camera pointed into the sun still
// sees them, in its own lens as much as in the air. The volume's shafts are the ones that stand in the
// world, seen from the side; these are the ones that radiate from the sun.
//
// Four modes over one program, by p.sun.z: the mask, two blurs, and the composite onto the bloom.

[[vk::binding(0, 0)]] Texture2D<float4> uSource;
[[vk::binding(1, 0)]] SamplerState uSourceSampler;

// The scene's depth, at whatever resolution the frame was rendered: nought is the sky.
[[vk::binding(2, 0)]] DepthTexture2D uDepth;

// What the blurs built, for the composite.
[[vk::binding(3, 0)]] Texture2D<float4> uShafts;
[[vk::binding(4, 0)]] SamplerState uShaftsSampler;

[[vk::binding(5, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct Params
{
    // the target's width, height, 1/width, 1/height.
    float4 size;

    // the sun's place on screen (uv), the mode, the step as a share of the way to the sun.
    float4 sun;

    // the strength, the sky brightness the mask ignores below, how tightly the mask hugs the sun, and how
    // much of the effect the sun's place on screen leaves.
    float4 control;

    // the source's width and height, and the depth's.
    float4 sizes;
};

[[vk::binding(6, 0)]] ConstantBuffer<Params> p;

static const int kTaps = 16;

/// Whether the scene is open sky at a uv.
bool SkyAt(float2 uv)
{
    int2 texel = clamp(int2(uv * p.sizes.zw), int2(0, 0), int2(p.sizes.zw) - 1);
    return uDepth.Load(int3(texel, 0)) <= 0.0;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.size.x) || px.y >= int(p.size.y))
    {
        return;
    }

    float2 uv = (float2(px) + 0.5) * p.size.zw;
    int mode = int(p.sun.z + 0.5);

    if (mode == 0)
    {
        // The mask: the sky alone, and only near the sun. Four taps over the source's texels, so the
        // half-size mask does not alias the lattice the rays are cut by.
        float3 sum = float3(0.0, 0.0, 0.0);
        for (int k = 0; k < 4; k++)
        {
            float2 offset = float2(float(k & 1) - 0.5, float(k >> 1) - 0.5) * p.size.zw * 0.5;
            float2 at = uv + offset;
            if (SkyAt(at))
            {
                float3 sky = uSource.SampleLevel(uSourceSampler, at, 0.0).rgb;
                float luminance = dot(sky, float3(0.2126, 0.7152, 0.0722));
                sum += sky * saturate((luminance - p.control.y) / max(luminance, 1e-4));
            }
        }

        // Fades with the distance from the sun's disc, measured with the screen's own aspect so the
        // glow is round rather than stretched across a wide frame; with a bright core round the disc
        // itself on a wide, faint skirt. The core is what the blur streaks into rays: a mask as smooth as
        // the sky round it gave the blur nothing to cut, and a lattice in front of the sun no shadow.
        float2 toSun = (p.sun.xy - uv) * float2(p.size.x / p.size.y, 1.0);
        float distance = length(toSun);
        float near = pow(saturate(1.0 - (distance * 1.2)), p.control.z)
            + (3.0 * pow(saturate(1.0 - (distance * 4.0)), 2.0));
        uTarget.Store(px, float4((sum * 0.25) * near, 1.0));
        return;
    }

    if (mode == 1 || mode == 2)
    {
        // A blur along the line to the sun: each tap a step nearer it, weighted less the further it went,
        // so the bright sky round the sun spreads outward and an occluder's gap or bar runs as a ray.
        float2 delta = (p.sun.xy - uv) * p.sun.w;
        float3 sum = float3(0.0, 0.0, 0.0);
        float weightSum = 0.0;
        float weight = 1.0;
        float2 at = uv;
        for (int t = 0; t < kTaps; t++)
        {
            sum += uSource.SampleLevel(uSourceSampler, at, 0.0).rgb * weight;
            weightSum += weight;
            weight *= 0.94;
            at += delta;
        }

        uTarget.Store(px, float4(sum / max(weightSum, 1e-4), 1.0));
        return;
    }

    // The composite: the bloom as it was, and the shafts on it.
    float3 bloom = uSource.SampleLevel(uSourceSampler, uv, 0.0).rgb;
    float3 shafts = uShafts.SampleLevel(uShaftsSampler, uv, 0.0).rgb;
    uTarget.Store(px, float4(bloom + (shafts * p.control.x * p.control.w), 1.0));
}
