// What the medium is at each froxel, and how much light it sends at the eye.
//
// Two forms. Where the adapter traces (OPENIGI_RAY_QUERY), each froxel fires one ray at the sun -- the frame
// keeps no shadow cascades then, and without that ray every froxel was sunlit and the fog an even veil with
// no shaft in it anywhere -- and its strongest lamps are traced too. Elsewhere the sun is read off the
// cascades and, past them, the level's distance field, which answers the lamps as well.

#ifndef OPENIGI_RAY_QUERY
#error "OPENIGI_RAY_QUERY must be defined by the shader build, as 1 or 0."
#endif

#include "../Common/ComputeBindings.slang"
#include "../Common/FogParams.slang"

// The level's distance field, which both forms declare: the software form marches it, and the shared trace
// reads its extent for how far a ray looks.
[[vk::binding(10, 0)]] Texture3D<float4> uClipmaps;
[[vk::binding(11, 0)]] SamplerState uClipmapsSampler;
#define uClipmapOrigin p.fieldOrigin
#define uClipmapRange p.fieldRange
#define uShadowLevels int(p.fieldMisc.x + 0.5)
#define uShadowResolution int(p.fieldMisc.y + 0.5)

// Every position here is camera-relative; the world the structure was built in is not.
#define kTraceOrigin p.cameraWorld.xyz

#if OPENIGI_RAY_QUERY
[[vk::binding(12, 0)]] RaytracingAccelerationStructure uScene;

// The world's cut-out masks, so a fence's wire stops the sun and the gaps let it through: patterned shafts.
[[vk::binding(13, 0)]] Texture2DArray<float4> uCutoutMasks;
#define uCutoutRecords uint2(asuint(p.records.x), asuint(p.records.y))
#endif

#include "../Common/SoftwareTrace.slang"

#if !OPENIGI_RAY_QUERY
// The nearest lamps' own shadow maps, which the lamp shadow pass draws where nothing is traced: the same
// block it reads (Lamp/Shadow.slang's FieldParams), of which only the maps are used here.
struct FogLampMapParams
{
    float4 origin[4];
    float4 range;
    int4 levels;
    float4x4 mapMatrix[240];
    float4 mapDepth[120];
    int4 mapSlot[10];
};

[[vk::binding(14, 0)]] DepthTexture2DArray uFogLampMaps;
[[vk::binding(15, 0)]] ConstantBuffer<FogLampMapParams> uFogLampMapParams;

// How much of a lamp reaches a camera-relative point by its own shadow map, or -1 where it has none or
// the point lies outside it. A texel's neighbourhood and a plain bias: the air has no surface to fit a
// receiver plane to, and the froxels' own jitter and history soften the edge.
float FogLampMapVisibility(int lamp, float3 cameraRelative)
{
    if (lamp < 0 || lamp >= 40)
    {
        return -1.0;
    }

    int entry = uFogLampMapParams.mapSlot[lamp >> 2][lamp & 3];
    if (entry < 0)
    {
        return -1.0;
    }

    int slot = entry & 0xFF;
    float4 clip = float4(0.0, 0.0, 0.0, 1.0);
    float3 ndc = float3(0.0, 0.0, 0.0);
    if ((entry & 0x100) != 0)
    {
        // A cube: the face whose view the point is in.
        bool found = false;
        for (int face = 0; face < 6 && !found; face++)
        {
            float4 faceClip = mul(uFogLampMapParams.mapMatrix[slot + face], float4(cameraRelative, 1.0));
            if (faceClip.w > 1e-4)
            {
                float3 faceNdc = faceClip.xyz / faceClip.w;
                if (all(abs(faceNdc.xy) <= float2(1.0, 1.0)))
                {
                    found = true;
                    slot += face;
                    clip = faceClip;
                    ndc = faceNdc;
                }
            }
        }

        if (!found)
        {
            return -1.0;
        }
    }
    else
    {
        clip = mul(uFogLampMapParams.mapMatrix[slot], float4(cameraRelative, 1.0));
        if (clip.w <= 1e-4)
        {
            return -1.0;
        }

        ndc = clip.xyz / clip.w;
        if (any(abs(ndc.xy) > float2(1.0, 1.0)))
        {
            return -1.0;
        }
    }

    float4 pair = uFogLampMapParams.mapDepth[slot >> 1];
    float2 terms = (slot & 1) != 0 ? pair.zw : pair.xy;
    float nearPlane = terms.y / (-1.0 - terms.x);
    float viewZ = clip.w;

    // Inside the near plane is inside the fitting, which nothing is drawn nearer than.
    if (viewZ <= nearPlane)
    {
        return 1.0;
    }

    float compareZ = max(viewZ - (0.05 + (0.01 * viewZ)), nearPlane + 0.005);
    float limit = ((terms.x + (terms.y / max(compareZ, 1e-4))) * 0.5) + 0.5;

    uint width;
    uint height;
    uint layers;
    uFogLampMaps.GetDimensions(width, height, layers);
    float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));
    int2 base = int2(floor((uv * float2(float(width), float(height))) - 0.5));
    int2 top = int2(int(width) - 1, int(height) - 1);

    float lit = 0.0;
    for (int y = 0; y < 2; y++)
    {
        for (int x = 0; x < 2; x++)
        {
            int2 at = clamp(base + int2(x, y), int2(0, 0), top);
            lit += uFogLampMaps.Load(int4(at, slot, 0)) >= limit ? 0.25 : 0.0;
        }
    }

    return lit;
}
#endif

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
//
// And two lobes before that: the forward one, and a weaker one scattering back. With the forward lobe
// alone a shaft showed only looking into the sun; turned side-on, or with the sun behind, it vanished,
// where real haze still shows its beams then.
float Phase(float cosTheta)
{
    const float isotropic = 1.0 / (4.0 * kPi);
    float lobes = lerp(PhaseHG(cosTheta, p.medium.w), PhaseHG(cosTheta, p.phase.x), p.phase.y);
    return lerp(lobes, isotropic, p.march.y);
}

// Value noise, smoothly interpolated between hashed lattice points.
float Hash31(float3 q)
{
    q = frac(q * 0.1031);
    q += dot(q, q.zyx + 31.32);
    return frac((q.x + q.y) * q.z);
}

float ValueNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    float3 u = f * f * (3.0 - (2.0 * f));
    float n000 = Hash31(i);
    float n100 = Hash31(i + float3(1.0, 0.0, 0.0));
    float n010 = Hash31(i + float3(0.0, 1.0, 0.0));
    float n110 = Hash31(i + float3(1.0, 1.0, 0.0));
    float n001 = Hash31(i + float3(0.0, 0.0, 1.0));
    float n101 = Hash31(i + float3(1.0, 0.0, 1.0));
    float n011 = Hash31(i + float3(0.0, 1.0, 1.0));
    float n111 = Hash31(i + float3(1.0, 1.0, 1.0));
    return lerp(
        lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
        lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y),
        u.z);
}

// How much denser or thinner the air is here than the medium's mean, drifting slowly downwind. A medium of
// one even density lights an even shaft, which reads as a pane of light rather than as air with light in
// it: the variation is what gives a beam its body.
float DensityVariation(float3 world)
{
    float amount = p.dust.w;
    if (amount <= 0.0)
    {
        return 1.0;
    }

    // In world metres, kept small: the camera's absolute position folded into a few kilometres so the
    // lattice keeps its precision.
    float3 absolute = world + (frac(p.cameraWorld.xyz / 4096.0) * 4096.0);
    float3 drift = float3(0.6, 0.25, 0.04) * p.phase.z;
    float n = (ValueNoise((absolute + drift) / 24.0) * 0.7) + (ValueNoise((absolute + (drift * 1.7)) / 9.0) * 0.3);
    return lerp(1.0, 0.3 + (1.4 * n), amount);
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
float DensityAt(float3 world, bool indoors)
{
    if (indoors)
    {
        return 0.0;
    }

    float height = world.z - p.medium.z;
    float banded = exp(-max(height, 0.0) * p.medium.y);
    float profile = lerp(banded, 1.0, p.march.x);
    return max(p.medium.x * profile * VolumeScaleAt(world) * DensityVariation(world), 0.0);
}

// The fine dust that hangs in any air, which scatters the sun and the lamps and nothing else. It is what a
// beam through a window, or a floodlight's cone at night, is seen in: rooms have it as much as yards do,
// and a room otherwise has no medium at all. Kept to the direct light alone -- the sky's dim fill through
// it would grey every interior -- and to the air near the eye out of doors, where a shaft is seen against
// what stands behind it; in the distance the medium itself takes over.
float DustAt(float3 world, float viewZ, bool indoors)
{
    float reach = max(p.dust.y, 1.0);
    // Even indoors: a room is metres across, and the drifting variation printed itself on its walls as grey
    // clouds rather than reading as air.
    if (indoors)
    {
        return max(p.dust.z, 0.0);
    }

    float density = p.dust.x * (1.0 - smoothstep(0.5 * reach, reach, viewZ));
    return max(density * DensityVariation(world), 0.0);
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

#if OPENIGI_RAY_QUERY
// How much sun reaches a froxel, by one ray aimed at a point on the sun's disc. The point moves per froxel
// and per frame, so the history averages the disc and a shaft's edge comes out as soft as the sun is
// wide. Cut-outs are tested against their masks: a fence's wire stops it and the gaps let it through.
float TracedSunVisibility(float3 world, float2 random)
{
    float3 sun = normalize(mul(p.viewToWorld, float4(p.sun.xyz, 0.0)).xyz);
    float3 up = abs(sun.z) < 0.99 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 across = normalize(cross(up, sun));
    float3 along = cross(sun, across);
    float radius = sqrt(random.x) * p.records.z;
    float angle = 6.2831853 * random.y;
    float3 direction = normalize(sun + (across * (radius * cos(angle))) + (along * (radius * sin(angle))));

    RayDesc ray;
    ray.Origin = world + kTraceOrigin;
    ray.TMin = 0.05;
    ray.Direction = direction;
    ray.TMax = 4000.0;

    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
    ProceedWithCutouts(query, uCutoutRecords, uCutoutMasks);
    return query.CommittedStatus() == COMMITTED_NOTHING ? 1.0 : 0.0;
}
#endif

// Past the cascades, the level's distance field towards the sun. The cascades used to fade into "lit"
// over their last stretch, so from some eighty metres out every froxel was sunlit again and the shafts of
// anything further away -- the far buildings, the hills -- were not there.
float FieldSunVisibility(float3 world)
{
    if (uShadowLevels <= 0)
    {
        return 1.0;
    }

    float3 sun = normalize(mul(p.viewToWorld, float4(p.sun.xyz, 0.0)).xyz);
    return Visibility(world, float3(0.0, 0.0, 0.0), sun);
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
    float far = beyond > 0.0 ? FieldSunVisibility(world) : 1.0;
    if (beyond >= 1.0)
    {
        return far;
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

        return lerp(sum, far, beyond);
    }

    return far;
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

    // This frame's sample point inside the cell. The offset walks a low-discrepancy sequence over frames
    // and the history below averages the walk into the whole cell (Wronski 2014) -- rotated by a hash of
    // the cell itself, so that neighbouring cells sample different parts of themselves on the same frame.
    // One offset shared by the whole grid moved every sample in step, and what the history could not
    // average away stood in the air as bands.
    float3 rotation = float3(
        Hash31(float3(cell) + 0.37),
        Hash31(float3(cell.yzx) + 11.1),
        Hash31(float3(cell.zxy) + 23.7));
    float3 offset = frac(p.jitter.xyz + rotation);
    float2 uv = (float2(cell.xy) + offset.xy) / p.grid.xy;
    float viewZ = SliceViewDepth(float(cell.z) + offset.z);
    float3 viewPos = ViewPositionAt(uv, viewZ);
    float3 world = mul(p.viewToWorld, float4(viewPos, 0.0)).xyz;

    bool indoors = Indoors(world);
    float density = DensityAt(world, indoors);
    float dust = DustAt(world, viewZ, indoors);
    float3 rayView = normalize(viewPos);
    float3 scattering = float3(0.0, 0.0, 0.0);

    if (density + dust > 0.0)
    {
        // The direct light, which both the medium and the dust scatter.
        float sunVisibility;
#if OPENIGI_RAY_QUERY
        if (p.phase.w > 0.5)
        {
            float2 random = frac(float2(Hash31(float3(cell) + p.phase.z), Hash31(float3(cell.zxy) - p.phase.z)) + offset.xy);
            sunVisibility = TracedSunVisibility(world, random);
        }
        else
        {
            sunVisibility = SunVisibility(viewPos, world);
        }
#else
        sunVisibility = SunVisibility(viewPos, world);
#endif
        float3 direct = p.sunColour.rgb * p.march.z * Phase(dot(rayView, p.sun.xyz)) * sunVisibility;

        // Lamps, attenuated the way the lighting pass attenuates them: inverse square with a window that
        // takes the light to nothing at its reach, and their facing, so a floodlight's light goes out of its
        // glass in a cone rather than every way at once. A lamp whose fitting is indoors lights no outdoor
        // air.
        //
        // And the ones that light this froxel most are shadowed: a lamp's cone through the air stops at
        // the wall, the canopy, the soldier in front of it. Every lamp was unshadowed, so its light went
        // straight through the building it hung on. Where nothing is traced, a lamp with a shadow map of its
        // own is answered by it, whatever its rank -- the distance field cannot hold a wall a lamp hangs
        // forty centimetres behind, and the room's lamp lit the air outside the wall; the strongest of the
        // rest are marched. Traced, the four strongest are. The rest are the faint ones.
#if OPENIGI_RAY_QUERY
        const int kShadowedLamps = 4;
#else
        const int kShadowedLamps = 2;
#endif
        int rankedLamp[4] = { -1, -1, -1, -1 };
        float3 rankedLight[4] = {
            float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 0.0) };
        float rankedLuminance[4] = { 0.0, 0.0, 0.0, 0.0 };

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
            if (Indoors(fitting) && !indoors)
            {
                continue;
            }

            float ratio = distanceSquared / (reach * reach);
            float window = saturate(1.0 - (ratio * ratio));
            float attenuation = (window * window) / (distanceSquared + 1.0);
            float3 direction = toLight / sqrt(max(distanceSquared, 1e-8));

            float3 facing = p.lampDirection[i].xyz;
            if (dot(facing, facing) > 0.25)
            {
                attenuation *= max(dot(normalize(facing), -direction), 0.0);
            }

            float3 lampLight = p.lampColour[i].rgb * p.lampColour[i].w * attenuation * Phase(dot(rayView, direction));

#if !OPENIGI_RAY_QUERY
            float mapped = FogLampMapVisibility(i, world);
            if (mapped >= 0.0)
            {
                direct += lampLight * mapped;
                continue;
            }
#endif

            direct += lampLight;

            // Kept in order, brightest first, as many as are shadowed.
            float luminance = dot(lampLight, float3(0.2126, 0.7152, 0.0722));
            int place = kShadowedLamps;
            for (int r = kShadowedLamps - 1; r >= 0; r--)
            {
                if (luminance > rankedLuminance[r])
                {
                    place = r;
                }
            }

            if (place < kShadowedLamps)
            {
                for (int r = kShadowedLamps - 1; r > place; r--)
                {
                    rankedLamp[r] = rankedLamp[r - 1];
                    rankedLight[r] = rankedLight[r - 1];
                    rankedLuminance[r] = rankedLuminance[r - 1];
                }

                rankedLamp[place] = i;
                rankedLight[place] = lampLight;
                rankedLuminance[place] = luminance;
            }
        }

        // Each ray stops as far short of its lamp as the surfaces' shadow rays do: half the fitting's size,
        // five to twenty centimetres -- past that is the fitting itself. Stopped a whole fitting's size
        // short, as it was, a lamp hung within forty centimetres of a wall never reached it: the lamp in
        // the room behind lit the air on the outside of the wall in a broad glowing patch.
        for (int r = 0; r < kShadowedLamps; r++)
        {
            int ranked = rankedLamp[r];
            if (ranked >= 0)
            {
                float3 worldLampAt = mul(p.viewToWorld, float4(p.lampPosition[ranked].xyz, 0.0)).xyz;
                float stopShort = clamp(p.lampDirection[ranked].w * 0.5, 0.05, 0.2);
                direct -= rankedLight[r]
                    * (1.0 - SegmentVisibility(world, float3(0.0, 0.0, 0.0), worldLampAt, stopShort));
            }
        }

        // The sky's light, which the medium scatters and the dust does not: the harmonics read back along
        // the view ray and scaled by the anisotropy, which is the isotropic average at g = 0 and leans
        // towards where the medium scatters from as g grows.
        float3 rayWorld = mul(p.viewToWorld, float4(rayView, 0.0)).xyz;
        float3 sky = p.ambient.w > 0.5 ? SkyIrradianceAt(-rayWorld * p.medium.w) : p.ambient.rgb;

        scattering = ((max(direct, float3(0.0, 0.0, 0.0)) * (density + dust)) + (sky * p.march.w * density))
            * p.albedo.rgb;
    }

    float4 result = float4(scattering, density + dust);

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
