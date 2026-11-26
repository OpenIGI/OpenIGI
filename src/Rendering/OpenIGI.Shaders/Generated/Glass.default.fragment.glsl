#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _89 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _303 = (cSamplerDecodes != 0);

layout(binding = 0, std140) uniform GlassPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 glass;
} push;

layout(binding = 0) uniform sampler2D _604;
layout(binding = 1) uniform sampler2D _606;

layout(location = 0) in vec2 input_diffuseUv;
layout(location = 2) in vec4 input_color;
layout(location = 3) in vec3 input_worldNormal;
layout(location = 5) in vec3 input_fromEye;
layout(location = 1) in vec2 input_lightmapUv;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    {
        vec4 sampled = texture(_604, input_diffuseUv);
        float alpha = sampled.w * input_color.w;
        if (alpha >= 0.980392158031463623046875)
        {
            discard;
        }
        if (alpha < 0.0199999995529651641845703125)
        {
            discard;
        }
        float _238 = abs(dot(normalize(input_worldNormal), normalize(input_fromEye)));
        float _243 = sqrt(clamp(1.0 - (_238 * _238), 0.0, 1.0));
        float _487 = 0.959999978542327880859375 * pow(1.0 - clamp(_238, 0.0, 1.0), 5.0);
        float through = (1.0 - ((2.0 * (0.039999999105930328369140625 + _487)) / (_487 + 1.03999996185302734375))) * exp((-0.0199999995529651641845703125) / max(sqrt(clamp(1.0 - (0.444444477558135986328125 * (_243 * _243)), 0.0, 1.0)), 0.0500000007450580596923828125));
        if (push.glass.x > 0.5)
        {
            vec3 light;
            if (push.glass.z > 0.5)
            {
                vec4 sampled_1 = texture(_606, input_lightmapUv);
                vec3 _292 = sampled_1.xyz;
                vec3 _492;
                if (_303)
                {
                    _492 = mix((pow(max(_292, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _292 * 12.9200000762939453125, step(_292, vec3(0.003130800090730190277099609375)));
                }
                else
                {
                    _492 = _292;
                }
                light = _492;
            }
            else
            {
                light = vec3(push.glass.w, push.glass.w, push.glass.w);
            }
            vec3 _343 = sampled.xyz;
            vec3 _510;
            if (_303)
            {
                _510 = mix((pow(max(_343, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _343 * 12.9200000762939453125, step(_343, vec3(0.003130800090730190277099609375)));
            }
            else
            {
                _510 = _343;
            }
            vec3 _350 = max((_510 * light) * input_color.xyz, vec3(0.0));
            entryPointParam_fragmentMain = vec4((mix(pow(max((_350 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _350 * vec3(0.077399380505084991455078125), step(_350, vec3(0.040449999272823333740234375))) * push.glass.y) * alpha, 1.0 - (through * (1.0 - alpha)));
        }
        else
        {
            entryPointParam_fragmentMain = vec4(0.0, 0.0, 0.0, 1.0 - through);
        }
    }
}

