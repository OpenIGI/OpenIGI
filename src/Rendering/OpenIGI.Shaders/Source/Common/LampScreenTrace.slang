// The software lamp trace's ray: a short march through the depth buffer for the occluders the camera sees in
// detail, then the level's distance field from where that stopped, read with each level handed smoothly to the
// next, for the walls it cannot.
//
// The host must include this after DistanceField.slang (with its clipmap names defined) and ViewDepth.slang,
// and after declaring the clipmaps as uClipmaps with uClipmapsSampler, its Params block as `p` (the
// unprojection and the view-to-world rotation), and LAMP_DEPTH_AT. Only the software form (OPENIGI_LAMP_FIELD) marches.

#ifndef OPENIGI_LAMP_SCREEN_TRACE_INCLUDED
#define OPENIGI_LAMP_SCREEN_TRACE_INCLUDED

// One level's reading at a point the level holds.
float LampLevelDistance(int level, float3 voxel)
{
    float slice = (float(level) * float(uShadowResolution)) + voxel.z;
    float3 uvw = float3(
        voxel.x / float(uShadowResolution),
        voxel.y / float(uShadowResolution),
        slice / float(uShadowResolution * uShadowLevels));
    float raw = uClipmaps.SampleLevel(uClipmapsSampler, uvw, 0.0).r;
    return ((raw * 2.0) - 1.0) * uClipmapRange[level];
}

// How many voxels from a level's edge its answer starts handing over to the next level out.
static const float kLampLevelBlend = 8.0;

// The field at a point, as DistanceAt reads it, but handed smoothly from each level to the next across the
// outer voxels of the finer one. DistanceAt answers from the finest level holding the point and nothing
// else, and the levels follow the camera: a wall a few metres off went from a quarter-metre voxel to a
// half-metre one the moment a step of the player's put it past the fine level's edge, and its lamp
// shadow -- the thin ones, the near-wall ones -- jumped with it. That was lamp light that changed with
// where the player stood and shimmered while they walked, and held still when they stopped. Each level is
// snapped to the world, so a point's reading in either level is fixed; only the share between them moves,
// and it moves gradually.
float LampDistanceAt(float3 world, out bool known, out float resolved)
{
    known = false;
    resolved = uClipmapOrigin[0].w;

    for (int level = 0; level < uShadowLevels; level++)
    {
        float4 corner = uClipmapOrigin[level];
        float3 voxel = (world - corner.xyz) / corner.w;
        float top = float(uShadowResolution) - 0.5;
        if (any(voxel < float3(0.5, 0.5, 0.5)) || any(voxel > float3(top, top, top)))
        {
            continue;
        }

        known = true;
        float here = LampLevelDistance(level, voxel);
        resolved = corner.w;

        float3 fromEdge = min(voxel - 0.5, top - voxel);
        float edge = min(fromEdge.x, min(fromEdge.y, fromEdge.z));
        float handOver = saturate(1.0 - (edge / kLampLevelBlend));
        if (handOver <= 0.0 || level + 1 >= uShadowLevels)
        {
            return here;
        }

        float4 outer = uClipmapOrigin[level + 1];
        float3 outerVoxel = (world - outer.xyz) / outer.w;
        if (any(outerVoxel < float3(0.5, 0.5, 0.5)) || any(outerVoxel > float3(top, top, top)))
        {
            return here;
        }

        resolved = lerp(corner.w, outer.w, handOver);
        return lerp(here, LampLevelDistance(level + 1, outerVoxel), handOver);
    }

    return 1e6;
}

// The depth buffer is trusted for the first metre only: beyond that a step lands on a pixel whose surface
// may be anything, and the field takes over. Sixteen steps keep each one a few centimetres long.
static const float kScreenReach = 1.0;
static const int kScreenSteps = 16;

float2 ProjectToUv(float3 viewPosition)
{
    float2 ndc = float2(
        (viewPosition.x * p.unproject.x) / viewPosition.z,
        (-viewPosition.y * p.unproject.y) / viewPosition.z);
    return (ndc * 0.5) + 0.5;
}

// Whether anything stands between a point and a point on a lamp, and how far along if so. Positions are
// relative to the camera, as the field's corners are.
//
// receiverLift is how far the ray's start stands off the receiving surface and receiverRise how fast the
// ray climbs away from it (the cosine to its normal); lampDistance is the whole length to the point on the
// lamp and lampRise how squarely the lamp's face looks back down the ray -- negative for a ray reaching it
// from behind the glass, nought for a lamp with no face.
// Together they say how far each sample is from the receiver's own plane and from the plane the lamp is
// mounted on -- see the field's hit test for why that matters.
bool SoftwareBlocked(
    float3 from,
    float3 direction,
    float segment,
    float receiverLift,
    float receiverRise,
    float lampDistance,
    float lampRise,
    out float hitT,
    inout float nearMiss)
{
    hitT = 0.0;

    // The depth buffer, stepped evenly out to kScreenReach. A step that lands behind a surface by less
    // than that surface's thickness has passed through it.
    float screenEnd = min(segment, kScreenReach);
    float screenT = 0.0;
    for (int i = 1; i <= kScreenSteps; i++)
    {
        float t = screenEnd * (float(i) / float(kScreenSteps));
        float3 view = mul(float4(from + (direction * t), 0.0), p.viewToWorld).xyz;
        if (view.z <= p.unproject.z)
        {
            break;
        }

        float2 uv = ProjectToUv(view);
        if (any(uv < float2(0.0, 0.0)) || any(uv > float2(1.0, 1.0)))
        {
            break;
        }

        float depth = LAMP_DEPTH_AT(uv);
        if (depth > 0.0)
        {
            float sceneZ = ViewDepthAt(depth, p.unproject);
            float behind = view.z - sceneZ;
            // Behind by more than two centimetres, so the receiver's own surface is not mistaken for an
            // occluder, and by less than a thickness that grows with distance as depth precision falls:
            // further behind than that the step has passed BEHIND the surface rather than through it.
            if (behind > 0.02 && behind < max(0.05, 0.01 * sceneZ))
            {
                hitT = t;
                return true;
            }
        }

        screenT = t;
    }

    // Then the field, resumed where the screen left off, with half a voxel of bias at each end: the field
    // blurs a surface by about that much, so a ray starting on one, or ending on the fitting, would find it.
    if (uShadowLevels <= 0)
    {
        return false;
    }

    float finest = uClipmapOrigin[0].w;
    float travelled = max(screenT, 0.5 * finest);
    float startT = travelled;
    float end = segment - (0.5 * finest);

    // A receiver the field has swallowed -- a door leaf in its frame, a fitting against its ceiling, both
    // thinner than a voxel -- starts the ray INSIDE the field's solid, and the first sample would call that
    // a hit: the office door read as permanently in shadow. So the ray walks out of whatever solid it starts
    // in before it may be stopped by anything, as far as a voxel and a half of the level it is in.
    bool leaving = true;
    float leaveBy = 0.0;

    // What the field reads at the start, where the nearest surface is the receiver's own: the field's
    // error there -- it can read a point a few centimetres under a ceiling as inside it -- is the same
    // error it makes along the receiver's plane further on, so the plane test below is measured from this
    // reading rather than from the true lift.
    bool startKnown;
    float startResolved;
    float startDistance = LampDistanceAt(from + (direction * travelled), startKnown, startResolved);
    float startTravelled = travelled;

    // And at the end, where the nearest surface is the lamp's own fitting and whatever it is mounted on:
    // the same calibration for the lamp's plane, so a fitting is told from a slab hanging in front of it.
    bool endKnown;
    float endResolved;
    float endDistance = LampDistanceAt(from + (direction * end), endKnown, endResolved);
    for (int step = 0; step < 64 && travelled < end; step++)
    {
        bool known;
        float resolved;
        float distance = LampDistanceAt(from + (direction * travelled), known, resolved);
        if (!known)
        {
            return false;
        }

        if (leaving)
        {
            if (leaveBy <= 0.0)
            {
                leaveBy = travelled + (1.5 * resolved);
            }

            if (distance <= 0.0 && travelled < leaveBy)
            {
                travelled += resolved * 0.25;
                continue;
            }

            leaving = false;
        }

        // A hit once the ray comes within a capture radius of a surface: nothing at the start, so a ray
        // leaving a surface does not catch it again, growing to half a voxel over the first two voxels so
        // a slab a short way off is still caught though the march steps over its thin middle.
        //
        // And only a surface nearer the ray than the receiver's own plane and the lamp's mounting plane. A
        // ray from a ceiling to the batten screwed to it runs a few centimetres under the ceiling all the
        // way, and the field -- a quarter of a metre a voxel at its finest -- calls that touching: every
        // ceiling under its own lights came out black, a desk top thinner than a voxel lost its lamp, and
        // the corners blotched. A sample whose nearest surface is no closer than those planes is only
        // seeing them; something really in the way stands closer to the ray than either.
        float hitRadius = (0.5 * resolved) * saturate(travelled / (2.0 * resolved));
        // A quarter of a voxel past the receiver's own reading. The plane is measured from what the field
        // said at the start, which already carries the field's error about it; a wider margin let a slab
        // standing out of the receiver's own wall -- the canopy under a row of wall lamps -- pass as part
        // of the wall, and the whole wall under it lit.
        float margin = 0.25 * resolved;
        float aboveReceiver = startKnown
            ? startDistance + ((travelled - startTravelled) * receiverRise)
            : receiverLift + (travelled * receiverRise);
        float toLamp = lampDistance - travelled;
        float belowLamp = lampRise <= 0.0 ? 1e9
            : endKnown ? endDistance + ((end - travelled) * lampRise)
            : toLamp * lampRise;

        // The last voxel and a half before the lamp is the fitting itself -- its housing, at the field's
        // resolution -- for a ray that reaches it from BEHIND the glass, as one from the ceiling it hangs on
        // does: that is not shadowed by the lamp it is going to. Any ray in front of the glass keeps every
        // hit, however glancing: a wall lamp facing out over a canopy is reached almost edge-on by a ray
        // climbing the wall below, and the canopy in the way is what keeps that wall dark.
        bool atFitting = lampRise <= 0.0 && toLamp < (1.5 * resolved);
        bool touching = distance < hitRadius;
        if (touching
            && (atFitting || distance >= aboveReceiver - margin || distance >= belowLamp - (0.25 * resolved)))
        {
            // Something within the capture radius that a plane test or the fitting let through; counted for
            // the lampmarch debug view, which is how a leak past a thin slab is told from a slab the field
            // never held at all.
            nearMiss = 1.0;
        }

        if (!atFitting
            && touching
            && distance < aboveReceiver - margin
            && distance < belowLamp - (0.25 * resolved))
        {
            hitT = travelled;
            return true;
        }

        travelled += max(distance, resolved * 0.25);
    }

    return false;
}

#endif
