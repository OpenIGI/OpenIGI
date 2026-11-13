#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _134 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _178 = (cTargetEncodes != 0);

layout(binding = 0) uniform sampler2D _324;

layout(location = 0) in vec2 input_texCoord;
layout(location = 1) in vec4 input_color;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 sampled = texture(_324, input_texCoord);
    vec3 _122 = sampled.xyz;
    vec3 _249;
    if (_134)
    {
        _249 = mix((pow(max(_122, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _122 * 12.9200000762939453125, step(_122, vec3(0.003130800090730190277099609375)));
    }
    else
    {
        _249 = _122;
    }
    vec4 _167 = vec4(_249, sampled.w) * input_color;
    vec4 _267;
    if (_178)
    {
        vec3 _271 = _167.xyz;
        _267 = vec4(mix(pow(max((_271 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _271 * vec3(0.077399380505084991455078125), step(_271, vec3(0.040449999272823333740234375))), _167.w);
    }
    else
    {
        _267 = _167;
    }
    entryPointParam_fragmentMain = _267;
}

