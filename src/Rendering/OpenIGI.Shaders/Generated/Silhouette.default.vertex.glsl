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
    float _516 = float((_13 << uint(1)) & 2u);
    float _518 = float(_13 & 2u);
    vec2 _790 = vec2(_516, _518);
    float _522 = _518 * 2.0;
    float _528;
    if (_57)
    {
        _528 = _522 - 1.0;
    }
    else
    {
        _528 = 1.0 - _522;
    }
    gl_Position = vec4((_516 * 2.0) - 1.0, _528, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _790;
}

