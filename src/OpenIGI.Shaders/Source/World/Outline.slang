// The selection outline: a shell pushed out along the silhouette in screen space, one flat colour.
//
// It skins. A posed mesh is uploaded already deformed, as the two vertex streams every mesh pipeline
// here blends between, and an outline reading only the first would trail a step behind an animating
// soldier. The same locations and the same blend the mesh shaders use, so the shell lands on the pose
// that was actually drawn; a mesh that does not animate reports one buffer as both streams, so its
// delta is zero and the blend cannot move a vertex.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct OutlinePush
{
    float4x4 modelViewProjection;
    float4 colour;

    // x width in pixels, y pose blend, z whether to pin the depth, w the depth.
    float4 control;

    // x width, y height, zw unused.
    float4 viewport;
};

OPENIGI_PUSH_CONSTANT(OutlinePush, push);

struct VertexInput
{
    [[vk::location(0)]] float3 position;
    [[vk::location(1)]] float3 normal;
    [[vk::location(5)]] float3 previousPosition;
    [[vk::location(6)]] float3 previousNormal;
};

/// The vertex stage: push the vertex out along its normal in screen space, then pin the depth.
[shader("vertex")]
float4 vertexMain(VertexInput input) : SV_Position
{
    float blend = push.control.y;
    float3 localPosition = input.position + (blend * (input.previousPosition - input.position));
    float3 localNormal = input.normal + (blend * (input.previousNormal - input.normal));

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));

    // OpenGL clip to Vulkan clip, exactly as every other mesh stage here does it: flip Y, and map Z
    // onto reverse-Z where the near plane is one. It happens BEFORE the pin so the pinned depth is
    // stated in the convention the depth buffer actually holds.
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;

    // Only the normal's screen-facing part can widen a silhouette; its depth component moves the
    // vertex towards or away from the camera, which is not what a rim of constant width wants. The
    // combined transform's upper three-by-three carries the projection and the model-view rotation
    // together, which is the same pair the two separate matrices would have applied -- and the Y is
    // negated to match the flip above.
    //
    // Taken as the transform applied to a direction, which is what a fourth component of zero means
    // and is exactly the upper three-by-three times the vector. Written that way rather than by
    // slicing the matrix, because a matrix constructor fills columns in GLSL and rows in Slang.
    float3 clipNormal = mul(push.modelViewProjection, float4(localNormal, 0.0)).xyz;
    float2 direction = float2(clipNormal.x, -clipNormal.y);
    float lengthSquared = dot(direction, direction);
    direction = lengthSquared > 1e-12 ? direction * rsqrt(lengthSquared) : float2(0.0, 0.0);

    // Clip space spans two units across the viewport and is divided through by w, so a pixel is
    // 2/viewport * w. Doing it here rather than after the divide keeps the offset perspective correct
    // for free.
    clip.xy += direction * push.control.x * 2.0 / push.viewport.xy * clip.w;

    clip.z = lerp(clip.z, push.control.w * clip.w, push.control.z);

    return clip;
}

/// The fragment stage: one flat colour, because a silhouette carries no shading.
[shader("fragment")]
float4 fragmentMain() : SV_Target
{
    return push.colour;
}
