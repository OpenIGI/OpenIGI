// The two stages that capture a patch of the world into a page of the card atlas.
//
// A card is a flat window onto part of a model, and capturing it means drawing the model through
// that window with an orthographic camera sitting on the card's own plane. What comes out is not a
// picture of the model lit -- it is the model's MATERIAL, laid out flat: what colour each patch of
// surface is, which way it faces, how rough it is, and how far behind the window it sits.
//
// Nothing here is lit, on purpose. Lighting the atlas is a separate pass that runs every frame over
// the captured material, and folding a light in at capture time would freeze that frame's sun into a
// texture that outlives it.
//
// The alpha channel of the albedo target is the coverage mask, and everything downstream depends on
// it: the atlas is cleared to zero, so a texel that no triangle reached still reads zero there. A
// reader that trusts the normal or the depth without checking coverage first will read the clear
// colour as if it were a surface, and light the empty gutter between two pages.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/SampledColour.slang"

struct CardPush
{
    float4x4 modelViewProjection;
    float4 normal0;
    float4 normal1;
    float4 normal2;

    // roughness, metallic, emissive, unused.
    float4 material;
};

OPENIGI_PUSH_CONSTANT(CardPush, push);

// The mesh texture set, shared with the forward, shadow and geometry passes. This pass reads only
// the diffuse; the rest of the set is declared by the modules that do read it.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;

struct VertexInput
{
    [[vk::location(0)]] float3 position;
    [[vk::location(1)]] float3 normal;
    [[vk::location(2)]] float2 diffuseUv;
    [[vk::location(3)]] float2 lightmapUv;
    [[vk::location(4)]] float4 colorBgra;
};

struct Varyings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 diffuseUv;
    [[vk::location(1)]] float4 color;
    [[vk::location(2)]] float3 modelNormal;
};

/// The four targets a page is captured into, written together.
struct CardTargets
{
    float4 albedo : SV_Target0;
    float4 normal : SV_Target1;
    float4 material : SV_Target2;
    float4 depth : SV_Target3;
};

/// The vertex stage: position through the card's orthographic camera, model normal carried on.
///
/// The normal is left in the model's own space rather than being turned into world space. A card
/// belongs to a model, not to a placement of it -- the same captured page is read by every copy of
/// that model in the level, each rotated differently -- so the only frame the normal can be stored
/// in that stays true for all of them is the model's.
[shader("vertex")]
Varyings vertexMain(VertexInput input)
{
    Varyings output;

    float4 clip = mul(push.modelViewProjection, float4(input.position, 1.0));

    // The same two corrections the G-buffer makes: Vulkan's clip space runs the other way vertically
    // to the one the projection was built for, and depth here is the ordinary zero-to-one range
    // rather than the reversed one the scene uses -- a card's depth is a distance behind a window,
    // and it is read back as such.
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.z + clip.w) * 0.5;
    output.position = clip;

    output.modelNormal = input.normal;
    output.diffuseUv = input.diffuseUv;
    output.color = input.colorBgra.bgra;

    return output;
}

/// The fragment stage: albedo, normal, material and depth, into four targets at once.
[shader("fragment")]
CardTargets fragmentMain(Varyings input)
{
    float4 diffuse = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv);

    // A cut-out must not be captured. Blending it away would leave a half-transparent texel that
    // reads as a surface with the clear colour mixed into it, and a card is sampled, never blended.
    if (diffuse.a < 0.5)
    {
        discard;
    }

    // Linear, as the lighting reads it, and exactly as the G-buffer builds its own: the texture decoded
    // only where the sampler has not already (a second decode squared the curve and the cache lit a world a
    // quarter as bright as the screen), and the vertex colour, which is display-encoded like every authored
    // colour, decoded too. Taken raw, a vertex tint of half was half in the cache and a fifth on screen, and
    // every tinted surface bounced more light than it showed.
    float3 albedo = SampledLinear(diffuse.rgb) * DecodeSrgb(input.color.rgb);

    CardTargets output;
    output.albedo = float4(albedo, 1.0);
    output.normal = float4((normalize(input.modelNormal) * 0.5) + 0.5, 1.0);
    output.material = float4(push.material.x, push.material.y, push.material.z, 1.0);

    // The card's camera is orthographic, so this is already a linear fraction of the card's own
    // depth range rather than something that has to be undone.
    output.depth = float4(input.position.z, 0.0, 0.0, 1.0);

    return output;
}
