// Light shafts from the sun, drawn in screen space. Two things are carried along lines from the sun's place
// on screen, in one image:
//
//  - in the alpha, whether the sun's direction is OPEN at each pixel: one over the sky and the distant
//    haze, nought over anything near. Blurred towards the sun, it says how much of the way to the sun
//    is clear; where a tower or a roof stands in the way it falls, and the composite darkens the haze
//    behind the occluder by it. That is what a light shaft is: the lit air either side of the shadow an
//    occluder casts into it, seen against the sun.
//  - in the colour, the brightest of the sky round the sun's disc -- only what stands over a threshold
//    set by the sky just outside it -- streaked out the same way, so the gaps in a lattice throw bright rays.
//
// What this adds is the look of air seen against the sun, which the volume cannot give at the scale of
// a frame: its cells are metres deep, and the shadow a fence casts into the haze is thinner than one.
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

    // the strength of the bright rays, the share of the sky round the disc the mask ignores below, how
    // much of the glow's radius the bright rays come from, and how much of the effect the sun's place on
    // screen leaves.
    float4 control;

    // the source's width and height, and the depth's.
    float4 sizes;

    // the near and far planes, how deep the shadowed shafts are cut into the haze, and unused.
    float4 occlusion;
};

[[vk::binding(6, 0)]] ConstantBuffer<Params> p;

static const int kTaps = 16;

// Over what range of distance a surface counts as haze rather than as something in front of it, in
// metres: nearer, the air between it and the eye is too thin to carry a shaft.
static const float kHazeStart = 40.0;
static const float kHazeFull = 160.0;

/// How far off along the view the scene is at a uv, in metres; the far plane for the sky.
float SceneDistance(float2 uv)
{
    int2 texel = clamp(int2(uv * p.sizes.zw), int2(0, 0), int2(p.sizes.zw) - 1);
    float depth = uDepth.Load(int3(texel, 0));
    if (depth <= 0.0)
    {
        return p.occlusion.y;
    }

    float near = p.occlusion.x;
    float far = p.occlusion.y;
    float ndcZ = 1.0 - (2.0 * depth);
    return (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
}

/// How much of a pixel is open haze or sky towards the sun, rather than something standing near.
float HazeAt(float2 uv)
{
    return smoothstep(kHazeStart, kHazeFull, SceneDistance(uv));
}

// The distance over which a view's air lies thick enough to be most of what is seen, in metres.
static const float kAirDepth = 80.0;

/// How much of what a pixel shows is the lit air in front of it rather than the surface: the share the
/// shafts can darken or brighten. The sky is all air; a wall ten metres off hardly any.
float AirAt(float2 uv)
{
    return 1.0 - exp(-SceneDistance(uv) / kAirDepth);
}

/// The distance from the sun's place, with the screen's own aspect so the glow is round.
float SunDistance(float2 uv)
{
    return length((p.sun.xy - uv) * float2(p.size.x / p.size.y, 1.0));
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
        // The threshold, from the sky in a ring just outside the sun's disc -- the dimmest of four points
        // on it, so the disc itself, thousands of times brighter, never sets it: only the core of the glow
        // round the disc throws bright rays. Off the screen, the nearest edge stands in.
        float ring = 1e9;
        for (int r = 0; r < 4; r++)
        {
            float2 around = float2(float(r & 1) * 2.0 - 1.0, float(r >> 1) * 2.0 - 1.0)
                * float2(p.size.y / p.size.x, 1.0) * 0.035;
            float3 sky = uSource.SampleLevel(uSourceSampler, saturate(p.sun.xy + around), 0.0).rgb;
            ring = min(ring, dot(sky, float3(0.2126, 0.7152, 0.0722)));
        }

        float threshold = ring * p.control.y;

        // And the most a texel may bring: a few times the ring. Every texel's line ends on the disc, so the
        // disc at its own brightness flooded the whole glow with one block of white.
        float ceiling = max(ring * 4.0, 1e-4);

        // Four taps over the source's texels, so the half-size mask does not alias the lattice the rays
        // are cut by.
        float3 bright = float3(0.0, 0.0, 0.0);
        float open = 0.0;
        for (int k = 0; k < 4; k++)
        {
            float2 offset = float2(float(k & 1) - 0.5, float(k >> 1) - 0.5) * p.size.zw * 0.5;
            float2 at = uv + offset;
            float haze = HazeAt(at);
            float3 colour = uSource.SampleLevel(uSourceSampler, at, 0.0).rgb;
            float luminance = dot(colour, float3(0.2126, 0.7152, 0.0722));
            float kept = min(max(luminance - threshold, 0.0), ceiling);
            bright += colour * ((kept / max(luminance, 1e-4)) * haze);
            open += haze;
        }

        float core = pow(saturate(1.0 - (SunDistance(uv) / max(p.control.z, 1e-3))), 2.0);
        uTarget.Store(px, float4(bright * (0.25 * core), open * 0.25));
        return;
    }

    if (mode == 1 || mode == 2)
    {
        // A blur along the line to the sun: each tap a step nearer it, weighted less the further it went,
        // so the bright core spreads outward and an occluder's gap or bar runs as a ray -- in the colour
        // and in the openness alike.
        float2 delta = (p.sun.xy - uv) * p.sun.w;
        float4 sum = float4(0.0, 0.0, 0.0, 0.0);
        float weightSum = 0.0;
        float weight = 1.0;
        float2 at = uv;
        for (int t = 0; t < kTaps; t++)
        {
            // Past the screen's edge nothing is known; the openness is taken as open there, so an edge does
            // not cast a shadow of its own.
            float4 tap = uSource.SampleLevel(uSourceSampler, at, 0.0);
            bool inside = all(at >= float2(0.0, 0.0)) && all(at <= float2(1.0, 1.0));
            sum += float4(tap.rgb, inside ? tap.a : 1.0) * weight;
            weightSum += weight;
            weight *= 0.95;
            at += delta;
        }

        uTarget.Store(px, sum / max(weightSum, 1e-4));
        return;
    }

    // The composite: the bloom as it was with the bright rays on it, and in the alpha what the scene is
    // multiplied by -- darker where the air lies in an occluder's shadow, within the glow round the sun
    // where the lit air is what one sees, and by as much as the pixel is air. A shadow cast into the air
    // lies between the occluder and the eye, so it falls over whatever is behind that air, near or far;
    // the nearer that is, the less air there is to darken. The bright rays are laid on the same way.
    float3 bloom = uSource.SampleLevel(uSourceSampler, uv, 0.0).rgb;
    float4 shafts = uShafts.SampleLevel(uShaftsSampler, uv, 0.0);
    float haze = AirAt(uv);
    float glow = pow(saturate(1.0 - (SunDistance(uv) * 0.9)), 1.5);
    float shadowed = saturate(1.0 - shafts.a);
    float darken = 1.0 - (p.occlusion.z * p.control.w * glow * haze * shadowed);
    float3 rays = shafts.rgb * (p.control.x * p.control.w * haze);
    uTarget.Store(px, float4(bloom + rays, saturate(darken)));
}
