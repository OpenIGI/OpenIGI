// The sun shadow pass: depth only, but cutting exactly the holes the G-buffer cuts.
//
// A depth-only pass would need no fragment stage at all were it not for cut-outs. The port's windows
// are opaque draws whose glass is a hole in the texture, so a shadow pass that wrote depth for every
// triangle put the whole room behind them in shade -- the office floor caught no sun at all, which is
// the most visible fault in the room. The test has to match the G-buffer's exactly, or a surface
// shadows itself along the edge where the two disagree.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"

struct ShadowPush
{
    float4x4 modelViewProjection;

    // scale, offset, slope, and non-zero only for terrain.
    float4 terrainRamp;

    // xyz where the page sits relative to the eye; w the pose blend.
    float4 terrainOffset;
};

OPENIGI_PUSH_CONSTANT(ShadowPush, push);

// The mesh texture set, shared with the forward and geometry passes: the diffuse and its sampler at
// 0 and 1, the lightmap's pair at 2 and 3, the detail's at 4 and 5. This pass samples only the
// diffuse, and only for its alpha, but the set is one shape for every pipeline that binds it.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;

// The cut-out threshold, baked into the pipeline. D3DRS_ALPHAREF out of 255: the default is the half
// this port always tested at, I.G.I. 2 asks for 64 generally and 32 for foliage. It has to be the
// same value here as in the G-buffer and the forward mesh, or the passes disagree.
[vk::constant_id(0)] const float cAlphaReference = 0.5019608;

struct VertexInput
{
    [[vk::location(0)]] float3 position;
    [[vk::location(1)]] float3 normal;
    [[vk::location(2)]] float2 diffuseUv;
    [[vk::location(3)]] float2 lightmapUv;
    [[vk::location(4)]] float4 colorBgra;
    [[vk::location(5)]] float3 previousPosition;
};

struct Varyings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 diffuseUv;
    [[vk::location(1)]] float alpha;
};

/// The vertex stage: the caster where the frame actually drew it, morph and pose included.
[shader("vertex")]
Varyings vertexMain(VertexInput input)
{
    Varyings output;

    // The terrain casts from where it slid to, not from where it started. The slide is the one the
    // frame itself drew -- measured from the eye, on the target rather than the start -- so the
    // shadow follows the ground rather than a shape it no longer has.
    //
    // A body casts from the pose it is DRAWN in for the same reason: the frame renders between two
    // simulation ticks, and a shadow cast from the tick alone walks at thirty hertz under a body that
    // does not. The delta is zero for anything that is not animated.
    float3 localPosition = input.position + push.terrainOffset.w * (input.previousPosition - input.position);
    float2 diffuseUv = input.diffuseUv;

    if (push.terrainRamp.w > 0.5)
    {
        float3 target = localPosition + push.terrainOffset.xyz + input.normal;
        float reach = max(abs(target.x), max(abs(target.y), abs(target.z)));
        float morph = clamp((reach * push.terrainRamp.x - push.terrainRamp.y) * push.terrainRamp.z, 0.0, 1.0);
        localPosition += input.normal * morph;
        diffuseUv += input.lightmapUv * morph;
    }

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.z + clip.w) * 0.5;
    output.position = clip;
    output.diffuseUv = diffuseUv;

    // The terrain's alpha selects a blend weight rather than a coverage (see TerrainChunkMesh); the
    // ground is solid everywhere and casts everywhere.
    output.alpha = push.terrainRamp.w > 0.5 ? 1.0 : input.colorBgra.a;

    return output;
}

/// The fragment stage: no colour, only the cut-out the G-buffer makes.
[shader("fragment")]
void fragmentMain(Varyings input)
{
    // Exactly the G-buffer's test, vertex colour and all. A shadow pass that cuts on less than the
    // G-buffer does keeps casters the frame never draws, and one that cuts on more drops shadows from
    // things you can see; either way the two disagree and the ground goes patchy.
    if ((uDiffuse.Sample(uDiffuseSampler, input.diffuseUv).a * input.alpha) < cAlphaReference)
    {
        discard;
    }
}
