// The guides DLSS ray reconstruction reads beside the noisy colour: what each pixel is made of, so it can
// tell the lighting it is to denoise apart from the texture the lighting falls on.
//
// Three images at the render size, from the G-buffer the shading read this same frame:
//
//   diffuse albedo     the colour the diffuse light was multiplied by -- nothing for a metal, which has no
//                      diffuse term;
//   specular albedo    the directional reflectance the specular light was scaled by, NVIDIA's fitted form
//                      of the split-sum average (EnvBRDFApprox2, from Streamline's DLSS-RR programming
//                      guide, section 4.2.1);
//   normal, roughness  the world shading normal, with the material's linear roughness packed into w.
//
// What emits rather than reflects -- the sky, which the depth buffer leaves at nought, and every unlit
// draw (the sky dome, a lamp's glass) -- has no material to separate from its light. All of its colour is
// light leaving it, and it is described as a white diffuse surface with no specular part: remodulated by
// one, the light comes back as it went in. Its normal faces the camera and its roughness is full, so
// nothing about it reads as a mirror.
//
// The first-person rig is drawn after the G-buffer and is never in it. Where it stands in front -- the
// depth buffer's foreground sliver, and a rig surface drawn there by World/RigGuides.slang -- the guides
// are its own: its base colour, its view normal turned into the world, and the one material its key light
// shades it as.

#include "../Common/GBufferEncoding.slang"
#include "../Common/ViewDepth.slang"
#include "../Common/DepthSlot.slang"

OPENIGI_DEPTH_SLOT(0) uDepth;

// Bound through an sRGB view, so a load is already linear.
[[vk::binding(1, 0)]] Texture2D<float4> uBaseColour;
[[vk::binding(2, 0)]] Texture2D<float4> uNormal;

// metallic, specular level, roughness, model and flags.
[[vk::binding(3, 0)]] Texture2D<float4> uMaterial;

[[vk::binding(4, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uDiffuseAlbedo;
[[vk::binding(5, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uSpecularAlbedo;
[[vk::binding(6, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uNormalRoughness;

// The rig's base colour and view-space normal, w one where a rig surface was drawn.
[[vk::binding(8, 0)]] Texture2D<float4> uRigBaseColour;
[[vk::binding(9, 0)]] Texture2D<float4> uRigNormal;

// The finished lit image, and the colour before transparency the rig is written into: the rig is opaque,
// but may have been drawn after the first transparent draw the copy was taken at.
[[vk::binding(10, 0)]] Texture2D<float4> uLit;
[[vk::binding(11, 0)]] [[vk::image_format("rgba16f")]] WTexture2D<float4> uBeforeTransparency;

// Where the first-person layer's depth begins (Viewport.ForegroundDepthFloor): nothing in the world is
// drawn this near.
static const float kForegroundDepthFloor = 0.9;

// The rig's one material, as World/Mesh3D.slang's key light shades it.
static const float kRigRoughness = 0.45;
static const float kRigMetallic = 0.35;

struct Params
{
    // view-space to camera-relative-world rotation.
    float4x4 viewToWorld;

    // xScale, yScale, near, far.
    float4 unproject;

    // width, height, 1/width, 1/height.
    float4 resolution;
};

[[vk::binding(7, 0)]] ConstantBuffer<Params> p;

// Streamline's DLSS-RR programming guide, section 4.2.1: the directional albedo of a GGX lobe with Schlick
// Fresnel, fitted as a scale on F0 and a bias, after Ray Tracing Gems chapter 32. alpha is the squared
// linear roughness. The bias is faded out below a two-per-cent F0 so a surface with no specular at all
// reports none.
float3 EnvBRDFApprox2(float3 specularColour, float alpha, float nDotV)
{
    nDotV = abs(nDotV);

    float4 x = float4(1.0, nDotV, nDotV * nDotV, nDotV * nDotV * nDotV);
    float4 y = float4(1.0, alpha, alpha * alpha, alpha * alpha * alpha);

    float2x2 m1 = float2x2(0.99044, -1.28514, 1.29678, -0.755907);
    float3x3 m2 = float3x3(1.0, 2.92338, 59.4188, 20.3225, -27.0302, 222.592, 121.563, 626.13, 316.627);
    float2x2 m3 = float2x2(0.0365463, 3.32707, 9.0632, -9.04756);
    float3x3 m4 = float3x3(1.0, 3.59685, -1.36772, 9.04401, -16.3174, 9.22949, 5.56589, 19.7886, -20.2123);

    float bias = dot(mul(m1, x.xy), y.xy) / dot(mul(m2, x.xyw), y.xyw);
    float scale = dot(mul(m3, x.xy), y.xy) / dot(mul(m4, x.xzw), y.xyw);
    bias *= saturate(specularColour.g * 50.0);
    return (specularColour * max(0.0, scale)) + max(0.0, bias);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 id : SV_DispatchThreadID)
{
    int2 px = int2(id.xy);
    if (px.x >= int(p.resolution.x) || px.y >= int(p.resolution.y))
    {
        return;
    }

    float2 uv = (float2(px) + 0.5) * p.resolution.zw;
    float depth = OPENIGI_DEPTH_LOAD(uDepth, px);
    float3 viewPos = ViewPositionAt(uv, max(depth, 1e-7), p.unproject);
    float3 toEye = -normalize(mul(p.viewToWorld, float4(viewPos, 0.0)).xyz);

    float4 rigNormal = uRigNormal.Load(int3(px, 0));
    if (depth >= kForegroundDepthFloor && rigNormal.w > 0.5)
    {
        float3 rigBase = uRigBaseColour.Load(int3(px, 0)).rgb;
        float3 viewNormal = normalize(rigNormal.xyz);

        // The rig is a hand's length away and its key light takes the eye as the view axis; so does this.
        float rigNDotV = -viewNormal.z;
        float3 rigF0 = lerp(float3(0.04, 0.04, 0.04), rigBase, kRigMetallic);

        uDiffuseAlbedo.Store(px, float4(rigBase * (1.0 - kRigMetallic), 1.0));
        uSpecularAlbedo.Store(px, float4(EnvBRDFApprox2(rigF0, kRigRoughness * kRigRoughness, rigNDotV), 1.0));
        uNormalRoughness.Store(px, float4(normalize(mul(p.viewToWorld, float4(viewNormal, 0.0)).xyz), kRigRoughness));
        uBeforeTransparency.Store(px, uLit.Load(int3(px, 0)));
        return;
    }

    float4 material = uMaterial.Load(int3(px, 0));
    bool emits = depth <= 0.0 || DecodeShadingModel(material) == kShadingModelUnlit;
    if (emits)
    {
        uDiffuseAlbedo.Store(px, float4(1.0, 1.0, 1.0, 1.0));
        uSpecularAlbedo.Store(px, float4(0.0, 0.0, 0.0, 1.0));
        uNormalRoughness.Store(px, float4(toEye, 1.0));
        return;
    }

    float3 normal = DecodeGBufferNormal(uNormal.Load(int3(px, 0)));
    float3 baseColour = uBaseColour.Load(int3(px, 0)).rgb;

    // As the shading reads the material: a matte surface has no specular and full roughness whatever the
    // channels say, and a dielectric's reflectance at normal incidence is eight per cent at full level.
    bool matte = DecodeMatte(material);
    float metallic = matte ? 0.0 : material.r;
    float specularLevel = matte ? 0.0 : material.g;
    float roughness = matte ? 1.0 : material.b;

    float3 f0 = lerp(float3(0.08, 0.08, 0.08) * specularLevel, baseColour, metallic);
    float nDotV = dot(normal, toEye);

    uDiffuseAlbedo.Store(px, float4(baseColour * (1.0 - metallic), 1.0));
    uSpecularAlbedo.Store(px, float4(EnvBRDFApprox2(f0, roughness * roughness, nDotV), 1.0));
    uNormalRoughness.Store(px, float4(normal, roughness));
}
