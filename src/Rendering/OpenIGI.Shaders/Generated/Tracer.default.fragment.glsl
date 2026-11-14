#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _58 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _175 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform TracerPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 style;
} push;

layout(location = 0) in vec2 input_strip;
layout(location = 1) in vec4 input_color;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 _95;
    if (push.style.x < 0.5)
    {
        vec4 _484 = input_color;
        vec4 _312;
        {
            float _319 = clamp(_484.w, 0.0, 1.0);
            if (push.style.w != 0.0)
            {
                _312 = vec4(mix(pow(max((_484.xyz + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _484.xyz * vec3(0.077399380505084991455078125), step(_484.xyz, vec3(0.040449999272823333740234375))) * push.style.z, _319);
            }
            else
            {
                vec4 _333 = vec4(_484.xyz, _319);
                vec4 _350;
                if (_175)
                {
                    vec3 _354 = _333.xyz;
                    _350 = vec4(mix(pow(max((_354 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _354 * vec3(0.077399380505084991455078125), step(_354, vec3(0.040449999272823333740234375))), _319);
                }
                else
                {
                    _350 = _333;
                }
                _312 = _350;
            }
        }
        _95 = _312;
    }
    else
    {
        vec2 _486 = input_strip;
        vec4 _487 = input_color;
        vec4 _374;
        {
            float _382 = (_486.y * 2.0) - 1.0;
            float _383 = _382 * _382;
            float _386 = exp(_383 * (-9.0));
            float _390 = exp(_383 * (-3.0)) * (1.0 - _383);
            float _394 = clamp(_487.w, 0.0, 1.0);
            float _395 = _394 * _394;
            vec3 _438 = mix(pow(max((_487.xyz + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _487.xyz * vec3(0.077399380505084991455078125), step(_487.xyz, vec3(0.040449999272823333740234375)));
            vec3 _407 = ((((_438 * _390) * 0.3499999940395355224609375) + (mix(_438, vec3(1.0, 0.959999978542327880859375, 0.86000001430511474609375), vec3(0.3499999940395355224609375)) * _386)) * _395) * push.style.y;
            float _411 = clamp(((_390 * 0.4000000059604644775390625) + _386) * _395, 0.0, 1.0);
            if (push.style.w != 0.0)
            {
                _374 = vec4(_407 * push.style.z, _411);
            }
            else
            {
                vec3 _441 = clamp(_407, vec3(0.0), vec3(1.0));
                vec4 _423 = vec4(mix((pow(max(_441, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _441 * 12.9200000762939453125, step(_441, vec3(0.003130800090730190277099609375))), _411);
                vec4 _452;
                if (_175)
                {
                    vec3 _456 = _423.xyz;
                    _452 = vec4(mix(pow(max((_456 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _456 * vec3(0.077399380505084991455078125), step(_456, vec3(0.040449999272823333740234375))), _411);
                }
                else
                {
                    _452 = _423;
                }
                _374 = _452;
            }
        }
        _95 = _374;
    }
    entryPointParam_fragmentMain = _95;
}

