#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _58 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _175 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform TracerPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 style;
} push;

layout(location = 2) in vec2 input_diffuseUv;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 0) in vec3 input_position;
layout(location = 0) out vec2 entryPointParam_vertexMain_strip;
layout(location = 1) out vec4 entryPointParam_vertexMain_color;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec2 _476 = input_diffuseUv;
    vec4 _477 = input_colorBgra.zyxw;
    vec4 _42 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _42;
    float _46 = _42.y;
    float _303;
    if (_58)
    {
        _303 = -_46;
    }
    else
    {
        _303 = _46;
    }
    vec4 _499 = clip;
    _499.y = _303;
    _499.z = (_499.w - _499.z) * 0.5;
    clip = _499;
    gl_Position = _499;
    entryPointParam_vertexMain_strip = _476;
    entryPointParam_vertexMain_color = _477;
}

