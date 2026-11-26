#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _98 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _204 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _287 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform InfraredPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 viewRow0;
    vec4 viewRow1;
    vec4 viewRow2;
    vec4 control;
} push;

layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 1) in vec3 input_normal;
layout(location = 6) in vec3 input_previousNormal;
layout(location = 0) out float entryPointParam_vertexMain_ramp;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 localNormal = input_normal + ((input_previousNormal - input_normal) * push.control.x);
    float _59 = dot(push.viewRow2.xyz, localNormal);
    vec3 viewNormal = vec3(dot(push.viewRow0.xyz, localNormal), dot(push.viewRow1.xyz, localNormal), _59);
    float _61 = dot(viewNormal, viewNormal);
    float depth;
    if (_61 > 9.9999999600419720025001879548654e-13)
    {
        depth = _59 * inversesqrt(_61);
    }
    else
    {
        depth = 0.0;
    }
    float _378 = 1.0 + depth;
    vec4 _84 = vec4(input_position + ((input_previousPosition - input_position) * push.control.x), 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _84;
    float _87 = _84.y;
    float _308;
    if (_98)
    {
        _308 = -_87;
    }
    else
    {
        _308 = _87;
    }
    vec4 _389 = clip;
    _389.y = _308;
    _389.z = (_389.w - _389.z) * 0.5;
    clip = _389;
    gl_Position = _389;
    entryPointParam_vertexMain_ramp = _378;
}

