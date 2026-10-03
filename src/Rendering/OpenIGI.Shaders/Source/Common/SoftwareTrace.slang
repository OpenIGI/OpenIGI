// Whether anything stands between a point and a direction, answered by whatever the adapter can do.
//
// Two forms with one signature, because everything that gathers indirect light asks the same question
// and should not have to know which answer it is getting. Where there is an acceleration structure the
// ray is put to it; where there is not, the level's global distance field is marched. The caller gets a
// visibility and, on a hit, where the ray stopped.
//
// The host declares the distance field's own contract either way (see DistanceField.slang): the traced
// form reads it too, because the reach it fires over is the reach the field would have described, and
// the offset off the surface along its normal is what keeps a ray from striking the surface it left.
// Where OPENIGI_RAY_QUERY is 1 the host must also declare RaytracingAccelerationStructure uScene, the
// cut-out test's Texture2DArray uCutoutMasks and uint2 uCutoutRecords (CutoutHit.slang), and the
// macro kTraceOrigin -- everything here is measured from the pass's own origin and the structure was
// built where the world actually is, so without it the rays start thousands of units from the geometry
// and every one of them misses, which reads as a room flooded with sky.

#ifndef OPENIGI_SOFTWARE_TRACE_INCLUDED
#define OPENIGI_SOFTWARE_TRACE_INCLUDED

#ifndef OPENIGI_RAY_QUERY
#error "OPENIGI_RAY_QUERY must be defined by the shader build, as 1 or 0."
#endif

#if OPENIGI_RAY_QUERY

// How far along a ray a hit is ignored. Some of this game's surfaces have a second, back-facing skin a few
// centimetres in front of the one the rasteriser draws -- a door leaf is two single-sided quads -- which a
// ray leaving the drawn surface strikes from behind. Low-frequency light loses nothing over eight
// centimetres; a door that comes back black from every gather ray loses all of it.
static const float kTraceNearSkip = 0.08;

// The march's own form is left out, so that this one takes the name.
#define OPENIGI_TRACED_VISIBILITY 1
#include "DistanceField.slang"
#include "HitRecords.slang"
#include "CutoutHit.slang"

float VisibilityAt(float3 world, float3 normal, float3 direction, out float3 hit)
{
    hit = world;

    // Off the surface along its own normal, as the march does, so a ray does not strike the surface it
    // left; and over the reach the field would have described, so the two forms agree about how far a
    // gather looks. A level with no field falls back to a reach no level is larger than.
    float finest = uClipmapOrigin[0].w;
    float3 at = world + (normal * finest * 1.5);
    float reach = uClipmapOrigin[0].w * float(uShadowResolution)
        * float(1 << max(uShadowLevels - 1, 0));
    if (reach <= 0.0)
    {
        reach = 4096.0;
    }

    RayDesc ray;

    // Into the space the structure was built in.
    ray.Origin = at + kTraceOrigin;
    ray.TMin = kTraceNearSkip;
    ray.Direction = direction;
    ray.TMax = reach;

    // The NEAREST solid hit.
    //
    // A gather ray does not only ask whether it is blocked: where it stops is looked up in the surface
    // cache, and the light of that card is what the ray brings back. Accepting the first hit the
    // traversal happens to reach returned whichever surface along the ray it found first, which is not
    // the nearest. A ray leaving a building's outside wall could come back with the lamp-lit floor
    // INSIDE the building, and the ground outside a sealed building glowed with its interior's light.
    //
    // And the cut-out surfaces tested for what they are. They used to be reported as candidates and
    // never confirmed, which made them invisible -- right for the glass in a window, wrong for the
    // wall the window is cut into, which is the same texture and so the same surface, and whose light
    // then passed straight through. The mask answers both.
    RayQuery<RAY_FLAG_NONE> query;

    // Everything but the first-person rig. A hand-sized occluder wrapped permanently around the camera
    // would darken whatever the player happened to be facing.
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
    ProceedWithCutouts(query, uCutoutRecords, uCutoutMasks);

    if (query.CommittedStatus() == COMMITTED_NOTHING)
    {
        return 1.0;
    }

    hit = at + (direction * query.CommittedRayT());
    return 0.0;
}

// Whether anything stands between a surface and a point a known distance away: a lamp. A shadow ray, so
// the first solid thing in the way is the whole answer, and it stops short of the target so the fitting
// itself is not its own occluder.
float SegmentVisibility(float3 world, float3 normal, float3 target, float stopShort)
{
    float finest = uClipmapOrigin[0].w;
    float3 at = world + (normal * max(finest * 1.5, 0.03));
    float3 toTarget = target - at;
    float distance = length(toTarget);
    if (distance <= stopShort + kTraceNearSkip + 0.01)
    {
        return 1.0;
    }

    RayDesc ray;
    ray.Origin = at + kTraceOrigin;
    ray.TMin = kTraceNearSkip;
    ray.Direction = toTarget / distance;
    ray.TMax = distance - stopShort;

    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
    ProceedWithCutouts(query, uCutoutRecords, uCutoutMasks);

    return query.CommittedStatus() == COMMITTED_NOTHING ? 1.0 : 0.0;
}

#else

#include "DistanceField.slang"

// The same question marched through the distance field, for an adapter with nothing to trace against.
// Coarser -- a wall thinner than the field's finest voxel can be missed -- but bounded by the segment, so
// it cannot be stopped by what stands behind the lamp.
float SegmentVisibility(float3 world, float3 normal, float3 target, float stopShort)
{
    if (uShadowLevels <= 0)
    {
        return 1.0;
    }

    float finest = uClipmapOrigin[0].w;
    float3 at = world + (normal * finest * 1.5);
    float3 toTarget = target - at;
    float distance = length(toTarget);
    float limit = distance - max(stopShort, finest);
    if (limit <= 0.0)
    {
        return 1.0;
    }

    float3 direction = toTarget / distance;
    float travelled = 0.0;

    for (int step = 0; step < 48 && travelled < limit; step++)
    {
        bool known;
        float raw;
        float resolved;
        float field = DistanceAt(at + (direction * travelled), known, raw, resolved);
        if (!known)
        {
            return 1.0;
        }

        // The same touch test the visibility march uses, so the two cannot disagree about a wall.
        float touchedAt;
        if (FieldStepHits(field, travelled, resolved, touchedAt))
        {
            return 0.0;
        }

        travelled += max(field, resolved * 0.25);
    }

    return 1.0;
}

#endif

// The same question asked by a caller with no use for where the ray stopped -- the sun's own shadow test,
// and the sky's. Written once over whichever form took the name above, so the two cannot answer
// differently.
//
// On the traced form it takes the first solid hit and stops: a shadow needs nothing more, and the nearest
// hit VisibilityAt pays for is only worth it to a caller that looks up what it hit.
float Visibility(float3 world, float3 normal, float3 direction)
{
#if OPENIGI_RAY_QUERY
    float3 at = world + (normal * uClipmapOrigin[0].w * 1.5);
    float reach = uClipmapOrigin[0].w * float(uShadowResolution)
        * float(1 << max(uShadowLevels - 1, 0));

    RayDesc ray;
    ray.Origin = at + kTraceOrigin;
    ray.TMin = kTraceNearSkip;
    ray.Direction = direction;
    ray.TMax = reach > 0.0 ? reach : 4096.0;

    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(uScene, RAY_FLAG_NONE, 0x7F, ray);
    ProceedWithCutouts(query, uCutoutRecords, uCutoutMasks);

    return query.CommittedStatus() == COMMITTED_NOTHING ? 1.0 : 0.0;
#else
    float3 ignored;
    return VisibilityAt(world, normal, direction, ignored);
#endif
}

#endif
