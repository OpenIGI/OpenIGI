// The sun's shadow as the lighting pass reads it from the cascades: the filtered comparison, the penumbra
// widened by how far the occluder stands off, the blend from one cascade into the next, and the short march
// through the depth buffer that finishes the contact a cascade's biases leave open.
//
// The host must include this after declaring its Gi block as `gi` (the cascades' matrices, splits and
// shadow configuration, the probe sky's filter radius, the view-to-world rotation, the unprojection and the
// contact reach in camera.w), the cascades as uShadowMap with uShadowMapSampler, and the depth as uDepth.

#ifndef OPENIGI_SUN_SHADOW_CASCADES_INCLUDED
#define OPENIGI_SUN_SHADOW_CASCADES_INCLUDED

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

#endif
