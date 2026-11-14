#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _539 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform ModePointPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 colour;
    vec4 state;
    vec4 target;
} push;

layout(location = 2) in vec2 input_diffuseUv;
layout(location = 3) in vec2 input_lightmapUv;
layout(location = 0) in vec3 input_position;
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;
layout(location = 1) out float entryPointParam_vertexMain_shape;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec2 _972 = input_diffuseUv;
    float _973 = input_lightmapUv.x;
    vec4 _42 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _42;
    float _45 = _42.y;
    float _582;
    if (_57)
    {
        _582 = -_45;
    }
    else
    {
        _582 = _45;
    }
    vec4 _1026 = clip;
    _1026.y = _582;
    _1026.z = (_1026.w - _1026.z) * 0.5;
    clip = _1026;
    gl_Position = _1026;
    entryPointParam_vertexMain_uv = _972;
    entryPointParam_vertexMain_shape = _973;
}

