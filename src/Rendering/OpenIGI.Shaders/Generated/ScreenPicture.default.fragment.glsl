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
const bool _141 = (cTargetEncodes != 0);

layout(binding = 0) uniform sampler2D _262;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec3 _204 = clamp(texelFetch(_262, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).xyz, vec3(0.0), vec3(1.0));
    vec4 _130 = vec4(mix((pow(max(_204, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _204 * 12.9200000762939453125, step(_204, vec3(0.003130800090730190277099609375))), 1.0);
    vec4 _215;
    if (_141)
    {
        vec3 _219 = _130.xyz;
        _215 = vec4(mix(pow(max((_219 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _219 * vec3(0.077399380505084991455078125), step(_219, vec3(0.040449999272823333740234375))), 1.0);
    }
    else
    {
        _215 = _130;
    }
    entryPointParam_fragmentMain = _215;
}

