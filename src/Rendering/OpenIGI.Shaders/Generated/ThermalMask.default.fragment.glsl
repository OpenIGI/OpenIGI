#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _98 = (cClipYDown != 0);

layout(binding = 0, std140) uniform ThermalMaskPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 depthRow;
    vec4 control;
} push;

layout(location = 0) in vec2 input_surface;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    float _135 = clamp(push.control.y, 0.0, 1.0);
    entryPointParam_fragmentMain = vec4(_135 * (0.7200000286102294921875 + (0.2800000011920928955078125 * sqrt(clamp(input_surface.x, 0.0, 1.0)))), _135, 1.0 / (1.0 + max(input_surface.y, 0.0)), 1.0);
}

