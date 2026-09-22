// The vertex stage both glazing passes share, and the sets they both bind.
//
// The transmission draw and the front-layer draw are two fragment stages over one vertex stage and
// one pair of descriptor sets, so a pane goes through the same alpha cut and arrives with the same
// world normal in each. They are two modules rather than one because a module names one entry point
// per stage; the shared half lives here so the transform exists once.

#ifndef OPENIGI_GLASS_VERTEX_INCLUDED
#define OPENIGI_GLASS_VERTEX_INCLUDED

#include "PushConstants.slang"
#include "ClipSpace.slang"

struct GlassPush
{
    float4x4 modelViewProjection;

    // The OBJECT-TO-WORLD rotation, by column, exactly as the G-buffer pass pushes and reads it.
    // Composed on the processor as the view-to-world rotation times the model-view own rotation,
    // which costs nothing per vertex and saves a second matrix here.
    float4 normal0;
    float4 normal1;
    float4 normal2;

    // spare.
    float4 glass;
};

OPENIGI_PUSH_CONSTANT(GlassPush, push);

// The same set the world and shadow pipelines bind, so a texture pairing built for one is bindable
// here without being rebuilt. The detail map is declared and unread, for the reason it is declared
// in those: the set is shared, so all of them must agree about its shape.
[[vk::binding(0, 0)]] Texture2D<float4> uDiffuse;
[[vk::binding(1, 0)]] SamplerState uDiffuseSampler;
[[vk::binding(2, 0)]] Texture2D<float4> uLightmap;
[[vk::binding(3, 0)]] SamplerState uLightmapSampler;
[[vk::binding(4, 0)]] Texture2D<float4> uDetail;
[[vk::binding(5, 0)]] SamplerState uDetailSampler;

// The scene's depth, read by texel and never sampled, so it needs no sampler of its own. The
// transmission draw declares it unread because the set is shared with the front-layer pass.
[[vk::binding(0, 1)]] Texture2D<float> uSceneDepth;

struct GlassParams
{
    // the frame's fog colour, and its density per metre of view depth.
    float4 fog;
};

[[vk::binding(1, 1)]] ConstantBuffer<GlassParams> p;

struct GlassVertexInput
{
    [[vk::location(0)]] float3 position;
    [[vk::location(1)]] float3 normal;
    [[vk::location(2)]] float2 diffuseUv;
    [[vk::location(3)]] float2 lightmapUv;
    [[vk::location(4)]] float4 colorBgra;

    // Declared because the vertex format the world binds has them, and a pipeline's attributes must
    // describe the buffer it is fed. Glazing does not move, so neither is read.
    [[vk::location(5)]] float3 previousPosition;
    [[vk::location(6)]] float3 previousNormal;
};

struct GlassVaryings
{
    float4 position : SV_Position;
    [[vk::location(0)]] float2 diffuseUv;
    [[vk::location(1)]] float2 lightmapUv;
    [[vk::location(2)]] float4 color;
    [[vk::location(3)]] float3 worldNormal;
    [[vk::location(4)]] float viewDepth;
};

/// A pane to clip space, with its normal in world axes and its own distance carried on.
GlassVaryings GlassTransform(GlassVertexInput input)
{
    GlassVaryings output;

    output.diffuseUv = input.diffuseUv;
    output.lightmapUv = input.lightmapUv;

    // The vertex colour arrives BGRA, as the original's packed word does.
    output.color = input.colorBgra.bgra;

    float4 clip = mul(push.modelViewProjection, float4(input.position, 1.0));

    // The engine's projection is an OpenGL one, so the same correction the world stage applies,
    // character for character: Y flipped for Vulkan's y-down framebuffer, and Z remapped to REVERSE-Z
    // in [0, 1] -- w minus z, not w plus z, so that nearer geometry takes the greater value. Getting
    // that subtraction backwards does not tilt the glazing slightly; it puts every pane at the wrong
    // end of the depth range, where the pipeline's greater-or-equal test rejects all of it. The
    // winding the Y flip reverses is compensated by the front face.
    clip.y = OpenIgiClipY(clip.y);
    clip.z = (clip.w - clip.z) * 0.5;
    output.position = clip;

    // How far away the pane is. With an OpenGL projection the clip w IS the view depth, so there is
    // nothing to undo; the reflection pass rebuilds the full position from this and the pixel's own
    // direction, which is why nothing else about the position has to travel.
    output.viewDepth = clip.w;

    // Straight to WORLD space, the same way the G-buffer pass does it. The normal that arrives is in
    // MODEL space, and handing that to a view-to-world rotation -- which is what this stage did at
    // first -- produces a normal pointing somewhere arbitrary. It does not tilt the reflection
    // slightly; it reflects the room about the wrong plane, so the floor comes out at the top of the
    // pane.
    //
    // Written as the three scaled columns summed rather than as a matrix: a matrix constructor fills
    // columns in GLSL and rows in Slang, and the GLSL this replaces multiplied by columns.
    output.worldNormal = (push.normal0.xyz * input.normal.x)
        + (push.normal1.xyz * input.normal.y)
        + (push.normal2.xyz * input.normal.z);

    return output;
}

#endif
