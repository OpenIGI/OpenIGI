#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_1
#define SPIRV_CROSS_CONSTANT_ID_1 0.0
#endif
const float cSunX = SPIRV_CROSS_CONSTANT_ID_1;
#ifndef SPIRV_CROSS_CONSTANT_ID_2
#define SPIRV_CROSS_CONSTANT_ID_2 0.0
#endif
const float cSunY = SPIRV_CROSS_CONSTANT_ID_2;
#ifndef SPIRV_CROSS_CONSTANT_ID_3
#define SPIRV_CROSS_CONSTANT_ID_3 0.0
#endif
const float cSunZ = SPIRV_CROSS_CONSTANT_ID_3;
#ifndef SPIRV_CROSS_CONSTANT_ID_9
#define SPIRV_CROSS_CONSTANT_ID_9 0.0
#endif
const float cSunRed = SPIRV_CROSS_CONSTANT_ID_9;
#ifndef SPIRV_CROSS_CONSTANT_ID_10
#define SPIRV_CROSS_CONSTANT_ID_10 0.0
#endif
const float cSunGreen = SPIRV_CROSS_CONSTANT_ID_10;
#ifndef SPIRV_CROSS_CONSTANT_ID_11
#define SPIRV_CROSS_CONSTANT_ID_11 0.0
#endif
const float cSunBlue = SPIRV_CROSS_CONSTANT_ID_11;
#ifndef SPIRV_CROSS_CONSTANT_ID_6
#define SPIRV_CROSS_CONSTANT_ID_6 0.0
#endif
const float cAmbientRed = SPIRV_CROSS_CONSTANT_ID_6;
#ifndef SPIRV_CROSS_CONSTANT_ID_7
#define SPIRV_CROSS_CONSTANT_ID_7 0.0
#endif
const float cAmbientGreen = SPIRV_CROSS_CONSTANT_ID_7;
#ifndef SPIRV_CROSS_CONSTANT_ID_8
#define SPIRV_CROSS_CONSTANT_ID_8 0.0
#endif
const float cAmbientBlue = SPIRV_CROSS_CONSTANT_ID_8;
#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _171 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _243 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_12
#define SPIRV_CROSS_CONSTANT_ID_12 0
#endif
const int cLinearScene = SPIRV_CROSS_CONSTANT_ID_12;
const bool _290 = (cLinearScene != 0);

layout(binding = 0, std140) uniform WaterPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 eyeFacing;
    vec4 diffuseScale;
    vec4 specularFog;
    vec4 fogNormalScale;
} push;

layout(location = 1) in vec3 input_normal;
layout(location = 0) in vec3 input_position;
layout(location = 2) in vec2 input_shallowness;
layout(location = 0) out vec4 entryPointParam_vertexMain_colour0;
layout(location = 1) out vec3 entryPointParam_vertexMain_colour1;
layout(location = 2) out vec3 entryPointParam_vertexMain_reflection;
layout(location = 3) out float entryPointParam_vertexMain_fog;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 sun = vec3(cSunX, cSunY, cSunZ);
    vec3 sunColour = vec3(cSunRed, cSunGreen, cSunBlue);
    vec3 normal = input_normal * push.fogNormalScale.w;
    vec3 _47 = normalize(push.eyeFacing.xyz - input_position);
    float _64 = max(dot(_47, normal), push.eyeFacing.w);
    vec3 _379 = ((normal * (_64 * 2.0)) - _47).xzy;
    float grazing = 1.0 - _64;
    float grazing2 = grazing * grazing;
    float _79 = ((grazing2 * grazing2) * grazing) * 0.979632794857025146484375;
    float fresnel = _79 + 0.02036719955503940582275390625;
    float _89 = 0.979632794857025146484375 - _79;
    vec4 _377 = clamp(vec4(fresnel, fresnel, fresnel, _89 * input_shallowness.x), vec4(0.0), vec4(1.0));
    float _105 = dot(normalize(_47 + sun), normal);
    float glint2 = _105 * _105;
    float glint8 = glint2 * glint2;
    vec3 _378 = clamp(((((push.specularFog.xyz * ((glint8 * glint8) * _105)) * sunColour) * fresnel) + ((((sunColour * dot(normal, sun)) * push.diffuseScale.xyz) + vec3(cAmbientRed, cAmbientGreen, cAmbientBlue)) * _89)) * (1.0 - input_shallowness.x), vec3(0.0), vec3(1.0));
    vec4 clip = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    float _14;
    if (push.specularFog.w > 0.0)
    {
        _14 = clamp(exp2((-push.specularFog.w) * max(clip.w, 0.0)), 0.0, 1.0);
    }
    else
    {
        _14 = 1.0;
    }
    float _380 = _14;
    float _335;
    if (_171)
    {
        _335 = -clip.y;
    }
    else
    {
        _335 = clip.y;
    }
    vec4 _394 = clip;
    _394.y = _335;
    _394.z = (_394.w - _394.z) * 0.5;
    clip = _394;
    gl_Position = _394;
    entryPointParam_vertexMain_colour0 = _377;
    entryPointParam_vertexMain_colour1 = _378;
    entryPointParam_vertexMain_reflection = _379;
    entryPointParam_vertexMain_fog = _380;
}

