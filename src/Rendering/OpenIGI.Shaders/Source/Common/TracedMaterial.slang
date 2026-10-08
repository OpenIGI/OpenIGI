// Shading a traced hit from its OWN texture, for what neither the frame nor the cards can answer.
//
// The third route, and the only one that can show a person. The other two both ask somebody else what
// colour the hit is: the lit frame, which needs the camera to be able to see the place the ray struck,
// and the surface cache, which needs the place to have been baked. A body satisfies neither -- a window
// in front of you reflects what is behind you, and the cards are a capture of the static level -- so a
// ray would strike a soldier or the player and come back with nothing to say. This reads the hit
// triangle's own texture instead, which needs no camera and no bake.
//
// This is the shadow pass's machinery, pointed at colour. RayTracedShadowPass already walks a hit back to
// its material to test cut-out alpha: the instance's custom index plus the geometry index give the
// record, the record names the vertex and index buffers by device address, and the primitive index and
// barycentrics give the texel. Everything here is that walk, reading VulkanTracedAlbedo where that reads
// the mask array.
//
// The lighting is deliberately cheap and deliberately unshadowed. One Lambert term against the sun plus a
// flat ambient, with no second ray asked about occlusion. A reflection arrives weighted by a Fresnel term
// that is four per cent head-on, and where it is not, it is stretched across a surface seen almost
// edge-on; a shadow ray per reflected hit would roughly double the pass's trace cost to refine something
// at that weight. What this has to get right is that a body in a window is RECOGNISABLE and the right
// colours, not that it is correctly lit.
//
// The host must declare before including: Texture2DArray<float4> uTracedAlbedo with its sampler
// uTracedAlbedoSampler, and the macros kRecords (a uint2, the record buffer's address low word then
// high), kSunDirection (a float3 pointing TOWARDS the sun, in world space), kSunColour and kAmbient.

#ifndef OPENIGI_TRACED_MATERIAL_INCLUDED
#define OPENIGI_TRACED_MATERIAL_INCLUDED

#include "HitRecords.slang"

// The last hit's albedo before anything was done to it, for DebugView.GlassAlbedo. A reflected body that
// comes out flat and colourless has two possible causes that look identical in the finished picture -- an
// albedo layer that was never written, which reads as the array's mid-grey clear, and lighting that has
// crushed a perfectly good texture -- and this is what tells them apart.
static float3 gShadeAlbedo = float3(0.0, 0.0, 0.0);

// Whether the last hit was on geometry that moves. Read by the caller BEFORE it considers a surface-cache
// card: the cache is a bake of the static level, so no card describes a body, and a card's box is a slab
// deep enough that a body standing near a wall sits inside the wall's. Set even where there is no albedo
// layer, because the card is wrong for a body either way.
static bool gHitDynamic = false;

// How strongly the shaded hit glows, as a multiple of its albedo; nought for a surface that does not. A
// lamp is ordinary solid geometry in the traced world, so a reflected ray stops on the tube -- and lit as a
// surface it came out as dark as the fitting round it.
static float gHitEmissive = 0.0;

// The last hit's geometric normal, turned to face the ray that found it, or nought where no record could
// say. The surface cache is sampled BY NORMAL -- the card that photographed a surface is the one that
// looked the way it faces -- so this is what the caller hands the cards. Written before the albedo layer
// is consulted, because a surface with no texture of its own still has cards.
static float3 gHitNormalW = float3(0.0, 0.0, 0.0);

// What a COMMITTED hit looks like, shaded from its own material. False where the hit has no record or no
// albedo layer, which leaves the caller to fall back exactly as it did before.
bool ShadeHit<let F : int>(inout RayQuery<F> query, float3 rayW, out float3 radiance)
{
    radiance = float3(0.0, 0.0, 0.0);
    gHitNormalW = float3(0.0, 0.0, 0.0);

    // No records at all: a structure built before this existed, or one whose records ran out.
    if ((kRecords.x | kRecords.y) == 0u)
    {
        return false;
    }

    // The instance carries where its structure's records start; the geometry index says which of them
    // this run is. A mesh is not a material, so both halves are needed.
    //
    // The COMMITTED hit throughout, not a candidate. The shadow pass asks all of these about a candidate,
    // because its question is whether to accept the hit at all; this asks about the one that was kept.
    // Asking about a candidate reads whichever the traversal happened to look at last, which is not
    // necessarily the hit and after a completed traversal is often nothing at all.
    uint record = uint(query.CommittedInstanceID()) + uint(query.CommittedGeometryIndex());

    ConstBufferPointer<uint> records = HitWordsAt(kRecords.x, kRecords.y);
    uint at = record * kHitRecordWords;

    uint flags = records[at + kHitDynamicWord];
    gHitDynamic = (flags & kHitDynamicBit) != 0u;
    gHitEmissive = float((flags >> kHitEmissiveShift) & 0xFFu) / kHitEmissiveSteps;

    ConstBufferPointer<float> vertices = HitFloatsAt(
        records[at + kHitVertexAddressWord], records[at + kHitVertexAddressWord + 1u]);
    ConstBufferPointer<uint> indices = HitWordsAt(
        records[at + kHitIndexAddressWord], records[at + kHitIndexAddressWord + 1u]);

    // The primitive index counts from the geometry's own first index, which is what the build was given
    // as its primitive offset -- so the first index is added back here.
    uint first = records[at + kHitFirstIndexWord]
        + (uint(query.CommittedPrimitiveIndex()) * 3u);

    uint i0 = HitIndexAt(indices, first + 0u);
    uint i1 = HitIndexAt(indices, first + 1u);
    uint i2 = HitIndexAt(indices, first + 2u);

    float2 bary = query.CommittedTriangleBarycentrics();
    float3 weights = float3(1.0 - bary.x - bary.y, bary.x, bary.y);

    uint v0 = i0 * kHitVertexFloats;
    uint v1 = i1 * kHitVertexFloats;
    uint v2 = i2 * kHitVertexFloats;

    // The GEOMETRIC normal, off the triangle itself. The vertex normals are in the buffer too, but the
    // face normal costs one cross product and cannot be wrong about which way a triangle points -- and
    // under a flat Lambert term at this weight, the difference does not reach the picture.
    float3 p0 = float3(vertices[v0 + 0u], vertices[v0 + 1u], vertices[v0 + 2u]);
    float3 p1 = float3(vertices[v1 + 0u], vertices[v1 + 1u], vertices[v1 + 2u]);
    float3 p2 = float3(vertices[v2 + 0u], vertices[v2 + 1u], vertices[v2 + 2u]);

    float3x4 toWorld = query.CommittedObjectToWorld3x4();
    float3 normalW = normalize(mul((float3x3)toWorld, cross(p1 - p0, p2 - p0)));

    // Turned to face the ray that found it. Winding is not something this can rely on -- a mirrored
    // placement reverses it, and a character's mesh is mirrored down its middle -- so a triangle judged to
    // be facing away would come back black down one half of every body.
    if (dot(normalW, rayW) > 0.0)
    {
        normalW = -normalW;
    }

    gHitNormalW = normalW;

    int layer = int(records[at + kHitAlbedoLayerWord]);
    if (layer < 0)
    {
        // MAGENTA, for DebugView.GlassAlbedo only -- nothing else ever reads this, because the false
        // below means the caller never uses the radiance either.
        //
        // It separates the two ways that view can come out black, which are otherwise identical on screen
        // and have nothing to do with each other: black because this route never ran (the frame or a card
        // answered first, and neither reads a texture), against MAGENTA because the route ran, the ray
        // struck real geometry, and that geometry's material has no albedo layer -- its texture was never
        // registered, or its record was written before the texture existed.
        gShadeAlbedo = float3(1.0, 0.0, 1.0);
        return false;
    }

    float2 uv =
        (float2(vertices[v0 + kHitUvFloat], vertices[v0 + kHitUvFloat + 1u]) * weights.x)
        + (float2(vertices[v1 + kHitUvFloat], vertices[v1 + kHitUvFloat + 1u]) * weights.y)
        + (float2(vertices[v2 + kHitUvFloat], vertices[v2 + kHitUvFloat + 1u]) * weights.z);

    // FILTERED, and read in normalised coordinates rather than by texel.
    //
    // This was a texel fetch, which ignores the sampler and therefore takes the nearest texel. The pass
    // binds a linear sampler, so the fetch was throwing away filtering that was already there and already
    // paid for -- and a reflected body seen at arm's length in a window is MAGNIFIED many times, so every
    // stored texel became a hard-edged block on screen. It read as the reflection being computed at a low
    // resolution rather than as a texture being point sampled.
    //
    // Normalised coordinates also mean nothing here has to know how big the array is, so the resolution
    // stops being a constant two languages have to agree about.
    //
    // AND THE UV IS PASSED WHOLE, past one and all. This was a frac, to wrap a tiling world texture
    // through a sampler that clamps, and the note that stood here called the cost a half-texel blend at
    // exactly u or v = 0 -- one texel of one surface. That was wrong twice over. A frac repeats the
    // texture, so the seam is not at nought, it is at EVERY repeat; and it moves the sample without moving
    // the filter, so at each of those the bilinear tap set straddles the jump and clamps to the edge texel
    // rather than reaching round. One texel of a layer stored at this resolution is a wide stripe, so
    // every repeat drew a hard line: a row of vertical marks up the side of every storage tank and along
    // every facade in the glazing's reflections. The array carries a wrapping sampler of its own now and
    // the hardware does it properly. See VulkanTracedAlbedo.Sampler.
    float3 albedo = uTracedAlbedo.SampleLevel(
        uTracedAlbedoSampler, float3(uv, float(layer)), 0.0).rgb;
    gShadeAlbedo = albedo;

    radiance = albedo * (kAmbient + (kSunColour * max(dot(normalW, kSunDirection), 0.0)));
    return true;
}

#endif
