#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _101 = (cClipYDown != 0);

layout(binding = 0, std140) uniform GhostMaskPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 rightRow;
    vec4 upRow;
    vec4 depthRow;
    vec4 control;
} push;

layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 1) in vec3 input_normal;
layout(location = 6) in vec3 input_previousNormal;
layout(location = 0) out vec3 entryPointParam_vertexMain_viewNormal;
layout(location = 1) out vec2 entryPointParam_vertexMain_surface;
layout(location = 2) out vec3 entryPointParam_vertexMain_bodyMetres;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 localPosition = input_position + ((input_previousPosition - input_position) * push.control.x);
    vec3 localNormal = input_normal + ((input_previousNormal - input_normal) * push.control.x);
    float _45 = length(push.depthRow.xyz);
    vec3 _488 = vec3(dot(push.rightRow.xyz, localNormal), dot(push.upRow.xyz, localNormal), dot(push.depthRow.xyz, localNormal));
    vec2 _489 = vec2(dot(push.depthRow.xyz, localPosition) + push.depthRow.w, localPosition.z * _45);
    vec3 _490 = localPosition * _45;
    vec4 _85 = vec4(localPosition, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _85;
    float _89 = _85.y;
    float _301;
    if (_101)
    {
        _301 = -_89;
    }
    else
    {
        _301 = _89;
    }
    vec4 _504 = clip;
    _504.y = _301;
    _504.z = (_504.w - _504.z) * 0.5;
    clip = _504;
    gl_Position = _504;
    entryPointParam_vertexMain_viewNormal = _488;
    entryPointParam_vertexMain_surface = _489;
    entryPointParam_vertexMain_bodyMetres = _490;
}

