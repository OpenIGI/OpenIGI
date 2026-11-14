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
const bool _110 = (cTargetEncodes != 0);

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;

void main()
{
    uint _13 = uint(gl_VertexID - SPIRV_Cross_BaseVertex);
    float _155 = float((_13 << uint(1)) & 2u);
    float _157 = float(_13 & 2u);
    vec2 _200 = vec2(_155, _157);
    float _161 = _157 * 2.0;
    float _167;
    if (_57)
    {
        _167 = _161 - 1.0;
    }
    else
    {
        _167 = 1.0 - _161;
    }
    gl_Position = vec4((_155 * 2.0) - 1.0, _167, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _200;
}

