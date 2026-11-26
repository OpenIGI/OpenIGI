#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _98 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _204 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _287 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform InfraredPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 viewRow0;
    vec4 viewRow1;
    vec4 viewRow2;
    vec4 control;
} push;

layout(binding = 0) uniform sampler2D _398;
layout(binding = 1) uniform sampler2D _400;

layout(location = 0) in float input_ramp;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    {
        uint width = uvec2(textureSize(_398, 0)).x;
        float u;
        if (input_ramp <= 1.0)
        {
            u = input_ramp;
        }
        else
        {
            u = 2.0 - input_ramp;
        }
        float edge = 0.5 / max(float(width), 1.0);
        vec4 sampled = textureLod(_400, vec2(clamp(u, edge, 1.0 - edge), 0.0), 0.0);
        float _187 = clamp(sampled.w * push.control.y, 0.0, 1.0);
        if (push.control.w != 0.0)
        {
            vec3 _192 = sampled.xyz;
            vec3 _316;
            if (_204)
            {
                _316 = _192;
            }
            else
            {
                _316 = mix(pow(max((_192 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _192 * vec3(0.077399380505084991455078125), step(_192, vec3(0.040449999272823333740234375)));
            }
            entryPointParam_fragmentMain = vec4(_316 * push.control.z, _187);
        }
        else
        {
            vec3 _242 = sampled.xyz;
            vec3 _336;
            if (_204)
            {
                _336 = mix((pow(max(_242, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _242 * 12.9200000762939453125, step(_242, vec3(0.003130800090730190277099609375)));
            }
            else
            {
                _336 = _242;
            }
            vec4 _276 = vec4(_336, _187);
            vec4 _354;
            if (_287)
            {
                vec3 _358 = _276.xyz;
                _354 = vec4(mix(pow(max((_358 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _358 * vec3(0.077399380505084991455078125), step(_358, vec3(0.040449999272823333740234375))), _187);
            }
            else
            {
                _354 = _276;
            }
            entryPointParam_fragmentMain = _354;
        }
    }
}

