#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _116 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_0
#define SPIRV_CROSS_CONSTANT_ID_0 0.501960813999176025390625
#endif
const float cAlphaReference = SPIRV_CROSS_CONSTANT_ID_0;

layout(binding = 0, std140) uniform ShadowPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 terrainRamp;
    vec4 terrainOffset;
} push;

layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 2) in vec2 input_diffuseUv;
layout(location = 1) in vec3 input_normal;
layout(location = 3) in vec2 input_lightmapUv;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out float entryPointParam_vertexMain_alpha;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 localPosition_1 = input_position + ((input_previousPosition - input_position) * push.terrainOffset.w);
    vec3 localPosition;
    vec2 diffuseUv;
    if (push.terrainRamp.w > 0.5)
    {
        vec3 target = (localPosition_1 + push.terrainOffset.xyz) + input_normal;
        float _80 = clamp(((max(abs(target.x), max(abs(target.y), abs(target.z))) * push.terrainRamp.x) - push.terrainRamp.y) * push.terrainRamp.z, 0.0, 1.0);
        localPosition = localPosition_1 + (input_normal * _80);
        diffuseUv = input_diffuseUv + (input_lightmapUv * _80);
    }
    else
    {
        localPosition = localPosition_1;
        diffuseUv = input_diffuseUv;
    }
    vec4 _102 = vec4(localPosition, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _102;
    float _105 = _102.y;
    float _197;
    if (_116)
    {
        _197 = -_105;
    }
    else
    {
        _197 = _105;
    }
    vec4 _212 = clip;
    _212.y = _197;
    _212.z = (_212.z + _212.w) * 0.5;
    clip = _212;
    vec4 _205 = _212;
    vec2 _206 = diffuseUv;
    float _19;
    if (push.terrainRamp.w > 0.5)
    {
        _19 = 1.0;
    }
    else
    {
        _19 = input_colorBgra.w;
    }
    gl_Position = _205;
    entryPointParam_vertexMain_diffuseUv = _206;
    entryPointParam_vertexMain_alpha = _19;
}

