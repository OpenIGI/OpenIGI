#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _89 = (cClipYDown != 0);

layout(binding = 0, std140) uniform GlassPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 glass;
} push;

layout(location = 0) in vec3 input_position;
layout(location = 1) in vec3 input_normal;
layout(location = 2) in vec2 input_diffuseUv;
layout(location = 3) in vec2 input_lightmapUv;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out vec2 entryPointParam_vertexMain_lightmapUv;
layout(location = 2) out vec4 entryPointParam_vertexMain_color;
layout(location = 3) out vec3 entryPointParam_vertexMain_worldNormal;
layout(location = 4) out float entryPointParam_vertexMain_viewDepth;
layout(location = 5) out vec3 entryPointParam_vertexMain_fromEye;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 _391 = input_normal;
    vec2 _354 = input_diffuseUv;
    vec2 _355 = input_lightmapUv;
    vec4 _356 = input_colorBgra.zyxw;
    vec4 _285 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 _267 = _285;
    float _287 = _285.y;
    float _345;
    if (_89)
    {
        _345 = -_287;
    }
    else
    {
        _345 = _287;
    }
    vec4 _401 = _267;
    _401.y = _345;
    _401.z = (_401.w - _401.z) * 0.5;
    _267 = _401;
    gl_Position = _401;
    entryPointParam_vertexMain_diffuseUv = _354;
    entryPointParam_vertexMain_lightmapUv = _355;
    entryPointParam_vertexMain_color = _356;
    entryPointParam_vertexMain_worldNormal = ((push.normal0.xyz * _391.x) + (push.normal1.xyz * _391.y)) + (push.normal2.xyz * _391.z);
    entryPointParam_vertexMain_viewDepth = _401.w;
    entryPointParam_vertexMain_fromEye = (((push.normal0.xyz * input_position.x) + (push.normal1.xyz * input_position.y)) + (push.normal2.xyz * input_position.z)) + vec3(push.normal0.w, push.normal1.w, push.normal2.w);
}

