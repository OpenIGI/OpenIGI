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
const bool _1108 = (cTargetEncodes != 0);

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec2 entryPointParam_vertexMain_uv;

void main()
{
    uint _13 = uint(gl_VertexID - SPIRV_Cross_BaseVertex);
    float _1179 = float((_13 << uint(1)) & 2u);
    float _1181 = float(_13 & 2u);
    vec2 _2305 = vec2(_1179, _1181);
    float _1185 = _1181 * 2.0;
    float _1191;
    if (_57)
    {
        _1191 = _1185 - 1.0;
    }
    else
    {
        _1191 = 1.0 - _1185;
    }
    gl_Position = vec4((_1179 * 2.0) - 1.0, _1191, 0.0, 1.0);
    entryPointParam_vertexMain_uv = _2305;
}

