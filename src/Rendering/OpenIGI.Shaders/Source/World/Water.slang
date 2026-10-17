// I.G.I. 2's water at its "High Water Quality", as the original draws it.
//
// The original's water is a vs.1.0 vertex shader (WaterRender_azVertexShader[2], igi2 0x6B0950, the program
// WaterRender_Draw picks for quality one with a cube map) and two fixed-function texture stages
// (WaterRender_SetTextureState). Everything is per vertex:
//
//   the Fresnel term   F = 0.0203672 + 0.9796328 * (1 - max(V.N, c14.z))^5
//   the reflection     R = 2 (V.N) N - V, swizzled xzy into the cube's axes
//   the water's light  D = sun * (N.L) * diffuse + ambient      (not clamped; the colour output is)
//   the sun's glint    S = specular * sun * (N.H)^9
//   colour 0           (F, F, F, (1 - F) * shallowness)
//   colour 1           (S * F + D * (1 - F)) * (1 - shallowness)
//
// and the stages are colour 0 times the cube, plus colour 1, with colour 0's alpha. It lands with ONE and
// SRCALPHA (WaterRender_SetBlendState), so the scene behind is kept by (1 - F) of the shallowness: a shore
// is clear, deep water is its own colour, and a grazing view is all reflection either way. Nothing here
// samples the water's own texture -- only the lower quality does.
//
// The fog is the shader's own: 1 / 2^(density * w), the clip w scaled by the ground's density (the
// constant row WaterRender_Draw builds from DrawTerrainMesh_vFogDensity), applied by the fixed-function
// fog against the objects' fog colour. A power of two, where the ground's haze is a power of e.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/SampledColour.slang"

struct WaterPush
{
    float4x4 modelViewProjection;

    // xyz: the eye, in the sheet's own space. w: the smallest V.N the reflection is built with (c14.z,
    // the water's Max reflection angle).
    float4 eyeFacing;

    // xyz: the water's diffuse colour (c20). w: what the finished colour is scaled by over a linear image --
    // the inverse of the exposure the resolve will apply; one where the target is the display.
    float4 diffuseScale;

    // xyz: the water's specular colour (c21). w: the fog's density per metre.
    float4 specularFog;

    // xyz: the colour the distance goes. w: what the normal is scaled by, one above the surface and minus
    // one below it (WaterRender_vNormalScale).
    float4 fogNormalScale;
};

OPENIGI_PUSH_CONSTANT(WaterPush, push);

// The set every mesh pipeline shares. Nothing in it is sampled; it is declared because the set is shared.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;

// The baked reflection, the forward pipeline's cube group.
[[vk::binding(0, 1)]] TextureCube<float4> uEnvironment;
[[vk::binding(1, 1)]] SamplerState uEnvironmentSampler;

// The level's sun (SkyDome_ptDirlight): its direction towards the light (c8) and its front colour (c9), and
// the objects' ambient (c10, Mesh3D_atAmbientColor[1]). Constants of the level; a level with no sun leaves
// them black, as the original sets c8 and c9 to nothing.
[vk::constant_id(1)] const float cSunX = 0.0;
[vk::constant_id(2)] const float cSunY = 0.0;
[vk::constant_id(3)] const float cSunZ = 0.0;
[vk::constant_id(6)] const float cAmbientRed = 0.0;
[vk::constant_id(7)] const float cAmbientGreen = 0.0;
[vk::constant_id(8)] const float cAmbientBlue = 0.0;
[vk::constant_id(9)] const float cSunRed = 0.0;
[vk::constant_id(10)] const float cSunGreen = 0.0;
[vk::constant_id(11)] const float cSunBlue = 0.0;

// Whether the target holds the scene in linear light before exposure; see Mesh3D.slang.
[vk::constant_id(12)] const int cLinearScene = 0;

// c14.xy: Schlick's approximation for water, the reflectance straight on and its complement.
static const float FresnelBias = 0.020367199555039406;
static const float FresnelScale = 0.9796327948570251;

struct WaterVertexInput
{
    [[vk::location(0)]] float3 position;
    [[vk::location(1)]] float3 normal;

    // x: how shallow the water is here (v2.x, the vertex's delta): one at the ground, nought an absorption
    // length or more above it.
    [[vk::location(2)]] float2 shallowness;
    [[vk::location(3)]] float2 lightmapUv;
    [[vk::location(4)]] float4 colorBgra;
    [[vk::location(5)]] float3 previousPosition;
    [[vk::location(6)]] float3 previousNormal;
};

struct WaterVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float4 colour0;
    [[vk::location(1)]] float3 colour1;
    [[vk::location(2)]] float3 reflection;
    [[vk::location(3)]] float fog;
};

/// The vertex stage: WaterRender_azVertexShader[2], instruction for instruction.
[shader("vertex")]
WaterVaryings vertexMain(WaterVertexInput input)
{
    WaterVaryings output;

    float3 sun = float3(cSunX, cSunY, cSunZ);
    float3 sunColour = float3(cSunRed, cSunGreen, cSunBlue);
    float3 normal = input.normal * push.fogNormalScale.w;

    // r0: from the surface to the eye.
    float3 toEye = normalize(push.eyeFacing.xyz - input.position);

    // r1: the water's own light, the sun's cosine unclamped.
    float3 lit = sunColour * dot(normal, sun) * push.diffuseScale.xyz
        + float3(cAmbientRed, cAmbientGreen, cAmbientBlue);

    // r2.x, held to the smallest the water allows, builds the reflection; oT0 is it in the cube's axes.
    float facing = max(dot(toEye, normal), push.eyeFacing.w);
    output.reflection = (normal * (facing * 2.0) - toEye).xzy;

    // r3.w: Schlick's Fresnel.
    float grazing = 1.0 - facing;
    float grazing2 = grazing * grazing;
    float fresnel = (grazing2 * grazing2 * grazing) * FresnelScale + FresnelBias;

    float shallowness = input.shallowness.x;
    output.colour0 = saturate(float4(fresnel, fresnel, fresnel, (1.0 - fresnel) * shallowness));

    // r7.x: the half vector's cosine to the ninth.
    float3 halfway = normalize(toEye + sun);
    float glint = dot(halfway, normal);
    float glint2 = glint * glint;
    float glint8 = glint2 * glint2;
    glint8 *= glint8;
    float3 specular = push.specularFog.xyz * (glint8 * glint) * sunColour;

    // oD1, which Direct3D clamps as it leaves the shader.
    output.colour1 = saturate((specular * fresnel + lit * (1.0 - fresnel)) * (1.0 - shallowness));

    float4 clip = mul(push.modelViewProjection, float4(input.position, 1.0));

    // oFog: rcp(exp(w * density)).
    float density = push.specularFog.w;
    output.fog = density > 0.0 ? saturate(exp2(-density * max(clip.w, 0.0))) : 1.0;

    // OpenGL clip -> Vulkan clip, as the world pipeline does it: flip Y, reverse-Z.
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;
    return output;
}

/// The fragment stage: stage 0 MODULATE(DIFFUSE, TEXTURE) with SELECTARG1(DIFFUSE) alpha, stage 1
/// ADD(SPECULAR, CURRENT), then the fog.
[shader("fragment")]
float4 fragmentMain(WaterVaryings input) : SV_Target
{
    float3 reflected = SampledDisplay(uEnvironment.Sample(uEnvironmentSampler, input.reflection).rgb);
    float3 colour = saturate(input.colour0.rgb * reflected + input.colour1);
    float3 shown = lerp(push.fogNormalScale.xyz, colour, input.fog);

    if (cLinearScene != 0)
    {
        shown = DecodeSrgb(max(shown, float3(0.0, 0.0, 0.0))) * push.diffuseScale.w;
    }

    return float4(shown, input.colour0.a);
}
