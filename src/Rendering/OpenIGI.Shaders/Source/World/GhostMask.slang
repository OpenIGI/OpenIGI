// A race's ghost: each of its bodies drawn, in the pose it was drawn in, into the ghost's own mask, which Post/Ghost.slang
// then lays over the scene as a runner under an optical cloak. Two targets:
//
//   shape  xy  the surface's normal in view space, its x right and its y up, turned to face the eye: the composite bends the
//              scene behind the body along it;
//          z   its distance ahead of the eye, in metres: the composite compares it with the scene's depth to find a ghost
//              standing behind something, and fades the ghost out as it comes up to the eye;
//          w   one, where any ghost is.
//   skin   x   its height up the body, a body's height to one, so the cloak's sweep rides with it;
//          y   a noise fixed on the body, coarse and blocky, which the ghost materialises and dissolves along;
//          z   its rim: how far the surface turns away from the eye, sharpened;
//          w   unused.
//
// A depth test of its own, so the nearest surface of the ghost is the one in the mask: a normal cannot be taken by the
// greater, as the thermal sensor's heat mask is (World/ThermalMask.slang), whose vertex stage this follows: the two pose
// streams blended by the pose blend.
//
// Where the noise stands above the ghost's presence, the surface is not there yet, or not any more: it is left out.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct GhostMaskPush
{
    float4x4 modelViewProjection;

    // The model-view transform's three rows: a point's place right of, above and ahead of the eye, and a normal's turn along
    // each. Their length is the model's scale, which takes the body's own units into metres.
    float4 rightRow;
    float4 upRow;
    float4 depthRow;

    // x the pose blend, y the seconds the ghost has been shown, z its presence (nought to one), w unused.
    float4 control;
};

OPENIGI_PUSH_CONSTANT(GhostMaskPush, push);

// A body's height, in metres, its skin's height is taken over.
static const float BodyMetres = 1.9;

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

    // The surface's normal in view space, unnormalised.
    [[vk::location(0)]] float3 viewNormal : TEXCOORD0;

    // x its distance ahead of the eye in metres; y its height up the body in metres.
    [[vk::location(1)]] float2 surface : TEXCOORD1;

    // Where it is on the body, in metres of the body's own frame.
    [[vk::location(2)]] float3 bodyMetres : TEXCOORD2;
};

struct GhostMaskTargets
{
    float4 shape : SV_Target0;
    float4 skin : SV_Target1;
};

float Hash3(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

// Value noise over the body: smooth, so the dissolve's front is a line and not a scatter.
float ValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - (2.0 * f));
    float a = lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), u.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), u.x), u.y);
    float b = lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), u.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), u.x), u.y);
    return lerp(a, b, u.z);
}

/// The vertex stage: the body where the pose blend puts it, its normal in view space, how far off it is and where on itself.
[shader("vertex")]
GhostMaskVaryings vertexMain(GhostMaskVertexInput input)
{
    float blend = push.control.x;
    float3 localPosition = input.position + (blend * (input.previousPosition - input.position));
    float3 localNormal = input.normal + (blend * (input.previousNormal - input.normal));
    float scale = length(push.depthRow.xyz);

    GhostMaskVaryings output;
    output.viewNormal = float3(
        dot(push.rightRow.xyz, localNormal),
        dot(push.upRow.xyz, localNormal),
        dot(push.depthRow.xyz, localNormal));
    output.surface = float2(dot(push.depthRow.xyz, localPosition) + push.depthRow.w, localPosition.z * scale);
    output.bodyMetres = localPosition * scale;

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

/// The fragment stage: the shape and the skin, where the ghost is there.
[shader("fragment")]
GhostMaskTargets fragmentMain(GhostMaskVaryings input)
{
    // Coarse value noise with a blockier noise over it: the front it comes and goes along runs smoothly round the body but is
    // cut into shards.
    float3 at = input.bodyMetres;
    float noise = saturate((0.7 * ValueNoise(at * 4.0)) + (0.3 * Hash3(floor(at * 16.0))));
    float presence = push.control.z;
    if (noise > presence * 1.0001)
    {
        discard;
    }

    // Turned to face the eye: the eye looks along +z, so a face towards it has a normal with z below nought.
    float3 n = input.viewNormal;
    float length2 = dot(n, n);
    n = length2 > 1e-12 ? n * rsqrt(length2) : float3(0.0, 0.0, -1.0);
    n = n.z > 0.0 ? -n : n;
    float rim = pow(saturate(1.0 - abs(n.z)), 2.4);

    GhostMaskTargets output;
    output.shape = float4(n.xy, max(input.surface.x, 0.0), 1.0);
    output.skin = float4(saturate(input.surface.y / BodyMetres), noise, rim, 0.0);
    return output;
}
