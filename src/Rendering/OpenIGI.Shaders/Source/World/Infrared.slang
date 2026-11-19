// A body as I.G.I. 2's infrared view draws it: a heat colour looked up by how squarely each part faces the eye.
//
// Direct3DRender_DrawBoneMesh, while isDrawInfra holds, draws a boned mesh with no lighting and no fog, its colour the
// infrared ramp's alone (Direct3DRender_SetInfrared: COLOROP SELECTARG1 of the texture). The ramp is indexed by the
// camera-space normal: D3DTSS_TCI_CAMERASPACENORMAL through a texture matrix whose only terms carry the normal's depth
// into U and add one, so U runs from nought for a face turned straight at or away from the eye to one edge-on, and
// past one the MIRROR address folds it back. V is nought. The ramp's alpha times the texture factor's (0x80) is the
// coverage of the second, see-through draw.
//
// The texture coordinate is formed per vertex and interpolated, as fixed function forms it, and folded per pixel, as
// the address mode folds it. The engine's view space is the original's: left-handed, +Z into the screen.
//
// It skins as the outline does: the two pose streams blended by the device's pose blend.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/SampledColour.slang"
#include "../Common/TargetEncoding.slang"

struct InfraredPush
{
    float4x4 modelViewProjection;

    // The model-view transform's three rows, which take a normal into view space.
    float4 viewRow0;
    float4 viewRow1;
    float4 viewRow2;

    // x: the pose blend.
    // y: the coverage the ramp's alpha is multiplied by.
    // z: one over the exposure the resolve will apply -- one on the direct path, which is never tone mapped.
    // w: non-zero where the target holds the scene in linear light (the deferred frame's lit image).
    float4 control;
};

OPENIGI_PUSH_CONSTANT(InfraredPush, push);

// The set every mesh pipeline shares: the ramp is bound as the diffuse.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

struct InfraredVertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(2)]] float2 diffuseUv : TEXCOORD2;
    [[vk::location(3)]] float2 lightmapUv : TEXCOORD3;
    [[vk::location(4)]] float4 colorBgra : TEXCOORD4;
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct InfraredVaryings
{
    float4 position : SV_Position;

    // One plus the view-space normal's depth, before the mirror folds it.
    [[vk::location(0)]] float ramp : TEXCOORD0;
};

/// The vertex stage: the mesh where the pose blend puts it, and its ramp coordinate.
[shader("vertex")]
InfraredVaryings vertexMain(InfraredVertexInput input)
{
    float blend = push.control.x;
    float3 localPosition = input.position + (blend * (input.previousPosition - input.position));
    float3 localNormal = input.normal + (blend * (input.previousNormal - input.normal));

    // D3DRS_NORMALIZENORMALS: the normal is taken into view space and made unit length there.
    float3 viewNormal = float3(
        dot(push.viewRow0.xyz, localNormal),
        dot(push.viewRow1.xyz, localNormal),
        dot(push.viewRow2.xyz, localNormal));
    float lengthSquared = dot(viewNormal, viewNormal);
    float depth = lengthSquared > 1e-12 ? viewNormal.z * rsqrt(lengthSquared) : 0.0;

    InfraredVaryings output;
    output.ramp = 1.0 + depth;

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

/// The fragment stage: the ramp's colour, unlit and unfogged.
[shader("fragment")]
float4 fragmentMain(InfraredVaryings input) : SV_Target
{
    // D3DTADDRESS_MIRROR: past one, U runs back down. Held half a texel inside either end, where a mirror's filter and a
    // clamp's agree; the sampler bound repeats, and would blend the far end in.
    uint width;
    uint height;
    uDiffuse.GetDimensions(width, height);
    float u = input.ramp <= 1.0 ? input.ramp : 2.0 - input.ramp;
    float edge = 0.5 / max(float(width), 1.0);
    u = clamp(u, edge, 1.0 - edge);

    float4 sampled = uDiffuse.SampleLevel(uDiffuseSampler, float2(u, 0.0), 0.0);
    float alpha = saturate(sampled.a * push.control.y);

    if (push.control.w != 0.0)
    {
        // Over the linear lit image: divided by the exposure the resolve will multiply back.
        return float4(SampledLinear(sampled.rgb) * push.control.z, alpha);
    }

    return ToTarget(float4(SampledDisplay(sampled.rgb), alpha));
}
