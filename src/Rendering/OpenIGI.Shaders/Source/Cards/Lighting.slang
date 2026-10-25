// The surface cache's own lighting: what every captured card receives, what it bounces, and what it
// finally sends onward.
//
// One program with four modes, because all four walk the same tiles and reconstruct the same world
// position from the same page record, and only then diverge. Mode nought lights a texel directly (sun,
// sky and every fixture); mode one gathers the bounce at one texel in sixteen; mode two folds the direct
// and the bounced together through the surface's albedo; mode three seeds that final atlas from the
// direct term alone so the first gather has something to bounce off.
//
// The traced form is SPIR-V alone, because WGSL has no ray tracing.

#include "../Common/ComputeBindings.slang"
#include "../Common/ImageHistory.slang"

// Read as well as written: the bounce's temporal accumulation reads a probe's own previous value back
// before blending this frame's onto it. Reading and writing the same texel in one invocation is a plain
// memory access and well defined, unlike sampling a texture being written through an image. WGSL cannot
// read an rgba16float image back, so there the read comes from a copy -- see ImageHistory.
OPENIGI_HISTORY_IMAGE_2D(0, uDirect);

// Nine vectors per page and a tenth for its bounding sphere. Padded throughout for the reason the packing
// forces: a three-float vector has a four-float alignment on the GPU and a three-float stride on the CPU,
// so a mixed record slides.
OPENIGI_COMPUTE_BUFFER(0) StructuredBuffer<float4> uPages;

// One entry per work group: which page, and which tile of it.
OPENIGI_COMPUTE_BUFFER(1) StructuredBuffer<int4> uTiles;

// The lights this level implies but never declares. Three vectors each: position rebased onto this pass's
// origin with the fixture's own radius in w, colour with its relative intensity, and the direction the
// fixture faces (zero for one that faces nowhere in particular).
OPENIGI_COMPUTE_BUFFER(2) StructuredBuffer<float4> uLights;

// The page index over the world, so a ray that stops somewhere can find out what it stopped on.
OPENIGI_COMPUTE_BUFFER(3) RWStructuredBuffer<int2> uCells;
OPENIGI_COMPUTE_BUFFER(4) RWStructuredBuffer<int> uCellPages;

// Each probe's world position relative to this pass's origin, w set when the probe is real. One entry per
// probe, indexed by the probe's grid coordinate -- the gather fills it, the combine reads a texel's
// neighbours from it to weight them by how far off its plane they sit.
OPENIGI_COMPUTE_BUFFER(5) RWStructuredBuffer<float4> uProbePos;

// The tiles inside the active radius, compacted on the CPU and walked in place of the whole array by the
// steady-state slices. Without it, a slice's cost depended on where the cursor stood: the pages near the
// camera sit together in the tile order, so the one slice per round that crossed them carried nearly all
// the unculled work as a several-millisecond spike while every other slice was culled to nothing. Walking
// only the active tiles gives every frame the same share of the real work. The list is a superset built
// with a margin (see ActiveRebuildMargin); the exact radius test below still decides each tile, so the
// lighting is identical to walking everything.
OPENIGI_COMPUTE_BUFFER(6) StructuredBuffer<int> uActiveTiles;

// The capture, each at the unit the host already binds it to. The material atlas is bound there too and
// is not declared: nothing in this pass reads it, and a card's roughness has no bearing on the diffuse
// irradiance every mode here is after.
OPENIGI_COMPUTE_TEXTURE(1) Texture2D<float4> uAlbedo;
OPENIGI_COMPUTE_SAMPLER(1) SamplerState uAlbedoSampler;

OPENIGI_COMPUTE_TEXTURE(2) Texture2D<float4> uNormal;
OPENIGI_COMPUTE_SAMPLER(2) SamplerState uNormalSampler;

OPENIGI_COMPUTE_TEXTURE(4) Texture2D<float4> uDepth;
OPENIGI_COMPUTE_SAMPLER(4) SamplerState uDepthSampler;

// The level's distance field, as nested grids stacked along z. Zero levels means none was built and the
// sun goes unoccluded.
OPENIGI_COMPUTE_TEXTURE(5) Texture3D<float4> uClipmaps;
OPENIGI_COMPUTE_SAMPLER(5) SamplerState uClipmapsSampler;

// Whatever atlas the mode before this one wrote: the last iteration's FINAL lighting for the bounce,
// which is what makes it multi-bounce rather than one hop, and the direct atlas for the combine and the
// seed. One texture because the host binds one -- the modes never run together, and a gather cannot read
// the atlas it is writing, which is what forces the double buffer behind it.
OPENIGI_COMPUTE_TEXTURE(6) Texture2D<float4> uPrevious;
OPENIGI_COMPUTE_SAMPLER(6) SamplerState uPreviousSampler;

// The bounce the gather wrote, read back by the combine at the probes around a texel.
OPENIGI_COMPUTE_TEXTURE(7) Texture2D<float4> uIndirectRead;
OPENIGI_COMPUTE_SAMPLER(7) SamplerState uIndirectReadSampler;

#if OPENIGI_RAY_QUERY
OPENIGI_COMPUTE_SCENE RaytracingAccelerationStructure uScene;
#else
// The lamps' coarse maps, where the device draws them (see SetLampMapEntries): five vectors per map --
// the four rows of its world to clip matrix, measured from this pass's origin, then its projection's two
// depth terms (ndc depth = x + y / view depth) and whether it has been drawn -- and the maps themselves.
OPENIGI_COMPUTE_BUFFER(7) StructuredBuffer<float4> uLampMapRecords;
OPENIGI_COMPUTE_TEXTURE(8) DepthTexture2DArray uLampMaps;
#endif

#if OPENIGI_RAY_QUERY
// The world's cut-out masks, and in the globals the address of the hit records naming each surface's
// layer: both set by the device itself, for the trace's alpha test (CutoutHit.slang).
OPENIGI_COMPUTE_TEXTURE(3) Texture2DArray<float4> uCutoutMasks;
#endif

OPENIGI_COMPUTE_GLOBALS cbuffer Globals
{
#if OPENIGI_RAY_QUERY
    uint2 uCutoutRecords;
#endif

    // Each level's minimum corner relative to THIS pass's origin, with its voxel size in w.
    float4 uClipmapOrigin[4];

    // The sky as spherical harmonics, evaluated along a ray's own direction where the backend provides
    // them. A valid of zero falls back to the flat colour below.
    float4 uSkySh[7];

    float3 uAtlasSize;
    float3 uLookupMin;
    float3 uSunDirection;
    float3 uSunColour;
    float3 uSkyColour;

    // The steady-state refresh's centre, in this cache's own space.
    float3 uActiveCentre;

    // The outermost level's low corner, for the position probe.
    float3 uProbeMin;

    // Where this pass's origin stands in the world, which the traced form fires from.
    float3 uTraceOrigin;

    float uClipmapRange[4];

    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;

    int uTileCount;

    // Which tile the dispatch's first work group stands for. The steady-state bounce traces only a slice
    // of the cache each frame and walks this offset across the whole set over several frames, so one
    // frame's cost is a fraction of a full relight while a still scene still converges. Zero for the seed
    // pass, which relights everything at once.
    int uTileOffset;

    // Whether to walk the active list at all, and how many of its entries are valid. Two values rather
    // than a count with a sentinel because an unset parameter reads zero, and zero has to mean the safe
    // thing -- walk the whole tile array (the seed, a dump, and an ActiveRadius of nought all cover
    // everything) -- not cull every tile.
    int uUseActiveList;
    int uActiveCount;

    // How far apart in the tile array consecutive work groups stand. One (or an unset nought) is a
    // contiguous range. The whole-cache sweeps take every Nth tile instead, so each of their slices is a
    // sample of the whole level rather than one neighbourhood of it -- and costs what every other slice
    // costs, where a contiguous slice over a dense interior cost five times one over open ground.
    int uTileStride;

    // The steady state's reach. A radius of nought relights everything, which is what the seed and the
    // dump pass. See ActiveRadius.
    float uActiveRadius;

    int uLightCount;
    float uLocalScale;

    int uProbeWidth;
    int uProbeHeight;

    // Which frame of the accumulation this is, and how many frames it converges over. Zero is a one-shot
    // with no history -- the load pass and every dump -- so those stay deterministic; above it the gather
    // jitters its hemisphere per frame and blends onto what the probe already held.
    int uTemporalIndex;
    int uMaxFrames;

    // How many frames apart one tile's traces are -- the bounce's amortisation spread -- so its jitter
    // advances once per trace of THAT tile rather than once per frame. See ProbeCenter.
    int uJitterStride;

    int uMode;
    float uBoost;
    int uSkyShValid;

    // Which diagnostic to write instead of light, and the outermost level's extent for the position
    // probe. Zero writes light.
    int uProbe;
    float uProbeInvExtent;
};

// A cbuffer rather than a named block, so its members are in scope unqualified -- which is what lets the
// shared march and the shared card lookup name them directly, exactly as they did when these were loose
// uniforms.
#define kTraceOrigin uTraceOrigin

#include "../Common/SoftwareTrace.slang"
#include "../Common/SurfaceCache.slang"
#include "../Common/Hemisphere.slang"

#if !OPENIGI_RAY_QUERY
// Whether a lamp reaches a point of the cache by the lamp's own cube of coarse maps; -1 where it has none,
// for the distance field to answer instead. Far from the camera the field's voxels are a metre or two and a
// wall is not in it, so a lamp lit the cache through the wall it stood behind and the bounce carried that
// onto the far side. The maps hold the wall as drawn.
float CardLampMapVisibility(int entry, float3 world, float3 normal)
{
    if (entry < 0)
    {
        return -1.0;
    }

    int first = entry;

    // Off the surface along its normal by a texel and a half of the map where the point stands, and five
    // centimetres more. These maps are coarse -- sixty-four texels a face, a hand's width each at a few
    // metres -- and a wall the lamp sees at a grazing angle, as every wall lamp sees its own wall, crosses
    // several of their depths within one texel: held only five centimetres off, the wall shadowed itself
    // in patches, the cache took the patches, and the gather spread them over the wall as clouds. The
    // offset that grows with the texel is the one that keeps a grazing receiver clear of its own depth.
    uint mapWidth;
    uint mapHeight;
    uint mapLayers;
    uLampMaps.GetDimensions(mapWidth, mapHeight, mapLayers);
    float pointZ = 0.25;
    for (int face = 0; face < 6; face++)
    {
        float4 forward = uLampMapRecords[((first + face) * 5) + 3];
        pointZ = max(pointZ, dot(forward.xyz, world) + forward.w);
    }

    float texelAtPoint = pointZ * 2.0 / float(max(mapWidth, 1u));
    float3 at = world + (normal * (0.05 + (1.5 * texelAtPoint)));

    // The face the point lies furthest in front of is the one whose view holds it.
    int slot = first;
    float deepest = -1e30;
    for (int face = 0; face < 6; face++)
    {
        float4 forward = uLampMapRecords[((first + face) * 5) + 3];
        float depth = dot(forward.xyz, at) + forward.w;
        if (depth > deepest)
        {
            deepest = depth;
            slot = first + face;
        }
    }

    float4 terms = uLampMapRecords[(slot * 5) + 4];
    if (terms.z <= 0.0)
    {
        return -1.0;
    }

    float4 row0 = uLampMapRecords[slot * 5];
    float4 row1 = uLampMapRecords[(slot * 5) + 1];
    float viewZ = deepest;
    float nearPlane = terms.y / (-1.0 - terms.x);

    // Inside the near plane is inside the fitting, which casts nothing on what it lights.
    if (viewZ <= nearPlane)
    {
        return 1.0;
    }

    float2 ndc = float2(dot(row0.xyz, at) + row0.w, dot(row1.xyz, at) + row1.w) / viewZ;
    float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));

    uint width;
    uint height;
    uint layers;
    uLampMaps.GetDimensions(width, height, layers);
    int2 size = int2(int(width), int(height));

    // A few centimetres, a little of the distance, and two of the map's texels where the point stands.
    float texelMetres = viewZ * (2.0 / max(length(row0.xyz), 1e-3)) / float(max(width, 1u));
    float bias = 0.05 + (0.005 * viewZ) + (2.0 * texelMetres);

    // The sixteen texels round the point, each compared and the verdicts weighted by a tent over the
    // point's own place among them: a filtered edge a texel and a half wide either way, where four taps
    // left a cache texel near any edge flipping on whichever texel it fell closest to.
    float2 texelAt = (uv * float2(size)) - 0.5;
    int2 corner = int2(floor(texelAt)) - 1;
    float2 blend = texelAt - floor(texelAt);
    float lit = 0.0;
    float weightSum = 0.0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            int2 texel = clamp(corner + int2(x, y), int2(0, 0), size - 1);
            float stored = uLampMaps.Load(int4(texel, slot, 0));
            float storedZ = terms.y / min(((stored * 2.0) - 1.0) - terms.x, -1e-6);
            float2 offset = abs(float2(float(x) - 1.0, float(y) - 1.0) - blend);
            float2 tent = saturate(1.0 - (offset / 2.0));
            float weight = tent.x * tent.y;
            lit += storedZ >= viewZ - bias ? weight : 0.0;
            weightSum += weight;
        }
    }

    return lit / max(weightSum, 1e-4);
}
#endif

// How far a fixture is worth evaluating, as a multiple of the reach it carries, and the floor under that.
// Both in metres, which this world is in -- see WorldScene.UnitsPerMetre.
//
// What a fixture carries is ALREADY a reach, derived from its size and brightness (LightFixtureExtractor),
// not its size. This used to multiply it by twelve as if it were a size, so the cache lit every surface
// within twenty-four metres of the smallest lamp, four times as far as the deferred pass lights the same
// lamp. Three is the deferred pass's own default (r_local_light_range), so the bounce comes from the same
// pool of light the direct term draws.
static const float kLocalReach = 3.0;
static const float kMinimumReach = 2.0;

// How short of a fixture's centre its shadow ray stops, so the fitting's own glass is not its occluder.
static const float kLampStopShort = 0.12;

// How many rays the sky integral gets. Eight is enough to separate open ground from a corner without the
// banding a smaller set shows on curved surfaces, and this pass runs when the clipmaps move rather than
// per frame.
static const int kSkyRays = 8;

// The bounce's hemisphere: a four-by-four grid of trace directions per probe, sixteen in all, generated
// by uniform hemisphere sampling. The sixteen are projected into spherical harmonics and convolved with
// the cosine lobe rather than summed, so the estimate is the probe's irradiance and the cosine weighting
// is restored by the transfer, not the sampling.
static const int kHemisphereResolution = 4;
static const int kTracesPerProbe = 16;

static const float kPi = 3.14159265358979;

// Probe spacing, as a mask: every fourth texel each way. A page smaller than this still gets the probe at
// its own corner, because the mask is taken against the atlas rather than the page.
static const int kProbeMask = 3;

// The sky in a direction: the frame's own harmonics where it has them, and a flat colour over a
// hemispherical fade where it does not.
float3 SkyRadiance(float3 direction)
{
    if (uSkyShValid == 0)
    {
        return uSkyColour * ((direction.z * 0.5) + 0.5);
    }

    float4 n = float4(direction, 1.0);
    float4 q = n.xyzz * n.yzzx;
    float3 linearPart = float3(dot(uSkySh[0], n), dot(uSkySh[1], n), dot(uSkySh[2], n));
    float3 quadraticPart = float3(dot(uSkySh[3], q), dot(uSkySh[4], q), dot(uSkySh[5], q));
    float3 lastBand = uSkySh[6].xyz * ((direction.x * direction.x) - (direction.y * direction.y));
    return max(float3(0.0, 0.0, 0.0), linearPart + quadraticPart + lastBand);
}

float SunVisibility(float3 world, float3 normal)
{
    return Visibility(world, normal, uSunDirection);
}

// Van der Corput, base two. A fixed low-discrepancy set rather than a random one per texel: there is no
// denoiser behind this pass yet, so noise would survive into the cache, and a fixed set is smooth in the
// normal instead -- biased, but stable and reproducible.
float RadicalInverse(int index)
{
    uint bits = uint(index);
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);

    return float(bits) * 2.3283064365386963e-10;
}

// What the sky is actually worth here, by tracing for it.
//
// This replaces a term that was the surface's own facing and nothing else -- a wall got half the sky
// whether it stood in the open or at the bottom of a shaft. Cosine-weighted rays over the hemisphere,
// each one contributing only if it escapes, which is what puts contact darkening into the cache and is a
// large part of why a traced scene reads as lit rather than flat.
//
// Each escaping ray takes the sky's colour by its OWN direction rather than the surface's, so a gradient
// is integrated properly and, more usefully, a surface facing the ground gets little even where no
// geometry stands under it to occlude the ray. That matters because this game's terrain is a heightfield
// and is not in the field at all.
float3 SkyLight(float3 world, float3 normal)
{
    if (uShadowLevels <= 0)
    {
        return SkyRadiance(normal) * kPi;
    }

    // Any vector not parallel to the normal will do; picking against the normal's smallest component
    // keeps the cross product well conditioned.
    float3 aside = abs(normal.z) < 0.9 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(normal, aside));
    float3 bitangent = cross(normal, tangent);

    float3 total = float3(0.0, 0.0, 0.0);

    // The set turned by a hash of where the texel stands. The same eight directions at every texel shared
    // the same eight blind spots: a ledge that one of them missed was missed by the whole wall, and the
    // cache's sky came out in stripes and wedges that the bounce then carried. Turned per texel the misses
    // fall differently on each, and the bounce, which reads many texels at once, averages them away. Fixed
    // in the world, so a texel relit is relit the same.
    float3 cell = floor(world * 8.0);
    float rotation = frac(sin(dot(cell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
    float stratum = frac(sin(dot(cell, float3(39.346, 11.135, 83.155))) * 24634.6345);

    for (int ray = 0; ray < kSkyRays; ray++)
    {
        float u1 = (float(ray) + stratum) / float(kSkyRays);
        float u2 = frac(RadicalInverse(ray) + rotation);

        // Cosine-weighted by construction, so the integral needs no per-ray weight: the density already
        // carries the dot product a diffuse surface would have applied.
        float radius = sqrt(u1);
        float angle = 6.28318530718 * u2;

        float3 direction = normalize(
            (tangent * (radius * cos(angle)))
            + (bitangent * (radius * sin(angle)))
            + (normal * sqrt(max(0.0, 1.0 - u1))));

        total += SkyRadiance(direction) * Visibility(world, normal, direction);
    }

    // Cosine-weighted rays integrate radiance to irradiance with a pi-over-N estimator, the same
    // normalisation the screen gather uses -- SkyLight feeds the card's INCIDENT light, which the final
    // combine later takes through albedo over pi.
    return total * (kPi / float(kSkyRays));
}

// The first two bands of the real spherical harmonics, in the order (Y00, Y1-1, Y10, Y11), evaluated
// along a unit direction. See Sloan 2008 for the constants.
float4 ShBasis(float3 d)
{
    return float4(0.282095, -0.488603 * d.y, 0.488603 * d.z, -0.488603 * d.x);
}

// Radiance coefficients to the irradiance a surface facing `normal` receives: the basis there, scaled by
// the clamped-cosine lobe's band factors -- pi for the constant band, two pi over three for the linear
// one (Ramamoorthi & Hanrahan 2001).
float4 ShIrradianceTransfer(float3 normal)
{
    float4 basis = ShBasis(normal);
    return float4(basis.x * kPi, basis.yzw * (2.0 * kPi / 3.0));
}

// The albedo the cache bounces with. Below one, the boost lifts a dark surface's luminance towards white
// by raising it to that power, scaling the colour so its hue is kept; the result stays just short of one
// in every channel so the bounce's feedback through the cache always decays.
float3 BoostAlbedo(float3 albedo, float boost)
{
    if (boost >= 1.0)
    {
        return albedo;
    }

    float luminance = dot(albedo, float3(0.3, 0.59, 0.11));
    float lifted = pow(max(luminance, 0.0), boost);
    float3 boosted = saturate(albedo * (lifted / max(luminance, 1e-8)));
    return min(boosted, float3(0.99, 0.99, 0.99));
}

#include "../Common/CardProbes.slang"

#include "../Common/CardDiagnostics.slang"

// The four probes around a texel, as offsets from the one covering it.
static const int2 kProbeCorner[4] = { int2(0, 0), int2(1, 0), int2(0, 1), int2(1, 1) };

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    int slot = (int(groupId.x) * max(uTileStride, 1)) + uTileOffset;
    int tile = slot;
    if (uUseActiveList != 0)
    {
        if (slot >= uActiveCount)
        {
            return;
        }

        tile = uActiveTiles[slot];
    }

    if (tile >= uTileCount)
    {
        return;
    }

    int4 entry = uTiles[tile];
    int page = entry.x;
    int2 within = (entry.yz * 8) + int2(localId.xy);

    int at = page * kVectorsPerPage;

    // A page whose placement moves carries the light it had. Its shadows here would be traced against a
    // field built from the level as it loaded, in which it still stands where it started -- lit at its new
    // place, a truck's interior comes out as if the truck were not round it. See MovePlacement.
    if (uPages[at + 5].w > 0.5)
    {
        return;
    }

    // Out of reach, and out before a single texel is read. The page's bounding sphere is the same one the
    // card lookup rejects candidates with, so this costs one vector read against the dozen reads, the
    // atlas fetches and the shadow trace it skips. On these maps that is most of the cache on most frames
    // -- see ActiveRadius for why the far part cannot produce a new answer anyway.
    if (uActiveRadius > 0.0)
    {
        float4 reach = uPages[at + 9];
        float3 toCentre = reach.xyz - uActiveCentre;
        float limit = uActiveRadius + reach.w;

        if (dot(toCentre, toCentre) > limit * limit)
        {
            return;
        }
    }

    float4 lightPage = uPages[at + 0];
    float4 cardPage = uPages[at + 1];

    // Past the page's own edge: the last tile of a page whose size is not a multiple of eight covers
    // texels that belong to nobody, and writing them would spill into the gutter.
    if (within.x >= int(lightPage.z) || within.y >= int(lightPage.w))
    {
        return;
    }

    float4 centreAndHalfWidth = uPages[at + 2];
    float4 rightAndHalfHeight = uPages[at + 3];
    float4 upAndDepth = uPages[at + 4];
    float4 direction = uPages[at + 5];
    float4 row0 = uPages[at + 6];
    float4 row1 = uPages[at + 7];
    float4 row2 = uPages[at + 8];

    // Where in the card this texel sits, as a fraction, sampled at the texel's centre.
    float2 uv = (float2(within) + 0.5) / float2(lightPage.z, lightPage.w);

    // The captured surface, read from the OTHER atlas at the same fraction of the same card. Both layouts
    // allocate the same size for a card, so this needs no rescale.
    uint albedoWidth;
    uint albedoHeight;
    uAlbedo.GetDimensions(albedoWidth, albedoHeight);
    float2 cardUv =
        (cardPage.xy + (uv * cardPage.zw)) / float2(float(albedoWidth), float(albedoHeight));

    float4 albedo = uAlbedo.SampleLevel(uAlbedoSampler, cardUv, 0.0);

    int2 pageAt = int2(int(lightPage.x), int(lightPage.y));

    // Alpha is the coverage mask -- see CardAtlasShaders. A texel no geometry reached holds nothing worth
    // lighting, and depth cannot say so because a surface on the card's own plane has a depth of zero
    // too.
    if (albedo.a < 0.5)
    {
        OPENIGI_HISTORY_STORE(uDirect, pageAt + within, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 normal = normalize(
        (uNormal.SampleLevel(uNormalSampler, cardUv, 0.0).xyz * 2.0) - 1.0);
    float surfaceDepth = uDepth.SampleLevel(uDepthSampler, cardUv, 0.0).r;

    // Back to a position in the model's own space: across the card's rectangle, then behind its plane by
    // the depth that was captured. The camera looked along -Direction, so a surface further from the
    // plane is further along -Direction.
    float3 onPlane = centreAndHalfWidth.xyz
        + (rightAndHalfHeight.xyz * ((uv.x * 2.0) - 1.0) * centreAndHalfWidth.w)
        + (upAndDepth.xyz * ((uv.y * 2.0) - 1.0) * rightAndHalfHeight.w);

    float3 local = onPlane - (direction.xyz * (surfaceDepth * upAndDepth.w));

    // Into the world, through the placement. The rows carry the model-to-world rotation, so it is applied
    // transposed -- the same way SceneDistanceField reaches world space, and the two must agree or the
    // lighting sits somewhere the geometry is not.
    float3 world = float3(
        dot(float3(row0.x, row1.x, row2.x), local),
        dot(float3(row0.y, row1.y, row2.y), local),
        dot(float3(row0.z, row1.z, row2.z), local)) + float3(row0.w, row1.w, row2.w);

    float3 worldNormal = normalize(float3(
        dot(float3(row0.x, row1.x, row2.x), normal),
        dot(float3(row0.y, row1.y, row2.y), normal),
        dot(float3(row0.z, row1.z, row2.z), normal)));

    int2 texelAt = pageAt + within;

    // The bounce, written to its own atlas because it is gathered from the one holding the last
    // iteration's answer.
    if (uMode == 1)
    {
        // One texel in sixteen carries a probe. Gathering per texel would be four index walks and four
        // traces for every texel of a million-texel atlas, and it does not finish inside a frame. Indirect
        // light varies slowly across a surface, so sampling it on a grid and reading the nearest probe back
        // costs almost nothing in the picture.
        if (((texelAt.x | texelAt.y) & kProbeMask) != 0)
        {
            OPENIGI_HISTORY_STORE(uDirect, texelAt, float4(0.0, 0.0, 0.0, 0.0));
            return;
        }

        // Record where this probe stands so the combine can weight it against the texels it neighbours.
        // The w marks it real: a probe grid cell no page reaches keeps the zero it was cleared to, and
        // the combine skips it.
        int2 probeCoord = texelAt >> 2;
        uProbePos[(probeCoord.y * uProbeWidth) + probeCoord.x] = float4(world, 1.0);

        float3 gathered = Gather(world, worldNormal, ProbeCenter(probeCoord, uTemporalIndex));

        // A running mean over the probe's traces, with the count kept in alpha and capped so the newest
        // trace always keeps a share of at least one in uMaxFrames + 1. A one-shot pass has no history.
        float4 previous = OPENIGI_HISTORY_LOAD_2D(uDirect, texelAt);
        float traced = uTemporalIndex > 0 ? previous.a : 0.0;
        float limit = float(uMaxFrames);
        float blend = 1.0 / (1.0 + min(traced, limit));
        OPENIGI_HISTORY_STORE(
            uDirect,
            texelAt,
            float4(lerp(previous.rgb, gathered, blend), min(traced + 1.0, limit + 1.0)));
        return;
    }

    // The radiance the surface sends onward: the direct and the bounced irradiance are added and then both
    // take the surface's albedo and the Lambertian one-over-pi together -- a red wall reflects the sun as
    // red as it reflects the bounce, which the old split, colouring only the bounce, got wrong. The albedo
    // is boosted first so dim interiors keep passing light along, and the boost's clamp below one is what
    // stops the feedback from running away.
    if (uMode == 2)
    {
        float3 direct =
            uPrevious.Load(int3(texelAt, 0)).rgb;

        // The bounce at this texel, interpolated from the four probes around it. The weights run from a
        // sixth to four sixths across the four texels between probes, so no probe is ever weighted to
        // nothing or to everything; each probe must also be real, on this page and on this texel's plane.
        int2 pageMax = pageAt + int2(int(lightPage.z), int(lightPage.w));
        int2 probe00 = texelAt >> 2;
        float2 lean = (float2(texelAt - (probe00 * 4)) + 1.0) / 6.0;

        float3 indirectSum = float3(0.0, 0.0, 0.0);
        float indirectWeight = 0.0;
        for (int corner = 0; corner < 4; corner++)
        {
            int2 offset = kProbeCorner[corner];
            int2 probeCoord = probe00 + offset;
            float3 position;
            if (!ProbeUsable(probeCoord, pageAt, pageMax, position))
            {
                continue;
            }

            if (ProbePlaneAccept(world, worldNormal, position) <= 0.0)
            {
                continue;
            }

            float2 share = lerp(1.0 - lean, lean, float2(offset));
            float w = share.x * share.y;
            indirectSum += FilteredProbe(probeCoord, pageAt, pageMax, world, worldNormal) * w;
            indirectWeight += w;
        }

        float3 indirect = indirectWeight > 0.0
            ? indirectSum / indirectWeight
            : ProbeIrradiance(probe00);
        float3 diffuse = BoostAlbedo(albedo.rgb, uBoost) / kPi;

        OPENIGI_HISTORY_STORE(uDirect, texelAt, float4((direct + indirect) * diffuse, 1.0));
        return;
    }

    // The seed for the first bounce: outgoing radiance from the direct term alone. The gather reads the
    // final atlas, so before any bounce exists that atlas has to hold what a surface sends from direct
    // light -- the bounce is taken off the cache's final lighting, never off the raw irradiance that
    // reached it.
    if (uMode == 3)
    {
        float3 direct =
            uPrevious.Load(int3(texelAt, 0)).rgb;
        float3 diffuse = BoostAlbedo(albedo.rgb, uBoost) / kPi;
        OPENIGI_HISTORY_STORE(uDirect, texelAt, float4(direct * diffuse, 1.0));
        return;
    }

    if (uProbe != 0)
    {
        OPENIGI_HISTORY_STORE(uDirect, texelAt, Probed(world, worldNormal, cardUv, texelAt));
        return;
    }

    float nDotL = max(dot(worldNormal, uSunDirection), 0.0);

    // Only lit surfaces are worth tracing for: one already facing away from the sun is dark for a reason
    // no shadow test can improve on, and tracing it only spends steps.
    float visibility = nDotL > 0.0 ? SunVisibility(world, worldNormal) : 1.0;

    float3 lit = (uSunColour * nDotL * visibility) + SkyLight(world, worldNormal);

    // Every lamp this level has, added on top. There is no spatial culling here and there does not need to
    // be: this pass runs when the clipmaps move rather than per frame, a level's fixtures number in the
    // hundreds, and the few within reach of a texel are the only ones that get as far as a ray.
    for (int light = 0; light < uLightCount; light++)
    {
        float4 place = uLights[light * 3];
        float4 emission = uLights[(light * 3) + 1];
        float4 facing = uLights[(light * 3) + 2];

        float3 delta = place.xyz - world;
        float squared = dot(delta, delta);

        // The window, before anything expensive. Beyond a fixture's reach it contributes nothing, and
        // most texels are beyond most fixtures.
        float reach = max(place.w * kLocalReach, kMinimumReach);
        float reachSquared = reach * reach;

        if (squared >= reachSquared)
        {
            continue;
        }

        float3 toLight = delta * rsqrt(max(squared, 1e-6));
        float localDot = max(dot(worldNormal, toLight), 0.0);

        if (localDot <= 0.0)
        {
            continue;
        }

        // The deferred lamps' own falloff, an inverse square: 1 / (d^2 + 1), windowed
        // smoothly to zero at the reach so a fixture cannot draw a hard circle on the floor where its
        // influence stops. The bounce must be integrated from the light the screen shows.
        float falloff = 1.0 / (squared + 1.0);
        float window =
            clamp(1.0 - ((squared * squared) / (reachSquared * reachSquared)), 0.0, 1.0);

        // And the fixture's facing, the same cosine the deferred pass applies for the same reason: a lamp
        // throws light out of its opening and none into the slab it is bolted to. It matters more here
        // than there, because this is the term the whole room's BOUNCE is integrated from -- a fixture
        // lighting the ceiling above itself would feed that back into every surface below it as indirect,
        // so the error does not stay where it started.
        float aim = dot(facing.xyz, facing.xyz) > 0.25
            ? max(dot(normalize(facing.xyz), -toLight), 0.0)
            : 1.0;

        float contribution = emission.w * uLocalScale * localDot * falloff * window * window * aim;
        if (contribution * dot(emission.rgb, float3(0.2126, 0.7152, 0.0722)) < 1e-5)
        {
            continue;
        }

        // And whether the fixture can SEE this texel at all. There was no test here, so every fixture
        // lit every surface in its reach through any number of walls: the outside of a sealed building
        // took its interior's light into the cache, and from there the bounce carried it onto the ground
        // around it. One shadow ray per fixture that survived the cheap tests above, so the costs are
        // those of the few lamps near a texel, not of the level's hundreds.
        float reaches = -1.0;
#if !OPENIGI_RAY_QUERY
        reaches = CardLampMapVisibility(asint(facing.w), world, worldNormal);
#endif
        if (reaches < 0.0)
        {
            reaches = SegmentVisibility(world, worldNormal, place.xyz, kLampStopShort);
        }

        if (reaches <= 0.0)
        {
            continue;
        }

        lit += emission.rgb * (contribution * reaches);
    }

    OPENIGI_HISTORY_STORE(uDirect, texelAt, float4(lit, 1.0));
}
