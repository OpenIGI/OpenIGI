// What each surface reflects: a ray against the world where there is a world to trace, a march through
// the level's distance field where there is not, and a march through the depth buffer under both.
//
// Two variants, because the ray query cannot be a runtime question: the traced form needs the extension
// and the software form needs a distance field bound in its place. Seven resources either way, which is
// why the parameter block lands on the same binding whichever is compiled.

#ifndef OPENIGI_RAY_QUERY
#error "OPENIGI_RAY_QUERY must be defined by the shader build, as 1 or 0."
#endif

#include "../Common/GBufferEncoding.slang"
#include "../Common/DepthFilter.slang"
#include "../Common/ViewDepth.slang"
#include "../Common/SkyAtmosphere.slang"
#include "../Common/SkyView.slang"
#include "../Common/Wetness.slang"

// the lit scene, the reflection's source.
[[vk::binding(0, 0)]] Texture2D<float4> uLit;
[[vk::binding(1, 0)]] SamplerState uLitSampler;

// where every surface stands.
// A depth image declared as one: WebGPU refuses a depth format on a float slot, and cannot filter it.
[[vk::binding(2, 0)]] DepthTexture2D uSceneDepth;
[[vk::binding(3, 0)]] SamplerState uSceneDepthSampler;

// the world normal, encoded.
[[vk::binding(4, 0)]] Texture2D<float4> uNormal;
[[vk::binding(5, 0)]] SamplerState uNormalSampler;

// metallic, specular, roughness, model.
[[vk::binding(6, 0)]] Texture2D<float4> uMaterial;
[[vk::binding(7, 0)]] SamplerState uMaterialSampler;

// The sky, in the general layout the atmosphere passes leave it in. Read for one reason only: to know
// what the shading already put in this pixel's water, so that a marched reflection can take the place of
// it instead of piling on top.
[[vk::binding(8, 0)]] Texture2D<float4> uSkyView;
[[vk::binding(9, 0)]] SamplerState uSkyViewSampler;

// The level's roofs, seen from above, exactly as the shading pass reads them. Fetched and never sampled,
// so the sampler bound with it is never consulted.
[[vk::binding(10, 0)]] Texture2D<float4> uShelterMap;
[[vk::binding(11, 0)]] SamplerState uShelterMapSampler;

// The albedo the geometry pass wrote. Read for ONE thing: a metal's reflection is its own colour. A
// dielectric reflects white at four per cent whatever it is made of, but a metal has no diffuse term at
// all -- what it reflects IS its albedo, which is why gold reflects a gold room and steel a grey one.
// Without this every metal in the level mirrored the world in neutral grey, the one thing that most gives
// a metal away as painted.
[[vk::binding(12, 0)]] Texture2D<float4> uBaseColour;
[[vk::binding(13, 0)]] SamplerState uBaseColourSampler;

[[vk::binding(14, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uReflection;

#if OPENIGI_RAY_QUERY

// The world, as the structure a ray is traced against.
[[vk::binding(15, 0)]] RaytracingAccelerationStructure uScene;

// The surface cache: the cards the level was captured onto, the grid that finds one from a point, and the
// atlas holding what each card was lit to. This is what turns a ray that left the screen into light --
// the frame cannot say what stands behind the camera, and this can.
[[vk::binding(16, 0)]] StructuredBuffer<float4> uPages;
[[vk::binding(17, 0)]] StructuredBuffer<int2> uCells;
[[vk::binding(18, 0)]] StructuredBuffer<int> uCellPages;

// the cards' albedo; its alpha says which texels exist.
[[vk::binding(19, 0)]] Texture2D<float4> uAlbedo;
[[vk::binding(20, 0)]] SamplerState uAlbedoSampler;

// the cards' depth, which decides between them.
[[vk::binding(21, 0)]] Texture2D<float4> uDepth;
[[vk::binding(22, 0)]] SamplerState uDepthSampler;

// the cards' lighting: outgoing radiance.
[[vk::binding(23, 0)]] Texture2D<float4> uFinal;
[[vk::binding(24, 0)]] SamplerState uFinalSampler;

// The world's cut-out masks, so a reflected ray stops on a fence's wire and not between them.
[[vk::binding(26, 0)]] Texture2DArray<float4> uCutoutMasks;

#define OPENIGI_REFLECTION_PARAMS_BINDING 25

#else

// THE SOFTWARE SET, and it is the same surface cache. An adapter that cannot trace is not missing the
// cards -- only the acceleration structure -- so the only substitution is the level's distance field,
// which a shader can walk to find where a ray stops.
[[vk::binding(15, 0)]] StructuredBuffer<float4> uPages;
[[vk::binding(16, 0)]] StructuredBuffer<int2> uCells;
[[vk::binding(17, 0)]] StructuredBuffer<int> uCellPages;

[[vk::binding(18, 0)]] Texture2D<float4> uAlbedo;
[[vk::binding(19, 0)]] SamplerState uAlbedoSampler;

[[vk::binding(20, 0)]] Texture2D<float4> uDepth;
[[vk::binding(21, 0)]] SamplerState uDepthSampler;

[[vk::binding(22, 0)]] Texture2D<float4> uFinal;
[[vk::binding(23, 0)]] SamplerState uFinalSampler;

// the level's distance field.
[[vk::binding(24, 0)]] Texture3D<float4> uClipmaps;
[[vk::binding(25, 0)]] SamplerState uClipmapsSampler;

#define OPENIGI_REFLECTION_PARAMS_BINDING 26

#endif

struct Params
{
    // view-space to camera-relative-world rotation.
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;

    // march steps, intensity, max roughness, thickness (metres).
    float4 ssr;

    // wetness amount, frame index, reach (metres), view height (km).
    float4 misc;

    // puddle coverage, puddle size (m), ripple strength, seconds.
    float4 wet0;

    // ripple size (m), wind ripple strength, camera X, camera Y.
    float4 wet1;

    // shelter map present, camera Z, _, _.
    float4 wet2;

    // the map's world origin XY, texels per metre, the height it counts from.
    float4 shelter0;

    // metres per step, the headroom, the reach, _.
    float4 shelter1;

    // texels across, texels down, _, _.
    float4 shelter2;

    // The traced half. Whether to trace at all, how far a ray reaches in metres, the ceiling on one ray's
    // radiance, and how much of the surface cache's bounce a hit keeps.
    float4 trace;

    // Where the camera stands relative to the surface cache's origin, and how much world one lookup cell
    // covers. Everything in this pass is measured from the camera; the cache measures from its own
    // centre, and this is the step between the two.
    float4 cacheOrigin;

    // the lookup grid's low corner, and how many buckets it has.
    float4 cacheGrid;

    // the lighting atlas's size in texels, and two spare.
    float4 cacheAtlas;

    // The trace's roughness ceiling, the first-person lens's own x and y scales, one spare.
    float4 trace2;

    // The level's distance field, for the half that MARCHES because it cannot trace. Declared in both
    // variants although only one reads it: one block layout is one offset table on the processor side,
    // and the floats a traced frame never looks at are cheaper than two packings that can drift apart.

    // how many clipmap levels are real, how many voxels square one is.
    float4 field;

    // each level's low corner RELATIVE TO THE CAMERA, and its voxel size.
    float4 sdfOrigin[4];

    // A SCALAR array, not a float4 one, because the shared march indexes it as scalars. Standard layout
    // still gives each element sixteen bytes, so the processor side writes at that stride.
    float sdfRange[4];
};

[[vk::binding(OPENIGI_REFLECTION_PARAMS_BINDING, 0)]] ConstantBuffer<Params> p;

// What the shared resolve reads this host's parameters through. Named apart from the fields they expand
// to, because a define is textual and one spelled the same would expand its own right-hand side.
#define kViewToWorld ((float3x3)p.viewToWorld)
#define kUnproject p.unproject
#define kWorldOrigin float3(p.wet1.zw, p.wet2.y)
#define kResolution p.resolution.xy

// What the shared card lookup expects to find declared.
#define uAtlasSize float3(p.cacheAtlas.xy, 0.0)
#define uLookupBuckets int(p.cacheGrid.w)
#define uLookupMin p.cacheGrid.xyz
#define uLookupCell p.cacheOrigin.w

// What the shared distance-field march reads this host's parameters through.
#define uShadowLevels int(p.field.x)
#define uShadowResolution int(p.field.y)
#define uClipmapOrigin p.sdfOrigin
#define uClipmapRange p.sdfRange

#include "../Common/SurfaceCache.slang"
#include "../Common/TracedScreen.slang"

#if OPENIGI_RAY_QUERY
// The hit records' address, low word then high, carried bit for bit in the atlas row's spare pair.
#define kRecords uint2(asuint(p.cacheAtlas.z), asuint(p.cacheAtlas.w))
#define kTraceCutoutMasks uCutoutMasks
#include "../Common/HitRecords.slang"
#include "../Common/TracedWorld.slang"
#else
#include "../Common/DistanceField.slang"

// What a surface is assumed to reflect when the march found it and nothing can say what it is made of,
// and the weights that take a colour's brightness without its hue.
static const float kUnknownAlbedo = 0.3;
static const float3 kLuminance = float3(0.2126, 0.7152, 0.0722);
#endif

// The scene's view depth at a UV, read at the one texel under it rather than blended across the four round
// it: at a silhouette a blend of foreground and background is a depth no surface has. Nought where the
// texel is open sky.
// How much of a sky the ray did not find in the way is still hidden by the surface's own crevices: the
// shading's specular occlusion, from the material's occlusion, for the fallbacks alone -- a ray that struck
// something already answers for what is in the way. Without it a crease, the underside of an eave, the
// shade under a vehicle each glinted with the full sky.
float FallbackOcclusion(float nDotV, float roughness, float2 uv)
{
    float visibility = saturate(uBaseColour.SampleLevel(uBaseColourSampler, uv, 0.0).a);
    float alpha = roughness * roughness;
    return clamp(pow(nDotV + visibility, alpha) - 1.0 + visibility, 0.0, 1.0);
}

float PointDepth(float2 uv)
{
    uint width;
    uint height;
    uSceneDepth.GetDimensions(width, height);
    int2 texel = clamp(int2(uv * float2(width, height)), int2(0, 0), int2(int(width) - 1, int(height) - 1));
    return uSceneDepth.Load(int3(texel, 0));
}

float SampleSceneViewZ(float2 uv)
{
    uint width;
    uint height;
    uSceneDepth.GetDimensions(width, height);
    int2 texel = clamp(int2(uv * float2(width, height)), int2(0, 0), int2(int(width) - 1, int(height) - 1));
    float d = uSceneDepth.Load(int3(texel, 0));
    return d > 0.0 ? SceneViewZ(d) : 0.0;
}

// The specular lobe averaged over the hemisphere at a roughness and a viewing angle: what F0 and F90 are
// scaled by to weigh an environment's reflection. The same analytic fit the shading weighs its own
// estimate of that environment with (Shading.slang, SplitSumApprox), so the reflection that stands in for
// it is weighed the same way.
float2 EnvironmentBrdf(float roughness, float cosNV)
{
    float smoothness = 1.0 - roughness;
    float grazing = min(smoothness * smoothness, exp2(-9.28 * cosNV));
    float t = (grazing * smoothness) + 0.0425 - (0.0275 * roughness);
    float scale = 1.04 - (0.572 * roughness) - (1.04 * t);
    float bias = (0.022 * roughness) - 0.04 + (1.04 * t);
    return float2(scale, bias);
}

// The weather, gathered out of this pass's own parameter block. The shading pass fills the same struct
// from its uniform; that both do it by hand from different layouts is the whole reason the struct exists.
WetParams Weather()
{
    WetParams w;
    w.camera = p.wet1.zw;
    w.amount = max(p.misc.x, 0.0);
    w.coverage = p.wet0.x;
    w.puddleSize = p.wet0.y;
    w.rippleStrength = p.wet0.z;
    w.seconds = p.wet0.w;
    w.rippleSize = p.wet1.x;
    w.windStrength = p.wet1.y;
    return w;
}

// This pass's own copy of the roof map's transform, filled from its parameter block.
ShelterMap Roofs()
{
    ShelterMap m;
    m.origin = p.shelter0.xy;
    m.invCell = p.shelter0.z;
    m.lowestZ = p.shelter0.w;
    m.scale = p.shelter1.x;
    m.headroom = p.shelter1.y;
    m.reach = p.shelter1.z;
    m.size = p.shelter2.xy;
    m.present = p.wet2.x;
    return m;
}

// Whether a roof stands over the surface being marched from. Without it the march painted a mirror across
// every sheltered floor in the level -- water the shading had never drawn, because the shading knows about
// roofs and this did not.
bool UnderShelter(float3 relative)
{
    ShelterMap m = Roofs();
    int2 at = ShelterTexel(m, relative.xy + p.wet1.zw);
    if (!ShelterHolds(m, at))
    {
        return false;
    }

    float surface = ShelterSurface(m, uShelterMap.Load(int3(at, 0)));
    return ShelterCovers(m, surface, relative.z + p.wet2.y);
}

#include "../Common/FrameNoise.slang"

// Where along the first step a ray starts marching, moving from frame to frame.
float Dither(float2 pos, float frame)
{
    return FrameNoise(pos, frame);
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

    float2 uv = (float2(px) + 0.5) * p.resolution.zw;

    float3 reflection = float3(0.0, 0.0, 0.0);

    // How much of this pixel's reflection the screen managed to answer, from none to all of it. The
    // march's border vignette is exactly a confidence: a ray that lands near the edge of the frame has
    // found SOMETHING, but the thing it found is about to slide off screen, so the answer is faded rather
    // than taken whole. What the fade leaves behind used to be nothing at all -- which is why a reflection
    // ended in a band of dark along every edge of the picture -- and is now what the traced half is asked
    // for.
    float screenWeight = 0.0;

    float depth = DepthBilinear(uSceneDepth, uSceneDepthSampler, uv);
    float4 material = uMaterial.SampleLevel(uMaterialSampler, uv, 0.0);
    int model = int((material.a * 255.0) + 0.5) & 0xF;

    // Sky and unlit geometry reflect nothing: they are light, not a surface standing in it.
    //
    // And neither does the first-person rig. The weapon and hands are drawn in the forward subpass, into
    // the lit colour and the depth buffer but NOT the material G-buffer, so at a weapon pixel this pass
    // reads the depth of the weapon (in the foreground sliver) over the material of whatever world stood
    // behind it -- wet ground, a glossy wall. It then judged the weapon a reflector, marched a ray from
    // the surface a few metres out in front of it and composited a reflection of the whole scene onto the
    // arms: the ghosted, see-through weapon with the world sliding across it. The rig occupies depths at
    // or beyond the foreground floor (reverse-Z: nearer than any world surface can be), so cull it here
    // and the reflection lands only on the world it belongs to.
    //
    // Nor does a matte surface. A body is written at full roughness, which already puts it past every
    // ceiling below, but the puddle branch does not ask about roughness at all: it asks whether the
    // normal is level, and a helmet's is.
    if (depth <= 0.0 || model == 0 || depth >= 0.9
        || DecodeMatte(material))
    {
        uReflection.Store(px, float4(reflection, 0.0));
        return;
    }

    // Whether this frame traces at all, which is what decides which of the two answers below is used --
    // and it is one or the other, never both.
    //
    // THE TWO DO NOT MIX, deliberately. A hybrid is tempting and is what this pass did first: march the
    // screen, and put a ray only to whatever the screen could not settle. It reads as the best of both and
    // is not, because the screen's answer is WRONG in a way the ray's is not -- a marched hit is the first
    // surface the depth buffer happens to record along the ray, which is the right surface only when the
    // camera drew it, and the frame carries no way to tell the two apart. So a hybrid picture is part
    // exact and part plausible, with the seam falling wherever the march happened to succeed: one
    // reflection resolved against the geometry, the one beside it against whatever was on screen, and no
    // way for a viewer to know which they are looking at. Where there is a structure to trace, the
    // structure is the answer for every pixel.
    //
    // The screen march is therefore the FALLBACK -- what a pixel gets when r_rt or r_rt_reflections is
    // off, when the adapter cannot trace, or when the level has no world and no lit cards yet. That is its
    // proper place: an approximation for the machines and the moments that cannot have the real thing.
    bool traces = p.trace.x > 0.5;

    // And whether this frame MARCHES instead, which is a third answer and not a shade of either of the two
    // above. The screen march reads whatever the depth buffer happens to record along the ray and can only
    // ever report what the camera drew; a march through the level's distance field reports what is
    // actually there, on or off screen, behind the camera included. So the two DO combine, where the
    // screen and the trace deliberately do not: the screen's answer is kept for every pixel it settled --
    // it is this frame's lighting at full detail -- and the march fills the rest. Before this, the rest
    // was nothing: a metal facing an empty
    // street, or facing the player, reflected the void.
    bool marches = !traces && p.field.x > 0.5;

    float3 groundNormal = normalize(
        (uNormal.SampleLevel(uNormalSampler, uv, 0.0).xyz * 2.0) - 1.0);
    float metallic = material.r;
    float roughness = material.b;

    float3 viewPos = ViewPositionAt(uv, depth, p.unproject);

    // The direction the eye looks along, in world space, and how square-on it meets the surface.
    float3 worldRayToPixel = normalize(mul((float3x3)p.viewToWorld, viewPos));
    float nDotV = max(dot(groundNormal, -worldRayToPixel), 1e-3);

    // What this surface reflects, and how sharply. A PUDDLE -- standing water in a hollow of level ground,
    // not merely wet road -- is a near-mirror whose strength climbs from almost nothing looking straight
    // down to all of the light at a grazing angle. The mask is the shading pass's own, so the reflection
    // lands on exactly the water it drew: the slope gate, the height field filled to the coverage level,
    // the same shoreline sharpness, and the same roofs excluded. Damp-but-dry road does NOT fall here --
    // the shading leaves it at a roughness well above the cutoff below, so it takes the glossy path and is
    // rejected, which is what keeps the mirror to the puddles. Everything smooth enough otherwise reflects
    // by its own material.
    float3 relative = mul((float3x3)p.viewToWorld, viewPos);
    WetParams w = Weather();

    float puddle = 0.0;
    if (w.amount > 0.0 && groundNormal.z > kPuddleLevelCosine && !UnderShelter(relative))
    {
        puddle = PuddleMask(w, relative, groundNormal);
    }

    // The surface the ray is actually reflected about, which for water is not the ground.
    float3 normalW = groundNormal;

    // Whether the SCREEN march may run on this pixel. It keeps its own roughness ceiling even where the
    // field raises the pixel's: the ceiling below is a statement about the march, not about the material,
    // and a screen ray on a surface rougher than that swaps one dark patch of the room for the smooth
    // answer whether or not something better follows it.
    bool screenAllowed = true;

    // Spectral, because a metal's is. See uBaseColour.
    float3 reflectance;
    bool water = puddle > 0.01;

    if (water)
    {
        // The rain's own rings, at the same size, phase and strength the shading rippled this pixel with
        // -- and damped by the same pixel footprint, so near water carries the rings and far water does
        // not pretend to. This is the distortion a real puddle puts through everything it reflects, and
        // its absence is what made the water read as a mirror with a rain effect playing over the top of
        // it rather than as rain falling into water.
        float footprint = WetFootprint(2.0 * p.resolution.z, viewPos.z, p.unproject.x, nDotV);
        WaterSurface surface = WaterSurfaceAt(w, relative, footprint);

        normalW = normalize(lerp(groundNormal, surface.normal, puddle));

        // The shading took this same Fresnel off the water's albedo, so the reflection is added here at
        // exactly the strength the surface stopped scattering -- and faded by however much of the water
        // one ray can still speak for, with the sky the shading laid in holding whatever is left.
        float share = WaterFresnel(normalW, -worldRayToPixel) * puddle * surface.mirror;
        reflectance = float3(share, share, share);
    }
    else
    {
        // The roughest surface that still gets a ray, which is a different number for each of the two
        // answers, for a real reason. The march's ceiling is a statement about the MARCH: a marched ray
        // reads whatever the depth buffer happens to record, so on anything but a near-mirror it swaps one
        // dark patch of the room for the smooth answer and the surface crushes darker. A traced ray has no
        // such failure -- it returns the surface that is actually there -- so it is held to what the
        // MATERIAL claims instead, and nothing else. See r_rt_reflection_roughness.
        float ceiling = (traces || marches) ? p.trace2.x : p.ssr.z;
        screenAllowed = roughness <= p.ssr.z;

        if (roughness > ceiling)
        {
            uReflection.Store(px, float4(reflection, 0.0));
            return;
        }

        // A dielectric's four per cent, and a metal's own colour, which is the whole of the difference
        // between the two in this model: a metal has no diffuse term, so the light it does not absorb it
        // reflects, tinted by what it is made of.
        //
        // Weighted exactly as the shading weighs its own estimate of the environment, which this replaces:
        // the material's own specular level for a dielectric, a cavity's darkening of the grazing edge, and
        // the split-sum average of the specular lobe over the hemisphere at this roughness. Plain Schlick
        // with nothing for roughness went to full white at every grazing angle, so a rough-ish floor seen
        // low came out near white.
        float specularLevel = material.g;
        float3 f0 = lerp(
            float3(0.08 * specularLevel, 0.08 * specularLevel, 0.08 * specularLevel),
            uBaseColour.SampleLevel(uBaseColourSampler, uv, 0.0).rgb,
            metallic);
        float f90 = clamp(50.0 * f0.g, 0.0, 1.0);
        float2 environmentBrdf = EnvironmentBrdf(roughness, nDotV);
        float3 fresnel = (f0 * environmentBrdf.x) + float3(f90, f90, f90) * environmentBrdf.y;

        // Fade out as the surface approaches the ceiling, so the answer does not end on a visible line at
        // that roughness.
        //
        // The march ramps across its WHOLE range and the trace only across the top of it, and the
        // difference between those is the difference between a fade and an attenuation. The march's ramp
        // is one at roughness zero and nothing at the ceiling, so a surface at nine tenths of the ceiling
        // keeps a tenth of its reflection -- and metal, authored at 0.45 under a ceiling of 0.5, is a
        // reflection at a tenth strength: present in the arithmetic and absent from the picture. That ramp
        // is left exactly as it was, because the whole march is calibrated around it and it is the
        // fallback.
        //
        // The trace fades over the top quarter of its own range instead, which is what the word fade meant
        // in the first place: full strength for everything the material file calls reflective, falling
        // away only as the ceiling is approached. A metal at 0.45 under a ceiling of 0.6 is then a metal
        // rather than a tenth of one.
        float fade = (traces || marches)
            ? clamp((ceiling - roughness) / max(ceiling * 0.25, 1e-3), 0.0, 1.0)
            : clamp((ceiling - roughness) / max(ceiling, 1e-3), 0.0, 1.0);

        reflectance = fresnel * fade;

        // A rough surface does not reflect along one line. The ray is aimed along a microfacet normal drawn
        // from the surface's own distribution, a different one every frame, so the frames the temporal
        // resolve averages together cover the lobe; the composite spreads each frame's answer over the
        // footprint the roughness implies. Fired along the mirror direction alone, metal at a roughness of
        // a half reflected the yard as sharply as a mirror -- chrome, where the material asks for brushed.
        if (roughness > 0.05)
        {
            float alpha = roughness * roughness;
            float u1 = FrameNoise(float2(px), p.misc.y);
            float u2 = FrameNoise(float2(px) + float2(31.0, 17.0), p.misc.y);
            float cosTheta = sqrt((1.0 - u1) / max(1.0 + (((alpha * alpha) - 1.0) * u1), 1e-6));
            float sinTheta = sqrt(max(1.0 - (cosTheta * cosTheta), 0.0));
            float phi = 6.2831853 * u2;
            float3 up = abs(groundNormal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
            float3 tangent = normalize(cross(up, groundNormal));
            float3 bitangent = cross(groundNormal, tangent);
            float3 facet = normalize(
                (tangent * (sinTheta * cos(phi))) + (bitangent * (sinTheta * sin(phi))) + (groundNormal * cosTheta));

            // Only a facet that still reflects the view above the surface: one tilted past the horizon
            // sends the ray into the ground, and the surface's own normal answers instead.
            if (dot(reflect(worldRayToPixel, facet), groundNormal) > 0.02)
            {
                normalW = facet;
            }
        }
    }

    if (max(reflectance.r, max(reflectance.g, reflectance.b)) <= 0.003)
    {
        uReflection.Store(px, float4(reflection, 0.0));
        return;
    }

    // The reflected ray, built and marched in view space so each step projects straight to a UV. The
    // normal is rotated world-to-view -- the transpose of the view-to-world rotation, which for a rotation
    // is its inverse -- so it stands in the same space as the incident ray.
    float3 incidentView = normalize(viewPos);
    float3 normalView = normalize(mul(transpose((float3x3)p.viewToWorld), normalW));
    float3 rayView = reflect(incidentView, normalView);

    // A ray turned back toward the camera has nothing ahead of it ON SCREEN to reflect: every sample along
    // it projects behind the eye, where the depth buffer describes nothing. It is not a reason to give up
    // on the pixel -- a surface facing the camera reflects almost entirely backwards, which is what facing
    // you means, so this is most of the front of every metal object a player walks up to and looks at. The
    // march skips it; the trace does not care.
    bool marchable = !traces && rayView.z > 0.0 && screenAllowed;

    int steps = max(int(p.ssr.x + 0.5), 1);
    float reach = p.misc.z;
    float dither = Dither(float2(px), p.misc.y);

    // A step that grows with distance: a fifth of a metre near the camera, widening far out. A uniform
    // metre-long step is tuned for the distant terrain and walks straight over a soldier standing at a
    // puddle a few metres away -- a thin, near thing -- which is why figures did not reflect while
    // buildings did. Fine near the camera catches them; the widening keeps the step budget reaching the
    // far field all the same.
    float t = 0.2 * (0.5 + dither);
    float3 previous = viewPos;

    // the surface begins in FRONT of the scene behind it.
    float previousDiff = -1.0;
    bool have = false;
    float hitEdge = 0.0;
    float2 hitUv = float2(0.0, 0.0);

    for (int i = 0; marchable && i < steps; i++)
    {
        float3 pv = viewPos + (rayView * t);

        // Across the near plane, or past the reach, there is no more to find. (t is the distance travelled
        // along the unit ray, so it IS how far out this sample stands.)
        if (pv.z <= p.unproject.z || t > reach)
        {
            break;
        }

        // Advance by a step that grows with distance, for the next sample.
        t += max(0.2, t * 0.08);

        float2 huv = ViewToUv(pv);
        if (any(huv < float2(0.0, 0.0)) || any(huv > float2(1.0, 1.0)))
        {
            // walked off the screen; the sky the shading laid in stands.
            break;
        }

        // The texel under the step, not a blend of four: at a silhouette a blend of foreground and background
        // is a depth no surface has, and the march found phantom surfaces there -- halos and streaks round
        // every edge it reflected.
        float d = PointDepth(huv);
        if (d <= 0.0)
        {
            // Open sky at this sample: the ray is in clear air, with no surface to cross. It may still
            // descend onto something further along, so keep marching -- but reset the front/behind
            // history, so a later surface reads as a fresh crossing.
            previous = pv;
            previousDiff = -1.0;
            have = true;
            continue;
        }

        float sceneZ = SceneViewZ(d);

        // > 0 : the ray is BEHIND the surface the camera drew.
        float diff = pv.z - sceneZ;

        if (diff > 0.0)
        {
            // The ray has passed behind the drawn surface. The NEAREST surface along the ray is what a
            // reflection shows and what hides everything behind it -- so the FIRST time the ray crosses
            // from in front of the depth buffer to behind it is the hit, whatever the size of the step
            // that carried it across. This is what makes a building or a soldier reflect (they are thin in
            // depth, and the old narrow-band test jumped clean over them to the terrain beyond) and what
            // stops the mountain reflecting through a building that stands in front of it: the building is
            // crossed first, and the march stops there.
            float thickness = max(p.ssr.w, pv.z * 0.05);
            bool crossed = have && previousDiff < 0.0;
            if (crossed || diff < thickness)
            {
                // Halvings between the last in-front sample and this behind one, to land the hit on the
                // surface rather than one coarse step past it.
                float3 a = previous;
                float3 b = pv;
                for (int k = 0; k < 5; k++)
                {
                    float3 mid = (a + b) * 0.5;
                    float md = PointDepth(ViewToUv(mid));
                    if (md > 0.0 && mid.z - SceneViewZ(md) > 0.0)
                    {
                        b = mid;
                    }
                    else
                    {
                        a = mid;
                    }
                }

                hitUv = ViewToUv(b);

                // A crossing is a hit only where the ray met the surface: landed within its thickness once
                // the halvings have closed on it. At a silhouette -- a pole, a soldier -- the depth buffer
                // jumps, and the ray that crosses that jump has only passed BEHIND the thing, whose drawn
                // surface stands metres in front of it. Taken as a hit, every such ray read the pole's
                // colour, and the pole smeared down the whole wet ground behind it. Hidden instead, the ray
                // is left for the world beyond the screen to answer.
                float landed = SampleSceneViewZ(hitUv);
                bool met = landed > 0.0 && (b.z - landed) < thickness;

                // The border vignette: full in the middle of the screen, fading to nothing across the
                // outer fifth, so a reflection does not end on the hard line of the frame.
                float2 sp = (hitUv * 2.0) - 1.0;
                float2 vig = clamp((abs(sp) * 5.0) - 4.0, float2(0.0, 0.0), float2(1.0, 1.0));
                hitEdge = met ? clamp(1.0 - dot(vig, vig), 0.0, 1.0) : 0.0;
            }

            // Behind a surface either way -- a hit, or the ray began already hidden behind something
            // nearer than the far plane. Screen space cannot see past a surface, so the march ends here
            // rather than punching through to whatever lies beyond.
            break;
        }

        previous = pv;
        previousDiff = diff;
        have = true;
    }

    if (hitEdge > 0.0)
    {
        float4 hit = uLit.SampleLevel(uLitSampler, hitUv, 0.0);

        // What the ray landed on has to be part of the WORLD to be worth reflecting. The lit target's
        // alpha says which: the shading writes one for a surface, and everything drawn over the picture
        // rather than into it -- the first-person rig, the selection glow -- writes zero. Without this
        // test a marker drawn over a soldier was mirrored down the whole length of every puddle in front
        // of him, as bright in the water as on the man.
        float world = step(0.5, hit.a);

        // Water reflects at its PHYSICAL strength and glossy surfaces keep the intensity knob. The knob
        // exists because a single screen ray misses most of the world and reflections came out weak
        // overall; a puddle looking at the wall beside it is the case where the ray does land, so the
        // boost lands hardest exactly where nothing was missing. And the water has already paid for this
        // light -- the Fresnel came off its albedo in the shading -- so scaling it is scaling energy that
        // was accounted for.
        float3 scene = max(hit.rgb, float3(0.0, 0.0, 0.0)) * (water ? 1.0 : p.ssr.y);

        // And the sky the shading laid in this same pixel, which the scene now stands in place of.
        // Subtracted with the ray's own weights, so the water ends up showing the sky where the screen had
        // nothing and the building where it had something, rather than both.
        float3 replaced = float3(0.0, 0.0, 0.0);
        if (water)
        {
            float3 reflected = normalize(reflect(worldRayToPixel, normalW));
            replaced = SampleSkyView(uSkyView, uSkyViewSampler, reflected, p.misc.w)
                * WaterSkyward(reflected);
        }

        screenWeight = hitEdge * world;
        reflection = (scene - replaced) * reflectance * screenWeight;
    }

#if OPENIGI_RAY_QUERY

    // The trace. On a frame that has a world to trace against this is the WHOLE of the reflection -- the
    // march above stood down for every pixel, screenWeight is nothing, and what this writes is what the
    // surface shows. See the note beside `traces` for why the two are not mixed.
    if (traces)
    {
        float3 rayW = normalize(reflect(worldRayToPixel, normalW));

        // Off the surface along its own normal, scaled with distance for the reason the sun's trace scales
        // it: the rounding at the origin grows with the magnitude of the position it is happening to, and
        // a ray that begins a hair inside its own surface hits it.
        float3 originRelative = relative + (normalW * (0.02 + (0.0005 * viewPos.z)));

        float3 hitRelative;

        // Declared and ignored. This pass does not include the hit-shading half, so kShadeHits is
        // undefined inside the trace and these come back false and black on every path. Water and metal
        // therefore still resolve from the frame and the cards alone, and a body they cannot see is still
        // a hole in them -- the same gap glass had, left standing here rather than half-closed.
        bool shaded;
        float3 shadedRadiance;
        bool struck = TraceWorld(
            originRelative, rayW, p.trace.y, hitRelative, shaded, shadedRadiance);

        float3 radiance = float3(0.0, 0.0, 0.0);
        bool resolved = false;

        if (struck)
        {
            // The frame first, the cards second. See LitAtHit.
            resolved = LitAtHit(hitRelative, radiance);

            if (!resolved)
            {
                float2 found;
                float2 litFound;
                float agreement;

                // Into the cache's own space, which measures from its centre rather than from the camera.
                // The atlas holds outgoing radiance -- incident light times albedo over pi -- so it is
                // read straight, scaled only by the one control the gather also applies, so that a wall's
                // reflection and the indirect light off that same wall agree about how bright it is.
                //
                // The same check the glazing pass makes, and for the same reason: a card's box is a slab,
                // so a point in the room in front of a wall is inside that wall's box and the lookup
                // returns the wall. A soldier reflected in a puddle took the lighting of whatever he was
                // standing in front of.
                if (LookupCard(hitRelative + p.cacheOrigin.xyz, found, litFound, agreement)
                    && agreement <= kCardSkin)
                {
                    radiance = max(
                        uFinal.SampleLevel(uFinalSampler, litFound, 0.0).rgb,
                        float3(0.0, 0.0, 0.0)) * p.trace.w;
                    resolved = true;
                }
            }
        }

        // A ceiling on one ray, so a single hit on something brilliant cannot flare. Every channel is
        // scaled by the same factor rather than each clipped on its own, so a bright surface keeps its
        // colour instead of sliding towards white -- the same shape the indirect gather uses.
        float brightest = max(radiance.r, max(radiance.g, radiance.b));
        if (brightest > p.trace.z)
        {
            radiance *= p.trace.z / brightest;
        }

        if (resolved)
        {
            // At its own strength: the intensity knob is for the screen march, whose single ray misses much
            // of the world. A traced ray misses nothing, and scaled up it made every reflection of geometry
            // brighter than the geometry, with a step where the reflection went over to sky.
            float3 scene = radiance;

            // And the sky the shading laid in this pixel's water, which the traced hit now stands in place
            // of -- taken back out at exactly this ray's share of it, the same bookkeeping the screen hit
            // above does with its own share. Between them the sky is removed once, and only where
            // something was actually found to replace it.
            float3 replaced = water
                ? SampleSkyView(uSkyView, uSkyViewSampler, rayW, p.misc.w) * WaterSkyward(rayW)
                : float3(0.0, 0.0, 0.0);

            reflection = (scene - replaced) * reflectance;
        }
        else if (!struck && !water)
        {
            // Nothing in the way for as far as the ray reaches: it left for the sky, and carries the sky's
            // light from the direction it left in. Water is excluded because the shading has ALREADY put
            // this term in its pixel -- adding it again is the double count that once made wet ground a
            // stop and a half too bright. A glossy or a metal surface gets no such term from the shading,
            // which is why one facing an empty street came out black rather than showing the sky over it.
            reflection = SampleSkyView(uSkyView, uSkyViewSampler, rayW, p.misc.w) * reflectance
                * FallbackOcclusion(nDotV, roughness, uv);
        }
    }

#else

    // It ADDS to the screen march rather than replacing it, which is the opposite of what the traced half
    // does and is right for the same reason the traced half is right to replace. Against a ray the
    // screen's answer is a guess, and mixing the two leaves a picture part exact and part plausible with
    // no way to tell which is which. Against a march they are two approximations of one thing, and the
    // screen's is the better one wherever it works -- it is this frame's real lighting at full resolution,
    // where a card holds one texel per several centimetres and knows nothing about anything that moves. So
    // the screen keeps every pixel it settled and this fills the remainder, weighted by the confidence the
    // screen march already computes for its own border fade.
    if (marches && screenWeight < 0.999)
    {
        float3 rayW = normalize(reflect(worldRayToPixel, normalW));

        float3 originRelative = relative + (normalW * (0.02 + (0.0005 * viewPos.z)));

        // Whether this ray, if it runs off the end of the field, has found the sky or merely lost a
        // ceiling the clipmaps are far too coarse to hold. Asked once, and used by both the miss and the
        // unresolved hit, so that the two cannot disagree.
        bool lostCeiling = LosesCeiling(
            Roofs(),
            uShelterMap,
            relative.xy + p.wet1.zw,
            relative.z + p.wet2.y,
            rayW);

        float3 hitRelative;
        float voxel;
        bool struck = SurfaceAt(originRelative, normalW, rayW, p.trace.y, hitRelative, voxel);

        float3 radiance = float3(0.0, 0.0, 0.0);

        if (!struck)
        {
            // Nothing in the way for as far as the field describes. Out of doors that means the ray left
            // for the sky and carries the sky's light from the direction it left in.
            //
            // UNDER A ROOF it means no such thing. The clipmaps are quarter-metre voxels at best and
            // coarser further out, and a ceiling is a thin sheet a few metres up: the march goes straight
            // through it and runs out of field on the far side. Answering that with the sky paints
            // daylight -- or, at night, a deep blue -- across the ceiling of every interior in the game.
            // The roof raster is already here for the water; it answers this too.
            float3 sky = SampleSkyView(uSkyView, uSkyViewSampler, rayW, p.misc.w);
            float grey = dot(sky, kLuminance) * kUnknownAlbedo;
            radiance = (lostCeiling ? float3(grey, grey, grey) : sky) * FallbackOcclusion(nDotV, roughness, uv);
        }
        else
        {
            // The frame first, the cards second -- the traced half's order, and for its reasons.
            bool resolved = LitAtHitWithin(hitRelative, voxel, radiance);

            if (!resolved)
            {
                float2 found;
                float2 litFound;
                float agreement;

                // The card has to DESCRIBE the hit and not merely contain it, because a card's box is a
                // slab -- and the bound is widened by the voxel because a marched hit is only that good.
                // Held to the traced tolerance, a marched hit matches almost no card and the surface comes
                // back black.
                float tolerance = kCardSkin + (voxel * 2.0);
                float cardDepth;
                if (LookupCardWithin(
                        hitRelative + p.cacheOrigin.xyz,
                        float3(0.0, 0.0, 0.0),
                        tolerance,
                        found,
                        litFound,
                        agreement,
                        cardDepth)
                    && agreement <= tolerance + (cardDepth * 0.5))
                {
                    radiance = max(
                        uFinal.SampleLevel(uFinalSampler, litFound, 0.0).rgb,
                        float3(0.0, 0.0, 0.0)) * p.trace.w;
                    resolved = true;
                }
            }

            if (!resolved)
            {
                // Struck something the frame cannot see and the bake does not hold. The traced path
                // answers this with nothing and is right to -- it knows precisely what it hit and can
                // shade it from its own material, so reaching that point is a genuine gap. A march has no
                // triangle, no texture and no material, so here it is an ordinary outcome and black would
                // cut a hard silhouette through the reflection.
                //
                // So it is estimated -- but WITHOUT the sky's colour, and that distinction is the whole of
                // this. What is being estimated is a SURFACE, and its colour is exactly the thing not
                // known; borrowing the sky's is not a neutral guess but a specific wrong one. By day it
                // passes. At night the sky is deep blue and nothing else in the level is, so every
                // unresolved pixel came back blue and a window at midnight read as tinted glass --
                // measured against the traced answer at the same viewpoint, a mean of (61, 68, 75) where
                // the trace gives (91, 89, 86). So the sky's BRIGHTNESS is kept, as a fair estimate of the
                // light falling there, and its hue is dropped.
                //
                // Answered exactly as a miss is, so that a marginal hit and a miss are the same colour.
                // The march's hits are not spatially coherent along a grazed surface -- one ray registers
                // and the next does not, in fine bands -- and while the two answers differed those bands
                // were the picture.
                float3 sky = SampleSkyView(uSkyView, uSkyViewSampler, rayW, p.misc.w);
                float grey = dot(sky, kLuminance) * kUnknownAlbedo;
                radiance = (lostCeiling ? float3(grey, grey, grey) : sky) * FallbackOcclusion(nDotV, roughness, uv);
            }
        }

        // A ceiling on one ray, so a single hit on something brilliant cannot flare. Every channel scaled
        // by the same factor rather than each clipped on its own, so a bright surface keeps its colour
        // instead of sliding towards white.
        float brightest = max(radiance.r, max(radiance.g, radiance.b));
        if (brightest > p.trace.z)
        {
            radiance *= p.trace.z / brightest;
        }

        // At its own strength, as the traced half's: the knob is the screen march's.
        float3 scene = radiance;

        // And the sky the shading already laid in this pixel's water, which this answer now stands in
        // place of -- taken back out at this ray's share of it, the same bookkeeping the screen hit does
        // with its own. Between them the sky is removed once. A glossy or a metal surface has no such term
        // to remove, which is why one facing an empty street used to come back black rather than showing
        // the sky over it.
        float3 replaced = water
            ? SampleSkyView(uSkyView, uSkyViewSampler, rayW, p.misc.w) * WaterSkyward(rayW)
            : float3(0.0, 0.0, 0.0);

        reflection += (scene - replaced) * reflectance * (1.0 - screenWeight);
        screenWeight = 1.0;
    }

#endif

    // The alpha tells the composite what to do with it: two and over is a rough surface's reflection, its
    // roughness the excess, to be spread over the footprint that implies; below is a sharp answer to lay
    // down as it is -- water, glass, a mirror.
    float spread = (!water && roughness > 0.05) ? 2.0 + roughness : (traces || marches) ? 1.0 : screenWeight;
    uReflection.Store(px, float4(reflection, spread));
}
