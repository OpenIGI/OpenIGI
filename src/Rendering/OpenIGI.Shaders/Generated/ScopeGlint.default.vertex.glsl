#version 430
#ifdef GL_ARB_shader_draw_parameters
#extension GL_ARB_shader_draw_parameters : enable
#endif

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);

struct _Array_std140_vector_float_4_7
{
    vec4 data[7];
};

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;

void main()
{
    uint _13 = uint(gl_VertexID - SPIRV_Cross_BaseVertex);
    float _298 = float((_13 << uint(1)) & 2u);
    float _300 = float(_13 & 2u);
    vec2 _401 = vec2(_298, _300);
    float _304 = _300 * 2.0;
    float _310;
    if (_57)
    {
        _310 = _304 - 1.0;
    }
    else
    {
        _310 = 1.0 - _304;
    }
    gl_Position = vec4((_298 * 2.0) - 1.0, _310, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _401;
}

