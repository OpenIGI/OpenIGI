#version 430

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

layout(binding = 0) uniform sampler2D _223;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 _99 = vec4(texelFetch(_223, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).xyz, 1.0);
    vec4 _175;
    if (_110)
    {
        vec3 _179 = _99.xyz;
        _175 = vec4(mix(pow(max((_179 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _179 * vec3(0.077399380505084991455078125), step(_179, vec3(0.040449999272823333740234375))), 1.0);
    }
    else
    {
        _175 = _99;
    }
    entryPointParam_fragmentMain = _175;
}

