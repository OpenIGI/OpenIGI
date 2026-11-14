#version 430
#ifdef GL_ARB_shader_draw_parameters
#extension GL_ARB_shader_draw_parameters : enable
#endif

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;

void main()
{
    uint _13 = uint(gl_VertexID - SPIRV_Cross_BaseVertex);
    float _812 = float((_13 << uint(1)) & 2u);
    float _814 = float(_13 & 2u);
    vec2 _1055 = vec2(_812, _814);
    float _818 = _814 * 2.0;
    float _824;
    if (_57)
    {
        _824 = _818 - 1.0;
    }
    else
    {
        _824 = 1.0 - _818;
    }
    gl_Position = vec4((_812 * 2.0) - 1.0, _824, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _1055;
}

