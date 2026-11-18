#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _89 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _303 = (cSamplerDecodes != 0);

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
    vec3 _578 = input_normal;
    vec2 _541 = input_diffuseUv;
    vec2 _542 = input_lightmapUv;
    vec4 _543 = input_colorBgra.zyxw;
    vec4 _414 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 _396 = _414;
    float _416 = _414.y;
    float _474;
    if (_89)
    {
        _474 = -_416;
    }
    else
    {
        _474 = _416;
    }
    vec4 _599 = _396;
    _599.y = _474;
    _599.z = (_599.w - _599.z) * 0.5;
    _396 = _599;
    gl_Position = _599;
    entryPointParam_vertexMain_diffuseUv = _541;
    entryPointParam_vertexMain_lightmapUv = _542;
    entryPointParam_vertexMain_color = _543;
    entryPointParam_vertexMain_worldNormal = ((push.normal0.xyz * _578.x) + (push.normal1.xyz * _578.y)) + (push.normal2.xyz * _578.z);
    entryPointParam_vertexMain_viewDepth = _599.w;
    entryPointParam_vertexMain_fromEye = (((push.normal0.xyz * input_position.x) + (push.normal1.xyz * input_position.y)) + (push.normal2.xyz * input_position.z)) + vec3(push.normal0.w, push.normal1.w, push.normal2.w);
}

