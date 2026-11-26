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

layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 1) in vec3 input_normal;
layout(location = 6) in vec3 input_previousNormal;
layout(location = 0) out vec2 entryPointParam_vertexMain_surface;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 localPosition = input_position + ((input_previousPosition - input_position) * push.control.x);
    vec3 localNormal = input_normal + ((input_previousNormal - input_normal) * push.control.x);
    float _53 = length(push.depthRow.xyz) * length(localNormal);
    float facing;
    if (_53 > 9.9999999600419720025001879548654e-13)
    {
        facing = abs(dot(push.depthRow.xyz, localNormal)) / _53;
    }
    else
    {
        facing = 1.0;
    }
    vec2 _166 = vec2(facing, dot(push.depthRow.xyz, localPosition) + push.depthRow.w);
    vec4 _84 = vec4(localPosition, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _84;
    float _87 = _84.y;
    float _157;
    if (_98)
    {
        _157 = -_87;
    }
    else
    {
        _157 = _87;
    }
    vec4 _170 = clip;
    _170.y = _157;
    _170.z = (_170.w - _170.z) * 0.5;
    clip = _170;
    gl_Position = _170;
    entryPointParam_vertexMain_surface = _166;
}

