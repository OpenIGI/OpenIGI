// The contract a mod's post-processing pass is written against: one compute pass over the lit scene, before the tone map.
//
// A mod's module includes this and writes one entry point:
//
//     #include "Mod/PostPass.slang"
//
//     [shader("compute")]
//     [numthreads(8, 8, 1)]
//     void main(uint3 id : SV_DispatchThreadID)
//     {
//         if (any(id.xy >= uint2(p.size.xy))) { return; }
//         float4 colour = uSource[id.xy];
//         uTarget.Store(id.xy, float4(dot(colour.rgb, float3(0.2126, 0.7152, 0.0722)).xxx, colour.a));
//     }
//
// The colour is linear and unexposed -- the radiance the tone map is about to read -- so a pass that darkens, tints or
// grains it is seen through the game's own exposure and grade. The game binds the names below by their names; a module that
// declares a binding the contract does not name, or names one with another shape, is refused as it loads.

// The scene as the passes before this one left it, at the scene's own resolution.
[[vk::binding(0, 0)]] Texture2D<float4> uSource;
[[vk::binding(1, 0)]] SamplerState uSourceSampler;

// The scene's depth: nought is the sky.
[[vk::binding(2, 0)]] DepthTexture2D uDepth;

// Where the pass writes the scene, every texel of it, at the source's resolution, with Store.
[[vk::binding(3, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uTarget;

struct ModPostParams
{
    // the target's width, height, 1/width, 1/height.
    float4 size;

    // seconds since the level began, the frame's number, the near and far planes in metres.
    float4 time;

    // the sixteen values the mod's settings give the pass, in the order its manifest names them.
    float4 values[4];
};

[[vk::binding(4, 0)]] ConstantBuffer<ModPostParams> p;
