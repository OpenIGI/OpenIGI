// The depth-only box an asynchronous visibility query draws.
//
// A query tests an object's bounding box against the depth already in the buffer. The box is drawn
// with colour writes masked and depth writes off, wrapped in an occlusion query; if any of its
// fragments survive the depth test the object might be visible and is drawn next frame, and if none
// do it was fully behind nearer geometry and is skipped.
//
// The eight corners and the thirty-six indices that wind them into twelve triangles are baked in, so
// the draw needs no vertex or index buffer -- vkCmdDraw(36, ...) and the vertex index select the
// corner. The box spans the object's min and max, so a unit corner is mixed between them.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct OcclusionPush
{
    float4x4 modelViewProjection;
    float4 minimum;
    float4 maximum;
};

OPENIGI_PUSH_CONSTANT(OcclusionPush, push);

static const float3 kCorners[8] = {
    float3(0.0, 0.0, 0.0), float3(1.0, 0.0, 0.0), float3(1.0, 1.0, 0.0), float3(0.0, 1.0, 0.0),
    float3(0.0, 0.0, 1.0), float3(1.0, 0.0, 1.0), float3(1.0, 1.0, 1.0), float3(0.0, 1.0, 1.0),
};

static const int kIndices[36] = {
    0, 2, 1, 0, 3, 2,
    4, 5, 6, 4, 6, 7,
    0, 1, 5, 0, 5, 4,
    1, 2, 6, 1, 6, 5,
    2, 3, 7, 2, 7, 6,
    3, 0, 4, 3, 4, 7,
};

/// The vertex stage: place one of the box's thirty-six corners in clip space.
[shader("vertex")]
float4 vertexMain(uint vertexIndex : SV_VertexID) : SV_Position
{
    float3 corner = kCorners[kIndices[vertexIndex]];
    float3 position = lerp(push.minimum.xyz, push.maximum.xyz, corner);
    float4 clip = mul(push.modelViewProjection, float4(position, 1.0));

    // OpenGL clip to the target's, exactly as the geometry the box is tested against was drawn: flip
    // Y where the target runs downwards, and map Z onto reverse-Z with the near plane at one. Left in
    // OpenGL's convention the box was probed upside down, and its depth ran backwards -- the part
    // nearest the eye fell outside the clip volume, so an object right in front of the camera could
    // come back with no samples and be hidden.
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    return clip;
}

/// The fragment stage: nothing -- the box writes no colour, only exercises the depth test.
[shader("fragment")]
void fragmentMain()
{
}
