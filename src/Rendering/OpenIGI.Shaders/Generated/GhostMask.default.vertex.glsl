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

layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 1) in vec3 input_normal;
layout(location = 6) in vec3 input_previousNormal;
layout(location = 0) out vec3 entryPointParam_vertexMain_surface;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 localPosition = input_position + ((input_previousPosition - input_position) * push.control.x);
    vec3 localNormal = input_normal + ((input_previousNormal - input_normal) * push.control.x);
    float _49 = length(push.depthRow.xyz);
    float _52 = _49 * length(localNormal);
    float facing;
    if (_52 > 9.9999999600419720025001879548654e-13)
    {
        facing = abs(dot(push.depthRow.xyz, localNormal)) / _52;
    }
    else
    {
        facing = 1.0;
    }
    vec3 _190 = vec3(facing, dot(push.depthRow.xyz, localPosition) + push.depthRow.w, localPosition.z * _49);
    vec4 _85 = vec4(localPosition, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _85;
    float _88 = _85.y;
    float _181;
    if (_99)
    {
        _181 = -_88;
    }
    else
    {
        _181 = _88;
    }
    vec4 _200 = clip;
    _200.y = _181;
    _200.z = (_200.w - _200.z) * 0.5;
    clip = _200;
    gl_Position = _200;
    entryPointParam_vertexMain_surface = _190;
}

