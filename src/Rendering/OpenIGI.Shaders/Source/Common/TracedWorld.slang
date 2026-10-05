// Firing the reflected ray itself, for a host compiling its traced variant.
//
// The space is the whole of what is delicate here. Everything in these passes is measured from the
// CAMERA, so that a level tens of kilometres from the world origin still resolves finely in a float; the
// acceleration structure was built where the world actually is. So the camera's absolute position goes
// back in before the ray is fired, and comes back out of the hit.
//
// That is kWorldOrigin, and it is NOT the surface cache's offset -- the cache measures from its own
// centre and has an offset of its own for the card lookup. Using one where the other belongs does not
// shift a reflection slightly: it starts every ray kilometres from any geometry, every ray misses, and
// the surface reflects nothing but the sky fallback. It reads as a tuning problem rather than as a bug,
// which is why both hosts name the two separately and a test pins them apart.
//
// The host must declare before including: RaytracingAccelerationStructure uScene, and the macro
// kWorldOrigin. A host that also defines kShadeHits must include TracedMaterial.slang first. A host that
// defines kTraceCutoutMasks (a Texture2DArray<float4>) and kRecords (the hit records' address, a uint2)
// has its rays test cut-out surfaces against their masks, as the sun's and the lamps' do.

#ifndef OPENIGI_TRACED_WORLD_INCLUDED
#define OPENIGI_TRACED_WORLD_INCLUDED

#ifdef kTraceCutoutMasks
#include "CutoutHit.slang"
#endif

// Where a reflected ray stops, or false if it reached its reach without striking anything.
//
// It also shades the hit from its own material on the way out, where the host asked for that by defining
// kShadeHits before including this. That is here rather than in the caller because the ray query is
// LOCAL: once this function returns, the object that knows which triangle was struck is gone, and the
// alternative is a second traversal to find out again what this one already knows. The two extra outputs
// are written on every path, so a host that shades nothing simply reads false and ignores them.
bool TraceWorld(
    float3 originRelative,
    float3 rayW,
    float reach,
    out float3 hitRelative,
    out bool shaded,
    out float3 shadedRadiance)
{
    hitRelative = originRelative;
    shaded = false;
    shadedRadiance = float3(0.0, 0.0, 0.0);

    RayDesc ray;

    // Into the space the structure was built in. See the remarks: this is the camera's absolute position,
    // never the surface cache's offset.
    ray.Origin = originRelative + kWorldOrigin;
    ray.TMin = 0.01;
    ray.Direction = rayW;
    ray.TMax = max(reach, 1.0);

    // NOT forced opaque. Traversal commits solid geometry on its own; a cut-out surface -- a wire fence, a
    // canopy -- is reported as a candidate, and where the host has the masks it is tested against its own
    // alpha: the wire stops the ray and the gap between wires lets it through, so a fence stands in the
    // puddle as it stands in the yard. Forcing opacity would put a solid board there instead. A host
    // without the masks leaves candidates unconfirmed, and the fence is absent from its reflections.
    RayQuery<RAY_FLAG_NONE> query;

    // Everything EXCEPT the first-person rig. The world is admitted as 0xFF and the rig alone as 0x80, so
    // clearing that bit keeps every world instance and drops the rig.
    //
    // It used to be 0xFF, and that was right while the rig was the only thing the renderer held of the
    // player: a reflection that omitted the weapon in your hands was a reflection of an empty room. It is
    // wrong now. The player's third-person body is in the traced world in first person too, so the arms
    // and the weapon exist TWICE -- once as the body's own, posed where the body is, and once as the rig,
    // floating at the lens. A window showed both, which reads as a second translucent player standing
    // inside the first.
    //
    // The body is the honest one of the pair: it is where the player actually is, it is posed by the same
    // animation everyone else sees, and it is the whole character rather than two arms. So the rig loses.
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);

#ifdef kTraceCutoutMasks
    ProceedWithCutouts(query, kRecords, kTraceCutoutMasks);
#else
    while (query.Proceed())
    {
    }
#endif

    if (query.CommittedStatus() == COMMITTED_NOTHING)
    {
        return false;
    }

    hitRelative = originRelative + (rayW * query.CommittedRayT());

#ifdef kShadeHits
    shaded = ShadeHit(query, rayW, shadedRadiance);
#endif

    return true;
}

#endif
