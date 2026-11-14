#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _45 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _139 = (cSamplerDecodes != 0);

layout(binding = 0, std140) uniform CardPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 material;
} push;

layout(location = 0) in vec3 input_position;
layout(location = 1) in vec3 input_normal;
layout(location = 2) in vec2 input_diffuseUv;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out vec4 entryPointParam_vertexMain_color;
layout(location = 2) out vec3 entryPointParam_vertexMain_modelNormal;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec4 _28 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _28;
    float _33 = _28.y;
    float _221;
    if (_45)
    {
        _221 = -_33;
    }
    else
    {
        _221 = _33;
    }
    vec4 _287 = clip;
    _287.y = _221;
    _287.z = (_287.z + _287.w) * 0.5;
    clip = _287;
    gl_Position = _287;
    entryPointParam_vertexMain_diffuseUv = input_diffuseUv;
    entryPointParam_vertexMain_color = input_colorBgra.zyxw;
    entryPointParam_vertexMain_modelNormal = input_normal;
}

