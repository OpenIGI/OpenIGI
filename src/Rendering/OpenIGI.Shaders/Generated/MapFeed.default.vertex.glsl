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
    float _707 = float((_13 << uint(1)) & 2u);
    float _709 = float(_13 & 2u);
    vec2 _909 = vec2(_707, _709);
    float _713 = _709 * 2.0;
    float _719;
    if (_57)
    {
        _719 = _713 - 1.0;
    }
    else
    {
        _719 = 1.0 - _713;
    }
    gl_Position = vec4((_707 * 2.0) - 1.0, _719, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _909;
}

