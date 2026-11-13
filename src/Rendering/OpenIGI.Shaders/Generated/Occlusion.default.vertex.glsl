#version 430
#ifdef GL_ARB_shader_draw_parameters
#extension GL_ARB_shader_draw_parameters : enable
#endif

uniform int _27_vert[36] = int[](0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7);
uniform vec3 _40_vert[8] = vec3[](vec3(0.0), vec3(1.0, 0.0, 0.0), vec3(1.0, 1.0, 0.0), vec3(0.0, 1.0, 0.0), vec3(0.0, 0.0, 1.0), vec3(1.0, 0.0, 1.0), vec3(1.0), vec3(0.0, 1.0, 1.0));
#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _88 = (cClipYDown != 0);

layout(binding = 0, std140) uniform OcclusionPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 minimum;
    vec4 maximum;
} push;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec4 _72 = vec4(mix(push.minimum.xyz, push.maximum.xyz, _40_vert[_27_vert[uint(gl_VertexID - SPIRV_Cross_BaseVertex)]]), 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _72;
    float _76 = _72.y;
    float _115;
    if (_88)
    {
        _115 = -_76;
    }
    else
    {
        _115 = _76;
    }
    vec4 _123 = clip;
    _123.y = _115;
    _123.z = (_123.w - _123.z) * 0.5;
    clip = _123;
    gl_Position = _123;
}

