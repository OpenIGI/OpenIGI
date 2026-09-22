// Reverse-Z device depth back to view space, which almost every pass that reads the depth buffer needs.
//
// One copy, because the arithmetic is a closed-form inverse of the projection and a pass that gets it
// slightly wrong does not fail: it reconstructs a surface a little way from where it is, and whatever
// the pass then computes about that surface is a little wrong everywhere.
//
// The unproject vector is the projection's x scale, y scale, near and far, in that order.

#ifndef OPENIGI_VIEW_DEPTH_INCLUDED
#define OPENIGI_VIEW_DEPTH_INCLUDED

// The view-space distance a depth sample stands at. Reverse-Z, so the stored depth is mapped back to the
// normalised z the closed form expects.
float ViewDepthAt(float depth, float4 unproject)
{
    float ndcZ = 1.0 - (2.0 * depth);
    return (2.0 * unproject.z * unproject.w)
        / ((unproject.w + unproject.z) - (ndcZ * (unproject.w - unproject.z)));
}

// The whole view-space position. The vertical is negated because the frame was rasterised through the
// vertex stage's Y flip, so a screen coordinate running down maps to a view axis running up.
float3 ViewPositionAt(float2 uv, float depth, float4 unproject)
{
    float viewZ = ViewDepthAt(depth, unproject);
    float2 ndc = (uv * 2.0) - 1.0;
    return float3((ndc.x * viewZ) / unproject.x, (-ndc.y * viewZ) / unproject.y, viewZ);
}

#endif
