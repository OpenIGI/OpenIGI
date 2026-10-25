// The software lamp trace's answer from a lamp's own shadow map: the face of the map the point falls in, the
// near plane and the edge rules that say when the map cannot answer, and the filter sized as an area light's
// penumbra and compared against the receiver's own plane.
//
// The host must include this after declaring its FieldParams block as `f` (the maps' matrices, depth terms and
// slots), the maps as uLampMaps with uLampMapSampler. Only the software form (OPENIGI_LAMP_FIELD) has maps.

#ifndef OPENIGI_LAMP_MAP_VISIBILITY_INCLUDED
#define OPENIGI_LAMP_MAP_VISIBILITY_INCLUDED

// What LampMapVisibility answers for a lamp that has no fine map: its coarse cube is asked instead.
static const float kNoLampMap = -1.0;

// What it answers for a point outside the lamp's map: the field is asked, never the coarse cube. A batten's
// map looks straight down through ninety degrees, so everything more than forty-five degrees off its axis --
// the far side of the wall the office's lights hang behind, seen from outside -- falls outside it; the coarse
// cube's sixty-four texels a face, with the half metre of offset and bias a texel that size needs at seven
// metres, could not hold a wall a third of a metre thick, and the office's light came through onto the plinth,
// the column and the soffit outside the security building.
static const float kOutsideLampMap = -2.0;


// How much of a lamp reaches a camera-relative point by the lamp's own shadow map, filtered over a texel
// either way; or kNoLampMap where the lamp has none, or kOutsideLampMap where the point is outside it.
// The bias is taken in metres along the lamp's view -- a few centimetres and a hundredth of the distance --
// and only then turned into the map's depth, which a perspective map spends unevenly.
// A map's projection depth terms: ndc depth = x + y / view depth.
float2 LampMapDepthTerms(int slot)
{
    float4 pair = f.mapDepth[slot >> 1];
    return (slot & 1) != 0 ? pair.zw : pair.xy;
}

// A map texel's stored depth as a distance along the lamp's view, through the map's own projection.
float LampMapViewDepth(float stored, float2 terms)
{
    float ndcZ = (stored * 2.0) - 1.0;
    return terms.y / min(ndcZ - terms.x, -1e-6);
}

// A view depth as the map stores it, through the map's own projection.
float LampMapDeviceDepth(float viewZ, float2 terms)
{
    return ((terms.x + (terms.y / max(viewZ, 1e-4))) * 0.5) + 0.5;
}

// The steepest the receiver's plane is followed away from the receiver: depth change per metre across.
static const float kLampMapMaxSlope = 2.0;

// Where the receiver's plane crosses the line of sight through a map texel, as a view depth, for a texel
// `across` metres to the side of the receiver. The plane is followed only as far as a slope of two in one:
// past that it is nearly edge-on to the lamp, and the crossing runs off to anywhere. A recessed wall under a
// slab is edge-on to the lamp above it; followed all the way, half the filter's taps put the wall in front
// of the slab that shadows it, read lit, and the wall glowed where the traced shadow had it wholly dark.
// Held to a slope instead, a grazing receiver keeps its own depth, as a shadow map's slope-scaled bias is
// capped for the same reason.
float LampMapPlaneDepth(float2 uv, float scale, float3 normalView, float planeOffset, float viewZ, float across)
{
    float2 ndcAt = float2((uv.x * 2.0) - 1.0, 1.0 - (uv.y * 2.0));
    float3 ray = float3(ndcAt / scale, 1.0);
    float facing = dot(normalView, ray);
    float z = abs(facing) > 1e-4 ? planeOffset / facing : viewZ;
    float reach = kLampMapMaxSlope * across;
    return clamp(z, viewZ - reach, viewZ + reach);
}

float LampMapVisibility(int lamp, float3 cameraRelative, float3 receiverNormal, float sourceWidth)
{
    int entry = f.mapSlot[lamp >> 2][lamp & 3];
    if (entry < 0)
    {
        return kNoLampMap;
    }

    int slot = entry & 0xFF;
    float4x4 m = f.mapMatrix[slot];
    float4 clip = mul(m, float4(cameraRelative, 1.0));
    float3 ndc = float3(0.0, 0.0, 0.0);

    if ((entry & 0x100) != 0)
    {
        // A lamp that faces nowhere holds a cube of six ninety-degree maps round it; the point is in exactly
        // one of their views, the one along whichever axis it lies furthest out on.
        bool found = false;
        for (int face = 0; face < 6 && !found; face++)
        {
            float4x4 faceMatrix = f.mapMatrix[slot + face];
            float4 faceClip = mul(faceMatrix, float4(cameraRelative, 1.0));
            if (faceClip.w > 1e-4)
            {
                float3 faceNdc = faceClip.xyz / faceClip.w;
                if (all(abs(faceNdc.xy) <= float2(1.0, 1.0)))
                {
                    found = true;
                    slot += face;
                    m = faceMatrix;
                    clip = faceClip;
                    ndc = faceNdc;
                }
            }
        }

        if (!found)
        {
            return kOutsideLampMap;
        }

        // Inside the near plane is inside the lamp's own housing, which is not drawn: nothing there to cast.
        if (ndc.z < -1.0)
        {
            return 1.0;
        }
    }
    else
    {
        // Behind the glass: a ceiling round a batten, the wall a lamp hangs on. The fitting's own aim gives
        // such a point none of its light, so nothing here can be taken away; and the field, asked instead,
        // finds the housing in the way at its resolution and blacked out every ceiling under its lights.
        // The map's near plane, out of its own projection: the camera stands behind the glass and nothing
        // nearer than just past it is drawn.
        float2 nearTerms = LampMapDepthTerms(slot);
        float mapNear = nearTerms.y / (-1.0 - nearTerms.x);
        if (clip.w <= mapNear)
        {
            return 1.0;
        }

        ndc = clip.xyz / clip.w;

        // Past the edge of the map but still in front of the glass: the band between seventy-five degrees
        // off the lamp's axis and its own plane, which a ceiling batten's light only grazes -- the top of a
        // wall beside it. Nothing stands between a fitting and the light that skims off its own face except
        // the fitting, and the field, asked instead, found exactly that housing at its quarter-metre
        // resolution and drew a black band along every wall under the ceiling. Only that close, though:
        // further off, past the edge of a lamp's view is as likely the far side of a wall from it -- a lamp
        // on the outside of a building, facing away -- and answering lit there let its light through the
        // wall in a broad band beside the part its map rightly held dark. Those go to the field instead.
        if (any(abs(ndc.xy) > float2(0.98, 0.98)))
        {
            float lateralScale = max(length(float3(m[0][0], m[0][1], m[0][2])), 1e-3);
            float3 fromLamp = float3(clip.xy / lateralScale, clip.w - mapNear);
            return dot(fromLamp, fromLamp) < (1.5 * 1.5) ? 1.0 : kOutsideLampMap;
        }
    }

    float viewZ = clip.w;
    float scale = max(length(float3(m[0][0], m[0][1], m[0][2])), 1e-3);
    float2 terms = LampMapDepthTerms(slot);
    float nearPlane = terms.y / (-1.0 - terms.x);
    float2 uv = float2((ndc.x * 0.5) + 0.5, 0.5 - (ndc.y * 0.5));

    uint width;
    uint height;
    uint layers;
    uLampMaps.GetDimensions(width, height, layers);
    float texel = 1.0 / float(max(width, 1u));
    int2 size = int2(int(width), int(height));

    // The receiver's own plane, in the map's view: its point, and its normal rotated into the light's
    // frame (the matrix's rows are the light's axes scaled by the projection; its w row is the forward
    // axis unscaled). Every comparison below is made against where THIS plane crosses the tap's own line
    // of sight -- a receiver-plane depth bias. Compared against the centre's depth instead, a filter tap a
    // dozen texels out on a wall the lamp sees edge-on stood metres further away than the centre and the
    // wall shadowed itself: the dark patch on the wall under every floodlight, where the traced shadow had
    // it lit. A constant bias on top then only has to cover the map's own depth quantisation.
    float3 pointView = float3(ndc.x * viewZ / scale, ndc.y * viewZ / scale, viewZ);
    float3 normalView = float3(
        dot(float3(m[0][0], m[0][1], m[0][2]), receiverNormal) / scale,
        dot(float3(m[1][0], m[1][1], m[1][2]), receiverNormal) / scale,
        dot(float3(m[3][0], m[3][1], m[3][2]), receiverNormal));
    float planeOffset = dot(normalView, pointView);
    float texelMetres = viewZ * (2.0 / scale) / float(max(width, 1u));

    float constantBias = 0.015 + (0.003 * viewZ) + (1.5 * texelMetres);

    // The fitting is not a point: a batten is a metre and a half of glass, and the traced shadow aims its
    // rays across all of it, so an edge a long way from its occluder goes soft. The filter is sized as an
    // area light's penumbra would be (percentage-closer soft shadows): the occluders near the receiver's
    // line to the lamp are found and their mean distance taken, then the shadow is filtered over the
    // light's width times how far the receiver lies past them, as similar triangles give it.
    //
    // In a perspective map that width shrinks with the receiver's own distance: the line from the receiver
    // to the edge of a source L wide crosses depth z at 0.5 * L * (zR - z) / (z * zR) map units off the
    // receiver's own texel (L in map units at a metre's distance). Left without the receiver's distance, the
    // filter six metres from a floodlight spread six times too wide, reached past the edge of the slab over a
    // recessed wall into the open ground beyond it, and lit a wall the traced shadow had wholly dark.
    float frustumAtUnit = 2.0 / scale;
    float lightUv = sourceWidth / frustumAtUnit;

    // Searched wide enough to find what stands anywhere in the nearer three quarters of the way to the lamp.
    float searchFrom = max(viewZ * 0.25, nearPlane + 0.01);
    float searchUv = clamp(
        lightUv * 0.5 * max(viewZ - searchFrom, 0.0) / (searchFrom * viewZ), 2.0 * texel, 32.0 * texel);

    float blockerSum = 0.0;
    float blockers = 0.0;
    for (int by = -2; by <= 2; by++)
    {
        for (int bx = -2; bx <= 2; bx++)
        {
            float2 at = uv + (float2(float(bx), float(by)) * (searchUv * 0.5));
            int2 texelAt = clamp(int2(at * float2(size)), int2(0, 0), size - 1);
            float stored = uLampMaps.Load(int4(texelAt, slot, 0));
            float receiverZ = LampMapPlaneDepth(
                at, scale, normalView, planeOffset, viewZ, length(at - uv) * frustumAtUnit * viewZ);
            float limit = LampMapDeviceDepth(max(receiverZ - constantBias, nearPlane + 0.005), terms);
            if (stored < limit)
            {
                blockerSum += LampMapViewDepth(stored, terms);
                blockers += 1.0;
            }
        }
    }

    // Nothing in the way anywhere near: lit, with no filtering to do.
    if (blockers <= 0.0)
    {
        return 1.0;
    }

    float blockerZ = max(blockerSum / blockers, 0.05);
    float penumbraUv = lightUv * max(viewZ - blockerZ, 0.0) / (blockerZ * viewZ);
    float radius = clamp(penumbraUv * 0.5, 1.0 * texel, 24.0 * texel);

    // Twelve points over a disc of that radius, turned per pixel so the pattern does not print.
    float angle = frac(52.9829189 * frac(dot(uv * float2(size), float2(0.06711056, 0.00583715)))) * 6.2831853;
    float s = sin(angle);
    float c = cos(angle);
    const float2 kDisc[12] =
    {
        float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
        float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
        float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
    };

    float sum = 0.0;
    for (int i = 0; i < 12; i++)
    {
        float2 offset = float2((kDisc[i].x * c) - (kDisc[i].y * s), (kDisc[i].x * s) + (kDisc[i].y * c)) * radius;
        float2 at = uv + offset;
        float receiverZ = LampMapPlaneDepth(
            at, scale, normalView, planeOffset, viewZ, length(offset) * frustumAtUnit * viewZ);
        float compare = LampMapDeviceDepth(max(receiverZ - constantBias, nearPlane + 0.005), terms);
        sum += uLampMaps.SampleCmpLevelZero(uLampMapSampler, float3(at, float(slot)), compare);
    }

    return sum / 12.0;
}

#endif
