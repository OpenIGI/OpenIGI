// The screen motion of things that move on their own, rasterised over the depth-reprojected motion.
//
// The compute pass behind this derives motion from depth alone, which can only ever describe what the
// CAMERA did. A soldier walking across a still frame has a depth that reprojects onto itself, so that
// pass reports him motionless and the temporal filter accumulates a walking man as though he were a
// wall. This draws every animated mesh again with both frames' transforms and writes the difference.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct ObjectVelocityPush
{
    // this frame's model-view-projection, exactly as the G-buffer drew it.
    float4x4 current;

    // The previous transform arrives as three rows rather than a matrix. Nothing downstream reads the
    // previous depth -- the vector is formed from x, y and w alone -- so its z row would be twelve
    // wasted bytes in a block that is full at 128, and the pose blends go there instead.
    float4 previousX;
    float4 previousY;
    float4 previousW;

    // x: this frame's pose blend, y: the one that names last frame's.
    float4 pose;
};

OPENIGI_PUSH_CONSTANT(ObjectVelocityPush, push);

struct Varyings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float4 currentClip;
    [[vk::location(1)]] float4 previousClip;
};

[shader("vertex")]
Varyings vertexMain(
    [[vk::location(0)]] float3 position,
    [[vk::location(1)]] float3 previousPosition)
{
    Varyings output;

    // Both frames' vertices are mixes of the same two poses, so one delta and two blends give both. A
    // mesh that does not animate has a zero delta and lands on the current position either way, which is
    // what keeps its motion purely the transform's.
    float3 poseDelta = previousPosition - position;
    float4 here = float4(position + (push.pose.x * poseDelta), 1.0);
    float4 was = float4(position + (push.pose.y * poseDelta), 1.0);

    float4 clip = mul(push.current, here);
    output.currentClip = clip;
    output.previousClip = float4(
        dot(push.previousX, was),
        dot(push.previousY, was),
        0.0,
        dot(push.previousW, was));

    // The same OpenGL-to-Vulkan correction the G-buffer vertex stage applies, in the same order, so the
    // rasterised depth is bit-comparable with what that pass stored.
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

// Engine clip space to the image-space uv the velocity consumers use: the engine's Y is up and the
// image's is down, hence the negation -- the same convention the compute pass writes.
float2 ImageUv(float4 clip)
{
    float2 ndc = clip.xy / clip.w;
    return float2((ndc.x * 0.5) + 0.5, (-ndc.y * 0.5) + 0.5);
}

[shader("fragment")]
float2 fragmentMain(Varyings input) : SV_Target
{
    // Behind the previous camera: it had no screen position to have moved from.
    if (input.previousClip.w < 1e-3)
    {
        return float2(0.0, 0.0);
    }

    return ImageUv(input.currentClip) - ImageUv(input.previousClip);
}
