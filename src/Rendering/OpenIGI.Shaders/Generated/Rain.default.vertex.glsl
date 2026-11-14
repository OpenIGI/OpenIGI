#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _58 = (cClipYDown != 0);

layout(binding = 1, std140) uniform RainPush_std140
{
    layout(row_major) mat4 modelViewProjection;
} push;

layout(location = 2) in vec2 input_diffuseUv;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 0) in vec3 input_position;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out vec4 entryPointParam_vertexMain_color;
layout(location = 2) out float entryPointParam_vertexMain_viewDepth;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec2 _372 = input_diffuseUv;
    vec4 _373 = input_colorBgra.zyxw;
    vec4 _42 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _42;
    float _46 = _42.y;
    float _317;
    if (_58)
    {
        _317 = -_46;
    }
    else
    {
        _317 = _46;
    }
    vec4 _380 = clip;
    _380.y = _317;
    _380.z = (_380.w - _380.z) * 0.5;
    clip = _380;
    gl_Position = _380;
    entryPointParam_vertexMain_diffuseUv = _372;
    entryPointParam_vertexMain_color = _373;
    entryPointParam_vertexMain_viewDepth = _380.w;
}

