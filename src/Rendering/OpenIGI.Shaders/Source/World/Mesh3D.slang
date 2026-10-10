// The world pipeline's shader: a mesh to clip space, shaded as diffuse x lightmap x colour --
// the original's direct-path look.
//
// This matches the OpenGL backend's forward mesh shader for the vanilla case: the diffuse
// texture, modulated by a baked lightmap and the vertex colour, with a hard alpha cut-out for
// foliage and fencing. Models with no lightmap bind an opaque white texture so the multiply is
// the identity and there is no branch. The sun-lit view-model path is layered on in the fragment
// stage, and the terrain's level-of-detail morph in the vertex stage; both ride the rows of the
// push block that the other has no use for.
//
// The projection the engine supplies is an OpenGL one -- clip space with Y up and Z in [-1, 1] --
// so the vertex stage corrects it to Vulkan's: Y is flipped and Z is remapped to [0, 1] in clip
// space. Doing it here rather than in the matrix keeps the CPU side using the engine's own
// matrices unchanged; the winding reversal the Y flip causes is compensated by the pipeline's
// clockwise front face.

#include "../Common/PushConstants.slang"
#include "../Common/ClipSpace.slang"
#include "../Common/SampledColour.slang"

// The guide form (World/RigGuides.slang): the same draw, writing what the surface is made of rather than
// how it is lit, for the first-person rig that the G-buffer never sees.
#ifndef OPENIGI_MESH_GUIDES
#define OPENIGI_MESH_GUIDES 0
#endif

// The combined model-view-projection, column-major and in the engine's OpenGL convention,
// and the lit-view-model block. Exactly the guaranteed 128 bytes.
struct MeshPush
{
    float4x4 modelViewProjection;

    // x: what this draw should leave in the lit target's alpha, so that a pass reading the
    //    lit colour back can tell world geometry from geometry that only exists in front of
    //    the eye. Negative means "leave the shaded alpha alone", which is what a translucent
    //    draw needs because its alpha is the blend's own source. Zero is the first-person rig
    //    lit by the key below; TWO is that same rig shaded as ordinary geometry.
    // y: one over the exposure the resolve will apply.
    // z: how much of the previous pose is in the position drawn.
    // w: for the rig, the ambient level under the key light. For a world draw, the x of the
    //    model-space direction towards the fixed-function sun, or zero.
    float4 markerExposure;

    // The model-to-view rotation, row by row; each w carries one channel of the key colour for
    // the rig, and for a world draw the first is the terrain flag and the other two the y and z
    // of the sun direction above. For terrain the first row is the level-of-detail ramp and the
    // second row's xyz is the chunk's origin relative to the eye. The third row is the frame's
    // distance fog for every draw but the lit rig.
    float4 normalKey0;
    float4 normalKey1;
    float4 normalKey2;
};

OPENIGI_PUSH_CONSTANT(MeshPush, push);

// The mesh set carries three textures, each with its own sampler: the diffuse, the baked
// lightmap, and the derived surface detail that only the geometry pass reads. The detail's pair
// is declared in the layout rather than here, because this shader never samples it -- but its
// slots are reserved so that every pipeline sharing the set agrees about the shape.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;

// Set ONE, not another binding on set zero: set zero is the trio every world draw rebinds
// constantly, and a cube beside them would widen a descriptor every mesh pays for.
[[vk::binding(0, 1)]] TextureCube<float4> uEnvironment;
[[vk::binding(1, 1)]] SamplerState uEnvironmentSampler;

// The cut-out threshold, baked into the pipeline. D3DRS_ALPHAREF out of 255.
[vk::constant_id(0)] const float cAlphaReference = 0.5019608;

// Whether this pipeline reflects an environment cube.
[vk::constant_id(1)] const int cReflective = 0;

// The fixed-function sun an object with no baked lightmap is lit by: the level's object
// ambient and its Dirlight's front colour. Constants of the LEVEL, and the push block has
// no room. A title without the light leaves them black, and black lights nothing.
[vk::constant_id(6)] const float cSunAmbientRed = 0.0;
[vk::constant_id(7)] const float cSunAmbientGreen = 0.0;
[vk::constant_id(8)] const float cSunAmbientBlue = 0.0;
[vk::constant_id(9)] const float cSunRed = 0.0;
[vk::constant_id(10)] const float cSunGreen = 0.0;
[vk::constant_id(11)] const float cSunBlue = 0.0;

// Whether the target holds the scene in linear light before exposure (Mesh3DConstants.LinearScene): the
// draw composites at the display encoding as the original did, then decodes once and is divided by the
// exposure the resolve will apply. Written straight into that image instead, an effect was encoded a second
// time on the way out -- washed out and too bright -- and swung with the eye's adaptation.
[vk::constant_id(12)] const int cLinearScene = 0;

struct VertexInput
{
    [[vk::location(0)]] float3 position : TEXCOORD0;
    [[vk::location(1)]] float3 normal : TEXCOORD1;
    [[vk::location(2)]] float2 diffuseUv : TEXCOORD2;
    [[vk::location(3)]] float2 lightmapUv : TEXCOORD3;
    [[vk::location(4)]] float4 colorBgra : TEXCOORD4;

    // The mesh's PREVIOUS pose. The first-person rig is skinned on the processor like a body,
    // and this is the pose it was last given; everything else reports its own buffer here, so
    // its delta is zero to the bit.
    [[vk::location(5)]] float3 previousPosition : TEXCOORD5;
    [[vk::location(6)]] float3 previousNormal : TEXCOORD6;
};

struct Varyings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 diffuseUv : TEXCOORD0;
    [[vk::location(1)]] float2 lightmapUv : TEXCOORD1;
    [[vk::location(2)]] float4 color : TEXCOORD2;
    [[vk::location(3)]] float3 normal : TEXCOORD3;
    [[vk::location(4)]] float fade : TEXCOORD4;

    // How much of this vertex's surface the air leaves, one at the eye and falling with depth.
    [[vk::location(5)]] float fog : TEXCOORD5;
    [[vk::location(6)]] float3 reflection : TEXCOORD6;
};

/// The vertex stage: model space to Vulkan clip space, colour unswizzled from BGRA.
[shader("vertex")]
Varyings vertexMain(VertexInput input)
{
    Varyings output;

    // Base plus blend times delta, not a mix: a mesh that does not animate has a zero delta
    // and comes out at input.position to the bit whatever this is set to.
    float poseBlend = push.markerExposure.z;
    float3 localPosition = input.position + poseBlend * (input.previousPosition - input.position);
    float3 localNormal = input.normal + poseBlend * (input.previousNormal - input.normal);

    // The reflection direction, built in MODEL space and left there. A reflective draw here is
    // water, and I.G.I. 2 draws its water camera-relative: the model origin IS the eye, so the
    // vector from eye to surface is the vertex's own position and the model axes are the
    // world's. A cube lookup takes a direction and ignores translation.
    //
    // R = 2 * (V . N) * N - V, with V pointing from the surface to the eye.
    output.reflection = float3(0.0, 0.0, 0.0);
    if (cReflective != 0)
    {
        float3 toEye = normalize(-localPosition);
        float3 unitNormal = normalize(localNormal);
        output.reflection = (2.0 * dot(toEye, unitNormal) * unitNormal) - toEye;
    }

    float2 diffuseUv = input.diffuseUv;
    output.fade = 1.0;

    // The terrain's seam morph, as the original computes it (sub_512C30). The normal channel
    // carries the offset to where the vertex sits at the next level of detail and the lightmap
    // channel the texture offset that goes with it; the vertex slides onto that far position as
    // its distance from the eye approaches the level's far edge, so a chunk meets its coarser
    // neighbour at a shared vertex instead of leaving a crack.
    float terrain = abs(push.markerExposure.x) > 0.5 ? push.normalKey0.w : 0.0;
    if (terrain > 0.5)
    {
        float3 ramp = push.normalKey0.xyz;
        float3 fromCamera = input.position + push.normalKey1.xyz;
        float3 target = fromCamera + input.normal;
        float reach = max(abs(target.x), max(abs(target.y), abs(target.z)));
        float morph = clamp((reach * ramp.x - ramp.y) * ramp.z, 0.0, 1.0);
        localPosition = input.position + input.normal * morph;
        diffuseUv += input.lightmapUv * morph;

        // The weight this pass carries at this vertex: the colour's alpha selects one -- nothing,
        // the fade, its complement, or one -- for how this vertex's material relates to the
        // pass's, and the pass's texture scale then takes the complement for the dense scale and
        // the fade for the sparse scale. Terrain passes add, and at every vertex these weights
        // sum to one across every pass and both scales.
        float selector = input.colorBgra.a * 3.0;
        float material = selector < 0.5 ? 0.0 : (selector < 1.5 ? morph : (selector < 2.5 ? 1.0 - morph : 1.0));
        output.fade = material * (terrain > 1.5 ? 1.0 - morph : morph);
    }

    float4 clip = mul(push.modelViewProjection, float4(localPosition, 1.0));

    // The original's distance fog, exp(-density * z) per vertex, evaluated where it evaluates
    // it: the view-space depth, which for this projection is exactly the clip w. The lit rig is
    // excluded -- it is the marker's zero and the only draw whose third push row is a rotation.
    float fogDensity = (abs(push.markerExposure.x) > 0.5 && push.markerExposure.x < 1.5)
        ? push.normalKey2.w
        : 0.0;
    output.fog = fogDensity > 0.0 ? clamp(exp(-fogDensity * max(clip.w, 0.0)), 0.0, 1.0) : 1.0;

    // OpenGL clip -> Vulkan clip: flip Y, and map Z onto reverse-Z [1, 0] (near = 1, far = 0).
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;

    output.diffuseUv = diffuseUv;
    output.lightmapUv = input.lightmapUv;

    // The terrain's alpha is the weight selector read above, not a coverage: the ground goes
    // through white and opaque, and the weight scales the colour instead.
    output.color = terrain > 0.5 ? float4(1.0, 1.0, 1.0, 1.0) : input.colorBgra.bgra;

    // The sun on an object with no bake, as Direct3D's vertex pipeline computed it for I.G.I. 2
    // (Direct3DRender_SetGenericStateRigid, igi2ed 0x56CA90): the global ambient plus the
    // light's diffuse times the cosine to the normal, saturated to one.
    float3 sunModel = float3(push.markerExposure.w, push.normalKey1.w, push.normalKey2.w);
    if (abs(push.markerExposure.x) > 0.5 && terrain < 0.5 && dot(sunModel, sunModel) > 0.25)
    {
        float normalLength = length(localNormal);
        float facing = normalLength > 0.0 ? dot(localNormal / normalLength, sunModel) : 0.0;
        float3 sunlight = float3(cSunAmbientRed, cSunAmbientGreen, cSunAmbientBlue)
            + float3(cSunRed, cSunGreen, cSunBlue) * max(facing, 0.0);
        output.color.rgb *= clamp(sunlight, 0.0, 1.0);
    }

    output.normal = float3(
        dot(push.normalKey0.xyz, localNormal),
        dot(push.normalKey1.xyz, localNormal),
        dot(push.normalKey2.xyz, localNormal));

    return output;
}

#if OPENIGI_MESH_GUIDES

struct GuideTargets
{
    // The surface's colour in linear light, before any light: what the key light below multiplies.
    float4 baseColour : SV_Target0;

    // The view-space shading normal, with one in w to say a rig surface is here.
    float4 normal : SV_Target1;
};

/// The guide stage: the rig's base colour and normal, exactly as the lit stage below derives them.
[shader("fragment")]
GuideTargets fragmentMain(Varyings input)
{
    float4 diffuse = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv);
    if (diffuse.a < cAlphaReference)
    {
        discard;
    }

    float3 lightmap = SampledDisplay(uLightmap.Sample(uLightmapSampler, input.lightmapUv).rgb);
    float3 base = SampledDisplay(diffuse.rgb) * lightmap * input.color.rgb * input.fade;

    GuideTargets output;
    output.baseColour = float4(DecodeSrgb(max(base, float3(0.0, 0.0, 0.0))), 1.0);
    output.normal = float4(normalize(input.normal), 1.0);
    return output;
}

#else

/// The fragment stage: the diffuse sample modulated by the lightmap and vertex colour,
/// alpha-tested, with the view-model key light layered on for the first-person rig.
[shader("fragment")]
float4 fragmentMain(Varyings input) : SV_Target
{
    float4 diffuse = uDiffuse.Sample(uDiffuseSampler, input.diffuseUv);

    // An opaque draw's alpha is unused by the blend, so it carries the marker instead: one for
    // the world, zero for what is only in front of the eye. A translucent draw needs its real
    // alpha and asks for it with a negative marker.
    float marker = push.markerExposure.x;
    bool translucent = marker < 0.0;

    // The cut-out test is on the TEXTURE alpha alone -- foliage and fencing carry strictly
    // 0-or-255 texture alpha. And it is for OPAQUE draws only: a translucent draw carries its
    // shape in that same channel as a smooth ramp, and testing it threw away every rain
    // streak's soft edge and tapered tail. A translucent draw blends its alpha; it must never
    // test it.
    if (!translucent && diffuse.a < cAlphaReference)
    {
        discard;
    }

    // The original composites at the encoding its texels were authored in, and so does this where the
    // target stores what it is given: a sample an sRGB image has decoded is put back first. See
    // SampledColour.slang, and Mesh3DConstants.Describe for the target where it is not.
    float3 lightmap = SampledDisplay(uLightmap.Sample(uLightmapSampler, input.lightmapUv).rgb);

    // The level's flat light on models, and the two draws it is not for: terrain, which is lit
    // by its materials, and anything blended, which the original draws with Direct3D lighting
    // off. White everywhere else it does not apply, so this is a multiply and not a branch.
    float3 modelTint = (push.normalKey0.w > 0.5 || marker < 0.5) ? float3(1.0, 1.0, 1.0) : push.normalKey1.xyz;

    // What the sampled lightmap is multiplied by before it modulates the texture: the device's
    // lightmap scale, which a WORLD draw carries in the exposure slot it has no other use for.
    // The rig reads that same slot as its exposure, which is why this is gated on the marker.
    float lightmapGain = (abs(marker) > 0.5 && cLinearScene == 0) ? push.markerExposure.y : 1.0;

    float3 base = SampledDisplay(diffuse.rgb) * lightmap * lightmapGain * input.color.rgb * input.fade * modelTint;

    // The baked reflection, added over the surface's own colour. Additive because that is what
    // the original's cube stage does. The .xzy swizzle puts the engine's Z-up faces where a cube
    // sampler expects them.
    if (cReflective != 0)
    {
        base += SampledDisplay(uEnvironment.Sample(uEnvironmentSampler, normalize(input.reflection).xzy).rgb);
    }

    // The unlit rig's marker is a shading instruction, not an alpha: what it leaves behind is
    // the rig's own zero, so the metering and the indirect gather still know to skip it.
    float alpha = translucent ? (diffuse.a * input.color.a) : (marker > 1.5 ? 0.0 : marker);

    // The key light below is for the first-person rig and nothing else, and the marker already
    // says which draw that is: a lit draw is exactly an opaque one that is only in front of the
    // eye, which is the marker's zero. Every other marker returns here -- the world, anything
    // translucent, and the UNLIT rig's two, which is the rig shaded as ordinary geometry. Two
    // once fell through into the key below with no key to light it, and the vanilla rig was a
    // black silhouette.
    if (abs(marker) > 0.5)
    {
        // What the hardware fog did for the original: the surface faded into the air's own
        // colour, which an additive pass is handed as black so that a ground drawn as several
        // passes fogs once rather than once per pass.
        float3 shown = lerp(push.normalKey2.xyz, base, input.fog);
        if (cLinearScene != 0)
        {
            // The lightmap scale and the inverse exposure, together: see ForwardPushBlock.
            return float4(DecodeSrgb(max(shown, float3(0.0, 0.0, 0.0))) * push.markerExposure.y, alpha);
        }

        return float4(shown, alpha);
    }

    // The rig is lit, and light falls on albedo, which is linear: over the scene's linear image the
    // composited colour is decoded first, as the G-buffer's is.
    if (cLinearScene != 0)
    {
        base = DecodeSrgb(max(base, float3(0.0, 0.0, 0.0)));
    }

    // Camera-relative key and fill, the same directions the OpenGL backend uses. View space here
    // has +Z running into the scene, so a negative Z points back toward the eye.
    const float3 keyDirection = normalize(float3(-0.35, 0.45, -0.82));
    const float3 fillDirection = normalize(float3(0.55, -0.20, -0.55));

    float3 normal = normalize(input.normal);

    // The rig sits at the bottom of the screen a hand's length away; the view direction barely
    // varies over it, so the eye vector is taken as the view axis rather than carried per pixel.
    const float3 toEye = float3(0.0, 0.0, -1.0);

    // One material for the whole rig: partway between a glove and gun steel.
    const float roughness = 0.45;
    const float metallic = 0.35;

    float3 keyColour = float3(push.normalKey0.w, push.normalKey1.w, push.normalKey2.w);
    float3 ambientColour = float3(push.markerExposure.w, push.markerExposure.w, push.markerExposure.w);

    float3 diffuseColour = base * (1.0 - metallic);
    float3 specularColour = lerp(float3(0.04, 0.04, 0.04), base, metallic);

    float keyDot = max(dot(normal, keyDirection), 0.0);
    float fillDot = max(dot(normal, fillDirection), 0.0);
    float3 irradiance = ambientColour + keyColour * keyDot + keyColour * 0.25 * fillDot;

    float3 half0 = normalize(keyDirection + toEye);
    float gloss = exp2(1.0 + 11.0 * (1.0 - roughness));
    float specular = pow(max(dot(normal, half0), 0.0), gloss) * (gloss + 8.0) / 25.0;

    float rim = pow(1.0 - max(dot(normal, toEye), 0.0), 4.0);

    float3 lit = diffuseColour * irradiance
        + specularColour * keyColour * specular * keyDot
        + specularColour * keyColour * rim * 0.35 * (0.25 + metallic);

    return float4(lit * push.markerExposure.y, alpha);
}

#endif
