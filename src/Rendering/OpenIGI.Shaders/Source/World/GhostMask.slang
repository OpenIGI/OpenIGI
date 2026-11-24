// A race's ghost: each of its bodies drawn, in the pose it was drawn in, into the ghost's own mask, which Post/Ghost.slang
// then lays over the scene. Each body writes:
//
//   r  its rim: how far the surface turns away from the eye, sharpened, so the figure's outline and the turn of every limb
//      glow while the middle of it stays clear;
//   g  its scan: thin bright bands climbing the body and one broad slow sweep, in the body's own height, so they ride with it;
//   b  how near it is, 1 / (1 + its distance in metres): the nearer part wins where two overlap, and the composite compares it
//      with the scene's depth to find a ghost standing behind something;
//   a  one, where any ghost is.
//
// No depth test and every channel written by taking the greater, as the thermal sensor's heat mask is (World/ThermalMask.slang),
// whose vertex stage this shares: the two pose streams blended by the pose blend.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct GhostMaskPush
{
    float4x4 modelViewProjection;

    // The model-view transform's third row: a point's distance ahead of the eye, and a normal's turn towards it. Its length is
    // the model's scale, which takes the body's own height into metres.
    float4 depthRow;

    // x the pose blend, y the seconds the ghost has been shown, zw unused.
    float4 control;
};

OPENIGI_PUSH_CONSTANT(GhostMaskPush, push);

struct GhostMaskVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct GhostMaskVaryings
{
    float4 position : SV_Position;

    // x how squarely the surface faces the eye, nought to one; y its distance ahead of the eye in metres; z its height up the
    // body in metres.
    [[vk::location(0)]] float3 surface : TEXCOORD0;
};

/// The vertex stage: the body where the pose blend puts it, how it faces the eye, how far off it is and how high up itself.
[shader("vertex")]
GhostMaskVaryings vertexMain(GhostMaskVertexInput input)
{
    float blend = push.control.x;
    float3 localPosition = input.position + (blend * (input.previousPosition - input.position));
    float3 localNormal = input.normal + (blend * (input.previousNormal - input.normal));

    float rowLength = length(push.depthRow.xyz);
    float normalLength = length(localNormal);
    float facing = rowLength * normalLength > 1e-12
        ? abs(dot(push.depthRow.xyz, localNormal)) / (rowLength * normalLength)
        : 1.0;

    GhostMaskVaryings output;
    output.surface = float3(facing, dot(push.depthRow.xyz, localPosition) + push.depthRow.w, localPosition.z * rowLength);

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

/// The fragment stage: the rim, the scan, the nearness and the coverage.
[shader("fragment")]
float4 fragmentMain(GhostMaskVaryings input) : SV_Target
{
    float seconds = push.control.y;
    float facing = saturate(input.surface.x);
    float rim = pow(1.0 - facing, 2.4);

    // Thin bands six to the metre climbing at a third of a metre a second, and a broad sweep up the body every four seconds.
    float height = input.surface.z;
    float band = frac((height * 6.0) - (seconds * 2.0));
    float bands = exp(-((band * 14.0) * (band * 14.0))) * 0.55;
    float sweepAt = frac((height * 0.32) - (seconds * 0.25)) - 0.5;
    float sweep = exp(-((sweepAt * 10.0) * (sweepAt * 10.0)));
    float scan = max(bands, sweep);

    float nearness = 1.0 / (1.0 + max(input.surface.y, 0.0));
    return float4(rim, scan, nearness, 1.0);
}
