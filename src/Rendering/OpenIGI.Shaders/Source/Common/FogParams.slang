// The parameter block and slice distribution both fog dispatches share.
//
// The two array sizes arrive as defines rather than as literals, because they are C# constants --
// LocalFogVolume.MaxPerLevel and DeferredLocalLight.Maximum -- and the host fills the block from
// those. FogArraySizeTests pins the manifest's defines to them, so the two cannot drift apart
// without a test saying so.
//
// The binding is a define too, because the two dispatches bind this block at different slots: the
// scattering pass has five resources before it and the integration two.

#ifndef OPENIGI_FOG_PARAMS_INCLUDED
#define OPENIGI_FOG_PARAMS_INCLUDED

#include "PushConstants.slang"
#include "Flashlight.slang"

#ifndef OPENIGI_MAX_FOG_VOLUMES
#error "OPENIGI_MAX_FOG_VOLUMES must be defined by the shader build."
#endif

#ifndef OPENIGI_MAX_LAMPS
#error "OPENIGI_MAX_LAMPS must be defined by the shader build."
#endif

#ifndef OPENIGI_FOG_PARAMS_BINDING
#error "OPENIGI_FOG_PARAMS_BINDING must be defined by the shader build."
#endif

struct FogParams
{
    // this frame's view rotation, camera-relative world.
    float4x4 viewToWorld;

    // the previous frame's inverse, for the history lookup.
    float4x4 prevWorldToView;

    // camera-relative world to each cascade's light clip.
    float4x4 cascade[4];

    // the view depth each cascade stops at.
    float4 cascadeSplits;

    // froxels across, down and deep; w the history weight.
    float4 grid;

    // xScale, yScale, near, far.
    float4 unproject;

    // the same, for the previous frame.
    float4 prevUnproject;

    // B, O, S of the slice distribution; w one over the slice count.
    float4 zParams;

    // density, 1/height falloff, base height from the camera, anisotropy.
    float4 medium;

    // uniform fraction, multiple scattering, sun and ambient intensity.
    float4 march;

    // the medium's scattering albedo; w the cascade count.
    float4 albedo;

    // view-space direction TOWARDS the sun; w one shadow texel.
    float4 sun;

    // the sun's colour already scaled; w the shadow's depth bias.
    float4 sunColour;

    // the flat ambient, for a frame with no sky harmonics; w says which.
    float4 ambient;

    // this camera minus the previous one; w the lamp count.
    float4 camera;

    // this frame's offset within the cell; w whether a history exists.
    float4 jitter;

    // where the camera stands in the world, for the roof map.
    float4 cameraWorld;

    // the roof map's world origin XY, texels per metre, its lowest Z.
    float4 shelter0;

    // metres per step, the headroom, the reach, whether there is a map.
    float4 shelter1;

    // texels across, texels down, _, _.
    float4 shelter2;

    // xyz camera-relative centre, w non-zero for a sphere.
    float4 volumeCentre[OPENIGI_MAX_FOG_VOLUMES];

    // xyz half-extent or radius, w the density multiplier.
    float4 volumeExtent[OPENIGI_MAX_FOG_VOLUMES];

    // xyz view-space position, w reach.
    float4 lampPosition[OPENIGI_MAX_LAMPS];

    // rgb colour, w intensity.
    float4 lampColour[OPENIGI_MAX_LAMPS];

    // the player's flashlight (FlashlightBlock), last so nothing before it moves; a range of nought is off.
    FlashlightParams flashlight;
};

[[vk::binding(OPENIGI_FOG_PARAMS_BINDING, 0)]] ConstantBuffer<FogParams> p;

static const float kPi = 3.14159265358979;

// The depth slices are spaced exponentially (Wronski 2014): thin near the eye, where fog detail is seen
// against nearby geometry, and growing with distance. In closed form a slice index s maps to the view
// depth z by s = S * log2(B z + O), with B, O and S chosen on the host so that slice 0 sits at the near
// depth and the last slice at the fog's reach. The shading pass finds a pixel's slice with the same form.
float SliceViewDepth(float slice)
{
    return (exp2(slice / p.zParams.z) - p.zParams.y) / p.zParams.x;
}

float ViewDepthSlice(float viewZ)
{
    return p.zParams.z * log2(max((p.zParams.x * viewZ) + p.zParams.y, 1e-6));
}

// Where a point on the screen at a given view depth sits in view space. The vertical is negated
// because the frame was rasterised through the vertex stage's Y flip, so a screen coordinate
// running down maps to a view axis running up.
float3 ViewPositionAt(float2 uv, float viewZ)
{
    float2 ndc = (uv * 2.0) - 1.0;
    return float3(
        ndc.x * viewZ / p.unproject.x,
        -ndc.y * viewZ / p.unproject.y,
        viewZ);
}

#endif
