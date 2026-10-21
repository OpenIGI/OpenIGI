// Whether a candidate a ray query reports actually stops light: the alpha test for cut-out surfaces,
// shared by every trace that asks whether light gets through.
//
// A cut-out surface -- a wire fence, a railing, and above all a wall whose texture carries its window
// openings -- is built into the acceleration structure non-opaque, so a trace can read its shape from its
// mask. A trace that forces those surfaces opaque turns every window into a wall, and one that ignores
// them walks through every wall that happens to have a window in it. Only testing the mask does both
// right: the solid part of the texture stops the ray and the opening lets it through.
//
// The host must include HitRecords.slang first. It passes the record buffer's address, low word then high,
// and the mask array the records' layers index; a zero address says there are no records, and every
// candidate is then taken as solid, which errs towards a shadow rather than towards a leak.

#ifndef OPENIGI_CUTOUT_HIT_INCLUDED
#define OPENIGI_CUTOUT_HIT_INCLUDED

// Every cut-out mask is stored at this size, so a UV maps to a texel without the array's own extent
// having to be known here.
static const float kCutoutMaskResolution = 256.0;

bool CutoutCandidateIsSolid<let F : int>(inout RayQuery<F> query, uint2 records, Texture2DArray<float4> masks)
{
    if ((records.x | records.y) == 0u)
    {
        return true;
    }

    uint record = uint(query.CandidateInstanceID()) + uint(query.CandidateGeometryIndex());
    HitWords words = HitWordsAt(records.x, records.y);
    uint at = record * kHitRecordWords;

    int layer = int(words[at + kHitMaskLayerWord]);
    if (layer < 0)
    {
        return true;
    }

    HitFloats vertices = HitFloatsAt(
        words[at + kHitVertexAddressWord], words[at + kHitVertexAddressWord + 1u]);
    HitWords indices = HitWordsAt(
        words[at + kHitIndexAddressWord], words[at + kHitIndexAddressWord + 1u]);

    uint first = words[at + kHitFirstIndexWord] + (uint(query.CandidatePrimitiveIndex()) * 3u);

    uint i0 = HitIndexAt(indices, first + 0u);
    uint i1 = HitIndexAt(indices, first + 1u);
    uint i2 = HitIndexAt(indices, first + 2u);

    float2 uv0 = float2(
        vertices[(i0 * kHitVertexFloats) + kHitUvFloat],
        vertices[(i0 * kHitVertexFloats) + kHitUvFloat + 1u]);
    float2 uv1 = float2(
        vertices[(i1 * kHitVertexFloats) + kHitUvFloat],
        vertices[(i1 * kHitVertexFloats) + kHitUvFloat + 1u]);
    float2 uv2 = float2(
        vertices[(i2 * kHitVertexFloats) + kHitUvFloat],
        vertices[(i2 * kHitVertexFloats) + kHitUvFloat + 1u]);

    float2 bary = query.CandidateTriangleBarycentrics();
    float2 uv = (uv0 * (1.0 - bary.x - bary.y)) + (uv1 * bary.x) + (uv2 * bary.y);

    int2 texel = int2(frac(uv) * kCutoutMaskResolution);
    return masks.Load(int4(texel, layer, 0)).r > 0.5;
}

// Runs a query to its end, committing each cut-out candidate whose mask is solid there. What it leaves
// committed is then the answer: for a shadow query built to end on its first hit, whether anything solid
// stands in the way; for a query left to find the nearest, the nearest solid surface.
void ProceedWithCutouts<let F : int>(inout RayQuery<F> query, uint2 records, Texture2DArray<float4> masks)
{
    while (query.Proceed())
    {
        if (query.CandidateType() == CANDIDATE_NON_OPAQUE_TRIANGLE
            && CutoutCandidateIsSolid(query, records, masks))
        {
            query.CommitNonOpaqueTriangleHit();
        }
    }
}

#endif
