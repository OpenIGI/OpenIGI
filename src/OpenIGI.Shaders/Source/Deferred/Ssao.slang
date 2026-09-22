// Screen-space ambient occlusion, gathered over a cosine hemisphere and eased onto the previous
// frame's answer.

#include "../Common/ViewDepth.slang"

[[vk::binding(0, 0)]] Texture2D<float4> uDepth;
[[vk::binding(1, 0)]] SamplerState uDepthSampler;

[[vk::binding(2, 0)]] [[vk::image_format("r8")]] WTexture2D<float4> uOcclusion;

[[vk::binding(3, 0)]] Texture2D<float4> uPreviousOcclusion;
[[vk::binding(4, 0)]] SamplerState uPreviousOcclusionSampler;

[[vk::binding(5, 0)]] Texture2D<float4> uPreviousDepth;
[[vk::binding(6, 0)]] SamplerState uPreviousDepthSampler;

struct Params
{
    // this frame's view rotation, camera-relative.
    float4x4 viewToWorld;

    // the previous frame's inverse view rotation.
    float4x4 prevWorldToView;

    // AO width, height, 1/width, 1/height.
    float4 resolution;

    // frame width, height, 1/width, 1/height.
    float4 fullResolution;

    // xScale, yScale, near, far.
    float4 unproject;

    // radius (metres), intensity, bias, power.
    float4 params;

    // this camera minus the previous, w: frame index for the noise.
    float4 cameraDelta;

    // x: whether a history exists, yzw unused.
    float4 temporal;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

float3 ViewPosition(float2 uv)
{
    return ViewPositionAt(uv, uDepth.SampleLevel(uDepthSampler, uv, 0.0).r, p.unproject);
}

// Where a view-space point lands on screen -- the inverse of the reconstruction above.
float2 ViewToUv(float3 v)
{
    return float2(
        0.5 + ((v.x * p.unproject.x) / (2.0 * v.z)),
        0.5 - ((v.y * p.unproject.y) / (2.0 * v.z)));
}

float Hash(float2 v)
{
    return frac(sin(dot(v, float2(12.9898, 78.233))) * 43758.5453);
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

    // Snapped to a full-resolution texel centre, so the half-resolution gather never reads a depth
    // bilinearly mixed across a silhouette -- a mixed depth reconstructs a point hanging in the air
    // between two surfaces, and the occlusion of a point that exists nowhere.
    float2 uv = ((float2(px) * 2.0) + 0.5) * p.fullResolution.zw;
    float depth = uDepth.SampleLevel(uDepthSampler, uv, 0.0).r;

    // The sky (reverse-Z far is zero) is not a surface and is never occluded.
    if (depth <= 0.0)
    {
        uOcclusion.Store(px, float4(1.0, 1.0, 1.0, 1.0));
        return;
    }

    float3 position = ViewPositionAt(uv, depth, p.unproject);

    // A face normal from the neighbouring depths -- a compute shader has no screen-space derivatives.
    // Turned to face the camera: the reconstruction's cross product can come out either way, and a
    // back-facing normal would send the whole hemisphere into the geometry.
    float3 dx = ViewPosition(uv + float2(p.fullResolution.z, 0.0)) - position;
    float3 dy = ViewPosition(uv + float2(0.0, p.fullResolution.w)) - position;
    float3 normal = normalize(cross(dx, dy));
    if (dot(normal, position) > 0.0)
    {
        normal = -normal;
    }

    // A rotation that changes per pixel AND per frame. Per pixel so the fixed directions do not band
    // across the image; per frame so the residual noise is a different pattern each frame and the
    // temporal blend below can integrate it away instead of preserving it for ever.
    float frameIndex = p.cameraDelta.w;
    float angle = Hash(float2(px) + float2(frameIndex * 17.0, frameIndex * 41.0)) * 6.2831853;
    float3 randomVec = float3(cos(angle), sin(angle), 0.0);
    float3 tangent = normalize(randomVec - (normal * dot(randomVec, normal)));
    float3 bitangent = cross(normal, tangent);

    // The radius is an absolute view-space distance: the hemisphere covers a fixed span of the world,
    // and the perspective divide shrinks it on screen as the surface recedes -- the correct
    // screen-space AO behaviour.
    float radius = p.params.x;
    float bias = p.params.z * radius;
    const int kCount = 16;
    float occlusion = 0.0;

    for (int i = 0; i < kCount; i++)
    {
        // A cosine-weighted point in the hemisphere over the surface, jittered per frame through the
        // rotation above and through the per-frame offsets folded into the sample hashes.
        float r1 = Hash(float2(float(i) + 0.13 + (frameIndex * 0.618), float((px.x * 3) + px.y)));
        float r2 = Hash(float2((float(i) * 1.7) + 0.5 + (frameIndex * 0.382), float((px.y * 3) + px.x)));
        float theta = 6.2831853 * r1;
        float disk = sqrt(r2);
        float3 hemi = float3(disk * cos(theta), disk * sin(theta), sqrt(max(1.0 - r2, 0.0)));
        float scale = lerp(0.1, 1.0, float(i) / float(kCount));

        // The tangent frame applied by hand rather than as a matrix. GLSL builds a mat3 from three
        // COLUMNS and Slang from three rows, so writing out the combination says what is meant in
        // either language and cannot be read the wrong way round.
        float3 orientedHemi = (tangent * hemi.x) + (bitangent * hemi.y) + (normal * hemi.z);
        float3 samplePos = position + (orientedHemi * radius * scale);

        float2 sampleUv = ViewToUv(samplePos);
        if (any(sampleUv < float2(0.0, 0.0)) || any(sampleUv > float2(1.0, 1.0)))
        {
            continue;
        }

        float sceneZ = ViewPosition(sampleUv).z;

        // Occluded when the actual surface at the sample's screen position sits in front of the sample
        // point -- closer to the camera, so a smaller view depth. The range check keeps a far surface
        // seen past an edge from casting occlusion it should not.
        float rangeCheck = smoothstep(0.0, 1.0, radius / max(abs(position.z - sceneZ), 1e-4));
        occlusion += (sceneZ <= samplePos.z - bias ? 1.0 : 0.0) * rangeCheck;
    }

    float ao = 1.0 - ((occlusion / float(kCount)) * p.params.y);
    ao = pow(clamp(ao, 0.0, 1.0), max(p.params.w, 0.01));

    // The temporal blend: find where this surface sat on the previous frame's screen, check the previous
    // depth agrees it was this surface, and ease the fresh estimate onto the history.
    if (p.temporal.x > 0.5)
    {
        float3 relativeToCamera = mul(p.viewToWorld, float4(position, 0.0)).xyz;
        float3 relativeToPrevCamera = relativeToCamera + p.cameraDelta.xyz;
        float3 prevView = mul(p.prevWorldToView, float4(relativeToPrevCamera, 0.0)).xyz;

        if (prevView.z > p.unproject.z)
        {
            float2 prevNdc = float2(
                prevView.x * p.unproject.x / prevView.z,
                -prevView.y * p.unproject.y / prevView.z);
            float2 prevUv = (prevNdc * 0.5) + 0.5;

            if (all(prevUv >= float2(0.0, 0.0)) && all(prevUv <= float2(1.0, 1.0)))
            {
                // Validated along the surface normal rather than by a depth ratio: reconstruct where the
                // previous frame actually saw a surface at that pixel, and measure how far off this
                // surface's plane it lies. A grazing wall fails a depth-ratio test the moment the camera
                // moves -- the same trap every temporal pass here has hit.
                float prevDepth = uPreviousDepth.SampleLevel(uPreviousDepthSampler, prevUv, 0.0).r;
                if (prevDepth > 0.0)
                {
                    float3 prevSurface = ViewPositionAt(prevUv, prevDepth, p.unproject);

                    // Both in the previous frame's view space, so the comparison needs no second
                    // rotation; the normal is carried over by the same rotation the point took.
                    float3 prevNormal = normalize(
                        mul(p.prevWorldToView, mul(p.viewToWorld, float4(normal, 0.0))).xyz);
                    float planeDistance = abs(dot(prevSurface - prevView, prevNormal));
                    float pixelSpan = (2.0 * position.z) / (p.unproject.y * p.fullResolution.y);

                    if (planeDistance < max(0.05, 8.0 * pixelSpan))
                    {
                        float history = uPreviousOcclusion.SampleLevel(
                            uPreviousOcclusionSampler, prevUv, 0.0).r;
                        ao = lerp(history, ao, 0.125);
                    }
                }
            }
        }
    }

    uOcclusion.Store(px, float4(ao, ao, ao, ao));
}
