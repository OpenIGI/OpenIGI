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
    float _326 = float((_13 << uint(1)) & 2u);
    float _328 = float(_13 & 2u);
    vec2 _347 = vec2(_326, _328);
    float _332 = _328 * 2.0;
    float _338;
    if (_57)
    {
        _338 = _332 - 1.0;
    }
    else
    {
        _338 = 1.0 - _332;
    }
    gl_Position = vec4((_326 * 2.0) - 1.0, _338, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _347;
}

