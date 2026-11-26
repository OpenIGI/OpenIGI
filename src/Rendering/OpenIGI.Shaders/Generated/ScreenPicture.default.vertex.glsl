#version 430
#ifdef GL_ARB_shader_draw_parameters
#extension GL_ARB_shader_draw_parameters : enable
#endif

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _141 = (cTargetEncodes != 0);

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;

void main()
{
    uint _13 = uint(gl_VertexID - SPIRV_Cross_BaseVertex);
    float _181 = float((_13 << uint(1)) & 2u);
    float _183 = float(_13 & 2u);
    vec2 _239 = vec2(_181, _183);
    float _187 = _183 * 2.0;
    float _193;
    if (_57)
    {
        _193 = _187 - 1.0;
    }
    else
    {
        _193 = 1.0 - _187;
    }
    gl_Position = vec4((_181 * 2.0) - 1.0, _193, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _239;
}

