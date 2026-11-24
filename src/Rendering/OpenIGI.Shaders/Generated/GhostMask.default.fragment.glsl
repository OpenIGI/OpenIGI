#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _99 = (cClipYDown != 0);

layout(binding = 0, std140) uniform GhostMaskPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 depthRow;
    vec4 control;
} push;

layout(location = 0) in vec3 input_surface;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    float _151 = fract((input_surface.z * 6.0) - (push.control.y * 2.0));
    float sweepAt = fract((input_surface.z * 0.319999992847442626953125) - (push.control.y * 0.25)) - 0.5;
    entryPointParam_fragmentMain = vec4(pow(1.0 - clamp(input_surface.x, 0.0, 1.0), 2.400000095367431640625), max(exp((_151 * _151) * (-196.0)) * 0.550000011920928955078125, exp((sweepAt * sweepAt) * (-100.0))), 1.0 / (1.0 + max(input_surface.y, 0.0)), 1.0);
}

