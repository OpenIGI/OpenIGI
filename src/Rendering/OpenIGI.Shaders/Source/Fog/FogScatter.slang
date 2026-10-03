// What the medium is at each froxel, and how much light it sends at the eye.

#include "../Common/FogParams.slang"

// A depth image, and declared as one: WebGPU binds a depth image to nothing else, and it is only ever
// fetched here, never filtered.
[[vk::binding(0, 0)]] DepthTexture2DArray uShadowMap;

// The sky's harmonics, fetched by texel.
[[vk::binding(2, 0)]] Texture2D<float4> uSkyIrradiance;

[[vk::binding(4, 0)]] Texture3D<float4> uHistory;
[[vk::binding(5, 0)]] SamplerState uHistorySampler;

[[vk::binding(6, 0)]] [[vk::image_format("rgba16f")]] WTexture3D<float4> uScattering;

// The level's roofs, seen from above, exactly as the shading and reflection passes read them.
// Fetched and never sampled, so the sampler bound with it is never consulted.
[[vk::binding(7, 0)]] Texture2D<float4> uShelterMap;

// Henyey-Greenstein. The reason a fog bank glows when you face the sun and goes flat when you turn
// away: real droplets scatter forward far more than backward, and a medium without this is a uniform
// grey filter wherever the light is.
float PhaseHG(float cosTheta, float g)
{
    float gg = g * g;
    float denom = 1.0 + gg - (2.0 * g * cosTheta);
    return (1.0 - gg) / (4.0 * kPi * pow(max(denom, 1e-4), 1.5));
}

// A single lobe is what light does on ONE bounce, and this grid only ever models one. At g = 0.85
// that lobe peaks at eighty-two times isotropic and falls to a twenty-fifth of it looking away, so
// the fog would be blinding towards the sun and invisible away from it. Real fog is not, because
// after a few bounces the light has been spread everywhere. Blending towards isotropic stands in for
// the multiple scattering: the forward glow survives and the medium gains a floor.
float Phase(float cosTheta)
{
    const float isotropic = 1.0 / (4.0 * kPi);
    return lerp(PhaseHG(cosTheta, p.medium.w), isotropic, p.march.y);
}

// Rooms, basements, smoke: where the level says the air differs from its own outdoor medium. The
// fade towards each edge is not decoration -- a hard boundary reads as a plane hanging in the air the
// moment the camera walks through it, which is worse than having no volume at all.
float VolumeScaleAt(float3 world)
{
    const float kEdgeSoftness = 0.7;

    float scale = 1.0;
    for (int i = 0; i < OPENIGI_MAX_FOG_VOLUMES; i++)
    {
        if (p.volumeExtent[i].x <= 0.0)
        {
            continue;
        }

        float3 delta = abs(world - p.volumeCentre[i].xyz);
        float reach = p.volumeCentre[i].w > 0.5
            ? length(delta) / max(p.volumeExtent[i].x, 1e-3)
            : max(delta.x / max(p.volumeExtent[i].x, 1e-3),
                max(delta.y / max(p.volumeExtent[i].y, 1e-3),
                    delta.z / max(p.volumeExtent[i].z, 1e-3)));

        float inside = 1.0 - smoothstep(kEdgeSoftness, 1.0, reach);
        scale = lerp(scale, p.volumeExtent[i].w, inside);
    }

    return scale;
}

// Whether this piece of air is indoors, read off the level's top-down map of what the solid world
// stands over -- the same raster the standing water and the falling drops are decided by.
//
// Per froxel and not per frame. The whole march used to be switched off while the camera was inside
// a building, because fog sitting between the eye and a window washed out the yard beyond it.
// Switching it off answers that by deleting the yard's fog as well. Answering it a froxel at a time
// leaves the room dry AND the yard hazy, which is what a person in a doorway actually sees.
bool Indoors(float3 world)
{
    if (p.shelter1.w < 0.5)
    {
        return false;
    }

    float3 absolute = world + p.cameraWorld.xyz;
    int2 at = int2(floor((absolute.xy - p.shelter0.xy) * p.shelter0.z));
    if (at.x < 0 || at.y < 0 || at.x >= int(p.shelter2.x) || at.y >= int(p.shelter2.y))
    {
        return false;
    }

    float4 texel = uShelterMap.Load(int3(at, 0));
    float packed = floor((texel.b * 255.0) + 0.5) + (floor((texel.g * 255.0) + 0.5) * 256.0);
    if (packed < 0.5)
    {
        return false;
    }

    float surface = p.shelter0.w + ((packed - 1.0) * p.shelter1.x);
    return surface > absolute.z + p.shelter1.y && surface < absolute.z + p.shelter1.z;
}

// Extinction per metre where this froxel is. The height term is what makes fog POOL: without it a
// medium is uniform and reads as a flat wash rather than as air. The uniform fraction is the floor
// under it, because real air hazes the distance at altitude too.
//
// Nothing indoors. A sealed room has no haze source, and a level's interiors are lit for a medium
// that is not there.
float DensityAt(float3 world)
{
    if (Indoors(world))
    {
        return 0.0;
    }

    float height = world.z - p.medium.z;
    float banded = exp(-max(height, 0.0) * p.medium.y);
    float profile = lerp(banded, 1.0, p.march.x);
    return max(p.medium.x * profile * VolumeScaleAt(world), 0.0);
}

// The sky's light on a surface facing a direction, from the harmonics the sky was reduced to -- the
// same nine the deferred lighting reads, evaluated the same way.
float3 SkyIrradianceAt(float3 direction)
{
    float4 n = float4(direction, 1.0);
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

// How much sun reaches a froxel, from the cascade whose depth range contains it. Fetched rather than
// filtered: the map is a depth format, and a fetch needs no filtering support from it. Four taps,
// because a froxel is large and one tap makes its boundary a staircase in the air.
//
// FADED OUT AT THE END OF THE CASCADES, and that is not a refinement. The grid is marched to the
// fog's own distance, which is further than the sun's shadows are drawn. Past the last cascade there
// is no shadow information at all, and answering "lit" there put a PLANE in the air where the
// cascades stop: every ray crossing it went from shadowed to sunlit in one step, and since a sky
// pixel integrates the whole column, that plane was drawn across the sky as a hard-edged wedge.
float SunVisibility(float3 viewPos, float3 world)
{
    int count = int(p.albedo.w + 0.5);
    if (count <= 0)
    {
        return 1.0;
    }

    float last = p.cascadeSplits[clamp(count - 1, 0, 3)];
    float beyond = last > 0.0
        ? clamp((viewPos.z - (last * 0.55)) / max(last * 0.45, 1e-3), 0.0, 1.0)
        : 1.0;
    if (beyond >= 1.0)
    {
        return 1.0;
    }

    for (int i = 0; i < 4; i++)
    {
        if (i >= count || viewPos.z > p.cascadeSplits[i])
        {
            continue;
        }

        float4 clip = mul(p.cascade[i], float4(world, 1.0));
        float3 ndc = clip.xyz / clip.w;
        if (any(abs(ndc.xy) > float2(1.0, 1.0)))
        {
            continue;
        }

        float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));
        float depth = (ndc.z * 0.5) + 0.5 - p.sunColour.w;

        uint mapWidth;
        uint mapHeight;
        uint mapLayers;
        uShadowMap.GetDimensions(mapWidth, mapHeight, mapLayers);
        float2 mapSize = float2(mapWidth, mapHeight);

        float2 texel = (uv * mapSize) - 0.5;
        int2 base = int2(floor(texel));
        float2 f = frac(texel);

        float sum = 0.0;
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                int2 at = clamp(base + int2(x, y), int2(0, 0), int2(mapSize) - 1);
                float stored = uShadowMap.Load(int4(at, i, 0));
                float lit = depth <= stored ? 1.0 : 0.0;
                sum += lit * lerp(1.0 - f.x, f.x, float(x)) * lerp(1.0 - f.y, f.y, float(y));
            }
        }

        return lerp(sum, 1.0, beyond);
    }

    return 1.0;
}

// Where this froxel was on the previous frame's grid, as a coordinate into the history volume.
// Camera-relative throughout: the world position is never formed, only the offset from this frame's
// camera and then the offset from the previous one -- this world's coordinates run to tens of
// thousands of metres, and differencing two of them in single precision would throw away most of the
// digits of a difference that is often a fraction of one.
bool HistoryCoordinate(float3 world, out float3 uvw)
{
    uvw = float3(0.0, 0.0, 0.0);
    float3 prevRelative = world + p.camera.xyz;
    float3 prevView = mul(p.prevWorldToView, float4(prevRelative, 0.0)).xyz;
    if (prevView.z <= p.prevUnproject.z)
    {
        return false;
    }

    float2 ndc = float2(
        prevView.x * p.prevUnproject.x / prevView.z,
        -prevView.y * p.prevUnproject.y / prevView.z);

    uvw = float3((ndc * 0.5) + 0.5, ViewDepthSlice(prevView.z) * p.zParams.w);
    return all(uvw >= float3(0.0, 0.0, 0.0)) && all(uvw <= float3(1.0, 1.0, 1.0));
}

[shader("compute")]
[numthreads(4, 4, 4)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int3 cell = int3(id);
    int3 size = int3(p.grid.xyz + 0.5);
    if (any(cell >= size))
    {
        return;
    }

    // This frame's sample point inside the cell; the offset walks a low-discrepancy sequence over frames
    // and the history below averages the walk into the whole cell (Wronski 2014).
    float2 uv = (float2(cell.xy) + p.jitter.xy) / p.grid.xy;
    float viewZ = SliceViewDepth(float(cell.z) + p.jitter.z);
    float3 viewPos = ViewPositionAt(uv, viewZ);
    float3 world = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz;

    float density = DensityAt(world);
    float3 rayView = normalize(viewPos);
    float3 scattering = float3(0.0, 0.0, 0.0);

    if (density > 0.0)
    {
        float3 light = p.sunColour.rgb * p.march.z * Phase(dot(rayView, p.sun.xyz)) * SunVisibility(viewPos, world);

        // The sky's light: the harmonics read back along the view ray and scaled by the anisotropy, which
        // is the isotropic average at g = 0 and leans towards where the medium scatters from as g grows.
        float3 rayWorld = mul(p.viewToWorld, float4(rayView, 0.0)).xyz;
        float3 sky = p.ambient.w > 0.5 ? SkyIrradianceAt(-rayWorld * p.medium.w) : p.ambient.rgb;
        light += sky * p.march.w;

        // Lamps, attenuated the way the lighting pass attenuates them: inverse square with a window that
        // takes the light to nothing at its reach. A lamp whose fitting is indoors lights no outdoor air.
        int lampCount = min(int(round(p.camera.w)), OPENIGI_MAX_LAMPS);
        for (int i = 0; i < lampCount; i++)
        {
            float4 lamp = p.lampPosition[i];
            float3 toLight = lamp.xyz - viewPos;
            float distanceSquared = dot(toLight, toLight);
            float reach = lamp.w;
            if (reach <= 0.0 || distanceSquared >= reach * reach)
            {
                continue;
            }

            float3 fitting = mul(p.viewToWorld, float4(lamp.xyz, 0.0)).xyz - float3(0.0, 0.0, 1.0);
            if (Indoors(fitting))
            {
                continue;
            }

            float ratio = distanceSquared / (reach * reach);
            float window = saturate(1.0 - (ratio * ratio));
            float attenuation = (window * window) / (distanceSquared + 1.0);
            float3 direction = toLight / sqrt(max(distanceSquared, 1e-8));
            light += p.lampColour[i].rgb * p.lampColour[i].w * attenuation * Phase(dot(rayView, direction));
        }

        scattering = light * p.albedo.rgb * density;
    }

    float4 result = float4(scattering, density);

    // Blend with where this cell was last frame. The cell's centre is reprojected, not the jittered
    // sample, so the history stays a steady average of the cell rather than chasing the jitter.
    if (p.jitter.w > 0.5)
    {
        float3 centreView = ViewPositionAt(
            (float2(cell.xy) + 0.5) / p.grid.xy,
            SliceViewDepth(float(cell.z) + 0.5));
        float3 centreWorld = mul(p.viewToWorld, float4(centreView, 0.0)).xyz;
        float3 historyUvw;
        if (HistoryCoordinate(centreWorld, historyUvw))
        {
            result = lerp(result, uHistory.SampleLevel(uHistorySampler, historyUvw, 0.0), p.grid.w);
        }
    }

    result = max(result, float4(0.0, 0.0, 0.0, 0.0));
    uScattering.Store(cell, result);
}
