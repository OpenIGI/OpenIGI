// What every window pane reflects: a ray against the world where there is a world to trace, and a march
// through the level's distance field where there is not.
//
// Two dispatches, for the reason the reflection pass has two. A pane's reflection is read FROM the lit
// scene and written back INTO it, and one pixel's reflection is another pixel's source. Writing in place
// would race, so the trace fills its own image and a second dispatch adds that image to the lit scene.
//
// Two variants, because the ray query cannot be a runtime question: the traced form needs the extension
// and the software form needs a distance field bound in its place, so they do not even carry the same
// descriptors. Whether a frame actually traces IS a runtime question, and rides in the parameters.

#ifndef OPENIGI_RAY_QUERY
#error "OPENIGI_RAY_QUERY must be defined by the shader build, as 1 or 0."
#endif

#include "../Common/SkyAtmosphere.slang"
#include "../Common/SkyView.slang"

// the finished frame, the reflection's source.
[[vk::binding(0, 0)]] Texture2D<float4> uLit;
[[vk::binding(1, 0)]] SamplerState uLitSampler;

// where every opaque surface stands. A depth texture, declared as one: WGSL refuses a depth image bound
// as a float texture.
[[vk::binding(2, 0)]] DepthTexture2D uSceneDepth;
[[vk::binding(3, 0)]] SamplerState uSceneDepthSampler;

// the panes: world normal, and view depth in alpha.
[[vk::binding(4, 0)]] Texture2D<float4> uGlass;
[[vk::binding(5, 0)]] SamplerState uGlassSampler;

// the sky, for a ray that struck nothing.
[[vk::binding(6, 0)]] Texture2D<float4> uSkyView;
[[vk::binding(7, 0)]] SamplerState uSkyViewSampler;

[[vk::binding(8, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uOut;

#if OPENIGI_RAY_QUERY

[[vk::binding(9, 0)]] RaytracingAccelerationStructure uScene;

[[vk::binding(10, 0)]] StructuredBuffer<float4> uPages;
[[vk::binding(11, 0)]] StructuredBuffer<int2> uCells;
[[vk::binding(12, 0)]] StructuredBuffer<int> uCellPages;

// the cards' albedo; its alpha says which texels exist.
[[vk::binding(13, 0)]] Texture2D<float4> uAlbedo;
[[vk::binding(14, 0)]] SamplerState uAlbedoSampler;

// the cards' depth, which decides between them.
[[vk::binding(15, 0)]] Texture2D<float4> uDepth;
[[vk::binding(16, 0)]] SamplerState uDepthSampler;

// the cards' lighting: outgoing radiance.
[[vk::binding(17, 0)]] Texture2D<float4> uFinal;
[[vk::binding(18, 0)]] SamplerState uFinalSampler;

// Every world texture reduced to one layer, for shading a hit the frame cannot see and the cards never
// baked. Read by the layer a hit's own material record names.
[[vk::binding(19, 0)]] Texture2DArray<float4> uTracedAlbedo;
[[vk::binding(20, 0)]] SamplerState uTracedAlbedoSampler;

// The world's cut-out masks, so a reflected ray stops on a fence's wire and not between them.
[[vk::binding(22, 0)]] Texture2DArray<float4> uCutoutMasks;

#define OPENIGI_GLAZING_PARAMS_BINDING 21

#else

// THE SOFTWARE SET. The cards are here too, and that is the point: an adapter that cannot trace still has
// the whole surface cache, and a hit found by marching resolves against exactly the same cards a traced
// hit does. What replaces the acceleration structure is the level's distance field -- the only
// description of the world a shader can walk without one.
[[vk::binding(9, 0)]] StructuredBuffer<float4> uPages;
[[vk::binding(10, 0)]] StructuredBuffer<int2> uCells;
[[vk::binding(11, 0)]] StructuredBuffer<int> uCellPages;

[[vk::binding(12, 0)]] Texture2D<float4> uAlbedo;
[[vk::binding(13, 0)]] SamplerState uAlbedoSampler;

[[vk::binding(14, 0)]] Texture2D<float4> uDepth;
[[vk::binding(15, 0)]] SamplerState uDepthSampler;

[[vk::binding(16, 0)]] Texture2D<float4> uFinal;
[[vk::binding(17, 0)]] SamplerState uFinalSampler;

// the level's distance field.
[[vk::binding(18, 0)]] Texture3D<float4> uClipmaps;
[[vk::binding(19, 0)]] SamplerState uClipmapsSampler;

// The level's roofs, seen from above, exactly as the shading and the water read them. Fetched and never
// sampled, so the sampler bound with it is never consulted.
[[vk::binding(20, 0)]] Texture2D<float4> uShelterMap;
[[vk::binding(21, 0)]] SamplerState uShelterMapSampler;

#define OPENIGI_GLAZING_PARAMS_BINDING 22

#endif

struct Params
{
    // view-space to camera-relative-world rotation.
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;

    // traces at all, reach (metres), radiance ceiling, sky view height (km).
    float4 trace;

    // the camera relative to the cache's origin, and the lookup cell size.
    float4 cacheOrigin;

    // the lookup grid's low corner, and how many buckets it has.
    float4 cacheGrid;

    // the lighting atlas in texels, the bounce scale, and the reflection's strength.
    float4 cacheAtlas;

    // show the reflection raw, the first-person lens x and y, one spare.
    float4 debug;

    // where the camera actually is in the world, and one spare.
    float4 cameraWorld;

    // device address of the material records, low word then high.
    uint4 records;

    // the direction TOWARDS the sun, in world space, and one spare.
    float4 sun;

    // the sun's colour already scaled by its strength, and one spare.
    float4 sunColour;

    // what a surface facing away from the sun still receives, and one spare.
    float4 ambient;

    // The distance field, for the software march. Declared in both variants although only one reads it:
    // one block layout is one offset table on the processor side, and the floats a traced frame never
    // looks at are cheaper than two packings that can drift apart.

    // how many clipmap levels are real, how many voxels square one is.
    float4 field;

    // each level's low corner RELATIVE TO THE CAMERA, and its voxel size.
    float4 sdfOrigin[4];

    // A SCALAR array, not a float4 one, because the shared march indexes it as scalars. Standard layout
    // still gives each element sixteen bytes, so the processor side writes at that stride.
    float sdfRange[4];

    // The roof raster's transform, packed as the reflection pass packs its own copy.

    // the map's world origin XY, texels per metre, the height it counts from.
    float4 shelter0;

    // metres per step, the headroom, the reach, whether there is a map at all.
    float4 shelter1;

    // texels across, texels down, two spare.
    float4 shelter2;
};

[[vk::binding(OPENIGI_GLAZING_PARAMS_BINDING, 0)]] ConstantBuffer<Params> p;

// What the shared resolve reads its host's parameters through. Named apart from the fields they expand
// to, because a define is textual and one spelled the same would expand its own right-hand side.
#define kViewToWorld ((float3x3)p.viewToWorld)
#define kUnproject p.unproject
#define kWorldOrigin p.cameraWorld.xyz
#define kResolution p.resolution.xy

// What the shared hit shading reads its host's parameters through.
#define kRecords p.records.xy
#define kSunDirection p.sun.xyz
#define kSunColour p.sunColour.rgb
#define kAmbient p.ambient.rgb

// What the shared distance-field march reads its host's parameters through.
#define uShadowLevels int(p.field.x)
#define uShadowResolution int(p.field.y)
#define uClipmapOrigin p.sdfOrigin
#define uClipmapRange p.sdfRange

// What the shared card lookup expects to find declared.
#define uLookupBuckets int(p.cacheGrid.w)
#define uLookupMin p.cacheGrid.xyz
#define uLookupCell p.cacheOrigin.w
#define uAtlasSize float3(p.cacheAtlas.xy, 0.0)

// And the lighting atlas by the name the shared sampling reads it under, which is what compiles
// SampleCardsWithin into this host at all.
#define kCardLighting uFinal
#define kCardLightingSampler uFinalSampler

#if OPENIGI_RAY_QUERY

// Material BEFORE the trace, and the switch between them: the trace shades its hit on the way out
// because the ray query is local to it, so ShadeHit has to be declared by the time it is called.
#include "../Common/TracedMaterial.slang"
#define kShadeHits 1
#define kTraceCutoutMasks uCutoutMasks
#include "../Common/TracedWorld.slang"

#else

// And what stands in for the acceleration structure where there is none. SurfaceAt, not VisibilityAt:
// the shadow march answers whether a ray was blocked, and a reflection needs where it stopped. The roofs
// come with it, because a march that loses the ceiling has to know that it has.
#include "../Common/Shelter.slang"
#include "../Common/DistanceField.slang"

// This pass's own copy of the roof map's transform, filled from its parameter block.
ShelterMap GlazingRoofs()
{
    ShelterMap m;
    m.origin = p.shelter0.xy;
    m.invCell = p.shelter0.z;
    m.lowestZ = p.shelter0.w;
    m.scale = p.shelter1.x;
    m.headroom = p.shelter1.y;
    m.reach = p.shelter1.z;
    m.size = p.shelter2.xy;
    m.present = p.shelter1.w;
    return m;
}

#endif

#include "../Common/SurfaceCache.slang"
#include "../Common/TracedScreen.slang"

// What the sky contributes when nothing was struck.
//
// Held to the LAST ROW OF SKY rather than read straight, and that is not a nicety. The table's lower half
// is the planet's own surface, and the boundary between the halves sits at v = 0.5 -- a function of the
// ray's zenith angle, so reading across it puts a hard line through the glass that SWEEPS as the camera
// pitches. Faded out downward as well, because a ray pointing at the floor is not looking at sky at any
// strength.
float3 FallbackSky(float3 rayW)
{
    return SampleSkyBackground(uSkyView, uSkyViewSampler, rayW, p.trace.w)
        * smoothstep(-0.10, 0.25, rayW.z);
}

// Which of the resolve's routes answered, for the route debug view. A global rather than an out parameter
// so that the resolve's signature -- and the software variant of it -- stay the shape the rest of this
// file expects. Nought is the sky; see DebugView.GlassRoute for the colours.
static int gRoute = 0;

#if OPENIGI_RAY_QUERY

// Turning a reflected ray into light, and the SAME light whichever way the camera happens to be pointing.
//
// THE LIT FRAME IS NOT CONSULTED. It used to be the first route -- the real thing at full resolution, for
// a hit the camera can also see -- and that is exactly what made the glass unstable: whether a reflected
// wall is on screen depends on where the player is looking, not on anything about the wall, so turning the
// mouse moved hits on and off the frame and each one flipped between the frame's lighting and a card's.
// The two never agree (one is this frame's shading at full detail, the other a coarse bake), so whole
// regions of a window changed colour, appeared and vanished as the view swept. A reflection has to be a
// function of the ray alone; every route below is.
//
// A lamp shows its own light, as the G-buffer gives it: its albedo times its strength. Static geometry reads
// its card -- the surface cache's own lighting, with the sun's shadows, the lamps and the bounce in it, which
// is what that surface actually looks like. Anything a card cannot describe -- a body, or a static surface
// too thin or too small for the cache, a monitor, a desk lamp -- is shaded from its own material under the
// light the CARDS AROUND IT receive: a card holds its surface's outgoing light and the albedo it photographed,
// and the first over the second is the light arriving there, lamps and bounce included. Only where no card is
// near does it fall back to the sun (where it can see the sun) and the level's ambient. A hit that nothing can
// name a material for is a neutral surface under the same light, never a hole.

// Whether a point sees the sun: one ray towards it, stopped by the first solid thing, cut-outs honoured.
// Without it the material route lit every surface that faced the sun as though it stood in the open, and a
// wall inside a room came out brighter than the card beside it that had its shadow.
float SunVisibility(float3 hitRelative, float3 normalW)
{
    if (dot(kSunColour, kSunColour) <= 0.0 || dot(normalW, kSunDirection) <= 0.0)
    {
        return 0.0;
    }

    RayDesc ray;
    ray.Origin = hitRelative + kWorldOrigin + (normalW * 0.05);
    ray.TMin = 0.01;
    ray.Direction = kSunDirection;
    ray.TMax = 4000.0;

    // Everything but the first-person rig, as the reflection's own trace does.
    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
    ProceedWithCutouts(query, kRecords, uCutoutMasks);
    return query.CommittedStatus() == COMMITTED_NOTHING ? 1.0 : 0.0;
}

// How far round a hit the cards are asked for the light arriving there. Wide, because the question is no
// longer which surface this IS but what light falls here, and the desk under a monitor answers it.
static const float kIrradianceReach = 1.0;

// A hit's own albedo under the light the cards around it receive, or the sun and the ambient where none is.
float3 LitMaterial(float3 albedo, float3 hitRelative, float3 normalW)
{
    float3 nearRadiance;
    float3 nearAlbedo;
    if (SampleCardsAndAlbedoWithin(
            hitRelative + p.cacheOrigin.xyz, normalW, kIrradianceReach, nearRadiance, nearAlbedo))
    {
        // The neighbour's outgoing light over its albedo, per channel, is the light reaching it; a surface
        // of this albedo there sends back that light times its own. Floored, so a near-black card cannot
        // make the estimate explode.
        float3 arriving = nearRadiance / max(nearAlbedo, float3(0.08, 0.08, 0.08));
        return albedo * arriving * p.cacheAtlas.z;
    }

    float direct = max(dot(normalW, kSunDirection), 0.0) * SunVisibility(hitRelative, normalW);
    return albedo * (kAmbient + (kSunColour * direct));
}

// What a surface nobody can identify is taken to reflect: a mid grey, so it reads as a surface in the
// light around it rather than as a hole punched through the glass.
static const float3 kNeutralAlbedo = float3(0.3, 0.3, 0.3);

float3 Resolve(float3 relative, float3 normalW, float3 rayW, float viewDepth)
{
    gRoute = 0;
    if (p.trace.x < 0.5)
    {
        return FallbackSky(rayW);
    }

    // Off the surface along its own normal, scaled with distance for the reason the other traces scale
    // it: the rounding at the origin grows with the magnitude of the position it happens to, and a ray
    // beginning a hair inside its own surface hits it.
    float3 originRelative = relative + (normalW * (0.02 + (0.0005 * viewDepth)));

    float3 hitRelative;
    bool shaded;
    float3 shadedRadiance;
    if (!TraceWorld(originRelative, rayW, p.trace.y, hitRelative, shaded, shadedRadiance))
    {
        return FallbackSky(rayW);
    }

    // The record could fail to name a normal -- no records at all -- and a hit then faces the ray that
    // found it, which is the one direction its surface is known to face.
    float3 hitNormal = dot(gHitNormalW, gHitNormalW) > 0.0 ? gHitNormalW : -rayW;

    float3 radiance = float3(0.0, 0.0, 0.0);
    bool resolved = false;

    // A lamp is its own light. Checked before the cards, which hold the dark fitting round the tube, and
    // before the material route, which would light the tube as though it were a panel in the room.
    if (gHitEmissive > 0.0)
    {
        radiance = (shaded ? gShadeAlbedo : float3(1.0, 1.0, 1.0)) * gHitEmissive;
        resolved = true;
        gRoute = 5;
    }

    // A CARD CANNOT DESCRIBE SOMETHING THAT MOVES, so a body never gets to ask for one: a player standing at
    // a window is within a hand's breadth of that building's wall, well inside the lookup's tolerance, and
    // the wall's card would claim them. The record says outright whether the hit was static.
    //
    // The cards are sampled BY THE HIT'S NORMAL, every card facing it contributing by how squarely it faced,
    // each texel judged against the depth recorded there -- at exactly the cache's own skin, which is the
    // right tolerance for an exact triangle hit. Wider would let a floor's card answer for the table top
    // above it; a surface its card does not reach is the material route's, lit by the same rule.
    if (!resolved && !gHitDynamic)
    {
        float3 cardRadiance;
        if (SampleCardsWithin(hitRelative + p.cacheOrigin.xyz, hitNormal, kCardSkin, cardRadiance))
        {
            radiance = cardRadiance * p.cacheAtlas.z;
            resolved = true;
            gRoute = 2;
        }
    }

    // The hit's OWN material: every body, and what the cache is too coarse to hold.
    if (!resolved && shaded)
    {
        radiance = LitMaterial(gShadeAlbedo, hitRelative, hitNormal);
        resolved = true;
        gRoute = 4;
    }

    // Struck something solid that nothing can put a material to. A surface still, under the same light.
    if (!resolved)
    {
        radiance = LitMaterial(kNeutralAlbedo, hitRelative, hitNormal);
        gRoute = 3;
    }

    // A ceiling on one ray, so a hit on something brilliant cannot flare. Scaled per channel by the same
    // factor rather than each clipped on its own, so a bright surface keeps its colour instead of sliding
    // towards white.
    float brightest = max(radiance.r, max(radiance.g, radiance.b));
    if (brightest > p.trace.z)
    {
        radiance *= p.trace.z / brightest;
    }

    return radiance;
}

#else

// THE SOFTWARE PATH. It used to be one line returning the sky, which is why a machine without ray tracing
// saw glass that reflected daylight and nothing else -- no building, no ground, no soldier. The hit is
// found by MARCHING the level's distance field instead of tracing an acceleration structure, and
// everything after that is the same: the lit frame where the camera can see the place, the surface
// cache's cards where it cannot.
//
// What it cannot do is the third route. Shading a hit from the hit's own texture needs to know WHICH
// triangle was struck, and a distance field has no triangles in it -- it is a field of distances, so a
// march knows where a surface is and nothing whatever about what it is made of. So a body off screen
// reflects as nothing here, where a traced frame would give it its skin.

// What a surface is assumed to reflect when the march found it and nothing can say what it is made of,
// and the weights that take a colour's brightness without its hue.
static const float kUnknownAlbedo = 0.3;
static const float3 kLuminance = float3(0.2126, 0.7152, 0.0722);

// A surface the march knows is there and can say nothing else about.
//
// The sky's BRIGHTNESS without its colour, plus the level's ambient. What is being estimated is a
// SURFACE, and its colour is exactly the thing not known -- borrowing the sky's is not a neutral guess
// but a specific wrong one. By day it passes; at night the sky is deep blue and nothing else in the level
// is, and every unresolved pixel came back blue.
float3 Unknown(float3 rayW)
{
    float grey = dot(FallbackSky(rayW), kLuminance) * kUnknownAlbedo;
    return float3(grey, grey, grey) + kAmbient;
}

float3 Resolve(float3 relative, float3 normalW, float3 rayW, float viewDepth)
{
    gRoute = 0;

    // No field to walk: the level has not built one, or the adapter was handed nothing. The sky is then
    // the whole honest answer, exactly as it was before any of this existed.
    if (uShadowLevels <= 0)
    {
        return FallbackSky(rayW);
    }

    // Whether this ray, if it runs off the end of the field, has found the sky or merely lost a ceiling
    // the clipmaps are far too coarse to hold.
    bool sheltered = LosesCeiling(
        GlazingRoofs(),
        uShelterMap,
        relative.xy + p.cameraWorld.xy,
        relative.z + p.cameraWorld.z,
        rayW);

    // The march. It starts the ray off the surface along the pane's own normal for us -- a ray beginning
    // exactly on a surface begins at distance nought, which reads as an immediate hit -- and what it
    // hands back beside the point is the size of the voxel that answered, which is this hit's own
    // accuracy and therefore every tolerance below.
    float3 hit;
    float voxel;
    if (!SurfaceAt(relative, normalW, rayW, p.trace.y, hit, voxel))
    {
        if (!sheltered)
        {
            return FallbackSky(rayW);
        }

        gRoute = 3;
        return Unknown(rayW);
    }

    // Every tolerance below is widened by the voxel that found the hit, and for one reason: a marched hit
    // is a point on a surface to within a fraction of that voxel and no better. The frame and the cards
    // both ask "is this hit ON the thing you are describing", and both answer no to a hit floating half a
    // metre in front of a wall -- so held to the traced path's tolerances the march resolves almost
    // nothing and the glass fills with holes.
    // NOT the lit frame, for the reason the traced path no longer reads it: whether a reflected place is
    // on screen depends on where the camera looks, so a reflection that reads the frame changes as the
    // mouse moves. The cards are a function of the ray alone.
    float3 radiance = float3(0.0, 0.0, 0.0);
    bool resolved = false;

    {
        // The cards, sampled by normal exactly as the traced path samples them, with the field's gradient
        // standing in for the triangle a march does not have -- and, where the field cannot say, the ray
        // itself, which is the one direction the struck surface is known to face. The bias is WIDENED BY
        // THE VOXEL: a traced hit lands on the surface, a marched one within a fraction of the voxel that
        // found it, which in the coarsest clipmap is a couple of metres. Twice the voxel, and the number
        // is measured rather than chosen: across a level's worth of panes the disagreement between a
        // marched hit and the card it belongs to came in at about one and a third voxels, so a bias of one
        // rejected half of every card that was in fact the right one.
        float3 hitNormal = FieldNormal(hit, voxel);
        if (dot(hitNormal, hitNormal) <= 0.0)
        {
            hitNormal = -rayW;
        }

        float3 cardRadiance;
        if (SampleCardsWithin(
                hit + p.cacheOrigin.xyz, hitNormal, kCardSkin + (voxel * 2.0), cardRadiance))
        {
            radiance = cardRadiance * p.cacheAtlas.z;
            resolved = true;
            gRoute = 2;
        }
    }

    if (!resolved)
    {
        // Struck something the frame cannot see and the bake does not hold. The traced path answers this
        // with black, and is right to: it knows precisely what it hit and can shade the hit from its own
        // material, so a hit that reaches here is a genuine gap, and a rare one. A march cannot. It has no
        // triangle, no texture and no material -- a distance field is a field of distances -- and a
        // clipmap of quarter-metre voxels at best cannot even find thin geometry a few metres out, so this
        // is not a gap here but the ordinary state of a fifth of what a window sees. Black turns that into
        // a hole in the glass with a hard edge round it, which reads as a rendering fault rather than as a
        // surface.
        //
        // ANSWERED EXACTLY AS A MISS IS, and that is the whole of what stops this looking like a fault.
        // The march's marginal hits are not spatially coherent: on a corrugated ceiling, or along any
        // surface a ray grazes, one ray registers a hit and the next does not, in fine interleaved bands.
        // While a hit and a miss were painted different colours those bands were the picture -- wavy
        // stripes across half a pane, which reads as a rendering fault rather than as a reflection.
        // Deciding the colour by the RAY's geometry instead of by whether the march happened to register,
        // the bands become invisible: only a hit that genuinely resolves to a card or to the frame differs
        // from its neighbours, and those are coherent because the geometry is.
        gRoute = 3;
        return sheltered ? Unknown(rayW) : FallbackSky(rayW);
    }

    float brightest = max(radiance.r, max(radiance.g, radiance.b));
    if (brightest > p.trace.z)
    {
        radiance *= p.trace.z / brightest;
    }

    return radiance;
}

#endif

// The route as a flat colour, for DebugView.GlassRoute. Deliberately NOT scaled by the radiance: the
// question this view answers is where a pixel's light came from, and a route that answered with something
// dark would otherwise read the same as a route that never ran.
float3 RouteColour(int route)
{
    // the lit frame. No longer a route: it made the reflection depend on where the camera looks.
    if (route == 1) { return float3(0.0, 1.0, 0.0); }

    // a surface-cache card: static, coarse.
    if (route == 2) { return float3(0.0, 0.2, 1.0); }

    // struck something nothing can name a material for, shaded as a neutral surface.
    if (route == 3) { return float3(1.0, 0.0, 0.0); }

    // the hit's own material: bodies live here.
    if (route == 4) { return float3(1.0, 0.8, 0.0); }

    // a light: the hit glows, and shows its own light.
    if (route == 5) { return float3(1.0, 1.0, 1.0); }

    // the sky, on a clean miss.
    return float3(0.25, 0.25, 0.25);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.resolution.x) || px.y >= int(p.resolution.y))
    {
        return;
    }

    // Cleared here rather than by a separate clear command: this dispatch covers the whole image anyway,
    // and a pixel with no glazing has to end up at nought for the composite to add nothing to it.
    float4 pane = uGlass.Load(int3(px, 0));
    if (pane.w <= 0.0)
    {
        uOut.Store(px, float4(0.0, 0.0, 0.0, 0.0));
        return;
    }

    float3 normalW = normalize(pane.xyz);
    float viewDepth = pane.w;

    // The pane's own position, rebuilt from the depth it recorded and the direction this pixel looks in.
    // The same arithmetic ViewToUv undoes.
    float2 ndc = (((float2(px) + 0.5) * p.resolution.zw) * 2.0) - 1.0;
    float3 viewPos = float3(
        ndc.x * viewDepth / p.unproject.x,
        -ndc.y * viewDepth / p.unproject.y,
        viewDepth);

    float3 relative = mul((float3x3)p.viewToWorld, viewPos);
    float3 toPixel = normalize(relative);

    // Two-sided, and it has to happen HERE rather than where the layer was written. A pane is a single
    // sheet of triangles with no thickness and a player walks around it: seen from inside the office the
    // authored normal points away, and a reflection about it would be fired into the wall behind the
    // glass. Which way it should face is a question about where the camera is, and this is the pass that
    // has the surface position to answer it.
    if (dot(normalW, toPixel) > 0.0)
    {
        normalW = -normalW;
    }

    float3 rayW = normalize(reflect(toPixel, normalW));

    // Schlick, about the reflectance of glass at normal incidence, for a PANE: two faces, so 2R/(1+R) --
    // 7.7 per cent head-on, where the room behind shows through almost unimpeded, rising towards a mirror
    // at a glancing angle. The transmission draw lets through exactly what this does not take, so the two
    // add up to the light that reached the glass. A fixed strength cannot produce that, and a window
    // without it reads as tinted film.
    float cosine = clamp(dot(normalW, -toPixel), 0.0, 1.0);
    float fresnel = 2.0 * (0.04 + (0.96 * pow(1.0 - cosine, 5.0)));
    fresnel = fresnel / (1.0 + (fresnel * 0.5));

    float3 radiance = Resolve(relative, normalW, rayW, viewDepth);

#if OPENIGI_RAY_QUERY
    // The hit's texture with no lighting on it at all. See DebugView.GlassAlbedo.
    //
    // Keyed on the ROUTE, not simply on whatever the shading left behind. The shading runs inside the
    // trace whether or not its answer is the one used, so reading its albedo unconditionally showed a
    // texture for pixels the frame or a card had already claimed -- which reads as proof that the material
    // route is working when it is not even being consulted. Black unless this pixel actually came from a
    // texture, and magenta only where the route was REACHED and had no layer to read.
    if (p.debug.w > 1.5 && p.debug.w < 2.5)
    {
        float3 shown = gRoute == 4
            ? gShadeAlbedo
            : (gRoute == 3 ? float3(1.0, 0.0, 1.0) : float3(0.0, 0.0, 0.0));
        uOut.Store(px, float4(shown, 1.0));
        return;
    }
#endif

    // Where this pixel is AIMING, before anything is asked about what is there; and one step further back,
    // the pane's own normal, which is what decides that aim.
    if (p.debug.w > 3.5)
    {
        uOut.Store(px, float4((normalW * 0.5) + 0.5, 1.0));
        return;
    }

    if (p.debug.w > 2.5)
    {
        uOut.Store(px, float4((rayW * 0.5) + 0.5, 1.0));
        return;
    }

    // Which route answered, rather than what it answered with. See DebugView.GlassRoute: a pane can look
    // entirely convincing and still be resolving every pixel out of a static card, and that is exactly the
    // failure the finished picture cannot show.
    if (p.debug.w > 0.5 && p.debug.w < 1.5)
    {
        uOut.Store(px, float4(RouteColour(gRoute), 1.0));
        return;
    }

    // The debug view takes it RAW, before the Fresnel share. Head-on that share is four per cent, which is
    // small enough that a reflection that is working and one that is returning nothing look the same in
    // the finished picture; taking the weight off is what separates them.
    if (p.debug.x > 0.5)
    {
        uOut.Store(px, float4(radiance, 1.0));
        return;
    }

    // Premultiplied by everything the composite must not have to know about: the Fresnel share and the
    // artistic strength. The composite then adds a colour and nothing else.
    uOut.Store(px, float4(radiance * fresnel * p.cacheAtlas.w, 1.0));
}
