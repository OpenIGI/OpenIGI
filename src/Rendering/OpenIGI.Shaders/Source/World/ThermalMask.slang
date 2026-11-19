// The remastered thermal sensor's heat mask: each body it sees, drawn where it was drawn, as heat.
//
// Not the original's. Its infrared view colours a body by a heat ramp looked up by how squarely the body faces the eye
// (World/Infrared.slang); the remaster's sensor (Post/Thermal.slang) instead reads this mask and makes the picture from it.
// Each body writes:
//
//   r  its surface heat: the body's heat, a little less where the surface turns away from the eye, as skin seen edge-on
//      gives off less towards the sensor than skin seen face-on;
//   g  the body's heat alone, flat, which the sensor spreads into the glow about the body and shows through cover;
//   b  how near it is, 1 / (1 + its distance in metres): the nearer body wins where two overlap, and the sensor compares it
//      with the scene's depth to find a body standing behind something;
//   a  one, where any body is.
//
// No depth test and every channel written by taking the greater, so a body's limbs over its own body merge, and a body behind
// a wall is in the mask as much as one in the open; the sensor decides what of it shows. It skins as the outline does: the
// two pose streams blended by the pose blend.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct ThermalMaskPush
{
    float4x4 modelViewProjection;

    // The model-view transform's third row: a point's distance ahead of the eye, and a normal's turn towards it.
    float4 depthRow;

    // x the pose blend, y the body's heat, zw unused.
    float4 control;
};

OPENIGI_PUSH_CONSTANT(ThermalMaskPush, push);

struct ThermalMaskVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct ThermalMaskVaryings
{
    float4 position : SV_Position;

    // x how squarely the surface faces the eye, nought to one; y its distance ahead of the eye in metres.
    [[vk::location(0)]] float2 surface : TEXCOORD0;
};

/// The vertex stage: the body where the pose blend puts it, how it faces the eye and how far off it is.
[shader("vertex")]
ThermalMaskVaryings vertexMain(ThermalMaskVertexInput input)
{
    float blend = push.control.x;
    float3 localPosition = input.position + (blend * (input.previousPosition - input.position));
    float3 localNormal = input.normal + (blend * (input.previousNormal - input.normal));

    // The row carries the model-view's rotation times its uniform scale, so the normal's turn is the cosine once both lengths
    // are divided out.
    float rowLength = length(push.depthRow.xyz);
    float normalLength = length(localNormal);
    float facing = rowLength * normalLength > 1e-12
        ? abs(dot(push.depthRow.xyz, localNormal)) / (rowLength * normalLength)
        : 1.0;

    ThermalMaskVaryings output;
    output.surface = float2(facing, dot(push.depthRow.xyz, localPosition) + push.depthRow.w);

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

/// The fragment stage: the body's heat, its flat heat, its nearness and its coverage.
[shader("fragment")]
float4 fragmentMain(ThermalMaskVaryings input) : SV_Target
{
    float heat = saturate(push.control.y);

    // Emissivity falls off towards grazing: a body's outline reads a little cooler than its middle, which is what gives a
    // figure shape on a sensor rather than a flat cut-out.
    float facing = saturate(input.surface.x);
    float surface = heat * (0.72 + (0.28 * sqrt(facing)));

    float nearness = 1.0 / (1.0 + max(input.surface.y, 0.0));
    return float4(surface, heat, nearness, 1.0);
}
