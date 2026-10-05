// The lighting pass: reads the G-buffer and writes lit HDR colour.
//
// Two variants, because whether the lamps cast traced shadows is a compile-time question -- the ray
// query needs its own resource bound. A lamp that does not shadow lights through the wall it is mounted
// on, so this is not a refinement; it is what stops a corridor fitting lighting the room on the other
// side of the partition. The software form marches the level's distance field for those shadows
// instead, and is the one a device without ray tracing -- a browser -- runs.

#ifndef OPENIGI_RAY_QUERY
#error "OPENIGI_RAY_QUERY must be defined by the shader build, as 1 or 0."
#endif

#include "../Common/Fullscreen.slang"
#include "../Common/GBufferEncoding.slang"
#include "../Common/PushConstants.slang"
#include "../Common/SkyAtmosphere.slang"
#include "../Common/SkyView.slang"
#include "../Common/NightSky.slang"
#include "../Common/Wetness.slang"
#include "../Common/Flashlight.slang"

// The G-buffer, read as ordinary textures at this very pixel, in a pass of its own after the one that
// wrote it: the form every backend has. (Vulkan could read it as subpass inputs and keep it on chip, but
// WebGPU has no subpasses, and one source read two ways is two chances for them to disagree.)
[[vk::binding(0, 0)]] Texture2D<float4> uBaseColour;
[[vk::binding(1, 0)]] Texture2D<float4> uNormal;
[[vk::binding(2, 0)]] Texture2D<float4> uMaterial;
[[vk::binding(3, 0)]] Texture2D<float4> uBaked;
// The depth is a DEPTH texture, not an array of floats that happens to hold depth: the two are one
// descriptor in Vulkan and different types in WGSL, and a bind group layout that calls this one a
// colour texture binds nothing.
[[vk::binding(4, 0)]] DepthTexture2D uDepth;

/// The texel of the G-buffer this invocation shades, which is the one under this fragment.
float4 LoadGBuffer(Texture2D<float4> image, float4 position)
{
    return image.Load(int3(int2(position.xy), 0));
}

/// The same, for the depth, which holds one channel rather than four.
float4 LoadGBuffer(DepthTexture2D image, float4 position)
{
    return float4(image.Load(int3(int2(position.xy), 0)), 0.0, 0.0, 0.0);
}

#define OPENIGI_GBUFFER(image) LoadGBuffer(image, input.position)

[[vk::binding(5, 0)]] Texture3D<float4> uProbeRed;
[[vk::binding(6, 0)]] SamplerState uProbeRedSampler;
[[vk::binding(7, 0)]] Texture3D<float4> uProbeGreen;
[[vk::binding(8, 0)]] SamplerState uProbeGreenSampler;
[[vk::binding(9, 0)]] Texture3D<float4> uProbeBlue;
[[vk::binding(10, 0)]] SamplerState uProbeBlueSampler;
[[vk::binding(11, 0)]] Texture3D<float4> uProbeDistance;
[[vk::binding(12, 0)]] SamplerState uProbeDistanceSampler;

struct Gi
{
    // xScale, yScale, near, far.
    float4 unproject;

    // xyz origin, w enabled.
    float4 probeOrigin;

    // xyz 1/count, w spacing.
    float4 probeInvCount;

    // halfSpacing, blend, lightScale, _.
    float4 probeMisc;

    // rgb sky tint, w the cascades' filter radius in texels.
    float4 probeSky;

    // xyz camera, w the reach of the cascades' contact shadow in metres (nought: none).
    float4 camera;

    // screen-GI strength, full-resolution, _, _.
    float4 giParams;

    // view-space to camera-relative world rotation.
    float4x4 viewToWorld;

    // the view depth each shadow cascade stops at.
    float4 cascadeSplits;

    // cascade count, depth bias, normal bias, shadow texel size.
    float4 shadowConfig;

    float4x4 cascadeMatrix[4];

    // wetness, puddle coverage, ripple strength, seconds.
    float4 wetness0;

    // puddle size, ripple size, wind ripple strength, shelter map present.
    float4 wetness1;

    // the map's world origin XY, texels per metre, the height it counts from.
    float4 shelterMap0;

    // metres per step, the headroom, the reach, _.
    float4 shelterMap1;

    // texels across, texels down, _, _.
    float4 shelterMap2;

    // the fog volume's slice distribution: B, O, S, 1/slices. S of 0 is off.
    float4 fog;

    // the player's flashlight (FlashlightBlock); a range of nought is off.
    FlashlightParams flashlight;
};

[[vk::binding(13, 0)]] ConstantBuffer<Gi> gi;

[[vk::binding(14, 0)]] Texture2D<float4> uScreenGi;
[[vk::binding(15, 0)]] SamplerState uScreenGiSampler;
// A DEPTH texture rather than an array of floats, which is the same descriptor in Vulkan and a
// different type in WGSL: a comparison lookup there takes texture_depth_2d_array, and given
// Texture2DArray<float> the compiler emits a call wgpu refuses to parse. The SPIR-V is unchanged.
[[vk::binding(16, 0)]] DepthTexture2DArray uShadowMap;
[[vk::binding(17, 0)]] SamplerComparisonState uShadowMapSampler;
[[vk::binding(18, 0)]] Texture2D<float4> uOcclusion;
[[vk::binding(19, 0)]] SamplerState uOcclusionSampler;
[[vk::binding(20, 0)]] Texture2D<float4> uSkyView;
[[vk::binding(21, 0)]] SamplerState uSkyViewSampler;
[[vk::binding(22, 0)]] Texture2D<float4> uTransmittanceLut;
[[vk::binding(23, 0)]] SamplerState uTransmittanceLutSampler;
[[vk::binding(24, 0)]] Texture2D<float4> uSkyIrradiance;
[[vk::binding(25, 0)]] SamplerState uSkyIrradianceSampler;
[[vk::binding(26, 0)]] Texture2D<float4> uSunShadow;
[[vk::binding(27, 0)]] SamplerState uSunShadowSampler;

// The level's lamps: the lights I.G.I. implies and never declares. Positions arrive in VIEW space,
// because the level sits tens of kilometres from the world origin and the camera-relative subtract has
// to be done in double precision on the game's side before a float ever sees it.
#ifndef OPENIGI_MAX_LAMPS
#error "OPENIGI_MAX_LAMPS must be defined by the shader build."
#endif

struct Lamps
{
    // count, traced shadows, frame index, screen occlusion.
    float4 control;

    // largest source a shadow ray samples, _, _, _.
    float4 source;

    // xyz view position, w reach.
    float4 positionRadius[OPENIGI_MAX_LAMPS];

    // rgb colour, a strength.
    float4 colourIntensity[OPENIGI_MAX_LAMPS];

    // xyz view-space facing (zero for a point), w source size.
    float4 direction[OPENIGI_MAX_LAMPS];

    // xyz the emitting rectangle's long axis at half its length (view space), w its half width. Read by
    // the lamp trace; declared here so the two layouts stay one.
    float4 rect[OPENIGI_MAX_LAMPS];
};

[[vk::binding(28, 0)]] ConstantBuffer<Lamps> lamps;

// What the lamp shadows resolved to, at half resolution, with the denoiser's frames behind it: traced
// against the world where the adapter can, marched through the screen and the distance field where it
// cannot (Lamp/Shadow.slang's software form). Declared in both forms for that reason.
[[vk::binding(29, 0)]] Texture2D<float4> uLampVisibility;
[[vk::binding(30, 0)]] SamplerState uLampVisibilitySampler;

#if OPENIGI_RAY_QUERY

// The world the lamps trace against, where the adapter can trace at all. This is the OTHER half of the
// lamp shadow -- the screen-space march finds a wall the camera can see; this finds the wall it cannot,
// because a ray reaches the whole world and not only the depth buffer. It is what stops a lamp behind a
// wall that is off-screen from lighting a puddle in front. Bound only in the traced variant; the ray is
// fired only when the RT switch is on, so it stays optional on hardware that could trace but has been
// asked not to.
[[vk::binding(31, 0)]] RaytracingAccelerationStructure uWorldTlas;
#endif

// The volumetric fog, already lit and integrated. Bound whether or not there is any fog this frame: a
// descriptor the layout declares must be written, and gi.fog says whether to read it.
[[vk::binding(32, 0)]] Texture3D<float4> uFogVolume;
[[vk::binding(33, 0)]] SamplerState uFogVolumeSampler;

// The level's roofs, seen from above: one texel per square of ground, holding the height of the solid
// world standing over it. Always bound -- a level with no map binds a placeholder and gi.wetness1.w says
// to ignore it, because a descriptor the layout declares must be written.
[[vk::binding(34, 0)]] Texture2D<float4> uShelterMap;
[[vk::binding(35, 0)]] SamplerState uShelterMapSampler;

// How much of the flashlight reaches each pixel, at this pass's own resolution: traced where the adapter can,
// marched through the depth buffer where it cannot (Deferred/FlashlightShadow.slang). Read with a load at the
// pixel, so it needs no sampler. Always bound; gi.flashlight.control.x says whether this frame made one.
[[vk::binding(36, 0)]] Texture2D<float4> uFlashlightShadow;

struct Light
{
    // xyz view-space sun direction, w lighting model (0 baked, 1 hybrid, 2 dynamic).
    float4 sunDirectionModel;

    // rgb sun colour, w light scale.
    float4 sunColourScale;

    // rgb ambient, w debug view.
    float4 ambient;

    // x: the title draws its own sky, z: its bakes are whole, yw unused.
    float4 sky;

    // xyz world direction towards the moon, w how bright its disc is drawn (nought: no moon).
    float4 moon;

    // x how bright the brightest star is drawn (nought: no stars), y cloud cover, z seconds, w unused.
    float4 stars;
};

OPENIGI_PUSH_CONSTANT(Light, light);

typedef FullscreenVaryings Varyings;

// The full-screen triangle, generated from the vertex index so there is no vertex buffer to bind.
[shader("vertex")]
Varyings vertexMain(uint index : SV_VertexID)
{
    return FullscreenTriangle(index);
}

// How much sunlight survives the air between space and a point at this altitude, and whether the planet
// is in the way at all.
float3 SunTransmittance(float3 sunDir, float viewHeightKm)
{
    float3 planetRelative = float3(0.0, 0.0, viewHeightKm);
    float2 ground = RayIntersectSphere(planetRelative, sunDir, kBottomRadiusKm);
    if (ground.x > 0.0 || ground.y > 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    return DecodeTransmittance(
        uTransmittanceLut.SampleLevel(uTransmittanceLutSampler, TransmittanceParamsToUv(viewHeightKm, sunDir.z), 0.0).rgb);
}

// The sky's nine coefficients evaluated along a normal, already folded with the cosine convolution and
// the division by pi, so this is three dot products and nothing else.
float3 SkyDiffuseIrradiance(float3 worldNormal)
{
    float4 n = float4(worldNormal, 1.0);
    float3 linearPart = float3(
        dot(uSkyIrradiance.Load(int3(0, 0, 0)), n),
        dot(uSkyIrradiance.Load(int3(1, 0, 0)), n),
        dot(uSkyIrradiance.Load(int3(2, 0, 0)), n));

    float4 q = n.xyzz * n.yzzx;
    float3 quadraticPart = float3(
        dot(uSkyIrradiance.Load(int3(3, 0, 0)), q),
        dot(uSkyIrradiance.Load(int3(4, 0, 0)), q),
        dot(uSkyIrradiance.Load(int3(5, 0, 0)), q));

    float3 lastBand = uSkyIrradiance.Load(int3(6, 0, 0)).xyz * ((n.x * n.x) - (n.y * n.y));
    return max(float3(0.0, 0.0, 0.0), linearPart + quadraticPart + lastBand);
}

// The response of a surface to a single light. Both lobes come out already weighted by the clamped
// cosine between the normal and the light, and carry neither the light's colour nor its strength.
// Diffuse is Lambertian. Specular is a microfacet lobe: the Trowbridge-Reitz (GGX) normal distribution
// as Walter et al. 2007 state it, the height-correlated Smith visibility in the closed-form
// approximation Hammon gave at GDC 2017, and Schlick's 1994 Fresnel. Materials are described by
// metalness plus a specular level, following Karis 2013.
void EvaluateSurface(
    float3 baseColour,
    float metallic,
    float specular,
    float roughness,
    float3 n,
    float3 v,
    float3 l,
    out float3 diffuse,
    out float3 specularOut)
{
    // Metals tint their reflection with the base colour and scatter nothing diffusely. For everything
    // else the specular level maps linearly onto reflectance at normal incidence, so 0.5 gives 4%.
    float3 diffuseColour = baseColour * (1.0 - metallic);
    float3 f0 = lerp((0.08 * specular).xxx, baseColour, metallic);

    // Roughness never reaches zero, so a perfectly polished surface still has a lobe wide enough for a
    // light to fall into. Perceptual roughness is squared to get the distribution width.
    float perceptualRoughness = max(roughness, 0.02);
    float a = perceptualRoughness * perceptualRoughness;
    float aSquared = a * a;

    // Half vector between the view and light directions. When the two point exactly away from each
    // other their sum vanishes; the length is floored so h shrinks towards zero instead of dividing by
    // zero, which leaves both half-vector cosines at (or near) zero.
    float3 halfSum = v + l;
    float3 h = halfSum * rsqrt(max(dot(halfSum, halfSum), 1e-8));
    float cosNH = saturate(dot(n, h));
    float cosVH = saturate(dot(v, h));

    // Clamp the light and view cosines only after the half vector is built. The view cosine is nudged
    // off zero so silhouettes facing away from the camera still get a finite visibility term.
    float cosNL = saturate(dot(n, l));
    float cosNV = saturate(abs(dot(n, v)) + 1e-5);

    // Trowbridge-Reitz / GGX:  D = a^2 / (pi * ((n.h)^2 (a^2 - 1) + 1)^2)
    float ggxDenom = (cosNH * cosNH * (aSquared - 1.0)) + 1.0;
    float ndf = aSquared / (kPi * ggxDenom * ggxDenom);

    // Height-correlated Smith masking-shadowing folded with the 1 / (4 n.l n.v) microfacet factor:
    //   V = 0.5 / lerp(2 (n.l)(n.v), (n.l) + (n.v), a)
    float smithDenom = lerp(2.0 * cosNL * cosNV, cosNL + cosNV, a);
    float vis = 0.5 / max(smithDenom, 1e-6);

    // Schlick:  F = F0 + (F90 - F0) (1 - v.h)^5. F90 is not fixed at one: it scales with the green
    // channel of F0, so anything reflecting less than about 2% is read as cavity occlusion and loses
    // its grazing highlight.
    float f90 = saturate(50.0 * f0.g);
    float schlick = pow(1.0 - cosVH, 5.0);
    float3 fresnel = f0 + ((f90 - f0) * schlick);

    diffuse = diffuseColour * (cosNL / kPi);
    specularOut = ndf * vis * fresnel * cosNL;
}

// Analytic stand-in for the pre-integrated split-sum environment BRDF (Karis 2014, "Physically Based
// Shading on Mobile"). Returns the scale (x) and bias (y) applied to F0 for ambient specular, avoiding
// a lookup-table fetch.
float2 SplitSumApprox(float roughness, float cosNV)
{
    float smoothness = 1.0 - roughness;
    float grazing = min(smoothness * smoothness, exp2(-9.28 * cosNV));
    float t = (grazing * smoothness) + 0.0425 - (0.0275 * roughness);
    float scale = 1.04 - (0.572 * roughness) - (1.04 * t);
    float bias = (0.022 * roughness) - 0.04 + (1.04 * t);
    return float2(scale, bias);
}

float SpecularOcclusion(float noV, float alpha, float visibility)
{
    return clamp(pow(noV + visibility, alpha) - 1.0 + visibility, 0.0, 1.0);
}

// A surface does not absorb its own occlusion once: light bounces inside a crevice before it leaves, so a
// bright material stays brighter under occlusion than a dark one. Fit from Jimenez et al. 2016, GTAO
// multi-bounce.
float3 AoMultiBounce(float3 albedo, float visibility)
{
    float3 a = (2.0404 * albedo) - float3(0.3324, 0.3324, 0.3324);
    float3 b = float3(0.6417, 0.6417, 0.6417) - (4.7951 * albedo);
    float3 c = (2.7552 * albedo) + float3(0.6903, 0.6903, 0.6903);
    return max(float3(visibility, visibility, visibility), ((((visibility * a) + b) * visibility) + c) * visibility);
}

// Twelve points spread over the unit disc, for the wide filter below.
static const float2 kShadowDisc[12] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

// A per-pixel rotation, so the disc's twelve taps make a different pattern on every pixel and the
// temporal filter averages them into a smooth penumbra rather than twelve stamped copies of an edge.
float ShadowNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// The cascade's comparison at uv, filtered over radius texels. One texel and below is the plain 3x3; wider
// is the rotated disc, so the softness setting actually widens the penumbra -- it was read by nothing, and
// every preset got the same one-texel edge, stair-stepped across the far cascades.
float PcfShadow(int cascade, float3 uv, float radiusTexels, float2 pixel)
{
    float texel = gi.shadowConfig.w;
    float sum = 0.0;

    if (radiusTexels <= 1.5)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                sum += uShadowMap.SampleCmpLevelZero(uShadowMapSampler,
                    float3(uv.xy + (float2(float(x), float(y)) * texel), float(cascade)), uv.z);
            }
        }

        return sum / 9.0;
    }

    float angle = ShadowNoise(pixel) * 6.2831853;
    float s = sin(angle);
    float c = cos(angle);
    float reach = radiusTexels * texel;

    for (int i = 0; i < 12; i++)
    {
        float2 tap = kShadowDisc[i];
        float2 offset = float2((tap.x * c) - (tap.y * s), (tap.x * s) + (tap.y * c)) * reach;
        sum += uShadowMap.SampleCmpLevelZero(uShadowMapSampler, float3(uv.xy + offset, float(cascade)), uv.z);
    }

    return sum / 12.0;
}

// One cascade's answer for a point, or inside false where the point falls outside its box. The biases
// are scaled by the cascade's own texel size in world units, so one setting holds across every cascade
// instead of being tuned for the nearest; and the depth bias by the filter's width too, since a wider
// filter reaches further across a slope that faces the sun.
float CascadeVisibility(int i, float3 base, float3 worldNormal, float radiusTexels, float2 pixel, out bool inside)
{
    float4x4 m = gi.cascadeMatrix[i];

    // The scales are read out of the matrix's COLUMNS, which is what they were in the GLSL: m[0][0]
    // there indexes column zero, row zero. Slang indexes m[row][column], so the same three elements
    // are the first entry of each row.
    float xScale = length(float3(m[0][0], m[1][0], m[2][0]));
    float zScale = length(float3(m[0][2], m[1][2], m[2][2]));
    float texel = (2.0 * gi.shadowConfig.w) / max(xScale, 1e-6);
    float depthRange = 2.0 / max(zScale, 1e-6);

    float3 world = base + (worldNormal * (gi.shadowConfig.z * texel));
    float4 clip = mul(m, float4(world, 1.0));
    float3 ndc = clip.xyz / clip.w;
    inside = all(abs(ndc.xy) <= float2(1.0, 1.0));
    if (!inside)
    {
        return 1.0;
    }

    float bias = gi.shadowConfig.y * texel;
    float depth = (ndc.z * 0.5) + 0.5 - (bias / depthRange);

    // The filter's width is the nearest cascade's, held in metres: a penumbra is a size in the world, not
    // in a map's texels. Counted in each cascade's own texels it grew with the cascade, and three texels
    // of a far one is a third of a metre -- wide enough to dissolve a ledge's shadow on the wall below it
    // altogether, past the first cascade boundary.
    float4x4 nearest = gi.cascadeMatrix[0];
    float nearestTexel = (2.0 * gi.shadowConfig.w)
        / max(length(float3(nearest[0][0], nearest[1][0], nearest[2][0])), 1e-6);
    float filterTexels = max(radiusTexels * nearestTexel / max(texel, 1e-6), 1.0);
    float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));

    // And wider where the occluder stands further off. The sun is a disc a little over half a degree
    // across, so a shadow's edge blurs by the occluder's height above the receiver times the tangent of
    // its half-angle: a fence post's foot is razor sharp, the tip of a long building's shadow soft. The
    // occluders round the point are found in the map and their mean height taken, as the traced sun's
    // denoiser takes it from its hit distance; a fixed filter made every edge the same, so near shadows
    // read pasted on and far ones aliased.
    uint mapWidth;
    uint mapHeight;
    uint mapLayers;
    uShadowMap.GetDimensions(mapWidth, mapHeight, mapLayers);
    int2 mapSize = int2(int(mapWidth), int(mapHeight));

    // Searched as far as the penumbra of something thirty metres up would reach.
    const float kSunHalfAngleTangent = 0.00468;
    float searchTexels = clamp((30.0 * kSunHalfAngleTangent) / max(texel, 1e-6), 2.0, 16.0);
    float angle = ShadowNoise(pixel + float2(7.0, 3.0)) * 6.2831853;
    float s = sin(angle);
    float c = cos(angle);
    float blockerSum = 0.0;
    float blockers = 0.0;
    for (int k = 0; k < 12; k++)
    {
        float2 tap = kShadowDisc[k];
        float2 offset = float2((tap.x * c) - (tap.y * s), (tap.x * s) + (tap.y * c)) * (searchTexels * gi.shadowConfig.w);
        int2 texelAt = clamp(int2((uv + offset) * float2(mapSize)), int2(0, 0), mapSize - 1);
        float stored = uShadowMap.Load(int4(texelAt, i, 0));
        if (stored < depth)
        {
            blockerSum += depth - stored;
            blockers += 1.0;
        }
    }

    if (blockers > 0.0)
    {
        float heightMetres = (blockerSum / blockers) * depthRange;
        float penumbraTexels = (heightMetres * kSunHalfAngleTangent) / max(texel, 1e-6);
        filterTexels = clamp(max(filterTexels, penumbraTexels), 1.0, 24.0);
    }

    return PcfShadow(i, float3(uv, depth), filterTexels, pixel);
}

// Which cascade covers this pixel, and what it says. The last tenth of each slice is blended into the
// next cascade, and the last cascade's into full light, so neither a cascade boundary nor the shadow
// distance draws a line across the ground: the line used to be hard and to sit at a view DEPTH, so it
// swept across walls as the camera turned and walked, a shadow drawn half-way and growing as the player
// approached.
float SunVisibility(float3 viewPos, float3 worldNormal, float2 pixel)
{
    int count = int(gi.shadowConfig.x + 0.5);
    if (count <= 0)
    {
        return 1.0;
    }

    float radiusTexels = max(gi.probeSky.w, 1.0);
    float3 base = mul(gi.viewToWorld, float4(viewPos, 1.0)).xyz;
    for (int i = 0; i < 4; i++)
    {
        if (i >= count)
        {
            break;
        }

        if (viewPos.z > gi.cascadeSplits[i])
        {
            continue;
        }

        bool inside;
        float visibility = CascadeVisibility(i, base, worldNormal, radiusTexels, pixel, inside);
        if (!inside)
        {
            continue;
        }

        float sliceStart = i == 0 ? 0.0 : gi.cascadeSplits[i - 1];
        float band = max((gi.cascadeSplits[i] - sliceStart) * 0.1, 1e-3);
        float toNext = saturate((viewPos.z - (gi.cascadeSplits[i] - band)) / band);
        if (toNext > 0.0)
        {
            float next = 1.0;
            if (i + 1 < count)
            {
                bool nextInside;
                float further = CascadeVisibility(i + 1, base, worldNormal, radiusTexels, pixel, nextInside);
                next = nextInside ? further : 1.0;
            }

            visibility = lerp(visibility, next, toNext);
        }

        return visibility;
    }

    return 1.0;
}

// The view depth a stored depth stands for; the same reconstruction ViewPositionOf makes.
float ViewDepthOf(float depth)
{
    float ndcZ = 1.0 - (2.0 * depth);
    float near = gi.unproject.z;
    float far = gi.unproject.w;
    return (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
}

// A short march towards the sun through the depth buffer, for the contact a shadow map cannot resolve:
// the foot of a wall, a crate on the ground, a window ledge. A cascade's texel is centimetres near and
// decimetres far, and its biases lift every shadow off the thing casting it by about that much; the
// traced sun starts at the surface itself and has none of that gap. Only what is on screen can occlude
// here, which is why the reach is a fraction of a metre -- it finishes the shadow the cascade began, it
// does not replace it.
float ContactShadow(float3 viewPos, float3 viewSun, float reach, float2 pixel)
{
    if (reach <= 0.0 || viewSun.z <= -0.999)
    {
        return 1.0;
    }

    uint width;
    uint height;
    uDepth.GetDimensions(width, height);
    float2 size = float2(float(width), float(height));

    const int kSteps = 12;
    float3 stride = viewSun * (reach / float(kSteps));
    float3 at = viewPos + (stride * ShadowNoise(pixel + float2(17.0, 31.0)));

    // How far behind a depth sample a point may sit and still count as hidden by it: beyond this the ray
    // has gone behind a surface's edge rather than into it, and calling that occluded draws a halo.
    float thickness = max(0.3, 0.02 * viewPos.z);
    float startBias = 0.01 + (0.002 * viewPos.z);

    for (int step = 0; step < kSteps; step++)
    {
        at += stride;
        if (at.z <= gi.unproject.z)
        {
            break;
        }

        float2 uv = float2(
            0.5 + (0.5 * at.x * gi.unproject.x / at.z),
            0.5 - (0.5 * at.y * gi.unproject.y / at.z));
        if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)))
        {
            break;
        }

        float sample = uDepth.Load(int3(int2(uv * size), 0));
        if (sample <= 0.0)
        {
            continue;
        }

        float behind = at.z - ViewDepthOf(sample);
        if (behind > startBias && behind < thickness)
        {
            // Faded over the reach, so the contact darkens most where the occluder touches.
            return float(step) / float(kSteps);
        }
    }

    return 1.0;
}

float3 ViewPositionOf(float2 uv, float depth)
{
    float ndcZ = 1.0 - (2.0 * depth);
    float near = gi.unproject.z;
    float far = gi.unproject.w;
    float viewZ = (2.0 * near * far) / ((far + near) - (ndcZ * (far - near)));
    float2 ndc = (uv * 2.0) - 1.0;
    return float3((ndc.x * viewZ) / gi.unproject.x, (-ndc.y * viewZ) / gi.unproject.y, viewZ);
}

// The volumetric fog's froxel for this pixel, already lit and integrated: a transmittance in alpha and
// the light that scattered towards the eye in rgb.
float3 ApplyFog(float3 colour, float2 uv, float viewZ)
{
    if (gi.fog.z <= 0.0)
    {
        return colour;
    }

    float slice = log2(max((viewZ * gi.fog.x) + gi.fog.y, 1e-6)) * gi.fog.z * gi.fog.w;
    float4 fog = uFogVolume.SampleLevel(uFogVolumeSampler, float3(uv, clamp(slice, 0.0, 1.0)), 0.0);
    return (colour * fog.a) + fog.rgb;
}


// The half-resolution lamp visibility, upsampled by DEPTH rather than bilinearly: a tap that landed on
// another surface knows nothing about this pixel's lamp, and taking it blurs one room's shadow onto the
// next. Each of the four taps is weighted by how far its recorded view depth is from this pixel's, and a
// pixel none of them agree with takes the nearest outright rather than an average of four wrong answers.
// z is how much of the bilinear footprint agreed with this pixel's depth: one where the taps are all on
// this surface, near nought where every tap landed on something else -- a fence wire in front of it, say --
// and the answer is a neighbour's rather than this pixel's.
//
// And by normal: where a wall meets the floor the two stand at the same depth, so a tap on the wall passed
// the depth test and its lamp shadow bled half a texel onto the floor. Each tap's normal is read off the
// G-buffer at the full-resolution pixel the half-resolution texel stands over.
float3 UpsampleLampVisibility(float2 uv, float viewZ, float3 normal)
{
    uint fullWidth;
    uint fullHeight;
    uNormal.GetDimensions(fullWidth, fullHeight);
    int2 fullLast = int2(int(fullWidth) - 1, int(fullHeight) - 1);

    uint width;
    uint height;
    uLampVisibility.GetDimensions(width, height);
    float2 size = float2(float(width), float(height));

    float2 coord = (uv * size) - 0.5;
    float2 f = frac(coord);
    int2 base = int2(floor(coord));
    int2 last = int2(size) - 1;

    float4 bilinear = float4(
        (1.0 - f.x) * (1.0 - f.y),
        f.x * (1.0 - f.y),
        (1.0 - f.x) * f.y,
        f.x * f.y);

    float2 total = float2(0.0, 0.0);
    float totalWeight = 0.0;
    float2 nearest = float2(1.0, 0.0);
    float nearestGap = 1e30;

    for (int i = 0; i < 4; i++)
    {
        int2 at = clamp(base + int2(i & 1, i >> 1), int2(0, 0), last);
        float4 tap = uLampVisibility.Load(int3(at, 0));
        if (tap.z <= 0.0)
        {
            continue;
        }

        float gap = abs(tap.z - viewZ);
        if (gap < nearestGap)
        {
            nearestGap = gap;
            nearest = tap.xy;
        }

        int2 fullAt = clamp(int2((float2(at) + 0.5) * (float2(float(fullWidth), float(fullHeight)) / size)), int2(0, 0), fullLast);
        float3 tapNormal = DecodeGBufferNormal(uNormal.Load(int3(fullAt, 0)));
        float facing = saturate(dot(tapNormal, normal));

        float weight = bilinear[i] * exp2(-gap / max(0.03 * viewZ, 1e-3)) * facing * facing * facing * facing;
        total += tap.xy * weight;
        totalWeight += weight;
    }

    float2 visibility = totalWeight > 1e-4 ? total / totalWeight : nearest;
    return float3(visibility, saturate(totalWeight * 2.0));
}


static const float kMetresToKm = 0.001;

// The air between the camera and a surface: what it takes out of the surface's colour, and what it adds
// back from the sun scattering towards the eye along the same line.
float3 AerialPerspective(
    float3 colour,
    float3 worldRay,
    float3 sunDirection,
    float3 sunIlluminance,
    float distanceKm)
{
    const float kMieScatteringOnly = 0.003996;
    const float kMieAbsorptionOnly = 0.000444;
    float3 scattering = kRayleighScattering + float3(kMieScatteringOnly, kMieScatteringOnly, kMieScatteringOnly);
    float3 extinction = kRayleighScattering
        + float3(
            kMieScatteringOnly + kMieAbsorptionOnly,
            kMieScatteringOnly + kMieAbsorptionOnly,
            kMieScatteringOnly + kMieAbsorptionOnly);
    float3 transmittance = exp(-extinction * distanceKm);

    float cosTheta = dot(sunDirection, worldRay);
    float rayleighPhase = RayleighPhase(cosTheta);
    float miePhase = MiePhase(kMiePhaseG, -cosTheta);

    float3 inScattering =
        ((kRayleighScattering * rayleighPhase)
            + float3(kMieScatteringOnly * miePhase, kMieScatteringOnly * miePhase, kMieScatteringOnly * miePhase))
        * sunIlluminance
        * ((1.0 - transmittance) / max(extinction, float3(1e-6, 1e-6, 1e-6)));

    return (colour * transmittance) + inScattering;
}

// The weather, gathered out of this pass's own parameter block. The reflection pass fills the same struct
// from its uniform; that both do it by hand from different layouts is the whole reason the struct exists.
WetParams Weather()
{
    WetParams w;
    w.camera = gi.camera.xy;
    w.amount = max(gi.wetness0.x, 0.0);
    w.coverage = gi.wetness0.y;
    w.rippleStrength = gi.wetness0.z;
    w.seconds = gi.wetness0.w;
    w.puddleSize = gi.wetness1.x;
    w.rippleSize = gi.wetness1.y;
    w.windStrength = gi.wetness1.z;
    return w;
}

ShelterMap Roofs()
{
    ShelterMap m;
    m.origin = gi.shelterMap0.xy;
    m.invCell = gi.shelterMap0.z;
    m.lowestZ = gi.shelterMap0.w;
    m.scale = gi.shelterMap1.x;
    m.headroom = gi.shelterMap1.y;
    m.reach = gi.shelterMap1.z;
    m.size = gi.shelterMap2.xy;
    m.present = gi.wetness1.w;
    return m;
}

bool UnderShelter(float3 relative)
{
    ShelterMap m = Roofs();
    int2 at = ShelterTexel(m, relative.xy + gi.camera.xy);
    if (!ShelterHolds(m, at))
    {
        return false;
    }

    float surface = ShelterSurface(m, uShelterMap.Load(int3(at, 0)));
    return ShelterCovers(m, surface, relative.z + gi.camera.z);
}

// Rain, as it changes a surface: darker where it has soaked in, smoother where it stands, and rippled
// and reflective where it has pooled. The reflection pass asks the same functions of the same weather, so
// the two land on exactly the same water.
void ApplyWetness(
    float3 relative,
    float3 toEye,
    float footprint,
    inout float3 albedo,
    inout float3 normal,
    inout float roughness,
    out float waterMask,
    out float waterReflectance)
{
    waterMask = 0.0;
    waterReflectance = 0.0;

    WetParams w = Weather();
    if (w.amount <= 0.0 || UnderShelter(relative))
    {
        return;
    }

    float damp = DampAmount(w, relative, normal);
    float puddle = PuddleMask(w, relative, normal);
    waterMask = puddle;

    albedo *= lerp(1.0, 0.6, damp);
    roughness = DampRoughness(roughness, damp);

    if (puddle <= 0.0)
    {
        return;
    }

    albedo = lerp(albedo, albedo * 0.5, puddle);

    WaterSurface water = WaterSurfaceAt(w, relative, footprint);
    normal = normalize(lerp(normal, water.normal, puddle));
    roughness = lerp(roughness, water.roughness, puddle);

    waterReflectance = WaterFresnel(normal, toEye) * puddle;
    albedo *= 1.0 - waterReflectance;
}

// The sun's angular radius, in radians, and the most a pixel of its disc may carry: a half-float target
// holds no more than some sixty-five thousand, and the bloom and the temporal resolve want headroom over it.
static const float kSunAngularRadius = 0.00468;
static const float kSunDiscCeiling = 4000.0;

// The sun's disc where a view ray meets it: its illuminance over the solid angle it subtends, darkened
// towards its limb as the real one is, its edge softened over a pixel so it does not crawl, and never less
// than a pixel across.
float3 SunDisc(float3 worldRay, float3 sunDirection, float3 illuminance, float pixelAngle)
{
    float cosine = dot(worldRay, sunDirection);
    if (cosine <= 0.999 || sunDirection.z <= -0.02)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float angle = acos(saturate(cosine));
    float radius = max(kSunAngularRadius, pixelAngle);
    float cover = 1.0 - smoothstep(radius - (pixelAngle * 0.5), radius + (pixelAngle * 0.5), angle);
    if (cover <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float r = saturate(angle / radius);
    float limb = 0.4 + (0.6 * sqrt(max(1.0 - (r * r), 0.0)));
    float solidAngle = 6.2831853 * (1.0 - cos(radius));
    return min(illuminance / max(solidAngle, 1e-8), float3(kSunDiscCeiling, kSunDiscCeiling, kSunDiscCeiling))
        * (limb * cover);
}

// The irradiance a uniform straight emitter puts on a surface, per unit of the emitter's whole intensity:
// the integral along it of n.L / |L|^3, where L runs from the surface to each point of the emitter, from
// L0 at one end to L0 + D at the other. Closed form, since |L|^2 is a quadratic in the parameter t:
//
//   integral dt / Q^(3/2)   = (a t + b) / (det sqrt(Q))
//   integral t dt / Q^(3/2) = -(b t + c) / (det sqrt(Q))
//
// with Q = a t^2 + 2 b t + c, a = D.D, b = L0.D, c = L0.L0 and det = a c - b^2. The part of the emitter
// below the surface's horizon gives it nothing, so the interval is first cut to where n.L is positive.
// A surface the emitter lies along (det near nought) takes it as one point at the middle of what is left.
float LineIrradiance(float3 n, float3 lineStart, float3 lineDelta)
{
    float startFacing = dot(n, lineStart);
    float deltaFacing = dot(n, lineDelta);
    float t0 = 0.0;
    float t1 = 1.0;
    if (abs(deltaFacing) > 1e-6)
    {
        float crossing = -startFacing / deltaFacing;
        if (deltaFacing > 0.0)
        {
            t0 = max(t0, crossing);
        }
        else
        {
            t1 = min(t1, crossing);
        }
    }
    else if (startFacing <= 0.0)
    {
        return 0.0;
    }

    if (t1 <= t0)
    {
        return 0.0;
    }

    float a = dot(lineDelta, lineDelta);
    float b = dot(lineStart, lineDelta);
    float c = dot(lineStart, lineStart);
    float det = (a * c) - (b * b);
    if (det <= 1e-5 * a * c)
    {
        float3 middle = lineStart + (lineDelta * (0.5 * (t0 + t1)));
        float distanceSq = max(dot(middle, middle), 1e-4);
        return saturate(dot(n, middle) * rsqrt(distanceSq)) / distanceSq * (t1 - t0);
    }

    float q0 = sqrt(max((a * t0 * t0) + (2.0 * b * t0) + c, 1e-8));
    float q1 = sqrt(max((a * t1 * t1) + (2.0 * b * t1) + c, 1e-8));
    float at1 = ((startFacing * ((a * t1) + b)) - (deltaFacing * ((b * t1) + c))) / (det * q1);
    float at0 = ((startFacing * ((a * t0) + b)) - (deltaFacing * ((b * t0) + c))) / (det * q0);
    return max(at1 - at0, 0.0);
}

// One lamp's light on this surface towards the eye, before anything stands in its way, and the surface's
// diffuse share of it for the ranking. Nought for a lamp out of reach, behind its own glass, or below the
// surface's horizon.
//
// A fitting with a measured face is lit as the tube it is: the irradiance integrated along its length (see
// LineIrradiance), so a long batten throws a long pool rather than a round one and the wall beside it
// takes the spread-out glow a tube gives; and the highlight is taken at the point along it nearest the
// reflected view ray, with the lobe widened by the tube's own thickness at that distance, so a wet floor
// shows a stretched streak rather than a pinpoint. A point lamp is a small sphere: the inverse square stops
// rising inside its own radius, rather than being softened by a metre everywhere -- which flattened every
// pool into a disc with a window-shaped edge and took the hot centre out of it.
float3 LampShade(
    int i,
    float3 viewPos,
    float3 normal,
    float3 toEye,
    float3 albedo,
    float metallic,
    float specularLevel,
    float roughness,
    out float diffuseWeight)
{
    diffuseWeight = 0.0;

    float4 positionRadius = lamps.positionRadius[i];
    float3 toLightView = positionRadius.xyz - viewPos;
    float distanceSq = dot(toLightView, toLightView);
    float reach = positionRadius.w;
    if (distanceSq >= reach * reach)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 toCentre = mul(gi.viewToWorld, float4(toLightView, 0.0)).xyz;
    float3 centreDir = toCentre * rsqrt(max(distanceSq, 1e-8));

    float ratio = distanceSq / max(reach * reach, 1e-4);
    float window = clamp(1.0 - (ratio * ratio), 0.0, 1.0);
    window *= window;

    float4 facing = lamps.direction[i];
    if (dot(facing.xyz, facing.xyz) > 0.25)
    {
        float3 axis = mul(gi.viewToWorld, float4(facing.xyz, 0.0)).xyz;
        window *= max(dot(normalize(axis), -centreDir), 0.0);
    }

    if (window <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 colour = lamps.colourIntensity[i].rgb * lamps.colourIntensity[i].a * window;
    float source = clamp(facing.w, 0.05, 1.0);

    float4 rect = lamps.rect[i];
    float3 halfAxis = mul(gi.viewToWorld, float4(rect.xyz, 0.0)).xyz;
    bool tube = dot(halfAxis, halfAxis) > 1e-4;

    // The diffuse irradiance, per unit of the lamp's intensity.
    float irradiance;
    float3 toSpecular = toCentre;
    float thickness = source;
    if (tube)
    {
        float3 lineStart = toCentre - halfAxis;
        float3 lineDelta = halfAxis * 2.0;
        irradiance = LineIrradiance(normal, lineStart, lineDelta);

        // Where along the tube the reflected view ray passes closest: the point whose light the highlight
        // is made of.
        float3 reflected = reflect(-toEye, normal);
        float along = dot(reflected, lineDelta);
        float denominator = dot(lineDelta, lineDelta) - (along * along);
        float t = denominator > 1e-6
            ? ((dot(reflected, lineStart) * along) - dot(lineStart, lineDelta)) / denominator
            : 0.5;
        toSpecular = lineStart + (lineDelta * saturate(t));
        thickness = clamp(rect.w, 0.02, source);
    }
    else
    {
        irradiance = saturate(dot(normal, centreDir)) / max(distanceSq, source * source);
    }

    float specularDistanceSq = max(dot(toSpecular, toSpecular), 1e-6);
    float3 specularDir = toSpecular * rsqrt(specularDistanceSq);

    diffuseWeight = dot(colour, float3(0.2126, 0.7152, 0.0722)) * irradiance;
    if (irradiance <= 0.0 && dot(normal, specularDir) <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    // The highlight: the lobe widened by the angle the source spans from here, and renormalised so the
    // wider lobe carries the same energy rather than more.
    float alpha = max(roughness, 0.02) * max(roughness, 0.02);
    float widened = saturate(alpha + (thickness / (2.0 * sqrt(specularDistanceSq))));
    float normalisation = (alpha / widened) * (alpha / widened);

    float3 lobeDiffuse;
    float3 lobeSpecular;
    EvaluateSurface(
        albedo,
        metallic,
        specularLevel,
        sqrt(widened),
        normal,
        toEye,
        specularDir,
        lobeDiffuse,
        lobeSpecular);

    float3 diffuseColour = albedo * (1.0 - metallic);
    float specularIrradiance = 1.0 / max(specularDistanceSq, thickness * thickness);
    return colour * ((diffuseColour * irradiance) + (lobeSpecular * normalisation * specularIrradiance * kPi));
}

// How much of the flashlight gets past what stands in its way at this pixel: one where no mask was made.
float FlashlightShadowAt(float4 position)
{
    if (gi.flashlight.control.x <= 0.5)
    {
        return 1.0;
    }

    return saturate(uFlashlightShadow.Load(int3(int2(position.xy), 0)).r);
}

// The flashlight's irradiance on this surface along its normal, shadowed, and the world direction to it. The
// same convention as a lamp's radiance: EvaluateSurface's lobes times this times pi.
float3 FlashlightReaching(float3 viewPos, float3 normal, float4 position, out float3 lightDir)
{
    lightDir = float3(0.0, 0.0, 1.0);
    if (!FlashlightOn(gi.flashlight))
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 toLightView;
    float distance;
    float3 irradiance = FlashlightIrradiance(gi.flashlight, viewPos, true, toLightView, distance);
    if (all(irradiance <= float3(0.0, 0.0, 0.0)))
    {
        return float3(0.0, 0.0, 0.0);
    }

    lightDir = normalize(mul(gi.viewToWorld, float4(toLightView, 0.0)).xyz);
    if (dot(normal, lightDir) <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    return irradiance * FlashlightShadowAt(position);
}

[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float2 vUv = input.uv;

    // One pixel's width in UV, taken before anything branches: a derivative is only defined where every
    // pixel of the quad takes the same path, and WGSL refuses one anywhere else.
    float uvPixelWidth = fwidth(vUv.x);

    float4 baseColour = OPENIGI_GBUFFER(uBaseColour);
    float3 albedo = baseColour.rgb;
    float4 material = OPENIGI_GBUFFER(uMaterial);
    float3 baked = OPENIGI_GBUFFER(uBaked).rgb;
    float depth = OPENIGI_GBUFFER(uDepth).r;

    float viewHeightKm = gi.giParams.w;
    int debugView = int(light.ambient.w + 0.5);

    if (depth <= 0.0)
    {
        if (debugView != 0 && debugView != 11)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }

        if (light.sky.x > 0.5)
        {
            return float4(
                ApplyFog(albedo * DecodeEmissiveStrength(material), vUv, gi.unproject.w), 1.0);
        }

        float2 ndc = (vUv * 2.0) - 1.0;
        float3 viewRay = normalize(float3(ndc.x / gi.unproject.x, -ndc.y / gi.unproject.y, 1.0));
        float3 worldRay = normalize(mul(gi.viewToWorld, float4(viewRay, 0.0)).xyz);
        float3 background = SampleSkyBackground(uSkyView, uSkyViewSampler, worldRay, viewHeightKm);

        // The sun's own disc, which the sky's table is far too coarse to hold. Its radiance is the
        // sunlight spread over the little solid angle the disc subtends -- some ten thousand times the
        // sky round it -- so it is what glares, blooms, and throws rays through every gap in front of
        // it; without it the sun was a soft patch of haze and nothing in front of it cast a ray.
        float sunPixelAngle = (2.0 * uvPixelWidth) / max(gi.unproject.x, 1e-4);
        background += SunDisc(
            worldRay,
            normalize(mul(gi.viewToWorld, float4(light.sunDirectionModel.xyz, 0.0)).xyz),
            light.sunColourScale.rgb * light.sunColourScale.w,
            sunPixelAngle);

        // The stars and the moon over the air, dimmed by as much of it as their light crosses. A pixel's
        // angular width at the centre of the view keeps every star at least a pixel across.
        if (light.stars.x > 0.0 || light.moon.w > 0.0)
        {
            float pixelAngle = (2.0 * uvPixelWidth) / max(gi.unproject.x, 1e-4);
            background += NightSkySources(
                worldRay,
                normalize(light.moon.xyz),
                light.moon.w,
                light.stars.x,
                light.stars.y,
                light.stars.z,
                pixelAngle,
                SunTransmittance(worldRay, viewHeightKm));
        }

        return float4(ApplyFog(background, vUv, gi.unproject.w), 1.0);
    }

    if (DecodeShadingModel(material) == kShadingModelUnlit)
    {
        if (debugView != 0 && debugView != 11)
        {
            return (debugView == 1) ? float4(albedo, 1.0) : float4(0.0, 0.0, 0.0, 1.0);
        }

        return float4(
            ApplyFog(
                albedo * DecodeEmissiveStrength(material), vUv, ViewPositionOf(vUv, depth).z),
            1.0);
    }

    float3 normal = DecodeGBufferNormal(OPENIGI_GBUFFER(uNormal));
    float scale = light.sunColourScale.w;
    int model = int(light.sunDirectionModel.w + 0.5);

    if (debugView == 1) { return float4(albedo, 1.0); }
    if (debugView == 2) { return float4((normal * 0.5) + 0.5, 1.0); }
    if (debugView == 3)
    {
        float viewZ = ViewPositionOf(vUv, depth).z;
        float ramp = pow(clamp(viewZ / 400.0, 0.0, 1.0), 0.4545);
        return float4(ramp, ramp, ramp, 1.0);
    }

    if (debugView == 4) { return float4(material.b, material.r, material.a, 1.0); }
    if (debugView == 6) { return float4(baked, 1.0); }
    if (debugView == 7)
    {
        float ao = uOcclusion.SampleLevel(uOcclusionSampler, vUv, 0.0).r;
        return float4(ao, ao, ao, 1.0);
    }

    if (debugView == 8) { return float4(uScreenGi.SampleLevel(uScreenGiSampler, vUv, 0.0).rgb * gi.giParams.x, 1.0); }
    if (debugView == 18) { return float4(material.b, material.b, material.b, 1.0); }
    if (debugView == 19) { return float4(material.r, material.r, material.r, 1.0); }
    if (debugView == 20) { return float4(baseColour.a, baseColour.a, baseColour.a, 1.0); }
    if (debugView == 11) { albedo = float3(0.18, 0.18, 0.18); }
    if (debugView == 28)
    {
        float torch = FlashlightShadowAt(input.position);
        return float4(torch, torch, torch, 1.0);
    }

    float3 viewPos = ViewPositionOf(vUv, depth);

    // The flashlight, which the baked and hybrid models light with too: it is the player's own light, and
    // a level still warming up its GI should not see it blink out. Diffuse only there, as their sun is.
    float3 flashDir;
    float3 flashIrradiance = FlashlightReaching(viewPos, normal, input.position, flashDir);
    float3 flashDiffuse = albedo * flashIrradiance * max(dot(normal, flashDir), 0.0);

    if (model == 0)
    {
        // Baked.
        return float4(ApplyFog((albedo * baked) + flashDiffuse, vUv, viewPos.z), 1.0);
    }

    float3 sunDirection = normalize(mul(gi.viewToWorld, float4(light.sunDirectionModel.xyz, 0.0)).xyz);
    float ndotl = max(dot(normal, sunDirection), 0.0);

    float visibility = 1.0;
    if (ndotl > 0.0 || debugView == 12)
    {
        if (gi.probeMisc.w > 0.5)
        {
            visibility = uSunShadow.SampleLevel(uSunShadowSampler, vUv, 0.0).r;
        }
        else
        {
            visibility = SunVisibility(viewPos, normal, input.position.xy);
            if (visibility > 0.0 && gi.shadowConfig.x > 0.5)
            {
                float3 viewSun = mul(float4(sunDirection, 0.0), gi.viewToWorld).xyz;
                visibility *= ContactShadow(viewPos, viewSun, gi.camera.w, input.position.xy);
            }
        }
    }

    if (debugView == 12) { return float4(visibility, visibility, visibility, 1.0); }

    if (model == 1)
    {
        float3 sun = light.sky.z > 0.5
            ? float3(0.0, 0.0, 0.0)
            : light.sunColourScale.rgb * ndotl * visibility;

        // Hybrid.
        return float4(ApplyFog((albedo * (baked + sun) * scale) + flashDiffuse, vUv, viewPos.z), 1.0);
    }

    float metallic = material.r;
    float specularLevel = material.g;
    float roughness = material.b;

    float3 worldView = normalize(-mul(gi.viewToWorld, float4(viewPos, 0.0)).xyz);
    float3 worldRayToPixel = -worldView;

    float waterMask = 0.0;
    float waterReflectance = 0.0;
    // A matte surface -- a body -- takes no rain: the puddle mask is a slope test and a height field,
    // and the top of a helmet is level enough to pass both, so a soldier in the rain grew a mirror of
    // standing water on his head and shoulders and the specular it restores.
    if (gi.wetness0.x > 0.0 && !DecodeMatte(material))
    {
        float footprint = WetFootprint(
            uvPixelWidth * 2.0,
            viewPos.z,
            gi.unproject.x,
            max(dot(normal, worldView), 1e-3));

        ApplyWetness(
            mul(gi.viewToWorld, float4(viewPos, 1.0)).xyz,
            worldView,
            footprint,
            albedo,
            normal,
            roughness,
            waterMask,
            waterReflectance);

        specularLevel = max(specularLevel, 0.5 * waterMask);
    }

    if (debugView == 13)
    {
        return float4(waterMask, (normal.xy * 0.5) + 0.5, 1.0);
    }

    // The sun as the host hands it, which is already what the air lets through to the ground at this hour
    // (DayNightCycle: the path's transmittance relative to a sun thirty degrees up, where the authored
    // colour is read). Taking the air's transmittance off a second time here dimmed and reddened every low
    // sun twice over -- a dusk that read muddy instead of warm -- and set the lit walls apart from the fog,
    // the cache and the traced light, which all take the host's colour as it is.
    float3 sunIlluminance = light.sunColourScale.rgb * scale;

    float3 surfaceDiffuse;
    float3 surfaceSpecular;
    EvaluateSurface(
        albedo,
        metallic,
        specularLevel,
        roughness,
        normal,
        worldView,
        sunDirection,
        surfaceDiffuse,
        surfaceSpecular);

    float3 sunLight = (surfaceDiffuse + surfaceSpecular) * sunIlluminance * visibility;

    if (waterReflectance > 0.0)
    {
        float3 reflected = normalize(reflect(worldRayToPixel, normal));
        sunLight += SampleSkyView(uSkyView, uSkyViewSampler, reflected, viewHeightKm)
            * waterReflectance * WaterSkyward(reflected);
    }

    int lampCount = int(lamps.control.x + 0.5);

    float3 lampShadow = UpsampleLampVisibility(vUv, viewPos.z, normal);

    // What stands between this pixel and its lamps: the traced ratio of its shadowed lamp light to its
    // unshadowed lamp light, estimated by the lamp trace from a few rays drawn in proportion to what each
    // lamp is worth here and then denoised. Every lamp's unshadowed light is computed below, exactly and
    // at full resolution, and multiplied by it (the ratio estimator: see Lamp/Shadow.slang).
    //
    // It used to be read as lerp(1, visible, share) -- one sample's verdict applied only to that sample's
    // share of the light, and every lamp the sample did not cover counted as unobstructed. On the outside
    // wall of a sealed building with four lamps inside, three-quarters of the interior's light came
    // through the wall on every frame. Read as the ratio it is, the blocked lamps cost the wall their
    // light.
    bool lampShadows = lamps.control.y > 0.5;
    float lampOcclusion = lampShadows ? clamp(lampShadow.x, 0.0, 1.0) : 1.0;

    if (debugView == 29)
    {
        return float4(saturate(lampShadow.x), lampShadow.y > 0.004 ? saturate(lampShadow.y) : 0.0, 0.0, 1.0);
    }

    if (debugView == 26)
    {
        return lampShadows && lampShadow.y <= 0.0
            ? float4(0.6, 0.0, 0.0, 1.0)
            : float4(lampOcclusion, lampOcclusion, lampOcclusion, 1.0);
    }

    float3 lampLight = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < lampCount; i++)
    {
        float lampWeight;
        float3 lampTerm = LampShade(
            i, viewPos, normal, worldView, albedo, metallic, specularLevel, roughness, lampWeight) * lampOcclusion;
        if (all(lampTerm <= float3(0.0, 0.0, 0.0)))
        {
            continue;
        }

        sunLight += lampTerm;
        lampLight += lampTerm;
    }

    if (debugView == 14) { return float4(lampLight, 1.0); }

    // The flashlight, through the full surface -- so a wet floor or a gun-metal crate throws its hot spot
    // back -- and into sunLight, which is what the screen gather reads next frame: the beam bounces off a
    // red wall onto the floor beside it with nothing more said.
    float3 flashLight = float3(0.0, 0.0, 0.0);
    if (any(flashIrradiance > float3(0.0, 0.0, 0.0)))
    {
        float3 flashDiffuseLobe;
        float3 flashSpecularLobe;
        EvaluateSurface(
            albedo,
            metallic,
            specularLevel,
            roughness,
            normal,
            worldView,
            flashDir,
            flashDiffuseLobe,
            flashSpecularLobe);

        flashLight = (flashDiffuseLobe + flashSpecularLobe) * flashIrradiance * kPi;
        sunLight += flashLight;
    }

    if (debugView == 27) { return float4(flashLight, 1.0); }

    float3 diffuseColour = albedo - (albedo * metallic);
    bool gathered = gi.giParams.x > 0.0;
    float3 indirect = light.ambient.rgb;

    if ((!gathered || debugView == 9) && gi.probeOrigin.w > 0.5)
    {
        float3 worldPos = mul(gi.viewToWorld, float4(viewPos, 1.0)).xyz + gi.camera.xyz;
        float3 worldNormal = normal;
        float3 lookup = worldPos + (worldNormal * gi.probeMisc.x);
        float3 grid = (lookup - gi.probeOrigin.xyz) / gi.probeInvCount.w;
        float3 uvw = (grid + 0.5) * gi.probeInvCount.xyz;

        if (all(uvw >= float3(0.0, 0.0, 0.0)) && all(uvw <= float3(1.0, 1.0, 1.0)))
        {
            float4 sh = float4(1.0, worldNormal.x, worldNormal.y, worldNormal.z);
            float3 probeLight = float3(
                max(dot(uProbeRed.SampleLevel(uProbeRedSampler, uvw, 0.0), sh), 0.0),
                max(dot(uProbeGreen.SampleLevel(uProbeGreenSampler, uvw, 0.0), sh), 0.0),
                max(dot(uProbeBlue.SampleLevel(uProbeBlueSampler, uvw, 0.0), sh), 0.0));

            if (debugView == 9)
            {
                return float4(probeLight * gi.probeSky.rgb, 1.0);
            }

            // Chebyshev against what the probe could see: a probe on the wrong side of the geometry it
            // is meant to describe is what leaks light through a wall.
            float2 moments = uProbeDistance.SampleLevel(uProbeDistanceSampler, uvw, 0.0).xy;
            float variance = max(moments.y - (moments.x * moments.x), 1e-4);
            //
            // Measured from the probe that dominates the sample, the nearest grid point to the lookup, to
            // the surface. It was measured from the lookup to the surface, which is the normal offset
            // twice over and the same everywhere, so the test compared a constant against the probe's
            // depth and never turned a probe away.
            float3 nearestProbe = gi.probeOrigin.xyz + (round(grid) * gi.probeInvCount.w);
            float toward = length(worldPos - nearestProbe);
            float trust = 1.0;
            if (toward > moments.x)
            {
                float difference = toward - moments.x;
                trust = variance / (variance + (difference * difference));
                trust = trust * trust * trust;
            }

            indirect = lerp(
                light.ambient.rgb, probeLight * gi.probeSky.rgb, trust * gi.probeMisc.y);
        }
    }

    if (debugView == 9) { return float4(0.0, 0.0, 0.0, 1.0); }

    if (gathered)
    {
        indirect = uScreenGi.SampleLevel(uScreenGiSampler, vUv, 0.0).rgb * gi.giParams.x;
    }

    float ambientVisibility = baseColour.a;
    if (gi.giParams.z > 0.5)
    {
        ambientVisibility *= uOcclusion.SampleLevel(uOcclusionSampler, vUv, 0.0).r;
    }

    float3 environment = indirect;
    if (ambientVisibility < 1.0)
    {
        float3 occlusion = AoMultiBounce(albedo, ambientVisibility);
        indirect *= (gi.probeOrigin.w > 0.5 && !gathered)
            ? lerp(float3(1.0, 1.0, 1.0), occlusion, 0.5)
            : occlusion;
    }

    float noVSpecular = clamp(dot(normal, worldView), 1e-4, 1.0);
    float3 f0 = lerp(
        float3(0.08 * specularLevel, 0.08 * specularLevel, 0.08 * specularLevel), albedo, metallic);
    float2 environmentBrdf = SplitSumApprox(roughness, noVSpecular);
    float f90 = clamp(50.0 * f0.g, 0.0, 1.0);
    float alpha = roughness * roughness;

    float3 ambientSpecular =
        environment
        * 0.3183098862
        * ((f0 * environmentBrdf.x) + float3(f90 * environmentBrdf.y, f90 * environmentBrdf.y, f90 * environmentBrdf.y))
        * SpecularOcclusion(noVSpecular, alpha, ambientVisibility);

    // Where the reflection pass answers this pixel with the environment itself, it stands in place of
    // this estimate rather than on top of it; faded over the same top quarter of its roughness range the
    // reflection fades over, so the two hand over without a line. See WithReflectionCoverage.
    float reflectionCeiling = gi.shelterMap2.z;
    if (reflectionCeiling > 0.0)
    {
        float covered = clamp((reflectionCeiling - roughness) / max(reflectionCeiling * 0.25, 1e-3), 0.0, 1.0);
        ambientSpecular *= 1.0 - covered;
    }

    if (gathered)
    {
        float3 litDynamic = (diffuseColour * 0.3183098862 * indirect) + ambientSpecular + sunLight;

        // Dynamic.
        return float4(
            ApplyFog(
                AerialPerspective(
                    litDynamic, worldRayToPixel, sunDirection, sunIlluminance, viewPos.z * kMetresToKm),
                vUv,
                viewPos.z),
            1.0);
    }

    float3 skyLight = SkyDiffuseIrradiance(normal) * diffuseColour;
    float3 litLegacy = (albedo * indirect * scale) + skyLight + ambientSpecular + sunLight;

    // Dynamic.
    return float4(
        ApplyFog(
            AerialPerspective(
                litLegacy, worldRayToPixel, sunDirection, sunIlluminance, viewPos.z * kMetresToKm),
            vUv,
            viewPos.z),
        1.0);
}
