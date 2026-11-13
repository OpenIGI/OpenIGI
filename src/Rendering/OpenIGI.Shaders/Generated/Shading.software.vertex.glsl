#version 430
#ifdef GL_ARB_shader_draw_parameters
#extension GL_ARB_shader_draw_parameters : enable
#endif

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);

struct _Array_std140_matrix_float_4_4_4
{
    mat4 data[4];
};

struct FlashlightParams_std140
{
    vec4 position;
    vec4 axis;
    vec4 up;
    vec4 colour;
    vec4 cone;
    vec4 control;
};

struct _Array_std140_vector_float_4_160
{
    vec4 data[160];
};

uvec3 _13735;
float _13736;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;

void main()
{
    uint _13 = uint(gl_VertexID - SPIRV_Cross_BaseVertex);
    float _5582 = float((_13 << uint(1)) & 2u);
    float _5584 = float(_13 & 2u);
    vec2 _12728 = vec2(_5582, _5584);
    float _5588 = _5584 * 2.0;
    float _5594;
    if (_57)
    {
        _5594 = _5588 - 1.0;
    }
    else
    {
        _5594 = 1.0 - _5588;
    }
    gl_Position = vec4((_5582 * 2.0) - 1.0, _5594, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _12728;
}

